using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static bool IsKnownMissingNullableValueByPriorAssignment(
        ExpressionSyntax nullableExpression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        nullableExpression = UnwrapFactExpression(nullableExpression);
        if (IsMissingNullableValueExpression(nullableExpression, semanticModel, cancellationToken)) return true;

        var symbol = GetLocalOrParameterSymbol(nullableExpression, semanticModel, cancellationToken);
        if (symbol == null ||
            !IsNullableType(SymbolicFactFactory.GetTrackedSymbolType(symbol)))
            return false;

        if (HasLaterLoopAssignmentOfMissingNullableValue(
                symbol,
                useNode,
                semanticModel,
                cancellationToken))
            return true;

        if (
            !TryResolveCurrentNullableValueExpression(
                symbol,
                useNode,
                semanticModel,
                cancellationToken,
                out var currentValueExpression))
            return false;

        return IsMissingNullableValueExpression(currentValueExpression, semanticModel, cancellationToken);
    }

    private static bool HasLaterLoopAssignmentOfMissingNullableValue(
        ISymbol symbol,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var loopBody = GetContainingLoopBody(useNode);
        if (loopBody == null) return false;

        return loopBody.DescendantNodes(candidate =>
                !ExecutionVisibility.IsNestedCallableBoundary(candidate))
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                assignment.SpanStart > useNode.SpanStart &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken) &&
                IsMissingNullableValueExpression(assignment.Right, semanticModel, cancellationToken));
    }

    private static bool TryResolveCurrentNullableValueExpression(
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
        foreach (var (block, containingStatement) in EnumerateContainingBlocks(useNode).Reverse())
            foreach (var statement in block.Statements)
            {
                if (ReferenceEquals(statement, containingStatement)) break;

                if (statement is LocalDeclarationStatementSyntax localDeclaration)
                {
                    foreach (var declarator in localDeclaration.Declaration.Variables)
                        if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol &&
                            SymbolEqualityComparer.Default.Equals(localSymbol.OriginalDefinition, symbol))
                            currentValue = declarator.Initializer?.Value;

                    if (StatementMutatesSymbolExceptLinearAssignment(statement, symbol, semanticModel, cancellationToken))
                        currentValue = null;

                    continue;
                }

                if (statement is ExpressionStatementSyntax
                    {
                        Expression: AssignmentExpressionSyntax assignment
                    } &&
                    ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken))
                {
                    if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                        ExpressionReferencesSymbol(assignment.Right, symbol, semanticModel, cancellationToken))
                    {
                        currentValue = null;
                        continue;
                    }

                    currentValue = assignment.Right;
                    continue;
                }

                if (StatementMutatesSymbolExceptLinearAssignment(statement, symbol, semanticModel, cancellationToken))
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
        var loopBody = GetContainingLoopBody(useNode);
        if (loopBody == null) return false;

        return loopBody.DescendantNodesAndSelf(candidate =>
                !ExecutionVisibility.IsNestedCallableBoundary(candidate))
            .Any(candidate => candidate.SpanStart > useNode.SpanStart &&
                              MutatesSymbol(candidate, symbol, semanticModel, cancellationToken));
    }

    private static StatementSyntax? GetContainingLoopBody(SyntaxNode useNode)
    {
        return useNode.Ancestors().Select(static ancestor => ancestor switch
            {
                WhileStatementSyntax whileStatement => whileStatement.Statement,
                DoStatementSyntax doStatement => doStatement.Statement,
                ForStatementSyntax forStatement => forStatement.Statement,
                ForEachStatementSyntax forEachStatement => forEachStatement.Statement,
                ForEachVariableStatementSyntax forEachVariable => forEachVariable.Statement,
                _ => null
            })
            .FirstOrDefault(body => body?.Span.Contains(useNode.SpanStart) == true);
    }

    private static bool IsMissingNullableValueExpression(
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        valueExpression = UnwrapFactExpression(valueExpression);
        var expressionType = GetExpressionType(valueExpression, semanticModel, cancellationToken);
        if (!IsNullableType(expressionType)) return false;

        if (semanticModel.GetConstantValue(valueExpression, cancellationToken) is
            { HasValue: true, Value: null }) return true;

        if (IsDefaultExpressionSyntax(valueExpression)) return true;

        return valueExpression is ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 } ||
               valueExpression is ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 };
    }
}
