using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic
{
    public sealed class SymbolicQueryService
    {
        private readonly SymbolicInvariantService _invariantService;
        private readonly SymbolicSourceQueryService _sourceQueryService;
        private readonly SymbolicRuntimeHazardQueryService _runtimeHazardQueryService;
        private readonly SymbolicComplexityService _complexityService;
        private readonly SymbolicCapabilityService _capabilityService;

        public SymbolicQueryService()
            : this(new SymbolicInvariantService())
        {
        }

        internal SymbolicQueryService(SymbolicInvariantService invariantService)
        {
            if (invariantService == null)
            {
                throw new ArgumentNullException(nameof(invariantService));
            }

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
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var options = request.Options ?? SymbolicQueryOptions.Default;
            var result = QueryCore(request.Source, request.Target, options, cancellationToken);
            return options.Filter == null || options.Filter.IsEmpty
                ? result
                : result.Filter(options.Filter);
        }

        public SymbolicConditionProofResult Prove(
            SymbolicConditionProofRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.ConditionText))
            {
                throw new ArgumentException("Condition text is required.", nameof(request));
            }

            var pointTarget = request.Target.Kind == SymbolicQueryTargetKind.Point
                ? request.Target
                : throw new ArgumentException("Condition proof requests require a point target.", nameof(request));
            var options = request.Options ?? SymbolicQueryOptions.Default;
            if (options.SmtAnalysis == null)
            {
                throw new ArgumentException("Condition proof requests require SMT analysis.", nameof(request));
            }

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
                        cancellationToken);
                case SymbolicSourceInputKind.Text:
                    return _sourceQueryService.ProveConditionAtSource(
                        source.SourceText!,
                        source.FilePath ?? SymbolicSourceInput.DefaultFilePath,
                        pointTarget.LineNumber!.Value,
                        pointTarget.ColumnNumber ?? 1,
                        request.ConditionText,
                        options.SmtAnalysis,
                        options.References,
                        cancellationToken);
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

        internal SymbolicConditionProofResult ProveAtSyntaxNode(
            SemanticModel semanticModel,
            SyntaxNode node,
            string conditionText,
            SmtAnalysisService smtAnalysis,
            bool includeCurrentStatementCompletionFacts,
            CancellationToken cancellationToken = default)
        {
            if (semanticModel == null)
            {
                throw new ArgumentNullException(nameof(semanticModel));
            }

            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (string.IsNullOrWhiteSpace(conditionText))
            {
                throw new ArgumentException("Condition text is required.", nameof(conditionText));
            }

            if (smtAnalysis == null)
            {
                throw new ArgumentNullException(nameof(smtAnalysis));
            }

            return _sourceQueryService.ProveConditionAtSyntaxNode(
                semanticModel,
                node,
                conditionText,
                smtAnalysis,
                includeCurrentStatementCompletionFacts,
                cancellationToken);
        }

        public SymbolicRuntimeHazardQueryResult QueryRuntimeHazards(
            SymbolicRuntimeHazardRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var options = request.Options ?? SymbolicQueryOptions.Default;
            if (options.SmtAnalysis == null)
            {
                throw new ArgumentException("Runtime hazard queries require SMT analysis.", nameof(request));
            }

            var hazardOptions = request.HazardOptions ?? SymbolicRuntimeHazardQueryOptions.Default;
            var source = request.Source;
            var target = request.Target;
            switch (source.Kind)
            {
                case SymbolicSourceInputKind.File:
                    return QueryFileRuntimeHazards(source.FilePath!, target, options, hazardOptions, cancellationToken);
                case SymbolicSourceInputKind.Text:
                    return QuerySourceRuntimeHazards(source.SourceText!, source.FilePath ?? SymbolicSourceInput.DefaultFilePath, target, options, hazardOptions, cancellationToken);
                case SymbolicSourceInputKind.SyntaxTree:
                    return QuerySyntaxTreeRuntimeHazards(source.SyntaxTree!, source.Compilation!, target, options, hazardOptions, cancellationToken);
                case SymbolicSourceInputKind.Node:
                    return _runtimeHazardQueryService.QueryNodeRuntimeHazards(
                        source.Node!,
                        source.SemanticModel!,
                        options.SmtAnalysis,
                        cancellationToken,
                        hazardOptions,
                        includeNestedCallables: target.IncludeNestedCallables);
                default:
                    throw new NotSupportedException("Runtime hazard source kind is not supported.");
            }
        }

        public SymbolicComplexityResult QueryComplexity(
            SymbolicComplexityRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return _complexityService.Query(
                request.Source,
                request.Target,
                request.Options ?? SymbolicQueryOptions.Default,
                cancellationToken);
        }

        public SymbolicCapabilityResult QueryCapabilities(
            SymbolicCapabilityRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return _capabilityService.Query(
                request.Source,
                request.Target,
                request.Options ?? SymbolicQueryOptions.Default,
                cancellationToken);
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
                    return SymbolicQueryResult.From(QueryFile(source.FilePath!, target, options, cancellationToken));
                case SymbolicSourceInputKind.Text:
                    return SymbolicQueryResult.From(QuerySource(source.SourceText!, source.FilePath ?? SymbolicSourceInput.DefaultFilePath, target, options, cancellationToken));
                case SymbolicSourceInputKind.SyntaxTree:
                    return SymbolicQueryResult.From(QuerySyntaxTree(source.SyntaxTree!, source.Compilation!, target, options, cancellationToken));
                case SymbolicSourceInputKind.Node:
                    return SymbolicQueryResult.From(QueryNode(source.Node!, source.SemanticModel!, target, options, cancellationToken));
                default:
                    throw new NotSupportedException("Source kind is not supported.");
            }
        }

        private object QueryFile(
            string filePath,
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
                        options.IncludeCurrentStatementCompletionFacts);
                case SymbolicQueryTargetKind.Position:
                    return _sourceQueryService.QueryFileAtPosition(
                        filePath,
                        target.PositionOffset!.Value,
                        options.References,
                        cancellationToken,
                        options.SmtAnalysis,
                        options.ImpliedConditions);
                case SymbolicQueryTargetKind.Line:
                    return _sourceQueryService.QueryFileLine(
                        filePath,
                        target.LineNumber!.Value,
                        options.References,
                        cancellationToken,
                        options.SmtAnalysis,
                        options.ImpliedConditions,
                        options.IncludeExpressionProgramPoints,
                        options.IncludeCurrentStatementCompletionFacts);
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
                        options.IncludeCurrentStatementCompletionFacts);
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
                        options.IncludeCurrentStatementCompletionFacts);
                case SymbolicQueryTargetKind.AllLines:
                    return _sourceQueryService.QueryFileAllLines(
                        filePath,
                        options.References,
                        cancellationToken,
                        options.SmtAnalysis,
                        options.ImpliedConditions,
                        options.IncludeExpressionProgramPoints,
                        options.IncludeCurrentStatementCompletionFacts);
                default:
                    throw new NotSupportedException("Target kind is not supported for file queries.");
            }
        }

        private object QuerySource(
            string sourceText,
            string filePath,
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
                        options.IncludeCurrentStatementCompletionFacts);
                case SymbolicQueryTargetKind.Position:
                    return _sourceQueryService.QuerySourceAtPosition(
                        sourceText,
                        filePath,
                        target.PositionOffset!.Value,
                        options.References,
                        cancellationToken,
                        options.SmtAnalysis,
                        options.ImpliedConditions);
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
                        options.IncludeCurrentStatementCompletionFacts);
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
                        options.IncludeCurrentStatementCompletionFacts);
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
                        options.IncludeCurrentStatementCompletionFacts);
                case SymbolicQueryTargetKind.AllLines:
                    return _sourceQueryService.QuerySourceAllLines(
                        sourceText,
                        filePath,
                        options.References,
                        cancellationToken,
                        options.SmtAnalysis,
                        options.ImpliedConditions,
                        options.IncludeExpressionProgramPoints,
                        options.IncludeCurrentStatementCompletionFacts);
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
            {
                throw new NotSupportedException("Node sources require a node target.");
            }

            var analysis = node is ForStatementSyntax forStatement
                ? _invariantService.AnalyzeForInitialEntry(forStatement, semanticModel, options.SmtAnalysis, cancellationToken)
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
                validatePosition: true);
            var span = SymbolicSourceLocation.GetNodeSourceSpan(node.SyntaxTree, node.Span, cancellationToken);
            var proofs = CreateNodeProofs(
                semanticModel,
                node.SpanStart,
                analysis,
                options.ImpliedConditions,
                options.SmtAnalysis,
                cancellationToken);
            var mergedInvariantText = SymbolicFormulaDisplay.FormatMergedInvariant(analysis.PathConditions);
            var invariant = SymbolicInvariantResult.FromFormulas(
                analysis.PathConditions,
                mergedInvariantText,
                SymbolicInvariantMergeKind.Conjunction);
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
                symbolicFacts: SymbolicFactInfo.FromState(analysis.PathState));
        }

        private IReadOnlyList<SymbolicConditionProofResult> CreateNodeProofs(
            SemanticModel semanticModel,
            int position,
            SymbolicProgramPointAnalysis analysis,
            IEnumerable<string> conditionTexts,
            SmtAnalysisService? smtAnalysis,
            CancellationToken cancellationToken)
        {
            if (conditionTexts == null)
            {
                return Array.Empty<SymbolicConditionProofResult>();
            }

            var syntaxTree = semanticModel.SyntaxTree;
            var lineColumn = SymbolicSourceLocation.GetLineAndColumn(
                syntaxTree,
                position,
                cancellationToken,
                validatePosition: true);
            return conditionTexts
                .Where(static condition => !string.IsNullOrWhiteSpace(condition))
                .Select(condition => _sourceQueryService.ProveConditionAtSyntaxTree(
                    syntaxTree,
                    semanticModel.Compilation,
                    lineColumn.Line,
                    lineColumn.Column,
                    condition,
                    smtAnalysis ?? throw new ArgumentException("Condition proof requests require SMT analysis."),
                    cancellationToken))
                .ToArray();
        }

        private SymbolicRuntimeHazardQueryResult QueryFileRuntimeHazards(
            string filePath,
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
                        hazardOptions);
                case SymbolicQueryTargetKind.Span:
                    return _runtimeHazardQueryService.QueryFileRuntimeHazardsSpan(
                        filePath,
                        target.SpanStart!.Value,
                        target.SpanEnd!.Value,
                        options.SmtAnalysis!,
                        options.References,
                        cancellationToken,
                        hazardOptions);
                case SymbolicQueryTargetKind.AllLines:
                    return _runtimeHazardQueryService.QueryFileRuntimeHazards(
                        filePath,
                        options.SmtAnalysis!,
                        options.References,
                        cancellationToken,
                        hazardOptions);
                default:
                    throw new NotSupportedException("Target kind is not supported for runtime hazard queries.");
            }
        }

        private SymbolicRuntimeHazardQueryResult QuerySourceRuntimeHazards(
            string sourceText,
            string filePath,
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
                        hazardOptions);
                case SymbolicQueryTargetKind.Span:
                    return _runtimeHazardQueryService.QuerySourceRuntimeHazardsSpan(
                        sourceText,
                        filePath,
                        target.SpanStart!.Value,
                        target.SpanEnd!.Value,
                        options.SmtAnalysis!,
                        options.References,
                        cancellationToken,
                        hazardOptions);
                case SymbolicQueryTargetKind.AllLines:
                    return _runtimeHazardQueryService.QuerySourceRuntimeHazards(
                        sourceText,
                        filePath,
                        options.SmtAnalysis!,
                        options.References,
                        cancellationToken,
                        hazardOptions);
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

    public sealed class SymbolicQueryRequest
    {
        public SymbolicQueryRequest(
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

    public sealed class SymbolicConditionProofRequest
    {
        public SymbolicConditionProofRequest(
            SymbolicSourceInput source,
            SymbolicQueryTarget target,
            string conditionText,
            SymbolicQueryOptions options)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            ConditionText = conditionText ?? throw new ArgumentNullException(nameof(conditionText));
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public SymbolicSourceInput Source { get; }

        public SymbolicQueryTarget Target { get; }

        public string ConditionText { get; }

        public SymbolicQueryOptions Options { get; }
    }

    public sealed class SymbolicRuntimeHazardRequest
    {
        public SymbolicRuntimeHazardRequest(
            SymbolicSourceInput source,
            SymbolicQueryTarget target,
            SymbolicQueryOptions options,
            SymbolicRuntimeHazardQueryOptions? hazardOptions = null)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            HazardOptions = hazardOptions ?? SymbolicRuntimeHazardQueryOptions.Default;
        }

        public SymbolicSourceInput Source { get; }

        public SymbolicQueryTarget Target { get; }

        public SymbolicQueryOptions Options { get; }

        public SymbolicRuntimeHazardQueryOptions HazardOptions { get; }
    }

    public sealed class SymbolicComplexityRequest
    {
        public SymbolicComplexityRequest(
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

    public sealed class SymbolicCapabilityRequest
    {
        public SymbolicCapabilityRequest(
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
        public static readonly SymbolicQueryOptions Default = new SymbolicQueryOptions();

        public SymbolicQueryOptions(
            IEnumerable<MetadataReference>? references = null,
            SmtAnalysisService? smtAnalysis = null,
            IEnumerable<string>? impliedConditions = null,
            bool includeExpressionProgramPoints = false,
            bool includeCurrentStatementCompletionFacts = false,
            SymbolicSourceQueryFilter? filter = null)
        {
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
            if (references == null)
            {
                return ImmutableArray<MetadataReference>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<MetadataReference>();
            foreach (var reference in references)
            {
                if (reference == null)
                {
                    throw new ArgumentException("References cannot contain null entries.", parameterName);
                }

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
            SemanticModel? semanticModel = null)
        {
            Kind = kind;
            FilePath = filePath;
            SourceText = sourceText;
            SyntaxTree = syntaxTree;
            Compilation = compilation;
            Node = node;
            SemanticModel = semanticModel;
        }

        public SymbolicSourceInputKind Kind { get; }

        public string? FilePath { get; }

        public string? SourceText { get; }

        public SyntaxTree? SyntaxTree { get; }

        public Compilation? Compilation { get; }

        public SyntaxNode? Node { get; }

        public SemanticModel? SemanticModel { get; }

        public static SymbolicSourceInput FromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            return new SymbolicSourceInput(SymbolicSourceInputKind.File, filePath: filePath);
        }

        public static SymbolicSourceInput FromText(string sourceText, string? filePath = null)
        {
            if (sourceText == null)
            {
                throw new ArgumentNullException(nameof(sourceText));
            }

            return new SymbolicSourceInput(
                SymbolicSourceInputKind.Text,
                filePath: string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath,
                sourceText: sourceText);
        }

        public static SymbolicSourceInput FromSyntaxTree(SyntaxTree syntaxTree, Compilation compilation)
        {
            return new SymbolicSourceInput(
                SymbolicSourceInputKind.SyntaxTree,
                filePath: syntaxTree?.FilePath,
                syntaxTree: syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree)),
                compilation: compilation ?? throw new ArgumentNullException(nameof(compilation)));
        }

        public static SymbolicSourceInput FromNode(SyntaxNode node, SemanticModel semanticModel)
        {
            return new SymbolicSourceInput(
                SymbolicSourceInputKind.Node,
                filePath: node?.SyntaxTree.FilePath,
                node: node ?? throw new ArgumentNullException(nameof(node)),
                semanticModel: semanticModel ?? throw new ArgumentNullException(nameof(semanticModel)));
        }
    }

    public enum SymbolicSourceInputKind
    {
        File,
        Text,
        SyntaxTree,
        Node,
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
            return new SymbolicQueryTarget(SymbolicQueryTargetKind.Point, line: line, column: column);
        }

        public static SymbolicQueryTarget Position(int position)
        {
            ValidateNonNegative(position, nameof(position));
            return new SymbolicQueryTarget(SymbolicQueryTargetKind.Position, position: position);
        }

        public static SymbolicQueryTarget Line(int line)
        {
            ValidatePositive(line, nameof(line));
            return new SymbolicQueryTarget(SymbolicQueryTargetKind.Line, line: line);
        }

        public static SymbolicQueryTarget Span(int spanStart, int spanEnd)
        {
            ValidateNonNegative(spanStart, nameof(spanStart));
            if (spanEnd < spanStart)
            {
                throw new ArgumentOutOfRangeException(nameof(spanEnd), "Span end cannot be less than span start.");
            }

            return new SymbolicQueryTarget(SymbolicQueryTargetKind.Span, spanStart: spanStart, spanEnd: spanEnd);
        }

        public static SymbolicQueryTarget LineSpan(int startLine, int startColumn, int endLine, int endColumn)
        {
            ValidatePositive(startLine, nameof(startLine));
            ValidatePositive(startColumn, nameof(startColumn));
            ValidatePositive(endLine, nameof(endLine));
            ValidatePositive(endColumn, nameof(endColumn));
            if (endLine < startLine)
            {
                throw new ArgumentOutOfRangeException(nameof(endLine), "End line cannot be before start line.");
            }

            if (endLine == startLine && endColumn < startColumn)
            {
                throw new ArgumentOutOfRangeException(nameof(endColumn), "End column cannot be before start column on the same line.");
            }

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
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(paramName, "Value must be positive.");
            }
        }

        private static void ValidateNonNegative(int value, string paramName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(paramName, "Value cannot be negative.");
            }
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
        Node,
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

        internal object InnerResult { get; }

        public SymbolicQueryResult Filter(SymbolicSourceQueryFilter filter)
        {
            if (filter == null)
            {
                throw new ArgumentNullException(nameof(filter));
            }

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
                _ => throw new InvalidOperationException("Unexpected symbolic query result type."),
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
                _ => throw new InvalidOperationException("Unexpected symbolic query result type."),
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
                _ => throw new InvalidOperationException("Unexpected symbolic query result type."),
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
                        line: line.Line);
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
}
