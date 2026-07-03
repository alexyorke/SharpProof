using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using PurelySharp.Symbolic.Ir;
using PurelySharp.Symbolic.Smt;
using SearchLib.Purity;
using SearchLib.Smt;
using ExceptionCategories = PurelySharp.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionCategories;
using ExceptionTypes = PurelySharp.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionTypes;

namespace PurelySharp.Symbolic
{
    internal sealed partial class SymbolicRuntimeHazardQueryService
    {
        private readonly SymbolicInvariantService _invariantService;

        public SymbolicRuntimeHazardQueryService()
            : this(new SymbolicInvariantService())
        {
        }

        internal SymbolicRuntimeHazardQueryService(SymbolicInvariantService invariantService)
        {
            _invariantService = invariantService ?? throw new ArgumentNullException(nameof(invariantService));
        }

        public SymbolicRuntimeHazardQueryResult QueryFileRuntimeHazards(
            string filePath,
            SmtAnalysisService smtAnalysis,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return QuerySourceRuntimeHazards(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                smtAnalysis,
                references,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QueryFileRuntimeHazardsLine(
            string filePath,
            int line,
            SmtAnalysisService smtAnalysis,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return QuerySourceRuntimeHazardsLine(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                line,
                smtAnalysis,
                references,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QueryFileRuntimeHazardsSpan(
            string filePath,
            int spanStart,
            int spanEnd,
            SmtAnalysisService smtAnalysis,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return QuerySourceRuntimeHazardsSpan(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                spanStart,
                spanEnd,
                smtAnalysis,
                references,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QuerySourceRuntimeHazards(
            string sourceText,
            string filePath,
            SmtAnalysisService smtAnalysis,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
                sourceText,
                filePath,
                "PurelySharp.Symbolic.RuntimeHazards.cs",
                "PurelySharp.Symbolic.RuntimeHazards",
                references,
                cancellationToken);
            return QuerySyntaxTreeRuntimeHazards(
                syntaxTree,
                compilation,
                smtAnalysis,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QuerySourceRuntimeHazardsLine(
            string sourceText,
            string filePath,
            int line,
            SmtAnalysisService smtAnalysis,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
                sourceText,
                filePath,
                "PurelySharp.Symbolic.RuntimeHazards.cs",
                "PurelySharp.Symbolic.RuntimeHazards",
                references,
                cancellationToken);
            return QuerySyntaxTreeRuntimeHazardsLine(
                syntaxTree,
                compilation,
                line,
                smtAnalysis,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QuerySourceRuntimeHazardsSpan(
            string sourceText,
            string filePath,
            int spanStart,
            int spanEnd,
            SmtAnalysisService smtAnalysis,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
                sourceText,
                filePath,
                "PurelySharp.Symbolic.RuntimeHazards.cs",
                "PurelySharp.Symbolic.RuntimeHazards",
                references,
                cancellationToken);
            return QuerySyntaxTreeRuntimeHazardsSpan(
                syntaxTree,
                compilation,
                spanStart,
                spanEnd,
                smtAnalysis,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QuerySyntaxTreeRuntimeHazards(
            SyntaxTree syntaxTree,
            Compilation compilation,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            return QuerySyntaxTreeRuntimeHazardsCore(
                syntaxTree,
                compilation,
                scope: null,
                requestedLine: null,
                smtAnalysis,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QuerySyntaxTreeRuntimeHazardsLine(
            SyntaxTree syntaxTree,
            Compilation compilation,
            int line,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            var lineSpan = SymbolicSourceLocation.GetLineSpan(syntaxTree, line, cancellationToken);
            return QuerySyntaxTreeRuntimeHazardsCore(
                syntaxTree,
                compilation,
                lineSpan,
                line,
                smtAnalysis,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QuerySyntaxTreeRuntimeHazardsSpan(
            SyntaxTree syntaxTree,
            Compilation compilation,
            int spanStart,
            int spanEnd,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            var sourceSpan = SymbolicSourceLocation.GetSourceSpan(syntaxTree, spanStart, spanEnd, cancellationToken);
            return QuerySyntaxTreeRuntimeHazardsCore(
                syntaxTree,
                compilation,
                sourceSpan,
                requestedLine: null,
                smtAnalysis,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QueryNodeRuntimeHazards(
            SyntaxNode node,
            SemanticModel semanticModel,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null,
            bool includeNestedCallables = false)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (semanticModel == null)
            {
                throw new ArgumentNullException(nameof(semanticModel));
            }

            if (smtAnalysis == null)
            {
                throw new ArgumentNullException(nameof(smtAnalysis));
            }

            return QueryRuntimeHazardsCore(
                node.SyntaxTree,
                semanticModel,
                node,
                scope: node.Span,
                requestedLine: null,
                smtAnalysis,
                cancellationToken,
                options,
                includeNestedCallables);
        }

        private SymbolicRuntimeHazardQueryResult QuerySyntaxTreeRuntimeHazardsCore(
            SyntaxTree syntaxTree,
            Compilation compilation,
            TextSpan? scope,
            int? requestedLine,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken,
            SymbolicRuntimeHazardQueryOptions? options)
        {
            if (syntaxTree == null)
            {
                throw new ArgumentNullException(nameof(syntaxTree));
            }

            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            if (smtAnalysis == null)
            {
                throw new ArgumentNullException(nameof(smtAnalysis));
            }

            options ??= SymbolicRuntimeHazardQueryOptions.Default;
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot(cancellationToken);
            return QueryRuntimeHazardsCore(
                syntaxTree,
                semanticModel,
                root,
                scope,
                requestedLine,
                smtAnalysis,
                cancellationToken,
                options,
                includeNestedCallables: true);
        }

        private SymbolicRuntimeHazardQueryResult QueryRuntimeHazardsCore(
            SyntaxTree syntaxTree,
            SemanticModel semanticModel,
            SyntaxNode root,
            TextSpan? scope,
            int? requestedLine,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken,
            SymbolicRuntimeHazardQueryOptions? options,
            bool includeNestedCallables)
        {
            if (syntaxTree == null)
            {
                throw new ArgumentNullException(nameof(syntaxTree));
            }

            if (semanticModel == null)
            {
                throw new ArgumentNullException(nameof(semanticModel));
            }

            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (smtAnalysis == null)
            {
                throw new ArgumentNullException(nameof(smtAnalysis));
            }

            options ??= SymbolicRuntimeHazardQueryOptions.Default;
            var hazards = EnumerateCandidates(root, semanticModel, cancellationToken, includeNestedCallables)
                .Where(candidate => scope == null || candidate.Site.Span.IntersectsWith(scope.Value))
                .Where(candidate => options.Includes(candidate.Kind))
                .Select(candidate => ClassifyCandidate(
                    syntaxTree,
                    semanticModel,
                    candidate,
                    smtAnalysis,
                    cancellationToken))
                .Where(hazard => options.IncludeUnprovenCandidates || hazard.Status == SymbolicRuntimeHazardStatus.Proven)
                .OrderBy(static hazard => hazard.SpanStart)
                .ThenBy(static hazard => hazard.Kind.ToString(), StringComparer.Ordinal)
                .ToArray();

            var sourceText = syntaxTree.GetText(cancellationToken);
            return new SymbolicRuntimeHazardQueryResult(
                syntaxTree.FilePath,
                sourceText.Lines.Count,
                scope?.Start,
                scope?.End,
                requestedLine,
                hazards,
                SymbolicSmtDiagnostics.FromService(smtAnalysis));
        }

        private SymbolicRuntimeHazard ClassifyCandidate(
            SyntaxTree syntaxTree,
            SemanticModel semanticModel,
            RuntimeHazardCandidate candidate,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken)
        {
            var analysis = _invariantService.AnalyzeAt(
                candidate.Site,
                semanticModel,
                smtAnalysis,
                cancellationToken);
            var triggerCondition = candidate.TriggerCondition;
            var triggerPrecondition = candidate.TriggerPrecondition;
            var exceptionType = candidate.ExceptionType;
            var category = candidate.Category;
            if (TryRefineThrowNullCandidate(
                    candidate,
                    analysis,
                    semanticModel,
                    smtAnalysis,
                    cancellationToken,
                    out var throwNullTrigger))
            {
                triggerCondition = throwNullTrigger;
                triggerPrecondition = null;
                exceptionType = ExceptionTypes.NullReferenceException;
                category = ExceptionCategories.DefiniteThrowNull;
            }

            var (status, reason) = ClassifyTrigger(
                analysis,
                triggerCondition,
                triggerPrecondition,
                smtAnalysis);
            var lineColumn = SymbolicSourceLocation.GetLineAndColumn(syntaxTree, candidate.Site.SpanStart, cancellationToken);
            var sourceSpan = SymbolicSourceLocation.GetNodeSourceSpan(syntaxTree, candidate.Site.Span, cancellationToken);

            return new SymbolicRuntimeHazard(
                syntaxTree.FilePath,
                candidate.Kind,
                status,
                reason,
                exceptionType,
                category,
                candidate.Site.Kind().ToString(),
                candidate.Site.ToString(),
                candidate.Site.SpanStart,
                candidate.Site.Span.End,
                lineColumn.Line,
                lineColumn.Column,
                sourceSpan.StartLine,
                sourceSpan.StartColumn,
                sourceSpan.EndLine,
                sourceSpan.EndColumn,
                triggerCondition.ToString() ?? string.Empty,
                triggerPrecondition == null ? null : SymbolicFactInfo.FromFact(triggerPrecondition),
                analysis.MergedInvariantText,
                analysis.Facts,
                SymbolicFactInfo.Distinct(
                    SymbolicFactInfo.FromState(analysis.PathState).Concat(
                        triggerPrecondition == null
                            ? Array.Empty<SymbolicFactInfo>()
                            : new[] { SymbolicFactInfo.FromFact(triggerPrecondition) })),
                analysis.Reachability,
                analysis.ReachabilityReason,
                SymbolicSmtDiagnostics.FromService(smtAnalysis));
        }

        private static bool TryRefineThrowNullCandidate(
            RuntimeHazardCandidate candidate,
            SymbolicProgramPointAnalysis analysis,
            SemanticModel semanticModel,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken,
            out SmtFormula trigger)
        {
            trigger = null!;
            if (candidate.Kind != SymbolicRuntimeHazardKind.DirectThrow ||
                !SymbolicRuntimeExceptionFacts.TryGetThrowExpression(candidate.Site, out var thrownExpression))
            {
                return false;
            }

            var hasIrNullCondition = TryCreateReferenceNullCondition(
                thrownExpression,
                semanticModel,
                cancellationToken,
                "ir.runtime-hazard.throw-null.trigger",
                out var nullCondition,
                out var irNullTrigger);
            var hasLegacyNullCondition = TryTranslateNullCondition(
                thrownExpression,
                semanticModel,
                cancellationToken,
                out var legacyNullTrigger);
            if (!hasIrNullCondition && !hasLegacyNullCondition)
            {
                return false;
            }

            trigger = hasIrNullCondition ? irNullTrigger : legacyNullTrigger;
            if (trigger is SmtBooleanConstant { Value: true })
            {
                return true;
            }

            if (hasIrNullCondition)
            {
                var provenNull = SymbolicReachabilityService.ClassifyStateImplication(
                    analysis.PathState,
                    nullCondition,
                    smtAnalysis);
                if (provenNull.Info.Status == SymbolicProofStatus.ProvenTrue)
                {
                    return true;
                }
            }

            return hasLegacyNullCondition &&
                SymbolicReachabilityService.PathConditionsImply(analysis.PathConditions, legacyNullTrigger, smtAnalysis);
        }

        private static (SymbolicRuntimeHazardStatus Status, string Reason) ClassifyTrigger(
            SymbolicProgramPointAnalysis analysis,
            SmtFormula triggerCondition,
            SymbolicFact? triggerPrecondition,
            SmtAnalysisService smtAnalysis)
        {
            if (analysis.Reachability == SymbolicReachability.Unreachable)
            {
                return (SymbolicRuntimeHazardStatus.Unreachable, analysis.ReachabilityReason);
            }

            if (analysis.Reachability == SymbolicReachability.Unknown)
            {
                return (SymbolicRuntimeHazardStatus.Unknown, analysis.ReachabilityReason);
            }

            if (!smtAnalysis.Options.IsEnabled)
            {
                return (SymbolicRuntimeHazardStatus.Unsupported, "smt_disabled");
            }

            if (triggerCondition is SmtBooleanConstant { Value: true })
            {
                return (SymbolicRuntimeHazardStatus.Proven, "trigger_always_true");
            }

            if (triggerCondition is SmtBooleanConstant { Value: false })
            {
                return (SymbolicRuntimeHazardStatus.Unreachable, "trigger_always_false");
            }

            if (triggerPrecondition != null &&
                TryClassifyIrTrigger(analysis, triggerPrecondition, smtAnalysis, out var irResult))
            {
                return irResult;
            }

            var proven = SymbolicReachabilityService.ClassifyImplication(
                analysis.PathConditions,
                triggerCondition,
                smtAnalysis);
            if (proven.Outcome == PurityProofOutcome.ProvablyPure)
            {
                return (SymbolicRuntimeHazardStatus.Proven, proven.Reason);
            }

            var disproven = SymbolicReachabilityService.ClassifyImplication(
                analysis.PathConditions,
                new SmtUnaryFormula(SmtUnaryOperator.Not, triggerCondition),
                smtAnalysis);
            if (disproven.Outcome == PurityProofOutcome.ProvablyPure)
            {
                return (SymbolicRuntimeHazardStatus.Unreachable, disproven.Reason);
            }

            return (SymbolicRuntimeHazardStatus.Unknown, proven.Reason);
        }

        private static bool TryClassifyIrTrigger(
            SymbolicProgramPointAnalysis analysis,
            SymbolicFact triggerPrecondition,
            SmtAnalysisService smtAnalysis,
            out (SymbolicRuntimeHazardStatus Status, string Reason) result)
        {
            var proven = SymbolicReachabilityService.ClassifyStateImplication(
                analysis.PathState,
                triggerPrecondition,
                smtAnalysis);
            if (proven.Info.Status == SymbolicProofStatus.ProvenTrue)
            {
                result = (SymbolicRuntimeHazardStatus.Proven, proven.Info.Reason);
                return true;
            }

            var negatedTrigger = new SymbolicNotCondition(new SymbolicFactCondition(triggerPrecondition));
            var disproven = SymbolicReachabilityService.ClassifyStateImplication(
                analysis.PathState,
                negatedTrigger,
                smtAnalysis);
            if (disproven.Info.Status == SymbolicProofStatus.ProvenTrue)
            {
                result = (SymbolicRuntimeHazardStatus.Unreachable, disproven.Info.Reason);
                return true;
            }

            result = default;
            return false;
        }

    }

    public sealed class SymbolicRuntimeHazardQueryOptions
    {
        public static readonly SymbolicRuntimeHazardQueryOptions Default = new();

        public SymbolicRuntimeHazardQueryOptions(
            bool includeUnprovenCandidates = false,
            IEnumerable<SymbolicRuntimeHazardKind>? kinds = null)
        {
            IncludeUnprovenCandidates = includeUnprovenCandidates;
            Kinds = kinds?.ToImmutableHashSet() ?? ImmutableHashSet<SymbolicRuntimeHazardKind>.Empty;
        }

        public bool IncludeUnprovenCandidates { get; }

        public ImmutableHashSet<SymbolicRuntimeHazardKind> Kinds { get; }

        public bool Includes(SymbolicRuntimeHazardKind kind)
        {
            return Kinds.Count == 0 || Kinds.Contains(kind);
        }
    }

    public sealed class SymbolicRuntimeHazardQueryResult
    {
        internal SymbolicRuntimeHazardQueryResult(
            string filePath,
            int lineCount,
            int? scopeStart,
            int? scopeEnd,
            int? line,
            IReadOnlyList<SymbolicRuntimeHazard> hazards,
            SymbolicSmtDiagnostics? smtDiagnostics = null)
        {
            FilePath = filePath;
            LineCount = lineCount;
            ScopeStart = scopeStart;
            ScopeEnd = scopeEnd;
            Line = line;
            Hazards = hazards ?? throw new ArgumentNullException(nameof(hazards));
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        }

        public string FilePath { get; }

        public int LineCount { get; }

        public int? ScopeStart { get; }

        public int? ScopeEnd { get; }

        public int? Line { get; }

        public IReadOnlyList<SymbolicRuntimeHazard> Hazards { get; }

        public int HazardCount => Hazards.Count;

        public SymbolicSmtDiagnostics SmtDiagnostics { get; }
    }

    public sealed class SymbolicRuntimeHazard
    {
        internal SymbolicRuntimeHazard(
            string filePath,
            SymbolicRuntimeHazardKind kind,
            SymbolicRuntimeHazardStatus status,
            string statusReason,
            string exceptionType,
            string category,
            string nodeKind,
            string operationText,
            int spanStart,
            int spanEnd,
            int line,
            int column,
            int nodeStartLine,
            int nodeStartColumn,
            int nodeEndLine,
            int nodeEndColumn,
            string triggerCondition,
            SymbolicFactInfo? triggerPrecondition,
            string mergedInvariantText,
            IReadOnlyList<string> pathConditions,
            IReadOnlyList<SymbolicFactInfo> symbolicFacts,
            SymbolicReachability reachability,
            string reachabilityReason,
            SymbolicSmtDiagnostics? smtDiagnostics = null)
        {
            FilePath = filePath;
            Kind = kind;
            Status = status;
            StatusReason = statusReason;
            ExceptionType = exceptionType;
            Category = category;
            NodeKind = nodeKind;
            OperationText = operationText;
            SpanStart = spanStart;
            SpanEnd = spanEnd;
            SpanLength = spanEnd - spanStart;
            Line = line;
            Column = column;
            NodeStartLine = nodeStartLine;
            NodeStartColumn = nodeStartColumn;
            NodeEndLine = nodeEndLine;
            NodeEndColumn = nodeEndColumn;
            TriggerCondition = triggerCondition;
            TriggerPrecondition = triggerPrecondition;
            MergedInvariantText = mergedInvariantText;
            PathConditions = pathConditions ?? throw new ArgumentNullException(nameof(pathConditions));
            PathConditionCount = pathConditions.Count;
            SymbolicFacts = symbolicFacts ?? throw new ArgumentNullException(nameof(symbolicFacts));
            Reachability = reachability;
            ReachabilityReason = reachabilityReason;
            Proof = new SymbolicProofInfo(
                MapProofStatus(status),
                ResolveProofBackend(status),
                ResolveUnknownReason(status, statusReason),
                statusReason,
                cacheHit: false,
                budget: null,
                category,
                triggerCondition,
                kind.ToString());
            InvariantInfo = new SymbolicInvariantInfo(
                MergedInvariantText,
                SymbolicFacts,
                new[] { Proof },
                SymbolicInvariantMergeKind.Conjunction,
                PathConditionCount);
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        }

        public string FilePath { get; }

        public SymbolicRuntimeHazardKind Kind { get; }

        public SymbolicRuntimeHazardStatus Status { get; }

        public string StatusReason { get; }

        public string ExceptionType { get; }

        public string Category { get; }

        public string NodeKind { get; }

        public string OperationText { get; }

        public int SpanStart { get; }

        public int SpanEnd { get; }

        public int SpanLength { get; }

        public int Line { get; }

        public int Column { get; }

        public int NodeStartLine { get; }

        public int NodeStartColumn { get; }

        public int NodeEndLine { get; }

        public int NodeEndColumn { get; }

        public string TriggerCondition { get; }

        public SymbolicFactInfo? TriggerPrecondition { get; }

        public string MergedInvariantText { get; }

        internal IReadOnlyList<string> PathConditions { get; }

        public int PathConditionCount { get; }

        public IReadOnlyList<SymbolicFactInfo> SymbolicFacts { get; }

        public SymbolicProofInfo Proof { get; }

        public SymbolicInvariantInfo InvariantInfo { get; }

        public SymbolicReachability Reachability { get; }

        public string ReachabilityReason { get; }

        public SymbolicSmtDiagnostics SmtDiagnostics { get; }

        private static SymbolicProofStatus MapProofStatus(SymbolicRuntimeHazardStatus status)
        {
            return status switch
            {
                SymbolicRuntimeHazardStatus.Proven => SymbolicProofStatus.ProvenTrue,
                SymbolicRuntimeHazardStatus.Unreachable => SymbolicProofStatus.Unreachable,
                _ => SymbolicProofStatus.Unknown,
            };
        }

        private static SymbolicProofBackend ResolveProofBackend(SymbolicRuntimeHazardStatus status)
        {
            return status == SymbolicRuntimeHazardStatus.Unsupported
                ? SymbolicProofBackend.None
                : SymbolicProofBackend.Smt;
        }

        private static SymbolicUnknownReason ResolveUnknownReason(
            SymbolicRuntimeHazardStatus status,
            string reason)
        {
            if (status is SymbolicRuntimeHazardStatus.Proven or SymbolicRuntimeHazardStatus.Unreachable)
            {
                return SymbolicUnknownReason.None;
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return SymbolicUnknownReason.Unknown;
            }

            if (ContainsReason(reason, "timeout") ||
                ContainsReason(reason, "timed_out"))
            {
                return SymbolicUnknownReason.Timeout;
            }

            if (ContainsReason(reason, "method_budget"))
            {
                return SymbolicUnknownReason.MethodBudgetExceeded;
            }

            if (ContainsReason(reason, "path_condition") ||
                ContainsReason(reason, "max_path_conditions") ||
                ContainsReason(reason, "too_many_path_conditions"))
            {
                return SymbolicUnknownReason.PathConditionBudgetExceeded;
            }

            if (ContainsReason(reason, "expression_budget") ||
                ContainsReason(reason, "max_expression"))
            {
                return SymbolicUnknownReason.ExpressionBudgetExceeded;
            }

            if (ContainsReason(reason, "cancellation") ||
                ContainsReason(reason, "cancelled") ||
                ContainsReason(reason, "canceled"))
            {
                return SymbolicUnknownReason.CancellationRequested;
            }

            if (ContainsReason(reason, "encoding"))
            {
                return SymbolicUnknownReason.EncodingFailure;
            }

            if (ContainsReason(reason, "unsupported"))
            {
                return SymbolicUnknownReason.UnsupportedIrEncoding;
            }

            if (ContainsReason(reason, "smt_required") ||
                ContainsReason(reason, "smt_disabled") ||
                ContainsReason(reason, "smt_off"))
            {
                return SymbolicUnknownReason.SmtDisabled;
            }

            if (ContainsReason(reason, "z3") ||
                ContainsReason(reason, "native") ||
                ContainsReason(reason, "unavailable") ||
                ContainsReason(reason, "load"))
            {
                return SymbolicUnknownReason.SmtUnavailable;
            }

            return SymbolicUnknownReason.Unknown;
        }

        private static bool ContainsReason(string reason, string value)
        {
            return reason.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public enum SymbolicRuntimeHazardKind
    {
        DirectThrow,
        Rethrow,
        DivideByZero,
        NullDereference,
        NullableValueWithoutValue,
        IndexOutOfRange,
        ArgumentOutOfRange,
        CheckedIntegralOverflow,
        ArrayTypeMismatch,
        UnboxNull,
        InvalidCast,
        DynamicNullBinding,
        SwitchExpressionNoMatch,
        NegativeArrayLength,
        NegativeStackAllocLength,
        ArgumentNull,
    }

    public enum SymbolicRuntimeHazardStatus
    {
        Proven,
        Unreachable,
        Unknown,
        Unsupported,
    }
}
