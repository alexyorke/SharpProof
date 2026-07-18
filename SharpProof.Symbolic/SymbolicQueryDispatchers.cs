using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicSourceQueryDispatcher
{
    private readonly SymbolicInvariantService _invariantService;
    private readonly SymbolicSourceQueryService _sourceQueryService;

    internal SymbolicSourceQueryDispatcher(
        SymbolicInvariantService invariantService,
        SymbolicSourceQueryService sourceQueryService)
    {
        _invariantService = invariantService ?? throw new ArgumentNullException(nameof(invariantService));
        _sourceQueryService = sourceQueryService ?? throw new ArgumentNullException(nameof(sourceQueryService));
    }

    internal SymbolicQueryResult Query(
        ValidatedSymbolicQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (!SupportsScopedTarget(request.Target.Kind) &&
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
                QuerySyntaxTree(syntaxTree, compilation, target, request.Options, token),
            (node, semanticModel, target, token) =>
                QueryNode(node, semanticModel, target, request.Options, token),
            cancellationToken);
    }

    private static bool SupportsScopedTarget(SymbolicQueryTargetKind kind)
    {
        return kind is SymbolicQueryTargetKind.Point or SymbolicQueryTargetKind.Position or
            SymbolicQueryTargetKind.Line or SymbolicQueryTargetKind.Span or SymbolicQueryTargetKind.LineSpan or
            SymbolicQueryTargetKind.AllLines;
    }

    private SymbolicQueryResult QuerySyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        return target.Kind switch
        {
            SymbolicQueryTargetKind.Point => SymbolicQueryResult.From(_sourceQueryService.QuerySyntaxTreeLinePoint(
                syntaxTree, compilation, target.LineNumber!.Value, target.ColumnNumber ?? 1, cancellationToken,
                options.SmtAnalysis, options.ImpliedConditions, options.IncludeExpressionProgramPoints,
                options.IncludeCurrentStatementCompletionFacts)),
            SymbolicQueryTargetKind.Position => SymbolicQueryResult.From(_sourceQueryService.QuerySyntaxTreeAtPosition(
                syntaxTree, compilation, target.PositionOffset!.Value, cancellationToken,
                options.SmtAnalysis, options.ImpliedConditions)),
            SymbolicQueryTargetKind.Line => _sourceQueryService.QuerySyntaxTreeLine(
                syntaxTree, compilation, target.LineNumber!.Value, cancellationToken,
                options.SmtAnalysis, options.ImpliedConditions, options.IncludeExpressionProgramPoints,
                options.IncludeCurrentStatementCompletionFacts),
            SymbolicQueryTargetKind.Span => _sourceQueryService.QuerySyntaxTreeSpan(
                syntaxTree, compilation, target.SpanStart!.Value, target.SpanEnd!.Value, cancellationToken,
                options.SmtAnalysis, options.ImpliedConditions, options.IncludeExpressionProgramPoints,
                options.IncludeCurrentStatementCompletionFacts),
            SymbolicQueryTargetKind.LineSpan => _sourceQueryService.QuerySyntaxTreeLineSpan(
                syntaxTree, compilation, target.StartLine!.Value, target.StartColumn!.Value,
                target.EndLine!.Value, target.EndColumn!.Value, cancellationToken,
                options.SmtAnalysis, options.ImpliedConditions, options.IncludeExpressionProgramPoints,
                options.IncludeCurrentStatementCompletionFacts),
            SymbolicQueryTargetKind.AllLines => _sourceQueryService.QuerySyntaxTreeAllLines(
                syntaxTree, compilation, cancellationToken, options.SmtAnalysis, options.ImpliedConditions,
                options.IncludeExpressionProgramPoints, options.IncludeCurrentStatementCompletionFacts),
            _ => throw new NotSupportedException("Target kind is not supported for syntax tree queries.")
        };
    }

    private SymbolicQueryResult QueryNode(
        SyntaxNode node,
        SemanticModel semanticModel,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        if (target.Kind != SymbolicQueryTargetKind.Node)
            throw new NotSupportedException("Node sources require a node target.");

        var analysis = node is ForStatementSyntax forStatement
            ? _invariantService.AnalyzeForInitialEntry(
                forStatement, semanticModel, options.SmtAnalysis, cancellationToken)
            : _invariantService.AnalyzeAt(node, semanticModel, options.SmtAnalysis, cancellationToken,
                options.IncludeCurrentStatementCompletionFacts);
        var linePosition = SymbolicSourceLocation.GetLineAndColumn(
            node.SyntaxTree, node.SpanStart, cancellationToken, true);
        var span = SymbolicSourceLocation.GetNodeSourceSpan(node.SyntaxTree, node.Span, cancellationToken);
        var proofs = CreateNodeProofs(
            semanticModel, node, analysis, options.ImpliedConditions, options.SmtAnalysis, cancellationToken);
        var mergedInvariantText = SymbolicFormulaDisplay.FormatMergedInvariant(analysis.PathConditions);
        var invariant = SymbolicInvariantResult.FromFormulas(analysis.PathConditions, mergedInvariantText);

        return SymbolicQueryResult.From(new SymbolicProgramPointResult(
            node.SyntaxTree.FilePath, linePosition.Line, linePosition.Column, node.SpanStart, node.SpanStart,
            node.Kind().ToString(), analysis.Facts, analysis.Reachability, analysis.ReachabilityReason, proofs,
            SymbolicSmtDiagnostics.FromService(options.SmtAnalysis), mergedInvariantText, invariant, node.Span.End,
            span.StartLine, span.StartColumn, span.EndLine, span.EndColumn,
            SymbolicProgramPointMetadata.GetContainingMethodName(node),
            SymbolicProgramPointKinds.Normalize(null, node.Kind().ToString()),
            symbolicFacts: SymbolicFactInfo.FromState(analysis.PathState),
            reachabilityWitness: SymbolicInputWitnessFactory.CreateReachability(
                analysis.ReachabilityProof?.PathCheck.Witness, analysis.PathConditions, semanticModel,
                node.SpanStart, analysis.Reachability, analysis.ReachabilityReason)));
    }

    private IReadOnlyList<SymbolicConditionProofResult> CreateNodeProofs(
        SemanticModel semanticModel,
        SyntaxNode node,
        SymbolicProgramPointAnalysis analysis,
        IEnumerable<string> conditionTexts,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken)
    {
        if (conditionTexts == null) return Array.Empty<SymbolicConditionProofResult>();

        return conditionTexts
            .Where(static condition => !string.IsNullOrWhiteSpace(condition))
            .Select(condition => _sourceQueryService.ProveConditionAtAnalysis(
                semanticModel, node, analysis, condition,
                smtAnalysis ?? throw new ArgumentException("Condition proof requests require SMT analysis."),
                cancellationToken))
            .ToArray();
    }
}

