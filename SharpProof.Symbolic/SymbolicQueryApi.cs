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
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var options = context.Options;
        using var limitScope = SymbolicAnalysisLimitContext.Push(options.AnalysisLimits);
        var result = QueryCore(context.Source, context.Target, options, cancellationToken);
        return options.Filter == null || options.Filter.IsEmpty
            ? result
            : result.Filter(options.Filter);
    }

    public SymbolicOperationResult<SymbolicQueryResult> TryQuery(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => Query(context, cancellationToken));
    }

    public SymbolicConditionProofResult Prove(
        SymbolicQueryContext context,
        string conditionText,
        CancellationToken cancellationToken = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        if (string.IsNullOrWhiteSpace(conditionText))
            throw new ArgumentException("Condition text is required.", nameof(conditionText));

        var pointTarget = context.Target.Kind == SymbolicQueryTargetKind.Point
            ? context.Target
            : throw new ArgumentException("Condition proof requests require a point target.", nameof(context));
        var options = context.Options;
        if (options.SmtAnalysis == null)
            throw new ArgumentException("Condition proof requests require SMT analysis.", nameof(context));

        using var limitScope = SymbolicAnalysisLimitContext.Push(options.AnalysisLimits);
        var source = context.Source;
        switch (source.Kind)
        {
            case SymbolicSourceInputKind.File:
                return _sourceQueryService.ProveConditionAtFile(
                    source.FilePath!,
                    pointTarget.LineNumber!.Value,
                    pointTarget.ColumnNumber ?? 1,
                    conditionText,
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
                    conditionText,
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
                    conditionText,
                    options.SmtAnalysis,
                    cancellationToken);
            default:
                throw new NotSupportedException("Condition proof source kind is not supported.");
        }
    }

    public SymbolicOperationResult<SymbolicConditionProofResult> TryProve(
        SymbolicQueryContext context,
        string conditionText,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => Prove(context, conditionText, cancellationToken));
    }

    internal SymbolicConditionProofResult ProveAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
    {
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
        SymbolicQueryContext context,
        SymbolicRuntimeHazardQueryOptions? hazardOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var options = context.Options;
        if (options.SmtAnalysis == null)
            throw new ArgumentException("Runtime hazard queries require SMT analysis.", nameof(context));

        using var limitScope = SymbolicAnalysisLimitContext.Push(options.AnalysisLimits);
        hazardOptions ??= SymbolicRuntimeHazardQueryOptions.Default;
        var source = context.Source;
        var target = context.Target;
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
        SymbolicQueryContext context,
        SymbolicRuntimeHazardQueryOptions? hazardOptions = null,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => QueryRuntimeHazards(context, hazardOptions, cancellationToken));
    }

    public SymbolicComplexityResult QueryComplexity(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var options = context.Options;
        using var limitScope = SymbolicAnalysisLimitContext.Push(options.AnalysisLimits);
        return _complexityService.Query(
            context.Source,
            context.Target,
            options,
            cancellationToken);
    }

    public SymbolicOperationResult<SymbolicComplexityResult> TryQueryComplexity(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => QueryComplexity(context, cancellationToken));
    }

    public SymbolicCapabilityResult QueryCapabilities(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var options = context.Options;
        using var limitScope = SymbolicAnalysisLimitContext.Push(options.AnalysisLimits);
        return _capabilityService.Query(
            context.Source,
            context.Target,
            options,
            cancellationToken);
    }

    public SymbolicOperationResult<SymbolicCapabilityResult> TryQueryCapabilities(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => QueryCapabilities(context, cancellationToken));
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
                return QueryFile(
                    source.FilePath!,
                    source.CompilationProfile,
                    target,
                    options,
                    cancellationToken);
            case SymbolicSourceInputKind.Text:
                return QuerySource(source.SourceText!,
                    source.FilePath ?? SymbolicSourceInput.DefaultFilePath, source.CompilationProfile, target, options,
                    cancellationToken);
            case SymbolicSourceInputKind.SyntaxTree:
                return QuerySyntaxTree(source.SyntaxTree!, source.Compilation!, target,
                    options, cancellationToken);
            case SymbolicSourceInputKind.Node:
                return QueryNode(source.Node!, source.SemanticModel!, target, options,
                    cancellationToken);
            default:
                throw new NotSupportedException("Source kind is not supported.");
        }
    }

    private SymbolicQueryResult QueryFile(
        string filePath,
        SymbolicSourceCompilationProfile? compilationProfile,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        switch (target.Kind)
        {
            case SymbolicQueryTargetKind.Point:
                return SymbolicQueryResult.From(_sourceQueryService.QueryFileLinePoint(
                    filePath,
                    target.LineNumber!.Value,
                    target.ColumnNumber ?? 1,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile));
            case SymbolicQueryTargetKind.Position:
                return SymbolicQueryResult.From(_sourceQueryService.QueryFileAtPosition(
                    filePath,
                    target.PositionOffset!.Value,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    compilationProfile));
            case SymbolicQueryTargetKind.Line:
                return SymbolicQueryResult.From(_sourceQueryService.QueryFileLine(
                    filePath,
                    target.LineNumber!.Value,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile));
            case SymbolicQueryTargetKind.Span:
                return SymbolicQueryResult.From(_sourceQueryService.QueryFileSpan(
                    filePath,
                    target.SpanStart!.Value,
                    target.SpanEnd!.Value,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile));
            case SymbolicQueryTargetKind.LineSpan:
                return SymbolicQueryResult.From(_sourceQueryService.QueryFileLineSpan(
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
                    compilationProfile));
            case SymbolicQueryTargetKind.AllLines:
                return SymbolicQueryResult.From(_sourceQueryService.QueryFileAllLines(
                    filePath,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile));
            default:
                throw new NotSupportedException("Target kind is not supported for file queries.");
        }
    }

    private SymbolicQueryResult QuerySource(
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
                return SymbolicQueryResult.From(_sourceQueryService.QuerySourceLinePoint(
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
                    compilationProfile));
            case SymbolicQueryTargetKind.Position:
                return SymbolicQueryResult.From(_sourceQueryService.QuerySourceAtPosition(
                    sourceText,
                    filePath,
                    target.PositionOffset!.Value,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    compilationProfile));
            case SymbolicQueryTargetKind.Line:
                return SymbolicQueryResult.From(_sourceQueryService.QuerySourceLine(
                    sourceText,
                    filePath,
                    target.LineNumber!.Value,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile));
            case SymbolicQueryTargetKind.Span:
                return SymbolicQueryResult.From(_sourceQueryService.QuerySourceSpan(
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
                    compilationProfile));
            case SymbolicQueryTargetKind.LineSpan:
                return SymbolicQueryResult.From(_sourceQueryService.QuerySourceLineSpan(
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
                    compilationProfile));
            case SymbolicQueryTargetKind.AllLines:
                return SymbolicQueryResult.From(_sourceQueryService.QuerySourceAllLines(
                    sourceText,
                    filePath,
                    options.References,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts,
                    compilationProfile));
            default:
                throw new NotSupportedException("Target kind is not supported for source queries.");
        }
    }

    private SymbolicQueryResult QuerySyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        switch (target.Kind)
        {
            case SymbolicQueryTargetKind.Point:
                return SymbolicQueryResult.From(_sourceQueryService.QuerySyntaxTreeLinePoint(
                    syntaxTree,
                    compilation,
                    target.LineNumber!.Value,
                    target.ColumnNumber ?? 1,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts));
            case SymbolicQueryTargetKind.Position:
                return SymbolicQueryResult.From(_sourceQueryService.QuerySyntaxTreeAtPosition(
                    syntaxTree,
                    compilation,
                    target.PositionOffset!.Value,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions));
            case SymbolicQueryTargetKind.Line:
                return SymbolicQueryResult.From(_sourceQueryService.QuerySyntaxTreeLine(
                    syntaxTree,
                    compilation,
                    target.LineNumber!.Value,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts));
            case SymbolicQueryTargetKind.Span:
                return SymbolicQueryResult.From(_sourceQueryService.QuerySyntaxTreeSpan(
                    syntaxTree,
                    compilation,
                    target.SpanStart!.Value,
                    target.SpanEnd!.Value,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts));
            case SymbolicQueryTargetKind.LineSpan:
                return SymbolicQueryResult.From(_sourceQueryService.QuerySyntaxTreeLineSpan(
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
                    options.IncludeCurrentStatementCompletionFacts));
            case SymbolicQueryTargetKind.AllLines:
                return SymbolicQueryResult.From(_sourceQueryService.QuerySyntaxTreeAllLines(
                    syntaxTree,
                    compilation,
                    cancellationToken,
                    options.SmtAnalysis,
                    options.ImpliedConditions,
                    options.IncludeExpressionProgramPoints,
                    options.IncludeCurrentStatementCompletionFacts));
            default:
                throw new NotSupportedException("Target kind is not supported for syntax tree queries.");
        }
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
        return SymbolicQueryResult.From(new SymbolicProgramPointResult(
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
                analysis.ReachabilityReason)));
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

