using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine;

internal static partial class ExecutionVisibility
{
    private static bool IsProgramPointUnreachableUsingSharedFacts(
        SyntaxNode syntaxNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        if (IsInReachableConstantSwitchGotoSection(syntaxNode, semanticModel, cancellationToken)) return false;

        var pathState = SymbolicReachabilityService.CollectPathStateAt(
            syntaxNode,
            semanticModel,
            cancellationToken);
        return SymbolicReachabilityService.ClassifyStateFeasibility(pathState, smtAnalysis).Info.Status ==
               SymbolicProofStatus.Unreachable;
    }
}
