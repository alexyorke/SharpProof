using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic;

internal static class SymbolCurrentValueResolver
{
    internal static bool TryResolveCurrentSimpleValueExpression(
        ExpressionSyntax expression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax valueExpression)
    {
        valueExpression = null!;
        if (!SymbolMutationFacts.TryGetLocalOrParameterSymbol(
                expression,
                semanticModel,
                cancellationToken,
                out var symbol))
            return false;

        return TryResolveCurrentSimpleValueExpression(
            symbol,
            useNode,
            semanticModel,
            cancellationToken,
            out valueExpression);
    }

    internal static bool TryResolveCurrentSimpleValueExpression(
        ISymbol symbol,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax valueExpression)
    {
        valueExpression = null!;
        if (IsMutatedAfterUseInContainingLoop(symbol, useNode, semanticModel, cancellationToken))
            return false;

        ExpressionSyntax? currentValue = null;
        foreach (var (block, containingStatement) in CSharpSyntaxFacts.EnumerateContainingBlocks(useNode).Reverse())
            foreach (var statement in block.Statements)
            {
                if (ReferenceEquals(statement, containingStatement)) break;

                if (statement is LocalDeclarationStatementSyntax localDeclaration)
                {
                    foreach (var declarator in localDeclaration.Declaration.Variables)
                        if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol &&
                            SymbolEqualityComparer.Default.Equals(localSymbol.OriginalDefinition, symbol))
                            currentValue = declarator.Initializer?.Value;

                    if (SymbolMutationFacts.ContainsMutation(
                            statement,
                            symbol,
                            semanticModel,
                            cancellationToken))
                        currentValue = null;

                    continue;
                }

                if (statement is ExpressionStatementSyntax
                    {
                        Expression: AssignmentExpressionSyntax assignment
                    } &&
                    SymbolMutationFacts.ExpressionMatchesSymbol(
                        assignment.Left,
                        symbol,
                        semanticModel,
                        cancellationToken))
                {
                    if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                        SymbolMutationFacts.ExpressionReferencesSymbol(
                            assignment.Right,
                            symbol,
                            semanticModel,
                            cancellationToken))
                    {
                        currentValue = null;
                        continue;
                    }

                    currentValue = assignment.Right;
                    continue;
                }

                if (SymbolMutationFacts.ContainsMutation(
                        statement,
                        symbol,
                        semanticModel,
                        cancellationToken))
                    currentValue = null;
            }

        if (currentValue == null) return false;

        valueExpression = currentValue;
        return true;
    }

    private static bool IsMutatedAfterUseInContainingLoop(
        ISymbol symbol,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var loopBody = CSharpSyntaxFacts.GetContainingLoopBody(useNode);
        if (loopBody == null) return false;

        return CSharpSyntaxFacts.DescendantNodesInExecution(loopBody)
            .Any(candidate => candidate.SpanStart > useNode.SpanStart &&
                              SymbolMutationFacts.MutatesSymbol(
                                  candidate,
                                  symbol,
                                  semanticModel,
                                  cancellationToken));
    }
}
