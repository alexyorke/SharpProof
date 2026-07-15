using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal static class SymbolicStateInvalidator
{
    internal static void InvalidateNestedAssignmentMutations(
        ref SymbolicState state,
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        InvalidateNestedMutations(ref state, assignment.Left, semanticModel, cancellationToken);
        InvalidateNestedMutations(ref state, assignment.Right, semanticModel, cancellationToken);
    }

    internal static void InvalidateNestedMutations(
        ref SymbolicState state,
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in root.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
        {
            if (SymbolMutationFacts.TryGetMutationTarget(node, out var mutatedExpression))
                InvalidateMutationTarget(
                    ref state,
                    mutatedExpression,
                    semanticModel,
                    cancellationToken);

            foreach (var receiverSymbol in GetPotentiallyMutatedArraySymbols(node, semanticModel, cancellationToken))
                InvalidateSymbol(ref state, receiverSymbol, node);
        }
    }

    internal static void InvalidateMutationTarget(
        ref SymbolicState state,
        ExpressionSyntax mutatedExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var invalidations = ImmutableArray.CreateBuilder<SymbolicInvalidationTarget>();
        var mutatedSymbol = GetMutatedSymbol(mutatedExpression, semanticModel, cancellationToken);
        if (mutatedSymbol is ILocalSymbol or IParameterSymbol)
            invalidations.Add(ForSymbol(mutatedSymbol));
        else if (mutatedSymbol is IFieldSymbol or IPropertySymbol &&
                 IsCurrentInstanceMemberReference(mutatedExpression, semanticModel, cancellationToken))
            invalidations.Add(new SymbolicInvalidationTarget(
                SymbolicStateValueFacts.ImplicitThisVariableName + "." + mutatedSymbol.Name,
                SymbolicInvalidationMatchKind.VariableOrMember));

        foreach (var receiverSymbol in GetMutatedReceiverSymbols(mutatedExpression, semanticModel, cancellationToken))
            invalidations.Add(ForSymbol(receiverSymbol));

        if (invalidations.Count != 0)
            state = SymbolicOperationTransferKernel.Invalidate(
                state,
                invalidations.ToImmutable(),
                mutatedExpression.Span,
                "operation-transfer.mutation-invalidation").State;
    }

    internal static void InvalidateSymbol(ref SymbolicState state, ISymbol symbol, SyntaxNode source)
    {
        state = SymbolicOperationTransferKernel.Invalidate(
            state,
            ImmutableArray.Create(ForSymbol(symbol)),
            source.Span,
            "operation-transfer.reference-invalidation").State;
    }

    private static SymbolicInvalidationTarget ForSymbol(ISymbol symbol)
    {
        return new SymbolicInvalidationTarget(
            SymbolicFactFactory.GetSmtVariableName(symbol.OriginalDefinition));
    }

    private static ISymbol? GetMutatedSymbol(
        ExpressionSyntax mutatedExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(mutatedExpression, cancellationToken).Symbol;
        if (symbol != null) return NormalizeMutatedSymbol(symbol);

        return semanticModel.GetOperation(mutatedExpression, cancellationToken) switch
        {
            IFieldReferenceOperation fieldReference => fieldReference.Field,
            IPropertyReferenceOperation propertyReference => propertyReference.Property,
            _ => null
        };
    }

    internal static ISymbol NormalizeMutatedSymbol(ISymbol symbol)
    {
        return symbol is IMethodSymbol { AssociatedSymbol: IPropertySymbol property }
            ? property
            : symbol;
    }

    internal static bool IsCurrentInstanceMemberReference(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (expression is IdentifierNameSyntax &&
            GetMutatedSymbol(expression, semanticModel, cancellationToken) is { IsStatic: false }
                and (IFieldSymbol or IPropertySymbol))
            return true;

        return expression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax };
    }

    private static IEnumerable<ISymbol> GetMutatedReceiverSymbols(
        ExpressionSyntax mutatedExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var receiverExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(mutatedExpression) switch
        {
            ElementAccessExpressionSyntax elementAccess => elementAccess.Expression,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            _ => null
        };

        if (receiverExpression == null) yield break;

        var receiverSymbol = semanticModel.GetSymbolInfo(CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(receiverExpression), cancellationToken).Symbol
            ?.OriginalDefinition;
        if (receiverSymbol is ILocalSymbol or IParameterSymbol) yield return receiverSymbol;
    }

    private static IEnumerable<ISymbol> GetPotentiallyMutatedArraySymbols(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (node)
        {
            case InvocationExpressionSyntax invocation:
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                    foreach (var symbol in GetReferencedArraySymbols(memberAccess.Expression, semanticModel,
                                 cancellationToken))
                        yield return symbol;

                foreach (var argument in invocation.ArgumentList.Arguments)
                    foreach (var symbol in GetReferencedArraySymbols(argument.Expression, semanticModel, cancellationToken))
                        yield return symbol;

                break;
            case ObjectCreationExpressionSyntax { ArgumentList: { } argumentList }:
                foreach (var argument in argumentList.Arguments)
                    foreach (var symbol in GetReferencedArraySymbols(argument.Expression, semanticModel, cancellationToken))
                        yield return symbol;

                break;
        }
    }

    private static IEnumerable<ISymbol> GetReferencedArraySymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in SymbolMutationFacts.GetReferencedLocalAndParameterSymbols(
                     root,
                     semanticModel,
                     cancellationToken))
            if (SymbolicFactFactory.GetTrackedSymbolType(symbol) is IArrayTypeSymbol)
                yield return symbol;
    }

    internal static bool MayMutateThroughReference(
        StatementSyntax statement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!IsPotentiallyMutableThroughReference(SymbolicFactFactory.GetTrackedSymbolType(symbol))) return false;

        foreach (var node in statement.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
            if (NodeMayMutateThroughReference(node, symbol, semanticModel, cancellationToken))
                return true;

        return false;
    }

    internal static bool NodeMayMutateThroughReference(
        SyntaxNode node,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (node)
        {
            case InvocationExpressionSyntax invocation:
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                    SymbolMutationFacts.ExpressionReferencesSymbol(memberAccess.Expression, symbol, semanticModel, cancellationToken))
                    return true;

                return invocation.ArgumentList.Arguments.Any(argument =>
                    SymbolMutationFacts.ExpressionReferencesSymbol(argument.Expression, symbol, semanticModel, cancellationToken));
            case ObjectCreationExpressionSyntax { ArgumentList: { } argumentList }:
                return argumentList.Arguments.Any(argument =>
                    SymbolMutationFacts.ExpressionReferencesSymbol(argument.Expression, symbol, semanticModel, cancellationToken));
            default:
                return false;
        }
    }

    private static bool IsPotentiallyMutableThroughReference(ITypeSymbol? type)
    {
        return type is IArrayTypeSymbol ||
               (type?.IsReferenceType == true &&
                type.SpecialType != SpecialType.System_String);
    }
}