internal sealed class SymbolicRuntimeHazardQueryDispatcher
{
    private readonly SymbolicRuntimeHazardQueryService _service;

    internal SymbolicRuntimeHazardQueryDispatcher(SymbolicRuntimeHazardQueryService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    internal SymbolicRuntimeHazardQueryResult Query(
        ValidatedSymbolicQueryRequest request,
        SmtAnalysisService smtAnalysis,
        SymbolicRuntimeHazardQueryOptions hazardOptions,
        CancellationToken cancellationToken)
    {
        if (!SupportsTarget(request.Target.Kind) && request.Source.Kind != SymbolicSourceInputKind.Node)
            throw new NotSupportedException("Target kind is not supported for runtime hazard queries.");

        return SymbolicSourceInputDispatcher.Execute(
            request.Source, request.Target, request.Options, SymbolicSourceCompilationKind.RuntimeHazards,
            "Runtime hazard source kind is not supported.",
            (syntaxTree, compilation, target, token) =>
                QuerySyntaxTree(syntaxTree, compilation, target, request.Options, hazardOptions, token),
            (node, semanticModel, target, token) => _service.QueryNodeRuntimeHazards(
                node, semanticModel, smtAnalysis, token, hazardOptions, target.IncludeNestedCallables),
            cancellationToken);
    }

    private static bool SupportsTarget(SymbolicQueryTargetKind kind)
    {
        return kind is SymbolicQueryTargetKind.Line or SymbolicQueryTargetKind.Point or
            SymbolicQueryTargetKind.Span or SymbolicQueryTargetKind.AllLines;
    }

    private SymbolicRuntimeHazardQueryResult QuerySyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        SymbolicRuntimeHazardQueryOptions hazardOptions,
        CancellationToken cancellationToken)
    {
        return target.Kind switch
        {
            SymbolicQueryTargetKind.Line or SymbolicQueryTargetKind.Point =>
                _service.QuerySyntaxTreeRuntimeHazardsLine(
                    syntaxTree, compilation, target.LineNumber!.Value, options.SmtAnalysis!, cancellationToken,
                    hazardOptions),
            SymbolicQueryTargetKind.Span => _service.QuerySyntaxTreeRuntimeHazardsSpan(
                syntaxTree, compilation, target.SpanStart!.Value, target.SpanEnd!.Value,
                options.SmtAnalysis!, cancellationToken, hazardOptions),
            SymbolicQueryTargetKind.AllLines => _service.QuerySyntaxTreeRuntimeHazards(
                syntaxTree, compilation, options.SmtAnalysis!, cancellationToken, hazardOptions),
            _ => throw new NotSupportedException("Target kind is not supported for runtime hazard queries.")
        };
    }
}
