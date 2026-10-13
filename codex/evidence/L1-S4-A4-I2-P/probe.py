import pathlib,subprocess,json,hashlib,re,time
R=pathlib.Path('/home/tea/github/lms'); E=pathlib.Path('/tmp/lms-s4-a4-i2-p')
def git(*a): return subprocess.check_output(['git',*a],cwd=R).decode().strip()
files=git('ls-files','--cached','--others','--exclude-standard').splitlines()
baseline={'branch':git('branch','--show-current'),'head':git('rev-parse','HEAD'),'index':hashlib.sha256(pathlib.Path(git('rev-parse','--git-path','index')).read_bytes()).hexdigest() if pathlib.Path(git('rev-parse','--git-path','index')).is_absolute() else hashlib.sha256((R/git('rev-parse','--git-path','index')).read_bytes()).hexdigest(),'files':{f:hashlib.sha256((R/f).read_bytes()).hexdigest() for f in files if (R/f).is_file()}}
(E/'baseline.json').write_text(json.dumps(baseline,indent=2))
N='lms-i2p-synthetic-'+str(int(time.time())); results=[]
def sql(s):
 p=subprocess.run(['docker','exec','-i',N,'psql','-X','-U','postgres','-d','synthetic','-v','ON_ERROR_STOP=1','-v','VERBOSITY=verbose','-At'],input=s,text=True,capture_output=True)
 return p.returncode,p.stdout+p.stderr
def test(name,s,state=None,contains=None):
 code,out=sql(s); ok=(code==0 if state is None else code!=0 and state in out) and (contains is None or contains in out)
 results.append(dict(name=name,matched_expected=ok,expected_sqlstate=state,exit=code,output=out,sql=s)); print(name,ok,flush=True)
 if not ok: raise RuntimeError(out)
