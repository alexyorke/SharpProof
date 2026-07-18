using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicQueryExecutor
{
    private readonly SymbolicCapabilityService _capabilityService;
    private readonly SymbolicComplexityService _complexityService;
    private readonly SymbolicInvariantService _invariantService;
    private readonly SymbolicRuntimeHazardQueryService _runtimeHazardQueryService;
    private readonly SymbolicSourceQueryService _sourceQueryService;

    internal SymbolicQueryExecutor()
        : this(new SymbolicInvariantService())
    {
    }

    internal SymbolicQueryExecutor(SymbolicInvariantService invariantService)
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
        return ExecuteWithLimits(context, cancellationToken, (request, token) =>
        {
            var result = QueryCore(request, token);
            return request.Options.Filter == null || request.Options.Filter.IsEmpty
                ? result
                : result.Filter(request.Options.Filter);
        });
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
        var validatedRequest = ValidatedSymbolicQueryRequest.Create(context);
        if (string.IsNullOrWhiteSpace(conditionText))
            throw new ArgumentException("Condition text is required.", nameof(conditionText));

        return ExecuteWithLimits(validatedRequest, cancellationToken, (request, token) =>
        {
            request.RequireTarget(
                static kind => kind == SymbolicQueryTargetKind.Point,
                "Condition proof requests require a point target.");
            var smtAnalysis = request.RequireSmt("Condition proof requests require SMT analysis.");
            return SymbolicSourceInputDispatcher.Execute(
                request.Source,
                request.Target,
                request.Options,
                SymbolicSourceCompilationKind.Query,
                "Condition proof source kind is not supported.",
                (syntaxTree, compilation, target, queryToken) => _sourceQueryService.ProveConditionAtSyntaxTree(
                    syntaxTree,
                    compilation,
                    target.LineNumber!.Value,
                    target.ColumnNumber ?? 1,
                    conditionText,
                    smtAnalysis,
                    queryToken),
                static (_, _, _, _) =>
                    throw new NotSupportedException("Condition proof source kind is not supported."),
                token);
        });
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
        hazardOptions ??= SymbolicRuntimeHazardQueryOptions.Default;
        return ExecuteWithLimits(context, cancellationToken, (request, token) =>
        {
            var smtAnalysis = request.RequireSmt("Runtime hazard queries require SMT analysis.");
            if (!SupportsRuntimeHazardTarget(request.Target.Kind) &&
                request.Source.Kind != SymbolicSourceInputKind.Node)
                throw new NotSupportedException("Target kind is not supported for runtime hazard queries.");

            return SymbolicSourceInputDispatcher.Execute(
                request.Source,
                request.Target,
                request.Options,
                SymbolicSourceCompilationKind.RuntimeHazards,
                "Runtime hazard source kind is not supported.",
                (syntaxTree, compilation, dispatchedTarget, queryToken) => QuerySyntaxTreeRuntimeHazards(
                    syntaxTree, compilation, dispatchedTarget, request.Options, hazardOptions, queryToken),
                (node, semanticModel, dispatchedTarget, queryToken) =>
                    _runtimeHazardQueryService.QueryNodeRuntimeHazards(
                        node,
                        semanticModel,
                        smtAnalysis,
                        queryToken,
                        hazardOptions,
                        dispatchedTarget.IncludeNestedCallables),
                token);
        });
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
        return ExecuteWithLimits(context, cancellationToken, (request, token) =>
            _complexityService.Query(request.Source, request.Target, request.Options, token));
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
        return ExecuteWithLimits(context, cancellationToken, (request, token) =>
            _capabilityService.Query(request.Source, request.Target, request.Options, token));
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

    private static TResult ExecuteWithLimits<TResult>(
        SymbolicQueryContext context,
        CancellationToken cancellationToken,
        Func<ValidatedSymbolicQueryRequest, CancellationToken, TResult> operation)
    {
        return ExecuteWithLimits(
            ValidatedSymbolicQueryRequest.Create(context),
            cancellationToken,
            operation);
    }

    private static TResult ExecuteWithLimits<TResult>(
        ValidatedSymbolicQueryRequest request,
        CancellationToken cancellationToken,
        Func<ValidatedSymbolicQueryRequest, CancellationToken, TResult> operation)
    {
        using var limitScope = SymbolicAnalysisLimitContext.Push(request.Options.AnalysisLimits);
        return operation(request, cancellationToken);
    }

    private SymbolicQueryResult QueryCore(
        ValidatedSymbolicQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (!SupportsScopedQueryTarget(request.Target.Kind) &&
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
            (syntaxTree, compilation, queryTarget, token) =>
                QuerySyntaxTree(syntaxTree, compilation, queryTarget, request.Options, token),
            (node, semanticModel, queryTarget, token) =>
                QueryNode(node, semanticModel, queryTarget, request.Options, token),
            cancellationToken);
    }

    private static bool SupportsScopedQueryTarget(SymbolicQueryTargetKind kind)
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

    private static bool SupportsRuntimeHazardTarget(SymbolicQueryTargetKind kind)
    {
        return kind is SymbolicQueryTargetKind.Line or SymbolicQueryTargetKind.Point or
            SymbolicQueryTargetKind.Span or SymbolicQueryTargetKind.AllLines;
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
        int? lineCount = null,
        int? startLine = null,
        int? startColumn = null,
        int? endLine = null,
        int? endColumn = null)
    {
        Kind = kind;
        FilePath = filePath ?? string.Empty;
        Line = line;
        Column = column;
        Position = position;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        LineCount = lineCount;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    public SymbolicQueryScopeKind Kind { get; }

    public string FilePath { get; }

    public int? Line { get; }

    public int? Column { get; }

    public int? Position { get; }

    public int? SpanStart { get; }

    public int? SpanEnd { get; }

    public int? LineCount { get; }

    internal int? StartLine { get; }

    internal int? StartColumn { get; }

    internal int? EndLine { get; }

    internal int? EndColumn { get; }
}

public sealed class SymbolicQueryResult
{
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
        IReadOnlyList<SymbolicQueryLineGroup>? lineGroups = null)
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
        LineGroups = lineGroups ?? Array.Empty<SymbolicQueryLineGroup>();
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

    public IReadOnlyList<SymbolicInputWitness> ReachabilityWitnesses { get; }

    public SymbolicInputDomainSummary InputDomainSummary { get; }

    internal IReadOnlyList<SymbolicQueryLineGroup> LineGroups { get; }

    internal IReadOnlyList<string> Facts =>
        ObservedInvariant.Conditions.Select(static condition => condition.Text).ToArray();

    internal IReadOnlyList<string> ObservedFacts => Facts;

    internal int ObservedFactCount => ObservedInvariant.ConditionCount;

    internal string MergedInvariantText => MergedPathFacts.MergedInvariantText;

    internal int? StartLine => Scope.StartLine;

    internal int? StartColumn => Scope.StartColumn;

    internal int? EndLine => Scope.EndLine;

    internal int? EndColumn => Scope.EndColumn;

    internal IReadOnlyList<SymbolicFactInfo> SymbolicFacts => InvariantInfo.Facts;

    internal IReadOnlyList<SymbolicQueryResult> Lines => LineGroups
        .Select(group => FromLine(FilePath, group.Line, group.ProgramPoints, SmtDiagnostics))
        .ToArray();

    internal int LinesWithProgramPoints => Scope.Kind switch
    {
        SymbolicQueryScopeKind.File => LineGroups.Count,
        SymbolicQueryScopeKind.Span => ProgramPoints.Select(static point => point.Line).Distinct().Count(),
        _ => ProgramPointCount == 0 ? 0 : 1
    };

    public SymbolicQueryResult Filter(SymbolicSourceQueryFilter filter)
    {
        if (filter == null) throw new ArgumentNullException(nameof(filter));

        var points = ProgramPoints.Where(filter.Matches).ToArray();
        return Scope.Kind switch
        {
            SymbolicQueryScopeKind.File => FromFile(
                FilePath,
                LineCount ?? 0,
                LineGroups
                    .Select(group => new SymbolicQueryLineGroup(
                        group.Line,
                        group.ProgramPoints.Where(filter.Matches).ToArray()))
                    .Where(static group => group.ProgramPoints.Count != 0)
                    .ToArray(),
                SmtDiagnostics),
            SymbolicQueryScopeKind.Line => FromLine(
                FilePath,
                Line ?? 0,
                points,
                SmtDiagnostics),
            SymbolicQueryScopeKind.Span => FromSpan(
                FilePath,
                SpanStart ?? 0,
                SpanEnd ?? 0,
                Scope.StartLine ?? 1,
                Scope.StartColumn ?? 1,
                Scope.EndLine ?? 1,
                Scope.EndColumn ?? 1,
                points,
                SmtDiagnostics),
            SymbolicQueryScopeKind.Point when points.Length != 0 => From(points[0]),
            SymbolicQueryScopeKind.Point => FromLine(
                FilePath,
                Line ?? 0,
                points,
                SmtDiagnostics),
            _ => throw new InvalidOperationException("Unexpected symbolic query scope.")
        };
    }

    internal static SymbolicQueryResult FromFile(
        string filePath,
        int lineCount,
        IReadOnlyList<SymbolicQueryLineGroup> lines,
        SymbolicSmtDiagnostics? smtDiagnostics = null)
    {
        if (lineCount < 0) throw new ArgumentOutOfRangeException(nameof(lineCount));
        if (lines == null) throw new ArgumentNullException(nameof(lines));
        return FromAggregate(
            new SymbolicQueryScope(
                SymbolicQueryScopeKind.File,
                filePath,
                lineCount: lineCount),
            lines.SelectMany(static line => line.ProgramPoints).ToArray(),
            smtDiagnostics,
            lines);
    }

    internal static SymbolicQueryResult FromLine(
        string filePath,
        int line,
        IReadOnlyList<SymbolicProgramPointResult> programPoints,
        SymbolicSmtDiagnostics? smtDiagnostics = null)
    {
        return FromAggregate(
            new SymbolicQueryScope(SymbolicQueryScopeKind.Line, filePath, line),
            programPoints,
            smtDiagnostics);
    }

    internal static SymbolicQueryResult FromSpan(
        string filePath,
        int spanStart,
        int spanEnd,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        IReadOnlyList<SymbolicProgramPointResult> programPoints,
        SymbolicSmtDiagnostics? smtDiagnostics = null)
    {
        if (spanStart < 0) throw new ArgumentOutOfRangeException(nameof(spanStart));
        if (spanEnd < spanStart) throw new ArgumentOutOfRangeException(nameof(spanEnd));
        return FromAggregate(
            new SymbolicQueryScope(
                SymbolicQueryScopeKind.Span,
                filePath,
                spanStart: spanStart,
                spanEnd: spanEnd,
                startLine: startLine,
                startColumn: startColumn,
                endLine: endLine,
                endColumn: endColumn),
            programPoints,
            smtDiagnostics);
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
            point.SmtDiagnostics);
    }

    private static SymbolicQueryResult FromAggregate(
        SymbolicQueryScope scope,
        IReadOnlyList<SymbolicProgramPointResult> programPoints,
        SymbolicSmtDiagnostics? smtDiagnostics,
        IReadOnlyList<SymbolicQueryLineGroup>? lineGroups = null)
    {
        if (programPoints == null) throw new ArgumentNullException(nameof(programPoints));
        var factSummary = SymbolicInvariantService.MergeInvariantFacts(
            programPoints.Select(static point => point.Facts));
        var observedInvariant = SymbolicInvariantResult.FromFacts(
            factSummary.Facts,
            factSummary.MergedInvariantText);
        var mergedPathFacts = SymbolicMergedPathFacts.FromProgramPoints(programPoints);
        var mergedInvariant = SymbolicInvariantResult.FromMergedPathFacts(mergedPathFacts);
        var programPointSummary = SymbolicProgramPointSummary.FromProgramPoints(programPoints);
        var conditionProofs = SymbolicConditionProofSummary.FromProgramPoints(programPoints);
        var diagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        return new SymbolicQueryResult(
            scope,
            programPoints,
            observedInvariant,
            mergedInvariant,
            mergedPathFacts,
            programPointSummary,
            programPointSummary.Reachability,
            conditionProofs,
            diagnostics,
            lineGroups);
    }
}

internal sealed record SymbolicQueryLineGroup(int Line, IReadOnlyList<SymbolicProgramPointResult> ProgramPoints);
