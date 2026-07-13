using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

public sealed class SymbolicQueryService
{
    private readonly SymbolicCapabilityService _capabilityService;
    private readonly SymbolicComplexityService _complexityService;
    private readonly SymbolicInvariantService _invariantService;
    private readonly SymbolicRuntimeHazardQueryService _runtimeHazardQueryService;
    private readonly SymbolicSourceQueryService _sourceQueryService;

    public SymbolicQueryService()
        : this(new SymbolicInvariantService())
    {
    }

    internal SymbolicQueryService(SymbolicInvariantService invariantService)
    {
        if (invariantService == null) throw new ArgumentNullException(nameof(invariantService));

        _invariantService = invariantService;
        _sourceQueryService = new SymbolicSourceQueryService(invariantService);
        _runtimeHazardQueryService = new SymbolicRuntimeHazardQueryService(invariantService);
        _complexityService = new SymbolicComplexityService();
        _capabilityService = new SymbolicCapabilityService();
    }

    public SymbolicQueryResult Query(
        SymbolicQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var options = request.Options ?? SymbolicQueryOptions.Default;
        using var limitScope = SymbolicAnalysisLimitContext.Push(options.AnalysisLimits);
        var result = QueryCore(request.Source, request.Target, options, cancellationToken);
        return options.Filter == null || options.Filter.IsEmpty
            ? result
            : result.Filter(options.Filter);
    }

