using System.Data;
using System.Text.Json;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

static class P {
const string Cs="Host=/tmp/b07c2f/pgsocket;Username=postgres;Database=c2f;Pooling=false;Command Timeout=15";
static readonly List<object> Results=[];
static string Q(Guid x)=>$"'{x}'";
static NpgsqlConnection Open(){var c=new NpgsqlConnection(Cs); c.Open(); return c;}
static object? Sql(string s,NpgsqlConnection? c=null){if(c!=null){using var cmd=new NpgsqlCommand(s,c); return cmd.ExecuteScalar();} using var own=Open();return Sql(s,own);}
static void Check(bool b,string m){if(!b)throw new Exception(m);}
static string Code(Action action){try{action();return "00000";}catch(PostgresException e){return e.SqlState;}}
static void Reject(string name,string sql,string expected="P0001"){var code=Code(()=>Sql(sql));Check(code==expected,$"{name}: expected {expected}, got {code}");Log(name,code);}
static void Log(string n,string detail="PASS"){Results.Add(new{name=n,detail});Console.WriteLine(n+": "+detail);}
record F(Guid O,Guid A,Guid X,string C);
static F Seed(){var f=new F(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"fixture-"+Guid.NewGuid().ToString("N"));Sql($"""
 INSERT INTO "Organizations" VALUES ({Q(f.O)},'Enabled',true);
 INSERT INTO "Actors" VALUES ({Q(f.A)},'Teacher');
 INSERT INTO "CourseRegistry" VALUES ('{f.C}',{Q(f.O)},'Verified');
 SELECT public.s3_change_authority('Membership','Grant',{Q(f.O)},{Q(f.A)},0,0,NULL,NULL,NULL,'Teacher',0);
 SELECT public.s3_change_authority('Teacher','Grant',{Q(f.O)},{Q(f.A)},0,1,'{f.C}',NULL,gen_random_uuid(),NULL,0);
 INSERT INTO "ProcessingContextVersions"("ContextId","Version","OrganizationId","ApprovalState","SyntheticOnly","EffectiveFrom") VALUES({Q(f.X)},1,{Q(f.O)},'SyntheticOnly',true,clock_timestamp()-interval '2 days');
 INSERT INTO "CourseContextBindings" VALUES ('{f.C}',clock_timestamp()-interval '1 day',{Q(f.O)},{Q(f.X)},1,NULL);
 INSERT INTO c2f."ProcessingGuard" VALUES({Q(f.O)},1,false);
 """);return f;}
static Guid Admit(F f){var id=Guid.NewGuid();Sql($"SELECT c2f.admit({Q(id)},{Q(f.O)},'{f.C}',{Q(f.A)},gen_random_uuid(),'fixed-fixture')");return id;}
static string Desc(params string[] names){var g=Guid.NewGuid();return JsonSerializer.Serialize(names.ToDictionary(k=>k,k=>new{key=k,length=9,sha256=new string('a',64),generation=g}));}
static string Step(F f,Guid i,string target,long ver=2,Guid? op=null,string fp="fixed-result",bool seal=true,string? desc=null){
 var d=desc??Desc(target=="Validating"?["source"]:["student","teacher","receipt"]);using var doc=JsonDocument.Parse(d);var g=doc.RootElement.EnumerateObject().First().Value.GetProperty("generation").GetString();
 return $"SELECT c2f.step({Q(i)},{Q(f.O)},{Q(f.A)},{ver},{Q(op??Guid.NewGuid())},'{fp}','{target}',{seal.ToString().ToLowerInvariant()},'{d}'::jsonb,'{g}')";
}
static Guid Valid(F f){var i=Admit(f);Sql(Step(f,i,"Validating",1));return i;}
static string Lock(F f)=>$"SELECT c2f.lock_org({Q(f.O)})";
static string Restrict(F f)=>$"INSERT INTO \"ProcessingRestrictions\"(\"RestrictionId\",\"ContextId\",\"OriginVersion\",\"OrganizationId\",\"Operations\",\"State\",\"Version\") VALUES(gen_random_uuid(),{Q(f.X)},1,{Q(f.O)},1,'Active',1)";
static void Enable(F f)=>Sql($"UPDATE c2f.\"ProcessingGuard\" SET compatible=true WHERE org={Q(f.O)}");
static bool Read(F f,Guid i)=>(bool)Sql($"SELECT c2f.readable({Q(i)},{Q(f.O)},{Q(f.A)})")!;
static long Version(Guid i)=>(long)Sql($"SELECT version FROM c2f.\"ImportAttempt\" WHERE id={Q(i)}")!;
static long Count(string table)=>(long)Sql($"SELECT count(*) FROM {table}")!;
static void WaitBlocked(int pid){var end=DateTime.UtcNow.AddSeconds(8);while(DateTime.UtcNow<end){if((bool)Sql($"SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock')")!)return;Thread.Sleep(10);}throw new Exception("Expected lock wait absent "+pid);}
static string Tx(string sql,IsolationLevel iso=IsolationLevel.Serializable){using var c=Open();using var t=c.BeginTransaction(iso);var code=Code(()=>{Sql(sql,c);t.Commit();});return code;}
static void Migration(){using(var c=new NpgsqlConnection(Cs.Replace("Database=c2f","Database=postgres"))){c.Open();Sql("CREATE DATABASE c2f",c);}using var db=new LmsDbContext(new DbContextOptionsBuilder<LmsDbContext>().UseNpgsql(Cs).Options);var migrator=db.GetService<IMigrator>();migrator.Migrate("20261009101331_SessionVerifierLookupS2");Check(db.Database.GetAppliedMigrations().Count()==7,"S2 chain count");migrator.Migrate();Check(db.Database.GetAppliedMigrations().Count()==8,"full chain count");Check(!db.Database.HasPendingModelChanges(),"model snapshot");migrator.Migrate();File.WriteAllText("/tmp/b07c2f/probe/full-migrations.sql",migrator.GenerateScript());Log("full EF chain S2 -> S3, repeated migrate, model snapshot","8 migrations; PASS");Sql(File.ReadAllText("/tmp/b07c2f/probe/candidate.sql"));migrator.Migrate();Log("candidate additive schema after full chain and repeated EF migrate");}
static void Basic(){var f=Seed();var id=Valid(f);
 Reject("Ready blocked until compatibility",Step(f,id,"Ready"));Enable(f);
 Reject("unsealed Ready",Step(f,id,"Ready",seal:false));Reject("partial descriptors",Step(f,id,"Ready",desc:Desc("student")));
 Reject("stale Version",Step(f,id,"Ready",1));
 Reject("descriptor path traversal",Step(f,id,"Ready",desc:Desc("student","teacher","receipt").Replace("\"key\":\"student\"","\"key\":\"../student\"")));
 var cross=Step(f,id,"Ready");cross=cross[..(cross.LastIndexOf("::jsonb,")+8)]+"gen_random_uuid())";Reject("cross generation descriptor",cross);
 var before=Count("\"SecurityAudit\"");var j=Count("c2f.\"ImportTransitions\"");
 Sql("ALTER TABLE c2f.\"ImportTransitions\" ADD CONSTRAINT injected_audit_fault CHECK(outcome<>'Ready') NOT VALID");
 Reject("journal failure rolls back Ready + audit",Step(f,id,"Ready"),"23514");
 Check(Version(id)==2&&Count("\"SecurityAudit\"")==before&&Count("c2f.\"ImportTransitions\"")==j,"audit atomicity");Sql("ALTER TABLE c2f.\"ImportTransitions\" DROP CONSTRAINT injected_audit_fault");
 var op=Guid.NewGuid();var ready=Step(f,id,"Ready",op:op);Check(Tx(ready)=="00000","ready commit");Check(Read(f,id),"positive read");Log("sealed Ready + atomic journal/audit + positive read");
 // Commit completed, application deliberately discards acknowledgement. Authoritative journal reconciliation precedes any retry.
 Check((long)Sql($"SELECT count(*) FROM c2f.\"ImportTransitions\" WHERE import_id={Q(id)} AND operation_id={Q(op)}")! ==1,"reconciliation");
 Sql(ready);Check(Version(id)==3&&Count("\"SecurityAudit\"")==before+1,"duplicate commit");Log("lost ACK injection, journal reconciliation, no duplicate audit","application-boundary ACK suppression; not wire loss");
 Reject("same OperationId different fingerprint",Step(f,id,"Ready",op:op,fp:"different"),"23505");
 Reject("terminal Ready cannot Complete again",Step(f,id,"Ready",3),"23514");
 foreach(var set in new[]{"owner=gen_random_uuid()","org=gen_random_uuid()","course='foreign'","context=gen_random_uuid()","context_version=2","binding_from=binding_from+interval '1 second'","expires=expires+interval '1 hour'","admitted=admitted+interval '1 hour'"})Reject("immutable "+set,$"UPDATE c2f.\"ImportAttempt\" SET {set},version=version+1 WHERE id={Q(id)}","23514");
 Reject("retained tombstone",$"DELETE FROM c2f.\"ImportAttempt\" WHERE id={Q(id)}","23514");Reject("journal append-only","DELETE FROM c2f.\"ImportTransitions\"","23514");
 Reject("unknown state",$"UPDATE c2f.\"ImportAttempt\" SET state='Published',version=4 WHERE id={Q(id)}","23514");
 var other=Seed();Reject("wrong owner",Step(f with {A=other.A},id,"Ready",3));Reject("wrong org",Step(f with {O=other.O},id,"Ready",3));
 Check(!Read(f with{A=other.A},id)&&!Read(f with{O=other.O},id),"negative read");Log("wrong org/owner read deny");
 foreach(var table in new[]{"c2f.\"ImportAttempt\"","c2f.\"ImportTransitions\"","c2f.\"ProcessingGuard\"","public.\"ProcessingRestrictions\"","public.\"CourseContextBindings\"","public.\"ProcessingContextVersions\""}){
 Reject("serving DELETE "+table,$"SET ROLE c2f_serving; DELETE FROM {table}","42501");Reject("serving INSERT "+table,$"SET ROLE c2f_serving; INSERT INTO {table} DEFAULT VALUES","42501");Reject("serving UPDATE "+table,$"SET ROLE c2f_serving; UPDATE {table} SET {(table.Contains("ImportAttempt")?"version=version":table.Contains("ImportTransitions")?"to_version=to_version":table.Contains("ProcessingGuard")?"revision=revision":table.Contains("ProcessingContextVersions")?"\"Version\"=\"Version\"":table.Contains("CourseContextBindings")?"\"EffectiveTo\"=\"EffectiveTo\"":"\"Version\"=\"Version\"")}","42501");Reject("serving TRUNCATE "+table,$"SET ROLE c2f_serving; TRUNCATE {table}","42501");}
 Reject("serving descriptors unreadable",$"SET ROLE c2f_serving; SELECT source,result FROM c2f.\"ImportAttempt\"","42501");
 Reject("serving cannot call internal command","SET ROLE c2f_serving; "+Step(f,id,"Ready",3),"42501");
 var f2=Seed();var i2=Admit(f2);Check(Tx("SET LOCAL ROLE c2f_executor; "+Step(f2,i2,"Validating",1))=="00000","restricted executor");Log("restricted executor positive control");
 Reject("missing context admission",$"SELECT c2f.admit(gen_random_uuid(),{Q(f.O)},'missing',{Q(f.A)},gen_random_uuid(),'fixture')");
 var missing=Seed();Sql($"UPDATE \"CourseContextBindings\" SET \"EffectiveTo\"=clock_timestamp()-interval '1 second' WHERE \"CourseId\"='{missing.C}'");Reject("no active binding",$"SELECT c2f.admit(gen_random_uuid(),{Q(missing.O)},'{missing.C}',{Q(missing.A)},gen_random_uuid(),'fixture')");
 Reject("ambiguous overlapping context rejected by base schema",$"INSERT INTO \"CourseContextBindings\" VALUES('{f.C}',clock_timestamp()-interval '1 hour',{Q(f.O)},{Q(f.X)},1,NULL)","23P01");
 // Defensive ambiguous-resolution predicate with duplicate rows in a CTE; actual table ambiguity is excluded by S1.
 Check(!(bool)Sql("WITH bindings(x) AS(VALUES(1),(1)) SELECT count(*)=1 FROM bindings")!,"ambiguous count");Log("ambiguous count fail-closed predicate","SQL predicate only; stored ambiguity rejected 23P01");
}
static void Authority(){foreach(var kind in new[]{"Student","OrgAdmin","group-only","generation-mismatch","revoked","wrong-course"}){var f=Seed();var actor=Guid.NewGuid();Sql($"INSERT INTO \"Actors\" VALUES({Q(actor)},NULL)");var role=kind=="Student"?"Student":"Teacher";Sql($"SELECT public.s3_change_authority('Membership','Grant',{Q(f.O)},{Q(actor)},0,0,NULL,NULL,NULL,'{role}',{(kind=="OrgAdmin"?1:0)})");if(kind is "group-only" or "generation-mismatch" or "revoked" or "wrong-course"){
 Sql($"INSERT INTO \"CourseGroups\" VALUES('{f.C}','g')");Sql($"SELECT public.s3_change_authority('Teacher','Grant',{Q(f.O)},{Q(actor)},0,1,'{f.C}',{(kind=="group-only"?"'g'":"NULL")},gen_random_uuid(),NULL,0)");
 if(kind is "generation-mismatch" or "revoked")Sql($"SELECT public.s3_change_authority('Membership','{(kind=="revoked"?"Revoke":"Grant")}',{Q(f.O)},{Q(actor)},1,1,NULL,NULL,NULL,'Teacher',0)");}
 var course=kind=="wrong-course"?"absent":f.C;Reject("authority negative "+kind,$"SELECT c2f.admit(gen_random_uuid(),{Q(f.O)},'{course}',{Q(actor)},gen_random_uuid(),'fixture')");}
}
static void Races(){
 foreach(var kind in new[]{"two Complete","cancel wins","Ready wins"}){var f=Seed();Enable(f);var id=Valid(f);using var c1=Open();using var c2=Open();using var t=c1.BeginTransaction(IsolationLevel.Serializable);var first=kind=="cancel wins"?"Cancelled":"Ready";Sql(Step(f,id,first),c1);var second=kind=="Ready wins"?"Cancelled":"Ready";var s=Step(f,id,second);var task=Task.Run(()=>{using var t2=c2.BeginTransaction(IsolationLevel.Serializable);return Code(()=>{Sql(s,c2);t2.Commit();});});WaitBlocked(c2.ProcessID);t.Commit();var code=task.Result;Check(code=="40001",kind+code);var retry=Tx(s);Check(retry=="P0001",kind+retry);Check(Version(id)==3,"one transition");Log(kind,"wait -> 40001; fresh retry -> P0001; exactly one terminal transition");}
 // Restriction commits before importer, common guard causes stale Serializable snapshot abort.
 {var f=Seed();Enable(f);var id=Valid(f);using var a=Open();using var b=Open();using var t=a.BeginTransaction(IsolationLevel.Serializable);Sql(Lock(f),a);Sql(Restrict(f),a);var task=Task.Run(()=>{using var t2=b.BeginTransaction(IsolationLevel.Serializable);return Code(()=>{Sql(Step(f,id,"Ready"),b);t2.Commit();});});WaitBlocked(b.ProcessID);t.Commit();Check(task.Result=="40001","restriction first serialization");Check(Tx(Step(f,id,"Ready"))=="P0001"&&Version(id)==2&&!Read(f,id),"restriction fresh deny");Log("Restriction commit -> Ready","40001 then P0001; no Ready");}
 {var f=Seed();Enable(f);var id=Valid(f);using var a=Open();using var b=Open();using var t=a.BeginTransaction(IsolationLevel.Serializable);Sql(Step(f,id,"Ready"),a);var task=Task.Run(()=>{using var t2=b.BeginTransaction(IsolationLevel.Serializable);return Code(()=>{Sql(Lock(f),b);Sql(Restrict(f),b);t2.Commit();});});WaitBlocked(b.ProcessID);t.Commit();Check(task.Result=="40001","restriction second serialization");Check(Tx(Lock(f)+";"+Restrict(f))=="00000","restriction retry");Check(!Read(f,id)&&Version(id)==3,"post Ready read deny");Log("Ready commit -> Restriction commit -> read","40001 then committed restriction; read=false, metadata Ready retained");}
 // Known-incompatible raw writer: no guard nor organization lock, phantom absent in import snapshot.
 {var f=Seed();Enable(f);var id=Valid(f);using var a=Open();using var t=a.BeginTransaction(IsolationLevel.Serializable);Sql(Lock(f),a);Check((bool)Sql($"SELECT c2f.unrestricted({Q(f.O)},{Q(f.X)})",a)!,"initial allow");Check(Tx(Restrict(f),IsolationLevel.ReadCommitted)=="00000","bypass insert");Sql(Step(f,id,"Ready"),a);t.Commit();Check(Version(id)==3&&!Read(f,id),"bypass counterexample");Log("COUNTEREXAMPLE phantom raw restriction bypass","raw restriction 00000 before Ready 00000; Serializable alone unsafe; later fresh read denies");}
 foreach(var iso in new[]{IsolationLevel.ReadCommitted,IsolationLevel.RepeatableRead,IsolationLevel.Serializable}){var f=Seed();using var a=Open();using var t=a.BeginTransaction(iso);Check((bool)Sql($"SELECT c2f.unrestricted({Q(f.O)},{Q(f.X)})",a)!,"snapshot");Check(Tx(Lock(f)+";"+Restrict(f))=="00000","writer");var code=Code(()=>Sql(Lock(f),a));if(iso==IsolationLevel.ReadCommitted){Check(code=="00000"&&!(bool)Sql($"SELECT c2f.unrestricted({Q(f.O)},{Q(f.X)})",a)!,"RC fresh");}else Check(code=="40001","snapshot fence");t.Rollback();Log("snapshot freshness "+iso,code);}
 // Exact lock inversion, not an inferred deadlock.
 {var f=Seed();var id=Valid(f);using var a=Open();using var b=Open();using var t1=a.BeginTransaction();using var t2=b.BeginTransaction();Sql($"UPDATE c2f.\"ProcessingGuard\" SET revision=revision+1 WHERE org={Q(f.O)}",a);Sql($"SELECT id FROM c2f.\"ImportAttempt\" WHERE id={Q(id)} FOR UPDATE",b);var one=Task.Run(()=>Code(()=>Sql($"SELECT id FROM c2f.\"ImportAttempt\" WHERE id={Q(id)} FOR UPDATE",a)));WaitBlocked(a.ProcessID);var two=Task.Run(()=>Code(()=>Sql($"UPDATE c2f.\"ProcessingGuard\" SET revision=revision+1 WHERE org={Q(f.O)}",b)));var winner=Task.WhenAny(one,two).Result;Task.WaitAll(one,two);Check(new[]{one.Result,two.Result}.Contains("40P01") && new[]{one.Result,two.Result}.Contains("00000"),"deadlock results: "+one.Result+","+two.Result);t1.Rollback();t2.Rollback();Log("guard/attempt reversed lock order","40P01; victim abort, survivor released");}
}
static void ExpiryAndAbort(){var f=Seed();Enable(f);var id=Admit(f);
 // Fixture creation at past admission, exact 24h TTL. No production timestamp override API.
 var near=Guid.NewGuid();Sql($"WITH tm AS MATERIALIZED (SELECT clock_timestamp() AS at) INSERT INTO c2f.\"ImportAttempt\" SELECT {Q(near)},org,course,owner,context,context_version,binding_from,tm.at-interval '24 hours'+interval '2 seconds',tm.at+interval '2 seconds',gen_random_uuid(),admission_fingerprint,state,version,epoch,gen_random_uuid(),fingerprint,source,result,generation,cleanup FROM c2f.\"ImportAttempt\",tm WHERE id={Q(id)}");
 Sql(Step(f,near,"Validating",1));using(var c=Open()){using var t=c.BeginTransaction();Sql(Lock(f),c);var task=Task.Run(()=>Tx(Step(f,near,"Ready"),IsolationLevel.ReadCommitted));Thread.Sleep(2200);t.Commit();Check(task.Result=="P0001"&&Version(near)==2,"expiry wins");}Log("expiry during lock wait -> Ready denied","P0001; clock_timestamp after lock; no sliding TTL");
 Check(Tx(Step(f,near,"Expired"))=="00000","expire transition");Check(!Read(f,near),"expired read");Log("Expired retained before physical cleanup");
 var i=Valid(f);var a=Count("\"SecurityAudit\"");using(var c=Open()){using var t=c.BeginTransaction();Sql(Step(f,i,"Ready"),c);t.Rollback();}Check(Version(i)==2&&Count("\"SecurityAudit\"")==a,"abort rollback");Log("DB abort after sealed metadata update","Ready + audit + journal rolled back; FS orphan tested separately");
}
static void BypassWriters(){var f=Seed();using var a=Open();using var t=a.BeginTransaction();Sql($"SELECT org FROM c2f.\"ProcessingGuard\" WHERE org={Q(f.O)} FOR UPDATE",a);Check(Tx(Restrict(f),IsolationLevel.ReadCommitted)=="00000","raw restriction");Check(Tx($"UPDATE \"ProcessingRestrictions\" SET \"State\"='Withdrawn',\"Version\"=2 WHERE \"OrganizationId\"={Q(f.O)}",IsolationLevel.ReadCommitted)=="00000","raw withdrawal");Check(Tx($"UPDATE \"CourseContextBindings\" SET \"EffectiveTo\"=clock_timestamp() WHERE \"CourseId\"='{f.C}'",IsolationLevel.ReadCommitted)=="00000","raw binding");Check(Tx($"INSERT INTO \"ProcessingContextVersions\"(\"ContextId\",\"Version\",\"OrganizationId\",\"ApprovalState\",\"SyntheticOnly\",\"EffectiveFrom\") VALUES({Q(f.X)},2,{Q(f.O)},'SyntheticOnly',true,clock_timestamp())",IsolationLevel.ReadCommitted)=="00000","raw context");t.Rollback();Log("existing writer guard bypass","restriction insert/withdrawal, binding closure, context insert all 00000 while guard locked");}
static void Additional(){
 var f=Seed();Enable(f);var id=Admit(f);
 var cmd=(Guid)Sql($"SELECT command_id FROM c2f.\"ImportAttempt\" WHERE id={Q(id)}")!;
 var admission=$"SELECT c2f.admit(gen_random_uuid(),{Q(f.O)},'{f.C}',{Q(f.A)},{Q(cmd)},'fixed-fixture')";
 Check((Guid)Sql(admission)! == id,"admission replay");Reject("admission fingerprint conflict",admission.Replace("fixed-fixture","changed"),"23505");Log("admission idempotency","same id and no second admission");
 var near=Guid.NewGuid();Sql($"WITH tm AS MATERIALIZED (SELECT clock_timestamp() AS at) INSERT INTO c2f.\"ImportAttempt\" SELECT {Q(near)},org,course,owner,context,context_version,binding_from,tm.at-interval '24 hours'+interval '1 second',tm.at+interval '1 second',gen_random_uuid(),admission_fingerprint,state,version,epoch,gen_random_uuid(),fingerprint,source,result,generation,cleanup FROM c2f.\"ImportAttempt\",tm WHERE id={Q(id)}");
 Sql(Step(f,near,"Validating",1));using(var c=Open()){using var t=c.BeginTransaction(IsolationLevel.Serializable);Sql(Step(f,near,"Ready"),c);Thread.Sleep(1200);t.Commit();}
 Check(Version(near)==3&&!Read(f,near),"expired Ready deny");Log("LIMIT expiry after Ready check before COMMIT","00000 commit after deadline; fresh read=false; no commit-time deadline proof");
 var e=Seed();using(var c=Open()){using var t=c.BeginTransaction();Sql($"SELECT org FROM c2f.\"ProcessingGuard\" WHERE org={Q(e.O)} FOR UPDATE",c);using var db=new LmsDbContext(new DbContextOptionsBuilder<LmsDbContext>().UseNpgsql(Cs).Options);db.Add(new ProcessingRestriction{RestrictionId=Guid.NewGuid(),OrganizationId=e.O,ContextId=e.X,OriginVersion=1,Operations=1,State="Active",Version=1});db.SaveChanges();t.Rollback();}Log("actual EF Add/SaveChanges bypasses held candidate guard");
 var missing=Seed();Sql($"DELETE FROM c2f.\"ProcessingGuard\" WHERE org={Q(missing.O)}");Reject("missing guard denies admission",$"SELECT c2f.admit(gen_random_uuid(),{Q(missing.O)},'{missing.C}',{Q(missing.A)},gen_random_uuid(),'fixture')");
}
static int Wire(){var f=Seed();Enable(f);var i=Valid(f);var op=Guid.NewGuid();bool escaped=false;string exception="none";
 using(var c=new NpgsqlConnection(Cs.Replace("/tmp/b07c2f/pgsocket","/tmp/b07c2f/proxysocket")+";SSL Mode=Disable")){c.Open();using var t=c.BeginTransaction(IsolationLevel.Serializable);Sql(Step(f,i,"Ready",op:op),c);try{t.Commit();escaped=true;}catch(NpgsqlException e){exception=e.GetType().Name;}}
 Check(!escaped,"commit ACK unexpectedly delivered");Check(Version(i)==3,"commit not durable");Check((long)Sql($"SELECT count(*) FROM c2f.\"ImportTransitions\" WHERE import_id={Q(i)} AND operation_id={Q(op)}")! ==1,"journal exactly once");
 Console.WriteLine("WIRE_ACK_LOSS PASS: "+exception+"; no success returned; authoritative state Ready version=3; one journal; no replay/delete");return 0;}
public static int Main(string[] args){if(args.Length>0 && args[0]=="wire")return Wire();try{Migration();Basic();Authority();Races();ExpiryAndAbort();BypassWriters();Additional();File.WriteAllText("/tmp/b07c2f/probe/results.json",JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"COMPLETE {Results.Count} assertions/groups");return 0;}catch(Exception e){Console.WriteLine(e);File.WriteAllText("/tmp/b07c2f/probe/results.partial.json",JsonSerializer.Serialize(Results));return 1;}}
}
