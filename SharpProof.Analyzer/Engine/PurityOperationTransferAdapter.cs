using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using PurityAnalysisState = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PurityAnalysisState;

namespace SharpProof.Analyzer.Engine;

internal static class PurityOperationTransferAdapter
{
    internal static PurityAnalysisState ApplyAssignment(
        PurityAnalysisState state,
        ISymbol targetSymbol,
        IOperation valueOperation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        PurityAnalysisState valueState,
        out SymbolicOperationTransitionResult transition)
    {
        transition = SymbolicOperationTransferAdapter.ApplyAssignment(
            state.PathState,
            targetSymbol,
            valueOperation.Syntax,
            semanticModel,
            cancellationToken,
            state.GetSmtSymbolVersion,
            valueState.GetSmtSymbolVersion,
            provenance: "analyzer.assignment",
            bindingProvenance: "analyzer.assignment",
            evidenceKey: "analyzer.assignment.value");
        return transition.IsUnsupported
            ? state
            : state.WithPathState(transition.State);
    }

    internal static PurityAnalysisState ApplyAssignmentFacts(
        PurityAnalysisState state,
        ISymbol targetSymbol,
        IOperation? valueOperation,
        PurityAnalysisState valueState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nextState = valueOperation?.Syntax is ExpressionSyntax
            ? ApplyAssignment(
                state,
                targetSymbol,
                valueOperation,
                semanticModel,
                cancellationToken,
                valueState,
                out _)
            : state;
        var sourceSymbol = PurityAnalysisEngine.TryResolveTrackedSymbol(valueOperation, valueState);
        return sourceSymbol == null ||
               SymbolEqualityComparer.Default.Equals(sourceSymbol, targetSymbol) ||
               SymbolicFactFactory.GetTrackedSymbolType(sourceSymbol)?.IsReferenceType != true ||
               SymbolicFactFactory.GetTrackedSymbolType(targetSymbol)?.IsReferenceType != true
            ? nextState
            : ApplyReferenceRelationship(
                nextState,
                sourceSymbol,
                valueState,
                targetSymbol,
                SymbolicLifetimeOperationKind.Alias,
                valueOperation!.Syntax,
                "analyzer.assignment.alias",
                "evidence.assignment.alias");
    }

    internal static PurityAnalysisState ApplyDeclaredBorrow(
        PurityAnalysisState state,
        ILocalSymbol declaredSymbol,
        IOperation initializerValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isRefInitializer = initializerValue.Syntax.Parent is RefExpressionSyntax ||
                               initializerValue.Syntax.Ancestors().OfType<RefExpressionSyntax>().Any();
        if (!isRefInitializer &&
            declaredSymbol.RefKind is not (RefKind.Ref or RefKind.Out or RefKind.In or RefKind.RefReadOnly))
            return state;

        var sourceSymbol = PurityAnalysisEngine.TryResolveTrackedSymbol(initializerValue, state) ??
                           TryResolveRefInitializerSymbol(
                               initializerValue.Syntax,
                               semanticModel,
                               state,
                               cancellationToken);
        if (sourceSymbol == null) return state;

        return ApplyReferenceRelationship(
            state,
            sourceSymbol,
            state,
            declaredSymbol,
            declaredSymbol.RefKind is RefKind.In or RefKind.RefReadOnly
                ? SymbolicLifetimeOperationKind.BorrowShared
                : SymbolicLifetimeOperationKind.BorrowMutable,
            initializerValue.Syntax,
            "analyzer.declaration.borrow",
            "evidence.declaration.borrow");
    }

    private static PurityAnalysisState ApplyReferenceRelationship(
        PurityAnalysisState state,
        ISymbol sourceSymbol,
        PurityAnalysisState sourceState,
        ISymbol targetSymbol,
        SymbolicLifetimeOperationKind kind,
        SyntaxNode source,
        string provenance,
        string evidenceKey)
    {
        var transition = SymbolicOperationTransferKernel.TransitionLifetime(
            state.PathState,
            PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(sourceSymbol, sourceState),
            kind,
            source.Span,
            provenance,
            targetSymbol,
            evidenceKey,
            PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(targetSymbol, state),
            SymbolicEscapeKind.RefAlias);
        return state.WithPathState(transition.State);
    }

    internal static PurityAnalysisState ApplyLifetime(
        PurityAnalysisState state,
        SymbolicTerm subject,
        SymbolicLifetimeOperationKind kind,
        SyntaxNode source,
        string provenance,
        ISymbol? symbol,
        string? evidenceKey)
    {
        return state.WithPathState(SymbolicOperationTransferKernel.TransitionLifetime(
            state.PathState,
            subject,
            kind,
            source.Span,
            provenance,
            symbol,
            evidenceKey).State);
    }

    private static ISymbol? TryResolveRefInitializerSymbol(
        SyntaxNode initializerSyntax,
        SemanticModel semanticModel,
        PurityAnalysisState state,
        CancellationToken cancellationToken)
    {
        var refExpression = initializerSyntax.AncestorsAndSelf().OfType<RefExpressionSyntax>().FirstOrDefault();
        if (refExpression == null) return null;
        return semanticModel.GetOperation(refExpression.Expression, cancellationToken) is { } operation &&
               PurityAnalysisEngine.TryResolveTrackedSymbol(operation, state) is { } operationSymbol
            ? operationSymbol
            : semanticModel.GetSymbolInfo(refExpression.Expression, cancellationToken).Symbol;
    }
}
