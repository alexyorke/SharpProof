using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicConditionProofDispatcher
{
    private readonly SymbolicSourceQueryService _service;

    internal SymbolicConditionProofDispatcher(SymbolicSourceQueryService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    internal SymbolicConditionProofResult Prove(
        ValidatedSymbolicQueryRequest request,
        string conditionText,
        CancellationToken cancellationToken)
    {
        request.RequireTarget(
            static kind => kind == SharpProofTargetKind.Point,
            "Condition proof requests require a point target.");
        var smtAnalysis = request.RequireSmt("Condition proof requests require SMT analysis.");

        return SymbolicSourceInputDispatcher.Execute(
            request.Source,
            request.Target,
            request.Options,
            SymbolicSourceCompilationKind.Query,
            "Condition proof source kind is not supported.",
            (syntaxTree, compilation, target, token) => _service.ProveConditionAtSyntaxTree(
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

    internal SymbolicConditionProofResult ProveAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken)
    {
        return _service.ProveConditionAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken);
    }

    internal SymbolicConditionProofResult ProveAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SymbolicCondition symbolicCondition,
        SymbolicState initialState,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken)
    {
        return _service.ProveConditionAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            symbolicCondition,
            initialState,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken);
    }
}

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

    private static bool SupportsScopedTarget(SharpProofTargetKind kind)
    {
        return kind is SharpProofTargetKind.Point or SharpProofTargetKind.Position or
            SharpProofTargetKind.Line or SharpProofTargetKind.Span or SharpProofTargetKind.LineSpan or
            SharpProofTargetKind.AllLines;
    }

    private SymbolicQueryResult QuerySyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SharpProofTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        return target.Kind switch
        {
            SharpProofTargetKind.Point => SymbolicQueryResult.From(_sourceQueryService.QuerySyntaxTreeLinePoint(
                syntaxTree, compilation, target.Line!.Value, target.Column ?? 1, cancellationToken,
                options.SmtAnalysis, options.ImpliedConditions, options.IncludeExpressionProgramPoints,
                options.IncludeCurrentStatementCompletionFacts)),
            SharpProofTargetKind.Position => SymbolicQueryResult.From(_sourceQueryService.QuerySyntaxTreeAtPosition(
                syntaxTree, compilation, target.Position!.Value, cancellationToken,
                options.SmtAnalysis, options.ImpliedConditions)),
            SharpProofTargetKind.Line => _sourceQueryService.QuerySyntaxTreeLine(
                syntaxTree, compilation, target.Line!.Value, cancellationToken,
                options.SmtAnalysis, options.ImpliedConditions, options.IncludeExpressionProgramPoints,
                options.IncludeCurrentStatementCompletionFacts),
            SharpProofTargetKind.Span => _sourceQueryService.QuerySyntaxTreeSpan(
                syntaxTree, compilation, target.SpanStart!.Value, target.SpanEnd!.Value, cancellationToken,
                options.SmtAnalysis, options.ImpliedConditions, options.IncludeExpressionProgramPoints,
                options.IncludeCurrentStatementCompletionFacts),
            SharpProofTargetKind.LineSpan => _sourceQueryService.QuerySyntaxTreeLineSpan(
                syntaxTree, compilation, target.StartLine!.Value, target.StartColumn!.Value,
                target.EndLine!.Value, target.EndColumn!.Value, cancellationToken,
                options.SmtAnalysis, options.ImpliedConditions, options.IncludeExpressionProgramPoints,
                options.IncludeCurrentStatementCompletionFacts),
            SharpProofTargetKind.AllLines => _sourceQueryService.QuerySyntaxTreeAllLines(
                syntaxTree, compilation, cancellationToken, options.SmtAnalysis, options.ImpliedConditions,
                options.IncludeExpressionProgramPoints, options.IncludeCurrentStatementCompletionFacts),
            _ => throw new NotSupportedException("Target kind is not supported for syntax tree queries.")
        };
    }

    private SymbolicQueryResult QueryNode(
        SyntaxNode node,
        SemanticModel semanticModel,
        SharpProofTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
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

    private static bool SupportsTarget(SharpProofTargetKind kind)
    {
        return kind is SharpProofTargetKind.Line or SharpProofTargetKind.Point or
            SharpProofTargetKind.Span or SharpProofTargetKind.AllLines;
    }

    private SymbolicRuntimeHazardQueryResult QuerySyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SharpProofTarget target,
        SymbolicQueryOptions options,
        SymbolicRuntimeHazardQueryOptions hazardOptions,
        CancellationToken cancellationToken)
    {
        return target.Kind switch
        {
            SharpProofTargetKind.Line or SharpProofTargetKind.Point =>
                _service.QuerySyntaxTreeRuntimeHazardsLine(
                    syntaxTree, compilation, target.Line!.Value, options.SmtAnalysis!, cancellationToken,
                    hazardOptions),
            SharpProofTargetKind.Span => _service.QuerySyntaxTreeRuntimeHazardsSpan(
                syntaxTree, compilation, target.SpanStart!.Value, target.SpanEnd!.Value,
                options.SmtAnalysis!, cancellationToken, hazardOptions),
            SharpProofTargetKind.AllLines => _service.QuerySyntaxTreeRuntimeHazards(
                syntaxTree, compilation, options.SmtAnalysis!, cancellationToken, hazardOptions),
            _ => throw new NotSupportedException("Target kind is not supported for runtime hazard queries.")
        };
    }
}
