using Lms.Application;
namespace Lms.Application.Tests;
public sealed class ScopePolicyTests
{
    private static readonly Guid Org = Guid.NewGuid(), Student = Guid.NewGuid();
    private static CurrentAuthority Authority(ScopeRole role, AuthorityKind kind, string? group = "g", long generation = 1)
        => new(new(Org, Student, 1, 1, role, 15), new[] { new EducationalGrant(kind, null, "course", group, generation, 1, 1) });
    [Fact]
    public void Membership_permissions_and_role_never_replace_explicit_grants()
    {
        var a = Authority(ScopeRole.Student, AuthorityKind.Enrollment) with { Grants = Array.Empty<EducationalGrant>() };
        Assert.False(ScopePolicy.Allows(a, new(Org, "course"), ScopeAction.Read, Student));
    }
    [Theory]
    [InlineData(ScopeAction.Read)][InlineData(ScopeAction.Submit)][InlineData(ScopeAction.Review)]
    public void Student_never_gets_another_students_work(ScopeAction action)
        => Assert.False(ScopePolicy.Allows(Authority(ScopeRole.Student, AuthorityKind.Enrollment), new(Org, "course", "g", Guid.NewGuid()), action, Student));
    [Fact]
    public void Group_teacher_is_not_whole_course_teacher_for_ungrouped_work()
        => Assert.False(ScopePolicy.Allows(Authority(ScopeRole.Teacher, AuthorityKind.Teacher), new(Org, "course", StudentId: Guid.NewGuid()), ScopeAction.Read, Student));
    [Fact]
    public void Whole_course_teacher_is_explicit_and_does_not_cross_organizations()
    {
        var a = Authority(ScopeRole.Teacher, AuthorityKind.Teacher, null);
        Assert.True(ScopePolicy.Allows(a, new(Org, "course", "other", Guid.NewGuid()), ScopeAction.Read, Student));
        Assert.False(ScopePolicy.Allows(a, new(Guid.NewGuid(), "course"), ScopeAction.Read, Student));
    }
    [Fact]
    public void Older_grant_cannot_gain_work_created_under_a_new_enrollment_generation()
    {
        var a = Authority(ScopeRole.Student, AuthorityKind.Enrollment);
        Assert.False(ScopePolicy.Allows(a, new(Org, "course", "h", Student, MembershipGeneration: 1, EnrollmentGeneration: 2), ScopeAction.Read, Student));
        Assert.False(ScopePolicy.Allows(a, new(Org, "course", "g", Student, MembershipGeneration: 2, EnrollmentGeneration: 1), ScopeAction.Read, Student));
        Assert.True(ScopePolicy.Allows(a, new(Org, "course", "g", Student, MembershipGeneration: 1, EnrollmentGeneration: 1), ScopeAction.Read, Student));
    }
    [Fact]
    public void Stale_membership_generation_invalid_action_and_actor_are_denied()
    {
        Assert.False(ScopePolicy.Allows(Authority(ScopeRole.Student, AuthorityKind.Enrollment, generation: 2), new(Org, "course"), ScopeAction.Read, Student));
        Assert.False(ScopePolicy.Allows(Authority(ScopeRole.Student, AuthorityKind.Enrollment), new(Org, "course"), (ScopeAction)99, Student));
        Assert.False(ScopePolicy.Allows(Authority(ScopeRole.Student, AuthorityKind.Enrollment), new(Org, "course"), ScopeAction.Read, Guid.NewGuid()));
    }
}
