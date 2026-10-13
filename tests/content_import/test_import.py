"""Independent transport and isolated-worker synthetic acceptance tests."""
import base64
from dataclasses import replace,asdict
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import struct
import tempfile
import tarfile
import threading
import time
import unittest
from unittest.mock import patch
import warnings
import zipfile
import zlib
from tools.content_import.model import Limits,Denied
from tools.content_import.zip_adapter import extract
from tools.content_import.importer import import_zip,snapshot_zip
from tools.content_import.sandbox import Runtime,run_worker,pid_identity
from tools.content_import.verify import verify

ROOT=Path(__file__).resolve().parents[2]
FIXTURE=ROOT/'tests/content_compiler/fixtures/counting'
NODE=Path('/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node')
RUNTIME=Runtime(NODE,ROOT/'src/Lms.Web/node_modules/katex')
EVIDENCE=[]


def archive(entries,compression=zipfile.ZIP_DEFLATED):
    out=io.BytesIO()
    with warnings.catch_warnings(),zipfile.ZipFile(out,'w',compression=compression) as z:
        warnings.simplefilter('ignore',UserWarning)
        for name,data in entries:z.writestr(name,data)
    return out.getvalue()


def fixture():
    return [(str(p.relative_to(FIXTURE)),p.read_bytes()) for p in sorted(FIXTURE.rglob('*')) if p.is_file()]


