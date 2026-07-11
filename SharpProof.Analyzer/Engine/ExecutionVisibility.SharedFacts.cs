using Microsoft.CodeAnalysis;
using SearchLib.Smt;
using SharpProof.Symbolic;
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

        var pathConditions = SymbolicReachabilityService.CollectPathConditionsAt(
            syntaxNode,
            semanticModel,
            cancellationToken);
        return pathConditions.Count > 0 &&
               ArePathConditionsUnsatisfiableAt(pathConditions, syntaxNode, smtAnalysis);
    }

    private static bool ArePathConditionsUnsatisfiableAt(
        IReadOnlyCollection<SmtFormula> pathConditions,
        SyntaxNode site,
        SmtAnalysisService? smtAnalysis)
    {
        return SymbolicReachabilityService.IsUnsatisfiable(
            pathConditions,
            smtAnalysis);
    }
}
