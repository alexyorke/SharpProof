using SearchLib.Purity;
using SearchLib.Smt;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicProofPipeline
{
    private readonly SmtAnalysisService? _smtAnalysis;

    internal SymbolicProofPipeline(SmtAnalysisService? smtAnalysis)
    {
        _smtAnalysis = smtAnalysis;
    }

    internal SymbolicIrProofResult ClassifyReachability(
        IEnumerable<SmtFormula> pathConditions,
        Func<SymbolicBudgetInfo?> budgetFactory,
        SymbolicProofSupport support)
    {
        var result = ClassifyPathFeasibility(pathConditions);
        return SymbolicIrProofResult.FromReachability(result, budgetFactory(), support);
    }

    internal SymbolicIrProofResult ClassifyImplication(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula factFormula,
        Func<SymbolicBudgetInfo?> budgetFactory,
        SymbolicProofSupport support)
    {
        var result = ClassifyRawImplication(pathConditions, factFormula);
        return SymbolicIrProofResult.FromImplication(result, budgetFactory(), support);
    }

    internal SymbolicIrProofResult ClassifyConditionTruth(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula conditionFormula,
        Func<SymbolicBudgetInfo?> budgetFactory,
        SymbolicProofSupport support)
    {
        if (conditionFormula == null) throw new ArgumentNullException(nameof(conditionFormula));

        var normalizedPath = pathConditions as IReadOnlyCollection<SmtFormula> ?? pathConditions.ToArray();
        var trueProof = ClassifyRawImplication(normalizedPath, conditionFormula);
        if (trueProof.Outcome == PurityProofOutcome.ProvablyPure)
        {
            var status = string.Equals(trueProof.Reason, "path_unsatisfiable", StringComparison.Ordinal)
                ? SymbolicProofStatus.Unreachable
                : SymbolicProofStatus.ProvenTrue;
            return SymbolicIrProofResult.FromConditionTruth(trueProof, status, budgetFactory(), support);
        }

        var falseProof = ClassifyRawImplication(
            normalizedPath,
            new SmtUnaryFormula(SmtUnaryOperator.Not, conditionFormula));
        if (falseProof.Outcome == PurityProofOutcome.ProvablyPure)
        {
            var status = string.Equals(falseProof.Reason, "path_unsatisfiable", StringComparison.Ordinal)
                ? SymbolicProofStatus.Unreachable
                : SymbolicProofStatus.ProvenFalse;
            return SymbolicIrProofResult.FromConditionTruth(falseProof, status, budgetFactory(), support);
        }

        return SymbolicIrProofResult.FromConditionTruth(
            falseProof,
            SymbolicProofStatus.Unknown,
            budgetFactory(),
            support);
    }

    internal PurityProofResult ClassifyRawImplication(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula factFormula)
    {
        if (factFormula == null) throw new ArgumentNullException(nameof(factFormula));

        return Execute(service => service.ClassifyImplication(pathConditions, factFormula));
    }

    internal PurityProofResult ClassifyBranchReachability(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula branchCondition)
    {
        if (branchCondition == null) throw new ArgumentNullException(nameof(branchCondition));

        return Execute(service => service.Classify(new PurityProofQuery(
            pathConditions.ToArray(),
            new PurityHazard(PurityHazardKind.BranchReachability, branchCondition))));
    }

    internal PurityProofResult ClassifyPathFeasibility(IEnumerable<SmtFormula> pathConditions)
    {
        return Execute(service => service.ClassifyPathFeasibility(pathConditions));
    }

    private PurityProofResult Execute(Func<SmtAnalysisService, PurityProofResult> classify)
    {
        if (_smtAnalysis != null) return classify(_smtAnalysis);

        // The pipeline owns the only ad hoc solver fallback. SmtAnalysisService then applies
        // pre-normalization depth safety, normalization, syntactic classification, configurable
        // budgets, SMT execution, and raw result mapping in that order.
        using var fallback = new SmtAnalysisService(SmtAnalysisOptions.Default);
        return classify(fallback);
    }
}