class ZipTests(unittest.TestCase):
    def extract(self,data,limits=Limits()):
        with tempfile.TemporaryDirectory() as d:
            return extract(data,Path(d),limits)
    def deny(self,data,code=None,limits=Limits()):
        with self.assertRaises(Denied) as c:self.extract(data,limits)
        if code:self.assertEqual(c.exception.code,code)
    def test_full_zip_index(self):
        entries=fixture(); index=self.extract(archive(entries))
        self.assertEqual(index,[dict(path=n,sizeBytes=len(v),sha256=hashlib.sha256(v).hexdigest()) for n,v in entries])
    def test_stored_and_explicit_directory(self):
        self.assertEqual(self.extract(archive([('a/',b''),('a/b.txt',b'abc')],zipfile.ZIP_STORED))[0]['sizeBytes'],3)
    def test_invalid_and_trailing_format(self):
        for b in [b'',b'not zip',archive([])+b'junk']:
            with self.subTest(b=b[:10]):self.deny(b,'I_ZIP_FORMAT')
    def test_entry_count_and_boundary(self):
        self.assertEqual(len(self.extract(archive([('a',b''),('b',b'')]),replace(Limits(),entries=2))),2)
        self.deny(archive([('a',b''),('b',b'')]),'I_ENTRY_LIMIT',replace(Limits(),entries=1))
    def test_false_eocd_count(self):
        b=bytearray(archive([('a',b''),('b',b'')]))
        struct.pack_into('<HH',b,len(b)-14,1,1)
        self.deny(bytes(b),'I_ENTRY_LIMIT',replace(Limits(),entries=1))
    def test_zip_byte_limit(self):
        b=archive([('a',b'a')]);self.extract(b,replace(Limits(),zip_bytes=len(b)))
        self.deny(b,'I_ZIP_LIMIT',replace(Limits(),zip_bytes=len(b)-1))
    def test_bomb_and_expanded_boundary(self):
        b=archive([('a',b'a'*2000000)])
        self.assertLess(len(b),3000);self.deny(b,'I_EXPANDED_LIMIT',replace(Limits(),expanded_bytes=10000))
        self.extract(archive([('a',b'123'),('b',b'45')]),replace(Limits(),expanded_bytes=5))
        self.deny(archive([('a',b'123'),('b',b'456')]),'I_EXPANDED_LIMIT',replace(Limits(),expanded_bytes=5))
    def test_lying_expanded_size(self):
        b=bytearray(archive([('a',b'a'*10000)]));cd=b.index(b'PK\x01\x02')
        struct.pack_into('<I',b,22,1);struct.pack_into('<I',b,cd+24,1)
        self.deny(bytes(b))
    def test_forged_size_and_matching_truncated_crc(self):
        b=bytearray(archive([('a',b'a'*10000)]));cd=b.index(b'PK\x01\x02')
        struct.pack_into('<I',b,22,1);struct.pack_into('<I',b,cd+24,1)
        struct.pack_into('<I',b,14,zlib.crc32(b'a'));struct.pack_into('<I',b,cd+16,zlib.crc32(b'a'))
        # Standard ZipExtFile accepts the one-byte truncation; adapter must count
        # the actual raw stream and deny, even with attacker-adjusted CRC.
        with zipfile.ZipFile(io.BytesIO(b)) as z:self.assertEqual(z.read('a'),b'a')
        self.deny(bytes(b),'I_EXPANDED_LIMIT',replace(Limits(),expanded_bytes=100))
        self.deny(bytes(b),'I_ZIP_FORMAT')
    def test_paths(self):
        for name in ['../escape','/etc/passwd','a/../../escape','a/./b','a//b','C:/secret','a\\b','%2e%2e/secret','a?x','a#x','a\n']:
            with self.subTest(name=name): self.deny(archive([(name,b'x')]),'I_PATH')
    def test_duplicates_and_parent_collisions(self):
        for names in [('a','a'),('a','A'),('A/x','a/y'),('a','a/b'),('a/b','a')]:
            with self.subTest(names=names):self.deny(archive([(x,b'') for x in names]),'I_COLLISION')
    def test_symlink_special_types(self):
        for mode in [stat.S_IFLNK,stat.S_IFIFO,stat.S_IFSOCK,stat.S_IFCHR,stat.S_IFBLK]:
            info=zipfile.ZipInfo('evil');info.create_system=3;info.external_attr=(mode|0o600)<<16
            with self.subTest(mode=mode):self.deny(archive([(info,b'/outside')]),'I_ENTRY_TYPE')
    def test_hardlink_extension(self):
        info=zipfile.ZipInfo('hard');info.create_system=3;info.external_attr=(stat.S_IFREG|0o600)<<16
        # PKWARE Unix extra field with link metadata is rejected wholesale.
        info.extra=struct.pack('<HH',0x000d,12)+b'\0'*12
        self.deny(archive([(info,b'target')]),'I_ZIP_FEATURE')
    def test_encrypted_flag(self):
        b=bytearray(archive([('a',b'x')]));cd=b.index(b'PK\x01\x02')
        struct.pack_into('<H',b,6,1);struct.pack_into('<H',b,cd+8,1)
        self.deny(bytes(b),'I_ZIP_FEATURE')
    def test_nested_archives_extension_magic(self):
        for name,data in [('a.zip',b'fake'),('a.txt',archive([])),('a.txt',b'\x1f\x8bhello'),('a.txt',b'0'*257+b'ustar')]:
            with self.subTest(name=name,data=data[:10]):self.deny(archive([(name,data)]),'I_NESTED_ARCHIVE')
    def test_crc_corruption(self):
        b=bytearray(archive([('a',b'hello')],zipfile.ZIP_STORED));b[31]^=1;self.deny(bytes(b),'I_ZIP_FORMAT')

    def test_nested_sfx_zip_and_v7_tar(self):
        self.deny(archive([('a.txt',b'prefix'+archive([]))]),'I_NESTED_ARCHIVE')
        out=io.BytesIO()
        with tarfile.open(fileobj=out,mode='w',format=tarfile.USTAR_FORMAT) as t:
            info=tarfile.TarInfo('inside');info.size=1;t.addfile(info,io.BytesIO(b'x'))
        self.deny(archive([('a.txt',out.getvalue())]),'I_NESTED_ARCHIVE')
    def test_unsupported_compression(self):
        self.deny(archive([('a',b'x')],zipfile.ZIP_BZIP2),'I_ZIP_FEATURE')
    def test_streaming_data_descriptor(self):
        class Unseekable(io.BytesIO):
            def seek(self,*a): raise OSError('unseekable')
        out=Unseekable()
        with zipfile.ZipFile(out,'w',compression=zipfile.ZIP_DEFLATED) as z:z.writestr('a',b'hello')
        self.assertEqual(self.extract(out.getvalue())[0]['sizeBytes'],5)


class SnapshotTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name);self.source=self.root/'a.zip';self.source.write_bytes(b'zip snapshot')
    def tearDown(self):self.temp.cleanup()
    def capture(self,**kw):return snapshot_zip(self.source,self.root/'snapshot',Limits(),None,**kw)
    def test_snapshot_matches(self):self.assertEqual(self.capture(),hashlib.sha256(self.source.read_bytes()).hexdigest())
    def test_replacement_during_read(self):
        def change():
            new=self.root/'new';new.write_bytes(b'changed');new.replace(self.source)
        with self.assertRaises(Denied) as c:self.capture(_hook=change)
        self.assertEqual(c.exception.code,'I_INPUT_CHANGED')
    def test_mutation_during_read(self):
        with self.assertRaises(Denied):self.capture(_hook=lambda:self.source.write_bytes(b'mutated'))
    def test_local_hardlink(self):
        os.link(self.source,self.root/'alias')
        with self.assertRaises(Denied) as c:self.capture()
        self.assertEqual(c.exception.code,'I_INPUT_TYPE')
    def test_local_fifo(self):
        self.source.unlink();os.mkfifo(self.source)
        with self.assertRaises(Denied):self.capture()
    def test_local_symlink_no_follow(self):
        self.source.unlink();self.source.symlink_to('/etc/passwd')
        with self.assertRaises(Denied):self.capture()


class ComponentTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name);self.zip=self.root/'course.zip';self.zip.write_bytes(archive(fixture()))
        self.stage=self.root/'stage';self.stage.mkdir()
    def tearDown(self):
        for p in self.stage.iterdir():p.chmod(0o700)
        self.temp.cleanup()
    def call(self,**kwargs):
        a=import_zip(self.zip,runtime=RUNTIME,source_revision='synthetic-1',staging_parent=self.stage,**kwargs)
        if a.evidence:EVIDENCE.append({'test':self.id(),**a.evidence})
        return a
    def unsuccessful(self,a,status=None):
        if status:self.assertEqual(a.status,status,a)
        self.assertNotEqual(a.status,'Ready');self.assertIsNone(a.receipt);self.assertIsNone(a.student);self.assertIsNone(a.teacher)
    def test_full_ready_private_hashes_and_cleanup(self):
        a=self.call();self.assertEqual(a.status,'Ready',a)
        self.assertEqual(a.receipt.zipDigest,hashlib.sha256(self.zip.read_bytes()).hexdigest())
        self.assertEqual(a.receipt.packageHash,'a1f45d3e931d128f7cf7725eb1c19b61e9d89aa368692c502b9b91a27be8958f')
        self.assertEqual(a.receipt.studentProjectionHash,'3caf281a564d8d33fc553bfd9cb74e8b8552685119c57820c509a460e9bf5c20')
        private=(FIXTURE/'assets/teacher/files/solution.txt').read_bytes()
        self.assertIn(private,dict(a.teacher.assets).values());self.assertNotIn(private,a.student.content)
        self.assertNotIn(private,dict(a.student.assets).values());self.assertEqual(len(a.student.assets),1)
        self.assertNotIn(b'packageHash',a.student.content);self.assertNotIn(b'teacherArtifactHash',a.student.content)
        self.assertEqual(list(self.stage.iterdir()),[])
        self.assertTrue(a.evidence['observedProcessesGone']);self.assertTrue(a.evidence['cgroupEmpty'])
    def test_repeated_input_receipt_reproducible(self):
        a,b=self.call(),self.call();self.assertEqual(a.status,'Ready');self.assertEqual(b.status,'Ready')
        self.assertEqual(a.receipt,b.receipt);self.assertEqual(a.student,b.student);self.assertEqual(a.teacher,b.teacher)
        self.assertNotEqual(a.evidence['unit'],b.evidence['unit'])
    def test_teacher_only_change_noninterference(self):
        a=self.call();entries=fixture();entries=[(n,v+b'\nprivate canary\n' if n=='assets/teacher/files/solution.txt' else v) for n,v in entries]
        self.zip.write_bytes(archive(entries));b=self.call()
        self.assertEqual(b.status,'Ready',b);self.assertEqual(a.student,b.student)
        self.assertNotEqual(a.receipt.packageHash,b.receipt.packageHash);self.assertNotEqual(a.receipt.zipDigest,b.receipt.zipDigest)
    def test_private_compile_error_atomic(self):
        self.zip.write_bytes(archive([(n,v+b'\n[bad](../../missing.txt)\n' if n.endswith('pairs-solution.md') else v) for n,v in fixture()]))
        a=self.call();self.unsuccessful(a,'Invalid');self.assertEqual(list(self.stage.iterdir()),[])
    def test_invalid_zip_atomic(self):
        self.zip.write_bytes(b'no zip');a=self.call();self.unsuccessful(a,'Invalid');self.assertEqual(list(self.stage.iterdir()),[])
    def test_output_limit_atomic(self):
        a=self.call(limits=replace(Limits(),output_bytes=1000));self.unsuccessful(a,'Failed')
    def test_cancel_before_start(self):
        e=threading.Event();e.set();a=self.call(cancel=e);self.unsuccessful(a,'Cancelled');self.assertIsNone(a.evidence)
    def test_cleanup_failure_no_ready(self):
        with patch('tools.content_import.importer.shutil.rmtree',side_effect=OSError('secret cleanup detail')):a=self.call()
        self.unsuccessful(a,'Failed');self.assertEqual(a.diagnostics[0]['code'],'I_CLEANUP')
        self.assertNotIn('secret',repr(a));dirs=list(self.stage.iterdir());self.assertEqual(len(dirs),1)
        self.assertEqual(stat.S_IMODE(dirs[0].stat().st_mode),0)
    def test_missing_runtime_fail_closed(self):
        a=import_zip(self.zip,runtime=Runtime(Path('/no/node'),RUNTIME.katex),source_revision='synthetic')
        self.unsuccessful(a,'Failed')
    def test_forged_success_no_digest(self):
        with patch('tools.content_import.importer.run_worker',return_value=(b'{"status":"Ready"}',None,{})):a=self.call()
        self.unsuccessful(a,'Failed')
    def test_tampered_artifact_rejected(self):
        from tools.content_import import importer
        original=importer.run_worker
        def tamper(*args,**kwargs):
            raw,reason,ev=original(*args,**kwargs);v=json.loads(raw);v['student']['assets']['assets/shared-table.txt']=base64.b64encode(b'changed').decode()
            return json.dumps(v).encode(),reason,ev
        with patch('tools.content_import.importer.run_worker',tamper):a=self.call()
        self.unsuccessful(a,'Failed')

    def test_literal_source_revision(self):
        a=import_zip(self.zip,runtime=RUNTIME,source_revision='$HOME ${USER} literal',staging_parent=self.stage)
        self.assertEqual(a.status,'Ready',a);self.assertEqual(a.receipt.sourceRevision,'$HOME ${USER} literal')
    def test_cancel_inflight_no_partial_artifacts(self):
        from tools.content_import import importer
        original=importer.run_worker
        probe=self.root/'probe.py';probe.write_text(SupervisorTests.CHILD)
        def hung(*args,**kw):return original(*args,**kw,_probe=probe)
        event=threading.Event();timer=threading.Timer(0.5,event.set);timer.start()
        try:
            with patch('tools.content_import.importer.run_worker',hung):a=self.call(cancel=event)
        finally:timer.cancel();timer.join()
        self.unsuccessful(a,'Cancelled');self.assertEqual(list(self.stage.iterdir()),[])
    def test_changed_source_after_snapshot_uses_retained_bytes(self):
        from tools.content_import import importer
        original=importer.run_worker;zip_hash=hashlib.sha256(self.zip.read_bytes()).hexdigest()
        def change(*args,**kwargs):
            self.zip.write_bytes(b'replaced during compile')
            return original(*args,**kwargs)
        with patch('tools.content_import.importer.run_worker',change):a=self.call()
        self.assertEqual(a.status,'Ready',a);self.assertEqual(a.receipt.zipDigest,zip_hash)
        self.assertEqual(a.receipt.packageHash,'a1f45d3e931d128f7cf7725eb1c19b61e9d89aa368692c502b9b91a27be8958f')


