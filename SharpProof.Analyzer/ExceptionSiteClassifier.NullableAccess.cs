using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

using static SharpProof.Analyzer.ExceptionFlowAnalyzer;

namespace SharpProof.Analyzer;

internal static partial class ExceptionSiteClassifier
{
    private static bool IsKnownMissingNullableValueByPriorAssignment(
        ExpressionSyntax nullableExpression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        nullableExpression = UnwrapFactExpression(nullableExpression);
        if (IsMissingNullableValueExpression(nullableExpression, semanticModel, cancellationToken)) return true;

        if (!SymbolMutationFacts.TryGetLocalOrParameterSymbol(
                nullableExpression,
                semanticModel,
                cancellationToken,
                out var symbol) ||
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
        var loopBody = CSharpSyntaxFacts.GetContainingLoopBody(useNode);
        if (loopBody == null) return false;

        return CSharpSyntaxFacts.DescendantNodesInExecution(loopBody, includeSelf: false)
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                assignment.SpanStart > useNode.SpanStart &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                SymbolMutationFacts.ExpressionMatchesSymbol(
                    assignment.Left,
                    symbol,
                    semanticModel,
                    cancellationToken) &&
                IsMissingNullableValueExpression(assignment.Right, semanticModel, cancellationToken));
    }

    private static bool TryResolveCurrentNullableValueExpression(
        ISymbol symbol,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax valueExpression)
    {
        return SymbolCurrentValueResolver.TryResolveCurrentSimpleValueExpression(
            symbol,
            useNode,
            semanticModel,
            cancellationToken,
            out valueExpression);
    }

    private static bool IsMissingNullableValueExpression(
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        valueExpression = UnwrapFactExpression(valueExpression);
        var expressionType = CSharpSyntaxFacts.GetExpressionType(valueExpression, semanticModel, cancellationToken);
        if (!IsNullableType(expressionType)) return false;

        if (semanticModel.GetConstantValue(valueExpression, cancellationToken) is
            { HasValue: true, Value: null }) return true;

        if (IsDefaultExpressionSyntax(valueExpression)) return true;

        return valueExpression is ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 } ||
               valueExpression is ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 };
    }
}
