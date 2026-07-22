namespace SharpProof.Symbolic;

internal sealed partial class SymbolicQueryExecutor {
    private SymbolicQueryResult QuerySource(SymbolicQueryContext request, CancellationToken cancellationToken) => QuerySourceSyntaxTree(
            request.Source.SyntaxTree,
            request.Source.Compilation,
            request.Target,
            request.Options,
            cancellationToken);

    private SymbolicQueryResult QuerySourceSyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SharpProofTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken) => target.Kind switch {
            SharpProofTargetKind.Point => SymbolicQueryResult.From(_rangeQueryExecutor.QueryLinePoint(
                syntaxTree, compilation, target.Line!.Value, target.Column ?? 1, options, cancellationToken)),
            SharpProofTargetKind.Position => SymbolicQueryResult.From(QueryPosition(
                syntaxTree, compilation, target.Position!.Value, options, cancellationToken)),
            SharpProofTargetKind.Line => _rangeQueryExecutor.QueryLine(
                syntaxTree, compilation, target.Line!.Value, options, cancellationToken),
            SharpProofTargetKind.Span => _rangeQueryExecutor.QuerySpan(
                syntaxTree, compilation, target.SpanStart!.Value, target.SpanEnd!.Value, options, cancellationToken),
            SharpProofTargetKind.AllLines => _rangeQueryExecutor.QueryAllLines(syntaxTree, compilation, options, cancellationToken),
            _ => throw new NotSupportedException("Target kind is not supported for syntax tree queries.")
        };
    private SymbolicProgramPointResult QueryPosition(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int position,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken) {
        var query = _programPointExecutor.AnalyzeAtPosition(syntaxTree, compilation, position, options, cancellationToken);
        return _programPointExecutor.Project(query, cancellationToken);
    }
    private SymbolicConditionProofResult ProveSource(
        SymbolicQueryContext request,
        string conditionText,
        CancellationToken cancellationToken) {
        if (request.Target.Kind != SharpProofTargetKind.Point)
            throw new ArgumentException("Condition proof requests require a point target.", "context");

        var smtAnalysis = RequireSmt(request, "Condition proof requests require SMT analysis.");
        return _conditionProofEngine.ProveAtSyntaxTree(
            request.Source.SyntaxTree,
            request.Source.Compilation,
            request.Target.Line!.Value,
            request.Target.Column ?? 1,
            conditionText,
            smtAnalysis,
            cancellationToken);
    }
    private SymbolicRuntimeHazardQueryResult QueryRuntimeHazardsSource(
        SymbolicQueryContext request,
        SmtAnalysisService smtAnalysis,
        SymbolicRuntimeHazardQueryOptions hazardOptions,
        CancellationToken cancellationToken) {
        if (!SupportsRuntimeHazardTarget(request.Target.Kind))
            throw new NotSupportedException("Target kind is not supported for runtime hazard queries.");

        return _runtimeHazardService.QuerySyntaxTreeRuntimeHazards(
            request.Source.SyntaxTree,
            request.Source.Compilation,
            request.Target,
            smtAnalysis,
            cancellationToken,
            hazardOptions);
    }
    private static bool SupportsRuntimeHazardTarget(SharpProofTargetKind kind) =>
        kind is SharpProofTargetKind.Line or SharpProofTargetKind.Point or
            SharpProofTargetKind.Span or SharpProofTargetKind.AllLines;

    private static SmtAnalysisService RequireSmt(SymbolicQueryContext request, string message) =>
        request.Options.SmtAnalysis ?? throw new ArgumentException(message, "context");
}
