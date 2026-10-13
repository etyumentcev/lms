using System.Data;
using System.Data.Common;
using Lms.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit.Abstractions;

namespace Lms.Infrastructure.Tests;

public sealed class ProcessingWriterCompatibilityTests(PostgresFixture fixture, ITestOutputHelper output) : IClassFixture<PostgresFixture>
{
    private const string ProcessingMigration = "20261011120000_ProcessingWriterGuardC2G";
    private const string Previous = "20261009153218_ScopedAuthorityS3";
    private sealed record Setup(string Cs, Guid Org, Guid Actor, Guid Context, string Course, DateTimeOffset From,
        string Admin, string Writer, string Serving, Guid Draft)
    {
        public ProcessingWriteContext Evidence => new(Org, Course, Context, 1, From, [Actor]);
    }
    private static async Task Sql(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c); await cmd.ExecuteNonQueryAsync();
    }
    private static async Task<T> Scalar<T>(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c); return (T)(await cmd.ExecuteScalarAsync())!;
    }
    private static async Task<LmsDbContext> As(string cs, string role, params IInterceptor[] interceptors)
    {
        var db = PostgresFixture.Open(cs, interceptors); await db.Database.OpenConnectionAsync();
        var roleSql = "SET ROLE \"" + role.Replace("\"", "\"\"") + "\"";
        await db.Database.ExecuteSqlRawAsync(roleSql); return db;
    }
    private async Task<string> Denied(Func<Task> action, string expected = "P0001")
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(action);
        var e = Assert.IsType<PostgresException>(failure.GetBaseException());
        output.WriteLine($"SQLSTATE {e.SqlState}: {e.MessageText}"); Assert.Equal(expected, e.SqlState); return e.SqlState;
    }
    private static async Task<long> Revision(Setup f) => await Scalar<long>(f.Cs, $"SELECT \"Revision\" FROM \"ProcessingGuards\" WHERE \"OrganizationId\"='{f.Org}'");
    private static async Task<long> Change(Setup f, ProcessingStateMutation mutation, long? expected = null, Guid? operation = null, params IInterceptor[] interceptors)
    {
        await using var db = await As(f.Cs, f.Admin, interceptors);
        return await new PostgresProcessingStateAdministration(db).Change(new(f.Org, operation ?? Guid.NewGuid(), expected ?? await Revision(f), mutation));
    }
    private async Task<Setup> Seed(string? migration = null)
    {
        var cs = await fixture.CreateDatabase(migration); var org = Guid.NewGuid(); var actor = Guid.NewGuid(); var context = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N"); var course = "c-" + suffix;
        // Round to PostgreSQL's microsecond precision for exact captured binding identity.
        var from = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds());
        var f = new Setup(cs, org, actor, context, course, from, "c2g_admin_"+suffix, "c2g_writer_"+suffix, "c2g_serving_"+suffix, Guid.NewGuid());
        await Sql(cs, $"""
            INSERT INTO "Organizations" VALUES ('{org}','Enabled',true);
            INSERT INTO "Actors" VALUES ('{actor}','Teacher');
            INSERT INTO "CourseRegistry" VALUES ('{course}','{org}','Verified');
            SELECT public.s3_change_authority('Membership','Grant','{org}','{actor}',0,0,NULL,NULL,NULL,'Teacher',0);
            SELECT public.s3_change_authority('Teacher','Grant','{org}','{actor}',0,1,'{course}',NULL,gen_random_uuid(),NULL,0);
            CREATE ROLE {f.Admin} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
            CREATE ROLE {f.Writer} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
            CREATE ROLE {f.Serving} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
            GRANT USAGE ON SCHEMA public TO {f.Admin},{f.Writer},{f.Serving};
            GRANT EXECUTE ON FUNCTION public.processing_change(uuid,uuid,bigint,text,jsonb),public.processing_find_command(uuid,uuid,bigint,text,jsonb) TO {f.Admin};
            GRANT EXECUTE ON FUNCTION public.processing_begin_write(uuid,bigint),public.processing_assert_current(uuid,text,uuid,bigint,timestamptz,uuid[]) TO {f.Writer};
            """);
        Assert.Equal(0, await Revision(f));
        await Change(f, new AddProcessingContext(context, 1, from.AddDays(-1)));
        await Change(f, new OpenProcessingBinding(course, context, 1, from));
        // Test-only affected rows. NO ImportAttempt / draft storage added to the migration/runtime.
        // The DB function re-locks and advances the real guard so direct EXECUTE cannot bypass
        // the CLR boundary. In composed calls two revision advances are intentional, in one tx.
        await Sql(cs, $"""
            CREATE TABLE public."C2GReadyProbe"(id uuid PRIMARY KEY,org uuid NOT NULL,course text NOT NULL,context uuid NOT NULL,
              context_version bigint NOT NULL,binding_from timestamptz NOT NULL,actor uuid NOT NULL,state text NOT NULL,version bigint NOT NULL);
            CREATE TABLE public."C2GReadyJournal"(operation uuid PRIMARY KEY,import_id uuid NOT NULL,
              audit uuid NOT NULL REFERENCES public."SecurityAudit"("AuditId") ON DELETE RESTRICT);
            INSERT INTO public."C2GReadyProbe" VALUES('{f.Draft}','{org}','{course}','{context}',1,'{from:O}','{actor}','Validating',1);
            CREATE FUNCTION public.c2g_test_complete(i uuid,op uuid) RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
            DECLARE r public."C2GReadyProbe"%ROWTYPE; v bigint; a uuid:=gen_random_uuid();
            BEGIN
              SELECT * INTO r FROM public."C2GReadyProbe" WHERE id=i;
              IF NOT FOUND THEN RAISE EXCEPTION 'missing_probe'; END IF;
              v:=public.processing_lock(r.org);
              PERFORM public.processing_begin_write(r.org,v);
              SELECT * INTO r FROM public."C2GReadyProbe" WHERE id=i FOR UPDATE;
              PERFORM public.processing_assert_current(r.org,r.course,r.context,r.context_version,r.binding_from,ARRAY[r.actor]);
              UPDATE public."C2GReadyProbe" SET state='Ready',version=version+1 WHERE id=i AND state='Validating' AND version=1;
              IF NOT FOUND THEN RAISE EXCEPTION 'probe_CAS'; END IF;
              INSERT INTO public."SecurityAudit"("AuditId","OrganizationId","Action","SubjectRef","Result","TraceId","OccurredAt")
                VALUES(a,r.org,'test_only_complete',i::text,'success',op::text,clock_timestamp());
              INSERT INTO public."C2GReadyJournal" VALUES(op,i,a);
              RETURN true;
            END $$;
            REVOKE ALL ON FUNCTION public.c2g_test_complete(uuid,uuid) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION public.c2g_test_complete(uuid,uuid) TO {f.Writer};
            """);
        return f;
    }
    private static async Task<bool> Complete(Setup f, long? expected = null, params IInterceptor[] interceptors)
    {
        await using var db = await As(f.Cs, f.Writer, interceptors);
        return await new PostgresProcessingWriteBoundary(db).Execute(f.Evidence, expected ?? await Revision(f),
            ct => db.Database.SqlQueryRaw<bool>("SELECT public.c2g_test_complete(@id,@op) AS \"Value\"",
                new NpgsqlParameter("id", f.Draft), new NpgsqlParameter("op", Guid.NewGuid())).SingleAsync(ct));
    }
    private sealed class Clock : IAuthorityClock
    {
        public AuthorityTime Read() { var at = DateTimeOffset.UtcNow; return new(at, at); }
    }
    private static async Task<bool> Read(Setup f)
    {
        await using var db = PostgresFixture.Open(f.Cs);
        return await new PostgresCurrentProcessingGate(db, new Clock()).Allows(new(f.Org,f.Course), f.Actor, ScopeAction.Read, default);
    }
    private static async Task<string> State(Setup f) => await Scalar<string>(f.Cs, $"SELECT state FROM \"C2GReadyProbe\" WHERE id='{f.Draft}'");
    private static AddProcessingRestriction Restriction(Setup f) => new(Guid.NewGuid(), f.Context, 1, null, 1, "opaque:synthetic");
    private static async Task WaitForLock(Setup f, string role)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        // SET ROLE doesn't change pg_stat_activity.usename; the exact blocked SQL is used instead.
        while (DateTime.UtcNow < until)
        {
            if (await Scalar<bool>(f.Cs, "SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND (query LIKE '%processing_%' OR query LIKE '%c2g_test_complete%'))")) return;
            await Task.Delay(10);
        }
        throw new TimeoutException("Expected real PostgreSQL lock wait: " + role);
    }

    [Fact]
    public async Task Operational_adapters_have_positive_controls_and_deny_direct_DML_DDL_and_capability_escalation()
    {
        var f = await Seed(); Assert.True(await Read(f)); var restriction = Restriction(f);
        await Change(f, restriction); Assert.False(await Read(f));
        await Change(f, new WithdrawProcessingRestriction(restriction.RestrictionId, 1)); Assert.True(await Read(f));
        await Change(f, new SetProcessingOrganizationState("Suspended")); Assert.False(await Read(f));
        await Change(f, new SetProcessingOrganizationState("Enabled")); Assert.True(await Read(f));
        foreach (var role in new[] { f.Admin,f.Writer,f.Serving })
        foreach (var table in new[] { "ProcessingRestrictions","CourseContextBindings","ProcessingContextVersions","Organizations","ProcessingGuards","ProcessingWriteJournal","SecurityAudit","C2GReadyProbe","C2GReadyJournal" })
        {
            var col = table switch { "C2GReadyProbe" => "id", "C2GReadyJournal" => "operation", "SecurityAudit" => "AuditId", "ProcessingRestrictions" => "RestrictionId", "CourseContextBindings" => "CourseId", "ProcessingContextVersions" => "ContextId", _ => "OrganizationId" };
            foreach (var sql in new[] { $"INSERT INTO \"{table}\" DEFAULT VALUES",$"UPDATE \"{table}\" SET \"{col}\"=\"{col}\"",$"DELETE FROM \"{table}\"",$"TRUNCATE \"{table}\"" })
                await Denied(() => Sql(f.Cs,$"SET ROLE {role}; {sql}"), "42501");
        }
        foreach (var role in new[] { f.Admin,f.Writer,f.Serving })
        foreach (var sql in new[] { "ALTER TABLE \"ProcessingRestrictions\" DISABLE TRIGGER ALL", "CREATE TABLE public.bypass(id int)", "SET session_replication_role=replica", "SELECT public.processing_lock('"+f.Org+"')" })
            await Denied(() => Sql(f.Cs,$"SET ROLE {role}; {sql}"),"42501");
        await using var serving = await As(f.Cs,f.Serving);
        await Denied(() => new PostgresProcessingStateAdministration(serving).Change(new(f.Org,Guid.NewGuid(),ExpectedRevision: 0,Mutation: restriction)),"42501");
        Assert.True(await Complete(f));
    }

    [Theory]
    [InlineData("restriction")][InlineData("binding-close")][InlineData("binding-replace")][InlineData("context-version")][InlineData("organization")]
    public async Task Writer_first_invalidates_old_snapshot_and_fresh_Ready_rechecks_actual_state(string kind)
    {
        var f = await Seed();
        if(kind=="binding-replace") await Change(f,new AddProcessingContext(f.Context,2,f.From.AddMinutes(-1)));
        var mutation = kind switch
        {
            "restriction" => (ProcessingStateMutation)Restriction(f),
            "binding-close" => new CloseProcessingBinding(f.Course,f.From),
            "binding-replace" => new ReplaceProcessingBinding(f.Course,f.From,f.Context,2),
            "context-version" => new AddProcessingContext(f.Context,2,f.From.AddMinutes(-1)),
            _ => new SetProcessingOrganizationState("Suspended")
        };
        var revision=await Revision(f);var pause=new PauseProcessingCommit();
        var writer=Change(f,mutation,revision,null,pause);await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var ready=Complete(f,revision);await WaitForLock(f,f.Writer);pause.Release.TrySetResult();await writer;
        await Denied(async()=>await ready,"40001");
        if(kind=="context-version") { Assert.True(await Complete(f)); Assert.True(await Read(f)); }
        else { await Denied(async()=>await Complete(f)); Assert.Equal("Validating",await State(f)); }
        if(kind=="restriction" || kind=="binding-close" || kind=="organization") Assert.False(await Read(f));
        if(kind=="binding-replace")
        {
            Assert.True(await Read(f)); // The course's new binding is valid, but this old captured draft is denied.
            Assert.Equal(2,await Scalar<long>(f.Cs,$"SELECT count(*) FROM \"CourseContextBindings\" WHERE \"CourseId\"='{f.Course}'"));
        }
        output.WriteLine(kind+": writer committed -> Ready 40001 -> fresh decision verified");
    }

    [Fact]
    public async Task Ready_first_then_real_restriction_adapter_commits_and_current_reader_denies()
    {
        var f=await Seed();var revision=await Revision(f);var pause=new PauseProcessingCommit();
        var ready=Complete(f,revision,pause);await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var command=Restriction(f);var op=Guid.NewGuid();var restriction=Change(f,command,revision,op);
        await WaitForLock(f,f.Admin);pause.Release.TrySetResult();Assert.True(await ready);
        await Denied(async()=>await restriction,"40001");
        await Change(f,command,await Revision(f),op);Assert.Equal("Ready",await State(f));Assert.False(await Read(f));
        output.WriteLine("Ready commit -> restriction 40001 -> fresh retry commit -> read denied");
    }

    [Fact]
    public async Task Phantom_insert_cannot_bypass_guard_with_operational_privileges()
    {
        var f=await Seed();await using var hold=PostgresFixture.Open(f.Cs);await using var tx=await hold.Database.BeginTransactionAsync();
        await hold.Database.ExecuteSqlInterpolatedAsync($"SELECT public.processing_lock({f.Org})");
        await Denied(()=>Sql(f.Cs,$"SET ROLE {f.Admin}; INSERT INTO \"ProcessingRestrictions\"(\"RestrictionId\",\"ContextId\",\"OriginVersion\",\"OrganizationId\",\"Operations\",\"State\",\"Version\") VALUES(gen_random_uuid(),'{f.Context}',1,'{f.Org}',1,'Active',1)"),"42501");
        var writer=Change(f,Restriction(f));await WaitForLock(f,f.Admin);Assert.False(writer.IsCompleted);await tx.CommitAsync();await writer;
        await Denied(async()=>await Complete(f));Assert.False(await Read(f));
    }

    [Fact]
    public async Task Journal_fingerprint_CAS_and_failure_roll_back_guard_mutation_and_audit()
    {
        var f=await Seed();var before=await Revision(f);var audits=await Scalar<long>(f.Cs,"SELECT count(*) FROM \"SecurityAudit\"");
        var journals=await Scalar<long>(f.Cs,"SELECT count(*) FROM \"ProcessingWriteJournal\"");
        await Sql(f.Cs,"ALTER TABLE \"ProcessingWriteJournal\" ADD CONSTRAINT injected_failure CHECK(\"Revision\"<0) NOT VALID");
        await Denied(async()=>await Change(f,Restriction(f)),"23514");
        Assert.Equal(before,await Revision(f));Assert.Equal(audits,await Scalar<long>(f.Cs,"SELECT count(*) FROM \"SecurityAudit\""));
        Assert.Equal(journals,await Scalar<long>(f.Cs,"SELECT count(*) FROM \"ProcessingWriteJournal\""));Assert.True(await Read(f));
        await Sql(f.Cs,"ALTER TABLE \"ProcessingWriteJournal\" DROP CONSTRAINT injected_failure");
        var op=Guid.NewGuid();var command=Restriction(f);var result=await Change(f,command,before,op);
        Assert.Equal(result,await Change(f,command,before,op));Assert.Equal(result,await Revision(f));
        await Denied(async()=>await Change(f,command with{ ResourceRef="different" },before,op),"23505");
        await Denied(async()=>await Change(f,new WithdrawProcessingRestriction(command.RestrictionId,1),before));
        var fail=new RejectProcessingCommit();var at=await Revision(f);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Change(f,new WithdrawProcessingRestriction(command.RestrictionId,1),at,null,fail));
        Assert.Equal(at,await Revision(f));Assert.False(await Read(f));
        output.WriteLine("guard + processing rows + journal + SecurityAudit atomic; replay unchanged; fingerprint 23505; rollback verified");
    }

    [Fact]
    public async Task Ready_probe_journal_failure_rolls_back_actual_boundary_and_mutation()
    {
        var f=await Seed();var before=await Revision(f);var audits=await Scalar<long>(f.Cs,"SELECT count(*) FROM \"SecurityAudit\"");
        await Sql(f.Cs,"ALTER TABLE \"C2GReadyJournal\" ADD CONSTRAINT fail_ready CHECK(false)");
        await Denied(async()=>await Complete(f),"23514");Assert.Equal("Validating",await State(f));Assert.Equal(before,await Revision(f));
        Assert.Equal(audits,await Scalar<long>(f.Cs,"SELECT count(*) FROM \"SecurityAudit\""));
        await Sql(f.Cs,"ALTER TABLE \"C2GReadyJournal\" DROP CONSTRAINT fail_ready");Assert.True(await Complete(f));
    }

    [Fact]
    public async Task Cross_org_context_subject_binding_invalid_real_mode_and_missing_evidence_deny()
    {
        var f=await Seed();var other=Guid.NewGuid();var context=Guid.NewGuid();var actor=Guid.NewGuid();
        await Sql(f.Cs,$"INSERT INTO \"Organizations\" VALUES('{other}','Enabled',true); INSERT INTO \"Actors\" VALUES('{actor}',NULL); INSERT INTO \"OrganizationMemberships\" VALUES('{other}','{actor}',1,'Active',NULL,0,1,NULL,NULL); INSERT INTO \"ProcessingContextVersions\"(\"ContextId\",\"Version\",\"OrganizationId\",\"ApprovalState\",\"SyntheticOnly\",\"EffectiveFrom\") VALUES('{context}',1,'{other}','SyntheticOnly',true,clock_timestamp()-interval '1 day')");
        await Denied(async()=>await Change(f,Restriction(f) with{ContextId=context}),"23503");
        await Denied(async()=>await Change(f,Restriction(f) with{SubjectActorId=actor}),"23503");
        await Denied(async()=>await Change(f,new ReplaceProcessingBinding(f.Course,f.From,context,1)),"23503");Assert.True(await Read(f));
        await Denied(async()=>await Change(f,new AddProcessingContext(Guid.NewGuid(),1,f.From,"ApprovedReal")));
        await Denied(async()=>await Change(f,new SetProcessingOrganizationState("Real")));
        await Denied(async()=>await Change(f,Restriction(f) with{Operations=0}),"23514");
        await using var db=await As(f.Cs,f.Writer);
        await Assert.ThrowsAsync<ArgumentException>(()=>new PostgresProcessingWriteBoundary(db).Execute(f.Evidence with{Subjects=[]},expectedRevision:0,mutate:_=>Task.FromResult(true)));
        await Denied(()=>Sql(f.Cs,$"SET ROLE {f.Writer}; SELECT public.processing_assert_current('{f.Org}','{f.Course}','{context}',1,'{f.From:O}',ARRAY['{f.Actor}'::uuid])"));
        await Sql(f.Cs,$"DELETE FROM \"ProcessingGuards\" WHERE \"OrganizationId\"='{f.Org}'");
        await Denied(async()=>await Change(f,new SetProcessingOrganizationState("Suspended"),2));
    }

    [Theory]
    [InlineData("restriction")][InlineData("context")][InlineData("binding")][InlineData("organization")]
    public async Task Existing_generic_EF_paths_are_denied_under_operational_role(string kind)
    {
        var f=await Seed();await using var db=await As(f.Cs,f.Admin);
        object row=kind switch
        {
            "restriction" => new ProcessingRestriction{RestrictionId=Guid.NewGuid(),OrganizationId=f.Org,ContextId=f.Context,OriginVersion=1,Operations=1,State="Active",Version=1},
            "context" => new ProcessingContextVersion{ContextId=f.Context,Version=2,OrganizationId=f.Org,ApprovalState="SyntheticOnly",SyntheticOnly=true,EffectiveFrom=f.From},
            "binding" => new CourseContextBinding{OrganizationId=f.Org,CourseId=f.Course,ContextId=f.Context,ContextVersion=1,EffectiveFrom=f.From.AddMinutes(1)},
            _ => new Organization{OrganizationId=Guid.NewGuid(),State="Enabled",SyntheticOnly=true}
        };
        db.Add(row);var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
        Assert.Equal("42501",Assert.IsType<PostgresException>(error.InnerException).SqlState);
        output.WriteLine(kind+" actual EF SaveChanges: 42501");
    }

    [Fact]
    public async Task Operational_profile_preflight_rejects_owner_inherited_and_column_DML()
    {
        var f=await Seed();foreach(var role in new[]{f.Admin,f.Writer,f.Serving})
            await Sql(f.Cs,$"SELECT public.processing_validate_operational_role('{role}'::regrole)");
        await Denied(()=>Sql(f.Cs,"SELECT public.processing_validate_operational_role(current_user::regrole)"),"42501");
        await Sql(f.Cs,$"GRANT pg_execute_server_program TO {f.Serving}");
        await Denied(()=>Sql(f.Cs,$"SELECT public.processing_validate_operational_role('{f.Serving}'::regrole)"),"42501");
        await Sql(f.Cs,$"REVOKE pg_execute_server_program FROM {f.Serving}");
        await Sql(f.Cs,$"GRANT UPDATE(\"State\") ON \"ProcessingRestrictions\" TO {f.Admin}");
        await Denied(()=>Sql(f.Cs,$"SELECT public.processing_validate_operational_role('{f.Admin}'::regrole)"),"42501");
        await Sql(f.Cs,$"REVOKE UPDATE(\"State\") ON \"ProcessingRestrictions\" FROM {f.Admin}; GRANT {f.Admin} TO {f.Writer}; GRANT DELETE ON \"ProcessingRestrictions\" TO {f.Admin}");
        // NOINHERIT still permits SET ROLE to a granted role. Explicit membership must also be audited.
        await Denied(()=>Sql(f.Cs,$"SELECT public.processing_validate_operational_role('{f.Admin}'::regrole)"),"42501");
        await Denied(()=>Sql(f.Cs,$"SELECT public.processing_validate_operational_role('{f.Writer}'::regrole)"),"42501");
        await Sql(f.Cs,$"ALTER ROLE {f.Writer} INHERIT");
        await Denied(()=>Sql(f.Cs,$"SELECT public.processing_validate_operational_role('{f.Writer}'::regrole)"),"42501");
    }

    [Fact]
    public async Task Real_wire_lost_ack_uses_read_only_reconciliation_without_replay()
    {
        var f=await Seed();var before=await Revision(f);var command=new ProcessingStateCommand(f.Org,Guid.NewGuid(),before,Restriction(f));
        await using var proxy=new DropCommitAcknowledgement(f.Cs);
        await using(var db=await As(proxy.ConnectionString,f.Admin))
            await Assert.ThrowsAnyAsync<NpgsqlException>(()=>new PostgresProcessingStateAdministration(db).Change(command));
        await proxy.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var verify=await As(f.Cs,f.Admin);var adapter=new PostgresProcessingStateAdministration(verify);
        Assert.Equal(before+1,await adapter.FindCommittedResult(command));Assert.Equal(before+1,await Revision(f));
        Assert.Null(await adapter.FindCommittedResult(command with{OperationId=Guid.NewGuid()}));
        Assert.Equal(before+1,await Revision(f));Assert.False(await Read(f));
        await Denied(()=>adapter.FindCommittedResult(command with{Mutation=Restriction(f)}),"23505");
        output.WriteLine("actual COMMIT CommandComplete dropped; no returned success; committed journal read once; no mutation replay");
    }

    [Fact]
    public async Task Real_adapter_and_reversed_privileged_lock_order_produce_detected_deadlock_abort()
    {
        var f=await Seed();var restriction=Restriction(f);await Change(f,restriction);
        await using var legacy=new NpgsqlConnection(f.Cs);await legacy.OpenAsync();await using var tx=await legacy.BeginTransactionAsync();
        await using(var lockRow=new NpgsqlCommand($"SELECT 1 FROM \"ProcessingRestrictions\" WHERE \"RestrictionId\"='{restriction.RestrictionId}' FOR UPDATE",legacy))await lockRow.ExecuteScalarAsync();
        static async Task<string> Outcome(Func<Task> action){try{await action();return "00000";}catch(Exception e) when(e.GetBaseException() is PostgresException){return ((PostgresException)e.GetBaseException()).SqlState;}}
        var writer=Outcome(async()=>{await Change(f,new WithdrawProcessingRestriction(restriction.RestrictionId,1));});
        await WaitForLock(f,f.Admin);
        await using var invert=new NpgsqlCommand($"SELECT public.processing_lock('{f.Org}')",legacy);
        var reverse=Outcome(async()=>{await invert.ExecuteScalarAsync();});
        var results=await Task.WhenAll(writer,reverse).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Contains("40P01",results);Assert.Contains("00000",results);await tx.RollbackAsync();
        output.WriteLine("legacy affected-row -> org vs actual adapter org -> guard -> row: "+string.Join(",",results));
        // A migration-owner is intentionally outside the operational privilege boundary.
        // It must quiesce operational writers and follow the protocol for emergency changes.
    }

    [Fact]
    public async Task Migration_failure_after_DDL_rolls_back_functions_trigger_guard_and_history_marker()
    {
        var cs=await fixture.CreateDatabase(Previous);var org=Guid.NewGuid();await Sql(cs,$"INSERT INTO \"Organizations\" VALUES('{org}','Enabled',true)");
        var fault=new FailProcessingMigration();await using(var db=PostgresFixture.Open(cs,fault))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>db.Database.MigrateAsync());
        Assert.True(fault.Triggered);
        Assert.False(await Scalar<bool>(cs,"SELECT to_regclass('public.\"ProcessingGuards\"') IS NOT NULL"));
        Assert.False(await Scalar<bool>(cs,"SELECT EXISTS(SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\"='20261011120000_ProcessingWriterGuardC2G')"));
        Assert.False(await Scalar<bool>(cs,"SELECT EXISTS(SELECT 1 FROM pg_trigger WHERE tgname='processing_organization_guard')"));
        await using var retry=PostgresFixture.Open(cs);await retry.Database.MigrateAsync();
        Assert.Equal(0,await Scalar<long>(cs,$"SELECT \"Revision\" FROM \"ProcessingGuards\" WHERE \"OrganizationId\"='{org}'"));
    }

    [Fact]
    public async Task Fixed_search_path_preserves_legacy_binding_check_despite_caller_temp_shadow()
    {
        var f=await Seed();var future=Guid.NewGuid();await Change(f,new AddProcessingContext(future,1,DateTimeOffset.UtcNow.AddHours(1)));
        await using var db=await As(f.Cs,f.Admin);
        await db.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE \"ProcessingContextVersions\" (\"ContextId\" uuid,\"Version\" bigint,\"EffectiveFrom\" timestamptz)");
        // The empty attacker temp table must not make the legacy trigger's EXISTS return false.
        var command=new ProcessingStateCommand(f.Org,Guid.NewGuid(),await Revision(f),new ReplaceProcessingBinding(f.Course,f.From,future,1));
        await Denied(()=>new PostgresProcessingStateAdministration(db).Change(command),"23514");
        Assert.True(await Read(f));
    }

    [Fact]
    public async Task Adapters_reject_ambient_and_dirty_contexts_and_missing_binding()
    {
        var f=await Seed();await using var db=await As(f.Cs,f.Admin);
        var command=new ProcessingStateCommand(f.Org,Guid.NewGuid(),await Revision(f),Restriction(f));
        await using(var tx=await db.Database.BeginTransactionAsync())
            await Assert.ThrowsAsync<InvalidOperationException>(()=>new PostgresProcessingStateAdministration(db).Change(command));
        db.Add(new Organization{OrganizationId=Guid.NewGuid(),State="Enabled",SyntheticOnly=true});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new PostgresProcessingStateAdministration(db).Change(command));
        await Change(f,new CloseProcessingBinding(f.Course,f.From));await Denied(async()=>await Complete(f));
        await Change(f,new OpenProcessingBinding(f.Course,f.Context,1,DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeSeconds())));
        // New/scheduled binding never rebinds the old draft capture.
        await Denied(async()=>await Complete(f));
    }

    [Fact]
    public async Task Migration_upgrade_empty_rollback_repeat_and_retained_history_refusal()
    {
        var cs=await fixture.CreateDatabase(Previous);var org=Guid.NewGuid();await Sql(cs,$"INSERT INTO \"Organizations\" VALUES('{org}','Enabled',true)");
        await using var db=PostgresFixture.Open(cs);var migrator=db.GetService<IMigrator>();await migrator.MigrateAsync(ProcessingMigration);
        Assert.Equal(0,await Scalar<long>(cs,$"SELECT \"Revision\" FROM \"ProcessingGuards\" WHERE \"OrganizationId\"='{org}'"));
        await migrator.MigrateAsync(ProcessingMigration);Assert.False(db.Database.HasPendingModelChanges());
        await migrator.MigrateAsync(Previous);Assert.False(await Scalar<bool>(cs,"SELECT to_regclass('public.\"ProcessingGuards\"') IS NOT NULL"));
        await migrator.MigrateAsync(ProcessingMigration);await migrator.MigrateAsync(ProcessingMigration);
        var script=migrator.GenerateScript(Previous,ProcessingMigration,options:MigrationsSqlGenerationOptions.Idempotent);
        await Sql(cs,script);await Sql(cs,script);
        var f=await Seed(ProcessingMigration);await Change(f,Restriction(f));await using var used=PostgresFixture.Open(f.Cs);
        await Denied(()=>used.GetService<IMigrator>().MigrateAsync(Previous));
        Assert.True(await Scalar<bool>(f.Cs,"SELECT to_regclass('public.\"ProcessingGuards\"') IS NOT NULL"));
        await used.Database.MigrateAsync();Assert.False(await Read(f));
        output.WriteLine("S3 upgrade/backfill; repeat + generated idempotent SQL twice; unused Down/Up; used Down refused, history preserved");
    }
}

internal sealed class PauseProcessingCommit : DbTransactionInterceptor
{
    public TaskCompletionSource Reached { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,TransactionEventData eventData,
        InterceptionResult result,CancellationToken cancellationToken=default)
    { Reached.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(20),cancellationToken);return result; }
}
internal sealed class RejectProcessingCommit : DbTransactionInterceptor
{
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,TransactionEventData eventData,
        InterceptionResult result,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("Injected before processing commit");
}

internal sealed class FailProcessingMigration : DbCommandInterceptor
{
    public bool Triggered { get; private set; }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command,CommandExecutedEventData eventData,int result,CancellationToken cancellationToken=default)
    {
        if(command.CommandText.Contains("CREATE TABLE public.\"ProcessingGuards\""))
        {Triggered=true;throw new InvalidOperationException("Injected after C2G DDL before migration commit");}
        return ValueTask.FromResult(result);
    }
}
