"""Revision 1.1 offline compiler; all publication and delivery are out of scope."""
import io
import os
import re
import stat
import warnings
from pathlib import PurePosixPath
from PIL import Image
from .model import COMPILER, REVISION, MAX_INTEGER, Diagnostics, Location, Reject, canonical, digest
from .snapshot import Snapshot, inventory_path
from .yaml_input import Mapping, Sequence, document, parse
from .markdown import process

ID = re.compile(r'[a-z][a-z0-9-]*\Z')
MIME = {'.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.webp': 'image/webp', '.txt': 'text/plain', '.pdf': 'application/pdf'}
BASE = {'course': ({'schema', 'id', 'title'}, {'description'}),
        'lesson': ({'schema', 'id', 'title', 'order'}, set()),
        'problem': ({'schema', 'id', 'order'}, {'title', 'source'}),
        'hints': ({'schema', 'id', 'targetKind', 'targetId', 'order', 'publication'}, {'title'}),
        'teacher': ({'schema', 'id', 'targetKind', 'targetId', 'kind', 'order'}, {'title'})}


def fields(m, required, optional=()):
    if not isinstance(m, Mapping): raise Reject('E_FIELD_TYPE')
    for key in sorted(required):
        if key not in m: raise Reject('E_FIELD_REQUIRED', m.loc)
    for key in m:
        if key not in required and key not in optional: raise Reject('E_FIELD_UNKNOWN', m.keys_at[key])


def string(m, key, values=None, identifier=False, nonempty=False):
    value = m[key]
    if not isinstance(value, str): raise Reject('E_FIELD_TYPE', m.at(key))
    if identifier and not ID.fullmatch(value): raise Reject('E_ID_FORMAT', m.at(key))
    if (values is not None and value not in values) or (nonempty and not value.strip()): raise Reject('E_FIELD_VALUE', m.at(key))
    return value


def asset_bytes(asset, data):
    loc = asset.at('path')
    ext = PurePosixPath(asset['path']).suffix.lower()
    if ext not in MIME or MIME[ext] != asset['mediaType'] or ext == '.pdf':
        # No safe PDF parser proof exists in B07a. Even ostensibly passive PDFs deny.
        raise Reject('E_ASSET_TYPE', loc)
    try:
        if ext == '.txt': data.decode('utf-8', errors='strict')
        else:
            with warnings.catch_warnings():
                warnings.simplefilter('error', Image.DecompressionBombWarning)
                with Image.open(io.BytesIO(data)) as im:
                    expected = {'.png':'PNG','.jpg':'JPEG','.jpeg':'JPEG','.webp':'WEBP'}[ext]
                    if im.format != expected: raise ValueError()
                    im.verify()
                with Image.open(io.BytesIO(data)) as im:
                    for frame in range(getattr(im, 'n_frames', 1)):
                        im.seek(frame)
                        im.load()
    except (UnicodeError, OSError, ValueError, SyntaxError, Image.DecompressionBombError, Image.DecompressionBombWarning):
        raise Reject('E_ASSET_TYPE', loc) from None


