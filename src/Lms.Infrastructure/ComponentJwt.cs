using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application;

namespace Lms.Infrastructure;

// Fixed RS256 profile, explicit key trust supplied by a trusted host; no HTTP registration.
// Signing is internal so production callers cannot sign detached S3 reads.
public sealed class ComponentJwt
{
    private readonly ComponentSigningKeys keys;
    private readonly string issuer, audience;
    private readonly IAuthorityClock clock;
    public ComponentJwt(RSA key, string issuer, string audience, IAuthorityClock clock)
        : this(SingleKey(key), issuer, audience, clock) { }
    public ComponentJwt(ComponentSigningKeys keys, string issuer, string audience, IAuthorityClock clock)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
            throw new ArgumentException("Invalid JWT profile");
        this.keys = keys; this.issuer = issuer; this.audience = audience; this.clock = clock;
    }
    private static ComponentSigningKeys SingleKey(RSA key)
    { var keys = new ComponentSigningKeys(); keys.Rotate(key); return keys; }
    internal AccessToken? Issue(SessionIdentity identity, CurrentAuthority authority, IEnumerable<DateTimeOffset?> deadlines)
    {
        var time = clock.Read();
        var exp = ComponentExpiry.Calculate(time, identity.AbsoluteExpiresAt, deadlines);
        if (exp == null) return null;
        var nbf = DateTimeOffset.FromUnixTimeSeconds(time!.Earliest.ToUnixTimeSeconds());
        var payload = new Payload(issuer, audience, identity.UserId, identity.ActorId, identity.SessionId,
            authority, nbf.ToUnixTimeSeconds(), exp.Value.ToUnixTimeSeconds());
        if (!Valid(payload)) return null;
        var kid = keys.ActiveId;
        if (kid == null) return null;
        var data = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid })) + "." +
            Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
        if (data.Length > 65536) return null;
        var signature = keys.Sign(kid, Encoding.ASCII.GetBytes(data));
        if (signature == null) return null;
        var value = data + "." + Encode(signature);
        return value.Length <= 65536 ? new(value) : null;
    }
    public TokenAuthority? Validate(AccessToken token)
    {
        try
        {
            var value = token.Reveal();
            if (value.Length > 65536) return null;
            var parts = value.Split('.');
            if (parts.Length != 3) return null;
            using var header = JsonDocument.Parse(Decode(parts[0]));
            var h = header.RootElement;
            if (!Unique(h) || !Fields(h, "alg", "typ", "kid") || h.GetProperty("alg").GetString() != "RS256" ||
                h.GetProperty("typ").GetString() != "JWT") return null;
            var kid = h.GetProperty("kid").GetString();
            if (kid == null || kid.Length != 64 || !keys.Verify(kid,
                    Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]))) return null;
            using var document = JsonDocument.Parse(Decode(parts[1]));
            var root = document.RootElement;
            if (!Unique(root) || !Fields(root, "iss", "aud", "sub", "actor", "sid", "scope", "nbf", "exp")) return null;
            var scope = root.GetProperty("scope");
            if (!Fields(scope, "Membership", "Grants") || !Fields(scope.GetProperty("Membership"),
                    "OrganizationId", "ActorId", "Generation", "Version", "Role", "Permissions")) return null;
            var grants = scope.GetProperty("Grants");
            if (grants.ValueKind != JsonValueKind.Array || grants.EnumerateArray().Any(g =>
                    !Fields(g, "Kind", "GrantId", "CourseId", "GroupId", "MembershipGeneration", "Generation", "Version"))) return null;
            var p = document.RootElement.Deserialize<Payload>(new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
            if (p == null || !Valid(p) || p.iss != issuer || p.aud != audience) return null;
            var now = clock.Read();
            var nbf = DateTimeOffset.FromUnixTimeSeconds(p.nbf); var exp = DateTimeOffset.FromUnixTimeSeconds(p.exp);
            if (now is not { Valid: true } || now.Earliest < nbf || now.Latest >= exp) return null;
            return new(new(p.sub, p.actor, p.sid, exp), p.scope, nbf, exp);
        }
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException or CryptographicException)
        { return null; }
    }
    private static bool Valid(Payload p)
    {
        if (p.sub == Guid.Empty || p.actor == Guid.Empty || p.sid == Guid.Empty || p.nbf <= 0 || p.exp <= p.nbf ||
            p.exp - p.nbf > 900 || p.scope?.Membership == null || p.scope.Grants == null) return false;
        var m = p.scope.Membership;
        if (m.ActorId != p.actor || m.OrganizationId == Guid.Empty || m.Generation <= 0 || m.Version <= 0 ||
            m.Permissions < 0 || (m.Permissions & ~OrganizationPermissionPolicy.KnownMask) != 0 ||
            p.scope.Grants.Count > 256) return false;
        if (m.Role is null)
            return OrganizationPermissionPolicy.CanManageOrganization(m) && p.scope.Grants.Count == 0;
        if (m.Role is not (ScopeRole.Student or ScopeRole.Teacher) || m.Permissions != 0 || p.scope.Grants.Count < 1) return false;
        var identities = new HashSet<string>(); var courses = new HashSet<string>();
        foreach (var g in p.scope.Grants)
        {
            if (g == null || string.IsNullOrWhiteSpace(g.CourseId) || g.CourseId.Length > 256 ||
                g.GroupId != null && (string.IsNullOrWhiteSpace(g.GroupId) || g.GroupId.Length > 256) ||
                g.MembershipGeneration != m.Generation || g.Generation <= 0 || g.Version <= 0) return false;
            if (m.Role == ScopeRole.Student)
            {
                if (g.Kind != AuthorityKind.Enrollment || g.GrantId != null || g.GroupId == null || !courses.Add(g.CourseId)) return false;
            }
            else if (g.Kind != AuthorityKind.Teacher || g.GrantId is null || g.GrantId == Guid.Empty || !identities.Add(g.GrantId.Value.ToString())) return false;
        }
        // Reject overlapping teacher scopes instead of choosing one interpretation.
        return m.Role != ScopeRole.Teacher || p.scope.Grants.GroupBy(g => g.CourseId).All(gs =>
            gs.Select(g => g.GroupId).Distinct().Count() == gs.Count() && (gs.Count() == 1 || gs.All(g => g.GroupId != null)));
    }
    private static bool Fields(JsonElement node, params string[] fields) => node.ValueKind == JsonValueKind.Object &&
        node.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(fields.Order());
    private static bool Unique(JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.Object => node.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() == node.EnumerateObject().Count()
            && node.EnumerateObject().All(x => Unique(x.Value)),
        JsonValueKind.Array => node.EnumerateArray().All(Unique), _ => true
    };
    private static string Encode(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value)
    {
        if (value.Length == 0 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) throw new FormatException();
        var data = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
        if (Encode(data) != value) throw new FormatException();
        return data;
    }
    private sealed record Payload(string iss, string aud, Guid sub, Guid actor, Guid sid, CurrentAuthority scope, long nbf, long exp);
}
