"""Fixed sandbox entry point. No transport-controlled command or environment."""
import base64
from dataclasses import asdict
import importlib.metadata
import json
import os
from pathlib import Path
import resource
import sys
from tools.content_compiler.compiler import compile_package
from tools.content_compiler.model import canonical, digest
from .model import Limits, Denied, diagnostic
from .zip_adapter import extract
from .verify import verify


def main():
    os.umask(0o077)
    limits = Limits(**json.loads(sys.argv[1])); limits.validate()
    revision = sys.argv[2]
    resource.setrlimit(resource.RLIMIT_CORE,(0,0))
    resource.setrlimit(resource.RLIMIT_CPU,(limits.cpu_seconds,limits.cpu_seconds))
    resource.setrlimit(resource.RLIMIT_FSIZE,(limits.output_bytes,limits.output_bytes))
    resource.setrlimit(resource.RLIMIT_NOFILE,(64,64))
    try:
        if sys.version_info[:3] != (3,14,4): raise Denied('I_DEPENDENCY','Failed')
        for n,v in [('PyYAML','6.0.3'),('markdown-it-py','3.0.0'),('Pillow','12.1.1')]:
            if importlib.metadata.version(n) != v: raise Denied('I_DEPENDENCY','Failed')
        root=Path('/work/source'); root.mkdir(mode=0o700)
        data=Path('/input.zip').read_bytes()
        files=extract(data,root,limits)
        # Only retained, private source tree is passed to accepted B07a.
        result=compile_package(root,package_limit=limits.expanded_bytes,source_revision=revision)
        if result['status'] != 'valid':
            output=dict(status='Invalid' if result['status']=='invalid' else 'Failed', diagnostics=result['diagnostics'])
        else:
            output=dict(status='Ready', diagnostics=result['diagnostics'],metadata=result['buildMetadata'],files=files)
            total=0
            for role in ('student','teacher'):
                artifact=result[role+'Artifact']; directory=Path('/work')/role;directory.mkdir(mode=0o700)
                entries={'content.json':artifact['json'], **artifact['assets']}
                for name,content in entries.items():
                    total+=len(content)
                    if total>limits.output_bytes: raise Denied('I_OUTPUT_LIMIT','Failed')
                    target=directory/name;target.parent.mkdir(mode=0o700,parents=True,exist_ok=True)
                    with target.open('xb') as f: f.write(content)
                    target.chmod(0o400)
                # Read back the exact private staged bytes; verifier checks all hashes.
                output[role]={'json':base64.b64encode((directory/'content.json').read_bytes()).decode(),
                              'assets':{p:base64.b64encode((directory/p).read_bytes()).decode() for p in artifact['assets']}}
            verify(output,revision,limits)
        output['zipDigest']=digest(data)
    except Denied as e:
        output=dict(status=e.status,diagnostics=[diagnostic(e.code)])
    except Exception:
        output=dict(status='Failed',diagnostics=[diagnostic('I_WORKER')])
    sys.stdout.buffer.write(canonical(output));sys.stdout.buffer.flush()


if __name__=='__main__': main()