class Build:
    def __init__(self, files, inventory, diagnostics):
        self.files, self.inventory, self.diagnostics = files, inventory, diagnostics
        self.docs, self.assets, self.entities, self.orders, self.materials = {}, {}, {}, {}, {}
        self.used_student, self.used_teacher, self.used = set(), set(), set()

    def duplicate(self, registry, key, loc, code):
        if key in registry: raise Reject(code, loc, [registry[key]])
        registry[key] = loc

    def documents(self):
        for path in sorted(self.inventory['documents']):
            m, body, offset = document(self.files[path], path)
            schema = string(m, 'schema') if 'schema' in m else None
            if schema is None: raise Reject('E_FIELD_REQUIRED', m.loc)
            if schema in ('course/v1','lesson/v1','problem/v1'): raise Reject('E_SCHEMA_MIX', m.at('schema'))
            kind = schema.split('/')[0]
            if schema not in ('course/v1.1','lesson/v1.1','problem/v1.1','hints/v1','teacher/v1'):
                raise Reject('E_SCHEMA_UNSUPPORTED', m.at('schema'))
            for required in sorted(BASE[kind][0]):
                if required not in m: raise Reject('E_FIELD_REQUIRED',m.loc)
            string(m, 'id', identifier=True)
            for key in ('title','description','source'):
                if key in m: string(m,key,nonempty=key=='title')
            if 'order' in m:
                if type(m['order']) is not int: raise Reject('E_FIELD_TYPE',m.at('order'))
                if not 1 <= m['order'] <= 2147483647: raise Reject('E_FIELD_VALUE',m.at('order'))
            patterns = {'course':r'course\.md', 'lesson':r'lessons/[^/]+/lesson\.md',
                        'problem':r'lessons/[^/]+/problems/[^/]+\.md',
                        'hints':r'materials/hints/[^/]+\.md', 'teacher':r'materials/teacher/[^/]+\.md'}
            if not re.fullmatch(patterns[kind],path): raise Reject('E_PACKAGE_STRUCTURE',m.at('schema'))
            fields(m, *BASE[kind])
            self.duplicate(self.entities,(kind,m['id']),m.at('id'),'E_ID_DUPLICATE')
            self.docs[path] = dict(meta=m,body=body,offset=offset,kind=kind)
        if 'course.md' not in self.docs: raise Reject('E_PACKAGE_STRUCTURE', Location('course.md'))
        self.course = self.docs['course.md']['meta']
        if self.course['id'] != self.inventory['courseId']: raise Reject('E_TARGET_MISSING',self.inventory.at('courseId'))
        self.parents = {('course',self.course['id']): None}
        for path,d in self.docs.items():
            m,k = d['meta'],d['kind']
            if k == 'lesson': self.parents[(k,m['id'])] = ('course', self.course['id'])
            if k == 'problem':
                parent_path = '/'.join(path.split('/')[:2])+'/lesson.md'
                if parent_path not in self.docs: raise Reject('E_PACKAGE_STRUCTURE',Location(parent_path))
                self.parents[(k,m['id'])] = ('lesson',self.docs[parent_path]['meta']['id'])
        for path,d in self.docs.items():
            m,k = d['meta'],d['kind']
            if k in ('hints','teacher'):
                string(m,'targetKind',values={'course','lesson','problem'})
                string(m,'targetId',identifier=True)
                if k == 'hints': string(m,'publication',values={'with-problem','withheld'})
                else: string(m,'kind',values={'notes','solution','rubric'})
                if (k=='hints' or m.get('kind') in ('solution','rubric')) and m['targetKind']!='problem':
                    raise Reject('E_TARGET_KIND',m.at('targetKind'))
                target=(m['targetKind'],m['targetId'])
                if target not in self.parents: raise Reject('E_TARGET_MISSING',m.at('targetId'))
                if not d['body'].strip(): raise Reject('E_FIELD_VALUE',Location(path,d['offset']+1))
                if k=='teacher' and m['kind']!='notes':
                    self.duplicate(self.materials,(target,m['kind']),m.at('kind'),'E_MATERIAL_DUPLICATE')
            else: target=(k,m['id'])
            d['target']=target
            d['student']=k in ('course','lesson','problem') or (k=='hints' and m['publication']=='with-problem')
            if 'order' in m:
                scope=target if k in ('hints','teacher') else self.parents[target]
                self.duplicate(self.orders,(k,scope,m['order']),m.at('order'),'E_ORDER_DUPLICATE')

    def asset_definitions(self):
        ids={}
        for a in self.inventory['assets']:
            fields(a,{'id','path','ownerKind','ownerId','visibility','mediaType'})
            string(a,'id',identifier=True)
            string(a,'ownerId',identifier=True)
            string(a,'ownerKind',values={'course','lesson','problem'})
            string(a,'visibility',values={'student','teacher'})
            string(a,'mediaType')
            self.duplicate(ids,a['id'],a.at('id'),'E_ID_DUPLICATE')
            if a['path'] in self.assets: raise Reject('E_ASSET_DUPLICATE',a.at('path'),[self.assets[a['path']].at('path')])
            owner=(a['ownerKind'],a['ownerId'])
            if owner not in self.parents: raise Reject('E_TARGET_MISSING',a.at('ownerId'))
            match=re.fullmatch(r'(?:lessons/([^/]+)/)?assets/(student|teacher)/(images|files)/(.+)',a['path'])
            if not match: raise Reject('E_PATH',a.at('path'))
            if match[2]!=a['visibility']: raise Reject('E_ASSET_VISIBILITY',a.at('visibility'))
            if match[1]:
                lesson=self.docs.get('lessons/'+match[1]+'/lesson.md')
                if not lesson or not self.in_scope(owner,('lesson',lesson['meta']['id'])): raise Reject('E_ASSET_SCOPE',a.at('ownerId'))
            if (match[3]=='images') != a['mediaType'].startswith('image/'):
                raise Reject('E_ASSET_TYPE',a.at('mediaType'))
            asset_bytes(a,self.files[a['path']])
            self.assets[a['path']]=a

    def in_scope(self, target, owner):
        while target is not None:
            if target == owner: return True
            target=self.parents.get(target)
        return False

    def markdown(self):
        expressions=[]
        for path,d in self.docs.items():
            def resolve(destination,is_image,used,loc):
                if destination in self.docs or destination=='content-package.yaml' or not PurePosixPath(destination).suffix:
                    raise Reject('E_LINK_TARGET')
                if destination not in self.assets: raise Reject('E_ASSET_UNDECLARED')
                a=self.assets[destination]
                if d['student'] and a['visibility']!='student': raise Reject('E_ASSET_VISIBILITY')
                if not self.in_scope(d['target'],(a['ownerKind'],a['ownerId'])): raise Reject('E_ASSET_SCOPE')
                if is_image and not a['mediaType'].startswith('image/'): raise Reject('E_LINK_TYPE')
                if used:
                    self.used.add(destination)
                    self.used_teacher.add(destination)
                    if d['student']: self.used_student.add(destination)
                return './assets/'+a['id']+PurePosixPath(destination).suffix.lower()
            d['body']=process(d['body'],path,d['offset'],resolve,self.diagnostics,expressions)
        from .math_validation import validate
        validate(expressions,self.diagnostics)
        for path,a in self.assets.items():
            if path not in self.used: self.diagnostics.add('W_ASSET_UNUSED',a.at('path'))

    def manifest(self, paths):
        entries=[]
        for path in paths:
            a=self.assets[path]
            entries.append({**{k:a[k] for k in ('id','ownerKind','ownerId','visibility','mediaType')},
                            'sizeBytes':len(self.files[path]),'sha256':digest(self.files[path]),
                            'artifactPath':'assets/'+a['id']+PurePosixPath(path).suffix.lower()})
        return sorted(entries,key=lambda a:a['id'])

    def projections(self):
        def common(d):
            return {**{k:d['meta'][k] for k in ('id','title','source','description','order') if k in d['meta']},'bodyMarkdown':d['body']}
        course=common(self.docs['course.md']);course['lessons']=[]
        for lesson in sorted((d for d in self.docs.values() if d['kind']=='lesson'),key=lambda d:d['meta']['order']):
            lo=common(lesson);lo['problems']=[]
            for problem in sorted((d for d in self.docs.values() if d['kind']=='problem' and self.parents[d['target']]==lesson['target']),key=lambda d:d['meta']['order']):
                po=common(problem)
                po['hints']=[common(h) for h in sorted((h for h in self.docs.values() if h['kind']=='hints' and h['target']==problem['target'] and h['student']),key=lambda h:h['meta']['order'])]
                lo['problems'].append(po)
            course['lessons'].append(lo)
        student=dict(artifactSchema='student-content/v1',contractRevision=REVISION,course=course,assets=self.manifest(self.used_student))
        student['studentProjectionHash']=digest(canonical(student))
        package_hash=digest(canonical(dict(contractRevision=REVISION,files=[dict(path=p,sizeBytes=len(v),sha256=digest(v)) for p,v in sorted(self.files.items())])))
        def sidecars(kind):
            docs=sorted((d for d in self.docs.values() if d['kind']==kind),key=lambda d:({'course':0,'lesson':1,'problem':2}[d['target'][0]],d['target'][1],d['meta']['order']))
            return [{**common(d),**{k:d['meta'][k] for k in ('targetKind','targetId','kind','publication') if k in d['meta']}} for d in docs]
        teacher=dict(artifactSchema='teacher-content/v1',contractRevision=REVISION,packageHash=package_hash,
                     studentProjectionHash=student['studentProjectionHash'],student=student,hints=sidecars('hints'),materials=sidecars('teacher'),assets=self.manifest(self.used_teacher))
        # Delivery bytes are physically separated from closed diagnostics and metadata.
        def bundle(obj,used):
            return dict(json=canonical(obj),assets={e['artifactPath']:self.files[next(p for p in used if self.assets[p]['id']==e['id'])] for e in obj['assets']})
        return student,teacher,bundle(student,self.used_student),bundle(teacher,self.used_teacher)


