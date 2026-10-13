using System.Security.Cryptography;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Lms.Api.Tests;

public sealed partial class IsolatedProcessingDeploymentTests
{
    private async Task<IdentityLink> Provision(Guid user,Guid actor,string name)
    {
        await using var db=Open(provisioning);
        return await new PostgresIdentityMapping(db,TimeProvider.System,true).CreateInactive(user,actor,existingOrg,name);
    }
    private async Task AssertIdentityCount(int expected)
    {
        await using var db=Open(owner);
        Assert.Equal(expected,await db.Users.CountAsync());
        Assert.Equal(expected,await db.Set<ActorIdentity>().CountAsync());
        Assert.Equal(expected,await db.Set<AccountSecurityState>().CountAsync());
        Assert.Equal(expected,await db.Set<SecurityAudit>().CountAsync(x=>x.Action=="identity_mapping_created"));
    }
    [Fact]
    public async Task Provisioning_LOGIN_creates_inactive_identity_and_only_mapping_audit()
    {
        Assert.Equal("c2j_provisioning",await Scalar<string>(provisioning,"SELECT session_user::text"));
        var user=Guid.NewGuid(); var actor=Guid.NewGuid();
        Assert.Equal(new IdentityLink(user,actor,false),await Provision(user,actor,"Synthetic-\u00e9"));
        await AssertIdentityCount(1);
        await using var db=Open(owner);
        var u=await db.Users.SingleAsync();
        Assert.Null(u.PasswordHash); Assert.False(u.EmailConfirmed); Assert.False(u.PhoneNumberConfirmed); Assert.False(u.TwoFactorEnabled);
        Assert.Equal(new UpperInvariantLookupNormalizer().NormalizeName("Synthetic-\u00e9"),u.NormalizedUserName);
        var state=await db.Set<AccountSecurityState>().SingleAsync();
        Assert.Equal("Enabled",state.State); Assert.Equal(1,state.Version); Assert.Equal(1,state.CredentialEpoch);
        var audit=await db.Set<SecurityAudit>().SingleAsync();
        Assert.Equal(actor,audit.ActorId); Assert.Equal(existingOrg,audit.OrganizationId); Assert.Equal("inactive",audit.Result);
        foreach(var table in new[]{"IdentitySessions","RefreshVerifiers","OrganizationMemberships","EnrollmentScopes","TeacherAssignmentScopes","AccessEnrollments","AccessTeachers"})
            Assert.Equal(0L,await Scalar<long>(owner,$"SELECT count(*) FROM \"{table}\""));
        Assert.Equal(SessionOutcome.Denied,(await Login("Synthetic-\u00e9","synthetic-password")).Outcome);
        await using var runtimeDb=Open(runtime);
        Assert.Equal(new IdentityLink(user,actor,false),await new PostgresIdentityMapping(runtimeDb,TimeProvider.System,true).Find(user));
        using var key=RSA.Create(2048); var clock=new IssuingClock(); var jwt=new ComponentJwt(key,"c2j","c2j",clock);
        await using var issuerDb=Open(runtime);
        Assert.Null(await new PostgresComponentTokenIssuer(issuerDb,clock,jwt,true).Issue(user,Guid.NewGuid(),existingOrg));
    }
    [Fact]
    public async Task Provisioning_duplicate_user_actor_and_normalized_name_never_rebind_or_duplicate()
    {
        var user=Guid.NewGuid(); var actor=Guid.NewGuid(); await Provision(user,actor,"unique");
        foreach(var candidate in new[]{(user,actor,"unique"),(user,Guid.NewGuid(),"other"),(Guid.NewGuid(),actor,"other"),(Guid.NewGuid(),Guid.NewGuid(),"UNIQUE")})
        {
            var e=await Assert.ThrowsAnyAsync<Exception>(()=>Provision(candidate.Item1,candidate.Item2,candidate.Item3));
            Assert.Equal("23505",Assert.IsType<PostgresException>(e.GetBaseException()).SqlState);
        }
        await AssertIdentityCount(1);
        Assert.Equal(1L,await Scalar<long>(owner,"SELECT count(*) FROM \"Actors\""));
        await using var db=Open(owner); var link=await db.Set<ActorIdentity>().SingleAsync(); Assert.Equal(user,link.UserId); Assert.Equal(actor,link.ActorId);
    }
    [Fact]
    public async Task Provisioning_existing_unmapped_actor_keeps_stable_identity()
    {
        var user=Guid.NewGuid(); var actor=Guid.NewGuid();
        await Sql(owner,$"INSERT INTO \"Actors\" VALUES('{actor}')");
        await Provision(user,actor,"existing-actor");
        await AssertIdentityCount(1); Assert.Equal(1L,await Scalar<long>(owner,"SELECT count(*) FROM \"Actors\""));
    }
    [Theory]
    [InlineData("same")][InlineData("actor")][InlineData("name")]
    public async Task Provisioning_concurrent_creation_has_one_winner(string conflict)
    {
        var user=Guid.NewGuid(); var actor=Guid.NewGuid(); var secondActor=conflict=="name"?Guid.NewGuid():actor;
        // Hold actor insertion so both real LOGIN calls overlap deterministically.
        await using var held=new NpgsqlConnection(owner); await held.OpenAsync(); await using var tx=await held.BeginTransactionAsync();
        await Execute(held,tx,$"INSERT INTO \"Actors\" VALUES('{actor}')");
        if(secondActor!=actor) await Execute(held,tx,$"INSERT INTO \"Actors\" VALUES('{secondActor}')");
        async Task<string> Run(Guid u,Guid a,string name)
        { try { await Provision(u,a,name); return "success"; } catch(Exception e) when(e.GetBaseException() is PostgresException pg){return pg.SqlState;} }
        var a=Run(user,actor,"race"); var b=Run(conflict=="same"?user:Guid.NewGuid(),secondActor,conflict=="actor"?"different":"RACE");
        var waiting=false; var until=DateTime.UtcNow.AddSeconds(10);
        while(DateTime.UtcNow<until)
        {
            waiting=await Scalar<long>(provisioning,"SELECT count(*) FROM pg_stat_activity WHERE usename=current_user AND wait_event_type='Lock'")>=2;
            if(waiting) break; await Task.Delay(20);
        }
        Assert.True(waiting); await tx.CommitAsync();
        var results=await Task.WhenAll(a,b).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(results,x=>x=="success"); Assert.Single(results,x=>x=="23505"); await AssertIdentityCount(1);
    }
    [Fact]
    public async Task Provisioning_audit_failure_and_precommit_failure_roll_back_all_four_rows()
    {
        var user=Guid.NewGuid(); var actor=Guid.NewGuid();
        await using(var db=new LmsDbContext(new DbContextOptionsBuilder<LmsDbContext>().UseNpgsql(provisioning).AddInterceptors(new BeforeCommitFailure()).Options))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>new PostgresIdentityMapping(db,TimeProvider.System,true).CreateInactive(user,actor,existingOrg,"rollback"));
        await AssertIdentityCount(0); Assert.Equal(0L,await Scalar<long>(owner,"SELECT count(*) FROM \"Actors\""));
        await Sql(owner,"""
            CREATE FUNCTION public.c2j_fail_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected_audit_failure'; END $$;
            CREATE TRIGGER c2j_fail_audit BEFORE INSERT ON public."SecurityAudit" FOR EACH ROW EXECUTE FUNCTION public.c2j_fail_audit();
            """);
        var e=await Assert.ThrowsAnyAsync<Exception>(()=>Provision(user,actor,"rollback"));
        Assert.Equal("P0001",Assert.IsType<PostgresException>(e.GetBaseException()).SqlState);
        await AssertIdentityCount(0); Assert.Equal(0L,await Scalar<long>(owner,"SELECT count(*) FROM \"Actors\""));
        await Sql(owner,"DROP TRIGGER c2j_fail_audit ON public.\"SecurityAudit\"");
        await Provision(user,actor,"rollback"); await AssertIdentityCount(1);
    }
    [Fact]
    public async Task Provisioning_lost_COMMIT_ACK_is_unknown_outcome_and_duplicate_retry_conflicts()
    {
        var user=Guid.NewGuid(); var actor=Guid.NewGuid(); IdentityLink? result=null;
        await using var proxy=new DropCommitAcknowledgement(provisioning);
        await using(var db=Open(proxy.ConnectionString))
            await Assert.ThrowsAnyAsync<Exception>(async()=>result=await new PostgresIdentityMapping(db,TimeProvider.System,true).CreateInactive(user,actor,existingOrg,"lost-ack"));
        Assert.Null(result); await proxy.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10)); await AssertIdentityCount(1);
        var e=await Assert.ThrowsAnyAsync<Exception>(()=>Provision(user,actor,"lost-ack"));
        Assert.Equal("23505",Assert.IsType<PostgresException>(e.GetBaseException()).SqlState); await AssertIdentityCount(1);
        await using var dbCheck=Open(runtime); Assert.Equal(new IdentityLink(user,actor,false),await new PostgresIdentityMapping(dbCheck,TimeProvider.System,true).Find(user));
    }
    [Fact]
    public async Task Provisioning_principal_and_runtime_have_no_direct_identity_DML_or_privilege_escalation()
    {
        foreach(var cs in new[]{runtime,processing,provisioning})
        {
            foreach(var table in new[]{"Actors","AspNetUsers","ActorIdentities","AccountSecurityState"})
            {
                var id=table=="Actors"?"ActorId":table=="AspNetUsers"?"Id":"UserId";
                foreach(var sql in new[]{$"INSERT INTO \"{table}\" DEFAULT VALUES",$"UPDATE \"{table}\" SET \"{id}\"=\"{id}\"",$"DELETE FROM \"{table}\"",$"ALTER TABLE \"{table}\" DISABLE TRIGGER ALL",$"TRUNCATE \"{table}\""})
                    await Denied(()=>Sql(cs,sql));
                Assert.False(await Scalar<bool>(cs,$"SELECT has_table_privilege(current_user,'\"{table}\"','INSERT,UPDATE,DELETE') OR has_any_column_privilege(current_user,'\"{table}\"','INSERT,UPDATE')"));
            }
            foreach(var role in new[]{"c2h_owner","postgres","c2j_provisioning","c2h_runtime","c2h_processing"})
                if(role!=await Scalar<string>(cs,"SELECT current_user::text")) await Denied(()=>Sql(cs,$"SET ROLE {role}"));
            await Denied(()=>Sql(cs,"CREATE TABLE public.c2j_bypass(id int)"));
            await Denied(()=>Sql(cs,$"SELECT public.processing_begin_write('{existingOrg}',0)"));
            await Denied(()=>Sql(cs,$"SELECT public.processing_assert_current('{existingOrg}','synthetic','{Guid.NewGuid()}',1,now(),ARRAY['{Guid.NewGuid()}'::uuid])"));
        }
        foreach(var cs in new[]{runtime,processing})
            await Denied(()=>Sql(cs,$"SELECT public.s2_create_inactive('{Guid.NewGuid()}','{Guid.NewGuid()}','{existingOrg}','forged','FORGED',now())"));
        Assert.False(await Scalar<bool>(provisioning,"SELECT has_schema_privilege(current_user,'public','CREATE') OR has_database_privilege(current_user,current_database(),'CREATE,TEMP')"));
        Assert.False(await Scalar<bool>(provisioning,"SELECT rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls FROM pg_roles WHERE rolname=current_user"));
        Assert.Equal(0L,await Scalar<long>(owner,"SELECT count(*) FROM pg_auth_members WHERE member='c2j_provisioning'::regrole"));
        await Denied(()=>Sql(provisioning,"ALTER FUNCTION public.s2_create_inactive(uuid,uuid,uuid,text,text,timestamptz) RENAME TO bypass"));
        await Denied(()=>Sql(provisioning,"INSERT INTO \"SecurityAudit\" DEFAULT VALUES"));
        Assert.Equal(1L,await Scalar<long>(provisioning,"SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND has_function_privilege(current_user,p.oid,'EXECUTE')"));
        await Denied(()=>Sql(provisioning,$"SELECT public.s3_change_authority('Membership','Grant','{existingOrg}','{Guid.NewGuid()}',0,NULL,NULL,NULL,NULL,'Teacher',15)"));
        await Denied(()=>Sql(provisioning,"INSERT INTO \"IdentitySessions\" DEFAULT VALUES"));
        await Denied(()=>Sql(provisioning,"SELECT public.s2_rehash_password(NULL,NULL,NULL)"));
        await Denied(()=>Sql(runtime,"SELECT public.s2_rehash_password(NULL,NULL,NULL)"));
        await Sql(owner,"SELECT public.processing_validate_operational_role('c2h_runtime'); SELECT public.processing_validate_operational_role('c2h_processing'); SELECT public.processing_validate_operational_role('c2j_provisioning')");
    }
    [Fact]
    public async Task Restricted_S2_rehash_preserves_password_and_atomic_session_audit_without_UPDATE_grant()
    {
        var seed=await IdentitySeed(); string old;
        await using(var db=Open(owner))
        {
            var u=await db.Users.SingleAsync();
            old=new PasswordHasher<IdentityUser<Guid>>(Options.Create(new PasswordHasherOptions{CompatibilityMode=PasswordHasherCompatibilityMode.IdentityV2})).HashPassword(u,seed.Password);
            u.PasswordHash=old; await db.SaveChangesAsync();
        }
        await using(var db=new LmsDbContext(new DbContextOptionsBuilder<LmsDbContext>().UseNpgsql(runtime).AddInterceptors(new BeforeCommitFailure()).Options))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>new PostgresIdentitySessions(db,TimeProvider.System,true).Authenticate(seed.Name,seed.Password));
        await using(var db=Open(owner)) Assert.Equal(old,(await db.Users.SingleAsync()).PasswordHash);
        Assert.Equal(SessionOutcome.Success,(await Login(seed.Name,seed.Password)).Outcome);
        await using(var db=Open(owner))
        {
            var u=await db.Users.SingleAsync(); Assert.NotEqual(old,u.PasswordHash);
            Assert.Equal(PasswordVerificationResult.Success,new PasswordHasher<IdentityUser<Guid>>().VerifyHashedPassword(u,u.PasswordHash!,seed.Password));
            Assert.Single(await db.Set<IdentitySession>().ToListAsync());
            Assert.Single(await db.Set<SecurityAudit>().Where(x=>x.Action=="session_created").ToListAsync());
        }
    }
}
