using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lms.Application;

namespace Lms.Infrastructure.Tests;

public sealed class ComponentClock : IAuthorityClock
{
    public AuthorityTime? Time { get; set; } = new(DateTimeOffset.Parse("2026-10-09T12:00:00Z"), DateTimeOffset.Parse("2026-10-09T12:00:00Z"));
    public AuthorityTime? Read() => Time;
}
public sealed class ComponentJwtTests
{
    private static readonly Guid Org = Guid.NewGuid(), Actor = Guid.NewGuid();
    private static CurrentAuthority Authority => new(new(Org, Actor, 1, 1, ScopeRole.Student, 0),
        [new(AuthorityKind.Enrollment, null, "a", "g", 1, 1, 1)]);
    private static JsonObject Payload(ComponentClock clock) => JsonSerializer.SerializeToNode(new
    {
        iss = "synthetic-issuer", aud = "synthetic-audience", sub = Guid.NewGuid(), actor = Actor, sid = Guid.NewGuid(),
        scope = Authority, nbf = clock.Time!.Earliest.ToUnixTimeSeconds(), exp = clock.Time.Latest.AddMinutes(15).ToUnixTimeSeconds()
    })!.AsObject();
    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static AccessToken Signed(RSA key, string payload, string? header = null)
    {
        header ??= JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT", kid = ComponentSigningKeys.KeyId(key) });
        var data = B64(Encoding.UTF8.GetBytes(header)) + "." + B64(Encoding.UTF8.GetBytes(payload));
        return new(data + "." + B64(key.SignData(Encoding.ASCII.GetBytes(data), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
    }
    [Fact]
    public void Conservative_cutoff_uses_earliest_clock_deadline_buffer_and_rounds_down()
    {
        var clock = new ComponentClock(); var now = clock.Time!.Earliest;
        var interval = new AuthorityTime(now.AddSeconds(-2.2), now.AddSeconds(2.2));
        Assert.Equal(now.AddSeconds(897), ComponentExpiry.Calculate(interval, now.AddHours(1), [null]));
        Assert.Equal(now.AddMinutes(4), ComponentExpiry.Calculate(interval, now.AddHours(1), [now.AddMinutes(14), now.AddMinutes(20)]));
        Assert.Equal(now.AddSeconds(10), ComponentExpiry.Calculate(interval, now.AddSeconds(10.8), [null]));
        Assert.Null(ComponentExpiry.Calculate(interval, now.AddHours(1), [now.AddMinutes(10)]));
        Assert.Null(ComponentExpiry.Calculate(null, now.AddHours(1), []));
        Assert.Null(ComponentExpiry.Calculate(new(now.AddSeconds(1), now), now.AddHours(1), []));
    }
    [Theory]
    [InlineData("iss")] [InlineData("aud")] [InlineData("sub")] [InlineData("actor")]
    [InlineData("sid")] [InlineData("scope")] [InlineData("nbf")] [InlineData("exp")]
    public void Missing_claim_denies(string claim)
    {
        using var key = RSA.Create(2048); var clock = new ComponentClock(); var p = Payload(clock); p.Remove(claim);
        Assert.Null(new ComponentJwt(key, "synthetic-issuer", "synthetic-audience", clock).Validate(Signed(key, p.ToJsonString())));
    }
    [Theory]
    [InlineData("issuer")] [InlineData("audience")] [InlineData("empty-id")] [InlineData("expired")]
    [InlineData("future")] [InlineData("long-life")] [InlineData("generation")]
    [InlineData("duplicate-scope")] [InlineData("wrong-kind")] [InlineData("missing-role")]
    [InlineData("unknown")] [InlineData("null-scope")]
    public void Invalid_or_contradictory_claims_deny(string mutation)
    {
        using var key = RSA.Create(2048); var clock = new ComponentClock(); var p = Payload(clock);
        switch (mutation)
        {
            case "issuer": p["iss"] = "other"; break;
            case "audience": p["aud"] = "other"; break;
            case "empty-id": p["sub"] = Guid.Empty; break;
            case "expired": p["exp"] = p["nbf"]!.DeepClone(); break;
            case "future": p["nbf"] = clock.Time!.Latest.AddSeconds(1).ToUnixTimeSeconds(); break;
            case "long-life": p["exp"] = clock.Time!.Latest.AddSeconds(901).ToUnixTimeSeconds(); break;
            case "generation": p["scope"]!["Grants"]![0]!["MembershipGeneration"] = 2; break;
            case "duplicate-scope": p["scope"]!["Grants"]!.AsArray().Add(p["scope"]!["Grants"]![0]!.DeepClone()); break;
            case "wrong-kind": p["scope"]!["Grants"]![0]!["Kind"] = 2; break;
            case "missing-role": p["scope"]!["Membership"]!.AsObject().Remove("Role"); break;
            case "unknown": p["admin"] = true; break;
            case "null-scope": p["scope"] = null; break;
        }
        Assert.Null(new ComponentJwt(key, "synthetic-issuer", "synthetic-audience", clock).Validate(Signed(key, p.ToJsonString())));
    }
    [Fact]
    public void Signature_algorithm_duplicate_properties_and_malformed_encoding_deny()
    {
        using var key = RSA.Create(2048); using var wrong = RSA.Create(2048); var clock = new ComponentClock();
        var jwt = new ComponentJwt(key, "synthetic-issuer", "synthetic-audience", clock); var p = Payload(clock).ToJsonString();
        Assert.NotNull(jwt.Validate(Signed(key, p)));
        Assert.Null(jwt.Validate(Signed(wrong, p, JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT", kid = ComponentSigningKeys.KeyId(key) }))));
        Assert.Null(jwt.Validate(Signed(key, p, "{\"alg\":\"none\",\"typ\":\"JWT\"}")));
        Assert.Null(jwt.Validate(Signed(key, p.Replace("{", "{\"exp\":1,", StringComparison.Ordinal))));
        foreach (var bad in new[] { "", "a.b.c", "é.b.c", new string('a', 65537), Signed(key, p).Reveal() + "=" })
            Assert.Null(jwt.Validate(new(bad)));
    }
    [Fact]
    public void Public_only_validator_trust_cannot_be_rebound_or_reintroduced_after_removal()
    {
        using var signer = RSA.Create(2048); using var publicKey = RSA.Create(); using var other = RSA.Create(2048);
        publicKey.ImportSubjectPublicKeyInfo(signer.ExportSubjectPublicKeyInfo(), out _);
        var keys = new ComponentSigningKeys(); var id = keys.TrustVerificationKey(publicKey); var clock = new ComponentClock();
        var jwt = new ComponentJwt(keys, "synthetic-issuer", "synthetic-audience", clock);
        var token = Signed(signer, Payload(clock).ToJsonString());
        Assert.NotNull(jwt.Validate(token));
        // Host-handle mutation cannot change the meaning of an already registered kid.
        publicKey.ImportSubjectPublicKeyInfo(other.ExportSubjectPublicKeyInfo(), out _);
        Assert.Null(jwt.Validate(token));
        publicKey.ImportSubjectPublicKeyInfo(signer.ExportSubjectPublicKeyInfo(), out _);
        Assert.NotNull(jwt.Validate(token));
        keys.RemoveTrust(id); Assert.Null(jwt.Validate(token));
        Assert.Throws<InvalidOperationException>(() => keys.TrustVerificationKey(publicKey));
    }
    [Fact]
    public void No_positive_leeway_and_unknown_clock_denies()
    {
        using var key = RSA.Create(2048); var clock = new ComponentClock(); var now = clock.Time!.Earliest;
        var jwt = new ComponentJwt(key, "synthetic-issuer", "synthetic-audience", clock); var token = Signed(key, Payload(clock).ToJsonString());
        clock.Time = new(now, now.AddMinutes(15).AddTicks(-1)); Assert.NotNull(jwt.Validate(token));
        clock.Time = new(now, now.AddMinutes(15)); Assert.Null(jwt.Validate(token));
        clock.Time = new(now.AddTicks(-1), now); Assert.Null(jwt.Validate(token));
        clock.Time = null; Assert.Null(jwt.Validate(token));
        Assert.DoesNotContain(token.Reveal(), JsonSerializer.Serialize(token) + token);
    }
    [Fact]
    public async Task Component_policy_uses_parent_and_current_processing_never_current_grants()
    {
        var clock = new ComponentClock(); var token = new TokenAuthority(new(Guid.NewGuid(), Actor, Guid.NewGuid(), clock.Time!.Latest.AddHours(1)),
            Authority, clock.Time.Earliest, clock.Time.Latest.AddMinutes(15));
        var resources = new Resources { Resource = new(Org, "a", "g", Actor, MembershipGeneration: 1, EnrollmentGeneration: 1) };
        var gate = new Gate(); var policy = new ComponentAuthorization(resources, gate, clock);
        Assert.True(await policy.Allows(token, new(ResourceKind.Work, Guid.NewGuid()), ScopeAction.Read));
        gate.Allow = false; Assert.False(await policy.Allows(token, new(ResourceKind.Work), ScopeAction.Read));
        gate.Allow = true;
        foreach (var r in new[] { resources.Resource with { OrganizationId = Guid.NewGuid() }, resources.Resource with { StudentId = Guid.NewGuid() },
            resources.Resource with { CourseId = "foreign" }, resources.Resource with { EnrollmentGeneration = 2 }, resources.Resource with { MembershipGeneration = 2 } })
        { resources.Resource = r; Assert.False(await policy.Allows(token, new(ResourceKind.Work), ScopeAction.Read)); }
        resources.Resource = null; Assert.False(await policy.Allows(token, new(ResourceKind.Work), ScopeAction.Read));
    }
    [Fact]
    public void Teacher_assignment_and_student_group_isolation_follow_S3()
    {
        var teacher = new CurrentAuthority(Authority.Membership with { Role = ScopeRole.Teacher },
            [new(AuthorityKind.Teacher, Guid.NewGuid(), "a", "g", 1, 1, 1)]);
        Assert.True(ScopePolicy.Allows(teacher, new(Org, "a", "g", Guid.NewGuid()), ScopeAction.Review, Actor));
        Assert.False(ScopePolicy.Allows(teacher, new(Org, "a", "h", Guid.NewGuid()), ScopeAction.Review, Actor));
        Assert.False(ScopePolicy.Allows(teacher, new(Org, "a", "g"), ScopeAction.Submit, Actor));
        Assert.False(ScopePolicy.Allows(Authority, new(Org, "a", "h"), ScopeAction.Read, Actor));
        Assert.False(ScopePolicy.Allows(Authority, new(Org, "a", "g", Actor), ScopeAction.Review, Actor));
    }
    private sealed class Gate : ICurrentProcessingGate
    {
        public bool Allow = true;
        public Task<bool> Allows(ResourceOwnership r, Guid a, ScopeAction s, CancellationToken ct) => Task.FromResult(Allow);
    }
    private sealed class Resources : IScopedAuthorityReader
    {
        public ResourceOwnership? Resource;
        public Task<ResourceOwnership?> ResolveResource(ResourceReference r, CancellationToken ct) => Task.FromResult(Resource);
        public Task<CurrentAuthority?> ResolveCurrent(Guid o, Guid a, CancellationToken ct) => throw new InvalidOperationException("Must not query grants");
        public Task<bool> CanAccess(Guid o, Guid a, ResourceReference r, ScopeAction s, CancellationToken ct) => throw new InvalidOperationException("Must not query grants");
    }
}