class SupervisorTests(unittest.TestCase):
    def run_probe(self,code,limits=Limits(),cancel_after=None):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);source=root/'input';source.write_bytes(b'synthetic');probe=root/'probe.py';probe.write_text(code)
            cancel=threading.Event();timer=None
            if cancel_after is not None:timer=threading.Timer(cancel_after,cancel.set);timer.start()
            try:raw,reason,ev=run_worker(source,RUNTIME,limits,'synthetic',cancel,_probe=probe)
            finally:
                if timer:timer.cancel();timer.join()
            EVIDENCE.append({'test':self.id(),'reason':reason,**ev})
            self.assertTrue(ev['cgroupEmpty'],ev);self.assertTrue(ev['observedProcessesGone'],ev)
            self.assertGreater(len(ev['observedPids']),0,ev)
            return raw,reason,ev
    CHILD="""import os,time,signal
pid=os.fork()
if pid==0:
 os.setsid()
 signal.signal(signal.SIGTERM,signal.SIG_IGN)
 while True:time.sleep(1)
else:
 while True:time.sleep(1)
"""
    def test_hung_detached_child_timeout(self):
        raw,reason,ev=self.run_probe(self.CHILD,replace(Limits(),wall_seconds=2))
        self.assertIsNone(raw);self.assertIn(reason,('I_TIMEOUT','I_WORKER_EXIT'));self.assertGreaterEqual(len(ev['observedPids']),4)
        self.assertLess(ev['elapsedSeconds'],5)
    def test_cancellation_kills_tree(self):
        raw,reason,ev=self.run_probe(self.CHILD,cancel_after=0.3)
        self.assertEqual(reason,'I_CANCELLED');self.assertIsNone(raw);self.assertLess(ev['elapsedSeconds'],3)
    def test_normal_exit_kills_lingering_child(self):
        code=self.CHILD.replace('else:\n while True:time.sleep(1)','else:\n time.sleep(0.2)\n print("done",flush=True)')
        raw,reason,ev=self.run_probe(code);self.assertIsNone(reason);self.assertEqual(raw,b'done\n')
    def test_memory_limit(self):
        code='import time\na=[]\nwhile True:\n a.append(bytearray(1048576))\n time.sleep(0.005)\n'
        raw,reason,ev=self.run_probe(code,replace(Limits(),memory_bytes=33554432))
        self.assertIsNone(raw);self.assertEqual(reason,'I_WORKER_EXIT')
        self.assertTrue(ev['managerResult']=='oom-kill' or int(ev['events'].get('memory.events',{}).get('oom_kill',0))>0,ev)
    def test_cpu_limit(self):
        raw,reason,ev=self.run_probe('while True: pass\n',replace(Limits(),cpu_seconds=1,wall_seconds=5))
        self.assertIsNone(raw);self.assertIn(reason,('I_CPU_LIMIT','I_WORKER_EXIT'));self.assertLess(ev['elapsedSeconds'],4)
    def test_aggregate_cpu_budget_across_children(self):
        code='import os,time\nfor i in range(3):\n if os.fork()==0:\n  while True:pass\nwhile True:time.sleep(1)\n'
        raw,reason,ev=self.run_probe(code,replace(Limits(),cpu_seconds=1,wall_seconds=5))
        self.assertIsNone(raw);self.assertEqual(reason,'I_CPU_LIMIT',ev)
        self.assertGreaterEqual(ev['cpuUsec'],1_000_000);self.assertGreaterEqual(len(ev['observedPids']),6)
        self.assertLess(ev['elapsedSeconds'],4)
    def test_output_pipe_limit(self):
        raw,reason,ev=self.run_probe('import os\nwhile True:os.write(1,b"x"*65536)\n',replace(Limits(),output_bytes=1024,entries=1))
        self.assertIsNone(raw);self.assertEqual(reason,'I_OUTPUT_LIMIT')
    def test_process_limit(self):
        code='''import os,time,json
children=[]
try:
 while True:
  p=os.fork()
  if p==0:
   time.sleep(10);os._exit(0)
  children.append(p)
except OSError:
 time.sleep(0.1)
 print(json.dumps({'children':len(children)}),flush=True)
'''
        raw,reason,ev=self.run_probe(code,replace(Limits(),tasks=8))
        self.assertIsNone(reason,ev);self.assertLessEqual(json.loads(raw)['children'],5)
        self.assertGreater(int(ev['events'].get('pids.events',{}).get('max',0)),0,ev)
    def test_namespace_no_network_secrets_db(self):
        os.environ['B07_TEST_SECRET']='must-not-inherit'
        code='''import os,socket,json,pathlib,time
checks={'secret': 'B07_TEST_SECRET' in os.environ,'home':pathlib.Path('/home/tea').exists(),'db':pathlib.Path('/run/postgresql').exists(),'network':False,'inputWritable':False,'netNamespace':os.stat('/proc/self/ns/net').st_ino}
try:
 s=socket.create_connection(('192.0.2.1',9),timeout=0.1);checks['network']=True
except OSError:pass
try:
 open('/input.zip','wb').write(b'x');checks['inputWritable']=True
except OSError:pass
time.sleep(0.1)
print(json.dumps(checks))
'''
        try:raw,reason,ev=self.run_probe(code)
        finally:del os.environ['B07_TEST_SECRET']
        self.assertIsNone(reason,ev);checks=json.loads(raw)
        self.assertNotEqual(checks.pop('netNamespace'),os.stat('/proc/self/ns/net').st_ino)
        self.assertEqual(checks,dict(secret=False,home=False,db=False,network=False,inputWritable=False))
    def test_tmpfs_storage_limit(self):
        code='''import os,json,time
n=0
try:
 with open('/work/fill','wb',buffering=0) as f:
  while True:n+=f.write(b'x'*65536)
except OSError as e:
 time.sleep(0.1);print(json.dumps({'errno':e.errno,'bytes':n}))
'''
        raw,reason,ev=self.run_probe(code,replace(Limits(),expanded_bytes=1024,output_bytes=1024))
        self.assertIsNone(reason,ev);self.assertEqual(json.loads(raw)['errno'],28);self.assertLessEqual(json.loads(raw)['bytes'],4_198_400)


def tearDownModule():
    path=os.environ.get('B07_EVIDENCE_PATH')
    if path:Path(path).write_text(json.dumps(EVIDENCE,indent=2)+'\n')
