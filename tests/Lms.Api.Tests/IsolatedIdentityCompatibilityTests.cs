using System.Data.Common;
using System.Security.Cryptography;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Lms.Api.Tests;

public sealed partial class IsolatedProcessingDeploymentTests
{
    private async Task<(Guid User, Guid Actor, string Name, string Password)> IdentitySeed()
    {
        var user=Guid.NewGuid(); var actor=Guid.NewGuid(); var name="c2i-"+user;
        var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using(var db=Open(provisioning))
            await new PostgresIdentityMapping(db,TimeProvider.System,true).CreateInactive(user,actor,existingOrg,name);
        await using(var db=Open(owner))
        {
            var u=await db.Users.SingleAsync(x=>x.Id==user);
            u.PasswordHash=new PasswordHasher<IdentityUser<Guid>>().HashPassword(u,password);
            (await db.Set<ActorIdentity>().SingleAsync(x=>x.UserId==user)).IsActive=true;
            await db.SaveChangesAsync();
        }
        return (user,actor,name,password);
    }
    private async Task<SessionResult> Login(string name,string password)
    {
        await using var db=Open(runtime);
        return await new PostgresIdentitySessions(db,TimeProvider.System,true).Authenticate(name,password);
    }
    [Fact]
    public async Task Restricted_S2_authenticate_rotate_revoke_revoke_all_and_audit()
    {
        var seed=await IdentitySeed();
        var first=await Login(seed.Name,seed.Password); Assert.Equal(SessionOutcome.Success,first.Outcome);
        await using(var db=Open(runtime))
        {
            var next=await new PostgresIdentitySessions(db,TimeProvider.System,true).Rotate(first.RefreshToken!);
            Assert.Equal(SessionOutcome.Success,next.Outcome);
        }
        await using(var db=Open(runtime)) await new PostgresIdentitySessions(db,TimeProvider.System,true).Revoke(seed.User,first.Identity!.SessionId);
        var second=await Login(seed.Name,seed.Password); var third=await Login(seed.Name,seed.Password);
        Assert.Equal(SessionOutcome.Success,second.Outcome); Assert.Equal(SessionOutcome.Success,third.Outcome);
        await using(var db=Open(runtime)) await new PostgresIdentitySessions(db,TimeProvider.System,true).RevokeAll(seed.User);
        await using var check=Open(owner);
        var audit=await check.Set<SecurityAudit>().Where(x=>x.SubjectRef==seed.User.ToString()).ToListAsync();
        Assert.Equal(8,audit.Count);
        Assert.Equal(3,audit.Count(x=>x.Action=="session_created")); Assert.Equal(2,audit.Count(x=>x.Action=="sessions_revoked"));
        Assert.Single(audit,x=>x.Action=="session_rotated"); Assert.Single(audit,x=>x.Action=="session_revoked");
        var mapping=Assert.Single(audit,x=>x.Action=="identity_mapping_created");
        Assert.Equal(seed.Actor,mapping.ActorId); Assert.Equal(existingOrg,mapping.OrganizationId);
        Assert.All(audit.Where(x=>x!=mapping),x=>{Assert.Null(x.ActorId);Assert.Null(x.OrganizationId);Assert.Null(x.ContextId);Assert.Null(x.EvidenceRef);});
        Assert.All(await check.Set<IdentitySession>().ToListAsync(),x=>Assert.NotNull(x.RevokedAt));
        await Sql(owner,"SELECT public.processing_validate_operational_role('c2h_runtime'); SELECT public.processing_validate_operational_role('c2h_processing')");
    }
    [Fact]
    public async Task Restricted_mapping_creation_is_blocked_by_non_audit_privileges_without_partial_effects()
    {
        var user=Guid.NewGuid(); var actor=Guid.NewGuid();
        await using(var db=Open(runtime))
            await Denied(()=>new PostgresIdentityMapping(db,TimeProvider.System,true).CreateInactive(user,actor,existingOrg,"blocked"));
        await using var check=Open(owner);
        Assert.False(await check.Users.AnyAsync(x=>x.Id==user));
        Assert.False(await check.Set<ActorRecord>().AnyAsync(x=>x.ActorId==actor));
        Assert.False(await check.Set<SecurityAudit>().AnyAsync(x=>x.SubjectRef==user.ToString()));
        foreach(var table in new[]{"Actors","AspNetUsers","ActorIdentities","AccountSecurityState"})
            Assert.False(await Scalar<bool>(runtime,$"SELECT has_table_privilege(current_user,'\"{table}\"','INSERT')"));
    }
    [Fact]
    public async Task Restricted_audit_rejects_forged_action_old_source_and_mapping_actor()
    {
        var seed=await IdentitySeed(); var login=await Login(seed.Name,seed.Password); var sid=login.Identity!.SessionId;
        foreach(var action in new[]{"processing_changed","membership_granted","identity_mapping_created",new string('x',1000),"session_created"})
            await Denied(()=>Sql(runtime,$"SELECT public.s2_session_audit('{sid}','{action}',now())"));
        await Denied(()=>Sql(runtime,$"SELECT public.s2_mapping_audit('{seed.User}',now())"));
        // There is no actor/organization/evidence parameter or JSON payload to forge.
        Assert.Equal("p_session uuid, p_action text, p_at timestamp with time zone",await Scalar<string>(owner,
            "SELECT pg_get_function_arguments('public.s2_session_audit(uuid,text,timestamptz)'::regprocedure)"));
        await Denied(()=>Sql(processing,$"SELECT public.s2_session_audit('{sid}','session_created',now())"));
    }
    private sealed class BeforeCommitFailure : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData,InterceptionResult result,CancellationToken cancellationToken=default)
            => throw new InvalidOperationException("injected_before_commit");
    }
    [Fact]
    public async Task Restricted_session_and_audit_rollback_atomically_after_capability_succeeds()
    {
        var seed=await IdentitySeed();
        await using(var db=new LmsDbContext(new DbContextOptionsBuilder<LmsDbContext>().UseNpgsql(runtime).AddInterceptors(new BeforeCommitFailure()).Options))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>new PostgresIdentitySessions(db,TimeProvider.System,true).Authenticate(seed.Name,seed.Password));
        await using var check=Open(owner);
        Assert.Empty(await check.Set<IdentitySession>().ToListAsync());
        Assert.Single(await check.Set<SecurityAudit>().ToListAsync()); // mapping only
        Assert.Equal(SessionOutcome.Success,(await Login(seed.Name,seed.Password)).Outcome);
    }
    private async Task IssuingEnrollment(Guid actor)
    {
        var course="c2i-"+actor;
        await Sql(owner,$"INSERT INTO \"CourseRegistry\" VALUES('{course}','{existingOrg}','Verified'); INSERT INTO \"CourseGroups\" VALUES('{course}','g')");
        await using var db=Open(runtime);
        await new PostgresScopedAuthorityAdministration(db).Change(new(AuthorityKind.Enrollment,AuthorityOperation.Grant,existingOrg,actor,0,1,course,"g"));
    }
    private sealed class IssuingClock : IAuthorityClock
    {
        public AuthorityTime? Read() { var now=DateTimeOffset.UtcNow; return new(now,now); }
    }
    [Fact]
    public async Task Restricted_issuer_S3_membership_and_delayed_JWT_revocation()
    {
        var seed=await IdentitySeed(); var login=await Login(seed.Name,seed.Password);
        async Task<AuthorityChange> Change(AuthorityCommand command)
        { await using var db=Open(runtime); return await new PostgresScopedAuthorityAdministration(db).Change(command); }
        await Change(new(AuthorityKind.Membership,AuthorityOperation.Grant,existingOrg,seed.Actor,0,Role:ScopeRole.Student));
        await IssuingEnrollment(seed.Actor);
        using var key=RSA.Create(2048); var clock=new IssuingClock(); var jwt=new ComponentJwt(key,"c2i","c2i",clock);
        async Task<AccessToken?> Issue(Guid org)
        { await using var db=Open(runtime); return await new PostgresComponentTokenIssuer(db,clock,jwt,true).Issue(seed.User,login.Identity!.SessionId,org); }
        var token=await Issue(existingOrg); Assert.NotNull(token); Assert.NotNull(jwt.Validate(token!));
        Assert.Null(await Issue(Guid.NewGuid())); Assert.Null(await Issue(Guid.Empty));
        await Change(new(AuthorityKind.Membership,AuthorityOperation.Revoke,existingOrg,seed.Actor,1));
        Assert.Null(await Issue(existingOrg)); Assert.NotNull(jwt.Validate(token!));
        await using(var db=Open(runtime)) Assert.Null(await new PostgresScopedAuthorityReader(db,TimeProvider.System).ResolveCurrent(existingOrg,seed.Actor));
    }
    [Fact]
    public async Task Restricted_organization_lock_is_FOR_UPDATE_until_rollback_and_denies_invalid_org()
    {
        await using var held=new NpgsqlConnection(runtime); await held.OpenAsync(); await using var tx=await held.BeginTransactionAsync();
        await using(var cmd=new NpgsqlCommand($"SELECT * FROM public.s2_lock_issuing_organization('{existingOrg}')",held,tx))
            await cmd.ExecuteNonQueryAsync();
        await using var other=new NpgsqlConnection(runtime); await other.OpenAsync();
        await using(var timeout=new NpgsqlCommand("SET lock_timeout='150ms'",other)) await timeout.ExecuteNonQueryAsync();
        await using(var cmd=new NpgsqlCommand($"SELECT public.s3_change_authority('Membership','Grant','{existingOrg}','{Guid.NewGuid()}',0,NULL,NULL,NULL,NULL,'Student',0)",other))
            Assert.Equal("55P03",(await Assert.ThrowsAsync<PostgresException>(()=>cmd.ExecuteNonQueryAsync())).SqlState);
        await tx.RollbackAsync();
        Assert.Equal(1L,await Scalar<long>(runtime,$"SELECT count(*) FROM public.s2_lock_issuing_organization('{existingOrg}')"));
        Assert.Equal(0L,await Scalar<long>(runtime,$"SELECT count(*) FROM public.s2_lock_issuing_organization('{Guid.NewGuid()}')"));
        await using(var db=Open(processing)) await new PostgresProcessingStateAdministration(db).Change(new(existingOrg,Guid.NewGuid(),0,new SetProcessingOrganizationState("Suspended")));
        Assert.Equal(0L,await Scalar<long>(runtime,$"SELECT count(*) FROM public.s2_lock_issuing_organization('{existingOrg}')"));
    }
    [Fact]
    public async Task Restricted_S2_lost_commit_ACK_commits_session_and_audit_but_returns_no_secret()
    {
        var seed=await IdentitySeed();
        await using var proxy=new DropCommitAcknowledgement(runtime);
        await using(var db=Open(proxy.ConnectionString))
        {
            // The wire fixture relays one connection; retain it across S2's discovery query.
            await db.Database.OpenConnectionAsync();
            var error=await Assert.ThrowsAnyAsync<Exception>(()=>new PostgresIdentitySessions(db,TimeProvider.System,true).Authenticate(seed.Name,seed.Password));
            Assert.True(proxy.Dropped.Task.IsCompletedSuccessfully,error.GetBaseException().Message);
        }
        await proxy.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var check=Open(owner);
        var session=Assert.Single(await check.Set<IdentitySession>().ToListAsync());
        Assert.Single(await check.Set<RefreshVerifier>().ToListAsync());
        Assert.Single(await check.Set<SecurityAudit>().Where(x=>x.ResourceRef==session.SessionId.ToString() && x.Action=="session_created").ToListAsync());
    }
    [Fact]
    public async Task Restricted_issuer_lost_commit_ACK_never_returns_JWT()
    {
        var seed=await IdentitySeed(); var login=await Login(seed.Name,seed.Password);
        await using(var db=Open(runtime)) await new PostgresScopedAuthorityAdministration(db).Change(
            new(AuthorityKind.Membership,AuthorityOperation.Grant,existingOrg,seed.Actor,0,Role:ScopeRole.Student));
        await IssuingEnrollment(seed.Actor);
        using var key=RSA.Create(2048); var clock=new IssuingClock(); var jwt=new ComponentJwt(key,"c2i","c2i",clock);
        await using var proxy=new DropCommitAcknowledgement(runtime);
        await using var issuing=Open(proxy.ConnectionString);
        AccessToken? result=null;
        await Assert.ThrowsAnyAsync<Exception>(async()=>result=await new PostgresComponentTokenIssuer(issuing,clock,jwt,true).Issue(seed.User,login.Identity!.SessionId,existingOrg));
        Assert.Null(result); await proxy.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
    private static async Task Execute(NpgsqlConnection c,NpgsqlTransaction tx,string sql)
    { await using var cmd=new NpgsqlCommand(sql,c,tx); await cmd.ExecuteNonQueryAsync(); }
    [Fact]
    public async Task Restricted_serialization_failure_rolls_back_session_and_capability_audit()
    {
        var seed=await IdentitySeed(); var login=await Login(seed.Name,seed.Password); var sid=login.Identity!.SessionId;
        await using var c=new NpgsqlConnection(runtime); await c.OpenAsync();
        await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await Execute(c,tx,$"SELECT * FROM \"Organizations\" WHERE \"OrganizationId\"='{existingOrg}'");
        await Execute(c,tx,$"UPDATE \"IdentitySessions\" SET \"RevokedAt\"=now(),\"RevocationReasonRef\"='session_revoked',\"Version\"=\"Version\"+1 WHERE \"SessionId\"='{sid}'; SELECT public.s2_session_audit('{sid}','session_revoked',now())");
        await using(var db=Open(processing)) await new PostgresProcessingStateAdministration(db).Change(new(existingOrg,Guid.NewGuid(),0,new SetProcessingOrganizationState("Suspended")));
        var error=await Assert.ThrowsAsync<PostgresException>(()=>Execute(c,tx,$"SELECT * FROM public.s2_lock_issuing_organization('{existingOrg}')"));
        Assert.Equal("40001",error.SqlState); await tx.RollbackAsync();
        await using var check=Open(owner);
        Assert.Null((await check.Set<IdentitySession>().SingleAsync()).RevokedAt);
        Assert.False(await check.Set<SecurityAudit>().AnyAsync(x=>x.Action=="session_revoked"));
    }
    [Fact]
    public async Task Restricted_deadlock_victim_rolls_back_session_and_audit_without_replay()
    {
        var seed=await IdentitySeed(); var a=await Login(seed.Name,seed.Password); var b=await Login(seed.Name,seed.Password);
        await using var one=new NpgsqlConnection(runtime); await one.OpenAsync();
        await using var two=new NpgsqlConnection(runtime); await two.OpenAsync();
        await using var tx1=await one.BeginTransactionAsync(); await using var tx2=await two.BeginTransactionAsync();
        string Retire(Guid sid)=>$"UPDATE \"IdentitySessions\" SET \"RevokedAt\"=now(),\"RevocationReasonRef\"='session_revoked',\"Version\"=\"Version\"+1 WHERE \"SessionId\"='{sid}'; SELECT public.s2_session_audit('{sid}','session_revoked',now())";
        await Execute(one,tx1,Retire(a.Identity!.SessionId)); await Execute(two,tx2,Retire(b.Identity!.SessionId));
        // Deliberately reverse two session locks to inject a database deadlock.
        async Task<Exception?> Wait(NpgsqlConnection c,NpgsqlTransaction tx,Guid sid)
        { try { await Execute(c,tx,$"SELECT * FROM \"IdentitySessions\" WHERE \"SessionId\"='{sid}' FOR UPDATE"); return null; } catch(Exception e){return e;} }
        var results=await Task.WhenAll(Wait(one,tx1,b.Identity.SessionId),Wait(two,tx2,a.Identity.SessionId)).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("40P01",Assert.IsType<PostgresException>(Assert.Single(results,x=>x!=null)).SqlState);
        await tx1.RollbackAsync(); await tx2.RollbackAsync();
        await using var check=Open(owner);
        Assert.All(await check.Set<IdentitySession>().ToListAsync(),x=>Assert.Null(x.RevokedAt));
        Assert.False(await check.Set<SecurityAudit>().AnyAsync(x=>x.Action=="session_revoked"));
    }

    private sealed class SessionClock : TimeProvider
    {
        public DateTimeOffset At { get; set; }=DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow()=>At;
    }
    [Fact]
    public async Task Restricted_replay_and_expiry_retirement_audit_paths_preserve_S2()
    {
        var seed=await IdentitySeed(); var clock=new SessionClock();
        async Task<SessionResult> Authenticate()
        { await using var db=Open(runtime); return await new PostgresIdentitySessions(db,clock,true).Authenticate(seed.Name,seed.Password); }
        async Task<SessionResult> Rotate(RefreshSecret token)
        { await using var db=Open(runtime); return await new PostgresIdentitySessions(db,clock,true).Rotate(token); }
        var one=await Authenticate(); Assert.Equal(SessionOutcome.Success,(await Rotate(one.RefreshToken!)).Outcome);
        Assert.Equal(SessionOutcome.RefreshAlreadyRotated,(await Rotate(one.RefreshToken!)).Outcome);
        clock.At+=SessionPolicy.ConcurrentReplayWindow+TimeSpan.FromSeconds(1);
        Assert.Equal(SessionOutcome.Denied,(await Rotate(one.RefreshToken!)).Outcome);
        var two=await Authenticate(); clock.At+=SessionPolicy.IdleTimeout;
        Assert.Equal(SessionOutcome.Denied,(await Rotate(two.RefreshToken!)).Outcome);
        await using var check=Open(owner); var audits=await check.Set<SecurityAudit>().ToListAsync();
        Assert.Single(audits,x=>x.Action=="refresh_replay"); Assert.Single(audits,x=>x.Action=="session_unavailable");
    }
    [Fact]
    public async Task Restricted_audit_failure_rolls_back_new_session_and_verifier()
    {
        var seed=await IdentitySeed();
        await Sql(owner,"""
            CREATE FUNCTION public.c2i_audit_failure() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected_audit_failure'; END $$;
            CREATE TRIGGER c2i_audit_failure BEFORE INSERT ON public."SecurityAudit"
            FOR EACH ROW EXECUTE FUNCTION public.c2i_audit_failure();
            """);
        var error=await Assert.ThrowsAnyAsync<Exception>(()=>Login(seed.Name,seed.Password));
        Assert.Equal("P0001",Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);
        await using var check=Open(owner);
        Assert.Empty(await check.Set<IdentitySession>().ToListAsync()); Assert.Empty(await check.Set<RefreshVerifier>().ToListAsync());
        Assert.Single(await check.Set<SecurityAudit>().ToListAsync());
    }
    [Fact]
    public async Task Restricted_issuer_holds_S2_locks_before_waiting_for_Organization()
    {
        var seed=await IdentitySeed(); var login=await Login(seed.Name,seed.Password);
        await using(var db=Open(runtime)) await new PostgresScopedAuthorityAdministration(db).Change(
            new(AuthorityKind.Membership,AuthorityOperation.Grant,existingOrg,seed.Actor,0,Role:ScopeRole.Student));
        await IssuingEnrollment(seed.Actor);
        await using var held=new NpgsqlConnection(runtime); await held.OpenAsync(); await using var tx=await held.BeginTransactionAsync();
        await Execute(held,tx,$"SELECT * FROM public.s2_lock_issuing_organization('{existingOrg}')");
        using var key=RSA.Create(2048); var clock=new IssuingClock(); var jwt=new ComponentJwt(key,"c2i","c2i",clock);
        var cs=new NpgsqlConnectionStringBuilder(runtime){ApplicationName="c2i-issuer-wait",Pooling=false}.ConnectionString;
        await using var issuing=Open(cs); using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pending=new PostgresComponentTokenIssuer(issuing,clock,jwt,true).Issue(seed.User,login.Identity!.SessionId,existingOrg,timeout.Token);
        var deadline=DateTime.UtcNow.AddSeconds(5); var waiting=false;
        while(DateTime.UtcNow<deadline)
        {
            waiting=await Scalar<bool>(runtime,"SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name='c2i-issuer-wait' AND wait_event_type='Lock')");
            if(waiting) break; await Task.Delay(20);
        }
        Assert.True(waiting); Assert.False(pending.IsCompleted);
        await using var contender=new NpgsqlConnection(owner); await contender.OpenAsync(); await using var contending=await contender.BeginTransactionAsync();
        var error=await Assert.ThrowsAsync<PostgresException>(()=>Execute(contender,contending,$"SELECT * FROM \"AccountSecurityState\" WHERE \"UserId\"='{seed.User}' FOR UPDATE NOWAIT"));
        Assert.Equal("55P03",error.SqlState); await contending.RollbackAsync();
        await tx.CommitAsync(); Assert.NotNull(await pending);
    }

    [Fact]
    public async Task Restricted_forged_action_is_denied_even_with_current_transaction_session_source()
    {
        var seed=await IdentitySeed(); var login=await Login(seed.Name,seed.Password); var sid=login.Identity!.SessionId;
        foreach(var action in new[]{"processing_changed","membership_granted",new string('x',1000)})
        {
            await using var c=new NpgsqlConnection(runtime); await c.OpenAsync(); await using var tx=await c.BeginTransactionAsync();
            await Execute(c,tx,$"UPDATE \"IdentitySessions\" SET \"Version\"=\"Version\"+1 WHERE \"SessionId\"='{sid}'");
            var error=await Assert.ThrowsAsync<PostgresException>(()=>Execute(c,tx,$"SELECT public.s2_session_audit('{sid}','{action}',now())"));
            Assert.Equal("42501",error.SqlState); await tx.RollbackAsync();
        }
        var forged=await Assert.ThrowsAsync<PostgresException>(()=>Sql(runtime,$"SELECT public.s2_mapping_audit('{seed.User}',now(),'{Guid.NewGuid()}'::uuid)"));
        Assert.Equal("42883",forged.SqlState); // no overload accepts an actor
        await using var check=Open(owner); Assert.Equal(2,await check.Set<SecurityAudit>().CountAsync());
    }
    [Fact]
    public async Task Existing_C2H_logins_cannot_invoke_Ready_composition_helpers()
    {
        foreach(var cs in new[]{runtime,processing})
        {
            await Denied(()=>Sql(cs,$"SELECT public.processing_begin_write('{existingOrg}',0)"));
            await Denied(()=>Sql(cs,$"SELECT public.processing_assert_current('{existingOrg}','synthetic','{Guid.NewGuid()}',1,now(),ARRAY['{Guid.NewGuid()}'::uuid])"));
        }
    }

    [Fact]
    public async Task Runtime_cannot_forge_mapping_creation_by_updating_existing_identity_rows()
    {
        var user=Guid.NewGuid(); var actor=Guid.NewGuid();
        await using(var db=Open(owner)) await new PostgresIdentityMapping(db,TimeProvider.System,true).CreateInactive(user,actor,existingOrg,"inactive-c2i");
        await using var c=new NpgsqlConnection(runtime); await c.OpenAsync(); await using var tx=await c.BeginTransactionAsync();
        await Denied(()=>Execute(c,tx,$"""
            UPDATE "AspNetUsers" SET "PasswordHash"=NULL WHERE "Id"='{user}';
            UPDATE "ActorIdentities" SET "UserId"="UserId" WHERE "UserId"='{user}';
            """));
        await tx.RollbackAsync();
        await Denied(()=>Sql(runtime,$"SELECT public.s2_mapping_audit('{user}',now())"));
        await using var check=Open(owner); Assert.Single(await check.Set<SecurityAudit>().ToListAsync());
    }

}
