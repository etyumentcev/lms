using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lms.Infrastructure;

public sealed class PostgresCourseScheduleService(LmsDbContext db, TimeProvider time,
    IAuthorityClock clock) : ICourseScheduleService
{
    private const int MaximumRangeDays = 93;

    public async Task<CourseScheduleDto?> Schedule(TokenAuthority token, string courseId,
        CancellationToken ct = default)
    {
        if (!await AllowsCourse(token, courseId, ct)) throw new UseCaseException("Forbidden");
        var exists = await CourseExists(token.Authority.Membership.OrganizationId, courseId, ct);
        if (!exists) return null;
        var rules = await Rules(courseId, token.Authority.Membership.OrganizationId, ct);
        var workload = await WorkloadCore(courseId, token.Authority.Membership.OrganizationId, rules, ct);
        if (!await AllowsCourse(token, courseId, ct)) throw new UseCaseException("Forbidden");
        return new(workload, rules.Select(RuleDto).ToList());
    }

    public async Task<CourseWorkloadDto?> Workload(TokenAuthority token, string courseId,
        CancellationToken ct = default) => (await Schedule(token, courseId, ct))?.Workload;

    public async Task<IReadOnlyList<CourseScheduleEventDto>> Events(TokenAuthority token,
        string courseId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ValidateRange(from, to);
        if (!await AllowsCourse(token, courseId, ct)) throw new UseCaseException("Forbidden");
        var result = await EventsCore(courseId, token.Authority.Membership.OrganizationId, from, to, ct);
        if (!await AllowsCourse(token, courseId, ct)) throw new UseCaseException("Forbidden");
        return result;
    }

    public async Task<IReadOnlyList<CourseScheduleEventDto>> Upcoming(TokenAuthority token,
        string courseId, int limit = 5, CancellationToken ct = default)
    {
        if (limit is < 1 or > 20) throw new UseCaseException("ValidationFailed");
        var now = clock.Read();
        if (now is not { Valid: true }) throw new UseCaseException("Forbidden");
        var from = DateOnly.FromDateTime(now.Earliest.UtcDateTime);
        var items = new List<CourseScheduleEventDto>();
        for (var cursor = from; cursor <= from.AddDays(366) && items.Count < limit; cursor = cursor.AddDays(MaximumRangeDays))
        {
            var to = cursor.AddDays(MaximumRangeDays - 1);
            if (to > from.AddDays(366)) to = from.AddDays(366);
            items.AddRange(await Events(token, courseId, cursor, to, ct));
        }
        return items.Where(x => x.EndsAt >= now.Earliest && x.Status != CourseOccurrenceStatus.Cancelled)
            .OrderBy(x => x.StartsAt).ThenBy(x => x.OccurrenceId, StringComparer.Ordinal).Take(limit).ToList();
    }

    public async Task<CourseScheduleDto?> AdminSchedule(Guid organizationId,
        Guid administratorActorId, string courseId, CancellationToken ct = default)
    {
        await EnsureAdmin(organizationId, administratorActorId, false, ct);
        if (!await CourseExists(organizationId, courseId, ct)) return null;
        if (!await ProcessingAllows(organizationId, administratorActorId, courseId, ct))
            throw new UseCaseException("Forbidden");
        var rules = await Rules(courseId, organizationId, ct);
        return new(await WorkloadCore(courseId, organizationId, rules, ct),
            rules.Select(RuleDto).ToList());
    }

    public Task<ScheduleMutationResult> SetWorkload(SetCourseWorkloadCommand command,
        CancellationToken ct = default)
    {
        if (command.TotalAcademicHours < 0 || command.ScheduledAcademicHours < 0 ||
            command.IndependentAcademicHours < 0 || command.TotalAcademicHours !=
            command.ScheduledAcademicHours + command.IndependentAcademicHours ||
            command.AcademicHourMinutes is < 1 or > 180 || command.ExpectedVersion < 0)
            throw new UseCaseException("ValidationFailed");
        var payload = $"{command.CourseId}\n{command.TotalAcademicHours}\n{command.ScheduledAcademicHours}\n{command.IndependentAcademicHours}\n{command.AcademicHourMinutes}\n{command.ExpectedVersion}";
        return Mutate(command.OrganizationId, command.AdministratorActorId, command.CourseId,
            "SetWorkload", payload, command.IdempotencyKey, command.TraceId, async ct2 =>
            {
                var row = await db.CourseWorkloads.FromSqlInterpolated(
                    $"SELECT * FROM \"CourseWorkloads\" WHERE \"CourseId\"={command.CourseId} FOR UPDATE")
                    .SingleOrDefaultAsync(ct2);
                if (row == null)
                {
                    if (command.ExpectedVersion != 0) throw new UseCaseException("ConcurrencyConflict");
                    row = new CourseWorkload { CourseId = command.CourseId,
                        OrganizationId = command.OrganizationId, Version = 1 };
                    db.Add(row);
                }
                else
                {
                    if (row.OrganizationId != command.OrganizationId) throw new UseCaseException("NotFound");
                    if (row.Version != command.ExpectedVersion) throw new UseCaseException("ConcurrencyConflict");
                    row.Version++;
                }
                row.TotalAcademicHours = command.TotalAcademicHours;
                row.ScheduledAcademicHours = command.ScheduledAcademicHours;
                row.IndependentAcademicHours = command.IndependentAcademicHours;
                row.AcademicHourMinutes = command.AcademicHourMinutes;
                row.UpdatedAt = time.GetUtcNow(); row.UpdatedBy = command.AdministratorActorId;
                return new(true, null, row.Version);
            }, ct);
    }

    public Task<ScheduleMutationResult> SaveRule(SaveCourseScheduleRuleCommand command,
        CancellationToken ct = default)
    {
        var start = ParseTime(command.StartTime); var end = ParseTime(command.EndTime);
        ValidateRule(command, start, end);
        var payload = JsonSerializer.Serialize(command);
        return Mutate(command.OrganizationId, command.AdministratorActorId, command.CourseId,
            "SaveRule", payload, command.IdempotencyKey, command.TraceId, async ct2 =>
            {
                var lessonId = Clean(command.LessonId, 160);
                if (lessonId != null && !await db.Lessons.AsNoTracking().AnyAsync(x =>
                    x.CourseId == command.CourseId && x.LessonId == lessonId && x.IsPublished, ct2))
                    throw new UseCaseException("ValidationFailed");
                var id = command.RuleId ?? Guid.NewGuid();
                var duplicate = await db.CourseScheduleRules.AnyAsync(x => x.CourseId == command.CourseId &&
                    x.RuleId != id && x.IsActive && command.IsActive && x.IsoDayOfWeek == command.IsoDayOfWeek &&
                    x.StartTime == start && x.EffectiveFrom <= command.EffectiveTo &&
                    x.EffectiveTo >= command.EffectiveFrom, ct2);
                if (duplicate) throw new UseCaseException("ScheduleOverlap");
                var row = await db.CourseScheduleRules.FromSqlInterpolated(
                    $"SELECT * FROM \"CourseScheduleRules\" WHERE \"RuleId\"={id} FOR UPDATE")
                    .SingleOrDefaultAsync(ct2);
                var now = time.GetUtcNow();
                if (row == null)
                {
                    if (command.RuleId != null || command.ExpectedVersion != 0)
                        throw new UseCaseException("ConcurrencyConflict");
                    row = new CourseScheduleRule { RuleId = id, CourseId = command.CourseId,
                        OrganizationId = command.OrganizationId, Version = 1, CreatedAt = now };
                    db.Add(row);
                }
                else
                {
                    if (row.CourseId != command.CourseId || row.OrganizationId != command.OrganizationId)
                        throw new UseCaseException("NotFound");
                    if (row.Version != command.ExpectedVersion) throw new UseCaseException("ConcurrencyConflict");
                    row.Version++;
                }
                row.IsoDayOfWeek = command.IsoDayOfWeek; row.StartTime = start; row.EndTime = end;
                row.AcademicHours = command.AcademicHours; row.EffectiveFrom = command.EffectiveFrom;
                row.EffectiveTo = command.EffectiveTo; row.TimeZoneId = command.TimeZoneId;
                row.DeliveryMode = command.DeliveryMode.ToString(); row.Location = Clean(command.Location, 300);
                row.OnlineUrl = CleanUrl(command.OnlineUrl); row.EventKind = command.EventKind.ToString();
                row.Title = Required(command.Title, 160); row.Description = Clean(command.Description, 1000);
                row.LessonId = lessonId; row.IsActive = command.IsActive;
                row.UpdatedAt = now; row.UpdatedBy = command.AdministratorActorId;
                return new(true, id, row.Version);
            }, ct);
    }

    public Task<ScheduleMutationResult> ChangeOccurrence(ChangeCourseOccurrenceCommand command,
        CancellationToken ct = default)
    {
        if (command.Status == CourseOccurrenceStatus.Scheduled || command.ExpectedVersion < 0)
            throw new UseCaseException("ValidationFailed");
        TimeOnly? newStart = command.Status == CourseOccurrenceStatus.Moved
            ? ParseTime(command.NewStartTime) : null;
        TimeOnly? newEnd = command.Status == CourseOccurrenceStatus.Moved
            ? ParseTime(command.NewEndTime) : null;
        if (command.Status == CourseOccurrenceStatus.Moved && command.NewDate == null)
            throw new UseCaseException("ValidationFailed");
        if (command.AcademicHours is < 1 or > 24) throw new UseCaseException("ValidationFailed");
        _ = CleanUrl(command.OnlineUrl);
        var payload = JsonSerializer.Serialize(command);
        return Mutate(command.OrganizationId, command.AdministratorActorId, command.CourseId,
            "ChangeOccurrence", payload, command.IdempotencyKey, command.TraceId, async ct2 =>
            {
                var rule = await db.CourseScheduleRules.FromSqlInterpolated(
                    $"SELECT * FROM \"CourseScheduleRules\" WHERE \"RuleId\"={command.RuleId} FOR UPDATE")
                    .SingleOrDefaultAsync(ct2) ?? throw new UseCaseException("NotFound");
                if (rule.CourseId != command.CourseId || rule.OrganizationId != command.OrganizationId ||
                    command.OriginalDate < rule.EffectiveFrom || command.OriginalDate > rule.EffectiveTo ||
                    IsoDay(command.OriginalDate) != rule.IsoDayOfWeek) throw new UseCaseException("NotFound");
                _ = Zoned(command.OriginalDate, rule.StartTime, rule.TimeZoneId);
                if (command.Status == CourseOccurrenceStatus.Moved)
                    _ = Zoned(command.NewDate!.Value, newStart!.Value, rule.TimeZoneId);
                var row = await db.CourseScheduleExceptions.FromSqlInterpolated(
                    $"SELECT * FROM \"CourseScheduleExceptions\" WHERE \"RuleId\"={command.RuleId} AND \"OriginalDate\"={command.OriginalDate} FOR UPDATE")
                    .SingleOrDefaultAsync(ct2);
                var now = time.GetUtcNow();
                if (row == null)
                {
                    if (command.ExpectedVersion != 0) throw new UseCaseException("ConcurrencyConflict");
                    row = new CourseScheduleException { ExceptionId = Guid.NewGuid(), RuleId = command.RuleId,
                        CourseId = command.CourseId, OrganizationId = command.OrganizationId,
                        OriginalDate = command.OriginalDate, Version = 1, CreatedAt = now };
                    db.Add(row);
                }
                else
                {
                    if (row.Version != command.ExpectedVersion) throw new UseCaseException("ConcurrencyConflict");
                    row.Version++;
                }
                row.Status = command.Status.ToString(); row.NewDate = command.NewDate;
                row.NewStartTime = newStart; row.NewEndTime = newEnd; row.AcademicHours = command.AcademicHours;
                row.DeliveryMode = command.DeliveryMode?.ToString(); row.Location = Clean(command.Location, 300);
                row.OnlineUrl = CleanUrl(command.OnlineUrl); row.UpdatedAt = now;
                row.UpdatedBy = command.AdministratorActorId;
                return new(true, command.RuleId, row.Version);
            }, ct);
    }

    private async Task<ScheduleMutationResult> Mutate(Guid organizationId, Guid actorId,
        string courseId, string kind, string payload, string idempotencyKey, string traceId,
        Func<CancellationToken, Task<ScheduleMutationResult>> change, CancellationToken ct)
    {
        var key = Required(idempotencyKey, 128); var digest = Digest(payload);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await Advisory($"processing:{organizationId}", ct);
            await EnsureAdmin(organizationId, actorId, true, ct);
            var course = await db.Set<CourseCatalogRecord>().FromSqlInterpolated(
                $"SELECT * FROM \"CourseCatalog\" WHERE \"CourseId\"={courseId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            if (course == null || course.OrganizationId != organizationId) throw new UseCaseException("NotFound");
            var resource = new ResourceOwnership(organizationId, courseId);
            if (!await new PostgresCurrentProcessingGate(db, clock).AllowsInTransaction(
                resource, actorId, ScopeAction.Read, ct)) throw new UseCaseException("Forbidden");
            await Advisory($"course-schedule:{actorId}:{key}", ct);
            var prior = await db.Set<CourseScheduleCommandReceipt>().SingleOrDefaultAsync(x =>
                x.AdministratorActorId == actorId && x.IdempotencyKey == key, ct);
            if (prior != null)
            {
                if (prior.Kind != kind || prior.PayloadDigest != digest || prior.CourseId != courseId)
                    throw new UseCaseException("IdempotencyConflict");
                var replay = JsonSerializer.Deserialize<ScheduleMutationResult>(prior.ResultJson)!
                    with { Changed = false };
                await tx.CommitAsync(ct); return replay;
            }
            var result = await change(ct); var now = time.GetUtcNow();
            db.Add(new CourseScheduleCommandReceipt { AdministratorActorId = actorId,
                IdempotencyKey = key, OrganizationId = organizationId, CourseId = courseId,
                Kind = kind, PayloadDigest = digest, ResultJson = JsonSerializer.Serialize(result), CreatedAt = now });
            db.Add(new CourseScheduleAudit { AuditId = Guid.NewGuid(), OrganizationId = organizationId,
                CourseId = courseId, ActorId = actorId, Action = kind, ResourceId = result.RuleId?.ToString() ?? courseId,
                Version = result.Version, TraceId = Clean(traceId, 200) ?? string.Empty, OccurredAt = now });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return result;
        }
        catch (PostgresException error) when (error.SqlState is "40001" or "23505")
        { throw new UseCaseException("ConcurrencyConflict"); }
        catch (DbUpdateConcurrencyException) { throw new UseCaseException("ConcurrencyConflict"); }
    }

    private async Task<IReadOnlyList<CourseScheduleEventDto>> EventsCore(string courseId,
        Guid organizationId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var movedRuleIds = await db.CourseScheduleExceptions.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.OrganizationId == organizationId &&
                x.NewDate >= from && x.NewDate <= to)
            .Select(x => x.RuleId).Distinct().ToListAsync(ct);
        var rules = await db.CourseScheduleRules.AsNoTracking().Where(x => x.CourseId == courseId &&
            x.OrganizationId == organizationId && x.IsActive &&
            (x.EffectiveFrom <= to && x.EffectiveTo >= from || movedRuleIds.Contains(x.RuleId)))
            .OrderBy(x => x.RuleId).ToListAsync(ct);
        var ruleIds = rules.Select(x => x.RuleId).ToArray();
        var exceptions = ruleIds.Length == 0 ? [] : await db.CourseScheduleExceptions.AsNoTracking()
            .Where(x => ruleIds.Contains(x.RuleId) &&
                (x.OriginalDate >= from && x.OriginalDate <= to || x.NewDate >= from && x.NewDate <= to))
            .ToListAsync(ct);
        var byOccurrence = exceptions.ToDictionary(x => (x.RuleId, x.OriginalDate));
        var result = new List<CourseScheduleEventDto>();
        foreach (var rule in rules)
        {
            var start = rule.EffectiveFrom > from ? rule.EffectiveFrom : from;
            var end = rule.EffectiveTo < to ? rule.EffectiveTo : to;
            foreach (var originalDate in Occurrences(start, end, rule.IsoDayOfWeek))
                AddEvent(result, rule, originalDate, byOccurrence.GetValueOrDefault((rule.RuleId, originalDate)), from, to);
            foreach (var moved in exceptions.Where(x => x.RuleId == rule.RuleId &&
                (x.OriginalDate < start || x.OriginalDate > end)))
                AddEvent(result, rule, moved.OriginalDate, moved, from, to);
        }
        return result.OrderBy(x => x.StartsAt).ThenBy(x => x.OccurrenceId, StringComparer.Ordinal).ToList();
    }

    private static void AddEvent(List<CourseScheduleEventDto> target, CourseScheduleRule rule,
        DateOnly originalDate, CourseScheduleException? exception, DateOnly from, DateOnly to)
    {
        var originalStart = Zoned(originalDate, rule.StartTime, rule.TimeZoneId);
        var status = exception == null ? CourseOccurrenceStatus.Scheduled :
            Enum.Parse<CourseOccurrenceStatus>(exception.Status);
        var eventDate = exception?.NewDate ?? originalDate;
        var startTime = exception?.NewStartTime ?? rule.StartTime;
        var endTime = exception?.NewEndTime ?? rule.EndTime;
        var start = Zoned(eventDate, startTime, rule.TimeZoneId);
        var endDate = endTime > startTime ? eventDate : eventDate.AddDays(1);
        var end = Zoned(endDate, endTime, rule.TimeZoneId);
        if (eventDate < from || eventDate > to) return;
        target.Add(new($"{rule.RuleId:N}:{originalDate:yyyy-MM-dd}", rule.RuleId, rule.CourseId,
            originalDate, start, end, rule.TimeZoneId, exception?.AcademicHours ?? rule.AcademicHours,
            exception?.DeliveryMode is { } mode ? Enum.Parse<CourseDeliveryMode>(mode) :
                Enum.Parse<CourseDeliveryMode>(rule.DeliveryMode), exception?.Location ?? rule.Location,
            exception?.OnlineUrl ?? rule.OnlineUrl, Enum.Parse<CourseEventKind>(rule.EventKind),
            rule.Title, rule.Description, rule.LessonId, status,
            status == CourseOccurrenceStatus.Moved ? originalStart : null, exception?.Version));
    }

    private async Task<CourseWorkloadDto?> WorkloadCore(string courseId, Guid organizationId,
        IReadOnlyList<CourseScheduleRule> rules, CancellationToken ct)
    {
        var row = await db.CourseWorkloads.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CourseId == courseId && x.OrganizationId == organizationId, ct);
        if (row == null) return null;
        var activeRules = rules.Where(x => x.IsActive).ToList();
        var total = activeRules.Sum(x => OccurrenceCount(x) * x.AcademicHours);
        var ids = activeRules.Select(x => x.RuleId).ToArray();
        if (ids.Length > 0)
        {
            var exceptions = await db.CourseScheduleExceptions.AsNoTracking().Where(x => ids.Contains(x.RuleId)).ToListAsync(ct);
            var ruleMap = activeRules.ToDictionary(x => x.RuleId);
            foreach (var item in exceptions)
            {
                var original = ruleMap[item.RuleId].AcademicHours;
                total -= original;
                if (item.Status == nameof(CourseOccurrenceStatus.Moved)) total += item.AcademicHours ?? original;
            }
        }
        return new(row.CourseId, row.TotalAcademicHours, row.ScheduledAcademicHours,
            row.IndependentAcademicHours, row.AcademicHourMinutes, total, null,
            Math.Max(0, row.ScheduledAcademicHours - total), total != row.ScheduledAcademicHours, row.Version);
    }

    private async Task<bool> AllowsCourse(TokenAuthority token, string courseId, CancellationToken ct)
    {
        var authorization = new ComponentAuthorization(new PostgresScopedAuthorityReader(db, time),
            new PostgresCurrentProcessingGate(db, clock), clock);
        return await authorization.Allows(token, new(ResourceKind.Course, CourseId: courseId),
            ScopeAction.Read, ct);
    }

    private Task<bool> ProcessingAllows(Guid organizationId, Guid actorId, string courseId, CancellationToken ct) =>
        new PostgresCurrentProcessingGate(db, clock).Allows(new(organizationId, courseId),
            actorId, ScopeAction.Read, ct);

    private Task<bool> CourseExists(Guid organizationId, string courseId, CancellationToken ct) =>
        db.Set<CourseCatalogRecord>().AsNoTracking().AnyAsync(x => x.OrganizationId == organizationId &&
            x.CourseId == courseId, ct);

    private Task<List<CourseScheduleRule>> Rules(string courseId, Guid organizationId, CancellationToken ct) =>
        db.CourseScheduleRules.AsNoTracking().Where(x => x.CourseId == courseId && x.OrganizationId == organizationId)
            .OrderBy(x => x.IsoDayOfWeek).ThenBy(x => x.StartTime).ThenBy(x => x.RuleId).ToListAsync(ct);

    private async Task EnsureAdmin(Guid organizationId, Guid actorId, bool forUpdate, CancellationToken ct)
    {
        OrganizationMembership? membership = forUpdate
            ? await db.Set<OrganizationMembership>().FromSqlInterpolated(
                $"SELECT * FROM \"OrganizationMemberships\" WHERE \"OrganizationId\"={organizationId} AND \"ActorId\"={actorId} FOR UPDATE")
                .SingleOrDefaultAsync(ct)
            : await db.Set<OrganizationMembership>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.ActorId == actorId, ct);
        if (membership == null || membership.State != "Active" ||
            !OrganizationPermissionPolicy.CanManageOrganization(new(membership.OrganizationId,
                membership.ActorId, membership.Generation, membership.Version, null, membership.PermissionFlags)))
            throw new UseCaseException("Forbidden");
    }

    private Task Advisory(string key, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtext({key}))", ct);

    private static CourseScheduleRuleDto RuleDto(CourseScheduleRule x) => new(x.RuleId, x.CourseId,
        x.IsoDayOfWeek, x.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        x.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture), x.AcademicHours, x.EffectiveFrom,
        x.EffectiveTo, x.TimeZoneId, Enum.Parse<CourseDeliveryMode>(x.DeliveryMode), x.Location,
        x.OnlineUrl, Enum.Parse<CourseEventKind>(x.EventKind), x.Title, x.Description, x.LessonId,
        x.IsActive, x.Version);

    private static IEnumerable<DateOnly> Occurrences(DateOnly from, DateOnly to, int isoDay)
    {
        var date = from.AddDays((isoDay - IsoDay(from) + 7) % 7);
        while (date <= to) { yield return date; date = date.AddDays(7); }
    }
    private static int OccurrenceCount(CourseScheduleRule x) =>
        Occurrences(x.EffectiveFrom, x.EffectiveTo, x.IsoDayOfWeek).Count();
    private static int IsoDay(DateOnly date) => ((int)date.DayOfWeek + 6) % 7 + 1;

    private static DateTimeOffset Zoned(DateOnly date, TimeOnly value, string timeZoneId)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { throw new UseCaseException("InvalidTimeZone"); }
        catch (InvalidTimeZoneException) { throw new UseCaseException("InvalidTimeZone"); }
        var local = DateTime.SpecifyKind(date.ToDateTime(value), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) throw new UseCaseException("NonexistentLocalTime");
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new(local, offset);
    }

    private static void ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from || to.DayNumber - from.DayNumber + 1 > MaximumRangeDays)
            throw new UseCaseException("ScheduleRangeInvalid");
    }

    private static void ValidateRule(SaveCourseScheduleRuleCommand command, TimeOnly start, TimeOnly end)
    {
        if (command.IsoDayOfWeek is < 1 or > 7 || command.AcademicHours is < 1 or > 24 ||
            command.EffectiveTo < command.EffectiveFrom ||
            command.EffectiveTo.DayNumber - command.EffectiveFrom.DayNumber > 731 ||
            command.ExpectedVersion < 0 || !Enum.IsDefined(command.DeliveryMode) ||
            !Enum.IsDefined(command.EventKind)) throw new UseCaseException("ValidationFailed");
        _ = Required(command.Title, 160); _ = Clean(command.Description, 1000);
        _ = Clean(command.Location, 300); _ = Clean(command.LessonId, 160);
        _ = CleanUrl(command.OnlineUrl);
        var duration = end > start ? end - start : TimeSpan.FromDays(1) - (start - end);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(18))
            throw new UseCaseException("ValidationFailed");
        var firstOccurrence = command.EffectiveFrom.AddDays(
            (command.IsoDayOfWeek - IsoDay(command.EffectiveFrom) + 7) % 7);
        if (firstOccurrence > command.EffectiveTo) throw new UseCaseException("ValidationFailed");
        _ = Zoned(firstOccurrence, start, command.TimeZoneId);
    }

    private static TimeOnly ParseTime(string? value) => TimeOnly.TryParseExact(value, "HH:mm",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed :
        throw new UseCaseException("ValidationFailed");
    private static string Required(string? value, int max) => Clean(value, max) ??
        throw new UseCaseException("ValidationFailed");
    private static string? Clean(string? value, int max)
    {
        if (value == null) return null;
        var clean = value.Trim();
        if (clean.Length == 0) return null;
        if (clean.Length > max || clean.Any(char.IsControl)) throw new UseCaseException("ValidationFailed");
        return clean;
    }
    private static string? CleanUrl(string? value)
    {
        var clean = Clean(value, 1000);
        if (clean == null) return null;
        if (!Uri.TryCreate(clean, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo)) throw new UseCaseException("ValidationFailed");
        return clean;
    }
    private static string Digest(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