    public SymbolicOperationResult<SymbolicQueryResult> TryQuery(
        SymbolicQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => Query(request, cancellationToken));
    }

    public SymbolicConditionProofResult Prove(
        SymbolicConditionProofRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.ConditionText))
            throw new ArgumentException("Condition text is required.", nameof(request));

        var pointTarget = request.Target.Kind == SymbolicQueryTargetKind.Point
            ? request.Target
            : throw new ArgumentException("Condition proof requests require a point target.", nameof(request));
        var options = request.Options ?? SymbolicQueryOptions.Default;
        if (options.SmtAnalysis == null)
            throw new ArgumentException("Condition proof requests require SMT analysis.", nameof(request));

        using var limitScope = SymbolicAnalysisLimitContext.Push(options.AnalysisLimits);
        var source = request.Source;
        switch (source.Kind)
        {
            case SymbolicSourceInputKind.File:
                return _sourceQueryService.ProveConditionAtFile(
                    source.FilePath!,
                    pointTarget.LineNumber!.Value,
                    pointTarget.ColumnNumber ?? 1,
                    request.ConditionText,
                    options.SmtAnalysis,
                    options.References,
                    cancellationToken,
                    source.CompilationProfile);
            case SymbolicSourceInputKind.Text:
                return _sourceQueryService.ProveConditionAtSource(
                    source.SourceText!,
                    source.FilePath ?? SymbolicSourceInput.DefaultFilePath,
                    pointTarget.LineNumber!.Value,
                    pointTarget.ColumnNumber ?? 1,
                    request.ConditionText,
                    options.SmtAnalysis,
                    options.References,
                    cancellationToken,
                    source.CompilationProfile);
            case SymbolicSourceInputKind.SyntaxTree:
                return _sourceQueryService.ProveConditionAtSyntaxTree(
                    source.SyntaxTree!,
                    source.Compilation!,
                    pointTarget.LineNumber!.Value,
                    pointTarget.ColumnNumber ?? 1,
                    request.ConditionText,
                    options.SmtAnalysis,
                    cancellationToken);
            default:
                throw new NotSupportedException("Condition proof source kind is not supported.");
        }
    }

    public SymbolicOperationResult<SymbolicConditionProofResult> TryProve(
        SymbolicConditionProofRequest request,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => Prove(request, cancellationToken));
    }

    internal SymbolicConditionProofResult ProveAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
    {
        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));

        if (node == null) throw new ArgumentNullException(nameof(node));

        if (string.IsNullOrWhiteSpace(conditionText))
            throw new ArgumentException("Condition text is required.", nameof(conditionText));

        if (smtAnalysis == null) throw new ArgumentNullException(nameof(smtAnalysis));

        return _sourceQueryService.ProveConditionAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken);
    }

    internal SymbolicOperationResult<SymbolicConditionProofResult> TryProveAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => ProveAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken));
    }

    internal SymbolicConditionProofResult ProveAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SymbolicCondition symbolicCondition,
        SymbolicState initialState,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
    {
        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));

        if (node == null) throw new ArgumentNullException(nameof(node));

        if (string.IsNullOrWhiteSpace(conditionText))
            throw new ArgumentException("Condition text is required.", nameof(conditionText));

        if (symbolicCondition == null) throw new ArgumentNullException(nameof(symbolicCondition));

        if (initialState == null) throw new ArgumentNullException(nameof(initialState));

        if (smtAnalysis == null) throw new ArgumentNullException(nameof(smtAnalysis));

        return _sourceQueryService.ProveConditionAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            symbolicCondition,
            initialState,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken);
    }

    internal SymbolicOperationResult<SymbolicConditionProofResult> TryProveAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SymbolicCondition symbolicCondition,
        SymbolicState initialState,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => ProveAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            symbolicCondition,
            initialState,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken));
    }

    public SymbolicRuntimeHazardQueryResult QueryRuntimeHazards(
        SymbolicRuntimeHazardRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var options = request.Options ?? SymbolicQueryOptions.Default;
        if (options.SmtAnalysis == null)
            throw new ArgumentException("Runtime hazard queries require SMT analysis.", nameof(request));

        using var limitScope = SymbolicAnalysisLimitContext.Push(options.AnalysisLimits);
        var hazardOptions = request.HazardOptions ?? SymbolicRuntimeHazardQueryOptions.Default;
        var source = request.Source;
        var target = request.Target;
        switch (source.Kind)
        {
            case SymbolicSourceInputKind.File:
                return QueryFileRuntimeHazards(
                    source.FilePath!,
                    source.CompilationProfile,
                    target,
                    options,
                    hazardOptions,
                    cancellationToken);
            case SymbolicSourceInputKind.Text:
                return QuerySourceRuntimeHazards(source.SourceText!,
                    source.FilePath ?? SymbolicSourceInput.DefaultFilePath, source.CompilationProfile, target, options,
                    hazardOptions,
                    cancellationToken);
            case SymbolicSourceInputKind.SyntaxTree:
                return QuerySyntaxTreeRuntimeHazards(source.SyntaxTree!, source.Compilation!, target, options,
                    hazardOptions, cancellationToken);
            case SymbolicSourceInputKind.Node:
                return _runtimeHazardQueryService.QueryNodeRuntimeHazards(
                    source.Node!,
                    source.SemanticModel!,
                    options.SmtAnalysis,
                    cancellationToken,
                    hazardOptions,
                    target.IncludeNestedCallables);
            default:
                throw new NotSupportedException("Runtime hazard source kind is not supported.");
        }
    }

    public SymbolicOperationResult<SymbolicRuntimeHazardQueryResult> TryQueryRuntimeHazards(
        SymbolicRuntimeHazardRequest request,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => QueryRuntimeHazards(request, cancellationToken));
    }

    public SymbolicComplexityResult QueryComplexity(
        SymbolicComplexityRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var options = request.Options ?? SymbolicQueryOptions.Default;
        using var limitScope = SymbolicAnalysisLimitContext.Push(options.AnalysisLimits);
        return _complexityService.Query(
            request.Source,
            request.Target,
            options,
            cancellationToken);
    }

    public SymbolicOperationResult<SymbolicComplexityResult> TryQueryComplexity(
        SymbolicComplexityRequest request,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => QueryComplexity(request, cancellationToken));
    }

    public SymbolicCapabilityResult QueryCapabilities(
        SymbolicCapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var options = request.Options ?? SymbolicQueryOptions.Default;
        using var limitScope = SymbolicAnalysisLimitContext.Push(options.AnalysisLimits);
        return _capabilityService.Query(
            request.Source,
            request.Target,
            options,
            cancellationToken);
    }

    public SymbolicOperationResult<SymbolicCapabilityResult> TryQueryCapabilities(
        SymbolicCapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => QueryCapabilities(request, cancellationToken));
    }

    private static SymbolicOperationResult<T> TryExecute<T>(Func<T> operation)
        where T : class
    {
        try
        {
            return SymbolicOperationResult<T>.Success(operation());
        }
        catch (Exception exception) when (!SymbolicErrorClassifier.IsFatal(exception))
        {
            return SymbolicOperationResult<T>.Failure(SymbolicErrorClassifier.FromException(exception));
        }
    }

    private SymbolicQueryResult QueryCore(
        SymbolicSourceInput source,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        switch (source.Kind)
        {
            case SymbolicSourceInputKind.File:
                return SymbolicQueryResult.From(QueryFile(
                    source.FilePath!,
                    source.CompilationProfile,
                    target,
                    options,
                    cancellationToken));
            case SymbolicSourceInputKind.Text:
                return SymbolicQueryResult.From(QuerySource(source.SourceText!,
                    source.FilePath ?? SymbolicSourceInput.DefaultFilePath, source.CompilationProfile, target, options,
                    cancellationToken));
            case SymbolicSourceInputKind.SyntaxTree:
                return SymbolicQueryResult.From(QuerySyntaxTree(source.SyntaxTree!, source.Compilation!, target,
                    options, cancellationToken));
            case SymbolicSourceInputKind.Node:
                return SymbolicQueryResult.From(QueryNode(source.Node!, source.SemanticModel!, target, options,
                    cancellationToken));
            default:
                throw new NotSupportedException("Source kind is not supported.");
        }
    }

    private object QueryFile(
        string filePath,
        SymbolicSourceCompilationProfile? compilationProfile,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        switch (target.Kind)
        {
            case SymbolicQueryTargetKind.Point:
                return _sourceQueryService.QueryFileLinePoint(
                    filePath,
                    target.LineNumber!.Value,
                    target.ColumnNumber ?? 1,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile);
            case SymbolicQueryTargetKind.Position:
                return _sourceQueryService.QueryFileAtPosition(
                    filePath,
                    target.PositionOffset!.Value,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    compilationProfile);
            case SymbolicQueryTargetKind.Line:
                return _sourceQueryService.QueryFileLine(
                    filePath,
                    target.LineNumber!.Value,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile);
            case SymbolicQueryTargetKind.Span:
                return _sourceQueryService.QueryFileSpan(
                    filePath,
                    target.SpanStart!.Value,
                    target.SpanEnd!.Value,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile);
            case SymbolicQueryTargetKind.LineSpan:
                return _sourceQueryService.QueryFileLineSpan(
                    filePath,
                    target.StartLine!.Value,
                    target.StartColumn!.Value,
                    target.EndLine!.Value,
                    target.EndColumn!.Value,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile);
            case SymbolicQueryTargetKind.AllLines:
                return _sourceQueryService.QueryFileAllLines(
                    filePath,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile);
            default:
                throw new NotSupportedException("Target kind is not supported for file queries.");
        }
    }

    private object QuerySource(
        string sourceText,
        string filePath,
        SymbolicSourceCompilationProfile? compilationProfile,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        switch (target.Kind)
        {
            case SymbolicQueryTargetKind.Point:
                return _sourceQueryService.QuerySourceLinePoint(
                    sourceText,
                    filePath,
                    target.LineNumber!.Value,
                    target.ColumnNumber ?? 1,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile);
            case SymbolicQueryTargetKind.Position:
                return _sourceQueryService.QuerySourceAtPosition(
                    sourceText,
                    filePath,
                    target.PositionOffset!.Value,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    compilationProfile);
            case SymbolicQueryTargetKind.Line:
                return _sourceQueryService.QuerySourceLine(
                    sourceText,
                    filePath,
                    target.LineNumber!.Value,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile);
            case SymbolicQueryTargetKind.Span:
                return _sourceQueryService.QuerySourceSpan(
                    sourceText,
                    filePath,
                    target.SpanStart!.Value,
                    target.SpanEnd!.Value,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile);
            case SymbolicQueryTargetKind.LineSpan:
                return _sourceQueryService.QuerySourceLineSpan(
                    sourceText,
                    filePath,
                    target.StartLine!.Value,
                    target.StartColumn!.Value,
                    target.EndLine!.Value,
                    target.EndColumn!.Value,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile);
            case SymbolicQueryTargetKind.AllLines:
                return _sourceQueryService.QuerySourceAllLines(
                    sourceText,
                    filePath,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile);
            default:
                throw new NotSupportedException("Target kind is not supported for source queries.");
        }
    }

    private object QuerySyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        switch (target.Kind)
        {
            case SymbolicQueryTargetKind.Point:
                return _sourceQueryService.QuerySyntaxTreeLinePoint(
                    syntaxTree,
                    compilation,
                    target.LineNumber!.Value,
                    target.ColumnNumber ?? 1,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts);
            case SymbolicQueryTargetKind.Position:
                return _sourceQueryService.QuerySyntaxTreeAtPosition(
                    syntaxTree,
                    compilation,
                    target.PositionOffset!.Value,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions);
            case SymbolicQueryTargetKind.Line:
                return _sourceQueryService.QuerySyntaxTreeLine(
                    syntaxTree,
                    compilation,
                    target.LineNumber!.Value,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts);
            case SymbolicQueryTargetKind.Span:
                return _sourceQueryService.QuerySyntaxTreeSpan(
                    syntaxTree,
                    compilation,
                    target.SpanStart!.Value,
                    target.SpanEnd!.Value,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts);
            case SymbolicQueryTargetKind.LineSpan:
                return _sourceQueryService.QuerySyntaxTreeLineSpan(
                    syntaxTree,
                    compilation,
                    target.StartLine!.Value,
                    target.StartColumn!.Value,
                    target.EndLine!.Value,
                    target.EndColumn!.Value,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts);
            case SymbolicQueryTargetKind.AllLines:
                return _sourceQueryService.QuerySyntaxTreeAllLines(
                    syntaxTree,
                    compilation,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts);
            default:
                throw new NotSupportedException("Target kind is not supported for syntax tree queries.");
        }
    }

    private object QueryNode(
        SyntaxNode node,
        SemanticModel semanticModel,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        if (target.Kind != SymbolicQueryTargetKind.Node)
            throw new NotSupportedException("Node sources require a node target.");

        var analysis = node is ForStatementSyntax forStatement
            ? _invariantService.AnalyzeForInitialEntry(forStatement, semanticModel, options.SmtAnalysis,
                cancellationToken)
            : _invariantService.AnalyzeAt(
                node,
                semanticModel,
                options.SmtAnalysis,
                cancellationToken,
                options.IncludeCurrentStatementCompletionFacts);
        var linePosition = SymbolicSourceLocation.GetLineAndColumn(
            node.SyntaxTree,
            node.SpanStart,
            cancellationToken,
            true);
        var span = SymbolicSourceLocation.GetNodeSourceSpan(node.SyntaxTree, node.Span, cancellationToken);
        var proofs = CreateNodeProofs(
            semanticModel,
            node,
            analysis,
            options.ImpliedConditions,
            options.SmtAnalysis,
            cancellationToken);
        var mergedInvariantText = SymbolicFormulaDisplay.FormatMergedInvariant(analysis.PathConditions);
        var invariant = SymbolicInvariantResult.FromFormulas(
            analysis.PathConditions,
            mergedInvariantText);
        return new SymbolicSourceQueryResult(
            node.SyntaxTree.FilePath,
            linePosition.Line,
            linePosition.Column,
            node.SpanStart,
            node.SpanStart,
            node.Kind().ToString(),
            analysis.Facts,
            analysis.Reachability,
            analysis.ReachabilityReason,
            proofs,
            SymbolicSmtDiagnostics.FromService(options.SmtAnalysis),
            mergedInvariantText,
            invariant,
            node.Span.End,
            span.StartLine,
            span.StartColumn,
            span.EndLine,
            span.EndColumn,
            SymbolicProgramPointMetadata.GetContainingMethodName(node),
            SymbolicProgramPointKinds.Normalize(null, node.Kind().ToString()),
            symbolicFacts: SymbolicFactInfo.FromState(analysis.PathState),
            reachabilityWitness: SymbolicInputWitnessFactory.CreateReachability(
                analysis.ReachabilityProof?.PathCheck.Witness,
                analysis.PathConditions,
                semanticModel,
                node.SpanStart,
                analysis.Reachability,
                analysis.ReachabilityReason));
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
                semanticModel,
                node,
                analysis,
                condition,
                smtAnalysis ?? throw new ArgumentException("Condition proof requests require SMT analysis."),
                cancellationToken))
            .ToArray();
    }

    private SymbolicRuntimeHazardQueryResult QueryFileRuntimeHazards(
        string filePath,
        SymbolicSourceCompilationProfile? compilationProfile,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        SymbolicRuntimeHazardQueryOptions hazardOptions,
        CancellationToken cancellationToken)
    {
        switch (target.Kind)
        {
            case SymbolicQueryTargetKind.Line:
            case SymbolicQueryTargetKind.Point:
                return _runtimeHazardQueryService.QueryFileRuntimeHazardsLine(
                    filePath,
                    target.LineNumber!.Value,
                    options.SmtAnalysis!,
                    options.References,
                    cancellationToken,
                    hazardOptions,
                    compilationProfile);
            case SymbolicQueryTargetKind.Span:
                return _runtimeHazardQueryService.QueryFileRuntimeHazardsSpan(
                    filePath,
                    target.SpanStart!.Value,
                    target.SpanEnd!.Value,
                    options.SmtAnalysis!,
                    options.References,
                    cancellationToken,
                    hazardOptions,
                    compilationProfile);
            case SymbolicQueryTargetKind.AllLines:
                return _runtimeHazardQueryService.QueryFileRuntimeHazards(
                    filePath,
                    options.SmtAnalysis!,
                    options.References,
                    cancellationToken,
                    hazardOptions,
                    compilationProfile);
            default:
                throw new NotSupportedException("Target kind is not supported for runtime hazard queries.");
        }
    }

    private SymbolicRuntimeHazardQueryResult QuerySourceRuntimeHazards(
        string sourceText,
        string filePath,
        SymbolicSourceCompilationProfile? compilationProfile,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        SymbolicRuntimeHazardQueryOptions hazardOptions,
        CancellationToken cancellationToken)
    {
        switch (target.Kind)
        {
            case SymbolicQueryTargetKind.Line:
            case SymbolicQueryTargetKind.Point:
                return _runtimeHazardQueryService.QuerySourceRuntimeHazardsLine(
                    sourceText,
                    filePath,
                    target.LineNumber!.Value,
                    options.SmtAnalysis!,
                    options.References,
                    cancellationToken,
                    hazardOptions,
                    compilationProfile);
            case SymbolicQueryTargetKind.Span:
                return _runtimeHazardQueryService.QuerySourceRuntimeHazardsSpan(
                    sourceText,
                    filePath,
                    target.SpanStart!.Value,
                    target.SpanEnd!.Value,
                    options.SmtAnalysis!,
                    options.References,
                    cancellationToken,
                    hazardOptions,
                    compilationProfile);
            case SymbolicQueryTargetKind.AllLines:
                return _runtimeHazardQueryService.QuerySourceRuntimeHazards(
                    sourceText,
                    filePath,
                    options.SmtAnalysis!,
                    options.References,
                    cancellationToken,
                    hazardOptions,
                    compilationProfile);
            default:
                throw new NotSupportedException("Target kind is not supported for runtime hazard queries.");
        }
    }

    private SymbolicRuntimeHazardQueryResult QuerySyntaxTreeRuntimeHazards(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        SymbolicRuntimeHazardQueryOptions hazardOptions,
        CancellationToken cancellationToken)
    {
        switch (target.Kind)
        {
            case SymbolicQueryTargetKind.Line:
            case SymbolicQueryTargetKind.Point:
                return _runtimeHazardQueryService.QuerySyntaxTreeRuntimeHazardsLine(
                    syntaxTree,
                    compilation,
                    target.LineNumber!.Value,
                    options.SmtAnalysis!,
                    cancellationToken,
                    hazardOptions);
            case SymbolicQueryTargetKind.Span:
                return _runtimeHazardQueryService.QuerySyntaxTreeRuntimeHazardsSpan(
                    syntaxTree,
                    compilation,
                    target.SpanStart!.Value,
                    target.SpanEnd!.Value,
                    options.SmtAnalysis!,
                    cancellationToken,
                    hazardOptions);
            case SymbolicQueryTargetKind.AllLines:
                return _runtimeHazardQueryService.QuerySyntaxTreeRuntimeHazards(
                    syntaxTree,
                    compilation,
                    options.SmtAnalysis!,
                    cancellationToken,
                    hazardOptions);
            default:
                throw new NotSupportedException("Target kind is not supported for runtime hazard queries.");
        }
    }
}