public sealed class SymbolicQueryContext
{
    public SymbolicQueryContext(
        SymbolicSourceInput source,
        SymbolicQueryTarget target,
        SymbolicQueryOptions? options = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Options = options ?? SymbolicQueryOptions.Default;
    }

    public SymbolicSourceInput Source { get; }

    public SymbolicQueryTarget Target { get; }

    public SymbolicQueryOptions Options { get; }
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

public enum SymbolicQueryScopeKind
{
    Point,
    Line,
    Span,
    File
}

public sealed class SymbolicQueryScope
{
    internal SymbolicQueryScope(
        SymbolicQueryScopeKind kind,
        string filePath,
        int? line = null,
        int? column = null,
        int? position = null,
        int? spanStart = null,
        int? spanEnd = null,
        int? lineCount = null)
    {
        Kind = kind;
        FilePath = filePath ?? string.Empty;
        Line = line;
        Column = column;
        Position = position;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        LineCount = lineCount;
    }

    public SymbolicQueryScopeKind Kind { get; }

    public string FilePath { get; }

    public int? Line { get; }

    public int? Column { get; }

    public int? Position { get; }

    public int? SpanStart { get; }

    public int? SpanEnd { get; }

    public int? LineCount { get; }
}

public sealed class SymbolicQueryResult
{
    private readonly SymbolicFileQueryResult? _fileResult;
    private readonly SymbolicLineQueryResult? _lineResult;
    private readonly SymbolicProgramPointResult? _pointResult;
    private readonly SymbolicSpanQueryResult? _spanResult;

