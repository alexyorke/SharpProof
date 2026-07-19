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

    internal SymbolicRuntimeHazardQueryResult QuerySyntaxTreeRuntimeHazards(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SharpProofTarget target,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default,
        SymbolicRuntimeHazardQueryOptions? options = null)
    {
        var scope = target.Kind switch
        {
            SharpProofTargetKind.Line or SharpProofTargetKind.Point => new RuntimeHazardScope(
                SymbolicSourceLocation.GetLineSpan(syntaxTree, target.Line!.Value, cancellationToken),
                target.Line),
            SharpProofTargetKind.Span => new RuntimeHazardScope(
                SymbolicSourceLocation.GetSourceSpan(
                    syntaxTree, target.SpanStart!.Value, target.SpanEnd!.Value, cancellationToken),
                null),
            SharpProofTargetKind.AllLines => RuntimeHazardScope.All,
            _ => throw new NotSupportedException("Target kind is not supported for runtime hazard queries.")
        };
        return QuerySyntaxTreeRuntimeHazardsCore(
            syntaxTree,
            compilation,
            scope,
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
        var descriptor = candidate.Operation;
        var triggerPrecondition = descriptor.ToPreconditionFact();
        var triggerCondition = descriptor.Trigger;

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
            descriptor,
            status,
            reason,
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
        SymbolicProofInfo? triggerProof,
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

        var triggerFeasibility = new SymbolicProofService(smtAnalysis)
            .ClassifyBranchFeasibility(analysis.PathState, triggerCondition);
        if (triggerFeasibility.Status == SymbolicProofStatus.Unreachable)
            return SymbolicInputWitnessFactory.None(triggerFeasibility.Reason);

        return SymbolicInputWitnessFactory.Create(
            triggerFeasibility.RawResult?.PathCheck.Witness,
            analysis.PathConditions.Concat(new[] { encodedTrigger }),
            semanticModel,
            position,
            SymbolicWitnessStatus.Unsupported,
            rawProof?.Reason ?? reason);
    }

    internal static (
        SymbolicRuntimeHazardStatus Status,
        string Reason,
        SymbolicProofInfo? Proof,
        SymbolicProofInfo? RawProof) ClassifyTriggerCore(
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
        SymbolicProofInfo RawProof) ClassifyIrTrigger(
        SymbolicProgramPointAnalysis analysis,
        SymbolicFact triggerPrecondition,
        SmtAnalysisService smtAnalysis)
    {
        var proof = new SymbolicProofService(smtAnalysis)
            .ClassifyHazardTrigger(analysis.PathState, triggerPrecondition);
        if (proof.Status == SymbolicProofStatus.ProvenTrue)
            return (SymbolicRuntimeHazardStatus.Proven, proof.Reason, proof, proof);

        if (proof.Status == SymbolicProofStatus.Unreachable)
            return (SymbolicRuntimeHazardStatus.Unreachable, proof.Reason, proof, proof);

        return (SymbolicRuntimeHazardStatus.Unknown, proof.Reason, proof, proof);
    }
}

internal sealed class SymbolicRuntimeHazardQueryOptions(
    bool includeUnprovenCandidates = false,
    IEnumerable<SymbolicRuntimeHazardKind>? kinds = null)
{
    public static readonly SymbolicRuntimeHazardQueryOptions Default = new();

    public bool IncludeUnprovenCandidates { get; } = includeUnprovenCandidates;
    public ImmutableHashSet<SymbolicRuntimeHazardKind> Kinds { get; } =
        kinds?.ToImmutableHashSet() ?? ImmutableHashSet<SymbolicRuntimeHazardKind>.Empty;

    public bool Includes(SymbolicRuntimeHazardKind kind)
    {
        return Kinds.Count == 0 || Kinds.Contains(kind);
    }
}

internal sealed class SymbolicRuntimeHazardQueryResult(
    string filePath,
    int lineCount,
    int? scopeStart,
    int? scopeEnd,
    int? line,
    IReadOnlyList<SymbolicRuntimeHazard> hazards,
    SymbolicSmtDiagnostics? smtDiagnostics = null)
{
    public string FilePath { get; } = filePath;

    public int LineCount { get; } = lineCount;

    public int? ScopeStart { get; } = scopeStart;

    public int? ScopeEnd { get; } = scopeEnd;

    public int? Line { get; } = line;

    public IReadOnlyList<SymbolicRuntimeHazard> Hazards { get; } =
        hazards ?? throw new ArgumentNullException(nameof(hazards));

    public int HazardCount => Hazards.Count;

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; } =
        SymbolicAnalysisTruncationInfo.Combine(
            (hazards ?? throw new ArgumentNullException(nameof(hazards)))
            .Select(static hazard => hazard.AnalysisTruncation));

    public SymbolicSmtDiagnostics SmtDiagnostics { get; } =
        smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;

    public IReadOnlyList<SymbolicInputWitness> TriggerWitnesses { get; } =
        (hazards ?? throw new ArgumentNullException(nameof(hazards)))
        .Select(static hazard => hazard.TriggerWitness).ToArray();

    public SymbolicInputDomainSummary InputDomainSummary { get; } =
        SymbolicInputWitnessFactory.MergeAlternatives(
            (hazards ?? throw new ArgumentNullException(nameof(hazards)))
            .Select(static hazard => hazard.TriggerWitness).ToArray());

}

