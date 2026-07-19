using NUnit.Framework;
using SharpProof.ProofCore.Purity;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Test;

[TestFixture]
internal class ProofCorePurityProofTests
{
    [Test]
    public void FormulaTraversal_Contains_VisitsNestedConditionalBranches()
    {
        var target = new SmtVariable("target", SmtValueKind.String);
        var root = new SmtConditionalFormula(
            new SmtBooleanConstant(true),
            new SmtStringConstant("first"),
            new SmtStringConcatTerm(new SmtStringConstant("second"), target),
            SmtValueKind.String);

        Assert.That(SmtFormulaTraversal.Contains(root, formula => ReferenceEquals(formula, target)), Is.True);
        Assert.That(SmtFormulaTraversal.Contains(root, static formula => formula is SmtRegexMatchFormula), Is.False);
    }

    [Test]
    public void RewriteBottomUp_StructurallyEquivalentReplacementIsUnchanged()
    {
        var root = new SmtBinaryFormula(
            SmtBinaryOperator.And,
            new SmtBooleanConstant(true),
            new SmtBooleanConstant(false));

        var rewritten = SmtFormulaTraversal.RewriteBottomUp(
            root,
            static formula => formula is SmtBooleanConstant constant
                ? new SmtBooleanConstant(constant.Value)
                : formula,
            out var changed);

        Assert.That(changed, Is.False);
        Assert.That(SmtFormulaTraversal.AreStructurallyEqual(root, rewritten), Is.True);
    }

    [Test]
    public void FormulaTraversal_MapChildren_PreservesNodeMetadata()
    {
        var root = new SmtRegexMatchFormula(
            new SmtVariable("old", SmtValueKind.String),
            "a+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var mapped = (SmtRegexMatchFormula)SmtFormulaTraversal.MapChildren(
            root,
            static _ => new SmtVariable("new", SmtValueKind.String));

        Assert.Multiple(() =>
        {
            Assert.That(mapped.Value, Is.EqualTo(new SmtVariable("new", SmtValueKind.String)));
            Assert.That(mapped.Pattern, Is.EqualTo(root.Pattern));
            Assert.That(mapped.Options, Is.EqualTo(root.Options));
        });
    }

    [Test]
    public void PurityProof_FalseImpurityCondition_IsProvablyPure()
    {
        using var search = new PurityProofSearch();

        var result = search.Classify(
            new SmtBooleanConstant(false),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.Reason, Is.EqualTo("impure_call_unreachable"));
    }

    [Test]
    public void PurityProof_TrueImpurityCondition_IsProvablyImpure()
    {
        using var search = new PurityProofSearch();

        var result = search.Classify(
            new SmtBooleanConstant(true),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
        Assert.That(result.Reason, Is.EqualTo("impure_call_reachable"));
    }

    [Test]
    public void PurityProof_ContradictoryPath_IsProvablyPure()
    {
        using var search = new PurityProofSearch();
        var x = new SmtVariable("x", SmtValueKind.Int);

        var result = search.Classify(
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, x, new SmtIntegerConstant(0))
            },
            new SmtBooleanConstant(true),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void PurityProof_ReachableImpurityGuard_IsProvablyImpure()
    {
        using var search = new PurityProofSearch();
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));

        var result = search.Classify(
            new[] { xIsZero },
            xIsZero,
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
        Assert.That(result.Reason, Is.EqualTo("impure_call_reachable"));
        Assert.That(result.PathCheck.Witness, Is.Not.Null);
        Assert.That(result.ImpurityCheck.Witness, Is.Not.Null);
        Assert.That(result.ImpurityCheck.Witness!.Assignments.Single().IntegerValue, Is.EqualTo(0));
    }