    private SymbolicQueryResult(
        SymbolicQueryScope scope,
        IReadOnlyList<SymbolicProgramPointResult> programPoints,
        SymbolicInvariantResult observedInvariant,
        SymbolicInvariantResult mergedInvariant,
        SymbolicMergedPathFacts mergedPathFacts,
        SymbolicProgramPointSummary programPointSummary,
        SymbolicReachabilitySummary reachability,
        IReadOnlyList<SymbolicConditionProofSummary> conditionProofs,
        SymbolicSmtDiagnostics smtDiagnostics,
        SymbolicInvariantQueryView invariantQuery,
        SymbolicFileQueryResult? fileResult = null,
        SymbolicLineQueryResult? lineResult = null,
        SymbolicSpanQueryResult? spanResult = null,
        SymbolicProgramPointResult? pointResult = null)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
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
        _fileResult = fileResult;
        _lineResult = lineResult;
        _spanResult = spanResult;
        _pointResult = pointResult;
        InvariantInfo = new SymbolicInvariantInfo(
            MergedInvariant.MergedInvariantText,
            SymbolicFactInfo.Distinct(ProgramPoints.SelectMany(static point => point.SymbolicFacts)),
            ConditionProofs.Select(static proof => proof.Proof).ToArray(),
            MergedInvariant.MergeKind,
            MergedInvariant.ConditionCount);
        ReachabilityWitnesses = ProgramPoints.Select(static point => point.ReachabilityWitness).ToArray();
        InputDomainSummary = SymbolicInputWitnessFactory.MergeAlternatives(ReachabilityWitnesses);
    }

    public SymbolicQueryScope Scope { get; }

    public string ScopeKind => Scope.Kind.ToString().ToLowerInvariant();

    public string FilePath => Scope.FilePath;

    public int? Line => Scope.Line;

    public int? Column => Scope.Column;

    public int? Position => Scope.Position;

    public int? SpanStart => Scope.SpanStart;

    public int? SpanEnd => Scope.SpanEnd;

    public int? LineCount => Scope.LineCount;

    public IReadOnlyList<SymbolicProgramPointResult> ProgramPoints { get; }

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

    internal SymbolicFileQueryResult? FileResult => _fileResult;

    internal SymbolicLineQueryResult? LineResult => _lineResult;

    internal SymbolicSpanQueryResult? SpanResult => _spanResult;

    internal SymbolicProgramPointResult? PointResult => _pointResult;

