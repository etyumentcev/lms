using Lms.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Api;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class QueriesController(IWorkspaceQueries queries, IActorMapper actors) : ControllerBase
{
    [HttpGet("problem-assignments/{assignmentId}/workspace")]
    public Task<StudentProblemWorkspaceDto> StudentProblem(Guid assignmentId, CancellationToken ct)
        => queries.StudentProblem(actors.Map(User), assignmentId, ct);

    [HttpGet("problem-works/{problemWorkId}")]
    public Task<ProblemWorkDetailsDto> ProblemWork(Guid problemWorkId, CancellationToken ct)
        => queries.ProblemWork(actors.Map(User), problemWorkId, ct);

    [HttpGet("courses/{courseId}/workspace")]
    public Task<CourseWorkspaceDto> Course(string courseId, CancellationToken ct)
        => queries.Course(actors.Map(User), courseId, ct);

    [HttpGet("courses/{courseId}/lessons/{lessonId}/workspace")]
    public Task<LessonWorkspaceDto> Lesson(string courseId, string lessonId, CancellationToken ct)
        => queries.Lesson(actors.Map(User), courseId, lessonId, ct);

    [HttpGet("teacher/review-queue")]
    public Task<ReviewQueueResponseDto> Queue([FromQuery] ReviewQueueQuery query, CancellationToken ct)
        => queries.ReviewQueue(actors.Map(User), query, ct);
}