internal readonly struct SymbolicQueryRequestEnvelope
{
    private SymbolicQueryRequestEnvelope(
        SymbolicSourceInput source,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options)
    {
        Source = source;
        Target = target;
        Options = options;
    }

    internal SymbolicSourceInput Source { get; }

    internal SymbolicQueryTarget Target { get; }

    internal SymbolicQueryOptions Options { get; }

    internal static SymbolicQueryRequestEnvelope Create(
        SymbolicSourceInput source,
        SymbolicQueryTarget target,
        SymbolicQueryOptions? options,
        bool useDefaultOptions)
    {
        return new SymbolicQueryRequestEnvelope(
            source ?? throw new ArgumentNullException(nameof(source)),
            target ?? throw new ArgumentNullException(nameof(target)),
            options ?? (useDefaultOptions
                ? SymbolicQueryOptions.Default
                : throw new ArgumentNullException(nameof(options))));
    }
}

public sealed class SymbolicQueryRequest
{
    private readonly SymbolicQueryRequestEnvelope _request;

    public SymbolicQueryRequest(
        SymbolicSourceInput source,
        SymbolicQueryTarget target,
        SymbolicQueryOptions? options = null)
    {
        _request = SymbolicQueryRequestEnvelope.Create(source, target, options, useDefaultOptions: true);
    }

