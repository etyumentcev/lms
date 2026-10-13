namespace Lms.Application;

// Trusted source contract, not a production clock implementation. Both intervals must
// contain the real values in one qualification epoch, including drift/measurement error.
public sealed record QualifiedClockReading(string QualificationId, bool Qualified, AuthorityTime Utc,
    TimeSpan MonotonicEarliest, TimeSpan MonotonicLatest);
public interface IQualifiedClockSource { QualifiedClockReading? Read(); }

// Limits are explicit host qualification inputs, not inferred SLAs. Loss of qualification
// latches this instance closed; recovery requires a newly qualified instance/epoch.
public sealed class QualifiedAuthorityClock(IQualifiedClockSource source, TimeSpan maximumUtcUncertainty,
    TimeSpan maximumMonotonicUncertainty) : IAuthorityClock
{
    private readonly object sync = new();
    private QualifiedClockReading? previous;
    private bool failed;
    public AuthorityTime? Read() { lock (sync) return Sample()?.Utc; }
    public Deadline? StartDeadline(TimeSpan budget)
    {
        lock (sync)
        {
            var sample = Sample();
            if (sample == null || budget <= TimeSpan.Zero || sample.MonotonicEarliest > TimeSpan.MaxValue - budget) return null;
            if (sample.Utc.Earliest > DateTimeOffset.MaxValue - budget) return null;
            return new Deadline(this, sample.QualificationId, sample.MonotonicEarliest + budget, sample.Utc.Earliest + budget);
        }
    }
    private QualifiedClockReading? Sample()
    {
        if (failed) return null;
        QualifiedClockReading? value;
        try { value = source.Read(); }
        catch { failed = true; return null; }
        if (maximumUtcUncertainty < TimeSpan.Zero || maximumMonotonicUncertainty < TimeSpan.Zero ||
            value is not { Qualified: true, Utc.Valid: true } || string.IsNullOrWhiteSpace(value.QualificationId) ||
            value.Utc.Latest - value.Utc.Earliest > maximumUtcUncertainty || value.MonotonicEarliest < TimeSpan.Zero ||
            value.MonotonicLatest < value.MonotonicEarliest || value.MonotonicLatest - value.MonotonicEarliest > maximumMonotonicUncertainty ||
            previous != null && (value.QualificationId != previous.QualificationId ||
                value.MonotonicEarliest < previous.MonotonicEarliest || value.MonotonicLatest < previous.MonotonicLatest ||
                value.Utc.Latest < previous.Utc.Earliest ||
                value.Utc.Latest - previous.Utc.Earliest < value.MonotonicEarliest - previous.MonotonicLatest ||
                value.Utc.Earliest - previous.Utc.Latest > value.MonotonicLatest - previous.MonotonicEarliest))
        { failed = true; return null; }
        previous = value;
        return value;
    }
    public sealed class Deadline
    {
        private readonly QualifiedAuthorityClock owner;
        private readonly string epoch;
        private readonly TimeSpan cutoff;
        public DateTimeOffset UtcCutoff { get; }
        internal Deadline(QualifiedAuthorityClock owner, string epoch, TimeSpan cutoff, DateTimeOffset utcCutoff)
        { this.owner = owner; this.epoch = epoch; this.cutoff = cutoff; UtcCutoff = utcCutoff; }
        // No budget reset and no UTC subtraction: queue/wait time consumes this same deadline.
        public TimeSpan? Remaining()
        {
            lock (owner.sync)
            {
                var sample = owner.Sample();
                if (sample == null || sample.QualificationId != epoch) return null;
                return sample.MonotonicLatest >= cutoff ? TimeSpan.Zero : cutoff - sample.MonotonicLatest;
            }
        }
    }
}
