using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using static SharpProof.Analyzer.ExceptionFlowAnalyzer;

namespace SharpProof.Analyzer;

internal static partial class ExceptionPathStateService
{
    internal static bool IsKnownByDominatingIf(
        ExpressionSyntax expression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        PathFactKind factKind,
        SmtAnalysisService smtAnalysis)
    {
        if (!TryCreatePathFactCondition(
                expression,
                factKind,
                semanticModel,
                cancellationToken,
                out var factCondition))
            return false;

        var pathState = CollectPathStateForUse(useNode, semanticModel, cancellationToken);
        return SymbolicReachabilityService.ClassifyStateConditionTruth(pathState, factCondition, smtAnalysis)
                   .Info.Status == SymbolicProofStatus.ProvenTrue;
    }

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

    internal static SymbolicState CollectExceptionSitePathState(
        SyntaxNode exceptionSite,
        SyntaxNode? relevantRoot,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return CollectPathStateForUse(exceptionSite, semanticModel, cancellationToken);
    }

    private static bool IsPathStateReachable(
        SymbolicState pathState,
        SmtAnalysisService smtAnalysis)
    {
        return SymbolicReachabilityService.ClassifyStateFeasibility(pathState, smtAnalysis).Info.Status !=
               SymbolicProofStatus.Unreachable;
    }

    private static bool TryCreatePathFactCondition(
        ExpressionSyntax expression,
        PathFactKind factKind,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition condition)
    {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        if (factKind == PathFactKind.Zero)
        {
            var zero = SymbolicSemanticPipeline.LowerNumericZeroCondition(expression, context);
            if (zero is { IsExact: true, Value: { } zeroCondition })
            {
                condition = zeroCondition;
                return true;
            }

            condition = null!;
            return false;
        }

        return SymbolicStateFactBuilder.TryCreateReferenceNullCondition(
            expression,
            true,
            semanticModel,
            cancellationToken,
            "analyzer.exception-flow.null",
            out condition);
    }
}
