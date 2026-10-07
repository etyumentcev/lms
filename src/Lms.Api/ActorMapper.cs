using System.Security.Claims;
using Lms.Application;
using Lms.Domain;

namespace Lms.Api;

public interface IActorMapper
{
    Actor Map(ClaimsPrincipal principal);
}

public sealed class ClaimsActorMapper : IActorMapper
{
    public Actor Map(ClaimsPrincipal principal)
    {
        // Never combine claims from an unauthenticated or a second identity.
        var identities = principal.Identities.Where(x => x.IsAuthenticated).ToArray();
        if (identities.Length != 1) throw new UseCaseException("Unauthenticated");
        var subjects = identities[0].FindAll("sub").ToArray();
        var roles = identities[0].FindAll("role").ToArray();
        if (subjects.Length != 1 || !Guid.TryParse(subjects[0].Value, out var id) || id == Guid.Empty)
            throw new UseCaseException("Forbidden");
        if (roles.Length != 1 || roles[0].Value is not ("Student" or "Teacher"))
            throw new UseCaseException("Forbidden");
        return new Actor(id, roles[0].Value == "Student" ? AuthorRole.Student : AuthorRole.Teacher);
    }
}
