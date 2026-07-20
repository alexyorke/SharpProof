using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic.Ir;

internal sealed record SymbolicForeachDomainPlan(
    ImmutableArray<SymbolicCondition> Conditions);

internal sealed record SymbolicFiniteElements(
    ImmutableArray<ExpressionSyntax> Expressions);

internal static class SymbolicFiniteDomainLowerer {
    internal static SymbolicLoweringResult<SymbolicFiniteElements> LowerElements(
        ExpressionSyntax expression) {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        switch (expression) {
            case ArrayCreationExpressionSyntax { Initializer: { } initializer }:
                return BoundElements(
                    expression,
                    initializer.Expressions.ToImmutableArray(),
                    "program_point.foreach_element_facts");
            case ImplicitArrayCreationExpressionSyntax { Initializer: { } initializer }:
                return BoundElements(
                    expression,
                    initializer.Expressions.ToImmutableArray(),
                    "program_point.foreach_element_facts");
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

    internal static SymbolicLoweringResult<SymbolicForeachDomainPlan> LowerForeachDomain(
        ExpressionSyntax expression,
        StatementSyntax foreachStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (foreachStatement is not ForEachStatementSyntax forEach ||
            semanticModel.GetDeclaredSymbol(forEach, cancellationToken) is not ILocalSymbol iterationSymbol ||
            !TryCreateSymbolTerm(iterationSymbol.OriginalDefinition, out var iterationTerm))
            return Unsupported(foreachStatement, "iteration");

        var elementLowering = LowerElements(expression);
        if (!elementLowering.IsExact)
            elementLowering = LowerPriorAssignedElements(
                expression,
                foreachStatement,
                semanticModel,
                cancellationToken);
        if (elementLowering is not { IsExact: true, Value: { } finiteElements })
            return Unsupported(expression, "elements");

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var conditions = ImmutableArray.CreateBuilder<SymbolicCondition>(2);
        SymbolicCondition? finiteDomain = null;
        var allReferencesNonNull =
            SymbolicFactFactory.GetTrackedSymbolType(iterationSymbol.OriginalDefinition)?.IsReferenceType == true;
        foreach (var element in finiteElements.Expressions) {
            if (SymbolMutationFacts.ExpressionReferencesSymbol(
                    element,
                    iterationSymbol.OriginalDefinition,
                    semanticModel,
                    cancellationToken))
                return Unsupported(element, "self-reference");

            var lowering = SymbolicSemanticPipeline.LowerTerm(element, context);
            if (lowering is { IsExact: true, Value: { } elementTerm } &&
                CanCompareIrTerms(iterationTerm, elementTerm)) {
                var equality = SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.Equal,
                    iterationTerm,
                    elementTerm,
                    element,
                    "ir.path.foreach-entry.finite-domain");
                finiteDomain = finiteDomain == null
                    ? equality
                    : new SymbolicBinaryCondition(
                        SymbolicConditionOperator.Or,
                        finiteDomain,
                        equality);
            }
            else if (!allReferencesNonNull) {
                return Unsupported(element, "element");
            }

            allReferencesNonNull = allReferencesNonNull &&
                NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                    element,
                    semanticModel,
                    cancellationToken);
        }

        if (finiteDomain != null)
            conditions.Add(finiteDomain);
        if (allReferencesNonNull &&
            iterationTerm.Kind == SharpProof.ProofCore.Smt.SmtValueKind.Reference)
            conditions.Add(SymbolicIrLowerer.CreateRelationCondition(
                SymbolicRelationOperator.NotEqual,
                iterationTerm,
                new SymbolicNullTerm(),
                foreachStatement,
                "ir.path.foreach-entry.finite-domain-not-null"));

        return conditions.Count == 0
            ? Unsupported(foreachStatement, "empty")
            : SymbolicLoweringResult<SymbolicForeachDomainPlan>.Exact(
                new SymbolicForeachDomainPlan(conditions.ToImmutable()),
                Provenance(foreachStatement, "exact"));
    }

    private static SymbolicLoweringResult<SymbolicFiniteElements> LowerPriorAssignedElements(
        ExpressionSyntax expression,
        StatementSyntax foreachStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (foreachStatement.Parent is not BlockSyntax containingBlock ||
            semanticModel.GetSymbolInfo(
                CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression),
                cancellationToken).Symbol?.OriginalDefinition is not { } receiverSymbol ||
            receiverSymbol is not ILocalSymbol and not IParameterSymbol)
            return UnsupportedElements(expression, "receiver");

