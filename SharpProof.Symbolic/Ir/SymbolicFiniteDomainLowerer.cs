using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicFiniteDomainLowerer {
    internal static SymbolicLoweringResult<IReadOnlyList<ExpressionSyntax>> LowerElements(ExpressionSyntax expression) {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        switch (expression) {
            case ArrayCreationExpressionSyntax { Initializer: { } initializer }:
                return BoundElements(expression, [.. initializer.Expressions], "program_point.foreach_element_facts");
            case ImplicitArrayCreationExpressionSyntax { Initializer: { } initializer }:
                return BoundElements(expression, [.. initializer.Expressions], "program_point.foreach_element_facts");
            case CollectionExpressionSyntax collection:
                if (collection.Elements.Count == 0)
                    return UnsupportedElements(collection, "empty");
                var limit = SymbolicAnalysisLimitContext.Limits.MaxFiniteForeachElementFacts;
                if (collection.Elements.Count > limit) {
                    SymbolicAnalysisLimitContext.Record(
                        SymbolicAnalysisLimitKind.ForeachElementFacts,
                        limit,
                        collection.Elements.Count,
                        collection,
                        "program_point.foreach_collection_element_facts");
                    return UnsupportedElements(collection, "limit");
                }
                var builder = ImmutableArray.CreateBuilder<ExpressionSyntax>(collection.Elements.Count);
                foreach (var element in collection.Elements)
                    if (element is ExpressionElementSyntax expressionElement)
                        builder.Add(expressionElement.Expression);
                    else
                        return UnsupportedElements(element, "spread");
                return ExactElements(collection, builder.ToImmutable());
            default:
                return UnsupportedElements(expression, "source");
        }
    }
    private static SymbolicLoweringResult<IReadOnlyList<ExpressionSyntax>> BoundElements(
        SyntaxNode source,
        ImmutableArray<ExpressionSyntax> elements,
        string eventDetail) {
        if (elements.IsEmpty)
            return UnsupportedElements(source, "empty");

        var limit = SymbolicAnalysisLimitContext.Limits.MaxFiniteForeachElementFacts;
        if (elements.Length > limit) {
            SymbolicAnalysisLimitContext.Record(SymbolicAnalysisLimitKind.ForeachElementFacts, limit, elements.Length, source, eventDetail);
            return UnsupportedElements(source, "limit");
        }
        return ExactElements(source, elements);
    }
    private static SymbolicLoweringResult<IReadOnlyList<ExpressionSyntax>> ExactElements(
        SyntaxNode source,
        ImmutableArray<ExpressionSyntax> elements) =>
        SymbolicLoweringResult<IReadOnlyList<ExpressionSyntax>>.Exact(elements, Provenance(source, "elements"));

    private static SymbolicLoweringResult<IReadOnlyList<ExpressionSyntax>> UnsupportedElements(SyntaxNode source, string detail) =>
        SymbolicLoweringResult<IReadOnlyList<ExpressionSyntax>>.Unsupported(Provenance(source, detail));

    private static SymbolicLoweringProvenance Provenance(SyntaxNode source, string detail) =>
        new("finite-foreach-domain", source.Span, detail);
}
