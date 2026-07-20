namespace SharpProof.Analyzer;

internal readonly record struct MethodNormalCompletion(
    ExpressionSyntax? ResultExpression,
    Location Location,
    SyntaxNode QueryNode,
    bool IncludeCurrentStatementCompletionFacts,
    string DisplayText);

internal static class MethodCompletionAnalysis
{
    internal static ImmutableArray<MethodNormalCompletion> Collect(
        MethodBodyAnalysisContext context,
        bool distinctByQueryPosition = false)
    {
        if (context.Snapshot.RootOperation == null) return ImmutableArray<MethodNormalCompletion>.Empty;
        var builder = ImmutableArray.CreateBuilder<MethodNormalCompletion>();
        foreach (var operation in context.Snapshot.VisibleOperations)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (operation is not IReturnOperation returnOperation ||
                AnalyzerSyntaxHelpers.IsCompilerMarkedUnreachable(
                    operation.Syntax,
                    context.SemanticModel,
                    context.CancellationToken))
                continue;

            var expression = returnOperation.ReturnedValue?.Syntax as ExpressionSyntax;
            builder.Add(new MethodNormalCompletion(
                expression,
                expression?.GetLocation() ?? operation.Syntax.GetLocation(),
                operation.Syntax,
                false,
                expression?.ToString() ?? "return"));
        }

        if (CSharpSyntaxFacts.TryGetExpressionBody(context.Node, out var expressionBody))
        {
            var hasResultValue = AnalyzerSyntaxHelpers.HasResultValue(context.MethodSymbol);
            builder.Add(new MethodNormalCompletion(
                hasResultValue ? expressionBody : null,
                expressionBody.GetLocation(),
                expressionBody,
                !hasResultValue,
                hasResultValue ? expressionBody.ToString() : "normal completion"));
        }
        else if (CSharpSyntaxFacts.GetBlockBody(context.Node) is { } body &&
                 AnalyzerSyntaxHelpers.BodyEndPointIsReachable(body, context.SemanticModel))
            builder.Add(new MethodNormalCompletion(
                null,
                body.CloseBraceToken.GetLocation(),
                body,
                true,
                "normal completion"));

        return distinctByQueryPosition
            ? builder
                .GroupBy(static completion => completion.QueryNode.SpanStart)
                .Select(static group => group.First())
                .ToImmutableArray()
            : builder.ToImmutable();
    }

    internal static SymbolicConditionProofResult Prove(
        MethodBodyAnalysisContext context,
        SmtAnalysisService smtAnalysis,
        MethodNormalCompletion completion,
        string condition)
    {
        if (MethodEnsuresAnalyzer.TryCreateEntrySnapshotProofCondition(
                condition,
                context.MethodSymbol,
                context.SemanticModel,
                completion.QueryNode.SpanStart,
                context.CancellationToken,
                out var symbolicCondition,
                out var initialState,
                out var snapshotFailureReason))
            return context.State.ProveAtNode(
                completion.QueryNode,
                condition,
                symbolicCondition,
                initialState,
                smtAnalysis,
                completion.IncludeCurrentStatementCompletionFacts,
                context.CancellationToken);

        return snapshotFailureReason == null
            ? context.State.ProveAtNode(
                completion.QueryNode,
                condition,
                smtAnalysis,
                completion.IncludeCurrentStatementCompletionFacts,
                context.CancellationToken)
            : new SymbolicConditionProofResult(
                condition,
                SymbolicTruthValue.Unknown,
                snapshotFailureReason);
    }
}
