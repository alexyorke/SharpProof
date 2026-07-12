using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static bool IsKnownByDominatingIf(
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

    private static SymbolicState CollectPathStateForUse(
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var initialState = CreateMethodEntryRequiresState(useNode, semanticModel, cancellationToken);
        return SymbolicReachabilityService.CollectPathStateAt(
            useNode,
            semanticModel,
            cancellationToken,
            initialState);
    }

    private static SymbolicState CreateMethodEntryRequiresState(
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var state = new SymbolicState();
        var methodNode = useNode
            .AncestorsAndSelf()
            .FirstOrDefault(IsMethodLikeDeclaration);
        if (methodNode == null ||
            semanticModel.GetDeclaredSymbol(methodNode, cancellationToken) is not IMethodSymbol methodSymbol)
            return state;

        var contracts = RequiresContractHelpers.ValidContracts(
            methodSymbol,
            ActiveAttributePolicy,
            cancellationToken);
        if (contracts.Length == 0) return state;

        var position = RequiresContractHelpers.GetMethodEntrySpeculativePosition(methodNode);
        foreach (var contract in contracts)
        {
            if (!RequiresContractHelpers.TryCreateCondition(
                    semanticModel,
                    position,
                    contract.Condition,
                    cancellationToken,
                    out var conditionExpression,
                    out _,
                    out var condition,
                    out _) ||
                RequiresContractHelpers.ContainsResultReference(conditionExpression))
                continue;

            state = state.AddPathCondition(condition);
        }

        return state;
    }

    private static bool IsMethodLikeDeclaration(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax ||
               node is AccessorDeclarationSyntax ||
               node is ConstructorDeclarationSyntax ||
               node is ConversionOperatorDeclarationSyntax ||
               node is OperatorDeclarationSyntax ||
               node is LocalFunctionStatementSyntax;
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
            TryAddReferenceNullCondition(
                ref pathState,
                disposeReceiver,
                false,
                semanticModel,
                cancellationToken);

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
        var lowering = factKind == PathFactKind.Null
            ? SymbolicSemanticPipeline.LowerReferenceTerm(expression, context)
            : SymbolicSemanticPipeline.LowerTerm(expression, context);
        if (lowering is not { IsExact: true, Value: { } term } ||
            (factKind == PathFactKind.Zero && term.Kind != SearchLib.Smt.SmtValueKind.Int))
        {
            condition = null!;
            return false;
        }

        condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                term,
                factKind == PathFactKind.Null
                    ? new SymbolicNullTerm()
                    : new SymbolicIntegerConstantTerm(0)),
            expression,
            factKind == PathFactKind.Null
                ? "analyzer.exception-flow.null"
                : "analyzer.exception-flow.zero"));
        return true;
    }

    private static void TryAddReferenceNullCondition(
        ref SymbolicState pathState,
        ExpressionSyntax expression,
        bool equalToNull,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var lowering = SymbolicSemanticPipeline.LowerReferenceTerm(
            expression,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } reference }) return;

        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                equalToNull ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
                reference,
                new SymbolicNullTerm()),
            expression,
            equalToNull
                ? "analyzer.exception-flow.null"
                : "analyzer.exception-flow.non-null");
        pathState = pathState.AddPathCondition(new SymbolicFactCondition(fact));
    }
}