    public SymbolicSourceInput Source => _request.Source;

    public SymbolicQueryTarget Target => _request.Target;

    public SymbolicQueryOptions Options => _request.Options;
}

public sealed class SymbolicConditionProofRequest
{
    private readonly SymbolicQueryRequestEnvelope _request;

    public SymbolicConditionProofRequest(
        SymbolicSourceInput source,
        SymbolicQueryTarget target,
        string conditionText,
        SymbolicQueryOptions options)
    {
        _request = SymbolicQueryRequestEnvelope.Create(source, target, options, useDefaultOptions: false);
        ConditionText = conditionText ?? throw new ArgumentNullException(nameof(conditionText));
    }

    public SymbolicSourceInput Source => _request.Source;

    public SymbolicQueryTarget Target => _request.Target;

    public string ConditionText { get; }

    public SymbolicQueryOptions Options => _request.Options;
}

public sealed class SymbolicRuntimeHazardRequest
{
    private readonly SymbolicQueryRequestEnvelope _request;

    public SymbolicRuntimeHazardRequest(
        SymbolicSourceInput source,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        SymbolicRuntimeHazardQueryOptions? hazardOptions = null)
    {
        _request = SymbolicQueryRequestEnvelope.Create(source, target, options, useDefaultOptions: false);
        HazardOptions = hazardOptions ?? SymbolicRuntimeHazardQueryOptions.Default;
    }

