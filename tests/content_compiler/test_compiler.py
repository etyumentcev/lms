import copy
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch
import yaml
from PIL import Image
from tools.content_compiler.compiler import compile_package
from tools.content_compiler.legacy import validate_legacy
from tools.content_compiler.model import canonical
from tools.content_compiler.snapshot import Snapshot

FIXTURE=Path(__file__).parent/'fixtures/counting'
H1='materials/hints/pairs-first.md';H2='materials/hints/pairs-second.md'
SOLUTION='materials/teacher/pairs-solution.md';RUBRIC='materials/teacher/pairs-rubric.md'
PROBLEM='lessons/one/problems/pairs.md';LESSON='lessons/one/lesson.md'
PUBLIC='assets/student/files/table.txt';PRIVATE='assets/teacher/files/solution.txt'


class CompilerTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'package';shutil.copytree(FIXTURE,self.root)
    def build(self,**kw): return compile_package(str(self.root),**({'package_limit':100000000}|kw))
    def replace(self,path,old,new):
        p=self.root/path;p.write_text(p.read_text().replace(old,new))
    def body(self,path,text):
        p=self.root/path;s=p.read_text();end=s.index('\n---',4)+4;p.write_text(s[:end]+'\n'+text)
    def inventory(self,mutate):
        p=self.root/'content-package.yaml';m=yaml.safe_load(p.read_text());mutate(m);p.write_text(yaml.safe_dump(m,sort_keys=False,allow_unicode=True))
    def add_document(self,path,content):
        p=self.root/path;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(content)
        self.inventory(lambda m:m['documents'].append(path))
    def add_asset(self,path='assets/student/files/extra.txt',data=b'extra\n',**kw):
        p=self.root/path;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data)
        a=dict(id='extra',path=path,ownerKind='course',ownerId='counting',visibility='student',mediaType='text/plain');a.update(kw)
        self.inventory(lambda m:m['assets'].append(a))
    def denied(self,code,**kw):
        result=self.build(**kw)
        self.assertIn(result['status'],('invalid','failed'),result)
        self.assertIn(code,[d['code'] for d in result['diagnostics']],result)
        self.assertNotIn('studentArtifact',result);self.assertNotIn('teacherArtifact',result);self.assertNotIn('buildMetadata',result)
        self.assertNotIn(str(self.root),json.dumps(result));return result
    def valid(self,**kw):
        r=self.build(**kw);self.assertEqual('valid',r['status'],r);return r

    def test_C01_independent_complete_student_projection(self):
        r=self.valid();s=json.loads(r['studentArtifact']['json']);t=json.loads(r['teacherArtifact']['json'])
        data=b'n | pairs\n'
        # Fixture bytes asserted independently; production projection/helpers are not used.
        self.assertEqual(data,(self.root/PUBLIC).read_bytes())
        expected={'artifactSchema':'student-content/v1','contractRevision':'1.1','course':{
          'id':'counting','title':'Счёт парами','bodyMarkdown':'\nКурс о парах. [Таблица](./assets/shared-table.txt).\n','lessons':[{
          'id':'first-lesson','title':'Пары делителей','order':1,'bodyMarkdown':'\nСравните пары. [Таблица](./assets/shared-table.txt).\n','problems':[{
          'id':'pairs','title':'Делители шести','source':'Synthetic fixture','order':1,'bodyMarkdown':'\nПеречислите положительные делители числа $6$.\n','hints':[{
          'id':'pair-products','title':'Произведения','order':1,'bodyMarkdown':'\nИщите пары чисел с произведением $6$.\n'}]}]}]},
          'assets':[{'id':'shared-table','ownerKind':'course','ownerId':'counting','visibility':'student','mediaType':'text/plain','sizeBytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'artifactPath':'assets/shared-table.txt'}]}
        encoded=json.dumps(expected,sort_keys=True,ensure_ascii=False,separators=(',',':')).encode()
        expected['studentProjectionHash']=hashlib.sha256(encoded).hexdigest()
        self.assertEqual(expected,s);self.assertEqual(s,t['student'])
        self.assertEqual({'assets/shared-table.txt':data},r['studentArtifact']['assets'])
        self.assertEqual(2,len(t['assets']));self.assertEqual(4,len(t['materials']));self.assertEqual(2,len(t['hints']))
        self.assertEqual(hashlib.sha256(r['teacherArtifact']['json']).hexdigest(),r['buildMetadata']['teacherArtifactHash'])
        package={'contractRevision':'1.1','files':[{'path':str(p.relative_to(self.root)),'sizeBytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(self.root.rglob('*')) if p.is_file()]}
        self.assertEqual(hashlib.sha256(json.dumps(package,sort_keys=True,separators=(',',':')).encode()).hexdigest(),t['packageHash'])

    def test_C02_no_sidecars(self):
        for p in (self.root/'materials').rglob('*.md'): p.unlink()
        (self.root/PRIVATE).unlink()
        self.inventory(lambda m:(m.update(documents=[p for p in m['documents'] if not p.startswith('materials/')]),m.update(assets=m['assets'][:1])))
        t=json.loads(self.valid()['teacherArtifact']['json']);self.assertEqual([],t['hints']);self.assertEqual([],t['materials'])

    def test_C03_C07_C19_C21_fields(self):
        cases=[(H1,'publication: with-problem\n','','E_FIELD_REQUIRED'),(H1,'with-problem','after-attempt','E_FIELD_VALUE'),
          (H1,'publication: with-problem','publication: with-problem\nreleaseAfter: 1','E_FIELD_UNKNOWN'),
          (H1,'targetKind: problem','targetKind: lesson','E_TARGET_KIND'),(H1,'targetId: pairs','targetId: absent','E_TARGET_MISSING'),
          (PROBLEM,'id: pairs','id: Pair_1','E_ID_FORMAT'),(PROBLEM,'title: Делители шести','title: null','E_FIELD_TYPE'),
          (PROBLEM,'problem/v1.1','problem/v99','E_SCHEMA_UNSUPPORTED')]
        cases += [(H1,'order: 1','order: '+v,c) for v,c in [('0','E_FIELD_VALUE'),('-1','E_FIELD_VALUE'),('1.5','E_FIELD_TYPE'),('"1"','E_FIELD_TYPE'),('null','E_FIELD_TYPE'),('2147483648','E_FIELD_VALUE')]]
        cases += [(PROBLEM,'order: 1','order: 1\n'+field+': anything','E_FIELD_UNKNOWN') for field in ['solution','rubric','audience','authorComment']]
        for path,old,new,code in cases:
            with self.subTest(path=path,new=new):
                original=(self.root/path).read_bytes();self.replace(path,old,new);self.denied(code);(self.root/path).write_bytes(original)

    def test_C06_C08_C09_C10_hint_order_and_release(self):
        original=(self.root/H2).read_bytes();self.replace(H2,'order: 2','order: 1');self.denied('E_ORDER_DUPLICATE');(self.root/H2).write_bytes(original)
        self.replace(H1,'order: 1','order: 10');self.replace(H2,'order: 2','order: 30');before=self.valid()
        self.replace(H2,'withheld','with-problem');self.denied('E_ASSET_VISIBILITY')
        self.body(H2,'Released synthetic hint.\n');after=self.valid()
        self.assertNotEqual(before['studentArtifact']['json'],after['studentArtifact']['json'])
        self.assertEqual(2,len(json.loads(after['studentArtifact']['json'])['course']['lessons'][0]['problems'][0]['hints']))

    def test_C11_C15_C24_teacher_targets(self):
        for old,new,code in [('targetKind: problem','targetKind: lesson','E_TARGET_KIND'),('targetId: pairs','targetId: elsewhere','E_TARGET_MISSING'),('teacher/v1','problem/v1.1','E_PACKAGE_STRUCTURE')]:
            original=(self.root/SOLUTION).read_bytes();self.replace(SOLUTION,old,new);self.denied(code);(self.root/SOLUTION).write_bytes(original)
        self.add_document('materials/teacher/wrong.md','---\nschema: problem/v1.1\nid: wrong\norder: 9\n---\ntext\n');self.denied('E_PACKAGE_STRUCTURE')

    def test_C12_material_cardinality(self):
        for source,kind in [(SOLUTION,'solution'),(RUBRIC,'rubric')]:
            text=(self.root/source).read_text();m=yaml.safe_load(text.split('---')[1]);self.add_document('materials/teacher/extra.md',text.replace('id: '+m['id'],'id: extra').replace('order: '+str(m['order']),'order: 99'))
            self.denied('E_MATERIAL_DUPLICATE');(self.root/'materials/teacher/extra.md').unlink();self.inventory(lambda m:m['documents'].remove('materials/teacher/extra.md'))

    def test_C13_notes_C14_order(self):
        self.add_document('materials/teacher/extra.md','---\nschema: teacher/v1\nid: extra\ntargetKind: course\ntargetId: counting\nkind: notes\norder: 99\n---\nExtra notes.\n');self.valid()
        self.replace('materials/teacher/extra.md','order: 99','order: 1');self.denied('E_ORDER_DUPLICATE')

    def test_C16_all_namespaces(self):
        for source in [LESSON,PROBLEM,H1,SOLUTION]:
            with self.subTest(source=source):
                if source==LESSON: target='lessons/two/lesson.md'
                elif source==PROBLEM: target='lessons/one/problems/extra.md'
                else: target=str(Path(source).parent/'extra.md')
                self.add_document(target,(self.root/source).read_text());self.denied('E_ID_DUPLICATE');(self.root/target).unlink();self.inventory(lambda m:m['documents'].remove(target))
                if source==LESSON: (self.root/'lessons/two').rmdir()
        self.add_asset(id='shared-table');self.denied('E_ID_DUPLICATE')

    def test_C17_typed_namespaces_C18_cross_lesson(self):
        self.replace(LESSON,'id: first-lesson','id: pairs');self.replace('materials/teacher/lesson-notes.md','targetId: first-lesson','targetId: pairs');self.valid()
        self.add_document('lessons/two/lesson.md','---\nschema: lesson/v1.1\nid: second\ntitle: Second\norder: 2\n---\nText\n')
        self.add_document('lessons/two/problems/extra.md',(self.root/PROBLEM).read_text());self.denied('E_ID_DUPLICATE')

    def test_C20_yaml(self):
        for extra in ['visibility: student\nvisibility: teacher','alias: &a x\nother: *a','thing: !custom value','? [a,b]\n: value','alias: &a {x: x}\nmerged: {<<: *a}']:
            with self.subTest(extra=extra):
                original=(self.root/H1).read_bytes();self.replace(H1,'title: Произведения',extra);self.denied('E_YAML');(self.root/H1).write_bytes(original)

    def test_C22_C23_discovery(self):
        self.add_asset();self.inventory(lambda m:m['assets'].pop());self.denied('E_INVENTORY');(self.root/'assets/student/files/extra.txt').unlink()
        self.inventory(lambda m:m['documents'].append('course.md'));self.denied('E_INVENTORY')

    def test_C22_missing_course_lesson(self):
        for path in ['course.md',LESSON]:
            original=(self.root/path).read_bytes();(self.root/path).unlink();self.inventory(lambda m:m['documents'].remove(path));self.denied('E_PACKAGE_STRUCTURE')
            (self.root/path).write_bytes(original);self.inventory(lambda m:m['documents'].append(path))

    def test_A01_A12_shared_unused(self):
        before=self.valid();self.body(SOLUTION,'[x](../../assets/student/files/table.txt) [again](../../assets/student/files/table.txt)\n')
        after=self.valid();self.assertEqual(before['studentArtifact'],after['studentArtifact'])
        self.add_asset();unused=self.valid();self.assertEqual(after['studentArtifact'],unused['studentArtifact']);self.assertIn('W_ASSET_UNUSED',[d['code'] for d in unused['diagnostics']])
        self.assertNotEqual(after['buildMetadata']['packageHash'],unused['buildMetadata']['packageHash'])

    def test_A02_A23_private_links(self):
        for body in ['[Answer](../../../assets/teacher/files/solution.txt)\n','[unused]: ../../../assets/teacher/files/solution.txt\n','[Answer][ref]\n\n[ref]: ../../../assets/teacher/files/solution.txt\n']:
            self.body(PROBLEM,body);self.denied('E_ASSET_VISIBILITY')

    def test_A03_A08_A10_asset_declarations(self):
        for idx,key,value,code in [(1,'visibility','student','E_ASSET_VISIBILITY'),(0,'ownerId','missing','E_TARGET_MISSING'),(0,'ownerKind','organization','E_FIELD_VALUE'),(0,'ownerKind','lesson','E_TARGET_MISSING')]:
            original=(self.root/'content-package.yaml').read_bytes();self.inventory(lambda m:m['assets'][idx].update({key:value}));self.denied(code);(self.root/'content-package.yaml').write_bytes(original)
        self.inventory(lambda m:m['assets'][0].update(ownerKind='lesson',ownerId='first-lesson'));self.denied('E_ASSET_SCOPE')

    def test_A04_A05_A06_A07_missing_duplicate(self):
        original=(self.root/PRIVATE).read_bytes();(self.root/PRIVATE).unlink();r=self.denied('E_FILE_MISSING');self.assertEqual('content-package.yaml',r['diagnostics'][0]['file']);(self.root/PRIVATE).write_bytes(original)
        self.body(PROBLEM,'[x](./absent.txt)\n');self.denied('E_ASSET_UNDECLARED')
        self.inventory(lambda m:m['assets'].append(copy.deepcopy(m['assets'][0])));self.denied('E_ASSET_DUPLICATE')

    def test_A09_problem_owner_A11_local_owner(self):
        self.inventory(lambda m:m['assets'][0].update(ownerKind='problem',ownerId='pairs'));self.denied('E_ASSET_SCOPE')
        self.inventory(lambda m:m['assets'][0].update(ownerKind='course',ownerId='counting'))
        self.add_asset('lessons/one/assets/student/files/extra.txt');self.denied('E_ASSET_SCOPE')

    def test_A14_A17_paths(self):
        for url in ['../../../../etc/passwd','/etc/passwd','C:\\secret','\\\\host\\secret','%2e%2e/x','%252e/x','foo%2fbar','foo\\bar','foo?x','foo#x','#fragment','../package/assets/student/files/table.txt']:
            with self.subTest(url=url):
                self.body('course.md','[x]('+url+')\n');self.denied('E_PATH')

    def test_A18_symlinks_and_zero_target_reads(self):
        secret=Path(self.temp.name)/'secret';secret.write_bytes(b'NEVER READ')
        for target in [secret,self.root/PUBLIC,Path(self.temp.name)/'absent']:
            with self.subTest(target=target):
                p=self.root/PRIVATE;original=p.read_bytes();p.unlink();p.symlink_to(target)
                reads=[];real=os.read
                def read(fd,n): reads.append(os.readlink('/proc/self/fd/'+str(fd)));return real(fd,n)
                with patch('tools.content_compiler.snapshot.os.read',side_effect=read): self.denied('E_FILE_LINK')
                self.assertNotIn(str(secret),reads);self.assertNotIn(str(self.root/PUBLIC),reads)
                p.unlink();p.write_bytes(original)
        alias=Path(self.temp.name)/'alias';alias.symlink_to(self.root,target_is_directory=True)
        self.assertEqual('E_FILE_LINK',compile_package(str(alias),package_limit=100000000)['diagnostics'][0]['code'])
        directory=self.root/'linked';directory.symlink_to(self.root/'assets',target_is_directory=True);self.denied('E_FILE_LINK')

    def test_A19_hardlink_fifo(self):
        p=self.root/'assets/student/files/extra.txt';os.link(self.root/PUBLIC,p);self.denied('E_FILE_LINK');p.unlink()
        os.mkfifo(p);self.denied('E_FILE_TYPE')

    def test_A20_case_collision(self):
        self.add_asset('assets/student/files/Table.txt');self.denied('E_PATH_COLLISION')

    def test_A21_swap_during_read_zero_external_reads(self):
        secret=Path(self.temp.name)/'secret';secret.write_bytes(b'NEVER READ')
        changed=False;reads=[];real=os.read
        def hook(event,path):
            nonlocal changed
            if event=='before_open' and path==PRIVATE and not changed:
                changed=True;(self.root/PRIVATE).unlink();(self.root/PRIVATE).symlink_to(secret)
        def read(fd,n): reads.append(os.readlink('/proc/self/fd/'+str(fd)));return real(fd,n)
        with patch('tools.content_compiler.snapshot.os.read',side_effect=read): self.denied('E_INPUT_CHANGED',_snapshot_hook=hook)
        self.assertNotIn(str(secret),reads)

    def test_A21_mutation_and_addition(self):
        for event_to_change in ['after_read','before_verify']:
            original=(self.root/PRIVATE).read_bytes();changed=False
            def hook(event,path):
                nonlocal changed
                if event==event_to_change and not changed and (event=='before_verify' or path==PRIVATE):
                    changed=True;(self.root/PRIVATE).write_bytes(original+b'changed')
            self.denied('E_INPUT_CHANGED',_snapshot_hook=hook);(self.root/PRIVATE).write_bytes(original)

    def test_A22_document_links(self):
        for url in ['../../../'+SOLUTION,'../../../content-package.yaml','../lesson.md']:
            self.body(PROBLEM,'[x]('+url+')\n');self.denied('E_LINK_TARGET')

    def test_A24_code_is_not_dependency_and_AST_rewrite(self):
        self.body(PROBLEM,'`[x](missing.pdf)`\n\n```md\n![secret](../../../assets/teacher/files/solution.txt)\n```\n\n[x][ref]\n\n[ref]: ../../../assets/student/files/table.txt "Title"\n')
        r=self.valid();text=json.loads(r['studentArtifact']['json'])['course']['lessons'][0]['problems'][0]['bodyMarkdown']
        self.assertIn('`[x](missing.pdf)`',text);self.assertIn('[ref]: ./assets/shared-table.txt "Title"',text)
        self.assertEqual(1,len(r['studentArtifact']['assets']))

    def test_A25_A26_URIs(self):
        for url in ['javascript:alert(1)','data:text/plain,secret','attachment:abc','lms:problem','http://example.test','//example.test','https://user:pass@example.test']:
            with self.subTest(url=url): self.body(PROBLEM,'[x]('+url+')\n');self.denied('E_URI')
        self.body(PROBLEM,'![x](https://example.test/a.png)\n');self.denied('E_EXTERNAL_IMAGE')
        self.body(PROBLEM,'https://example.test/video\n');self.assertIn('W_VIDEO_PROVIDER',[d['code'] for d in self.valid()['diagnostics']])

    def test_A27_HTML_include(self):
        for value in ['<iframe src="x"></iframe>','<img src="x" onerror="evil()">','text <b>html</b>','!include secret','{% include secret %}']:
            self.body(SOLUTION,value+'\n');self.denied('E_MARKDOWN')

    def test_A28_A29_bad_assets(self):
        for path,data,mime in [('x.svg',b'<svg/>','image/svg+xml'),('x.exe',b'abc','application/octet-stream'),('x.png',b'\x89PNG\r\n\x1a\nBAD','image/png'),('x.pdf',b'%PDF-1.7\n/OpenAction /JavaScript','application/pdf'),('x.txt',b'\xff','text/plain')]:
            with self.subTest(path=path):
                target='assets/student/'+('images/' if mime.startswith('image/') else 'files/')+path
                self.add_asset(target,data,mediaType=mime);self.denied('E_ASSET_TYPE');(self.root/target).unlink();self.inventory(lambda m:m['assets'].pop())

    def test_A30_reference_image_to_text(self):
        for body in ['![x](../../../assets/student/files/table.txt)\n','![x][ref]\n\n[ref]: ../../../assets/student/files/table.txt\n']:
            self.body(PROBLEM,body);self.denied('E_LINK_TYPE')

    def test_S01_S05_private_noninterference(self):
        before=self.valid()['studentArtifact']
        for path in [SOLUTION,RUBRIC,H2,PRIVATE,'materials/teacher/course-notes.md']:
            with (self.root/path).open('ab') as f:f.write(b'\nPRIVATE-CANARY-123\n')
            r=self.valid();self.assertEqual(before,r['studentArtifact']);self.assertNotIn(b'PRIVATE-CANARY',r['studentArtifact']['json'])
        self.add_asset();self.body(SOLUTION,'[extra](../../assets/student/files/extra.txt)\n')
        self.assertEqual(before,self.valid()['studentArtifact'])

    def test_S07_S10_determinism_provenance_roots(self):
        r=self.valid(source_revision='one');again=self.valid(source_revision='two')
        self.assertEqual(r['studentArtifact'],again['studentArtifact']);self.assertEqual(r['teacherArtifact'],again['teacherArtifact'])
        other=Path(self.temp.name)/'other';shutil.copytree(self.root,other)
        for p in other.rglob('*'):os.utime(p,(1000,1000))
        with patch.dict(os.environ,{'LC_ALL':'tr_TR.UTF-8'}):
            twin=compile_package(str(other),package_limit=100000000,source_revision='one')
        self.assertEqual(r,twin)

    def test_S08_S09_public_changes_and_source_whitespace(self):
        before=self.valid();self.replace('course.md','title: Счёт парами','title:  Счёт парами');whitespace=self.valid()
        self.assertEqual(before['studentArtifact'],whitespace['studentArtifact']);self.assertNotEqual(before['buildMetadata']['packageHash'],whitespace['buildMetadata']['packageHash'])
        self.replace(PUBLIC,'pairs','changed');changed=self.valid();self.assertNotEqual(before['studentArtifact'],changed['studentArtifact'])

    def test_L01_L02_limits(self):
        total=sum(p.stat().st_size for p in self.root.rglob('*') if p.is_file());maximum=max(p.stat().st_size for p in self.root.rglob('*') if p.suffix in ('.md','.yaml'))
        self.valid(package_limit=total,document_limit=maximum)
        self.denied('E_LIMIT',package_limit=total-1);self.denied('E_LIMIT',document_limit=maximum-1)
        static_max=max((self.root/PUBLIC).stat().st_size,(self.root/PRIVATE).stat().st_size)
        self.valid(static_limit=static_max);self.denied('E_LIMIT',static_limit=static_max-1)
        self.add_asset(data=b'X'*2000);self.denied('E_LIMIT',static_limit=1999)

    def test_L01_image_structure_and_limit(self):
        for extension,fmt,mime in [('png','PNG','image/png'),('jpeg','JPEG','image/jpeg'),('webp','WEBP','image/webp')]:
            stream=io.BytesIO();Image.new('RGB',(2,2),'white').save(stream,format=fmt);data=stream.getvalue()
            target='assets/student/images/x.'+extension
            self.add_asset(target,data,mediaType=mime);self.body(PROBLEM,'![image](../../../'+target+')\n')
            r=self.valid(image_limit=len(data));self.assertEqual(data,r['studentArtifact']['assets']['assets/extra.'+extension])
            self.denied('E_LIMIT',image_limit=len(data)-1);(self.root/target).unlink();self.inventory(lambda m:m['assets'].pop())

    def test_L03_bad_config_no_reads(self):
        for value in [None,0,-1,1.5,'100',True,9007199254740992]:
            with patch.object(Snapshot,'__enter__',side_effect=AssertionError('must not open')):
                r=self.denied('E_CONFIG',package_limit=value);self.assertEqual('failed',r['status'])

    def test_L04_sanitized_failures(self):
        for exception,code in [(PermissionError('/secret/path'),'E_IO'),(RuntimeError('secret token'),'E_INTERNAL')]:
            with patch.object(Snapshot,'capture',side_effect=exception):
                r=self.denied(code);self.assertEqual('failed',r['status']);self.assertNotIn('secret',json.dumps(r))

    def test_D01_coordinates_and_related(self):
        (self.root/H1).write_bytes(b'---\r\nschema: hints/v1\r\nid: pair-products\r\ntargetKind: problem\r\ntargetId: pairs\r\norder: 1\r\npublication: after-attempt\r\n---\r\nHint\r\n')
        r=self.denied('E_FIELD_VALUE');d=r['diagnostics'][0];self.assertEqual((H1,7,14),(d['file'],d['line'],d['column']))
        (self.root/H1).write_bytes((FIXTURE/H1).read_bytes());self.body(PROBLEM,'αβ [Ответ](../../../assets/teacher/files/solution.txt)\n')
        d=next(d for d in self.denied('E_ASSET_VISIBILITY')['diagnostics'] if d['code']=='E_ASSET_VISIBILITY');self.assertEqual(12,d['column'])
        self.replace(H1,'publication: with-problem','publication: with-problem\npublication: withheld')
        d=self.denied('E_YAML')['diagnostics'][0];self.assertEqual(8,d['line']);self.assertEqual(7,d['related'][0]['line'])

    def test_legacy_compatibility(self):
        for p in list((self.root/'materials').rglob('*.md')):p.unlink()
        (self.root/'content-package.yaml').unlink()
        for path in ['course.md',LESSON,PROBLEM]:self.replace(path,'/v1.1','/v1')
        self.replace(PROBLEM,'order: 1','order: 1\nauthorComment: note\nsolution: not-interpreted\naudience: teacher')
        self.body(PROBLEM,'[lesson](../lesson.md) `missing.pdf`\n')
        alias=self.root/'assets/student/files/alias.txt';alias.symlink_to(self.root/PUBLIC)
        v=validate_legacy(self.root);self.assertEqual('valid',v['status'],v);self.assertEqual(3,len(v['diagnostics']))
        r=self.denied('E_REVISION_REQUIRED');self.assertEqual('valid',r['legacyValidation']['status'])
        self.add_legacy_sidecar();self.assertEqual('invalid',validate_legacy(self.root)['status'])

    def add_legacy_sidecar(self):
        (self.root/'materials/hints/extra.md').write_text('---\nschema: hints/v1\nid: extra\n---\nHint\n')

    def test_legacy_examples_unchanged(self):
        root=Path(__file__).resolve().parents[2]/'examples/course'
        before={str(p):p.read_bytes() for p in root.rglob('*') if p.is_file()}
        r=validate_legacy(root);self.assertEqual('valid',r['status'],r)
        self.assertEqual(before,{str(p):p.read_bytes() for p in root.rglob('*') if p.is_file()})

    def test_revision_mix_no_fallback(self):
        self.replace(PROBLEM,'problem/v1.1','problem/v1');self.denied('E_SCHEMA_MIX')
        (self.root/'content-package.yaml').write_text('bad: [');r=self.denied('E_YAML');self.assertNotIn('legacyValidation',r)


if __name__=='__main__':unittest.main()
