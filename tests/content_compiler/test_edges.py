import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import yaml
import test_compiler as fixture_helpers
from test_compiler import H1, H2, PROBLEM, LESSON, SOLUTION, PRIVATE, PUBLIC
from tools.content_compiler.compiler import compile_package
from tools.content_compiler.legacy import validate_legacy
from tools.content_compiler.markdown import process
from tools.content_compiler.model import Diagnostics, canonical

# Reuse fixture helpers without inheriting the suite a second time.
class EdgeTests(unittest.TestCase):
    setUp=fixture_helpers.CompilerTests.setUp
    build=fixture_helpers.CompilerTests.build
    replace=fixture_helpers.CompilerTests.replace
    body=fixture_helpers.CompilerTests.body
    inventory=fixture_helpers.CompilerTests.inventory
    add_document=fixture_helpers.CompilerTests.add_document
    add_asset=fixture_helpers.CompilerTests.add_asset
    denied=fixture_helpers.CompilerTests.denied
    valid=fixture_helpers.CompilerTests.valid

    def test_C14_solution_rubric_share_order_scope(self):
        self.replace('materials/teacher/pairs-rubric.md','order: 2','order: 1');self.denied('E_ORDER_DUPLICATE')

    def test_S02_private_metadata_add_remove_noninterference(self):
        before=self.valid()['studentArtifact']
        self.replace(H2,'id: private-pair-check','id: renamed-private')
        self.replace(H2,'order: 2','order: 99')
        self.replace(H2,'publication: withheld','publication: withheld\ntitle: Private canary')
        self.assertEqual(before,self.valid()['studentArtifact'])
        (self.root/H2).unlink();self.inventory(lambda m:m['documents'].remove(H2))
        self.assertEqual(before,self.valid()['studentArtifact'])
        self.add_document('materials/teacher/extra.md','---\nschema: teacher/v1\nid: private-extra\ntargetKind: problem\ntargetId: pairs\nkind: notes\norder: 99\n---\nPRIVATE EXTRA\n')
        self.assertEqual(before,self.valid()['studentArtifact'])

    def test_S13_classification_not_semantic_censorship(self):
        self.body(PROBLEM,'AUTHOR-DECLARED-STUDENT-CANARY\n')
        self.assertIn(b'AUTHOR-DECLARED-STUDENT-CANARY',self.valid()['studentArtifact']['json'])

    def test_A28_mime_mismatch_and_passive_PDF_denied(self):
        self.inventory(lambda m:m['assets'][0].update(mediaType='application/pdf'));self.denied('E_ASSET_TYPE')
        self.inventory(lambda m:m['assets'][0].update(mediaType='text/plain'))
        self.add_asset('assets/student/files/passive.pdf',b'%PDF-1.7\n%%EOF\n',mediaType='application/pdf');self.denied('E_ASSET_TYPE')

    def test_AST_escaped_table_pipe_preserved(self):
        body='| a | b |\n|---|---|\n|[a\\|b](../../../assets/student/files/table.txt)|z|\n'
        self.body(PROBLEM,body);r=self.valid();actual=json.loads(r['studentArtifact']['json'])['course']['lessons'][0]['problems'][0]['bodyMarkdown']
        self.assertEqual(body.replace('../../../assets/student/files/table.txt','./assets/shared-table.txt'),actual)

    def test_D01_duplicate_id_related_declaration(self):
        self.add_document('materials/hints/z.md',(self.root/H1).read_text())
        d=self.denied('E_ID_DUPLICATE')['diagnostics'][0]
        self.assertEqual(('materials/hints/z.md',3,5),(d['file'],d['line'],d['column']))
        self.assertEqual([{'file':H1,'line':3,'column':5}],d['related'])

    def test_inventory_malformed_and_ambiguous_types(self):
        original=(self.root/'content-package.yaml').read_bytes()
        values=['[]','null','schema: content-package/v1\n---\nother: x','schema: x\nschema: y',
                'schema: content-package/v1\ncourseId: counting\ndocuments: null\nassets: []',
                '\ufeff'+original.decode()]
        for text in values:
            with self.subTest(text=text[:30]):
                (self.root/'content-package.yaml').write_text(text)
                r=self.build();self.assertEqual('invalid',r['status'],r);self.assertNotIn('studentArtifact',r)
        (self.root/'content-package.yaml').write_bytes(original)
        self.inventory(lambda m:m.update(schema='future/v2'));self.denied('E_SCHEMA_UNSUPPORTED')

    def test_inventory_paths_unknown_duplicate_self_reference(self):
        original=(self.root/'content-package.yaml').read_bytes()
        for value in ['../outside.md','./course.md','/course.md','x//y.md','x/%41.md','x/ä.md','x\\y.md','x/../y.md']:
            self.inventory(lambda m:m['documents'].append(value));self.denied('E_PATH');(self.root/'content-package.yaml').write_bytes(original)
        self.inventory(lambda m:m['documents'].append('content-package.yaml'));self.denied('E_INVENTORY')
        (self.root/'content-package.yaml').write_bytes(original)
        self.inventory(lambda m:m['assets'][0].update(path='course.md'));self.denied('E_INVENTORY')
        (self.root/'content-package.yaml').write_bytes(original)
        self.inventory(lambda m:m.update(extra=1));self.denied('E_FIELD_UNKNOWN')

    def test_target_null_wrong_types_empty_bodies_and_NFC(self):
        for path,old,new,code in [(H1,'targetId: pairs','targetId: null','E_FIELD_TYPE'),(H1,'targetKind: problem','targetKind: nonsense','E_FIELD_VALUE'),
          ('course.md','title: Счёт парами','title: ""','E_FIELD_VALUE'),('course.md','title: Счёт парами','title: [x]','E_FIELD_TYPE'),
          ('course.md','title: Счёт парами','title: e\u0301','E_FIELD_VALUE')]:
            original=(self.root/path).read_bytes();self.replace(path,old,new);self.denied(code);(self.root/path).write_bytes(original)
        self.body(H2,'  \n');self.denied('E_FIELD_VALUE')

    def test_scope_sibling_denial_and_positive_controls(self):
        self.add_document('lessons/one/problems/other.md','---\nschema: problem/v1.1\nid: other\norder: 2\n---\nText\n')
        self.add_asset(ownerKind='problem',ownerId='pairs')
        self.body(PROBLEM,'[x](../../../assets/student/files/extra.txt)\n');self.valid()
        self.body('lessons/one/problems/other.md','[x](../../../assets/student/files/extra.txt)\n');self.denied('E_ASSET_SCOPE')
        self.inventory(lambda m:m['assets'][-1].update(ownerKind='lesson',ownerId='first-lesson'));self.valid()
        self.add_document('lessons/two/lesson.md','---\nschema: lesson/v1.1\nid: second\ntitle: Second\norder: 2\n---\n[x](../../assets/student/files/extra.txt)\n');self.denied('E_ASSET_SCOPE')
        self.inventory(lambda m:m['assets'][-1].update(ownerKind='course',ownerId='counting'));self.valid()

    def test_lesson_local_positive_and_wrong_lesson(self):
        self.add_asset('lessons/one/assets/student/files/extra.txt',ownerKind='problem',ownerId='pairs')
        self.body(PROBLEM,'[x](../assets/student/files/extra.txt)\n');self.valid()
        self.add_document('lessons/two/lesson.md','---\nschema: lesson/v1.1\nid: second\ntitle: Second\norder: 2\n---\nText\n')
        self.inventory(lambda m:m['assets'][-1].update(ownerKind='lesson',ownerId='second'));self.denied('E_ASSET_SCOPE')

    def test_document_layout_and_missing_lessons(self):
        self.add_document('loose.md','---\nschema: problem/v1.1\nid: loose\norder: 9\n---\nText\n');self.denied('E_PACKAGE_STRUCTURE')
        (self.root/'loose.md').unlink();self.inventory(lambda m:m['documents'].remove('loose.md'))
        shutil.rmtree(self.root/'lessons');self.denied('E_PACKAGE_STRUCTURE')

    def test_socket_rejected_before_read(self):
        sock=socket.socket(socket.AF_UNIX);self.addCleanup(sock.close)
        sock.bind(str(self.root/'socket'));self.denied('E_FILE_TYPE')

    def test_directory_substitution_before_open(self):
        changed=False;external=Path(self.temp.name)/'external';external.mkdir();(external/'solution.txt').write_text('NEVER READ')
        def hook(event,path):
            nonlocal changed
            if event=='before_open' and path==PRIVATE and not changed:
                changed=True;directory=self.root/'assets/teacher/files';directory.rename(self.root/'assets/teacher/original');directory.symlink_to(external,target_is_directory=True)
        reads=[];read=os.read
        def observe(fd,n):reads.append(os.readlink('/proc/self/fd/'+str(fd)));return read(fd,n)
        with patch('tools.content_compiler.snapshot.os.read',side_effect=observe):self.denied('E_INPUT_CHANGED',_snapshot_hook=hook)
        self.assertNotIn(str(external/'solution.txt'),reads)

    def test_added_file_during_snapshot(self):
        changed=False
        def hook(event,path):
            nonlocal changed
            if event=='before_verify' and not changed:changed=True;(self.root/'new.txt').write_text('New')
        self.denied('E_INPUT_CHANGED',_snapshot_hook=hook)

    def test_AST_tables_repeated_cells_lists_quotes_labels_titles(self):
        body='| a | b |\n|---|---|\n|[x](../../../assets/student/files/table.txt)|[x](../../../assets/student/files/table.txt)|\n\n> - **[label](<../../../assets/student/files/table.txt> "title")**\n\n[ref]: ../../../assets/student/files/table.txt "ref title"\n\n[ref] and [ref][]\n'
        self.body(PROBLEM,body);r=self.valid();result=json.loads(r['studentArtifact']['json'])['course']['lessons'][0]['problems'][0]['bodyMarkdown']
        self.assertEqual(body.replace('../../../assets/student/files/table.txt','./assets/shared-table.txt'),result)

    def test_AST_invalid_unused_duplicate_definitions_and_NUL(self):
        for body in ['[ref]: ../../../assets/student/files/table.txt\n\n[ref]: ../../../assets/teacher/files/solution.txt\n', '[x](../../../assets/student/files/ta\x00ble.txt)\n']:
            self.body(PROBLEM,body);self.denied('E_ASSET_VISIBILITY' if '\x00' not in body else 'E_PATH')

    def test_AST_nested_image_alt_source_maps(self):
        body='![text [x](./a.txt)](./b.png "title")\n'
        d=Diagnostics();out=process(body,'course.md',0,lambda p,i,u,l:'./assets/'+p,d)
        self.assertEqual('![text [x](./assets/a.txt)](./assets/b.png "title")\n',out)
        self.assertEqual([],d.items)

    def test_external_autolinks_and_unused_refs(self):
        for text,code in [('<https://user@example.test>','E_URI'),('<ftp://example.test>','E_URI'),('<a@example.test>','E_URI'),('[r]: javascript:alert(1)','E_URI')]:
            self.body(PROBLEM,text+'\n');self.denied(code)
        self.body(PROBLEM,'<https://example.test/path>\n');self.valid()

    def test_D02_warning_sort_and_repeated_compilation(self):
        self.add_asset();self.body(PROBLEM,'https://example.test/video\n');a=self.valid();b=self.valid()
        self.assertEqual(a,b);self.assertEqual(['W_ASSET_UNUSED','W_VIDEO_PROVIDER'],[d['code'] for d in a['diagnostics']])

    def test_S06_bad_unused_teacher_asset_blocks_both(self):
        (self.root/PRIVATE).write_bytes(b'\xff');self.denied('E_ASSET_TYPE')
        self.body(H2,'private text\n');self.body(SOLUTION,'private text\n');self.denied('E_ASSET_TYPE')

    def test_S09_rename_and_CRLF_noninterference(self):
        before=self.valid();new='assets/student/files/renamed.txt';(self.root/PUBLIC).rename(self.root/new)
        self.inventory(lambda m:m['assets'][0].update(path=new))
        for p in self.root.rglob('*.md'):p.write_text(p.read_text().replace('assets/student/files/table.txt',new))
        after=self.valid();self.assertEqual(before['studentArtifact'],after['studentArtifact']);self.assertNotEqual(before['teacherArtifact'],after['teacherArtifact'])
        for p in self.root.rglob('*.md'):p.write_bytes(p.read_bytes().replace(b'\n',b'\r\n'))
        self.assertEqual(after['studentArtifact'],self.valid()['studentArtifact'])

    def test_legacy_coursewide_problem_duplicate_warns(self):
        shutil.rmtree(self.root/'materials');(self.root/'content-package.yaml').unlink()
        for path in ['course.md',LESSON,PROBLEM]:self.replace(path,'/v1.1','/v1')
        folder=self.root/'lessons/two/problems';folder.mkdir(parents=True)
        (folder.parent/'lesson.md').write_text('---\nschema: lesson/v1\nid: other\ntitle: Other\norder: 2\n---\nText\n')
        (folder/'duplicate.md').write_bytes((self.root/PROBLEM).read_bytes())
        result=validate_legacy(self.root);self.assertEqual('valid',result['status']);self.assertIn('W_ID_DUPLICATE',[d['code'] for d in result['diagnostics']])
        self.replace(PROBLEM,'problem/v1','problem/v1.1');self.assertEqual('invalid',validate_legacy(self.root)['status'])

    def test_JCS_restricted_domain_vectors(self):
        self.assertEqual(b'{"a":1,"b":"\\b\\t\\n\\f\\r\\u0000\\\"\\\\/"}',canonical({'b':'\b\t\n\f\r\x00"\\/','a':1}))
        self.assertEqual('{"a":"é😀"}'.encode(),canonical({'a':'é😀'}))
        for value in [float('nan'),1.5,9007199254740992,{'nonasciié':1},'\ud800']:
            with self.assertRaises((ValueError,UnicodeError)):canonical(value)

    def test_cli_write_failure_has_no_partial_output(self):
        from tools.content_compiler.__main__ import main
        output=Path(self.temp.name)/'write-failure'
        import io
        with patch.object(sys,'argv',['compiler',str(self.root),'--package-limit','100000000','--output',str(output)]), patch('tools.content_compiler.__main__.Path.write_bytes',side_effect=OSError('PRIVATE PATH')), patch('sys.stdout',new_callable=io.StringIO) as stdout:
            self.assertEqual(1,main())
        result=json.loads(stdout.getvalue());self.assertEqual('failed',result['status']);self.assertEqual('E_IO',result['diagnostics'][0]['code'])
        self.assertNotIn('PRIVATE PATH',stdout.getvalue());self.assertNotIn('buildMetadata',result)
        self.assertFalse(output.exists());self.assertEqual([],list(Path(self.temp.name).glob('.content-build-*')))

    def test_cli_atomic_success_failure_and_no_overwrite(self):
        output=Path(self.temp.name)/'output'
        command=[sys.executable,'-m','tools.content_compiler',str(self.root),'--package-limit','100000000','--output',str(output)]
        p=subprocess.run(command,capture_output=True,text=True);self.assertEqual(0,p.returncode,p.stderr+p.stdout)
        before={str(p.relative_to(output)):p.read_bytes() for p in output.rglob('*') if p.is_file()}
        self.assertEqual({'content.json','assets/shared-table.txt'}, {str(p.relative_to(output/'student')) for p in (output/'student').rglob('*') if p.is_file()})
        p=subprocess.run(command,capture_output=True,text=True);self.assertNotEqual(0,p.returncode)
        self.assertEqual(before,{str(p.relative_to(output)):p.read_bytes() for p in output.rglob('*') if p.is_file()})
        self.body(SOLUTION,'<script>bad()</script>\n');bad=Path(self.temp.name)/'bad'
        p=subprocess.run(command[:-1]+[str(bad)],capture_output=True,text=True);self.assertEqual(1,p.returncode);self.assertFalse(bad.exists())
        self.assertEqual([],list(Path(self.temp.name).glob('.content-build-*')))

if __name__=='__main__':unittest.main()