    public SymbolicSourceInput Source => _request.Source;

    public SymbolicQueryTarget Target => _request.Target;

    public SymbolicQueryOptions Options => _request.Options;

    public SymbolicRuntimeHazardQueryOptions HazardOptions { get; }
}

public sealed class SymbolicComplexityRequest
{
    private readonly SymbolicQueryRequestEnvelope _request;

    public SymbolicComplexityRequest(
        SymbolicSourceInput source,
        SymbolicQueryTarget target,
        SymbolicQueryOptions? options = null)
    {
        _request = SymbolicQueryRequestEnvelope.Create(source, target, options, useDefaultOptions: true);
    }

    public SymbolicSourceInput Source => _request.Source;

    public SymbolicQueryTarget Target => _request.Target;

    public SymbolicQueryOptions Options => _request.Options;
}

public sealed class SymbolicCapabilityRequest
{
    private readonly SymbolicQueryRequestEnvelope _request;

    public SymbolicCapabilityRequest(
        SymbolicSourceInput source,
        SymbolicQueryTarget target,
        SymbolicQueryOptions? options = null)
    {
        _request = SymbolicQueryRequestEnvelope.Create(source, target, options, useDefaultOptions: true);
    }

    public SymbolicSourceInput Source => _request.Source;

    public SymbolicQueryTarget Target => _request.Target;

    public SymbolicQueryOptions Options => _request.Options;
}

public sealed class SymbolicQueryOptions
{
    public static readonly SymbolicQueryOptions Default = new();

    public SymbolicQueryOptions(
        IEnumerable<MetadataReference>? references = null,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicSourceQueryFilter? filter = null)
        : this(
            SymbolicAnalysisLimits.Default,
            references,
            smtAnalysis,
            impliedConditions,
            includeExpressionProgramPoints,
            includeCurrentStatementCompletionFacts,
            filter)
    {
    }

    private SymbolicQueryOptions(
        SymbolicAnalysisLimits analysisLimits,
        IEnumerable<MetadataReference>? references = null,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicSourceQueryFilter? filter = null)
    {
        AnalysisLimits = analysisLimits ?? throw new ArgumentNullException(nameof(analysisLimits));
        References = SymbolicQueryOptionHelpers.NormalizeReferences(references, nameof(references));
        SmtAnalysis = smtAnalysis;
        ImpliedConditions = impliedConditions?
            .Where(static condition => !string.IsNullOrWhiteSpace(condition))
            .Select(static condition => condition.Trim())
            .ToImmutableArray() ?? ImmutableArray<string>.Empty;
        IncludeExpressionProgramPoints = includeExpressionProgramPoints;
        IncludeCurrentStatementCompletionFacts = includeCurrentStatementCompletionFacts;
        Filter = filter;
    }

    public SymbolicAnalysisLimits AnalysisLimits { get; }

    public SymbolicQueryOptions WithAnalysisLimits(SymbolicAnalysisLimits analysisLimits)
    {
        return new SymbolicQueryOptions(
            analysisLimits,
            References,
            SmtAnalysis,
            ImpliedConditions,
            IncludeExpressionProgramPoints,
            IncludeCurrentStatementCompletionFacts,
            Filter);
    }

    public ImmutableArray<MetadataReference> References { get; }

    public SmtAnalysisService? SmtAnalysis { get; }

    public ImmutableArray<string> ImpliedConditions { get; }

    public bool IncludeExpressionProgramPoints { get; }

    public bool IncludeCurrentStatementCompletionFacts { get; }

    public SymbolicSourceQueryFilter? Filter { get; }
}

