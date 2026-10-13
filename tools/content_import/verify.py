"""Verify closed B07a build output before an import can become Ready."""
import base64
import json
import re
from tools.content_compiler.model import canonical, digest, REVISION, COMPILER, MESSAGES
from .model import Artifact, Denied
from .zip_adapter import path_parts, package_hash


def closed_json(data):
    def pairs(values):
        d = {}
        for k,v in values:
            if k in d: raise ValueError('Duplicate JSON key')
            d[k] = v
        return d
    return json.loads(data, object_pairs_hook=pairs)


def diagnostics(items):
    if not isinstance(items,list): raise ValueError('Invalid diagnostics')
    for d in items:
        if set(d) != {'severity','code','file','line','column','message','related'}: raise ValueError('Invalid diagnostics')
        if d['code'] not in MESSAGES or d['message'] != MESSAGES[d['code']]: raise ValueError('Invalid diagnostics')
        if d['severity'] != ('WARNING' if d['code'].startswith('W_') else 'ERROR'): raise ValueError('Invalid diagnostics')
        for loc in [d, *d['related']]:
            path_parts(loc['file'])
            if any(type(loc[k]) is not int or loc[k] < 1 for k in ('line','column')): raise ValueError('Invalid location')
    return tuple(items)


def bundle(value, role):
    if set(value) != {'json','assets'} or not isinstance(value['assets'],dict): raise ValueError('Invalid bundle')
    data = base64.b64decode(value['json'],validate=True)
    obj = closed_json(data)
    if canonical(obj) != data or obj['contractRevision'] != REVISION: raise ValueError('Noncanonical artifact')
    assets = {p:base64.b64decode(v,validate=True) for p,v in value['assets'].items()}
    expected = set()
    for a in obj['assets']:
        if set(a) != {'id','ownerKind','ownerId','visibility','mediaType','sizeBytes','sha256','artifactPath'}: raise ValueError('Invalid manifest')
        p = a['artifactPath']
        if len(path_parts(p)) != 2 or not p.startswith('assets/') or p in expected: raise ValueError('Invalid manifest path')
        expected.add(p)
        if role == 'student' and a['visibility'] != 'student': raise ValueError('Private asset')
        if a['visibility'] not in ('student','teacher'): raise ValueError('Visibility')
        if a['sizeBytes'] != len(assets[p]) or a['sha256'] != digest(assets[p]): raise ValueError('Asset integrity')
    if set(assets) != expected: raise ValueError('Unexpected assets')
    return Artifact(data, tuple(sorted(assets.items()))),obj


def verify(value, source_revision, limits):
    if set(value) != {'status','diagnostics','metadata','student','teacher','files'} or value['status'] != 'Ready':
        raise ValueError('Invalid success envelope')
    ds = diagnostics(value['diagnostics'])
    if any(d['severity'] == 'ERROR' for d in ds): raise ValueError('Error in success')
    student,s = bundle(value['student'],'student'); teacher,t = bundle(value['teacher'],'teacher')
    if set(s) != {'artifactSchema','contractRevision','course','assets','studentProjectionHash'} or s['artifactSchema'] != 'student-content/v1': raise ValueError('Student schema')
    if set(t) != {'artifactSchema','contractRevision','packageHash','studentProjectionHash','student','hints','materials','assets'} or t['artifactSchema'] != 'teacher-content/v1': raise ValueError('Teacher schema')
    projection = {k:v for k,v in s.items() if k != 'studentProjectionHash'}
    if s['studentProjectionHash'] != digest(canonical(projection)) or t['student'] != s or t['studentProjectionHash'] != s['studentProjectionHash']: raise ValueError('Projection integrity')
    index = value['files']
    if not isinstance(index,list) or len(index) > limits.entries: raise ValueError('File index')
    seen = set(); total = 0
    for f in index:
        if set(f) != {'path','sizeBytes','sha256'}: raise ValueError('File index')
        path_parts(f['path'])
        if f['path'] in seen or type(f['sizeBytes']) is not int or f['sizeBytes'] < 0 or not re.fullmatch('[0-9a-f]{64}',f['sha256']): raise ValueError('File index')
        seen.add(f['path']); total += f['sizeBytes']
    if total > limits.expanded_bytes or index != sorted(index,key=lambda x:x['path']): raise ValueError('File index')
    m = value['metadata']
    expected = dict(contractRevision=REVISION,compilerRevision=COMPILER,packageHash=package_hash(index),studentProjectionHash=s['studentProjectionHash'],teacherArtifactHash=digest(teacher.content),limits=dict(document=1000000,image=10000000,static=50000000,package=limits.expanded_bytes),sourceRevision=source_revision)
    if m != expected or t['packageHash'] != m['packageHash']: raise ValueError('Metadata integrity')
    if sum(len(a.content)+sum(len(v) for _,v in a.assets) for a in (student,teacher)) > limits.output_bytes: raise ValueError('Output limit')
    return ds, student, teacher, m
