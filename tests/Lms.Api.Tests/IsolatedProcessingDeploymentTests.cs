using System.Net.Http.Json;
using System.Security.Cryptography;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Lms.Api.Tests;

// Disposable profile: real logins, no production DI/config changes or SET ROLE shortcut.
public sealed partial class IsolatedProcessingDeploymentTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17.6-alpine").Build();
    private string owner = "", runtime = "", processing = "", provisioning = "";
    private readonly Guid existingOrg = Guid.NewGuid();
    private static LmsDbContext Open(string cs) => new(new DbContextOptionsBuilder<LmsDbContext>().UseNpgsql(cs).Options);
    private static async Task Sql(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql,c); await cmd.ExecuteNonQueryAsync();
    }
    private static async Task<T> Scalar<T>(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql,c); return (T)(await cmd.ExecuteScalarAsync())!;
    }
    private static async Task Denied(Func<Task> action)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(action);
        Assert.Equal("42501", Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);
    }
    private static string Profile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName,"tools/processing_isolated/profile.sql"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName,"tools/processing_isolated/profile.sql"))
            + File.ReadAllText(Path.Combine(dir.FullName,"tools/processing_isolated/c2i-capabilities.sql"))
            + File.ReadAllText(Path.Combine(dir.FullName,"tools/processing_isolated/c2j-capabilities.sql"));
    }
    public async Task InitializeAsync()
    {
        await container.StartAsync();
        var bootstrap = container.GetConnectionString();
        foreach (var role in new[] { "c2h_owner", "c2h_runtime", "c2h_processing", "c2j_provisioning" })
        {
            // Generated only in memory. Never output connection strings/password values.
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await Sql(bootstrap,$"CREATE ROLE {role} LOGIN PASSWORD '{secret}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS");
            var cs = new NpgsqlConnectionStringBuilder(bootstrap) { Database="c2h_disposable",Username=role,Password=secret,IncludeErrorDetail=false }.ConnectionString;
            if(role=="c2h_owner") owner=cs; else if(role=="c2h_runtime") runtime=cs; else if(role=="c2h_processing") processing=cs; else provisioning=cs;
        }
        await Sql(bootstrap,"CREATE DATABASE c2h_disposable OWNER c2h_owner");
        await Sql(owner,"REVOKE ALL ON DATABASE c2h_disposable FROM PUBLIC; GRANT CONNECT ON DATABASE c2h_disposable TO c2h_runtime,c2h_processing,c2j_provisioning");
        await using(var db=Open(owner)) await db.GetService<IMigrator>().MigrateAsync("20261009153218_ScopedAuthorityS3");
        await Sql(owner,$"INSERT INTO \"Organizations\" VALUES('{existingOrg}','Enabled',true)");
        await using(var db=Open(owner)) await db.Database.MigrateAsync();
        // Trusted btree_gist C functions are bootstrap-owned. Deployment ACL only.
        await Sql(new NpgsqlConnectionStringBuilder(bootstrap){Database="c2h_disposable"}.ConnectionString,
            "REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public FROM PUBLIC; GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA public TO c2h_owner");
        await Sql(owner,Profile());
    }
    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    [Fact]
    public async Task Ownership_membership_defaults_and_callable_capabilities_are_closed()
    {
        Assert.Equal("c2h_runtime",await Scalar<string>(runtime,"SELECT session_user::text"));
        Assert.Equal("c2h_processing",await Scalar<string>(processing,"SELECT session_user::text"));
        Assert.Equal(0L,await Scalar<long>(owner,"SELECT count(*) FROM pg_auth_members WHERE member IN ('c2h_runtime'::regrole,'c2h_processing'::regrole)"));
        Assert.Equal(0L,await Scalar<long>(owner,"SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relowner<>'c2h_owner'::regrole"));
        Assert.Equal(0L,await Scalar<long>(owner,"SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.proowner<>'c2h_owner'::regrole AND NOT EXISTS(SELECT 1 FROM pg_depend d JOIN pg_extension e ON e.oid=d.refobjid WHERE d.classid='pg_proc'::regclass AND d.objid=p.oid AND d.deptype='e' AND e.extname='btree_gist' AND p.proowner='postgres'::regrole AND NOT p.prosecdef)"));
        foreach(var cs in new[]{runtime,processing})
        {
            Assert.False(await Scalar<bool>(cs,"SELECT has_schema_privilege(current_user,'public','CREATE') OR has_database_privilege(current_user,current_database(),'CREATE,TEMP')"));
            Assert.False(await Scalar<bool>(cs,"SELECT rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls FROM pg_roles WHERE rolname=current_user"));
            foreach(var role in new[]{"c2h_owner","postgres",cs==runtime?"c2h_processing":"c2h_runtime"})
                await Denied(()=>Sql(cs,$"SET ROLE {role}"));
        }
        Assert.Equal(5L,await Scalar<long>(runtime,"SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND has_function_privilege(current_user,p.oid,'EXECUTE')"));
        Assert.Equal(2L,await Scalar<long>(processing,"SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND has_function_privilege(current_user,p.oid,'EXECUTE')"));
        await Sql(owner,"CREATE TABLE public.c2h_future(id int); CREATE FUNCTION public.c2h_future() RETURNS int LANGUAGE sql AS 'SELECT 1'");
        foreach(var cs in new[]{runtime,processing})
        {
            await Denied(()=>Sql(cs,"SELECT * FROM public.c2h_future"));
            await Denied(()=>Sql(cs,"SELECT public.c2h_future()"));
        }
    }

    [Fact]
    public async Task Runtime_can_read_organization_names_but_cannot_mutate_profiles()
    {
        Assert.True(await Scalar<bool>(runtime,"SELECT has_table_privilege(current_user, '\"OrganizationProfiles\"', 'SELECT')"));
        Assert.False(await Scalar<bool>(runtime,"SELECT has_table_privilege(current_user, '\"OrganizationProfiles\"', 'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')"));
        Assert.False(await Scalar<bool>(processing,"SELECT has_table_privilege(current_user, '\"OrganizationProfiles\"', 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')"));
        Assert.True(await Scalar<bool>(runtime,"SELECT has_table_privilege(current_user, '\"OrganizationParticipantProfiles\"', 'SELECT')"));
        Assert.False(await Scalar<bool>(runtime,"SELECT has_table_privilege(current_user, '\"OrganizationParticipantProfiles\"', 'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')"));
        Assert.False(await Scalar<bool>(processing,"SELECT has_table_privilege(current_user, '\"OrganizationParticipantProfiles\"', 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')"));
        await Sql(owner,$"INSERT INTO \"OrganizationProfiles\" VALUES ('{existingOrg}', 'Runtime profile name')");
        Assert.Equal("Runtime profile name",await Scalar<string>(runtime,$"SELECT \"Name\" FROM \"OrganizationProfiles\" WHERE \"OrganizationId\"='{existingOrg}'"));
        await Denied(()=>Sql(runtime,$"UPDATE \"OrganizationProfiles\" SET \"Name\"='Forged' WHERE \"OrganizationId\"='{existingOrg}'"));
        await Denied(()=>Sql(processing,"SELECT * FROM \"OrganizationProfiles\""));
    }

    [Fact]
    public async Task Direct_processing_DML_DDL_EF_and_function_replacement_are_denied()
    {
        foreach(var cs in new[]{runtime,processing})
        {
            foreach(var table in new[]{"Organizations","ProcessingContextVersions","CourseContextBindings","ProcessingRestrictions","ProcessingGuards","ProcessingWriteJournal","SecurityAudit"})
            {
                var key=table switch {"ProcessingContextVersions"=>"ContextId","CourseContextBindings"=>"CourseId","ProcessingRestrictions"=>"RestrictionId","SecurityAudit"=>"AuditId",_=>"OrganizationId"};
                foreach(var sql in new[]{$"INSERT INTO \"{table}\" DEFAULT VALUES",$"UPDATE \"{table}\" SET \"{key}\"=\"{key}\"",$"DELETE FROM \"{table}\"",$"TRUNCATE \"{table}\"",$"ALTER TABLE \"{table}\" DISABLE TRIGGER ALL"})
                    await Denied(()=>Sql(cs,sql));
            }
            foreach(var sql in new[]{"CREATE TABLE public.c2h_bypass(id int)","CREATE SCHEMA c2h_bypass","SELECT * FROM \"Organizations\" FOR UPDATE","SET session_replication_role=replica","ALTER FUNCTION public.processing_change(uuid,uuid,bigint,text,jsonb) RENAME TO c2h_bypass","SELECT public.processing_lock('"+existingOrg+"')"})
                await Denied(()=>Sql(cs,sql));
            await using var db=Open(cs);
            db.Attach(new Organization{OrganizationId=existingOrg,State="Enabled",SyntheticOnly=true});
            db.Entry(db.Set<Organization>().Local.Single()).Property(x=>x.State).CurrentValue="Suspended";
            await Denied(()=>db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Guard_backfill_new_org_adapter_commands_and_repeat_migration()
    {
        Assert.Equal(1L,await Scalar<long>(owner,$"SELECT count(*) FROM \"ProcessingGuards\" WHERE \"OrganizationId\"='{existingOrg}' AND \"Revision\"=0"));
        var org=Guid.NewGuid(); var context=Guid.NewGuid(); var restriction=Guid.NewGuid();
        await Sql(owner,$"INSERT INTO \"Organizations\" VALUES('{org}','Enabled',true)");
        Assert.Equal(0L,await Scalar<long>(owner,$"SELECT \"Revision\" FROM \"ProcessingGuards\" WHERE \"OrganizationId\"='{org}'"));
        var course="c2h-course";
        var from=DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds());
        await Sql(owner,$"INSERT INTO \"CourseRegistry\" VALUES('{course}','{org}','Verified')");
        var mutations=new ProcessingStateMutation[]{new AddProcessingContext(context,1,from.AddDays(-1)),new OpenProcessingBinding(course,context,1,from),new CloseProcessingBinding(course,from),new AddProcessingRestriction(restriction,context,1,null,1),new WithdrawProcessingRestriction(restriction,1),new SetProcessingOrganizationState("Suspended")};
        for(var i=0;i<mutations.Length;i++)
        {
            await using var db=Open(processing);
            Assert.Equal(i+1L,await new PostgresProcessingStateAdministration(db).Change(new(org,Guid.NewGuid(),i,mutations[i])));
        }
        await using(var db=Open(owner)) { await db.Database.MigrateAsync(); await db.Database.MigrateAsync(); }
        await Sql(owner,Profile());
        Assert.Equal(6L,await Scalar<long>(owner,$"SELECT \"Revision\" FROM \"ProcessingGuards\" WHERE \"OrganizationId\"='{org}'"));
        await Denied(()=>Sql(runtime,$"SELECT public.processing_change('{org}','{Guid.NewGuid()}',6,'SetOrganizationState','{{\"State\":\"Enabled\"}}')"));
    }

    [Fact]
    public async Task Existing_Program_starts_and_Submit_Review_Revise_Accept_uses_scoped_login()
    {
        await using var app=new ApiFactory(owner,true);
        var assignment=await app.Seed(studentGroup:"A",teacherGroup:"A",scopedTeacher:true);
        using var student=app.Client(ApiFactory.Student); using var teacher=await app.SecureClient();
        Assert.True((await student.GetAsync("/api/health")).IsSuccessStatusCode);
        var path=$"/api/v1/problem-assignments/{assignment.Id}/submissions";
        var first=await student.PostAsJsonAsync(path,new{bodyMarkdown="Synthetic proof",attachmentIds=Array.Empty<Guid>()});
        Assert.Equal(201,(int)first.StatusCode);
        var one=await first.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var work=one.GetProperty("problemWorkId").GetGuid();
        var revision=await teacher.PostAsJsonAsync($"/api/v1/problem-works/{work}/reviews/needs-revision",new{commandId=Guid.NewGuid(),submissionId=one.GetProperty("submissionId").GetGuid(),expectedVersion=1,feedback="Explain"});
        Assert.Equal(200,(int)revision.StatusCode);
        var second=await student.PostAsJsonAsync(path,new{bodyMarkdown="Synthetic revised proof",attachmentIds=Array.Empty<Guid>(),expectedVersion=2});
        Assert.Equal(201,(int)second.StatusCode);
        var two=await second.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var accepted=await teacher.PostAsJsonAsync($"/api/v1/problem-works/{work}/reviews/accept",new{commandId=Guid.NewGuid(),submissionId=two.GetProperty("submissionId").GetGuid(),expectedVersion=3,feedback=(string?)null});
        Assert.Equal(200,(int)accepted.StatusCode);
        var persisted=await app.Reload(work);
        Assert.Equal(Lms.Domain.ProblemWorkStatus.Accepted,persisted.Status); Assert.Equal(2,persisted.Submissions.Count);
        using var stranger=app.Client(Guid.NewGuid());
        Assert.False((await stranger.PostAsJsonAsync(path,new{bodyMarkdown="denied",attachmentIds=Array.Empty<Guid>(),expectedVersion=4})).IsSuccessStatusCode);
    }

    [Fact]
    public async Task S2_login_audit_uses_narrow_runtime_capability_without_table_insert()
    {
        var userId=Guid.NewGuid(); var actor=Guid.NewGuid();
        var secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using(var db=Open(owner)) await new PostgresIdentityMapping(db,TimeProvider.System).CreateInactive(userId,actor,existingOrg,"c2h-synthetic");
        await using(var db=Open(owner))
        {
            var user=await db.Users.SingleAsync();
            user.PasswordHash=new PasswordHasher<IdentityUser<Guid>>().HashPassword(user,secret);
            (await db.Set<ActorIdentity>().SingleAsync()).IsActive=true;
            await db.SaveChangesAsync();
        }
        SessionResult login;
        await using(var db=Open(runtime))
            login=await new PostgresIdentitySessions(db,TimeProvider.System,true).Authenticate("c2h-synthetic",secret);
        Assert.Equal(SessionOutcome.Success,login.Outcome);
        Assert.Equal(1L,await Scalar<long>(owner,"SELECT count(*) FROM \"IdentitySessions\""));
        Assert.Equal(1L,await Scalar<long>(owner,"SELECT count(*) FROM \"SecurityAudit\" WHERE \"Action\"='session_created'"));
        Assert.True(await Scalar<bool>(runtime,"SELECT has_function_privilege(current_user,'public.s2_session_audit(uuid,text,timestamptz)','EXECUTE')"));
        // A direct table grant remains forbidden and is not the cutover mechanism.
        await using var c=new NpgsqlConnection(owner); await c.OpenAsync(); await using var tx=await c.BeginTransactionAsync();
        await using(var cmd=new NpgsqlCommand("GRANT INSERT ON \"SecurityAudit\" TO c2h_runtime",c,tx)) await cmd.ExecuteNonQueryAsync();
        await Denied(async()=>{await using var cmd=new NpgsqlCommand("SELECT public.processing_validate_operational_role('c2h_runtime')",c,tx); await cmd.ExecuteNonQueryAsync();});
        await tx.RollbackAsync();
        Assert.False(await Scalar<bool>(runtime,"SELECT has_table_privilege(current_user,'\"SecurityAudit\"','INSERT')"));
    }
}