internal static class SymbolicQueryOptionHelpers
{
    public static ImmutableArray<MetadataReference> NormalizeReferences(
        IEnumerable<MetadataReference>? references,
        string parameterName)
    {
        if (references == null) return ImmutableArray<MetadataReference>.Empty;

        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var reference in references)
        {
            if (reference == null)
                throw new ArgumentException("References cannot contain null entries.", parameterName);

            builder.Add(reference);
        }

        return builder.ToImmutable();
    }
}

public sealed class SymbolicSourceInput
{
    internal const string DefaultFilePath = "SharpProof.Symbolic.Query.cs";

    private SymbolicSourceInput(
        SymbolicSourceInputKind kind,
        string? filePath = null,
        string? sourceText = null,
        SyntaxTree? syntaxTree = null,
        Compilation? compilation = null,
        SyntaxNode? node = null,
        SemanticModel? semanticModel = null,
        SymbolicSourceCompilationProfile? compilationProfile = null,
        SymbolicSourceMap? sourceMap = null)
    {
        Kind = kind;
        FilePath = filePath;
        SourceText = sourceText;
        SyntaxTree = syntaxTree;
        Compilation = compilation;
        Node = node;
        SemanticModel = semanticModel;
        CompilationProfile = compilationProfile;
        SourceMap = sourceMap;
    }

    public SymbolicSourceInputKind Kind { get; }

    public string? FilePath { get; }

    public string? SourceText { get; }

    public SyntaxTree? SyntaxTree { get; }

    public Compilation? Compilation { get; }

    public SyntaxNode? Node { get; }

    public SemanticModel? SemanticModel { get; }

    public SymbolicSourceCompilationProfile? CompilationProfile { get; }

    public SymbolicSourceMap? SourceMap { get; }

    public static SymbolicSourceInput FromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        return FromFile(filePath, SymbolicSourceCompilationProfile.Default);
    }

    public static SymbolicSourceInput FromFile(
        string filePath,
        SymbolicSourceCompilationProfile compilationProfile)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        return new SymbolicSourceInput(
            SymbolicSourceInputKind.File,
            filePath,
            compilationProfile: compilationProfile ??
                                throw new ArgumentNullException(nameof(compilationProfile)));
    }

    public static SymbolicSourceInput FromText(string sourceText, string? filePath = null)
    {
        if (sourceText == null) throw new ArgumentNullException(nameof(sourceText));

        return FromTextWithProfile(sourceText, SymbolicSourceCompilationProfile.Default, filePath);
    }

    public static SymbolicSourceInput FromTextWithProfile(
        string sourceText,
        SymbolicSourceCompilationProfile compilationProfile,
        string? filePath = null)
    {
        if (sourceText == null) throw new ArgumentNullException(nameof(sourceText));

        return new SymbolicSourceInput(
            SymbolicSourceInputKind.Text,
            string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath,
            sourceText,
            compilationProfile: compilationProfile ??
                                throw new ArgumentNullException(nameof(compilationProfile)));
    }

    public static SymbolicSourceInput FromSyntaxTree(SyntaxTree syntaxTree, Compilation compilation)
    {
        return new SymbolicSourceInput(
            SymbolicSourceInputKind.SyntaxTree,
            syntaxTree?.FilePath,
            syntaxTree: syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree)),
            compilation: compilation ?? throw new ArgumentNullException(nameof(compilation)));
    }

    public static SymbolicSourceInput FromNode(SyntaxNode node, SemanticModel semanticModel)
    {
        return new SymbolicSourceInput(
            SymbolicSourceInputKind.Node,
            node?.SyntaxTree.FilePath,
            node: node ?? throw new ArgumentNullException(nameof(node)),
            semanticModel: semanticModel ?? throw new ArgumentNullException(nameof(semanticModel)));
    }

    public SymbolicSourceInput WithSourceMap(SymbolicSourceMap sourceMap)
    {
        return new SymbolicSourceInput(
            Kind,
            FilePath,
            SourceText,
            SyntaxTree,
            Compilation,
            Node,
            SemanticModel,
            CompilationProfile,
            sourceMap ?? throw new ArgumentNullException(nameof(sourceMap)));
    }
}

public enum SymbolicSourceInputKind
{
    File,
    Text,
    SyntaxTree,
    Node
}

public sealed class SymbolicQueryTarget
{
    private SymbolicQueryTarget(
        SymbolicQueryTargetKind kind,
        int? line = null,
        int? column = null,
        int? position = null,
        int? spanStart = null,
        int? spanEnd = null,
        int? startLine = null,
        int? startColumn = null,
        int? endLine = null,
        int? endColumn = null,
        bool includeNestedCallables = false)
    {
        Kind = kind;
        LineNumber = line;
        ColumnNumber = column;
        PositionOffset = position;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        IncludeNestedCallables = includeNestedCallables;
    }

    public SymbolicQueryTargetKind Kind { get; }

    public int? LineNumber { get; }

    public int? ColumnNumber { get; }

    public int? PositionOffset { get; }

    public int? SpanStart { get; }

    public int? SpanEnd { get; }

    public int? StartLine { get; }

    public int? StartColumn { get; }

    public int? EndLine { get; }

    public int? EndColumn { get; }

    public bool IncludeNestedCallables { get; }

    public static SymbolicQueryTarget Point(int line, int column = 1)
    {
        ValidatePositive(line, nameof(line));
        ValidatePositive(column, nameof(column));
        return new SymbolicQueryTarget(SymbolicQueryTargetKind.Point, line, column);
    }

