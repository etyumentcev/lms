using System.Diagnostics.CodeAnalysis;

namespace Lms.Application;

// Deliberately separate from ScopeAction ordinals. No conversion or generic bit planner.
public enum Prs1Operation
{
    Read = 1, Submit = 2, Review = 4,
    SubmitWithReturnedContent = 3, ReviewWithSubmissionRead = 5
}

public enum Prs1OperationError { None, EmptyMask, NegativeMask, UnknownBits, ReservedDeny, UnsupportedOperation }

/// <summary>
/// Explicit registry of declared required bits only, not an admission result or proof of effect completeness.
/// Future callers must independently prove the full effect set; no persisted-row classification is provided.
/// </summary>
public sealed record Prs1OperationPlan
{
    public int RequiredBits { get; }
    private Prs1OperationPlan(int bits) => RequiredBits = bits;

    public static bool TryForOperation(Prs1Operation operation,
        [NotNullWhen(true)] out Prs1OperationPlan? plan, out Prs1OperationError error)
    {
        switch (operation)
        {
            case Prs1Operation.Read: return TryCreate(1, out plan, out error);
            case Prs1Operation.Submit: return TryCreate(2, out plan, out error);
            case Prs1Operation.Review: return TryCreate(4, out plan, out error);
            case Prs1Operation.SubmitWithReturnedContent: return TryCreate(3, out plan, out error);
            case Prs1Operation.ReviewWithSubmissionRead: return TryCreate(5, out plan, out error);
            default:
                plan = null;
                error = Prs1OperationError.UnsupportedOperation;
                return false;
        }
    }

    public static bool TryCreate(int requiredBits,
        [NotNullWhen(true)] out Prs1OperationPlan? plan, out Prs1OperationError error)
    {
        plan = null;
        error = ValidateNewRestrictionMask(requiredBits);
        if (error != Prs1OperationError.None) return false;
        plan = new(requiredBits);
        return true;
    }

    /// <summary>Validates a new value, never classifies or evaluates an existing persisted restriction.</summary>
    public static Prs1OperationError ValidateNewRestrictionMask(int mask) => mask switch
    {
        1 or 2 or 3 or 4 or 5 or 6 or 7 => Prs1OperationError.None,
        0 => Prs1OperationError.EmptyMask,
        < 0 => Prs1OperationError.NegativeMask,
        _ when (mask & ~31) != 0 => Prs1OperationError.UnknownBits,
        _ => Prs1OperationError.ReservedDeny
    };
}
