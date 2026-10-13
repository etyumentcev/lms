using Lms.Application;

namespace Lms.Infrastructure;

public enum ClockQualificationState { Untrusted, Qualified, Invalidated }

// Bounds refer to the same sampled instant. SourceId identifies the local profile,
// clocksource and time namespace; BootId changes on reboot. No UTC trust is implied.
public sealed record LocalClockObservation(string SourceId, string BootId, AuthorityTime WallUtc,
    TimeSpan MonotonicLower, TimeSpan MonotonicUpper, TimeSpan BootLower, TimeSpan BootUpper);
public interface ILocalClockProbe { LocalClockObservation? Read(); }
public sealed record ClockQualificationRequest(string Nonce, LocalClockObservation Anchor);

// Numbers require independently verified evidence, including the maximum fractional
// MONOTONIC rate error relative to true elapsed time. Missing is never zero error.
public sealed record ConfirmedClockEnvelope(string RequestNonce, string EvidenceId, AuthorityTime UtcAtAnchor,
    long MaximumRateErrorPpb, TimeSpan MaximumAge, DateTimeOffset ValidUntil,
    TimeSpan MaximumUtcWidth, TimeSpan MaximumElapsedWidth);
public interface ILocalClockQualificationVerifier
{
    ConfirmedClockEnvelope? Confirm(ClockQualificationRequest request);
    // Must reject lost/ambiguous upstream, incompatible configuration, time discipline
    // changes and invalidated evidence. This is a trusted host port, not client input.
    bool IsCurrent(ConfirmedClockEnvelope envelope, LocalClockObservation observation);
}

// Local chrony has no proven external UTC/rate/age envelope. Its tracking snapshot
// cannot grant qualification. This is the only supplied deployment verifier.
public sealed class UnprovenLocalClockVerifier : ILocalClockQualificationVerifier
{
    public ConfirmedClockEnvelope? Confirm(ClockQualificationRequest request) => null;
    public bool IsCurrent(ConfirmedClockEnvelope envelope, LocalClockObservation observation) => false;
}

public sealed record LocalClockStatus(ClockQualificationState State, string? QualificationId,
    string? SourceId, string? BootId, string? EvidenceId, TimeSpan? AgeUpper,
    TimeSpan? UtcWidth, TimeSpan? MaximumAge, DateTimeOffset? ValidUntil, string? Failure);
public sealed record LocalClockSnapshot(LocalClockStatus Status, QualifiedClockReading? Reading);