    public static SymbolicQueryTarget Position(int position)
    {
        ValidateNonNegative(position, nameof(position));
        return new SymbolicQueryTarget(SymbolicQueryTargetKind.Position, position: position);
    }

    public static SymbolicQueryTarget Line(int line)
    {
        ValidatePositive(line, nameof(line));
        return new SymbolicQueryTarget(SymbolicQueryTargetKind.Line, line);
    }

    public static SymbolicQueryTarget Span(int spanStart, int spanEnd)
    {
        ValidateNonNegative(spanStart, nameof(spanStart));
        if (spanEnd < spanStart)
            throw new ArgumentOutOfRangeException(nameof(spanEnd), "Span end cannot be less than span start.");

        return new SymbolicQueryTarget(SymbolicQueryTargetKind.Span, spanStart: spanStart, spanEnd: spanEnd);
    }

    public static SymbolicQueryTarget LineSpan(int startLine, int startColumn, int endLine, int endColumn)
    {
        ValidatePositive(startLine, nameof(startLine));
        ValidatePositive(startColumn, nameof(startColumn));
        ValidatePositive(endLine, nameof(endLine));
        ValidatePositive(endColumn, nameof(endColumn));
        if (endLine < startLine)
            throw new ArgumentOutOfRangeException(nameof(endLine), "End line cannot be before start line.");

        if (endLine == startLine && endColumn < startColumn)
            throw new ArgumentOutOfRangeException(nameof(endColumn),
                "End column cannot be before start column on the same line.");

        return new SymbolicQueryTarget(
            SymbolicQueryTargetKind.LineSpan,
            startLine: startLine,
            startColumn: startColumn,
            endLine: endLine,
            endColumn: endColumn);
    }

    public static SymbolicQueryTarget AllLines()
    {
        return new SymbolicQueryTarget(SymbolicQueryTargetKind.AllLines);
    }

    public static SymbolicQueryTarget Node(bool includeNestedCallables = false)
    {
        return new SymbolicQueryTarget(
            SymbolicQueryTargetKind.Node,
            includeNestedCallables: includeNestedCallables);
    }

    private static void ValidatePositive(int value, string paramName)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(paramName, "Value must be positive.");
    }

    private static void ValidateNonNegative(int value, string paramName)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(paramName, "Value cannot be negative.");
    }
}

public enum SymbolicQueryTargetKind
{
    Point,
    Position,
    Line,
    Span,
    LineSpan,
    AllLines,
    Node
}

public sealed class SymbolicQueryResult
{
    private SymbolicQueryResult(
        string scopeKind,
        object innerResult,
        IReadOnlyList<SymbolicSourceQueryResult> programPoints,
        SymbolicInvariantResult observedInvariant,
        SymbolicInvariantResult mergedInvariant,
        SymbolicMergedPathFacts mergedPathFacts,
        SymbolicProgramPointSummary programPointSummary,
        SymbolicReachabilitySummary reachability,
        IReadOnlyList<SymbolicConditionProofSummary> conditionProofs,
        SymbolicSmtDiagnostics smtDiagnostics,
        SymbolicInvariantQueryView invariantQuery,
        string filePath,
        int? line = null,
        int? column = null,
        int? position = null,
        int? spanStart = null,
        int? spanEnd = null,
        int? lineCount = null)
    {
        ScopeKind = scopeKind ?? throw new ArgumentNullException(nameof(scopeKind));
        InnerResult = innerResult ?? throw new ArgumentNullException(nameof(innerResult));
        ProgramPoints = programPoints ?? throw new ArgumentNullException(nameof(programPoints));
        AnalysisTruncation = SymbolicAnalysisTruncationInfo.Combine(
            ProgramPoints.Select(static point => point.AnalysisTruncation));
        ObservedInvariant = observedInvariant ?? throw new ArgumentNullException(nameof(observedInvariant));
        MergedInvariant = mergedInvariant ?? throw new ArgumentNullException(nameof(mergedInvariant));
        MergedPathFacts = mergedPathFacts ?? throw new ArgumentNullException(nameof(mergedPathFacts));
        ProgramPointSummary = programPointSummary ?? throw new ArgumentNullException(nameof(programPointSummary));
        Reachability = reachability ?? throw new ArgumentNullException(nameof(reachability));
        ConditionProofs = conditionProofs ?? throw new ArgumentNullException(nameof(conditionProofs));
        SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        InvariantQuery = invariantQuery ?? throw new ArgumentNullException(nameof(invariantQuery));
        InvariantInfo = new SymbolicInvariantInfo(
            MergedInvariant.MergedInvariantText,
            SymbolicFactInfo.Distinct(ProgramPoints.SelectMany(static point => point.SymbolicFacts)),
            ConditionProofs.Select(static proof => proof.Proof).ToArray(),
            MergedInvariant.MergeKind,
            MergedInvariant.ConditionCount);
        FilePath = filePath ?? string.Empty;
        Line = line;
        Column = column;
        Position = position;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        LineCount = lineCount;
        ReachabilityWitnesses = ProgramPoints.Select(static point => point.ReachabilityWitness).ToArray();
        InputDomainSummary = SymbolicInputWitnessFactory.MergeAlternatives(ReachabilityWitnesses);
    }

    public string ScopeKind { get; }

    public string FilePath { get; }

    public int? Line { get; }

    public int? Column { get; }

    public int? Position { get; }

    public int? SpanStart { get; }

    public int? SpanEnd { get; }

    public int? LineCount { get; }

