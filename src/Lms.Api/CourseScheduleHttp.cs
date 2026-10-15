using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lms.Application;
using Microsoft.AspNetCore.Antiforgery;

namespace Lms.Api;

internal sealed record SetWorkloadBody(int TotalAcademicHours, int ScheduledAcademicHours,
    int IndependentAcademicHours, int AcademicHourMinutes, long ExpectedVersion, string IdempotencyKey);
internal sealed record SaveScheduleRuleBody(Guid? RuleId, int IsoDayOfWeek, string StartTime,
    string EndTime, int AcademicHours, DateOnly EffectiveFrom, DateOnly EffectiveTo,
    string TimeZoneId, CourseDeliveryMode DeliveryMode, string? Location, string? OnlineUrl,
    CourseEventKind EventKind, string Title, string? Description, string? LessonId,
    bool IsActive, long ExpectedVersion, string IdempotencyKey);
internal sealed record ChangeOccurrenceBody(CourseOccurrenceStatus Status, DateOnly? NewDate,
    string? NewStartTime, string? NewEndTime, int? AcademicHours, CourseDeliveryMode? DeliveryMode,
    string? Location, string? OnlineUrl, long ExpectedVersion, string IdempotencyKey);

public static class CourseScheduleHttp
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void MapCourseSchedule(this WebApplication app, string allowedOrigin)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization(policy => policy
            .AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser());

        api.MapGet("/courses/{courseId}/schedule", async (HttpContext context, string courseId,
            ICourseScheduleService schedule) =>
        {
            var result = await schedule.Schedule(Educational(context), courseId, context.RequestAborted);
            if (result == null) { await ApiErrors.Write(context, "NotFound", 404); return; }
            await Respond(context, result);
        });
        api.MapGet("/courses/{courseId}/workload", async (HttpContext context, string courseId,
            ICourseScheduleService schedule) =>
        {
            var result = await schedule.Workload(Educational(context), courseId, context.RequestAborted);
            if (result == null) { await ApiErrors.Write(context, "NotFound", 404); return; }
            await Respond(context, result);
        });
        api.MapGet("/courses/{courseId}/schedule/events", async (HttpContext context, string courseId,
            ICourseScheduleService schedule) =>
        {
            var from = Date(context, "from"); var to = Date(context, "to");
            await Respond(context, new { items = await schedule.Events(Educational(context), courseId,
                from, to, context.RequestAborted) });
        });
        api.MapGet("/courses/{courseId}/schedule/upcoming", async (HttpContext context, string courseId,
            ICourseScheduleService schedule) =>
        {
            var limit = context.Request.Query.TryGetValue("limit", out var raw) &&
                int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 5;
            await Respond(context, new { items = await schedule.Upcoming(Educational(context), courseId,
                limit, context.RequestAborted) });
        });
        api.MapGet("/admin/courses/{courseId}/schedule", async (HttpContext context, string courseId,
            ICourseScheduleService schedule) =>
        {
            var token = Admin(context);
            var result = await schedule.AdminSchedule(token.Authority.Membership.OrganizationId,
                token.Identity.ActorId, courseId, context.RequestAborted);
            if (result == null) { await ApiErrors.Write(context, "NotFound", 404); return; }
            await Respond(context, result);
        });
        api.MapPost("/admin/courses/{courseId}/workload", (HttpContext context, string courseId,
            IAntiforgery csrf, ICourseScheduleService schedule) => Write(context, csrf, allowedOrigin,
            async token =>
            {
                var body = await Read<SetWorkloadBody>(context);
                await Respond(context, await schedule.SetWorkload(new(
                    token.Authority.Membership.OrganizationId, token.Identity.ActorId, courseId,
                    body.TotalAcademicHours, body.ScheduledAcademicHours, body.IndependentAcademicHours,
                    body.AcademicHourMinutes, body.ExpectedVersion, body.IdempotencyKey,
                    context.TraceIdentifier), context.RequestAborted));
            }));
        api.MapPost("/admin/courses/{courseId}/schedule/rules", (HttpContext context, string courseId,
            IAntiforgery csrf, ICourseScheduleService schedule) => Write(context, csrf, allowedOrigin,
            async token =>
            {
                var body = await Read<SaveScheduleRuleBody>(context);
                await Respond(context, await schedule.SaveRule(new(
                    token.Authority.Membership.OrganizationId, token.Identity.ActorId, courseId,
                    body.RuleId, body.IsoDayOfWeek, body.StartTime, body.EndTime, body.AcademicHours,
                    body.EffectiveFrom, body.EffectiveTo, body.TimeZoneId, body.DeliveryMode,
                    body.Location, body.OnlineUrl, body.EventKind, body.Title, body.Description,
                    body.LessonId, body.IsActive, body.ExpectedVersion, body.IdempotencyKey,
                    context.TraceIdentifier), context.RequestAborted));
            }));
        api.MapPost("/admin/courses/{courseId}/schedule/rules/{ruleId:guid}/occurrences/{originalDate}",
            (HttpContext context, string courseId, Guid ruleId, string originalDate, IAntiforgery csrf,
                ICourseScheduleService schedule) => Write(context, csrf, allowedOrigin, async token =>
            {
                var body = await Read<ChangeOccurrenceBody>(context);
                if (!DateOnly.TryParseExact(originalDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date)) throw new UseCaseException("ValidationFailed");
                await Respond(context, await schedule.ChangeOccurrence(new(
                    token.Authority.Membership.OrganizationId, token.Identity.ActorId, courseId,
                    ruleId, date, body.Status, body.NewDate, body.NewStartTime, body.NewEndTime,
                    body.AcademicHours, body.DeliveryMode, body.Location, body.OnlineUrl,
                    body.ExpectedVersion, body.IdempotencyKey, context.TraceIdentifier),
                    context.RequestAborted));
            }));
    }

    private static async Task Write(HttpContext context, IAntiforgery csrf, string allowedOrigin,
        Func<TokenAuthority, Task> command)
    {
        if (!context.Request.IsHttps || context.Request.Headers.Origin.Count != 1 ||
            context.Request.Headers.Origin[0] != allowedOrigin) throw new UseCaseException("Forbidden");
        try { await csrf.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { throw new UseCaseException("Forbidden"); }
        await command(Admin(context));
    }

    private static TokenAuthority Educational(HttpContext context) =>
        context.Features.Get<ValidatedCourseBearer>()?.Authority is { } token &&
        token.Authority.Membership.Role is not null ? token : throw new UseCaseException("Forbidden");

    private static TokenAuthority Admin(HttpContext context)
    {
        var token = context.Features.Get<ValidatedCourseBearer>()?.Authority;
        return token != null && OrganizationPermissionPolicy.CanManageOrganization(token.Authority.Membership)
            ? token : throw new UseCaseException("Forbidden");
    }

    private static DateOnly Date(HttpContext context, string name) =>
        context.Request.Query.TryGetValue(name, out var raw) && DateOnly.TryParseExact(raw, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value :
            throw new UseCaseException("ValidationFailed");
    private static async Task<T> Read<T>(HttpContext context) =>
        await JsonSerializer.DeserializeAsync<T>(context.Request.Body, Json, context.RequestAborted)
        ?? throw new UseCaseException("ValidationFailed");
    private static Task Respond(HttpContext context, object value)
    {
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsJsonAsync(value, Json, context.RequestAborted);
    }
}
