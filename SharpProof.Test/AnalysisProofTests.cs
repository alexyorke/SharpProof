using NUnit.Framework;
using SharpProof.ProofCore.Analysis;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Test;

[TestFixture]
internal class AnalysisProofTests {
    [Test]
    public void FormulaTraversal_Contains_VisitsNestedConditionalBranches() {
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
    public void RewriteBottomUp_StructurallyEquivalentReplacementIsUnchanged() {
        var root = new SmtBinaryFormula(SmtBinaryOperator.And, new SmtBooleanConstant(true), new SmtBooleanConstant(false));

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
    public void FormulaTraversal_MapChildren_PreservesNodeMetadata() {
        var root = new SmtRegexMatchFormula(
            new SmtVariable("old", SmtValueKind.String),
            "a+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var mapped = (SmtRegexMatchFormula)SmtFormulaTraversal.MapChildren(root, static _ => new SmtVariable("new", SmtValueKind.String));

        Assert.Multiple(() => {
            Assert.That(mapped.Value, Is.EqualTo(new SmtVariable("new", SmtValueKind.String)));
            Assert.That(mapped.Pattern, Is.EqualTo(root.Pattern));
            Assert.That(mapped.Options, Is.EqualTo(root.Options));
        });
    }
    [TestCase(false, "impure_call_unreachable", TestName = "AnalysisProof_FalseHazardCondition_IsProven")]
    [TestCase(true, "impure_call_reachable", TestName = "AnalysisProof_TrueHazardCondition_IsDisproven")]
    public void ConstantEffectHazardMatrix(bool reachable, string reason) {
        using var search = new AnalysisProofSearch();
        var result = search.Classify(new SmtBooleanConstant(reachable), TimeSpan.FromSeconds(2));
        Assert.That(result.Outcome, Is.EqualTo(reachable ? AnalysisProofOutcome.Disproven : AnalysisProofOutcome.Proven));
        Assert.That(result.Reason, Is.EqualTo(reason));
    }
    [Test]
    public void AnalysisProof_ContradictoryPath_IsProven() {
        using var search = new AnalysisProofSearch();
        var x = new SmtVariable("x", SmtValueKind.Int);

        var result = search.Classify(
            new SmtFormula[] {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, x, new SmtIntegerConstant(0))
            },
            new SmtBooleanConstant(true),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }
    [Test]
    public void AnalysisProof_ReachableHazardGuard_IsDisproven() {
        using var search = new AnalysisProofSearch();
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));

        var result = search.Classify(new[] { xIsZero }, xIsZero, TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Disproven));
        Assert.That(result.Reason, Is.EqualTo("impure_call_reachable"));
        Assert.That(result.PathCheck.Witness, Is.Not.Null);
        Assert.That(result.HazardCheck.Witness, Is.Not.Null);
        Assert.That(result.HazardCheck.Witness!.Assignments.Single().IntegerValue, Is.EqualTo(0));
    }
    [Test]
    public void AnalysisProof_ApproximateRegexPathDoesNotProveHazardReachable() {
        using var search = new AnalysisProofSearch();
        var text = new SmtVariable("text", SmtValueKind.String);

        var result = search.Classify(
            new SmtFormula[] {
                new SmtBinaryFormula(SmtBinaryOperator.And, new SmtBooleanConstant(true), new SmtRegexMatchFormula(text, @"\A\bA\z")),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("A"))
            },
            new SmtBooleanConstant(true),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Unknown));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.HazardCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.Reason, Is.EqualTo("path_feasibility_unknown"));
    }
    [Test]
    public void AnalysisProof_NullReceiverCondition_IsDisproven() {
        using var search = new AnalysisProofSearch();
        var s = new SmtVariable("s", SmtValueKind.Reference);
        var sIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, s, new SmtNullConstant());

        var result = search.Classify(new AnalysisProofQuery(new[] { sIsNull }, new AnalysisHazard(AnalysisHazardKind.NullDereference,
            sIsNull)), TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Disproven));
        Assert.That(result.Reason, Is.EqualTo("null_dereference_reachable"));
    }
    [Test]
    public void AnalysisProof_NonZeroGuard_MakesDivideByZeroProven() {
        using var search = new AnalysisProofSearch();
        var divisor = new SmtVariable("divisor", SmtValueKind.Int);
        var divisorNotZero = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, divisor, new SmtIntegerConstant(0));
        var divisorIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, divisor, new SmtIntegerConstant(0));

        var result = search.Classify(
            new AnalysisProofQuery(new[] { divisorNotZero }, new AnalysisHazard(AnalysisHazardKind.DivideByZero, divisorIsZero)),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.Reason, Is.EqualTo("divide_by_zero_unreachable"));
        Assert.That(result.PathCheck.Witness, Is.Not.Null);
        Assert.That(result.PathCheck.Witness!.Assignments.Single().IntegerValue, Is.Not.EqualTo(0));
        Assert.That(result.HazardCheck.Witness?.Status, Is.EqualTo(SmtWitnessStatus.None));
    }
    [Test]
    public void AnalysisProof_ReachableEffectViolation_IsDisproven() {
        using var search = new AnalysisProofSearch();
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xIsNonNegative = new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(0));

        var result = search.Classify(
            new AnalysisProofQuery(new[] { xIsNonNegative }, new AnalysisHazard(AnalysisHazardKind.EffectViolationReachability,
                xIsNonNegative)),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Disproven));
        Assert.That(result.Reason, Is.EqualTo("impure_call_reachable"));
    }
    [Test]
    public void AnalysisProof_InternalOnlyEffectViolation_IsConservativeUnknown() {
        using var search = new AnalysisProofSearch();
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
        var query = new AnalysisProofQuery(new[] { xIsZero }, new AnalysisHazard(AnalysisHazardKind.EffectViolationReachability, xIsZero,
            AnalysisEffectVisibility.InternalOnly));

        var result = search.Classify(query, TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("invalid_internal_only_hazard"));
        Assert.That(result.PathCheck.WasAttempted, Is.False);
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.HazardCheck.WasAttempted, Is.False);
        Assert.That(result.HazardCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.HazardCheck.Witness, Is.Null);
    }
    [TestCase("StaticCacheRead", false, "safe_static_cache_read", TestName = "AnalysisProof_SafeStaticCacheRead_IsProven")]
    [TestCase("FreshOwnedObjectWrite", false, "fresh_owned_object_write", TestName = "AnalysisProof_FreshOwnedObjectWrite_IsProven")]
    [TestCase("FreshOwnedArrayWrite", false, "fresh_owned_array_write", TestName = "AnalysisProof_FreshOwnedArrayWrite_IsProven")]
    [TestCase("CallerVisibleMemoryWrite", true, "caller_visible_memory_write_reachable",
        TestName = "AnalysisProof_CallerVisibleMemoryWrite_IsDisproven")]
    public void StructuralEffectClassificationMatrix(string kind, bool disproven, string reason) {
        using var search = new AnalysisProofSearch();
        var result = search.Classify(
            new AnalysisProofQuery(Array.Empty<SmtFormula>(), new AnalysisHazard(Enum.Parse<AnalysisHazardKind>(kind),
                new SmtBooleanConstant(true))),
            TimeSpan.FromSeconds(2));
        Assert.That(result.Outcome, Is.EqualTo(disproven ? AnalysisProofOutcome.Disproven : AnalysisProofOutcome.Proven));
        Assert.That(result.Reason, Is.EqualTo(reason));
    }
    [Test]
    public void AnalysisProof_QueryBranchReachability_ContradictoryGuard_IsProven() {
        using var search = new AnalysisProofSearch();
        var x = new SmtVariable("x", SmtValueKind.Int);
        var query = new AnalysisProofQuery(
            new SmtFormula[] {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(0))
            },
            new AnalysisHazard(
                AnalysisHazardKind.BranchReachability,
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, x, new SmtIntegerConstant(0))));

        var result = search.Classify(query, TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
    }
    [Test]
    public void AnalysisProof_NullPathConditionsDefaultToEmpty() {
        using var search = new AnalysisProofSearch();
        var query = new AnalysisProofQuery(null!, new AnalysisHazard(AnalysisHazardKind.BranchReachability, new SmtBooleanConstant(true)));

        var result = search.Classify(query, TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Disproven));
        Assert.That(result.Reason, Is.EqualTo("branch_reachable"));
    }
}
internal static class AnalysisProofSearchTestExtensions {
    internal static AnalysisProofResult Classify(this AnalysisProofSearch search, SmtFormula impurityCondition, TimeSpan timeout) =>
        search.Classify(new AnalysisProofQuery(Array.Empty<SmtFormula>(),
            new AnalysisHazard(AnalysisHazardKind.EffectViolationReachability, impurityCondition)), timeout);

    internal static AnalysisProofResult Classify(this AnalysisProofSearch search, IEnumerable<SmtFormula> pathConditions,
        SmtFormula impurityCondition, TimeSpan timeout) =>
        search.Classify(new AnalysisProofQuery(pathConditions.ToArray(), new AnalysisHazard(AnalysisHazardKind.EffectViolationReachability,
            impurityCondition)), timeout);
}
