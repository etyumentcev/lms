"""Linux synthetic supervisor: user systemd cgroup + bubblewrap namespaces.

No fallback to an unisolated subprocess. Only trusted caller configuration enters
commands. systemd owns descendant cleanup independently of the Python caller.
"""
from dataclasses import dataclass, asdict
import hashlib
import json
import os
from pathlib import Path
import platform
import selectors
import subprocess
import time
import uuid
from .model import Denied


@dataclass(frozen=True)
class Runtime:
    node: Path
    katex: Path

    def fingerprint(self):
        if platform.system() != 'Linux' or platform.machine() != 'x86_64': raise Denied('I_PLATFORM','Failed')
        if not self.node.is_absolute() or not self.katex.is_absolute(): raise Denied('I_DEPENDENCY','Failed')
        if json.loads((self.katex/'package.json').read_text())['version'] != '0.16.47': raise Denied('I_DEPENDENCY','Failed')
        version=subprocess.run([str(self.node),'--version'],capture_output=True,timeout=3,env={},check=True).stdout.strip()
        if version != b'v24.19.0': raise Denied('I_DEPENDENCY','Failed')
        h=hashlib.sha256()
        # Trusted runtime installation is immutable for the duration of a call.
        # Fingerprint pins binaries, application code and KaTeX; distro library
        # supply-chain attestation is explicitly outside this component.
        for label,p in [('python',Path('/usr/bin/python3').resolve()),('node',self.node),('bwrap',Path('/usr/bin/bwrap'))]:
            h.update(label.encode()); h.update(hashlib.sha256(p.read_bytes()).digest())
        for base in [Path(__file__).parent,Path(__file__).parent.parent/'content_compiler',self.katex]:
            for p in sorted(base.rglob('*')):
                if p.is_file() and '__pycache__' not in p.parts:
                    h.update((base.name+'/'+str(p.relative_to(base))).encode()); h.update(hashlib.sha256(p.read_bytes()).digest())
        return h.hexdigest()


def control(*args):
    return subprocess.run(['/usr/bin/systemctl','--user',*args],stdin=subprocess.DEVNULL,
                          stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,timeout=3,check=False)


def pid_identity(pid):
    try: return Path(f'/proc/{pid}/stat').read_text().rsplit(')',1)[1].split()[19]
    except (OSError,IndexError): return None


