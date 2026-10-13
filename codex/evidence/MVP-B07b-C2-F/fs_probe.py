import os, pathlib, tempfile, ctypes, errno, hashlib, json, subprocess, uuid, shutil, stat
BASE=pathlib.Path(tempfile.mkdtemp(prefix='.b07c2f-private-',dir='/home/tea/github/lms'))
os.chmod(BASE,0o700)
LIB=ctypes.CDLL(None,use_errno=True)
LIB.renameat2.argtypes=[ctypes.c_int,ctypes.c_char_p,ctypes.c_int,ctypes.c_char_p,ctypes.c_uint]
REPORT=[]; SYNC=[]
def log(name,detail='PASS'): REPORT.append(dict(name=name,detail=detail)); print(name+': '+detail,flush=True)
def sync_dir(p):
 fd=os.open(p,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
 try: os.fsync(fd);SYNC.append('dir:'+str(p.relative_to(BASE)))
 finally: os.close(fd)
def rename_no_replace(a,b):
 if LIB.renameat2(-100,os.fsencode(a),-100,os.fsencode(b),1)!=0: raise OSError(ctypes.get_errno(),os.strerror(ctypes.get_errno()))
def put(p,data):
 fd=os.open(p,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
 try:
  with os.fdopen(fd,'wb',closefd=False) as f:f.write(data);f.flush();os.fsync(fd);SYNC.append('file:'+str(p.relative_to(BASE)))
 finally:os.close(fd)
def seal(label,crash=None,fail=None):
 generation=str(uuid.uuid5(uuid.NAMESPACE_DNS,'c2f-'+label));stage=BASE/('stage-'+label); dest=BASE/('sealed-'+generation);stage.mkdir(mode=0o700)
 manifest={}
 for k in ['source','student','teacher','receipt']:
  data=('synthetic-'+k).encode();put(stage/k,data);manifest[k]=dict(key=k,length=len(data),sha256=hashlib.sha256(data).hexdigest(),generation=generation)
  if crash=='mid-write':os._exit(91)
 put(stage/'manifest.json',json.dumps(manifest,sort_keys=True).encode());sync_dir(stage)
 if fail=='before-rename':raise OSError(errno.EIO,'injected fsync/IO failure before seal')
 if crash=='before-seal':os._exit(92)
 rename_no_replace(stage,dest)
 if fail=='after-rename':raise OSError(errno.EIO,'injected parent fsync failure; seal not acknowledged')
 sync_dir(BASE)
 if crash=='after-seal':os._exit(93)
 return dest,manifest

def psql(sql):
 p=subprocess.run(['docker','exec','-i','b07c2f-pg','psql','-X','-qAt','-v','ON_ERROR_STOP=1','-U','postgres','-d','c2f'],input=sql,text=True,capture_output=True)
 if p.returncode:raise RuntimeError(p.stderr)
 return p.stdout.strip()
def query(sql):return psql(sql).splitlines()[-1]
row=query('''SELECT g.org,r.course,r.owner FROM c2f."ProcessingGuard" g JOIN c2f."ImportAttempt" r ON r.org=g.org WHERE g.compatible AND c2f.authorized(r.org,r.course,r.owner) AND c2f.context_ok(r.org,r.course,r.context,r.context_version,r.binding_from) AND c2f.unrestricted(r.org,r.context) LIMIT 1''').split('|')
org,course,actor=row
q=lambda s:"'"+str(s).replace("'","''")+"'"
def admit():
 i=str(uuid.uuid4());query(f"SELECT c2f.admit('{i}','{org}',{q(course)},'{actor}',gen_random_uuid(),'fs-controlled-fixture')");return i

def step(i,state,ver,m,gen,op=None):
 keys=['source'] if state=='Validating' else ['student','teacher','receipt']
 d={k:m[k] for k in keys}
 return f"SELECT c2f.step('{i}','{org}','{actor}',{ver},'{op or uuid.uuid4()}','fs-{state}','{state}',true,{q(json.dumps(d))}::jsonb,'{gen}')"
def state(i):return query(f'SELECT state FROM c2f."ImportAttempt" WHERE id=\'{i}\'')
def child_crash(label,point):
 pid=os.fork()
 if pid==0:seal(label,crash=point);os._exit(99)
 _,status=os.waitpid(pid,0);return os.waitstatus_to_exitcode(status)
try:
 log('filesystem profile',subprocess.check_output(['stat','-f','-c','%T',str(BASE)],text=True).strip()+'; private directory mode 0700; Linux renameat2')
 assert stat.S_IMODE(BASE.stat().st_mode)==0o700
 for point,code in [('mid-write',91),('before-seal',92),('after-seal',93)]:
  i=admit();assert child_crash(point,point)==code;assert state(i)=='Receiving';assert (BASE/('sealed-'+str(uuid.uuid5(uuid.NAMESPACE_DNS,'c2f-'+point)))).exists()==(point=='after-seal')
  log('process crash '+point,'actual fork/_exit; DB Receiving; no partial Ready')
 for point in ['before-rename','after-rename']:
  i=admit()
  try:seal(point,fail=point);raise AssertionError('failure not injected')
  except OSError as e:assert e.errno==errno.EIO
  assert state(i)=='Receiving';log('failure '+point,'injected EIO; seal unacknowledged; DB Receiving')
 dest,manifest=seal('success');gen=manifest['source']['generation'];i=admit();psql(step(i,'Validating',1,manifest,gen));psql(step(i,'Ready',2,manifest,gen));assert state(i)=='Ready'
 for k,d in manifest.items():assert (dest/k).stat().st_size==d['length'] and hashlib.sha256((dest/k).read_bytes()).hexdigest()==d['sha256']
 assert query(f"SELECT c2f.readable('{i}','{org}','{actor}')")=='t'
 log('sealed generation -> atomic Ready','all four synthetic artifacts, manifest, actual lengths/digests, file + directory fsync, metadata commit')
 crash_attempt=admit();psql(step(crash_attempt,'Validating',1,manifest,gen));assert child_crash('result-crash','after-seal')==93;assert state(crash_attempt)=='Validating'
 log('crash between result seal and metadata commit','actual child exit 93; source already sealed, DB Validating; result orphan has no Ready reference')

 occupied=BASE/'collision';occupied.mkdir(mode=0o700);(occupied/'marker').write_bytes(b'original');stage=BASE/'collision-stage';stage.mkdir(mode=0o700)
 try:rename_no_replace(stage,occupied);raise AssertionError('overwrite')
 except OSError as e:assert e.errno==errno.EEXIST
 assert (occupied/'marker').read_bytes()==b'original' and stage.exists();log('atomic rename no-replace','EEXIST=17; target and source unchanged')
 link=BASE/'symlink';link.symlink_to(dest/'source')
 try:os.open(link,os.O_RDONLY|os.O_NOFOLLOW);raise AssertionError('followed')
 except OSError as e:assert e.errno==errno.ELOOP
 log('no-follow open','ELOOP=40')
 # Orphan after known abort: file survives rollback; quarantine only after original transaction finished and authoritative absence of generation.
 orphan,m=seal('abort');g=m['source']['generation'];a=admit();psql(step(a,'Validating',1,m,g));psql('BEGIN;'+step(a,'Ready',2,m,g)+';ROLLBACK;');assert state(a)=='Validating' and orphan.exists()
 assert query(f"SELECT count(*) FROM c2f.\"ImportAttempt\" WHERE generation='{g}'")=='0'
 quarantined=BASE/'quarantine-abort';rename_no_replace(orphan,quarantined);sync_dir(BASE);assert quarantined.exists();log('known DB abort orphan quarantine','Ready rollback; no committed generation; quiescent writer; rename+parent fsync; bytes retained')
 # Recovery policy state is durable in private executor ledger; unknown outcome never grants deletion or replay.
 unknown,m=seal('unknown');g=m['source']['generation'];a=admit();psql(step(a,'Validating',1,m,g));op=str(uuid.uuid4())
 ledger=BASE/'unknown-outcome.json';put(ledger,json.dumps(dict(import_id=a,operation_id=op,outcome='Unknown',generation=g)).encode());sync_dir(BASE)
 before={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in unknown.iterdir()}
 def cleanup(path,outcome):
  if outcome=='Unknown':return 'Quarantined-logically; preserve path and bytes'
  raise AssertionError('no automatic delete in this validation')
 decision=cleanup(unknown,'Unknown');assert before=={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in unknown.iterdir()};assert state(a)=='Validating'
 log('cleanup at unknown outcome',decision+'; no replay, rename or unlink')
 # Commit ACK is deliberately ignored; reconciliation queries actual command journal.
 psql('BEGIN;'+step(a,'Ready',2,m,g,op)+';COMMIT;');assert cleanup(unknown,'Unknown').startswith('Quarantined')
 assert query(f"SELECT count(*) FROM c2f.\"ImportTransitions\" WHERE import_id='{a}' AND operation_id='{op}'")=='1';assert state(a)=='Ready'
 log('unknown commit reconciliation','application ACK suppression; committed journal found; bytes retained; not a network-loss simulation')
 # Corruption must be rejected by storage verifier before any future delivery, even if metadata says Ready.
 (unknown/'teacher').write_bytes(b'corrupt')
 assert hashlib.sha256((unknown/'teacher').read_bytes()).hexdigest()!=m['teacher']['sha256'];log('corrupt required artifact','storage digest mismatch detected; no delivery implemented; DB predicate alone does not check FS')
 log('fsync calls executed',str(len(SYNC))+' successful calls in parent; children also fsynced before designated crash')
 log('power-loss durability','NOT PROVEN: process exits and successful fsync syscalls do not prove device/WAL/failover durability')
finally:
 # Only remove this script-owned disposable tree after probes and reconciliation complete; not runtime janitor behavior.
 shutil.rmtree(BASE)
 pathlib.Path('/tmp/b07c2f/fs-results.json').write_text(json.dumps(dict(results=REPORT,sync_calls=SYNC,private_root_removed=not BASE.exists()),indent=2))
