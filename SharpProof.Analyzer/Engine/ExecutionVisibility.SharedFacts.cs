namespace SharpProof.Analyzer.Engine;

internal static partial class ExecutionVisibility {
    private static bool IsProgramPointUnreachableUsingSharedFacts(
        SyntaxNode syntaxNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis) {
        if (IsInReachableConstantSwitchGotoSection(syntaxNode, semanticModel, cancellationToken)) return false;

        var pathState = SymbolicReachabilityService.CollectPathStateAt(
            syntaxNode,
            semanticModel,
            cancellationToken);
        return new SymbolicProofService(smtAnalysis).ClassifyReachability(pathState).Status ==
               SymbolicProofStatus.Unreachable;
    }
}