def run_worker(snapshot, runtime, limits, revision, cancel, *, _probe=None):
    """_probe is trusted test-only module bytes, never archive/user input."""
    unit='b07c1-'+uuid.uuid4().hex+'.service'
    root=Path(__file__).resolve().parent.parent
    bwrap=['/usr/bin/bwrap','--unshare-all','--unshare-user','--disable-userns','--die-with-parent','--new-session',
           '--cap-drop','ALL','--ro-bind','/usr','/usr','--symlink','usr/lib','/lib','--symlink','usr/lib64','/lib64',
           '--proc','/proc','--dev','/dev','--dir','/code','--dir','/code/tools',
           '--ro-bind',str(root/'content_compiler'),'/code/tools/content_compiler',
           '--ro-bind',str(root/'content_import'),'/code/tools/content_import',
           '--ro-bind',str(runtime.node),'/runtime/node','--ro-bind',str(runtime.katex),'/runtime/modules/katex',
           '--ro-bind',str(snapshot),'/input.zip','--size',str(limits.expanded_bytes+limits.output_bytes+4_194_304),
           '--tmpfs','/work','--chdir','/code','--clearenv','--setenv','PATH','/usr/bin',
           '--setenv','PYTHONDONTWRITEBYTECODE','1','--setenv','PYTHONPATH','/code',
           '--setenv','SAPIENTUM_CONTENT_NODE','/runtime/node','--setenv','NODE_PATH','/runtime/modules']
    if _probe is not None:
        bwrap += ['--ro-bind',str(_probe),'/probe.py']
        command=['/usr/bin/python3','-B','/probe.py']
    else:
        command=['/usr/bin/python3','-B','-m','tools.content_import.worker',json.dumps(asdict(limits)),revision]
    props=[f'MemoryMax={limits.memory_bytes}','MemorySwapMax=0',f'TasksMax={limits.tasks}',
           'CPUQuota=100%',f'LimitCPU={limits.cpu_seconds}', 'LimitCORE=0',
           f'RuntimeMaxSec={limits.wall_seconds}','TimeoutStopSec=1','KillMode=control-group',
           'KillSignal=SIGKILL','SendSIGKILL=yes','OOMPolicy=kill',
           'MemoryAccounting=yes','TasksAccounting=yes']
    cmd=['/usr/bin/systemd-run','--user','--quiet','--expand-environment=no','--pipe','--wait','--unit='+unit,'--service-type=exec']
    for prop in props: cmd += ['-p',prop]
    cmd += bwrap+['--']+command
    started=time.monotonic(); reason=None; output=bytearray(); identities={}; cg=None; cpu=0; memory=0; events={}; effective={}
    p=subprocess.Popen(cmd,stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,env={k:v for k,v in os.environ.items() if k in ('XDG_RUNTIME_DIR','DBUS_SESSION_BUS_ADDRESS','HOME','PATH')})
    selector=selectors.DefaultSelector();selector.register(p.stdout,selectors.EVENT_READ)
    limit=limits.output_bytes*2+limits.entries*2048+1_048_576
    try:
        while True:
            if cg is None and p.poll() is None:
                name=control('show',unit,'--property=ControlGroup','--value').stdout.decode().strip()
                if name.startswith('/user.slice/'):
                    cg=Path('/sys/fs/cgroup')/name.lstrip('/')
            if cg is not None and cg.exists():
                try:
                    if not effective:
                        effective={f:(cg/f).read_text().strip() for f in ('memory.max','memory.swap.max','pids.max','cpu.max')}
                        if effective['memory.max'] != str(limits.memory_bytes) or effective['memory.swap.max'] != '0' or effective['pids.max'] != str(limits.tasks):
                            reason='I_ISOLATION_UNPROVEN'
                    for pid in (cg/'cgroup.procs').read_text().split():
                        identity=pid_identity(pid)
                        if identity: identities[pid]=identity
                    cpu=int(dict(line.split() for line in (cg/'cpu.stat').read_text().splitlines())['usage_usec'])
                    memory=max(memory,int((cg/'memory.peak').read_text()))
                    for f in ('memory.events','pids.events'):
                        events[f]=dict(line.split() for line in (cg/f).read_text().splitlines())
                    if cpu >= limits.cpu_seconds*1_000_000: reason='I_CPU_LIMIT'
                except FileNotFoundError: pass
            if cancel is not None and cancel.is_set(): reason='I_CANCELLED'
            if time.monotonic()-started >= limits.wall_seconds and reason is None: reason='I_TIMEOUT'
            if reason:
                control('kill','--signal=KILL','--kill-whom=all',unit)
                control('stop',unit)
                break
            for key,_ in selector.select(0.02):
                block=os.read(key.fileobj.fileno(),65536)
                if block:
                    output.extend(block)
                    if len(output)>limit: reason='I_OUTPUT_LIMIT'
                else: selector.unregister(key.fileobj)
            if p.poll() is not None and not selector.get_map(): break
        # Capture manager result before collection; never return worker stderr.
        manager_result=control('show',unit,'--property=Result','--value').stdout.decode().strip()
        # stop kills even detached/session-changing descendants before any result.
        control('stop',unit)
        p.wait(timeout=5)
        empty=(cg is None or not cg.exists() or 'populated 0' in (cg/'cgroup.events').read_text())
        gone=all(pid_identity(pid)!=identity for pid,identity in identities.items())
        evidence={'unit':unit,'returncode':p.returncode,'elapsedSeconds':round(time.monotonic()-started,4),
                  'managerResult':manager_result,'cpuUsec':cpu,'memoryPeak':memory,'events':events,'effectiveLimits':effective,'observedPids':sorted(map(int,identities)),
                  'cgroupEmpty':empty,'observedProcessesGone':gone}
        if not empty or not gone: raise Denied('I_TERMINATION_UNPROVEN','Failed')
        if reason: return None,reason,evidence
        if p.returncode: return None,'I_WORKER_EXIT',evidence
        if cg is None: return None,'I_ISOLATION_UNPROVEN',evidence
        return bytes(output),None,evidence
    finally:
        selector.close(); p.stdout.close()
        control('kill','--signal=KILL','--kill-whom=all',unit)
        control('stop',unit)
        if p.poll() is None:
            p.kill();p.wait(timeout=5)
        control('reset-failed',unit)