        for (var index = containingBlock.Statements.Count - 1; index >= 0; index--) {
            var statement = containingBlock.Statements[index];
            if (statement.SpanStart >= foreachStatement.SpanStart)
                continue;

            if (TryGetAssignedValue(
                    statement,
                    receiverSymbol,
                    semanticModel,
                    cancellationToken,
                    out var assignedValue)) {
                var lowering = LowerElements(assignedValue);
                if (lowering is not { IsExact: true, Value: { } elements } ||
                    AnyStatementInvalidates(
                        containingBlock,
                        index + 1,
                        foreachStatement.SpanStart,
                        receiverSymbol,
                        semanticModel,
                        cancellationToken) ||
                    AnyReferencedSymbolInvalidated(
                        elements.Expressions,
                        containingBlock,
                        index + 1,
                        foreachStatement.SpanStart,
                        receiverSymbol,
                        semanticModel,
                        cancellationToken))
                    return UnsupportedElements(assignedValue, "invalidated");

                return lowering;
            }

            if (Invalidates(statement, receiverSymbol, semanticModel, cancellationToken))
                return UnsupportedElements(statement, "invalidated");
        }

        return UnsupportedElements(expression, "assignment");
    }

    private static bool AnyStatementInvalidates(
        BlockSyntax block,
        int firstIndex,
        int beforeSpanStart,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        for (var index = firstIndex; index < block.Statements.Count; index++) {
            var statement = block.Statements[index];
            if (statement.SpanStart >= beforeSpanStart)
                break;
            if (Invalidates(statement, symbol, semanticModel, cancellationToken))
                return true;
        }

        return false;
    }

    private static bool AnyReferencedSymbolInvalidated(
        ImmutableArray<ExpressionSyntax> elements,
        BlockSyntax block,
        int firstIndex,
        int beforeSpanStart,
        ISymbol receiverSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var referencedSymbols = ImmutableArray.CreateBuilder<ISymbol>();
        foreach (var element in elements)
            foreach (var symbol in SymbolMutationFacts.GetReferencedLocalAndParameterSymbols(
                         element,
                         semanticModel,
                         cancellationToken)) {
                if (SymbolEqualityComparer.Default.Equals(symbol, receiverSymbol))
                    return true;
                if (referencedSymbols.All(candidate =>
                        !SymbolEqualityComparer.Default.Equals(candidate, symbol)))
                    referencedSymbols.Add(symbol);
            }

        return referencedSymbols.Any(symbol => AnyStatementInvalidates(
            block,
            firstIndex,
            beforeSpanStart,
            symbol,
            semanticModel,
            cancellationToken));
    }

    private static bool TryGetAssignedValue(
        StatementSyntax statement,
        ISymbol receiverSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax value) {
        if (statement is LocalDeclarationStatementSyntax localDeclaration)
            foreach (var declarator in localDeclaration.Declaration.Variables)
                if (declarator.Initializer is { } initializer &&
                    semanticModel.GetDeclaredSymbol(declarator, cancellationToken)?.OriginalDefinition is { } declaredSymbol &&
                    SymbolEqualityComparer.Default.Equals(declaredSymbol, receiverSymbol)) {
                    value = initializer.Value;
                    return true;
                }

        if (statement is ExpressionStatementSyntax {
            Expression: AssignmentExpressionSyntax assignment
        } &&
            assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
            semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol?.OriginalDefinition is { } assignedSymbol &&
            SymbolEqualityComparer.Default.Equals(assignedSymbol, receiverSymbol)) {
            value = assignment.Right;
            return true;
        }

        value = null!;
        return false;
    }

    private static bool Invalidates(
        StatementSyntax statement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        SymbolicProgramPointFacts.StatementInvalidatesSymbolValue(
            statement,
            symbol,
            semanticModel,
            cancellationToken);

    private static SymbolicLoweringResult<SymbolicFiniteElements> BoundElements(
        SyntaxNode source,
        ImmutableArray<ExpressionSyntax> elements,
        string eventDetail) {
        if (elements.IsEmpty)
            return UnsupportedElements(source, "empty");

        var limit = SymbolicAnalysisLimitContext.Limits.MaxFiniteForeachElementFacts;
        if (elements.Length > limit) {
            SymbolicAnalysisLimitContext.Record(
                SymbolicAnalysisLimitKind.ForeachElementFacts,
                limit,
                elements.Length,
                source,
                eventDetail);
            return UnsupportedElements(source, "limit");
        }

        return ExactElements(source, elements);
    }

    private static SymbolicLoweringResult<SymbolicFiniteElements> ExactElements(
        SyntaxNode source,
        ImmutableArray<ExpressionSyntax> elements) =>
        SymbolicLoweringResult<SymbolicFiniteElements>.Exact(
            new SymbolicFiniteElements(elements),
            Provenance(source, "elements"));

    private static SymbolicLoweringResult<SymbolicFiniteElements> UnsupportedElements(
        SyntaxNode source,
        string detail) =>
        SymbolicLoweringResult<SymbolicFiniteElements>.Unsupported(Provenance(source, detail));

    private static SymbolicLoweringResult<SymbolicForeachDomainPlan> Unsupported(
        SyntaxNode source,
        string detail) =>
        SymbolicLoweringResult<SymbolicForeachDomainPlan>.Unsupported(Provenance(source, detail));

    private static SymbolicLoweringProvenance Provenance(SyntaxNode source, string detail) =>
        new("finite-foreach-domain", source.Span, detail);
}
