namespace SharpProof.Analyzer.Engine.Rules;

internal partial class AssignmentPurityRule : IPurityRule
{
    private static PurityAnalysisEngine.PurityAnalysisResult CheckCompoundAssignmentOperatorPurity(
        IMethodSymbol operatorMethod,
        IOperation operation,
        PurityAnalysisContext context)
    {
        return PurityCalleeResolver.GetCalleePurityAtUse(operatorMethod, operation.Syntax, context);
    }

    private static bool TryCreateMutableBorrowConflictEvidence(
        IOperation operation,
        ISymbol? targetSymbol,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityEvidence evidence)
    {
        return PuritySymbolicStateFacts.TryCreateMutableBorrowConflictEvidence(
            operation,
            targetSymbol,
            currentState,
            context.SemanticModel,
            context.CancellationToken,
            nameof(AssignmentPurityRule),
            out evidence);
    }
}
