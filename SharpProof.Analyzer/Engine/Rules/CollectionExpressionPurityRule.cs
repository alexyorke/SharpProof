using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal class CollectionExpressionPurityRule : PurityRuleBase<ICollectionExpressionOperation>
{
    protected override OperationKind Kind => OperationKind.CollectionExpression;

    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(
        ICollectionExpressionOperation collectionExpression, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        var targetType = collectionExpression.Type;

        if (targetType != null)
        {
            var targetTypeName = targetType.OriginalDefinition.ToDisplayString();

            var isFreshLocalArrayInitialization =
                targetType is IArrayTypeSymbol &&
                RuleAnalysisHelper.IsFreshLocalArrayInitialization(collectionExpression);

            if (!IsPureCollectionExpressionTargetType(targetType) &&
                !isFreshLocalArrayInitialization)
                return PurityAnalysisEngine.ImpureResult(
                    collectionExpression,
                    targetType is IArrayTypeSymbol ? "mutable_state_write" : "unsupported_operation",
                    nameof(CollectionExpressionPurityRule),
                    targetType,
                    "collection_expression_target");

            if (isFreshLocalArrayInitialization)
            {
            }
        }

        foreach (var element in collectionExpression.Elements)
        {
            if (element is null)
                continue;

            var elementResult = PurityAnalysisEngine.CheckSingleOperation(element, context, currentState);
            if (!elementResult.IsPure)
            {
                var node = elementResult.ImpureSyntaxNode ?? collectionExpression.Syntax;
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(node, elementResult.Evidence);
            }
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    /// <summary>
    ///     Types for which a collection expression is treated as constructing immutable / stack-only
    ///     data without hidden mutation (arrays, <see cref="List{T}" />, etc. remain impure targets).
    /// </summary>
    private static bool IsPureCollectionExpressionTargetType(ITypeSymbol type)
    {
        var def = type.OriginalDefinition;

        if (def.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Collections.Immutable")
            return true;

        if (def is INamedTypeSymbol named &&
            named.TypeArguments.Length == 1 &&
            named.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System" &&
            (named.Name == "ReadOnlySpan" || named.Name == "Span"))
            return true;

        return false;
    }
}
