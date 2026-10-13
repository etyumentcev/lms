using Lms.Application;

namespace Lms.Application.Tests;

public sealed class Prs1OperationPlanTests
{
    [Theory]
    [InlineData(Prs1Operation.Read, 1)]
    [InlineData(Prs1Operation.Submit, 2)]
    [InlineData(Prs1Operation.Review, 4)]
    [InlineData(Prs1Operation.SubmitWithReturnedContent, 3)]
    [InlineData(Prs1Operation.ReviewWithSubmissionRead, 5)]
    public void B01_B06_Explicit_operation_registry(Prs1Operation operation, int expected)
    {
        Assert.Equal(expected, (int)operation);
        Assert.True(Prs1OperationPlan.TryForOperation(operation, out var plan, out var error));
        Assert.Equal(Prs1OperationError.None, error);
        Assert.Equal(expected, plan.RequiredBits);
    }

    public static IEnumerable<object[]> MaskPlanPairs()
    {
        for (var mask = 1; mask <= 7; mask++)
            for (var plan = 1; plan <= 7; plan++) yield return new object[] { mask, plan };
    }

    [Theory]
    [MemberData(nameof(MaskPlanPairs))]
    public void B04_All_49_registry_pairs_preserve_bits_without_evaluating_restrictions(int mask, int bits)
    {
        Assert.Equal(Prs1OperationError.None, Prs1OperationPlan.ValidateNewRestrictionMask(mask));
        Assert.True(Prs1OperationPlan.TryCreate(bits, out var plan, out var error));
        Assert.Equal(Prs1OperationError.None, error);
        Assert.Equal(bits, plan.RequiredBits);
        // Independent set oracle checks registry numbers only. This is not a restriction evaluator.
        int[][] sets = [[], [1], [2], [1, 2], [4], [1, 4], [2, 4], [1, 2, 4]];
        Assert.Equal(sets[mask].Intersect(sets[bits]).Any(), (mask & plan.RequiredBits) != 0);
    }

    public static IEnumerable<object[]> InvalidMasks()
    {
        yield return new object[] { 0, Prs1OperationError.EmptyMask };
        foreach (var mask in new[] { -1, int.MinValue }) yield return new object[] { mask, Prs1OperationError.NegativeMask };
        for (var mask = 8; mask <= 31; mask++) yield return new object[] { mask, Prs1OperationError.ReservedDeny };
        foreach (var mask in new[] { 32, 33, 39, 40, 48, 63, 64, 255, int.MaxValue })
            yield return new object[] { mask, Prs1OperationError.UnknownBits };
        for (var bit = 7; bit < 31; bit++) yield return new object[] { 1 << bit, Prs1OperationError.UnknownBits };
    }

    [Theory]
    [MemberData(nameof(InvalidMasks))]
    public void B02_B03_B05_RB03_Invalid_new_masks_and_required_plans_fail_with_reason(int bits, Prs1OperationError expected)
    {
        Assert.Equal(expected, Prs1OperationPlan.ValidateNewRestrictionMask(bits));
        Assert.False(Prs1OperationPlan.TryCreate(bits, out var plan, out var error));
        Assert.Null(plan);
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData(0)][InlineData(-1)][InlineData(6)][InlineData(7)][InlineData(8)][InlineData(16)][InlineData(32)][InlineData(99)]
    public void B07_Unknown_operations_do_not_default_to_read(int action)
    {
        Assert.False(Prs1OperationPlan.TryForOperation((Prs1Operation)action, out var plan, out var error));
        Assert.Null(plan);
        Assert.Equal(Prs1OperationError.UnsupportedOperation, error);
    }

    [Fact]
    public void B06_Compound_plans_explicitly_include_read()
    {
        foreach (var operation in new[] { Prs1Operation.SubmitWithReturnedContent, Prs1Operation.ReviewWithSubmissionRead })
        {
            Assert.True(Prs1OperationPlan.TryForOperation(operation, out var plan, out _));
            Assert.Equal(1, plan.RequiredBits & 1);
        }
    }

    [Fact]
    public void No_implicit_ScopeAction_mapping_or_public_construction()
    {
        Assert.NotEqual((int)ScopeAction.Read, (int)Prs1Operation.Read);
        Assert.NotEqual((int)ScopeAction.Submit, (int)Prs1Operation.Submit);
        Assert.NotEqual((int)ScopeAction.Review, (int)Prs1Operation.Review);
        foreach (var type in new[] { typeof(Prs1OperationPlan), typeof(Prs1Selector) })
        {
            Assert.Empty(type.GetConstructors());
            Assert.DoesNotContain(type.GetMethods(), m => m.Name is "op_Implicit" or "op_Explicit");
            Assert.DoesNotContain(type.GetMethods().SelectMany(m => m.GetParameters()), p => p.ParameterType == typeof(ScopeAction));
            Assert.All(type.GetProperties(), p => Assert.Null(p.SetMethod));
        }
    }
}
