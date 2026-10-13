namespace Lms.Application;

// Ordinary educational authority only; processing prohibitions remain independent.
public static class RevocationPolicy
{
    public static readonly TimeSpan MaximumOperationLifetime = TimeSpan.FromMinutes(30);
    // Conditional execution envelope: cancellation, pending I/O/COMMIT, disposal and
    // controlled egress must finish within this reserve. Not a measured deployment SLA.
    public static readonly TimeSpan CompletionReserve = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaximumRevocationDelay = TimeSpan.FromMinutes(60);
    public static TimeSpan LatestOldAuthorityCompletion => SessionPolicy.MaximumAccessLifetime + MaximumOperationLifetime;
}
