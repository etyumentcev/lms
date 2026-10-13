"""Trusted local test caller API; no HTTP, storage service or authority model."""
from dataclasses import asdict
import hashlib
import errno
import os
from pathlib import Path
import shutil
import stat
import tempfile
import time
from .model import Limits, Result, BuildReceipt, Denied, diagnostic
from .sandbox import Runtime, run_worker
from .verify import closed_json, verify, diagnostics


def identity(s):
    return (s.st_dev,s.st_ino,s.st_size,s.st_mtime_ns,s.st_ctime_ns,s.st_nlink,s.st_mode)


def snapshot_zip(path, destination, limits, cancel, *, _hook=None):
    start=time.monotonic()
    try:
        fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC)
    except OSError as e:
        if e.errno == errno.ELOOP: raise Denied('I_INPUT_TYPE') from None
        raise
    try:
        before=os.fstat(fd)
        if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1: raise Denied('I_INPUT_TYPE')
        if before.st_size>limits.zip_bytes: raise Denied('I_ZIP_LIMIT')
        h=hashlib.sha256(); total=0
        with destination.open('xb') as out:
            while True:
                if cancel is not None and cancel.is_set(): raise Denied('I_CANCELLED','Cancelled')
                if time.monotonic()-start>limits.wall_seconds: raise Denied('I_TIMEOUT','Failed')
                block=os.read(fd,min(65536,limits.zip_bytes-total+1))
                if not block: break
                total+=len(block)
                if total>limits.zip_bytes: raise Denied('I_ZIP_LIMIT')
                h.update(block);out.write(block)
                if _hook: _hook()
        if identity(before)!=identity(os.fstat(fd)) or identity(before)!=identity(os.stat(path,follow_symlinks=False)):
            raise Denied('I_INPUT_CHANGED')
        destination.chmod(0o400)
        return h.hexdigest()
    finally: os.close(fd)


def import_zip(path, *, runtime, source_revision, limits=Limits(), cancel=None, staging_parent=None):
    """Returns private in-memory bundles only after sandbox teardown and cleanup.

    Paths/runtime/source revision are supplied by trusted synthetic test code.
    Never pass this API directly to an HTTP request. It issues no permissions.
    """
    stage=None; evidence=None; result=None
    try:
        limits.validate()
        if type(source_revision) is not str or not 0<len(source_revision.encode('utf-8'))<=256 or any(ord(c)<32 for c in source_revision):
            raise Denied('I_CONFIG','Failed')
        if cancel is not None and cancel.is_set(): raise Denied('I_CANCELLED','Cancelled')
        fingerprint=runtime.fingerprint()
        stage=Path(tempfile.mkdtemp(prefix='b07c1-',dir=staging_parent));stage.chmod(0o700)
        snapshot=stage/'input.zip'
        zip_digest=snapshot_zip(Path(path),snapshot,limits,cancel)
        raw,reason,evidence=run_worker(snapshot,runtime,limits,source_revision,cancel)
        if reason: raise Denied(reason,'Cancelled' if reason=='I_CANCELLED' else 'Failed')
        value=closed_json(raw)
        reported_digest=value.pop('zipDigest',None)
        if reported_digest != zip_digest and (value.get('status')=='Ready' or reported_digest is not None): raise Denied('I_INTEGRITY','Failed')
        if value.get('status')=='Ready':
            ds,student,teacher,metadata=verify(value,source_revision,limits)
            receipt=BuildReceipt('b07b-c1/v1',zip_digest,metadata['packageHash'],metadata['studentProjectionHash'],metadata['teacherArtifactHash'],source_revision,metadata['compilerRevision'],metadata['contractRevision'],fingerprint,tuple(sorted(asdict(limits).items())))
            result=Result('Ready',ds,receipt,student,teacher,evidence)
        elif value.get('status') in ('Invalid','Failed') and set(value)=={'status','diagnostics'}:
            ds=value['diagnostics']
            if len(ds)==1 and set(ds[0])=={'severity','code','message'} and ds[0]==diagnostic(ds[0]['code']) and ds[0]['code'] in {'I_PATH','I_ZIP_LIMIT','I_ZIP_FORMAT','I_ENTRY_LIMIT','I_COLLISION','I_ENTRY_TYPE','I_ZIP_FEATURE','I_EXPANDED_LIMIT','I_NESTED_ARCHIVE','I_DEPENDENCY','I_OUTPUT_LIMIT','I_WORKER'}:
                ds=tuple(ds)
            else: ds=diagnostics(ds)
            if not ds: raise Denied('I_INTEGRITY','Failed')
            result=Result(value['status'],ds,evidence=evidence)
        else: raise Denied('I_INTEGRITY','Failed')
    except Denied as e:
        result=Result(e.status,(diagnostic(e.code),),evidence=evidence)
    except Exception:
        result=Result('Failed',(diagnostic('I_IMPORT'),),evidence=evidence)
    finally:
        if stage is not None:
            try: shutil.rmtree(stage)
            except OSError:
                # No candidate escapes after cleanup failure. Private quarantine
                # denies traversal until explicit trusted test-operator removal.
                try: stage.chmod(0)
                except OSError: pass # original root remains mode 0700, never exposed
                result=Result('Failed',(diagnostic('I_CLEANUP'),),evidence={**(evidence or {}),'quarantined':True})
    if cancel is not None and cancel.is_set() and result.status=='Ready':
        return Result('Cancelled',(diagnostic('I_CANCELLED'),),evidence=evidence)
    return result