    public IReadOnlyList<SymbolicSourceQueryResult> ProgramPoints { get; }

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; }

    public int ProgramPointCount => ProgramPoints.Count;

    public SymbolicInvariantResult ObservedInvariant { get; }

    internal SymbolicInvariantResult MergedInvariant { get; }

    public SymbolicInvariantInfo InvariantInfo { get; }

    public SymbolicMergedPathFacts MergedPathFacts { get; }

    public SymbolicProgramPointSummary ProgramPointSummary { get; }

    public SymbolicReachabilitySummary Reachability { get; }

    public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs { get; }

    public SymbolicSmtDiagnostics SmtDiagnostics { get; }

    public SymbolicInvariantQueryView InvariantQuery { get; }

    public IReadOnlyList<SymbolicInputWitness> ReachabilityWitnesses { get; }

    public SymbolicInputDomainSummary InputDomainSummary { get; }

    internal object InnerResult { get; }

    public SymbolicQueryResult Filter(SymbolicSourceQueryFilter filter)
    {
        if (filter == null) throw new ArgumentNullException(nameof(filter));

        return From(InnerResult switch
        {
            SymbolicFileQueryResult fileResult => fileResult.Filter(filter),
            SymbolicLineQueryResult lineResult => lineResult.Filter(filter),
            SymbolicSpanQueryResult spanResult => spanResult.Filter(filter),
            SymbolicSourceQueryResult pointResult when filter.Matches(pointResult) => pointResult,
            SymbolicSourceQueryResult pointResult => new SymbolicLineQueryResult(
                pointResult.FilePath,
                pointResult.Line,
                Array.Empty<SymbolicSourceQueryResult>(),
                pointResult.SmtDiagnostics),
            _ => throw new InvalidOperationException("Unexpected symbolic query result type.")
        });
    }

    public SymbolicCompactQueryResult ToCompactResult(SymbolicCompactQueryOptions? options = null)
    {
        return InnerResult switch
        {
            SymbolicFileQueryResult fileResult => fileResult.ToCompactResult(options),
            SymbolicLineQueryResult lineResult => lineResult.ToCompactResult(options),
            SymbolicSpanQueryResult spanResult => spanResult.ToCompactResult(options),
            SymbolicSourceQueryResult pointResult => pointResult.ToCompactResult(options),
            _ => throw new InvalidOperationException("Unexpected symbolic query result type.")
        };
    }

    public SymbolicInvariantQueryResult ToInvariantQueryResult(SymbolicCompactQueryOptions? options = null)
    {
        return InnerResult switch
        {
            SymbolicFileQueryResult fileResult => fileResult.ToInvariantQueryResult(options),
            SymbolicLineQueryResult lineResult => lineResult.ToInvariantQueryResult(options),
            SymbolicSpanQueryResult spanResult => spanResult.ToInvariantQueryResult(options),
            SymbolicSourceQueryResult pointResult => pointResult.ToInvariantQueryResult(options),
            _ => throw new InvalidOperationException("Unexpected symbolic query result type.")
        };
    }

    internal TInner GetInnerResult<TInner>()
        where TInner : class
    {
        return InnerResult as TInner ??
               throw new InvalidOperationException("Unexpected symbolic query result type.");
    }

    internal static SymbolicQueryResult From(object result)
    {
        switch (result)
        {
            case SymbolicFileQueryResult file:
                return new SymbolicQueryResult(
                    "file",
                    file,
                    file.Lines.SelectMany(static line => line.ProgramPoints).ToArray(),
                    file.ObservedInvariant,
                    file.MergedInvariant,
                    file.MergedPathFacts,
                    file.ProgramPointSummary,
                    file.Reachability,
                    file.ConditionProofs,
                    file.SmtDiagnostics,
                    file.InvariantQuery,
                    file.FilePath,
                    lineCount: file.LineCount);
            case SymbolicLineQueryResult line:
                return new SymbolicQueryResult(
                    "line",
                    line,
                    line.ProgramPoints,
                    line.ObservedInvariant,
                    line.MergedInvariant,
                    line.MergedPathFacts,
                    line.ProgramPointSummary,
                    line.Reachability,
                    line.ConditionProofs,
                    line.SmtDiagnostics,
                    line.InvariantQuery,
                    line.FilePath,
                    line.Line);
            case SymbolicSpanQueryResult span:
                return new SymbolicQueryResult(
                    "span",
                    span,
                    span.ProgramPoints,
                    span.ObservedInvariant,
                    span.MergedInvariant,
                    span.MergedPathFacts,
                    span.ProgramPointSummary,
                    span.Reachability,
                    span.ConditionProofs,
                    span.SmtDiagnostics,
                    span.InvariantQuery,
                    span.FilePath,
                    spanStart: span.SpanStart,
                    spanEnd: span.SpanEnd);
            case SymbolicSourceQueryResult point:
                return new SymbolicQueryResult(
                    "point",
                    point,
                    new[] { point },
                    point.Invariant,
                    point.Invariant,
                    SymbolicMergedPathFacts.FromProgramPoints(new[] { point }),
                    SymbolicProgramPointSummary.FromProgramPoints(new[] { point }),
                    SymbolicReachabilitySummary.FromProgramPoints(new[] { point }),
                    SymbolicConditionProofSummary.FromProgramPoints(new[] { point }),
                    point.SmtDiagnostics,
                    point.InvariantQuery,
                    point.FilePath,
                    point.Line,
                    point.Column,
                    point.Position);
            default:
                throw new InvalidOperationException("Unexpected symbolic query result type.");
        }
    }
}
