using System.Data;
using Lms.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Lms.Infrastructure;

// Synthetic-only organization administration over the existing immutable processing writer.
// The organization and actor always come from a validated component token at the HTTP boundary.
public sealed class PostgresProcessingContextAdministration(LmsDbContext db, TimeProvider clock)
    : IProcessingContextAdministration
{
    private const string Purpose = "synthetic:test:course-import";
    private const string Retention = "synthetic:ephemeral:test-data";

    public async Task<ProcessingContextAdministrationState> State(Guid organizationId,
        Guid administratorActorId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(organizationId, administratorActorId);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,
            cancellationToken);
        await RequireAdministrator(organizationId, administratorActorId, forUpdate: false,
            cancellationToken);
        var result = await ReadState(organizationId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ProcessingContextAdministrationResult> CreateVersion(
        CreateSyntheticProcessingContextCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateIdentity(command.OrganizationId, command.AdministratorActorId);
        if (command.OperationId == Guid.Empty || command.ExpectedRevision < 0 ||
            command.ExpectedContextId.HasValue != command.ExpectedContextVersion.HasValue ||
            command.ExpectedContextVersion is <= 0)
            throw new UseCaseException("ValidationFailed");
        ValidateEffectiveFrom(command.EffectiveFrom);

        var contextId = command.ExpectedContextId ?? command.OperationId;
        var version = command.ExpectedContextVersion.GetValueOrDefault() + 1;
        var mutation = new AddProcessingContext(contextId, version, command.EffectiveFrom,
            PurposeRef: Purpose, RetentionPolicyRef: Retention);
        return await Change(command.OrganizationId, command.AdministratorActorId,
            new(command.OrganizationId, command.OperationId, command.ExpectedRevision, mutation),
            async ct =>
            {
                var versions = await db.Set<ProcessingContextVersion>().AsNoTracking()
                    .Where(x => x.OrganizationId == command.OrganizationId)
                    .OrderBy(x => x.ContextId).ThenBy(x => x.Version).ToListAsync(ct);
                if (versions.Count == 0)
                {
                    if (command.ExpectedContextId != null) throw new UseCaseException("ConcurrencyConflict");
                    return;
                }
                var ids = versions.Select(x => x.ContextId).Distinct().ToArray();
                if (ids.Length != 1) throw new UseCaseException("ProcessingContextConflict");
                var latest = versions[^1];
                if (command.ExpectedContextId != latest.ContextId ||
                    command.ExpectedContextVersion != latest.Version ||
                    command.EffectiveFrom <= latest.EffectiveFrom)
                    throw new UseCaseException("ConcurrencyConflict");
            }, cancellationToken);
    }

    public async Task<ProcessingContextAdministrationResult> BindCourse(
        BindCourseProcessingContextCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateIdentity(command.OrganizationId, command.AdministratorActorId);
        if (command.OperationId == Guid.Empty || command.ExpectedRevision < 0 ||
            string.IsNullOrWhiteSpace(command.CourseId) || command.CourseId.Length > 200 ||
            command.ContextId == Guid.Empty || command.ContextVersion <= 0 ||
            (command.ExpectedBindingFrom == null) == (command.EffectiveFrom == null))
            throw new UseCaseException("ValidationFailed");
        if (command.EffectiveFrom.HasValue) ValidateEffectiveFrom(command.EffectiveFrom.Value);

        ProcessingStateMutation mutation = command.ExpectedBindingFrom.HasValue
            ? new ReplaceProcessingBinding(command.CourseId, command.ExpectedBindingFrom.Value,
                command.ContextId, command.ContextVersion)
            : new OpenProcessingBinding(command.CourseId, command.ContextId,
                command.ContextVersion, command.EffectiveFrom!.Value);
        return await Change(command.OrganizationId, command.AdministratorActorId,
            new(command.OrganizationId, command.OperationId, command.ExpectedRevision, mutation),
            async ct =>
            {
                var now = clock.GetUtcNow();
                var currentContext = await CurrentContext(command.OrganizationId, now, ct);
                if (currentContext == null || currentContext.ContextId != command.ContextId ||
                    currentContext.Version != command.ContextVersion)
                    throw new UseCaseException("ProcessingContextMissing");
                var courseExists = await db.Set<CourseRegistration>().AsNoTracking().AnyAsync(x =>
                    x.OrganizationId == command.OrganizationId && x.CourseId == command.CourseId &&
                    x.MappingState == "Verified", ct);
                if (!courseExists) throw new UseCaseException("NotFound");
                var bindings = await db.Set<CourseContextBinding>().AsNoTracking().Where(x =>
                    x.OrganizationId == command.OrganizationId && x.CourseId == command.CourseId &&
                    x.EffectiveFrom <= now && (x.EffectiveTo == null || x.EffectiveTo > now))
                    .ToListAsync(ct);
                if (bindings.Count > 1) throw new UseCaseException("ProcessingContextConflict");
                var current = bindings.SingleOrDefault();
                if (command.ExpectedBindingFrom == null)
                {
                    if (current != null) throw new UseCaseException("ConcurrencyConflict");
                }
                else if (current == null || current.EffectiveFrom != command.ExpectedBindingFrom)
                    throw new UseCaseException("ConcurrencyConflict");
            }, cancellationToken);
    }

    private async Task<ProcessingContextAdministrationResult> Change(Guid organizationId,
        Guid administratorActorId, ProcessingStateCommand processingCommand,
        Func<CancellationToken, Task> validateCurrent, CancellationToken ct)
    {
        PostgresProcessingWriteBoundary.RequireClean(db);
        var (action, payload) = PostgresProcessingStateAdministration.Describe(processingCommand);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var actualRevision = await Lock(organizationId, ct);
            await RequireAdministrator(organizationId, administratorActorId, forUpdate: true, ct);
            var prior = await Find(processingCommand, action, payload, ct);
            if (prior.HasValue)
            {
                var replay = await ReadState(organizationId, ct);
                await tx.CommitAsync(ct);
                return new(false, replay);
            }
            if (actualRevision != processingCommand.ExpectedRevision)
                throw new UseCaseException("ConcurrencyConflict");
            await validateCurrent(ct);
            await Execute(processingCommand, action, payload, ct);
            var state = await ReadState(organizationId, ct);
            await tx.CommitAsync(ct);
            ct.ThrowIfCancellationRequested();
            return new(true, state);
        }
        catch (PostgresException error) when (error.SqlState == "P0001")
        {
            throw new UseCaseException(error.MessageText switch
            {
                "processing_revision_conflict" or "processing_binding_conflict" or
                    "processing_operation_conflict" => "ConcurrencyConflict",
                "processing_course_denied" => "NotFound",
                "processing_org_missing" or "processing_org_denied" => "Forbidden",
                "processing_context_denied" => "ProcessingContextMissing",
                "processing_restricted" => "ProcessingRestricted",
                _ => "ValidationFailed"
            });
        }
        catch (PostgresException error) when (error.SqlState is "23505" or "40001" or "40P01")
        {
            throw new UseCaseException("ConcurrencyConflict");
        }
    }

    private Task<long> Lock(Guid organizationId, CancellationToken ct) => db.Database.SqlQueryRaw<long>(
        "SELECT public.processing_lock(@org) AS \"Value\"",
        new NpgsqlParameter("org", organizationId)).SingleAsync(ct);

    private Task<long?> Find(ProcessingStateCommand command, string action, string payload,
        CancellationToken ct) => db.Database.SqlQueryRaw<long?>(
        "SELECT public.processing_find_command(@org,@operation,@expected,@action,@payload) AS \"Value\"",
        Parameters(command, action, payload)).SingleAsync(ct);

    private Task<long> Execute(ProcessingStateCommand command, string action, string payload,
        CancellationToken ct) => db.Database.SqlQueryRaw<long>(
        "SELECT public.processing_change(@org,@operation,@expected,@action,@payload) AS \"Value\"",
        Parameters(command, action, payload)).SingleAsync(ct);

    private static object[] Parameters(ProcessingStateCommand command, string action, string payload) =>
    [
        new NpgsqlParameter("org", command.OrganizationId),
        new NpgsqlParameter("operation", command.OperationId),
        new NpgsqlParameter("expected", command.ExpectedRevision),
        new NpgsqlParameter("action", action),
        new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload }
    ];

    private async Task RequireAdministrator(Guid organizationId, Guid actorId, bool forUpdate,
        CancellationToken ct)
    {
        OrganizationMembership? membership;
        if (forUpdate)
            membership = await db.Set<OrganizationMembership>().FromSqlInterpolated(
                $"SELECT * FROM \"OrganizationMemberships\" WHERE \"OrganizationId\"={organizationId} AND \"ActorId\"={actorId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
        else
            membership = await db.Set<OrganizationMembership>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.ActorId == actorId, ct);
        if (membership is not { State: "Active", EducationalRole: null } ||
            !OrganizationPermissionPolicy.CanManageOrganization(new(membership.OrganizationId,
                membership.ActorId, membership.Generation, membership.Version, null,
                membership.PermissionFlags)))
            throw new UseCaseException("Forbidden");
    }

    private async Task<ProcessingContextAdministrationState> ReadState(Guid organizationId,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var revision = await db.Database.SqlQueryRaw<long>(
            "SELECT \"Revision\" AS \"Value\" FROM \"ProcessingGuards\" WHERE \"OrganizationId\"=@org",
            new NpgsqlParameter("org", organizationId)).SingleAsync(ct);
        var contexts = await db.Set<ProcessingContextVersion>().AsNoTracking().Where(x =>
            x.OrganizationId == organizationId && x.EffectiveFrom <= now)
            .OrderBy(x => x.ContextId).ThenBy(x => x.Version).ToListAsync(ct);
        var contextIds = contexts.Select(x => x.ContextId).Distinct().ToArray();
        if (contextIds.Length > 1) throw new UseCaseException("ProcessingContextConflict");
        var current = contexts.Count == 0 ? null : contexts.OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.Version).First();
        var summary = current == null ? null : new ProcessingContextSummary(current.ContextId,
            current.Version, current.EffectiveFrom, current.ApprovalState, current.SyntheticOnly,
            current.PurposeRef ?? string.Empty, current.RetentionPolicyRef ?? string.Empty,
            contexts.Count);
        var restricted = current != null && await db.Set<ProcessingRestriction>().AsNoTracking().AnyAsync(x =>
            x.OrganizationId == organizationId && x.ContextId == current.ContextId && x.State == "Active", ct);
        var courses = await db.Set<CourseCatalogRecord>().AsNoTracking().Where(x =>
            x.OrganizationId == organizationId).OrderBy(x => x.Title).ToListAsync(ct);
        var courseIds = courses.Select(x => x.CourseId).ToArray();
        var bindings = await db.Set<CourseContextBinding>().AsNoTracking().Where(x =>
            x.OrganizationId == organizationId && courseIds.Contains(x.CourseId) &&
            x.EffectiveFrom <= now && (x.EffectiveTo == null || x.EffectiveTo > now)).ToListAsync(ct);
        if (bindings.GroupBy(x => x.CourseId).Any(x => x.Count() > 1))
            throw new UseCaseException("ProcessingContextConflict");
        var byCourse = bindings.ToDictionary(x => x.CourseId);
        var courseItems = courses.Select(course => new CourseProcessingContextSummary(course.CourseId,
            course.Title, course.State, byCourse.TryGetValue(course.CourseId, out var binding)
                ? new(binding.ContextId, binding.ContextVersion, binding.EffectiveFrom) : null)).ToList();
        return new(revision, summary, restricted, courseItems);
    }

    private async Task<ProcessingContextVersion?> CurrentContext(Guid organizationId,
        DateTimeOffset now, CancellationToken ct)
    {
        var contexts = await db.Set<ProcessingContextVersion>().AsNoTracking().Where(x =>
            x.OrganizationId == organizationId && x.EffectiveFrom <= now && x.SyntheticOnly &&
            x.ApprovalState == "SyntheticOnly").OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.Version).ToListAsync(ct);
        if (contexts.Select(x => x.ContextId).Distinct().Take(2).Count() > 1)
            throw new UseCaseException("ProcessingContextConflict");
        return contexts.FirstOrDefault();
    }

    private void ValidateEffectiveFrom(DateTimeOffset value)
    {
        var now = clock.GetUtcNow();
        if (value == default || value < now.AddDays(-1) || value > now.AddMinutes(5))
            throw new UseCaseException("ValidationFailed");
    }

    private static void ValidateIdentity(Guid organizationId, Guid actorId)
    {
        if (organizationId == Guid.Empty || actorId == Guid.Empty)
            throw new UseCaseException("Forbidden");
    }
}
