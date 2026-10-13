namespace Lms.Application;

// Isolated component contract. TokenAuthority must come from the accepted validator,
// never from request JSON. No production HTTP registration in this slice.
public sealed record CourseNavigationItemDto(CourseDto Course, string Role, CourseProgressDto? Progress);
public sealed record CourseNavigationDto(IReadOnlyList<CourseNavigationItemDto> Items);
public interface ICourseNavigationQueries
{
    Task<CourseNavigationDto> Courses(TokenAuthority token, CancellationToken cancellationToken = default);
}
