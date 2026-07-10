using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
            !IsNullableType(SymbolicFactFactory.GetTrackedSymbolType(symbol)) ||
            !TryResolveCurrentNullableValueExpression(
                symbol,
                useNode,
                semanticModel,
                cancellationToken,
                out var currentValueExpression))
            return false;

        return IsMissingNullableValueExpression(currentValueExpression, semanticModel, cancellationToken);
    }

    private static bool TryResolveCurrentNullableValueExpression(
        ISymbol symbol,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax valueExpression)
    {
        valueExpression = null!;
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