// Isolated source for QualifiedAuthorityClock. Read never restores trust. Existing
// adapters/deadlines stay terminal after failure or replacement, even if they missed
// the fault. All transitions and observations are serialized by one in-memory lock.
public sealed class LocalQualifiedClockSource(ILocalClockProbe probe, ILocalClockQualificationVerifier verifier)
    : IQualifiedClockSource
{
    private const long Scale = 1_000_000_000;
    private readonly object sync = new();
    private ClockQualificationState state;
    private ConfirmedClockEnvelope? envelope;
    private LocalClockObservation? anchor, previous;
    private QualifiedClockReading? last;
    private string? epoch, failure;
    private long wallOffsetLower, wallOffsetUpper, bootOffsetLower, bootOffsetUpper;

    public bool Qualify()
    {
        lock (sync)
        {
            if (state == ClockQualificationState.Qualified) Invalidate("qualification-replaced");
            try
            {
                var observation = probe.Read();
                if (!Valid(observation)) return Reject("source-unavailable");
                var request = new ClockQualificationRequest(Guid.NewGuid().ToString("N"), observation!);
                var confirmed = verifier.Confirm(request);
                if (confirmed == null || confirmed.RequestNonce != request.Nonce ||
                    string.IsNullOrWhiteSpace(confirmed.EvidenceId) || confirmed.UtcAtAnchor is not { Valid: true } ||
                    confirmed.MaximumRateErrorPpb < 0 || confirmed.MaximumRateErrorPpb >= Scale ||
                    confirmed.MaximumAge <= TimeSpan.Zero || confirmed.MaximumUtcWidth < TimeSpan.Zero ||
                    confirmed.MaximumElapsedWidth < TimeSpan.Zero ||
                    confirmed.UtcAtAnchor.Latest >= confirmed.ValidUntil ||
                    confirmed.UtcAtAnchor.Latest - confirmed.UtcAtAnchor.Earliest > confirmed.MaximumUtcWidth ||
                    !verifier.IsCurrent(confirmed, observation!)) return Reject("qualification-not-confirmed");
                anchor = previous = observation;
                envelope = confirmed;
                epoch = request.Nonce;
                last = new(epoch, true, confirmed.UtcAtAnchor, TimeSpan.Zero, TimeSpan.Zero);
                (wallOffsetLower, wallOffsetUpper) = WallOffset(observation!);
                (bootOffsetLower, bootOffsetUpper) = BootOffset(observation!);
                state = ClockQualificationState.Qualified;
                failure = null;
                // Waiting for confirmation consumes age; never reset the anchor here.
                return Sample() != null;
            }
            catch { return Reject("qualification-error"); }
        }
    }

    public QualifiedClockReading? Read() => Snapshot().Reading;
    public LocalClockSnapshot Snapshot()
    {
        lock (sync)
        {
            var reading = Sample();
            return new(new(state, epoch, anchor?.SourceId, anchor?.BootId, envelope?.EvidenceId,
                reading?.MonotonicLatest, reading == null ? null : reading.Utc.Latest - reading.Utc.Earliest,
                envelope?.MaximumAge, envelope?.ValidUntil, failure), reading);
        }
    }
    // Trusted host notification of independently detected discontinuity.
    public void Invalidate(string reason)
    {
        lock (sync)
        {
            if (state == ClockQualificationState.Qualified) state = ClockQualificationState.Invalidated;
            failure = reason;
        }
    }
    private bool Reject(string reason) { Invalidate(reason); return false; }
    private QualifiedClockReading? Fail(string reason) { Invalidate(reason); return null; }

    private QualifiedClockReading? Sample()
    {
        if (state != ClockQualificationState.Qualified) return null;
        try
        {
            var value = probe.Read();
            if (!Valid(value)) return Fail("source-unavailable");
            var a = anchor!; var p = previous!; var e = envelope!;
            if (value!.SourceId != a.SourceId || value.BootId != a.BootId) return Fail("source-or-boot-changed");
            if (!verifier.IsCurrent(e, value)) return Fail("qualification-lost");
            if (value.MonotonicLower < p.MonotonicLower || value.MonotonicUpper < p.MonotonicUpper ||
                value.BootLower < p.BootLower || value.BootUpper < p.BootUpper) return Fail("counter-regression");
            // Intersect offsets over the whole epoch: small repeated changes cannot
            // reset the window. Overlap does NOT prove every step/suspend was detected.
            var wall = WallOffset(value); var boot = BootOffset(value);
            wallOffsetLower = Math.Max(wallOffsetLower, wall.Item1);
            wallOffsetUpper = Math.Min(wallOffsetUpper, wall.Item2);
            bootOffsetLower = Math.Max(bootOffsetLower, boot.Item1);
            bootOffsetUpper = Math.Min(bootOffsetUpper, boot.Item2);
            if (wallOffsetLower > wallOffsetUpper) return Fail("wall-clock-step");
            if (bootOffsetLower > bootOffsetUpper) return Fail("suspend-or-boot-discontinuity");
            long lowDelta = Math.Max(0, checked(value.MonotonicLower.Ticks - a.MonotonicUpper.Ticks));
            long highDelta = checked(value.MonotonicUpper.Ticks - a.MonotonicLower.Ticks);
            // Integer arithmetic, outward rounding, no floating point optimism.
            long lower = checked((long)((Int128)lowDelta * Scale / (Scale + e.MaximumRateErrorPpb)));
            Int128 numerator = (Int128)highDelta * Scale;
            long divisor = Scale - e.MaximumRateErrorPpb;
            long upper = checked((long)((numerator + divisor - 1) / divisor));
            lower = Math.Max(lower, last!.MonotonicEarliest.Ticks);
            upper = Math.Max(upper, last.MonotonicLatest.Ticks);
            var utc = new AuthorityTime(e.UtcAtAnchor.Earliest.AddTicks(lower), e.UtcAtAnchor.Latest.AddTicks(upper));
            if (upper >= e.MaximumAge.Ticks || utc.Latest >= e.ValidUntil) return Fail("qualification-expired");
            if (!utc.Valid || utc.Latest - utc.Earliest > e.MaximumUtcWidth ||
                upper - lower > e.MaximumElapsedWidth.Ticks) return Fail("uncertainty-exceeded");
            previous = value;
            return last = new(epoch!, true, utc, TimeSpan.FromTicks(lower), TimeSpan.FromTicks(upper));
        }
        catch { return Fail("source-or-arithmetic-error"); }
    }
    private static bool Valid(LocalClockObservation? v) => v != null &&
        !string.IsNullOrWhiteSpace(v.SourceId) && !string.IsNullOrWhiteSpace(v.BootId) &&
        v.WallUtc is { Valid: true } && v.MonotonicLower >= TimeSpan.Zero && v.MonotonicUpper >= v.MonotonicLower &&
        v.BootLower >= TimeSpan.Zero && v.BootUpper >= v.BootLower;
    private static (long, long) WallOffset(LocalClockObservation v) =>
        (checked(v.WallUtc.Earliest.UtcTicks - v.MonotonicUpper.Ticks), checked(v.WallUtc.Latest.UtcTicks - v.MonotonicLower.Ticks));
    private static (long, long) BootOffset(LocalClockObservation v) =>
        (checked(v.BootLower.Ticks - v.MonotonicUpper.Ticks), checked(v.BootUpper.Ticks - v.MonotonicLower.Ticks));
}