    public SymbolicQueryResult Filter(SymbolicSourceQueryFilter filter)
    {
        if (filter == null) throw new ArgumentNullException(nameof(filter));

        if (_fileResult != null) return From(_fileResult.Filter(filter));

        if (_lineResult != null) return From(_lineResult.Filter(filter));

        if (_spanResult != null) return From(_spanResult.Filter(filter));

        if (_pointResult == null)
            throw new InvalidOperationException("Symbolic query result has no typed scope result.");

        return filter.Matches(_pointResult)
            ? From(_pointResult)
            : From(new SymbolicLineQueryResult(
                _pointResult.FilePath,
                _pointResult.Line,
                Array.Empty<SymbolicProgramPointResult>(),
                _pointResult.SmtDiagnostics));
    }

    public SymbolicCompactQueryResult ToCompactResult(SymbolicCompactQueryOptions? options = null)
    {
        if (_fileResult != null) return _fileResult.ToCompactResult(options);

        if (_lineResult != null) return _lineResult.ToCompactResult(options);

        if (_spanResult != null) return _spanResult.ToCompactResult(options);

        if (_pointResult != null) return _pointResult.ToCompactResult(options);

        throw new InvalidOperationException("Symbolic query result has no typed scope result.");
    }

    public SymbolicInvariantQueryResult ToInvariantQueryResult(SymbolicCompactQueryOptions? options = null)
    {
        if (_fileResult != null) return _fileResult.ToInvariantQueryResult(options);

        if (_lineResult != null) return _lineResult.ToInvariantQueryResult(options);

        if (_spanResult != null) return _spanResult.ToInvariantQueryResult(options);

        if (_pointResult != null) return _pointResult.ToInvariantQueryResult(options);

        throw new InvalidOperationException("Symbolic query result has no typed scope result.");
    }

    internal static SymbolicQueryResult From(SymbolicFileQueryResult file)
    {
        if (file == null) throw new ArgumentNullException(nameof(file));

        return new SymbolicQueryResult(
            new SymbolicQueryScope(
                SymbolicQueryScopeKind.File,
                file.FilePath,
                lineCount: file.LineCount),
            file.Lines.SelectMany(static line => line.ProgramPoints).ToArray(),
            file.ObservedInvariant,
            file.MergedInvariant,
            file.MergedPathFacts,
            file.ProgramPointSummary,
            file.Reachability,
            file.ConditionProofs,
            file.SmtDiagnostics,
            file.InvariantQuery,
            fileResult: file);
    }

    internal static SymbolicQueryResult From(SymbolicLineQueryResult line)
    {
        if (line == null) throw new ArgumentNullException(nameof(line));

        return new SymbolicQueryResult(
            new SymbolicQueryScope(SymbolicQueryScopeKind.Line, line.FilePath, line.Line),
            line.ProgramPoints,
            line.ObservedInvariant,
            line.MergedInvariant,
            line.MergedPathFacts,
            line.ProgramPointSummary,
            line.Reachability,
            line.ConditionProofs,
            line.SmtDiagnostics,
            line.InvariantQuery,
            lineResult: line);
    }

    internal static SymbolicQueryResult From(SymbolicSpanQueryResult span)
    {
        if (span == null) throw new ArgumentNullException(nameof(span));

        return new SymbolicQueryResult(
            new SymbolicQueryScope(
                SymbolicQueryScopeKind.Span,
                span.FilePath,
                spanStart: span.SpanStart,
                spanEnd: span.SpanEnd),
            span.ProgramPoints,
            span.ObservedInvariant,
            span.MergedInvariant,
            span.MergedPathFacts,
            span.ProgramPointSummary,
            span.Reachability,
            span.ConditionProofs,
            span.SmtDiagnostics,
            span.InvariantQuery,
            spanResult: span);
    }

    internal static SymbolicQueryResult From(SymbolicProgramPointResult point)
    {
        if (point == null) throw new ArgumentNullException(nameof(point));

        return new SymbolicQueryResult(
            new SymbolicQueryScope(
                SymbolicQueryScopeKind.Point,
                point.FilePath,
                point.Line,
                point.Column,
                point.Position),
            new[] { point },
            point.Invariant,
            point.Invariant,
            SymbolicMergedPathFacts.FromProgramPoints(new[] { point }),
            SymbolicProgramPointSummary.FromProgramPoints(new[] { point }),
            SymbolicReachabilitySummary.FromProgramPoints(new[] { point }),
            SymbolicConditionProofSummary.FromProgramPoints(new[] { point }),
            point.SmtDiagnostics,
            point.InvariantQuery,
            pointResult: point);
    }
}
