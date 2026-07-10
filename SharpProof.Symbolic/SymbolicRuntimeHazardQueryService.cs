using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SearchLib.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using ExceptionCategories = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionCategories;
using ExceptionTypes = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionTypes;

namespace SharpProof.Symbolic;

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
            throw new ArgumentException("File path is required.", nameof(filePath));

        if (!File.Exists(filePath)) throw new FileNotFoundException("Source file does not exist.", filePath);

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
            throw new ArgumentException("File path is required.", nameof(filePath));

        if (!File.Exists(filePath)) throw new FileNotFoundException("Source file does not exist.", filePath);

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
            throw new ArgumentException("File path is required.", nameof(filePath));

        if (!File.Exists(filePath)) throw new FileNotFoundException("Source file does not exist.", filePath);

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
            "SharpProof.Symbolic.RuntimeHazards.cs",
            "SharpProof.Symbolic.RuntimeHazards",
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
            "SharpProof.Symbolic.RuntimeHazards.cs",
            "SharpProof.Symbolic.RuntimeHazards",
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
            "SharpProof.Symbolic.RuntimeHazards.cs",
            "SharpProof.Symbolic.RuntimeHazards",
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
            null,
            null,
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
            null,
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
        if (node == null) throw new ArgumentNullException(nameof(node));

        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));

        if (smtAnalysis == null) throw new ArgumentNullException(nameof(smtAnalysis));

        return QueryRuntimeHazardsCore(
            node.SyntaxTree,
            semanticModel,
            node,
            node.Span,
            null,
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
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));

        if (compilation == null) throw new ArgumentNullException(nameof(compilation));

        if (smtAnalysis == null) throw new ArgumentNullException(nameof(smtAnalysis));

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
            true);
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
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));

        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));

        if (root == null) throw new ArgumentNullException(nameof(root));

        if (smtAnalysis == null) throw new ArgumentNullException(nameof(smtAnalysis));

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
                out var throwNullTrigger,
                out var throwNullTriggerPrecondition))
        {
            triggerCondition = throwNullTrigger;
            triggerPrecondition = throwNullTriggerPrecondition;
            exceptionType = ExceptionTypes.NullReferenceException;
            category = ExceptionCategories.DefiniteThrowNull;
        }

        var (status, reason, proofInfo) = ClassifyTriggerCore(
            analysis,
            candidate.Site,
            triggerCondition,
            triggerPrecondition,
            smtAnalysis);
        var lineColumn =
            SymbolicSourceLocation.GetLineAndColumn(syntaxTree, candidate.Site.SpanStart, cancellationToken);
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
            SymbolicFormulaDisplay.Format(triggerCondition),
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
            proofInfo,
            SymbolicSmtDiagnostics.FromService(smtAnalysis));
    }

    private static bool TryRefineThrowNullCandidate(
        RuntimeHazardCandidate candidate,
        SymbolicProgramPointAnalysis analysis,
        SemanticModel semanticModel,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken,
        out SmtFormula trigger,
        out SymbolicFact? triggerPrecondition)
    {
        trigger = null!;
        triggerPrecondition = null;
        if (candidate.Kind != SymbolicRuntimeHazardKind.DirectThrow ||
            !SymbolicRuntimeExceptionFacts.TryGetThrowExpression(candidate.Site, out var thrownExpression))
            return false;

        var hasIrNullCondition = TryCreateReferenceNullCondition(
            thrownExpression,
            semanticModel,
            cancellationToken,
            "ir.runtime-hazard.throw-null.trigger",
            out var nullCondition);
        SmtFormula irNullTrigger = null!;
        var hasIrNullTrigger = hasIrNullCondition &&
                               SymbolicIrFormulaEncoder.TryEncode(nullCondition, out irNullTrigger);
        SmtFormula legacyNullTrigger = null!;
        var hasLegacyNullCondition = TryTranslateNullCondition(
            thrownExpression,
            semanticModel,
            cancellationToken,
            out legacyNullTrigger);
        if (!hasIrNullTrigger && !hasLegacyNullCondition) return false;

        trigger = hasIrNullTrigger ? irNullTrigger : legacyNullTrigger;
        triggerPrecondition = hasIrNullCondition ? TryGetFactPrecondition(nullCondition) : null;
        if (trigger is SmtBooleanConstant { Value: true }) return true;

        if (hasIrNullCondition)
        {
            var provenNull = SymbolicReachabilityService.ClassifyStateConditionTruth(
                analysis.PathState,
                nullCondition,
                smtAnalysis);
            if (provenNull.Info.Status == SymbolicProofStatus.ProvenTrue) return true;
        }

        return hasLegacyNullCondition &&
               SymbolicReachabilityService.PathConditionsImplyWithIrFirst(
                   analysis.PathConditions,
                   legacyNullTrigger,
                   candidate.Site,
                   smtAnalysis,
                   "runtime.hazard.trigger",
                   "runtime-hazard-trigger");
    }

    private static SymbolicFact? TryGetFactPrecondition(SymbolicCondition condition)
    {
        return condition is SymbolicFactCondition factCondition
            ? factCondition.Fact
            : null;
    }

    internal static (SymbolicRuntimeHazardStatus Status, string Reason, SymbolicProofInfo? Proof) ClassifyTriggerCore(
        SymbolicProgramPointAnalysis analysis,
        SyntaxNode sourceNode,
        SmtFormula triggerCondition,
        SymbolicFact? triggerPrecondition,
        SmtAnalysisService smtAnalysis)
    {
        if (analysis.Reachability == SymbolicReachability.Unreachable)
            return (SymbolicRuntimeHazardStatus.Unreachable, analysis.ReachabilityReason, null);

        if (analysis.Reachability == SymbolicReachability.Unknown)
            return (SymbolicRuntimeHazardStatus.Unknown, analysis.ReachabilityReason, null);

        if (!smtAnalysis.Options.IsEnabled) return (SymbolicRuntimeHazardStatus.Unsupported, "smt_disabled", null);

        if (triggerCondition is SmtBooleanConstant { Value: true })
            return (SymbolicRuntimeHazardStatus.Proven, "trigger_always_true", null);

        if (triggerCondition is SmtBooleanConstant { Value: false })
            return (SymbolicRuntimeHazardStatus.Unreachable, "trigger_always_false", null);

        if (triggerPrecondition is { Confidence: SymbolicFactConfidence.Unsupported })
            return (SymbolicRuntimeHazardStatus.Unknown, "unsupported_typed_projection", null);

        if (IsFallbackDerivedTriggerPrecondition(triggerPrecondition))
            return (SymbolicRuntimeHazardStatus.Unknown, "unsupported_formula_fallback", null);

        if (triggerPrecondition != null &&
            TryClassifyIrTrigger(analysis, triggerPrecondition, smtAnalysis, out var irResult))
            return irResult;

        var formulaTruth = SymbolicReachabilityService.ClassifyFormulaConditionTruthWithIrFirst(
            analysis.PathConditions,
            triggerCondition,
            sourceNode,
            smtAnalysis,
            "runtime.hazard.trigger",
            "runtime-hazard-trigger");
        if (formulaTruth.Info.Status == SymbolicProofStatus.ProvenTrue)
            return (SymbolicRuntimeHazardStatus.Proven, formulaTruth.Info.Reason, formulaTruth.Info);

        if (formulaTruth.Info.Status == SymbolicProofStatus.ProvenFalse ||
            formulaTruth.Info.Status == SymbolicProofStatus.Unreachable)
            return (SymbolicRuntimeHazardStatus.Unreachable, formulaTruth.Info.Reason, formulaTruth.Info);

        return (SymbolicRuntimeHazardStatus.Unknown, formulaTruth.Info.Reason, formulaTruth.Info);
    }

    private static bool IsFallbackDerivedTriggerPrecondition(SymbolicFact? triggerPrecondition)
    {
        if (triggerPrecondition == null) return false;

        return triggerPrecondition.Provenance.EndsWith(".formula-fallback", StringComparison.Ordinal) ||
               triggerPrecondition.Provenance.EndsWith(".fallback", StringComparison.Ordinal);
    }

    private static bool TryClassifyIrTrigger(
        SymbolicProgramPointAnalysis analysis,
        SymbolicFact triggerPrecondition,
        SmtAnalysisService smtAnalysis,
        out (SymbolicRuntimeHazardStatus Status, string Reason, SymbolicProofInfo Proof) result)
    {
        var proof = SymbolicReachabilityService.ClassifyStateHazardTrigger(
            analysis.PathState,
            triggerPrecondition,
            smtAnalysis);
        if (proof.Info.Status == SymbolicProofStatus.ProvenTrue)
        {
            result = (SymbolicRuntimeHazardStatus.Proven, proof.Info.Reason, proof.Info);
            return true;
        }

        if (proof.Info.Status == SymbolicProofStatus.Unreachable)
        {
            result = (SymbolicRuntimeHazardStatus.Unreachable, proof.Info.Reason, proof.Info);
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
        SymbolicProofInfo? proofInfo,
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
        Proof = CreateProofInfo(status, statusReason, category, triggerCondition, kind, proofInfo);
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

    public string GetDisplayStatusReason()
    {
        if (string.IsNullOrWhiteSpace(StatusReason)) return StatusReason;

        return StatusReason switch
        {
            "unsupported_formula_fallback" =>
                "unsupported formula fallback; legacy translated trigger was not trusted as proof",
            "unsupported_typed_projection" =>
                "runtime-hazard trigger could not be projected to typed symbolic IR",
            "smt_disabled" => "SMT disabled",
            "smt_disposed" => "SMT solver disposed",
            "smt_timeout" => "SMT solver timed out",
            "smt_unavailable" => "SMT solver unavailable",
            "smt_encoding_failure" => "SMT formula encoding failed",
            "smt_expression_budget_exceeded" => "SMT expression node budget exceeded",
            "smt_path_condition_budget_exceeded" => "SMT path condition budget exceeded",
            "smt_method_budget_exceeded" => "SMT method-level budget exceeded",
            "trigger_always_true" => "trigger condition is always true",
            "trigger_always_false" => "trigger condition is always false",
            _ => StatusReason
        };
    }

    private static SymbolicProofStatus MapProofStatus(SymbolicRuntimeHazardStatus status)
    {
        return status switch
        {
            SymbolicRuntimeHazardStatus.Proven => SymbolicProofStatus.ProvenTrue,
            SymbolicRuntimeHazardStatus.Unreachable => SymbolicProofStatus.Unreachable,
            _ => SymbolicProofStatus.Unknown
        };
    }

    private static SymbolicProofInfo CreateProofInfo(
        SymbolicRuntimeHazardStatus status,
        string statusReason,
        string category,
        string triggerCondition,
        SymbolicRuntimeHazardKind kind,
        SymbolicProofInfo? proofInfo)
    {
        if (proofInfo == null)
            return new SymbolicProofInfo(
                MapProofStatus(status),
                ResolveProofBackend(status, statusReason),
                ResolveUnknownReason(status, statusReason),
                statusReason,
                false,
                null,
                category,
                triggerCondition,
                kind.ToString());

        return new SymbolicProofInfo(
            MapProofStatus(status),
            proofInfo.Backend,
            status is SymbolicRuntimeHazardStatus.Proven or SymbolicRuntimeHazardStatus.Unreachable
                ? SymbolicUnknownReason.None
                : proofInfo.UnknownReason,
            string.IsNullOrWhiteSpace(statusReason) ? proofInfo.Reason : statusReason,
            proofInfo.CacheHit,
            proofInfo.Budget,
            category,
            triggerCondition,
            kind.ToString());
    }

    private static SymbolicProofBackend ResolveProofBackend(
        SymbolicRuntimeHazardStatus status,
        string statusReason)
    {
        return status == SymbolicRuntimeHazardStatus.Unsupported ||
               string.Equals(statusReason, "unsupported_formula_fallback", StringComparison.Ordinal) ||
               string.Equals(statusReason, "unsupported_typed_projection", StringComparison.Ordinal)
            ? SymbolicProofBackend.None
            : SymbolicProofBackend.Smt;
    }

    private static SymbolicUnknownReason ResolveUnknownReason(
        SymbolicRuntimeHazardStatus status,
        string reason)
    {
        if (status is SymbolicRuntimeHazardStatus.Proven or SymbolicRuntimeHazardStatus.Unreachable)
            return SymbolicUnknownReason.None;

        return SymbolicUnknownReasonClassifier.Classify(reason);
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
    InvalidCollectionCardinality
}

public enum SymbolicRuntimeHazardStatus
{
    Proven,
    Unreachable,
    Unknown,
    Unsupported
}
