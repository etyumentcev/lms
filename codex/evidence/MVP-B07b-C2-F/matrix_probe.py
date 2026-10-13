import subprocess,json,uuid,pathlib
report=[]
def run(s):return subprocess.run(['docker','exec','-i','b07c2f-pg','psql','-X','-qAt','-v','ON_ERROR_STOP=1','-U','postgres','-d','c2f'],input='\\set VERBOSITY verbose\n'+s,text=True,capture_output=True)
def query(s):
 p=run(s);assert p.returncode==0,p.stderr;return p.stdout.strip().splitlines()[-1]
org,course,actor=query('''SELECT r.org,r.course,r.owner FROM c2f."ImportAttempt" r JOIN c2f."ProcessingGuard" g ON g.org=r.org WHERE g.compatible AND c2f.authorized(r.org,r.course,r.owner) AND c2f.context_ok(r.org,r.course,r.context,r.context_version,r.binding_from) AND c2f.unrestricted(r.org,r.context) LIMIT 1''').split('|')
states=['Receiving','Validating','Ready','Invalid','Failed','Cancelled','Expired']
allowed={'Receiving':{'Validating','Invalid','Failed','Cancelled','Expired'},'Validating':{'Ready','Invalid','Failed','Cancelled','Expired'},'Ready':{'Failed','Expired'},'Invalid':{'Expired'},'Failed':{'Expired'},'Cancelled':{'Expired'},'Expired':set()}
for source in states:
 for target in states:
  i=str(uuid.uuid4());g=str(uuid.uuid4());desc=lambda keys:json.dumps({k:dict(key=k,length=1,sha256='a'*64,generation=g) for k in keys})
  # Owner DML only to exercise structural transition trigger; semantic checks live in step and are tested in C#.
  def upd(to):return f'''UPDATE c2f."ImportAttempt" SET state='{to}',version=version+1,operation_id=gen_random_uuid(),fingerprint='matrix',source=CASE WHEN '{to}'='Validating' THEN '{desc(['source'])}'::jsonb ELSE source END,result=CASE WHEN '{to}'='Ready' THEN '{desc(['student','teacher','receipt'])}'::jsonb ELSE result END,generation=CASE WHEN '{to}'='Ready' THEN '{g}'::uuid ELSE generation END WHERE id='{i}';'''
  sql=f"BEGIN; SELECT c2f.admit('{i}','{org}','{course}','{actor}',gen_random_uuid(),'matrix');"
  if source in ['Validating','Ready']:sql+=upd('Validating')
  if source not in ['Receiving','Validating']:sql+=upd(source)
  # Target error occurs before COMMIT; transaction rolled back on connection close.
  p=run(sql+upd(target)+'ROLLBACK;');expected=target in allowed[source]
  assert (p.returncode==0)==expected,(source,target,p.stderr)
  if not expected:assert '23514' in p.stderr,p.stderr
  report.append(dict(source=source,target=target,sqlstate='00000' if expected else '23514'))
pathlib.Path('/tmp/b07c2f/matrix-results.json').write_text(json.dumps(report,indent=2));print(f'Structural transition matrix: {len(report)} cells; {sum(x["sqlstate"]=="00000" for x in report)} allowed; all other cells rejected 23514. Each transaction rolled back.')
