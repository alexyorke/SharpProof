using SharpProof.ProofCore.Purity;
using SharpProof.ProofCore.Smt;
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

    internal PurityProofResult ClassifyRawImplication(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula factFormula)
    {
        if (factFormula == null) throw new ArgumentNullException(nameof(factFormula));

        return Execute(service => service.ClassifyImplication(pathConditions, factFormula));
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
