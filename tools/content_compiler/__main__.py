"""Closed local build command. Publish neither source nor build metadata."""
import argparse
import json
import os
from pathlib import Path
import shutil
import tempfile
from .compiler import compile_package
from .legacy import validate_legacy
from .model import canonical, Diagnostics


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root',type=Path)
    parser.add_argument('--package-limit',type=int,required=True)
    parser.add_argument('--document-limit',type=int,default=1000000)
    parser.add_argument('--image-limit',type=int,default=10000000)
    parser.add_argument('--static-limit',type=int,default=50000000)
    parser.add_argument('--source-revision')
    parser.add_argument('--output',type=Path,help='New private local build directory (must not exist).')
    parser.add_argument('--legacy-validate',action='store_true')
    args=parser.parse_args()
    root=args.root.absolute()
    if args.legacy_validate:
        result=validate_legacy(root,dict(package=args.package_limit,document=args.document_limit,image=args.image_limit,static=args.static_limit))
    else:
        result=compile_package(root,package_limit=args.package_limit,document_limit=args.document_limit,image_limit=args.image_limit,static_limit=args.static_limit,source_revision=args.source_revision)
    if result['status']=='valid' and not args.legacy_validate and args.output:
        output=args.output.absolute()
        output=output.parent.resolve()/output.name
        if output.exists() or output.is_symlink() or output.is_relative_to(root):
            parser.error('Output must be a new directory outside the source package.')
        # Output parent is a trusted caller-controlled local build directory.
        # Both bundles are staged together, then exposed by one directory rename.
        stage=None
        try:
            stage=Path(tempfile.mkdtemp(prefix='.content-build-',dir=output.parent))
            for role in ('student','teacher'):
                bundle=result[role+'Artifact'];folder=stage/role;folder.mkdir(mode=0o700)
                (folder/'content.json').write_bytes(bundle['json'])
                for path,data in bundle['assets'].items():
                    target=folder/path;target.parent.mkdir(exist_ok=True,mode=0o700);target.write_bytes(data)
            (stage/'build-metadata.json').write_bytes(canonical(result['buildMetadata']))
            (stage/'diagnostics.json').write_bytes(canonical(result['diagnostics']))
            # renameat2(RENAME_NOREPLACE) prevents races with another build.
            import ctypes
            libc=ctypes.CDLL(None,use_errno=True)
            if libc.renameat2(-100,os.fsencode(stage),-100,os.fsencode(output),1)!=0:
                raise OSError(ctypes.get_errno(),'Atomic build rename failed')
        except OSError:
            diagnostics=Diagnostics();diagnostics.add('E_IO')
            result={k:v for k,v in result.items() if k not in ('studentArtifact','teacherArtifact','buildMetadata')}
            result.update(status='failed',diagnostics=diagnostics.ordered())
        finally:
            if stage is not None and stage.exists(): shutil.rmtree(stage)
    summary={k:v for k,v in result.items() if k not in ('studentArtifact','teacherArtifact')}
    print(json.dumps(summary,ensure_ascii=False,indent=2))
    return 0 if result['status']=='valid' else 1


if __name__=='__main__': raise SystemExit(main())
