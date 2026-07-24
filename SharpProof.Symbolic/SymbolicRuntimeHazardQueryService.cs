namespace SharpProof.Symbolic;
internal sealed partial class SymbolicRuntimeHazardQueryService {
    private readonly SymbolicInvariantService _invariantService;
    public SymbolicRuntimeHazardQueryService()
        : this(new SymbolicInvariantService()) {
    }
    internal SymbolicRuntimeHazardQueryService(SymbolicInvariantService invariantService)
        => _invariantService = invariantService ?? throw new ArgumentNullException(nameof(invariantService));
    internal SymbolicRuntimeHazardQueryResult QuerySyntaxTreeRuntimeHazards(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SharpProofTarget target,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default,
        SymbolicRuntimeHazardQueryOptions? options = null) {
        var source = SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation);
        return QuerySyntaxTreeRuntimeHazards(
            ResolvedQueryTarget.Create(source, target, cancellationToken),
            smtAnalysis,
            cancellationToken,
            options);
    }
    internal SymbolicRuntimeHazardQueryResult QuerySyntaxTreeRuntimeHazards(
        ResolvedQueryTarget target,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default,
        SymbolicRuntimeHazardQueryOptions? options = null) {
        var scope = target.Target.Kind switch {
            SharpProofTargetKind.Point or SharpProofTargetKind.Position =>
                new RuntimeHazardScope(target.PositionContainer.Span, target.Position),
            SharpProofTargetKind.Line or SharpProofTargetKind.Span => new RuntimeHazardScope(target.Span),
            SharpProofTargetKind.AllLines => RuntimeHazardScope.All,
            _ => throw new NotSupportedException("Target kind is not supported for runtime hazard queries.")
        };
        return QueryRuntimeHazardsCore(
            target.Source.SyntaxTree, target.SemanticModel, target.Root, scope, smtAnalysis, cancellationToken, options, true);
    }
    public SymbolicRuntimeHazardQueryResult QueryNodeRuntimeHazards(
        SyntaxNode node,
        SemanticModel semanticModel,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default,
        SymbolicRuntimeHazardQueryOptions? options = null,
        bool includeNestedCallables = false) {
        if (node == null) throw new ArgumentNullException(nameof(node));
        return QueryRuntimeHazardsCore(
            node.SyntaxTree,
            semanticModel,
            node,
            new RuntimeHazardScope(node.Span),
            smtAnalysis,
            cancellationToken,
            options,
            includeNestedCallables);
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
        SymbolicState? initialState = null) {
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));
        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (smtAnalysis == null) throw new ArgumentNullException(nameof(smtAnalysis));
        options ??= SymbolicRuntimeHazardQueryOptions.Default;
        var hazards = SymbolicRuntimeHazardCandidateFactory
            .EnumerateCandidates(root, semanticModel, cancellationToken, includeNestedCallables)
            .Where(candidate => !scope.Span.HasValue || candidate.Site.Span.IntersectsWith(scope.Span.Value))
            .Where(candidate => !scope.Position.HasValue || candidate.Site.Span.Contains(scope.Position.Value))
            .Where(candidate => options.Includes(candidate.Kind))
            .Select(candidate => ClassifyCandidate(semanticModel, candidate, smtAnalysis, cancellationToken, initialState))
            .Where(hazard => options.IncludeUnprovenCandidates || hazard.Status == SymbolicRuntimeHazardStatus.Proven)
            .OrderBy(static hazard => hazard.SpanStart)
            .ThenBy(static hazard => hazard.Kind.ToString(), StringComparer.Ordinal)
            .ToArray();
        return new SymbolicRuntimeHazardQueryResult(hazards);
    }
    readonly record struct RuntimeHazardScope(TextSpan? Span, int? Position = null) {
        public static RuntimeHazardScope All { get; } = new(null);
    }
    private SymbolicRuntimeHazard ClassifyCandidate(
        SemanticModel semanticModel,
        RuntimeHazardCandidate candidate,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken,
        SymbolicState? initialState) {
        var analysis = _invariantService.AnalyzeAt(
            candidate.Site,
            semanticModel,
            smtAnalysis,
            cancellationToken,
            initialState: initialState);
        var descriptor = candidate.Operation;
        var triggerPrecondition = descriptor.ToPreconditionFact();
        var triggerCondition = descriptor.Trigger;
        var (status, reason, proofInfo, triggerProof) = ClassifyTriggerCore(analysis, triggerCondition, triggerPrecondition, smtAnalysis);
        var triggerWitness = CreateTriggerWitness(
            analysis,
            triggerCondition,
            triggerProof,
            smtAnalysis,
            semanticModel,
            candidate.Site.SpanStart,
            reason);
        return new SymbolicRuntimeHazard(
            descriptor,
            status,
            reason,
            candidate.Site.ToString(),
            candidate.Site.SpanStart,
            candidate.Site.Span.End,
            proofInfo,
            triggerWitness,
            analysis.AnalysisTruncation);
    }
    private static SymbolicInputWitness CreateTriggerWitness(
        SymbolicProgramPointAnalysis analysis,
        SymbolicCondition triggerCondition,
        SymbolicProofInfo? triggerProof,
        SmtAnalysisService smtAnalysis,
        SemanticModel semanticModel,
        int position,
        string reason) {
        var rawProof = triggerProof?.RawResult;
        if (analysis.Reachability == SymbolicReachability.Unreachable ||
            rawProof?.HazardCheck.Feasibility == Feasibility.Unsatisfiable)
            return SymbolicInputWitnessFactory.None(reason);
        if (!SymbolicIrFormulaEncoder.TryEncode(triggerCondition, out var encodedTrigger))
            return SymbolicInputWitnessFactory.None("unsupported_typed_projection");
        var triggerFeasibility = new SymbolicProofService(smtAnalysis)
            .ClassifyBranchFeasibility(analysis.PathState, triggerCondition);
        if (triggerFeasibility.Status == SymbolicProofStatus.Unreachable)
            return SymbolicInputWitnessFactory.None(triggerFeasibility.Reason);
        return SymbolicInputWitnessFactory.Create(
            triggerFeasibility.RawResult?.PathCheck.Witness,
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
        SmtAnalysisService smtAnalysis) {
        if (analysis.Reachability == SymbolicReachability.Unreachable)
            return (SymbolicRuntimeHazardStatus.Unreachable, analysis.ReachabilityReason, null, null);
        if (analysis.Reachability == SymbolicReachability.Unknown)
            return (SymbolicRuntimeHazardStatus.Unknown, analysis.ReachabilityReason, null, null);
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
        SmtAnalysisService smtAnalysis) {
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
    IEnumerable<SymbolicRuntimeHazardKind>? kinds = null) {
    public static readonly SymbolicRuntimeHazardQueryOptions Default = new();
    public bool IncludeUnprovenCandidates { get; } = includeUnprovenCandidates;
    public ImmutableHashSet<SymbolicRuntimeHazardKind> Kinds { get; } =
        kinds?.ToImmutableHashSet() ?? [];
    public bool Includes(SymbolicRuntimeHazardKind kind) =>
        Kinds.Count == 0 || Kinds.Contains(kind);
}
internal sealed record SymbolicRuntimeHazardQueryResult(IReadOnlyList<SymbolicRuntimeHazard> Hazards) {
    public SymbolicAnalysisTruncationInfo AnalysisTruncation =>
        SymbolicAnalysisTruncationInfo.Combine(Hazards.Select(static hazard => hazard.AnalysisTruncation));
}
internal sealed record SymbolicRuntimeHazard(
    SymbolicHazardOperation Descriptor,
    SymbolicRuntimeHazardStatus Status,
    string StatusReason,
    string OperationText,
    int SpanStart,
    int SpanEnd,
    SymbolicProofInfo? RawProofInfo,
    SymbolicInputWitness TriggerWitness,
    SymbolicAnalysisTruncationInfo AnalysisTruncation) {
    public SymbolicRuntimeHazardKind Kind => Descriptor.HazardKind;
    public string ExceptionType => Descriptor.ExceptionType;
    public string Category => Descriptor.Category;
    public SymbolicUnknownReasonInfo UnknownReasonInfo => SymbolicUnknownReasonTaxonomy.ForRuntimeHazard(
        Status,
        StatusReason,
        RawProofInfo?.UnknownReason ?? (Status is SymbolicRuntimeHazardStatus.Unknown or
            SymbolicRuntimeHazardStatus.Unsupported
            ? SymbolicUnknownReasonClassifier.Classify(StatusReason)
            : SymbolicUnknownReason.None));
}
internal enum SymbolicRuntimeHazardKind {
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
internal enum SymbolicRuntimeHazardStatus {
    Proven,
    Unreachable,
    Unknown,
    Unsupported
}