def compile_package(root, *, package_limit=None, document_limit=1000000, image_limit=10000000,
                    static_limit=50000000, contract_revision=REVISION, source_revision=None, _snapshot_hook=None):
    diagnostics=Diagnostics()
    result=dict(status='invalid',contractRevision=contract_revision,compilerRevision=COMPILER)
    limits=dict(document=document_limit,image=image_limit,static=static_limit,package=package_limit)
    try:
        if contract_revision!=REVISION or any(type(v) is not int or not 0<v<=MAX_INTEGER for v in limits.values()) or (source_revision is not None and not isinstance(source_revision,str)):
            raise Reject('E_CONFIG',failed=True)
        if source_revision is not None:
            try: source_revision.encode('utf-8')
            except UnicodeError: raise Reject('E_CONFIG',failed=True) from None
        # Legacy capability is deliberately separate, never an implicit conversion.
        if not os.path.lexists(os.path.join(root,'content-package.yaml')):
            from .legacy import validate_legacy
            result['legacyValidation']=validate_legacy(root,limits)
            raise Reject('E_REVISION_REQUIRED')
        from .math_validation import validate as validate_math
        validate_math([],diagnostics,configuration_only=True)
        with Snapshot(root,limits,_snapshot_hook) as snapshot:
            # First inspect inventory only; no other file content is opened until
            # declaration locations and per-type limits are known.
            st=os.stat('content-package.yaml',dir_fd=snapshot.fd,follow_symlinks=False)
            if stat.S_ISLNK(st.st_mode) or st.st_nlink!=1: raise Reject('E_FILE_LINK')
            if not stat.S_ISREG(st.st_mode): raise Reject('E_FILE_TYPE')
            from .snapshot import identity
            initial=snapshot.read('content-package.yaml',identity(st),min(document_limit,package_limit))
            try: text=initial.decode('utf-8')
            except UnicodeError: raise Reject('E_YAML') from None
            if text.startswith('\ufeff'): raise Reject('E_YAML')
            inv=parse(text,'content-package.yaml')
            fields(inv,{'schema','courseId','documents','assets'})
            if string(inv,'schema') != 'content-package/v1': raise Reject('E_SCHEMA_UNSUPPORTED',inv.at('schema'))
            string(inv,'courseId',identifier=True)
            if not isinstance(inv['documents'],Sequence) or not inv['documents'] or not isinstance(inv['assets'],Sequence): raise Reject('E_FIELD_TYPE',inv.loc)
            paths={'content-package.yaml':document_limit}
            for p,loc in zip(inv['documents'],inv['documents'].locations):
                if not inventory_path(p): raise Reject('E_PATH',loc)
                if p in paths or not p.endswith('.md'): raise Reject('E_INVENTORY',loc)
                paths[p]=document_limit;snapshot.locations[p]=loc
            for a in inv['assets']:
                fields(a,{'id','path','ownerKind','ownerId','visibility','mediaType'})
                p=string(a,'path')
                if not inventory_path(p): raise Reject('E_PATH',a.at('path'))
                if p in paths: raise Reject('E_ASSET_DUPLICATE' if p not in inv['documents'] else 'E_INVENTORY',a.at('path'))
                if p.endswith(('.md','.yaml')): raise Reject('E_INVENTORY',a.at('path'))
                paths[p]=image_limit if str(a['mediaType']).startswith('image/') else static_limit
                snapshot.locations[p]=a.at('path')
            entries=snapshot.inventory()
            if 'lessons' not in entries or not stat.S_ISDIR(entries['lessons'][2]) or 'course.md' not in paths or 'course.md' not in entries: raise Reject('E_PACKAGE_STRUCTURE',Location('course.md'))
            for path,entry in entries.items():
                if stat.S_ISREG(entry[2]) and path not in paths: raise Reject('E_INVENTORY',Location(path))
                if stat.S_ISDIR(entry[2]) and re.fullmatch(r'lessons/[^/]+',path) and path+'/lesson.md' not in paths: raise Reject('E_PACKAGE_STRUCTURE',Location(path+'/lesson.md'))
            data=snapshot.capture(entries,paths)
            if data['content-package.yaml']!=initial: raise Reject('E_INPUT_CHANGED')
        build=Build(data,inv,diagnostics)
        build.documents();build.asset_definitions();build.markdown()
        if any(d['severity']=='ERROR' for d in diagnostics.items):
            result['diagnostics']=diagnostics.ordered();return result
        student,teacher,sbundle,tbundle=build.projections()
        metadata=dict(contractRevision=REVISION,compilerRevision=COMPILER,packageHash=teacher['packageHash'],studentProjectionHash=student['studentProjectionHash'],teacherArtifactHash=digest(tbundle['json']),limits=limits)
        if source_revision is not None: metadata['sourceRevision']=source_revision
        result.update(status='valid',studentArtifact=sbundle,teacherArtifact=tbundle,buildMetadata=metadata)
    except Reject as e:
        diagnostics.add(e.code,e.location,e.related)
        if e.failed: result['status']='failed'
    except OSError:
        diagnostics.add('E_IO');result['status']='failed'
    except Exception:
        diagnostics.add('E_INTERNAL');result['status']='failed'
    result['diagnostics']=diagnostics.ordered()
    return result
