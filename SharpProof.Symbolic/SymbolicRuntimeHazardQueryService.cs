using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SharpProof.ProofCore.Purity;
using SharpProof.ProofCore.Smt;
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
        SymbolicRuntimeHazardQueryOptions? options = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var source = SymbolicSourceFile.Load(filePath);
        return QuerySourceRuntimeHazards(
            source.Text,
            source.FilePath,
            smtAnalysis,
            references,
            cancellationToken,
            options,
            compilationProfile);
    }

    public SymbolicRuntimeHazardQueryResult QueryFileRuntimeHazardsLine(
        string filePath,
        int line,
        SmtAnalysisService smtAnalysis,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SymbolicRuntimeHazardQueryOptions? options = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var source = SymbolicSourceFile.Load(filePath);
        return QuerySourceRuntimeHazardsLine(
            source.Text,
            source.FilePath,
            line,
            smtAnalysis,
            references,
            cancellationToken,
            options,
            compilationProfile);
    }

    public SymbolicRuntimeHazardQueryResult QueryFileRuntimeHazardsSpan(
        string filePath,
        int spanStart,
        int spanEnd,
        SmtAnalysisService smtAnalysis,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SymbolicRuntimeHazardQueryOptions? options = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var source = SymbolicSourceFile.Load(filePath);
        return QuerySourceRuntimeHazardsSpan(
            source.Text,
            source.FilePath,
            spanStart,
            spanEnd,
            smtAnalysis,
            references,
            cancellationToken,
            options,
            compilationProfile);
    }

    public SymbolicRuntimeHazardQueryResult QuerySourceRuntimeHazards(
        string sourceText,
        string filePath,
        SmtAnalysisService smtAnalysis,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SymbolicRuntimeHazardQueryOptions? options = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var (syntaxTree, compilation) = SymbolicSourceCompilation.CreateRuntimeHazards(
            sourceText,
            filePath,
            references,
            cancellationToken,
            compilationProfile);
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
        SymbolicRuntimeHazardQueryOptions? options = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var (syntaxTree, compilation) = SymbolicSourceCompilation.CreateRuntimeHazards(
            sourceText,
            filePath,
            references,
            cancellationToken,
            compilationProfile);
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
        SymbolicRuntimeHazardQueryOptions? options = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var (syntaxTree, compilation) = SymbolicSourceCompilation.CreateRuntimeHazards(
            sourceText,
            filePath,
            references,
            cancellationToken,
            compilationProfile);
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
            RuntimeHazardScope.All,
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
            new RuntimeHazardScope(lineSpan, line),
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
            new RuntimeHazardScope(sourceSpan, null),
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

        return QueryRuntimeHazardsCore(
            node.SyntaxTree,
            semanticModel,
            node,
            new RuntimeHazardScope(node.Span, null),
            smtAnalysis,
            cancellationToken,
            options,
            includeNestedCallables);
    }

    internal SymbolicRuntimeHazardQueryResult QueryNodeRuntimeHazardsWithInitialState(
        SyntaxNode node,
        SemanticModel semanticModel,
        SmtAnalysisService smtAnalysis,
        SymbolicState initialState,
        CancellationToken cancellationToken = default,
        SymbolicRuntimeHazardQueryOptions? options = null,
        bool includeNestedCallables = false)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        if (initialState == null) throw new ArgumentNullException(nameof(initialState));

        return QueryRuntimeHazardsCore(
            node.SyntaxTree,
            semanticModel,
            node,
            new RuntimeHazardScope(node.Span, null),
            smtAnalysis,
            cancellationToken,
            options,
            includeNestedCallables,
            initialState);
    }

    private SymbolicRuntimeHazardQueryResult QuerySyntaxTreeRuntimeHazardsCore(
        SyntaxTree syntaxTree,
        Compilation compilation,
        RuntimeHazardScope scope,
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
            smtAnalysis,
            cancellationToken,
            options,
            true);
    }

    private SymbolicRuntimeHazardQueryResult QueryRuntimeHazardsCore(
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        SyntaxNode root,
        RuntimeHazardScope scope,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken,
        SymbolicRuntimeHazardQueryOptions? options,
        bool includeNestedCallables,
        SymbolicState? initialState = null)
    {
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));

        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));

        if (root == null) throw new ArgumentNullException(nameof(root));

        if (smtAnalysis == null) throw new ArgumentNullException(nameof(smtAnalysis));

        options ??= SymbolicRuntimeHazardQueryOptions.Default;
        var hazards = SymbolicRuntimeHazardCandidateFactory
            .EnumerateCandidates(root, semanticModel, cancellationToken, includeNestedCallables)
            .Where(candidate => !scope.Span.HasValue || candidate.Site.Span.IntersectsWith(scope.Span.Value))
            .Where(candidate => options.Includes(candidate.Kind))
            .Select(candidate => ClassifyCandidate(
                syntaxTree,
                semanticModel,
                candidate,
                smtAnalysis,
                cancellationToken,
                initialState))
            .Where(hazard => options.IncludeUnprovenCandidates || hazard.Status == SymbolicRuntimeHazardStatus.Proven)
            .OrderBy(static hazard => hazard.SpanStart)
            .ThenBy(static hazard => hazard.Kind.ToString(), StringComparer.Ordinal)
            .ToArray();

        var sourceText = syntaxTree.GetText(cancellationToken);
        return new SymbolicRuntimeHazardQueryResult(
            syntaxTree.FilePath,
            sourceText.Lines.Count,
            scope.Span?.Start,
            scope.Span?.End,
            scope.RequestedLine,
            hazards,
            SymbolicSmtDiagnostics.FromService(smtAnalysis));
    }

    private readonly record struct RuntimeHazardScope(TextSpan? Span, int? RequestedLine)
    {
        public static RuntimeHazardScope All { get; } = new(null, null);
    }

    private SymbolicRuntimeHazard ClassifyCandidate(
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        RuntimeHazardCandidate candidate,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken,
        SymbolicState? initialState)
    {
        var analysis = _invariantService.AnalyzeAt(
            candidate.Site,
            semanticModel,
            smtAnalysis,
            cancellationToken,
            initialState: initialState);
        var triggerPrecondition = candidate.TriggerPrecondition;
        var triggerCondition = GetTriggerCondition(triggerPrecondition);
        var exceptionType = candidate.ExceptionType;
        var category = candidate.Category;

        var (status, reason, proofInfo, triggerProof) = ClassifyTriggerCore(
            analysis,
            triggerCondition,
            triggerPrecondition,
            smtAnalysis);
        var lineColumn =
            SymbolicSourceLocation.GetLineAndColumn(syntaxTree, candidate.Site.SpanStart, cancellationToken);
        var sourceSpan = SymbolicSourceLocation.GetNodeSourceSpan(syntaxTree, candidate.Site.Span, cancellationToken);
        var triggerWitness = CreateTriggerWitness(
            analysis,
            triggerCondition,
            triggerProof,
            smtAnalysis,
            semanticModel,
            candidate.Site.SpanStart,
            reason);

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
            SymbolicInvariantService.FormatCondition(triggerCondition),
            SymbolicFactInfo.FromFact(triggerPrecondition),
            analysis.MergedInvariantText,
            analysis.Facts,
            SymbolicFactInfo.Distinct(
                SymbolicFactInfo.FromState(analysis.PathState).Concat(
                    new[] { SymbolicFactInfo.FromFact(triggerPrecondition) })),
            analysis.Reachability,
            analysis.ReachabilityReason,
            proofInfo,
            SymbolicSmtDiagnostics.FromService(smtAnalysis),
            triggerWitness,
            analysis.Truncation);
    }

    private static SymbolicInputWitness CreateTriggerWitness(
        SymbolicProgramPointAnalysis analysis,
        SymbolicCondition triggerCondition,
        SymbolicIrProofResult? triggerProof,
        SmtAnalysisService smtAnalysis,
        SemanticModel semanticModel,
        int position,
        string reason)
    {
        var rawProof = triggerProof?.RawResult;
        if (analysis.Reachability == SymbolicReachability.Unreachable ||
            rawProof?.ImpurityCheck.Feasibility == Feasibility.Unsatisfiable)
            return SymbolicInputWitnessFactory.None(reason);

        if (!SymbolicIrFormulaEncoder.TryEncode(triggerCondition, out var encodedTrigger))
            return SymbolicInputWitnessFactory.None("unsupported_typed_projection");

        var triggerFeasibility = SymbolicReachabilityService.ClassifyStateBranchFeasibility(
            analysis.PathState,
            triggerCondition,
            smtAnalysis);
        if (triggerFeasibility.Info.Status == SymbolicProofStatus.Unreachable)
            return SymbolicInputWitnessFactory.None(triggerFeasibility.Info.Reason);

        return SymbolicInputWitnessFactory.Create(
            triggerFeasibility.RawResult?.PathCheck.Witness,
            analysis.PathConditions.Concat(new[] { encodedTrigger }),
            semanticModel,
            position,
            SymbolicWitnessStatus.Unsupported,
            rawProof?.Reason ?? reason);
    }

    private static SymbolicCondition GetTriggerCondition(SymbolicFact triggerPrecondition)
    {
        return triggerPrecondition.Atom is SymbolicExceptionPreconditionAtom precondition
            ? precondition.Trigger
            : new SymbolicFactCondition(triggerPrecondition);
    }

    internal static (
        SymbolicRuntimeHazardStatus Status,
        string Reason,
        SymbolicProofInfo? Proof,
        SymbolicIrProofResult? RawProof) ClassifyTriggerCore(
        SymbolicProgramPointAnalysis analysis,
        SymbolicCondition triggerCondition,
        SymbolicFact triggerPrecondition,
        SmtAnalysisService smtAnalysis)
    {
        if (analysis.Reachability == SymbolicReachability.Unreachable)
            return (SymbolicRuntimeHazardStatus.Unreachable, analysis.ReachabilityReason, null, null);

        if (analysis.Reachability == SymbolicReachability.Unknown)
            return (SymbolicRuntimeHazardStatus.Unknown, analysis.ReachabilityReason, null, null);

        if (!smtAnalysis.Options.IsEnabled)
            return (SymbolicRuntimeHazardStatus.Unsupported, "smt_disabled", null, null);

        if (triggerCondition is SymbolicConstantCondition { Value: true })
            return (SymbolicRuntimeHazardStatus.Proven, "trigger_always_true", null, null);

        if (triggerCondition is SymbolicConstantCondition { Value: false })
            return (SymbolicRuntimeHazardStatus.Unreachable, "trigger_always_false", null, null);

        if (triggerPrecondition is { Confidence: SymbolicFactConfidence.Unsupported })
            return (SymbolicRuntimeHazardStatus.Unknown, "unsupported_typed_projection", null, null);

        return ClassifyIrTrigger(analysis, triggerPrecondition, smtAnalysis);
    }

    private static (
        SymbolicRuntimeHazardStatus Status,
        string Reason,
        SymbolicProofInfo Proof,
        SymbolicIrProofResult RawProof) ClassifyIrTrigger(
        SymbolicProgramPointAnalysis analysis,
        SymbolicFact triggerPrecondition,
        SmtAnalysisService smtAnalysis)
    {
        var proof = SymbolicReachabilityService.ClassifyStateHazardTrigger(
            analysis.PathState,
            triggerPrecondition,
            smtAnalysis);
        if (proof.Info.Status == SymbolicProofStatus.ProvenTrue)
            return (SymbolicRuntimeHazardStatus.Proven, proof.Info.Reason, proof.Info, proof);

        if (proof.Info.Status == SymbolicProofStatus.Unreachable)
            return (SymbolicRuntimeHazardStatus.Unreachable, proof.Info.Reason, proof.Info, proof);

        return (SymbolicRuntimeHazardStatus.Unknown, proof.Info.Reason, proof.Info, proof);
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
        AnalysisTruncation = SymbolicAnalysisTruncationInfo.Combine(
            Hazards.Select(static hazard => hazard.AnalysisTruncation));
        SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        TriggerWitnesses = Hazards.Select(static hazard => hazard.TriggerWitness).ToArray();
        InputDomainSummary = SymbolicInputWitnessFactory.MergeAlternatives(TriggerWitnesses);
    }

    public string FilePath { get; }

    public int LineCount { get; }

    public int? ScopeStart { get; }

    public int? ScopeEnd { get; }

    public int? Line { get; }

    public IReadOnlyList<SymbolicRuntimeHazard> Hazards { get; }

    public int HazardCount => Hazards.Count;

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; }

    public SymbolicSmtDiagnostics SmtDiagnostics { get; }

    public IReadOnlyList<SymbolicInputWitness> TriggerWitnesses { get; }

    public SymbolicInputDomainSummary InputDomainSummary { get; }

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
        SymbolicSmtDiagnostics? smtDiagnostics = null,
        SymbolicInputWitness? triggerWitness = null,
        SymbolicAnalysisTruncationInfo? analysisTruncation = null)
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
        TriggerWitness = triggerWitness ?? SymbolicInputWitnessFactory.Unsupported(
            "runtime_hazard_trigger_witness_unavailable");
        Proof = CreateProofInfo(status, statusReason, category, triggerCondition, kind, proofInfo);
        UnknownReasonInfo = SymbolicUnknownReasonTaxonomy.ForRuntimeHazard(
            status,
            StatusReason,
            Proof.UnknownReason);
        InvariantInfo = new SymbolicInvariantInfo(
            MergedInvariantText,
            SymbolicFacts,
            new[] { Proof },
            SymbolicInvariantMergeKind.Conjunction,
            PathConditionCount);
        SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        AnalysisTruncation = analysisTruncation ?? SymbolicAnalysisTruncationInfo.None;
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

    public SymbolicUnknownReasonInfo UnknownReasonInfo { get; }

    public SymbolicInvariantInfo InvariantInfo { get; }

    public SymbolicReachability Reachability { get; }

    public string ReachabilityReason { get; }

    public SymbolicSmtDiagnostics SmtDiagnostics { get; }

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; }

    public SymbolicInputWitness TriggerWitness { get; }

    public string GetDisplayStatusReason()
    {
        return SymbolicReasonDisplay.Format(StatusReason);
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
        var proofStatus = MapProofStatus(status);
        if (proofInfo == null)
        {
            var isSolverBacked = status != SymbolicRuntimeHazardStatus.Unsupported &&
                                 !string.Equals(
                                     statusReason,
                                     "unsupported_typed_projection",
                                     StringComparison.Ordinal);
            return SymbolicProofProjection
                .FromSolverBackedResult(
                    proofStatus,
                    isSolverBacked,
                    proofStatus == SymbolicProofStatus.Unknown ? statusReason : null)
                .CreateInfo(
                    statusReason,
                    false,
                    null,
                    category,
                    triggerCondition,
                    kind.ToString());
        }

        return SymbolicProofProjection
            .FromExisting(proofStatus, proofInfo)
            .CreateInfo(
                string.IsNullOrWhiteSpace(statusReason) ? proofInfo.Reason : statusReason,
                proofInfo.CacheHit,
                proofInfo.Budget,
                category,
                triggerCondition,
                kind.ToString());
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