    [Test]
    public void PurityProof_ApproximateRegexPathDoesNotProveImpurityReachable()
    {
        using var search = new PurityProofSearch();
        var text = new SmtVariable("text", SmtValueKind.String);

        var result = search.Classify(
            new SmtFormula[]
            {
                new SmtBinaryFormula(
                    SmtBinaryOperator.And,
                    new SmtBooleanConstant(true),
                    new SmtRegexMatchFormula(text, @"\A\bA\z")),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("A"))
            },
            new SmtBooleanConstant(true),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.ImpurityCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.Reason, Is.EqualTo("path_feasibility_unknown"));
    }

    [Test]
    public void PurityProof_NullReceiverCondition_IsProvablyImpure()
    {
        using var search = new PurityProofSearch();
        var s = new SmtVariable("s", SmtValueKind.Reference);
        var sIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, s, new SmtNullConstant());

        var result = search.Classify(
            new PurityProofQuery(
                new[] { sIsNull },
                new PurityHazard(PurityHazardKind.NullDereference, sIsNull)),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
        Assert.That(result.Reason, Is.EqualTo("null_dereference_reachable"));
    }

    [Test]
    public void PurityProof_NonZeroGuard_MakesDivideByZeroProvablyPure()
    {
        using var search = new PurityProofSearch();
        var divisor = new SmtVariable("divisor", SmtValueKind.Int);
        var divisorNotZero = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, divisor, new SmtIntegerConstant(0));
        var divisorIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, divisor, new SmtIntegerConstant(0));

        var result = search.Classify(
            new PurityProofQuery(
                new[] { divisorNotZero },
                new PurityHazard(PurityHazardKind.DivideByZero, divisorIsZero)),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.Reason, Is.EqualTo("divide_by_zero_unreachable"));
        Assert.That(result.PathCheck.Witness, Is.Not.Null);
        Assert.That(result.PathCheck.Witness!.Assignments.Single().IntegerValue, Is.Not.EqualTo(0));
        Assert.That(result.ImpurityCheck.Witness?.Status, Is.EqualTo(SmtWitnessStatus.None));
    }

    [Test]
    public void PurityProof_ReachableImpureCall_IsProvablyImpure()
    {
        using var search = new PurityProofSearch();
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xIsNonNegative = new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(0));

        var result = search.Classify(
            new PurityProofQuery(
                new[] { xIsNonNegative },
                new PurityHazard(PurityHazardKind.ImpureCallReachability, xIsNonNegative)),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
        Assert.That(result.Reason, Is.EqualTo("impure_call_reachable"));
    }

    [Test]
    public void PurityProof_InternalOnlyImpureCall_IsConservativeUnknown()
    {
        using var search = new PurityProofSearch();
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
        var query = new PurityProofQuery(
            new[] { xIsZero },
            new PurityHazard(
                PurityHazardKind.ImpureCallReachability,
                xIsZero,
                PurityEffectVisibility.InternalOnly));

        var result = search.Classify(query, TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("invalid_internal_only_hazard"));
        Assert.That(result.PathCheck.WasAttempted, Is.False);
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.ImpurityCheck.WasAttempted, Is.False);
        Assert.That(result.ImpurityCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.ImpurityCheck.Witness, Is.Null);
    }

    [Test]
    public void PurityProof_SafeStaticCacheRead_IsProvablyPure()
    {
        using var search = new PurityProofSearch();

        var result = search.Classify(
            new PurityProofQuery(
                Array.Empty<SmtFormula>(),
                new PurityHazard(PurityHazardKind.StaticCacheRead, new SmtBooleanConstant(true))),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.Reason, Is.EqualTo("safe_static_cache_read"));
    }

    [Test]
    public void PurityProof_FreshOwnedObjectWrite_IsProvablyPure()
    {
        using var search = new PurityProofSearch();

        var result = search.Classify(
            new PurityProofQuery(
                Array.Empty<SmtFormula>(),
                new PurityHazard(PurityHazardKind.FreshOwnedObjectWrite, new SmtBooleanConstant(true))),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.Reason, Is.EqualTo("fresh_owned_object_write"));
    }

    [Test]
    public void PurityProof_FreshOwnedArrayWrite_IsProvablyPure()
    {
        using var search = new PurityProofSearch();

        var result = search.Classify(
            new PurityProofQuery(
                Array.Empty<SmtFormula>(),
                new PurityHazard(PurityHazardKind.FreshOwnedArrayWrite, new SmtBooleanConstant(true))),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.Reason, Is.EqualTo("fresh_owned_array_write"));
    }

    [Test]
    public void PurityProof_CallerVisibleMemoryWrite_IsProvablyImpure()
    {
        using var search = new PurityProofSearch();
        var memoryWrite = new SmtBooleanConstant(true);

        var result = search.Classify(
            new PurityProofQuery(
                Array.Empty<SmtFormula>(),
                new PurityHazard(PurityHazardKind.CallerVisibleMemoryWrite, memoryWrite)),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
        Assert.That(result.Reason, Is.EqualTo("caller_visible_memory_write_reachable"));
    }

    [Test]
    public void PurityProof_QueryBranchReachability_ContradictoryGuard_IsProvablyPure()
    {
        using var search = new PurityProofSearch();
        var x = new SmtVariable("x", SmtValueKind.Int);
        var query = new PurityProofQuery(
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(0))
            },
            new PurityHazard(
                PurityHazardKind.BranchReachability,
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, x, new SmtIntegerConstant(0))));

        var result = search.Classify(query, TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
    }

    [Test]
    public void PurityProof_NullPathConditionsDefaultToEmpty()
    {
        using var search = new PurityProofSearch();
        var query = new PurityProofQuery(
            null!,
            new PurityHazard(
                PurityHazardKind.BranchReachability,
                new SmtBooleanConstant(true)));

        var result = search.Classify(query, TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
        Assert.That(result.Reason, Is.EqualTo("branch_reachable"));
    }
}

internal static class PurityProofSearchTestExtensions
{
    internal static PurityProofResult Classify(
        this PurityProofSearch search,
        SmtFormula impurityCondition,
        TimeSpan timeout) =>
        search.Classify(
            new PurityProofQuery(
                Array.Empty<SmtFormula>(),
                new PurityHazard(PurityHazardKind.ImpureCallReachability, impurityCondition)),
            timeout);

    internal static PurityProofResult Classify(
        this PurityProofSearch search,
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula impurityCondition,
        TimeSpan timeout) =>
        search.Classify(
            new PurityProofQuery(
                pathConditions.ToArray(),
                new PurityHazard(PurityHazardKind.ImpureCallReachability, impurityCondition)),
            timeout);
}
