namespace SharpProof.Symbolic;

internal sealed partial class SymbolicQueryExecutor {
    private SymbolicQueryResult QuerySource(
        SymbolicQueryContext request,
        CancellationToken cancellationToken) {
        if (!SupportsSourceTarget(request.Target.Kind) &&
            request.Source.Kind is SymbolicSourceInputKind.File or SymbolicSourceInputKind.Text)
            throw new NotSupportedException(request.Source.Kind == SymbolicSourceInputKind.File
                ? "Target kind is not supported for file queries."
                : "Target kind is not supported for source queries.");

        return SymbolicSourceInputDispatcher.Execute(
            request.Source,
            request.Target,
            request.Options,
            SymbolicSourceCompilationKind.Query,
            "Source kind is not supported.",
            (syntaxTree, compilation, target, token) =>
                QuerySourceSyntaxTree(syntaxTree, compilation, target, request.Options, token),
            (node, semanticModel, target, token) =>
                QuerySourceNode(node, semanticModel, target, request.Options, token),
            cancellationToken);
    }

    private static bool SupportsSourceTarget(SharpProofTargetKind kind) =>
        kind is SharpProofTargetKind.Point or SharpProofTargetKind.Position or
            SharpProofTargetKind.Line or SharpProofTargetKind.Span or SharpProofTargetKind.LineSpan or
            SharpProofTargetKind.AllLines;