internal sealed class SymbolicRuntimeHazard(
    string filePath,
    SymbolicHazardOperation descriptor,
    SymbolicRuntimeHazardStatus status,
    string statusReason,
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
    public string FilePath { get; } = filePath;

    internal SymbolicHazardOperation Descriptor { get; } =
        descriptor ?? throw new ArgumentNullException(nameof(descriptor));

    public SymbolicRuntimeHazardKind Kind { get; } = descriptor.HazardKind;

    public SymbolicRuntimeHazardStatus Status { get; } = status;

    public string StatusReason { get; } = statusReason;

    public string ExceptionType { get; } = descriptor.ExceptionType;

    public string Category { get; } = descriptor.Category;

    public string NodeKind { get; } = nodeKind;

    public string OperationText { get; } = operationText;

    public int SpanStart { get; } = spanStart;

    public int SpanEnd { get; } = spanEnd;

    public int SpanLength { get; } = spanEnd - spanStart;

    public int Line { get; } = line;

    public int Column { get; } = column;

    public int NodeStartLine { get; } = nodeStartLine;

    public int NodeStartColumn { get; } = nodeStartColumn;

    public int NodeEndLine { get; } = nodeEndLine;

    public int NodeEndColumn { get; } = nodeEndColumn;

    public string TriggerCondition { get; } = triggerCondition;

    public SymbolicFactInfo? TriggerPrecondition { get; } = triggerPrecondition;

    public string MergedInvariantText { get; } = mergedInvariantText;

    internal IReadOnlyList<string> PathConditions { get; } =
        pathConditions ?? throw new ArgumentNullException(nameof(pathConditions));

    public int PathConditionCount { get; } = pathConditions.Count;

    public IReadOnlyList<SymbolicFactInfo> SymbolicFacts { get; } =
        symbolicFacts ?? throw new ArgumentNullException(nameof(symbolicFacts));

    public SymbolicProofInfo Proof { get; } = CreateProofInfo(
        status, statusReason, descriptor.Category, triggerCondition, descriptor.HazardKind, proofInfo);

    public SymbolicUnknownReasonInfo UnknownReasonInfo => SymbolicUnknownReasonTaxonomy.ForRuntimeHazard(
        Status, StatusReason, Proof.UnknownReason);

    public SymbolicInvariantInfo InvariantInfo => new(
        MergedInvariantText,
        SymbolicFacts,
        new[] { Proof },
        SymbolicInvariantMergeKind.Conjunction,
        PathConditionCount);

    public SymbolicReachability Reachability { get; } = reachability;

    public string ReachabilityReason { get; } = reachabilityReason;

    public SymbolicSmtDiagnostics SmtDiagnostics { get; } =
        smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; } =
        analysisTruncation ?? SymbolicAnalysisTruncationInfo.None;

    public SymbolicInputWitness TriggerWitness { get; } = triggerWitness ??
        SymbolicInputWitnessFactory.Unsupported("runtime_hazard_trigger_witness_unavailable");

    public string GetDisplayStatusReason()
    {
        return SymbolicReasonDisplay.Format(StatusReason);
    }

    private static SymbolicProofInfo CreateProofInfo(
        SymbolicRuntimeHazardStatus status,
        string statusReason,
        string category,
        string triggerCondition,
        SymbolicRuntimeHazardKind kind,
        SymbolicProofInfo? proofInfo)
    {
        var proofStatus = SymbolicProofInfo.MapStatus(status);
        if (proofInfo == null)
        {
            var isSolverBacked = status != SymbolicRuntimeHazardStatus.Unsupported &&
                                 !string.Equals(
                                     statusReason,
                                     "unsupported_typed_projection",
                                     StringComparison.Ordinal);
            return SymbolicProofInfo.Project(
                proofStatus,
                isSolverBacked,
                statusReason,
                false,
                null,
                category,
                triggerCondition,
                kind.ToString(),
                proofStatus == SymbolicProofStatus.Unknown ? statusReason : null);
        }

        return SymbolicProofInfo.Project(
            proofStatus,
            proofInfo,
            string.IsNullOrWhiteSpace(statusReason) ? proofInfo.Reason : statusReason,
            category,
            triggerCondition,
            kind.ToString());
    }
}

internal enum SymbolicRuntimeHazardKind
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

internal enum SymbolicRuntimeHazardStatus
{
    Proven,
    Unreachable,
    Unknown,
    Unsupported
}
