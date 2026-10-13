using System.Security.Cryptography;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Tests;

public sealed partial class IsolatedProcessingDeploymentTests
{
    private async Task<AuthorityChange> ChangeRestricted(AuthorityCommand command)
    { await using var db=Open(runtime); return await new PostgresScopedAuthorityAdministration(db).Change(command); }
    private async Task<CurrentAuthority?> CurrentRestricted(Guid org,Guid actor)
    { await using var db=Open(runtime); return await new PostgresScopedAuthorityReader(db,TimeProvider.System).ResolveCurrent(org,actor); }
    // Test-only fresh whole-course predicate over the existing S3 reader. This is not
    // an ImportAttempt writer or an HTTP authorization adapter.
    private async Task<bool> WholeCourse(Guid org,Guid actor,string course)
    {
        var current=await CurrentRestricted(org,actor);
        return current?.Membership.Role==ScopeRole.Teacher && current.Grants.Any(g=>
            g.Kind==AuthorityKind.Teacher && g.CourseId==course && g.GroupId==null && g.MembershipGeneration==current.Membership.Generation);
    }
    [Fact]
    public async Task Restricted_S3_whole_course_teacher_Student_group_only_admin_and_foreign_scope_matrix()
    {
        var teacher=await IdentitySeed(); var student=await IdentitySeed(); var group=await IdentitySeed(); var admin=await IdentitySeed();
        var otherOrg=Guid.NewGuid();
        await Sql(owner,$"""
            INSERT INTO "Organizations" VALUES('{otherOrg}','Enabled',true);
            INSERT INTO "CourseRegistry" VALUES('whole','{existingOrg}','Verified'),('other-course','{existingOrg}','Verified'),('foreign','{otherOrg}','Verified');
            INSERT INTO "CourseGroups" VALUES('whole','g'),('whole','h');
            """);
        foreach(var (actor,role,flags) in new[]{(teacher.Actor,(ScopeRole?)ScopeRole.Teacher,0),(student.Actor,(ScopeRole?)ScopeRole.Student,0),(group.Actor,(ScopeRole?)ScopeRole.Teacher,0),(admin.Actor,(ScopeRole?)null,15)})
            await ChangeRestricted(new(AuthorityKind.Membership,AuthorityOperation.Grant,existingOrg,actor,0,Role:role,Permissions:flags));
        var whole=new AuthorityCommand(AuthorityKind.Teacher,AuthorityOperation.Grant,existingOrg,teacher.Actor,0,1,"whole",GrantId:Guid.NewGuid());
        await ChangeRestricted(whole);
        await ChangeRestricted(whole with{ActorId=group.Actor,GroupId="g",GrantId=Guid.NewGuid()});
        await ChangeRestricted(new(AuthorityKind.Enrollment,AuthorityOperation.Grant,existingOrg,student.Actor,0,1,"whole","g"));
        Assert.True(await WholeCourse(existingOrg,teacher.Actor,"whole"));
        foreach(var actor in new[]{student.Actor,group.Actor,admin.Actor,Guid.NewGuid()}) Assert.False(await WholeCourse(existingOrg,actor,"whole"));
        Assert.False(await WholeCourse(otherOrg,teacher.Actor,"whole")); Assert.False(await WholeCourse(existingOrg,teacher.Actor,"foreign"));
        Assert.False(await WholeCourse(existingOrg,teacher.Actor,"other-course"));
        await using(var db=Open(runtime))
        {
            var reader=new PostgresScopedAuthorityReader(db,TimeProvider.System);
            Assert.True(await reader.CanAccess(existingOrg,teacher.Actor,new(ResourceKind.Group,CourseId:"whole",GroupId:"h"),ScopeAction.Read));
            Assert.False(await reader.CanAccess(existingOrg,group.Actor,new(ResourceKind.Group,CourseId:"whole",GroupId:"h"),ScopeAction.Read));
            Assert.False(await reader.CanAccess(existingOrg,student.Actor,new(ResourceKind.Group,CourseId:"whole",GroupId:"g"),ScopeAction.Review));
            Assert.False(await reader.CanAccess(existingOrg,teacher.Actor,new(ResourceKind.Course,CourseId:"foreign"),ScopeAction.Read));
            Assert.Null(await reader.ResolveResource(new(ResourceKind.Submission,Guid.NewGuid(),CourseId:"whole")));
        }
        foreach(var command in new[]{whole with{ActorId=student.Actor,GrantId=Guid.NewGuid()},whole with{OrganizationId=otherOrg,GrantId=Guid.NewGuid()},whole with{CourseId="foreign",GrantId=Guid.NewGuid()},whole with{GroupId="absent",GrantId=Guid.NewGuid()}})
            await Assert.ThrowsAsync<UseCaseException>(()=>ChangeRestricted(command));
        var login=await Login(teacher.Name,teacher.Password); using var key=RSA.Create(2048); var clock=new IssuingClock(); var jwt=new ComponentJwt(key,"c2j","c2j",clock);
        async Task<AccessToken?> Issue()
        { await using var db=Open(runtime); return await new PostgresComponentTokenIssuer(db,clock,jwt,true).Issue(teacher.User,login.Identity!.SessionId,existingOrg); }
        var token=await Issue(); Assert.NotNull(token); Assert.NotNull(jwt.Validate(token!));
        var revoked=await ChangeRestricted(whole with{Operation=AuthorityOperation.Revoke,ExpectedVersion=1});
        Assert.False(await WholeCourse(existingOrg,teacher.Actor,"whole")); Assert.NotNull(jwt.Validate(token!));
        Assert.Empty((await CurrentRestricted(existingOrg,teacher.Actor))!.Grants);
        var repeat=await ChangeRestricted(whole with{Operation=AuthorityOperation.Revoke,ExpectedVersion=revoked.Version}); Assert.Equal(revoked,repeat);
        await ChangeRestricted(whole with{ExpectedVersion=revoked.Version}); Assert.True(await WholeCourse(existingOrg,teacher.Actor,"whole"));
        var membership=await ChangeRestricted(new(AuthorityKind.Membership,AuthorityOperation.Revoke,existingOrg,teacher.Actor,1));
        Assert.Null(await Issue()); Assert.False(await WholeCourse(existingOrg,teacher.Actor,"whole"));
        await ChangeRestricted(new(AuthorityKind.Membership,AuthorityOperation.Grant,existingOrg,teacher.Actor,membership.Version,Role:ScopeRole.Teacher));
        Assert.Empty((await CurrentRestricted(existingOrg,teacher.Actor))!.Grants); Assert.False(await WholeCourse(existingOrg,teacher.Actor,"whole"));
        await Assert.ThrowsAsync<UseCaseException>(()=>ChangeRestricted(whole with{ExpectedVersion=3})); // stale membership generation
        await ChangeRestricted(whole with{ExpectedVersion=3,MembershipGeneration=2}); Assert.True(await WholeCourse(existingOrg,teacher.Actor,"whole"));
    }
    [Fact]
    public async Task Restricted_S3_move_revoke_stale_generation_and_conflict_preserve_current_authority()
    {
        var student=await IdentitySeed();
        await Sql(owner,$"INSERT INTO \"CourseRegistry\" VALUES('moving','{existingOrg}','Verified'); INSERT INTO \"CourseGroups\" VALUES('moving','g'),('moving','h')");
        await ChangeRestricted(new(AuthorityKind.Membership,AuthorityOperation.Grant,existingOrg,student.Actor,0,Role:ScopeRole.Student));
        var enroll=new AuthorityCommand(AuthorityKind.Enrollment,AuthorityOperation.Grant,existingOrg,student.Actor,0,1,"moving","g");
        await ChangeRestricted(enroll);
        var move=enroll with{Operation=AuthorityOperation.Move,ExpectedVersion=1,GroupId="h"};
        var changed=await ChangeRestricted(move); Assert.Equal(2,changed.Generation); Assert.Equal(3,changed.Version);
        Assert.Equal("h",Assert.Single((await CurrentRestricted(existingOrg,student.Actor))!.Grants).GroupId);
        Assert.Equal("ConcurrencyConflict",(await Assert.ThrowsAsync<UseCaseException>(()=>ChangeRestricted(move))).Code);
        await Assert.ThrowsAsync<UseCaseException>(()=>ChangeRestricted(move with{ExpectedVersion=3,GroupId="foreign"}));
        var revoked=await ChangeRestricted(move with{Operation=AuthorityOperation.Revoke,ExpectedVersion=3});
        Assert.Empty((await CurrentRestricted(existingOrg,student.Actor))!.Grants);
        Assert.Equal(revoked,await ChangeRestricted(move with{Operation=AuthorityOperation.Revoke,ExpectedVersion=revoked.Version}));
        await ChangeRestricted(enroll with{ExpectedVersion=revoked.Version});
        var membership=await ChangeRestricted(new(AuthorityKind.Membership,AuthorityOperation.Revoke,existingOrg,student.Actor,1));
        Assert.Null(await CurrentRestricted(existingOrg,student.Actor));
        await ChangeRestricted(new(AuthorityKind.Membership,AuthorityOperation.Grant,existingOrg,student.Actor,membership.Version,Role:ScopeRole.Student));
        Assert.Empty((await CurrentRestricted(existingOrg,student.Actor))!.Grants);
        await Assert.ThrowsAsync<UseCaseException>(()=>ChangeRestricted(enroll with{ExpectedVersion=5}));
        await ChangeRestricted(enroll with{ExpectedVersion=5,MembershipGeneration=2});
        Assert.Equal(2,Assert.Single((await CurrentRestricted(existingOrg,student.Actor))!.Grants).MembershipGeneration);
    }
}