    private SymbolicQueryResult QuerySourceSyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SharpProofTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken) {
        return target.Kind switch {
            SharpProofTargetKind.Point => SymbolicQueryResult.From(_rangeQueryExecutor.QueryLinePoint(
                syntaxTree, compilation, target.Line!.Value, target.Column ?? 1, options, cancellationToken)),
            SharpProofTargetKind.Position => SymbolicQueryResult.From(QueryPosition(
                syntaxTree, compilation, target.Position!.Value, options, cancellationToken)),
            SharpProofTargetKind.Line => _rangeQueryExecutor.QueryLine(
                syntaxTree, compilation, target.Line!.Value, options, cancellationToken),
            SharpProofTargetKind.Span => _rangeQueryExecutor.QuerySpan(
                syntaxTree, compilation, target.SpanStart!.Value, target.SpanEnd!.Value, options, cancellationToken),
            SharpProofTargetKind.LineSpan => _rangeQueryExecutor.QueryLineSpan(
                syntaxTree, compilation, target.StartLine!.Value, target.StartColumn!.Value,
                target.EndLine!.Value, target.EndColumn!.Value, options, cancellationToken),
            SharpProofTargetKind.AllLines => _rangeQueryExecutor.QueryAllLines(
                syntaxTree, compilation, options, cancellationToken),
            _ => throw new NotSupportedException("Target kind is not supported for syntax tree queries.")
        };
    }

    private SymbolicProgramPointResult QueryPosition(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int position,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken) {
        var query = _programPointExecutor.AnalyzeAtPosition(
            syntaxTree,
            compilation,
            position,
            options,
            cancellationToken);
        var lineColumn = SymbolicSourceLocation.GetLineAndColumn(
            syntaxTree,
            position,
            cancellationToken,
            true);
        return _programPointExecutor.Project(
            syntaxTree,
            query,
            lineColumn.Line,
            lineColumn.Column,
            options,
            cancellationToken);
    }

    private SymbolicQueryResult QuerySourceNode(
        SyntaxNode node,
        SemanticModel semanticModel,
        SharpProofTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken) {
        if (target.Kind != SharpProofTargetKind.Node)
            throw new NotSupportedException("Node sources require a node target.");

        var analysis = node is ForStatementSyntax forStatement
            ? _invariantService.AnalyzeForInitialEntry(
                forStatement, semanticModel, options.SmtAnalysis, cancellationToken)
            : _invariantService.AnalyzeAt(node, semanticModel, options.SmtAnalysis, cancellationToken,
                options.IncludeCurrentStatementCompletionFacts);
        var linePosition = SymbolicSourceLocation.GetLineAndColumn(
            node.SyntaxTree, node.SpanStart, cancellationToken, true);
        var proofs = CreateNodeProofs(
            semanticModel, node, analysis, options.ImpliedConditions, options.SmtAnalysis, cancellationToken);
        return SymbolicQueryResult.From(SymbolicProgramPointProjector.Project(
            node.SyntaxTree,
            new SymbolicProgramPointQueryContext(semanticModel, node.SpanStart, node, analysis),
            linePosition.Line,
            linePosition.Column,
            proofs,
            SymbolicSmtDiagnostics.FromService(options.SmtAnalysis),
            cancellationToken));
    }

    private IReadOnlyList<SymbolicConditionProofResult> CreateNodeProofs(
        SemanticModel semanticModel,
        SyntaxNode node,
        SymbolicProgramPointAnalysis analysis,
        IEnumerable<string> conditionTexts,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken) {
        if (conditionTexts == null) return Array.Empty<SymbolicConditionProofResult>();

        return conditionTexts
            .Where(static condition => !string.IsNullOrWhiteSpace(condition))
            .Select(condition => _conditionProofEngine.ProveAtAnalysis(
                semanticModel, node, analysis, condition,
                smtAnalysis ?? throw new ArgumentException("Condition proof requests require SMT analysis."),
                cancellationToken))
            .ToArray();
    }

    private SymbolicConditionProofResult ProveSource(
        SymbolicQueryContext request,
        string conditionText,
        CancellationToken cancellationToken) {
        if (request.Target.Kind != SharpProofTargetKind.Point)
            throw new ArgumentException("Condition proof requests require a point target.", "context");

        var smtAnalysis = RequireSmt(request, "Condition proof requests require SMT analysis.");
        return SymbolicSourceInputDispatcher.Execute(
            request.Source,
            request.Target,
            request.Options,
            SymbolicSourceCompilationKind.Query,
            "Condition proof source kind is not supported.",
            (syntaxTree, compilation, target, token) => _conditionProofEngine.ProveAtSyntaxTree(
                syntaxTree,
                compilation,
                target.Line!.Value,
                target.Column ?? 1,
                conditionText,
                smtAnalysis,
                token),
            static (_, _, _, _) =>
                throw new NotSupportedException("Condition proof source kind is not supported."),
            cancellationToken);
    }

    private SymbolicRuntimeHazardQueryResult QueryRuntimeHazardsSource(
        SymbolicQueryContext request,
        SmtAnalysisService smtAnalysis,
        SymbolicRuntimeHazardQueryOptions hazardOptions,
        CancellationToken cancellationToken) {
        if (!SupportsRuntimeHazardTarget(request.Target.Kind) &&
            request.Source.Kind != SymbolicSourceInputKind.Node)
            throw new NotSupportedException("Target kind is not supported for runtime hazard queries.");

        return SymbolicSourceInputDispatcher.Execute(
            request.Source,
            request.Target,
            request.Options,
            SymbolicSourceCompilationKind.RuntimeHazards,
            "Runtime hazard source kind is not supported.",
            (syntaxTree, compilation, target, token) => _runtimeHazardService.QuerySyntaxTreeRuntimeHazards(
                syntaxTree, compilation, target, request.Options.SmtAnalysis!, token, hazardOptions),
            (node, semanticModel, target, token) => _runtimeHazardService.QueryNodeRuntimeHazards(
                node, semanticModel, smtAnalysis, token, hazardOptions, target.IncludeNestedCallables),
            cancellationToken);
    }

    private static bool SupportsRuntimeHazardTarget(SharpProofTargetKind kind) =>
        kind is SharpProofTargetKind.Line or SharpProofTargetKind.Point or
            SharpProofTargetKind.Span or SharpProofTargetKind.AllLines;

    private static SmtAnalysisService RequireSmt(SymbolicQueryContext request, string message) =>
        request.Options.SmtAnalysis ?? throw new ArgumentException(message, "context");
}