def q(n): return "'00000000-0000-0000-0000-%012d'"%n
org=q(1); actor=q(2); ctx=q(3)
def root(n,ref='canonical',state='Active',version=1): return f'''INSERT INTO "ProcessingRestrictions" VALUES ({q(n)},{ctx},1,{org},NULL,'{ref}',1,'{state}',{version});'''
def decision(n): return f'''INSERT INTO "Prs1Decisions" ("DecisionId","OrganizationId","AttestedByActorId","DecisionRef","ApprovedCommand","CommandDigest","Evidence") VALUES ({q(n)},{org},{actor},'synthetic','{{}}',decode(repeat('01',32),'hex'),'{{"synthetic":true}}');'''
def command(n,new,old=None,kind='Issue',expected=None): return f'''INSERT INTO "Prs1Commands" ("CommandId","OrganizationId","DecisionId","Kind","NewRestrictionId","OldRestrictionId","ExpectedOldVersion","CommandDigest") VALUES ({q(n+2000)},{org},{q(n+1000)},'{kind}',{q(new) if new else 'NULL'},{q(old) if old else 'NULL'},{expected or 'NULL'},decode(repeat('01',32),'hex'));'''
def side(n,r,old=None): return f'''INSERT INTO "RestrictionSelectorProvenance" ("RestrictionId","OrganizationId","ContextId","OriginVersion","SubjectActorId","ProfileId","ContractRevision","CanonicalResourceRef","Operations","CommandId","DecisionId","SupersedesRestrictionId") VALUES ({q(r)},{org},{ctx},1,NULL,'PRS1','R1-2026-10-10','canonical',1,{q(n+2000)},{q(n+1000)},{q(old) if old else 'NULL'});'''
def pair(n): return decision(n+1000)+command(n,n)+root(n)+side(n,n)
try:
 subprocess.run(['docker','run','--detach','--rm','--name',N,'--network','none','--tmpfs','/var/lib/postgresql/data:rw','-e','POSTGRES_HOST_AUTH_METHOD=trust','-e','POSTGRES_DB=synthetic','postgres:17.6-alpine'],check=True,capture_output=True)
 for _ in range(80):
  if sql('SELECT 1;')[0]==0: break
  time.sleep(.25)
 pre='''CREATE ROLE prs1_owner NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
CREATE ROLE prs1_writer NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
CREATE ROLE prs1_reader NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
CREATE ROLE prs1_app NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE,CREATE ON SCHEMA public TO prs1_owner;
SET ROLE prs1_owner;
CREATE TABLE "Organizations" ("OrganizationId" uuid PRIMARY KEY);
CREATE TABLE "Actors" ("ActorId" uuid PRIMARY KEY);
CREATE TABLE "OrganizationMemberships" ("OrganizationId" uuid,"ActorId" uuid,PRIMARY KEY("OrganizationId","ActorId"));
CREATE TABLE "ProcessingContextVersions" ("ContextId" uuid,"Version" bigint,"OrganizationId" uuid,PRIMARY KEY("ContextId","Version","OrganizationId"));
CREATE TABLE "ProblemWorks" ("Id" uuid PRIMARY KEY);
CREATE TABLE "Submissions" ("Id" uuid PRIMARY KEY,"ProblemWorkId" uuid, UNIQUE("ProblemWorkId","Id"));
CREATE TABLE "Reviews" ("Id" uuid PRIMARY KEY);
CREATE TABLE "ProcessingRestrictions" ("RestrictionId" uuid PRIMARY KEY,"ContextId" uuid NOT NULL,"OriginVersion" bigint NOT NULL,"OrganizationId" uuid NOT NULL,"SubjectActorId" uuid,"ResourceRef" text,"Operations" integer NOT NULL,"State" text NOT NULL,"Version" bigint NOT NULL,
CHECK ("State" IN ('Active','Withdrawn') AND "Version">0 AND "Operations">0 AND "Operations"<=31),
FOREIGN KEY("ContextId","OriginVersion","OrganizationId") REFERENCES "ProcessingContextVersions"("ContextId","Version","OrganizationId") ON DELETE RESTRICT,
FOREIGN KEY("OrganizationId","SubjectActorId") REFERENCES "OrganizationMemberships"("OrganizationId","ActorId") ON DELETE RESTRICT);
'''
 migration=(R/'src/Lms.Infrastructure/Migrations/20261009094756_SchemaFoundationsM1.cs').read_text()
 pre+=re.search(r'CREATE FUNCTION s1_stable_key\(\).*?END \$\$;',migration,re.S)[0]+re.search(r'CREATE TRIGGER s1_restriction_provenance[^;]+;',migration)[0]
 pre+=f'INSERT INTO "Organizations" VALUES ({org}); INSERT INTO "Actors" VALUES ({actor}); INSERT INTO "ProcessingContextVersions" VALUES ({ctx},1,{org});'+root(10)
 (E/'prerequisites.sql').write_text(pre); test('prerequisite synthetic projection',pre)
 ddl=re.search(r'```sql\n(.*?)\n```',(R/'codex/L1-S4-A4-PROVENANCE-SCHEMA.md').read_text(),re.S)[1]
 (E/'candidate.sql').write_text(ddl); test('unmodified candidate DDL', 'SET ROLE prs1_owner;'+ddl)
 test('environment','SELECT version(); SELECT rolname,rolsuper,rolcreaterole,rolcanlogin FROM pg_roles WHERE rolname LIKE \'prs1_%\';')
 test('positive issue commit','BEGIN; SET LOCAL ROLE prs1_owner;'+pair(100)+'COMMIT;')
 for table in ['RestrictionSelectorProvenance','Prs1Decisions','Prs1Commands','RestrictionBirths']:
  for verb in ['UPDATE','DELETE']:
   stmt=f'UPDATE "{table}" SET "OrganizationId"="OrganizationId";' if verb=='UPDATE' else f'DELETE FROM "{table}";'
   test('immutable '+table+' '+verb,'SET ROLE prs1_owner;'+stmt,'23514')
 test('preinstall legacy late attestation','BEGIN; SET LOCAL ROLE prs1_owner;'+decision(1200)+command(200,10)+side(200,10)+'COMMIT;','23514', 'prs1_late_attestation')
 test('postinstall legacy insert','SET ROLE prs1_owner;'+root(11))
 test('postinstall late attestation','BEGIN; SET LOCAL ROLE prs1_owner;'+decision(1201)+command(201,11)+side(201,11)+'COMMIT;','23514','prs1_late_attestation')
 test('missing sidecar at commit','BEGIN; SET LOCAL ROLE prs1_owner;'+decision(1300)+command(300,300)+root(300)+'COMMIT;','23503')
 test('missing pair rollback verified',f'SELECT count(*) FROM "ProcessingRestrictions" WHERE "RestrictionId"={q(300)};',contains='0\n')
 test('snapshot mismatch','BEGIN; SET LOCAL ROLE prs1_owner;'+decision(1301)+command(301,301)+root(301,'wrong')+side(301,301)+'COMMIT;','23514','prs1_snapshot_mismatch')
 test('digest mismatch','BEGIN; SET LOCAL ROLE prs1_owner;'+decision(1302)+command(302,302).replace("repeat('01'","repeat('02'")+root(302)+side(302,302)+'COMMIT;','23514','prs1_decision_mismatch')
 test('root only commits (legacy permitted)','SET ROLE prs1_owner;'+root(303))
 test('forged evidence and noncanonical selector commits with owner','BEGIN; SET LOCAL ROLE prs1_owner;'+pair(304)+'COMMIT;')
 test('withdraw without transition rejected','BEGIN; SET LOCAL ROLE prs1_owner;'+decision(1400)+command(400,None,100,'Withdraw',1)+'COMMIT;','23514','prs1_withdraw_incomplete')
 test('withdraw positive','BEGIN; SET LOCAL ROLE prs1_owner;'+decision(1401)+command(401,None,100,'Withdraw',1)+f'UPDATE "ProcessingRestrictions" SET "State"=\'Withdrawn\',"Version"=2 WHERE "RestrictionId"={q(100)};COMMIT;')
 test('repeated withdrawal without new transition commits','BEGIN; SET LOCAL ROLE prs1_owner;'+decision(1402)+command(402,None,100,'Withdraw',1)+'COMMIT;')
 test('resurrection without receipt commits',f'SET ROLE prs1_owner;UPDATE "ProcessingRestrictions" SET "State"=\'Active\',"Version"=1 WHERE "RestrictionId"={q(100)};')
 test('reissue positive','BEGIN; SET LOCAL ROLE prs1_owner;'+decision(1500)+command(500,500,100,'Reissue',1)+root(500)+side(500,500,100)+f'UPDATE "ProcessingRestrictions" SET "State"=\'Withdrawn\',"Version"=2 WHERE "RestrictionId"={q(100)};COMMIT;')
 test('reissue failure rollback','BEGIN; SET LOCAL ROLE prs1_owner;'+decision(1501)+command(501,501,500,'Reissue',1)+root(501)+side(501,501,500)+'COMMIT;','23514','prs1_withdraw_incomplete')
 test('old active new absent after rollback',f'SELECT "State" FROM "ProcessingRestrictions" WHERE "RestrictionId"={q(500)}; SELECT count(*) FROM "ProcessingRestrictions" WHERE "RestrictionId"={q(501)};',contains='Active\n0\n')
 # Explicit synthetic ACL overlay, not part of candidate DDL, no procedures implemented.
 acl='GRANT USAGE ON SCHEMA public TO prs1_app,prs1_reader,prs1_writer; GRANT SELECT ON ALL TABLES IN SCHEMA public TO prs1_reader;'
 (E/'acl-overlay.sql').write_text(acl); test('ACL overlay',acl)
 for role in ['prs1_app','prs1_reader','prs1_writer']:
  for table in ['ProcessingRestrictions','RestrictionSelectorProvenance','Prs1Decisions','Prs1Commands','RestrictionBirths','Prs1MutationReceipts']:
   test(role+' denies insert '+table,f'SET ROLE {role};INSERT INTO "{table}" DEFAULT VALUES;','42501')
  for table in ['ProcessingRestrictions','RestrictionSelectorProvenance','Prs1Decisions','Prs1Commands','RestrictionBirths','Prs1MutationReceipts']:
   for stmt in [f'UPDATE "{table}" SET "OrganizationId"="OrganizationId";',f'DELETE FROM "{table}";',f'TRUNCATE "{table}";']:
    test(role+' denies '+stmt,f'SET ROLE {role};'+stmt,'42501')
  test(role+' denies trigger disable',f'SET ROLE {role};ALTER TABLE "ProcessingRestrictions" DISABLE TRIGGER ALL;','42501')
  test(role+' denies owner membership',f'SET ROLE {role};SET ROLE prs1_owner;','42501') if False else None
  test(role+' denies function execute',f'SET ROLE {role};SELECT prs1_record_birth();','42501')
 test('reader SELECT','SET ROLE prs1_reader;SELECT count(*)>0 FROM "RestrictionSelectorProvenance";',contains='t\n')
 test('no membership','SELECT count(*) FROM pg_auth_members m JOIN pg_roles r ON r.oid=m.roleid WHERE r.rolname=\'prs1_owner\';',contains='0\n')
 # Model an existing serving-role grant: candidate REVOKE PUBLIC does not remove it.
 test('existing grant setup','GRANT INSERT ON "ProcessingRestrictions" TO prs1_app;')
 test('existing app grant survives candidate revoke','SET ROLE prs1_owner;'+ddl[ddl.index('REVOKE ALL ON public."RestrictionSelectorProvenance"'):])
 test('existing direct root grant bypass','SET ROLE prs1_app;'+root(600))
 test('revoke fixture grant','REVOKE INSERT ON "ProcessingRestrictions" FROM prs1_app;')
 test('mutation fixture',f'INSERT INTO "ProblemWorks" VALUES ({q(700)});INSERT INTO "Submissions" VALUES ({q(701)},{q(700)});INSERT INTO "Prs1MutationReceipts" ("OrganizationId","CommandId","ActorId","Kind","CommandDigest","WorkId","SubmissionId","ResultWorkVersion") VALUES ({org},{q(702)},{actor},\'Submit\',decode(repeat(\'01\',32),\'hex\'),{q(700)},{q(701)},1);')
 test('mutation immutable update','SET ROLE prs1_owner;UPDATE "Prs1MutationReceipts" SET "ResultWorkVersion"=2;','23514')
 test('mutation immutable delete','SET ROLE prs1_owner;DELETE FROM "Prs1MutationReceipts";','23514')
 test('TRUNCATE row triggers bypass on isolated history table' ,'BEGIN; SET LOCAL ROLE prs1_owner;TRUNCATE "Prs1MutationReceipts";SELECT count(*) FROM "Prs1MutationReceipts";ROLLBACK;')
finally:
 (E/'results.json').write_text(json.dumps(results,indent=2))
 p=subprocess.run(['docker','rm','-f',N],text=True,capture_output=True)
 (E/'cleanup.json').write_text(json.dumps({'container':N,'exit':p.returncode,'output':p.stdout+p.stderr}))
