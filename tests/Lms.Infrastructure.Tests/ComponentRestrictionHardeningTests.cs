using System.Data.Common;
using Lms.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lms.Infrastructure.Tests;

public sealed partial class ScopedAuthorityTests
{
    [Theory]
    [InlineData(1, null)] [InlineData(2, "work:unknown")] [InlineData(4, "course:a")]
    [InlineData(8, "malformed:opaque")] [InlineData(16, "other-organization/resource")] [InlineData(31, "")]
    public async Task A2_opaque_selectors_cannot_bypass_parent_or_child_restrictions(int operations, string? selector)
    {
        var cs = await Seed(); var work = await Work(cs, review: true); var time = new ComponentClock();
        var restriction = Guid.NewGuid();
        await using (var setup = PostgresFixture.Open(cs))
        {
            setup.Add(new CourseContextBinding { OrganizationId = A, CourseId = "a", ContextId = Context, ContextVersion = 1, EffectiveFrom = At.AddHours(-1) });
            await setup.SaveChangesAsync();
        }
        var refs = new ResourceReference[] { new(ResourceKind.Assignment, work.Assignment.Id), new(ResourceKind.Work, work.Work.Id),
            new(ResourceKind.Submission, work.Work.Submissions[0].Id), new(ResourceKind.Review, work.Work.Reviews[0].Id) };
        var authority = (await Current(cs, T))!;
        var token = new TokenAuthority(new(Guid.NewGuid(), T, Guid.NewGuid(), At.AddHours(1)), authority, At, At.AddMinutes(15));
        await using var resources = PostgresFixture.Open(cs); await using var processing = PostgresFixture.Open(cs);
        var gate = new PostgresCurrentProcessingGate(processing, time);
        var policy = new ComponentAuthorization(new PostgresScopedAuthorityReader(resources, clock), gate, time);
        foreach (var reference in refs) Assert.True(await policy.Allows(token, reference, ScopeAction.Read));
        await using (var setup = PostgresFixture.Open(cs))
        {
            setup.Add(new ProcessingRestriction { RestrictionId = restriction, OrganizationId = A, ContextId = Context,
                OriginVersion = 1, SubjectActorId = S, Operations = operations, ResourceRef = selector, State = "Active", Version = 1 });
            await setup.SaveChangesAsync();
        }
        // Same policy/gate instance must observe fresh authoritative restrictions. A teacher
        // cannot avoid a student's restriction by choosing a child or another operation bit.
        foreach (var reference in refs)
        foreach (var action in new[] { ScopeAction.Read, ScopeAction.Review })
            Assert.False(await policy.Allows(token, reference, action));
        Assert.False(await gate.Allows(new(B, "a"), T, ScopeAction.Read, default));
        Assert.False(await policy.Allows(token, new(ResourceKind.Course, CourseId: "b"), ScopeAction.Read));
        Assert.False(await policy.Allows(token, new(ResourceKind.Submission, Guid.NewGuid()), ScopeAction.Read));
        await using (var setup = PostgresFixture.Open(cs))
        {
            var row = await setup.Set<ProcessingRestriction>().SingleAsync(r => r.RestrictionId == restriction);
            row.State = "Withdrawn"; row.Version++; await setup.SaveChangesAsync();
        }
        foreach (var reference in refs) Assert.True(await policy.Allows(token, reference, ScopeAction.Read));
    }
    [Theory]
    [InlineData("expired")] [InlineData("unqualified")]
    public async Task A2_processing_rechecks_binding_and_clock_after_database_wait(string failure)
    {
        var cs = await Seed(); var time = new ComponentClock();
        await using (var setup = PostgresFixture.Open(cs))
        {
            setup.Add(new CourseContextBinding { OrganizationId = A, CourseId = "a", ContextId = Context, ContextVersion = 1,
                EffectiveFrom = At.AddHours(-1), EffectiveTo = At.AddSeconds(1) });
            await setup.SaveChangesAsync();
        }
        var observer = new AfterRestrictionRead(() => time.Time = failure == "unqualified" ? null : new(At.AddSeconds(1), At.AddSeconds(1)));
        await using var db = PostgresFixture.Open(cs, observer);
        Assert.False(await new PostgresCurrentProcessingGate(db, time).Allows(new(A, "a"), S, ScopeAction.Read, default));
        Assert.True(observer.Reached);
    }
    [Fact]
    public async Task A2_processing_rejects_ambient_snapshot_and_tracked_context()
    {
        var cs = await Seed(); var time = new ComponentClock();
        await using var db = PostgresFixture.Open(cs); var gate = new PostgresCurrentProcessingGate(db, time);
        await using (var tx = await db.Database.BeginTransactionAsync())
            Assert.False(await gate.Allows(new(A, "a"), S, ScopeAction.Read, default));
        await db.Set<Organization>().ToListAsync();
        Assert.False(await gate.Allows(new(A, "a"), S, ScopeAction.Read, default));
    }
}
internal sealed class AfterRestrictionRead(Action action) : DbCommandInterceptor
{
    public bool Reached;
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("ProcessingRestrictions")) { Reached = true; action(); }
        return ValueTask.FromResult(result);
    }
}
