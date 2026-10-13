"""Isolated v1 validation semantics. Never produces delivery artifacts.

No pre-existing executable validator was present. This profile implements the
unchanged specification: unknown fields warn, cross-lesson problem IDs warn,
internal symlinks and local Markdown links are allowed. It is not a 1.1 snapshot.
"""
import os
import re
from pathlib import Path
from urllib.parse import urlsplit, unquote
from markdown_it import MarkdownIt
from .model import Diagnostics, Location, Reject
from .yaml_input import document


def validate_legacy(root, limits=None):
    limits=limits or dict(document=1000000,image=10000000,static=50000000,package=100000000)
    diagnostics=Diagnostics();result={'status':'invalid'}
    root=Path(root).resolve()
    try:
        if not (root/'course.md').is_file() or not (root/'lessons').is_dir(): raise Reject('E_PACKAGE_STRUCTURE',Location('course.md'))
        total=0;docs={};seen_dirs=set()
        def inside(path):
            resolved=path.resolve()
            if not resolved.is_relative_to(root): raise Reject('E_PATH',Location(str(path.relative_to(root))))
            return resolved
        def walk(directory):
            nonlocal total
            resolved=inside(directory)
            if resolved in seen_dirs: return
            seen_dirs.add(resolved)
            for path in sorted(directory.iterdir()):
                actual=inside(path)
                if actual.is_dir(): walk(path);continue
                if not actual.is_file(): raise Reject('E_FILE_TYPE',Location(str(path.relative_to(root))))
                size=actual.stat().st_size;total+=size
                limit=limits['document'] if path.suffix=='.md' else limits['image'] if path.suffix.lower() in ('.png','.jpg','.jpeg','.svg','.webp') else limits['static']
                if size>limit or total>limits['package']: raise Reject('E_LIMIT',Location(str(path.relative_to(root))))
                if path.suffix=='.md':
                    with actual.open('rb') as stream: data=stream.read(limit+1)
                    if len(data)>limit: raise Reject('E_LIMIT')
                    rel=str(path.relative_to(root));docs[rel]=document(data,rel,strict=False)
        walk(root)
        for d in (root/'lessons').iterdir():
            if d.is_dir() and not (d/'lesson.md').is_file(): raise Reject('E_PACKAGE_STRUCTURE',Location(str(d.relative_to(root))+'/lesson.md'))
        ids={};orders={}
        for file,(m,body,offset) in sorted(docs.items()):
            schema=m.get('schema')
            if schema not in ('course/v1','lesson/v1','problem/v1'): raise Reject('E_SCHEMA_UNSUPPORTED',m.at('schema'))
            kind=schema.split('/')[0]
            patterns={'course':r'course\.md','lesson':r'lessons/[^/]+/lesson\.md','problem':r'lessons/[^/]+/problems/[^/]+\.md'}
            if not re.fullmatch(patterns[kind],file): raise Reject('E_PACKAGE_STRUCTURE',m.at('schema'))
            required={'schema','id'}|({'title'} if kind!='problem' else set())|({'order'} if kind!='course' else set())
            allowed=required|({'description'} if kind=='course' else {'title','source'} if kind=='problem' else set())
            for k in required:
                if k not in m: raise Reject('E_FIELD_REQUIRED',m.loc)
            for k in m:
                if k not in allowed: diagnostics.add('W_FIELD_UNKNOWN',m.keys_at[k])
            if not isinstance(m['id'],str) or not re.fullmatch('[a-z][a-z0-9-]*',m['id']): raise Reject('E_ID_FORMAT',m.at('id'))
            key=(kind,m['id'])
            if key in ids:
                code='W_ID_DUPLICATE' if kind=='problem' else 'E_ID_DUPLICATE'
                diagnostics.add(code,m.at('id'),[ids[key]])
            ids[key]=m.at('id')
            for k in ('title','description','source'):
                if k in m and not isinstance(m[k],str): raise Reject('E_FIELD_TYPE',m.at(k))
            if 'order' in m:
                if type(m['order']) is not int: raise Reject('E_FIELD_TYPE',m.at('order'))
                if kind=='lesson' and m['order']<=0: raise Reject('E_FIELD_VALUE',m.at('order'))
                scope=(kind,str(Path(file).parent) if kind=='problem' else '',m['order'])
                if scope in orders: raise Reject('E_ORDER_DUPLICATE',m.at('order'),[orders[scope]])
                orders[scope]=m.at('order')
            md=MarkdownIt('commonmark',{'html':True,'inline_definitions':True}).enable('table')
            md.validateLink=lambda _:True
            def check(url,image=False):
                u=urlsplit(url)
                if u.scheme:
                    if u.scheme!='https' or not u.hostname: raise Reject('E_URI',Location(file,offset+1))
                    if image: raise Reject('E_EXTERNAL_IMAGE',Location(file,offset+1))
                elif url.startswith('//'): raise Reject('E_URI',Location(file,offset+1))
                elif u.path:
                    target=inside(root/Path(file).parent/unquote(u.path))
                    if not target.exists(): raise Reject('E_FILE_MISSING',Location(file,offset+1))
            def inspect(tokens):
                for t in tokens:
                    if t.type in ('html_block','html_inline'): raise Reject('E_MARKDOWN',Location(file,offset+1))
                    if t.type=='definition': check(t.meta['url'])
                    if t.type=='link_open': check(t.attrGet('href'))
                    if t.type=='image': check(t.attrGet('src'),True)
                    if t.children: inspect(t.children)
            inspect(md.parse(body))
        result['status']='invalid' if any(x['severity']=='ERROR' for x in diagnostics.items) else 'valid'
    except Reject as e: diagnostics.add(e.code,e.location,e.related)
    except (OSError,ValueError): diagnostics.add('E_IO');result['status']='failed'
    result['diagnostics']=diagnostics.ordered()
    return result
