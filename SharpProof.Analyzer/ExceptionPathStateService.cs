using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using static SharpProof.Analyzer.ExceptionFlowAnalyzer;

namespace SharpProof.Analyzer;

internal static partial class ExceptionPathStateService
{
    internal static SymbolicState CollectPathStateForUse(
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var initialState = RequiresEntryStateBuilder.CreateForUse(
            useNode,
            semanticModel,
            ActiveAttributePolicy,
            cancellationToken);
        return SymbolicReachabilityService.CollectPathStateAt(
            useNode,
            semanticModel,
            cancellationToken,
            initialState);
    }

    internal static bool IsExceptionPathReachable(
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return IsPathStateReachable(
            CollectPathStateForUse(useNode, semanticModel, cancellationToken),
            smtAnalysis);
    }

    internal static bool IsMethodCallCandidatePathReachable(
        MethodCallCandidate candidate,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var pathState = CollectPathStateForUse(candidate.CallSite, semanticModel, cancellationToken);

        if (candidate.UsingDisposeGuard?.ResourceExpression is { } disposeReceiver)
            pathState = SymbolicStateFactBuilder.AddReferenceNullCondition(
                pathState,
                disposeReceiver,
                false,
                semanticModel,
                cancellationToken,
                "analyzer.exception-flow.non-null");

        return IsPathStateReachable(pathState, smtAnalysis);
    }

    private static bool IsPathStateReachable(
        SymbolicState pathState,
        SmtAnalysisService smtAnalysis)
    {
        return SymbolicReachabilityService.ClassifyStateFeasibility(pathState, smtAnalysis).Info.Status !=
               SymbolicProofStatus.Unreachable;
    }

}
