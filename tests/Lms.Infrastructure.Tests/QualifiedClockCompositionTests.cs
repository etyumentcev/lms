using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application;
using static Lms.Infrastructure.Tests.LocalQualifiedClockSourceTests;

namespace Lms.Infrastructure.Tests;

public sealed class QualifiedClockCompositionTests
{
    [Theory]
    [InlineData(-1, true)] [InlineData(0, false)] [InlineData(1, false)]
    public void Jwt_admission_requires_upper_strictly_before_exp(long offset, bool allowed)
    {
        var probe = new Probe(); var source = new LocalQualifiedClockSource(probe, new Verifier());
        Assert.True(source.Qualify()); var clock = Clock(source);
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "clock-test", "component", clock);
        var exp = ComponentExpiry.Calculate(clock.Read(), At.AddHours(1), [null])!.Value;
        Assert.Equal(At.AddSeconds(900), exp);
        var token = Signed(key, exp);
        probe.Value = Observation(TimeSpan.FromSeconds(900).Ticks + offset);
        Assert.Equal(allowed, jwt.Validate(token) != null);
        source.Invalidate("unknown-clock"); Assert.Null(jwt.Validate(token));
    }

    [Theory]
    [InlineData(-1, true)] [InlineData(0, false)] [InlineData(1, false)]
    public void Upper_interval_endpoint_not_midpoint_controls_admission(long offset, bool allowed)
    {
        var probe = new Probe(); var verifier = new Verifier { Factory = r => Envelope(r) with
            { UtcAtAnchor = new(At, At.AddSeconds(2)) } };
        var source = new LocalQualifiedClockSource(probe, verifier); Assert.True(source.Qualify());
        var clock = Clock(source); using var key = RSA.Create(2048);
        var token = Signed(key, At.AddSeconds(900));
        probe.Value = Observation(TimeSpan.FromSeconds(898).Ticks + offset);
        Assert.Equal(allowed, new ComponentJwt(key, "clock-test", "component", clock).Validate(token) != null);
    }

    [Theory]
    [InlineData(-1, true)] [InlineData(0, false)] [InlineData(1, false)]
    public void D_and_original_t0_budget_are_strict_decision_inputs(long offset, bool allowed)
    {
        var probe = new Probe(); var source = new LocalQualifiedClockSource(probe, new Verifier()); Assert.True(source.Qualify());
        var clock = Clock(source); var deadline = clock.StartDeadline(TimeSpan.FromSeconds(60))!;
        probe.Value = Observation(TimeSpan.FromSeconds(45).Ticks);
        Assert.Equal(TimeSpan.FromSeconds(15), deadline.Remaining());
        probe.Value = Observation(TimeSpan.FromSeconds(60).Ticks + offset);
        // Component inputs at q only: not an application/HTTP dispatch integration.
        Assert.Equal(allowed, clock.Read()!.Latest < At.AddSeconds(60));
        Assert.Equal(allowed, deadline.Remaining() > TimeSpan.Zero);
        Assert.Equal(allowed ? TimeSpan.FromTicks(1) : TimeSpan.Zero, deadline.Remaining());
    }

    [Theory]
    [InlineData(60)] [InlineData(480)]
    public void Budget_cannot_increase_with_uncertainty_and_requalification_cannot_reset_t0(int budget)
    {
        var probe = new Probe(); var verifier = new Verifier { Factory = r => Envelope(r) with { MaximumRateErrorPpb = 1_000_000 } };
        var source = new LocalQualifiedClockSource(probe, verifier); Assert.True(source.Qualify());
        var clock = Clock(source); var deadline = clock.StartDeadline(TimeSpan.FromSeconds(budget))!;
        var previous = TimeSpan.FromSeconds(budget);
        for (int second = 0; second <= budget; second++)
        {
            probe.Value = Observation(TimeSpan.FromSeconds(second).Ticks);
            var remaining = deadline.Remaining(); Assert.NotNull(remaining);
            Assert.True(remaining <= previous); Assert.True(remaining <= TimeSpan.FromSeconds(budget - second));
            previous = remaining.Value;
        }
        Assert.Equal(TimeSpan.Zero, previous);
        source.Invalidate("resume"); Assert.True(source.Qualify()); Assert.Null(deadline.Remaining());
    }

    [Theory]
    [InlineData(-1, 899)] [InlineData(0, 900)] [InlineData(1, 900)]
    public void Lifetime_900_and_reserve_600_are_never_rounded_up(long offset, int seconds)
    {
        var probe = new Probe(); var verifier = new Verifier { Factory = r => Envelope(r) with
            { UtcAtAnchor = new(At.AddTicks(offset), At.AddTicks(offset + 2)) } };
        var source = new LocalQualifiedClockSource(probe, verifier); Assert.True(source.Qualify());
        var reading = Clock(source).Read()!;
        var exp = ComponentExpiry.Calculate(reading, At.AddHours(1), [null])!.Value;
        Assert.Equal(At.AddSeconds(seconds), exp);
        Assert.True(exp - reading.Earliest <= TimeSpan.FromSeconds(900));
        var d = At.AddSeconds(700).AddTicks(offset);
        var reserved = ComponentExpiry.Calculate(reading, At.AddHours(1), [d])!.Value;
        Assert.True(reserved <= d.AddSeconds(-600));
        Assert.Equal(At.AddSeconds(offset < 0 ? 99 : 100), reserved);
        Assert.Null(ComponentExpiry.Calculate(null, At.AddHours(1), [d]));
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(1)]
    public void Reserve_exactly_600_leaves_no_issuable_whole_second(long offset)
    {
        var source = new LocalQualifiedClockSource(new Probe(), new Verifier()); Assert.True(source.Qualify());
        Assert.Null(ComponentExpiry.Calculate(Clock(source).Read(), At.AddHours(1), [At.AddSeconds(600).AddTicks(offset)]));
    }

    [Fact]
    public void Sampling_brackets_conservatively_consume_budget()
    {
        var probe = new Probe { Value = Observation(100) with
            { MonotonicLower = TimeSpan.FromTicks(98), MonotonicUpper = TimeSpan.FromTicks(102) } };
        var source = new LocalQualifiedClockSource(probe, new Verifier()); Assert.True(source.Qualify());
        var clock = Clock(source); var deadline = clock.StartDeadline(TimeSpan.FromTicks(100))!;
        probe.Value = Observation(150) with
            { MonotonicLower = TimeSpan.FromTicks(147), MonotonicUpper = TimeSpan.FromTicks(153) };
        var value = source.Read()!;
        Assert.Equal(TimeSpan.FromTicks(45), value.MonotonicEarliest);
        Assert.Equal(TimeSpan.FromTicks(55), value.MonotonicLatest);
        Assert.Equal(TimeSpan.FromTicks(45), deadline.Remaining());
    }

    private static AccessToken Signed(RSA key, DateTimeOffset exp)
    {
        var actor = Guid.NewGuid();
        CurrentAuthority scope = new(new(Guid.NewGuid(), actor, 1, 1, ScopeRole.Student, 0),
            [new(AuthorityKind.Enrollment, null, "synthetic", "g", 1, 1, 1)]);
        var header = JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid = ComponentSigningKeys.KeyId(key) });
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { iss = "clock-test", aud = "component", sub = Guid.NewGuid(),
            actor, sid = Guid.NewGuid(), scope, nbf = At.ToUnixTimeSeconds(), exp = exp.ToUnixTimeSeconds() });
        static string B64(byte[] x) => Convert.ToBase64String(x).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var data = B64(header) + "." + B64(payload);
        return new(data + "." + B64(key.SignData(Encoding.ASCII.GetBytes(data), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
    }
}

public sealed partial class ScopedAuthorityTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Clock01_real_Postgres_issuer_composes_with_source_and_denies_invalidation_after_commit(bool invalidate)
    {
        var cs = await Seed(); var identity = await IssuingIdentity(cs);
        // Match the existing fixture's Oct 9 session timestamps; no fixture mutation.
        var probe = new Probe { Value = Observation(0) with { WallUtc = new(At, At) } };
        var source = new LocalQualifiedClockSource(probe, new Verifier()); Assert.True(source.Qualify());
        var qualified = LocalQualifiedClockSourceTests.Clock(source);
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "synthetic", "component", qualified);
        var observer = new AfterIssuanceCommit(() => { if (invalidate) source.Invalidate("qualification-lost-at-commit"); });
        await using var db = PostgresFixture.Open(cs, observer);
        var result = await new PostgresComponentTokenIssuer(db, qualified, jwt).Issue(identity.User, identity.Session, A);
        Assert.True(observer.Reached);
        if (invalidate) Assert.Null(result);
        else
        {
            Assert.NotNull(result); var validated = jwt.Validate(result); Assert.NotNull(validated);
            Assert.Equal(At.AddSeconds(900), validated.ExpiresAt);
        }
    }
}
