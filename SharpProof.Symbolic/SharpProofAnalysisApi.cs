namespace SharpProof.Symbolic;

public enum SharpProofQueryKind
{
    SourceLocation,
    Method,
    Invariant,
    Reachability,
    Condition,
    RuntimeHazards,
    Capabilities,
    Complexity
}

public enum SharpProofQueryStatus
{
    Succeeded,
    Unknown,
    Failed,
    Canceled
}

public enum SharpProofTargetKind
{
    Point,
    Position,
    Line,
    Span,
    LineSpan,
    AllLines,
    Node
}

public sealed record SharpProofTarget(
    SharpProofTargetKind Kind,
    int? Line = null,
    int? Column = null,
    int? Position = null,
    int? SpanStart = null,
    int? SpanEnd = null,
    int? StartLine = null,
    int? StartColumn = null,
    int? EndLine = null,
    int? EndColumn = null,
    bool IncludeNestedCallables = false)
{
    public static SharpProofTarget Point(int line, int column = 1)
    {
        ValidatePositive(line, nameof(line));
        ValidatePositive(column, nameof(column));
        return new SharpProofTarget(SharpProofTargetKind.Point, Line: line, Column: column);
    }

    public static SharpProofTarget AtPosition(int position)
    {
        ValidateNonNegative(position, nameof(position));
        return new SharpProofTarget(SharpProofTargetKind.Position, Position: position);
    }

    public static SharpProofTarget LineNumber(int line)
    {
        ValidatePositive(line, nameof(line));
        return new SharpProofTarget(SharpProofTargetKind.Line, Line: line);
    }

    public static SharpProofTarget Span(int start, int end)
    {
        ValidateNonNegative(start, nameof(start));
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end));
        return new SharpProofTarget(SharpProofTargetKind.Span, SpanStart: start, SpanEnd: end);
    }

    public static SharpProofTarget LineSpan(
        int startLine,
        int startColumn,
        int endLine,
        int endColumn)
    {
        ValidatePositive(startLine, nameof(startLine));
        ValidatePositive(startColumn, nameof(startColumn));
        ValidatePositive(endLine, nameof(endLine));
        ValidatePositive(endColumn, nameof(endColumn));
        if (endLine < startLine) throw new ArgumentOutOfRangeException(nameof(endLine));
        if (endLine == startLine && endColumn < startColumn)
            throw new ArgumentOutOfRangeException(nameof(endColumn));
        return new SharpProofTarget(
            SharpProofTargetKind.LineSpan,
            StartLine: startLine,
            StartColumn: startColumn,
            EndLine: endLine,
            EndColumn: endColumn);
    }

    public static SharpProofTarget AllLines() => new(SharpProofTargetKind.AllLines);

    internal static SharpProofTarget Node(bool includeNestedCallables = false) =>
        new(SharpProofTargetKind.Node, IncludeNestedCallables: includeNestedCallables);

    private static void ValidatePositive(int value, string parameterName)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void ValidateNonNegative(int value, string parameterName)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(parameterName);
    }
}

public sealed record SharpProofAnalysisOptions
{
    public static SharpProofAnalysisOptions Default { get; } = new();

    public SharpProofAnalysisOptions(
        bool enableSmt = false,
        IEnumerable<string>? impliedConditions = null,
        SharpProofAnalysisBudget? analysisBudget = null)
    {
        EnableSmt = enableSmt;
        ImpliedConditions = impliedConditions?
            .Where(static condition => !string.IsNullOrWhiteSpace(condition))
            .Select(static condition => condition.Trim())
            .ToImmutableArray() ?? ImmutableArray<string>.Empty;
        AnalysisBudget = analysisBudget ?? SharpProofAnalysisBudget.Default;
    }

    public bool EnableSmt { get; }

    public ImmutableArray<string> ImpliedConditions { get; }

    public SharpProofAnalysisBudget AnalysisBudget { get; }
}

public sealed record SharpProofAnalysisBudget(
    int MaxMergedIfElseFacts = 16,
    int MaxMergedSwitchFacts = 32,
    int MaxMergedTryFacts = 16,
    int MaxTryCompletionBranches = 8,
    int MaxFiniteForeachElementFacts = 8,
    int MaxScopedBlockCompletionStatements = 32,
    int MaxStructuralNullStateDepth = 4,
    int MaxMergedPathConditions = 32,
    int MaxMergeableFactsPerTargetPerState = 4,
    int MaxFactChoiceCombinationsPerTarget = 64,
    int MaxGuardFactsPerTargetPerState = 6)
{
    public static SharpProofAnalysisBudget Default { get; } = new();

    private static readonly (string Name, Func<SharpProofAnalysisBudget, int> Read)[] NamedLimits =
    [
        ("merged-if-else-facts", static value => value.MaxMergedIfElseFacts),
        ("merged-switch-facts", static value => value.MaxMergedSwitchFacts),
        ("merged-try-facts", static value => value.MaxMergedTryFacts),
        ("try-completion-branches", static value => value.MaxTryCompletionBranches),
        ("finite-foreach-element-facts", static value => value.MaxFiniteForeachElementFacts),
        ("scoped-block-completion-statements", static value => value.MaxScopedBlockCompletionStatements),
        ("structural-null-state-depth", static value => value.MaxStructuralNullStateDepth),
        ("merged-path-conditions", static value => value.MaxMergedPathConditions),
        ("mergeable-facts-per-target-per-state", static value => value.MaxMergeableFactsPerTargetPerState),
        ("fact-choice-combinations-per-target", static value => value.MaxFactChoiceCombinationsPerTarget),
        ("guard-facts-per-target-per-state", static value => value.MaxGuardFactsPerTargetPerState)
    ];

    internal static bool IsNamedLimit(string name) =>
        NamedLimits.Any(limit => string.Equals(limit.Name, name, StringComparison.Ordinal));

    internal static SharpProofAnalysisBudget FromNamedValues(
        SharpProofAnalysisBudget defaults,
        Func<string, int, int> getValue)
    {
        var values = NamedLimits.Select(limit => getValue(limit.Name, limit.Read(defaults))).ToArray();
        return new SharpProofAnalysisBudget(
            values[0], values[1], values[2], values[3], values[4], values[5],
            values[6], values[7], values[8], values[9], values[10]);
    }

    internal SharpProofAnalysisBudget Validate()
    {
        var invalid = NamedLimits.FirstOrDefault(limit => limit.Read(this) <= 0);
        if (invalid.Name != null)
            throw new ArgumentOutOfRangeException(invalid.Name, "Analysis limits must be positive.");
        return this;
    }
}

public sealed record SharpProofRuntimeHazardOptions(
    bool IncludeUnprovenCandidates = false,
    ImmutableArray<string> Kinds = default)
{
    public static SharpProofRuntimeHazardOptions Default { get; } = new();

    internal SymbolicRuntimeHazardQueryOptions ToEngineOptions() => new(
        IncludeUnprovenCandidates,
        Kinds.IsDefaultOrEmpty
            ? null
            : Kinds.Select(static kind =>
                (SymbolicRuntimeHazardKind)Enum.Parse(typeof(SymbolicRuntimeHazardKind), kind, true)));

}

public abstract record SharpProofQuery(SharpProofQueryKind Kind, SharpProofTarget Target)
{
    public static SharpProofQuery SourceLocation(SharpProofTarget target) =>
        Simple(SharpProofQueryKind.SourceLocation, target);

    public static SharpProofQuery Method(SharpProofTarget target) =>
        Simple(SharpProofQueryKind.Method, target);

    public static SharpProofQuery Invariant(SharpProofTarget target) =>
        Simple(SharpProofQueryKind.Invariant, target);

    public static SharpProofQuery Reachability(SharpProofTarget target) =>
        Simple(SharpProofQueryKind.Reachability, target);

    public static SharpProofQuery Condition(SharpProofTarget target, string conditionText) =>
        new ConditionQuery(target, conditionText);

    public static SharpProofQuery RuntimeHazards(
        SharpProofTarget target,
        SharpProofRuntimeHazardOptions? options = null) =>
        new RuntimeHazardQuery(target, options ?? SharpProofRuntimeHazardOptions.Default);

    public static SharpProofQuery Capabilities(SharpProofTarget target) =>
        Simple(SharpProofQueryKind.Capabilities, target);

    public static SharpProofQuery Complexity(SharpProofTarget target) =>
        Simple(SharpProofQueryKind.Complexity, target);

    private static SharpProofQuery Simple(SharpProofQueryKind kind, SharpProofTarget target) =>
        new SimpleQuery(kind, target ?? throw new ArgumentNullException(nameof(target)));

    private sealed record SimpleQuery(SharpProofQueryKind QueryKind, SharpProofTarget QueryTarget)
        : SharpProofQuery(QueryKind, QueryTarget);

    internal sealed record ConditionQuery : SharpProofQuery
    {
        internal ConditionQuery(SharpProofTarget target, string conditionText)
            : base(SharpProofQueryKind.Condition, target ?? throw new ArgumentNullException(nameof(target)))
        {
            if (string.IsNullOrWhiteSpace(conditionText))
                throw new ArgumentException("Condition text is required.", nameof(conditionText));
            ConditionText = conditionText;
        }

        internal string ConditionText { get; }
    }

    internal sealed record RuntimeHazardQuery(SharpProofTarget QueryTarget, SharpProofRuntimeHazardOptions Options)
        : SharpProofQuery(
            SharpProofQueryKind.RuntimeHazards,
            QueryTarget ?? throw new ArgumentNullException(nameof(QueryTarget)));
}

public sealed record SharpProofLocation(
    string FilePath,
    int? Line,
    int? Column,
    int? Position,
    int? SpanStart,
    int? SpanEnd);

public sealed record SharpProofUnknownReason(
    string Code,
    string Category,
    string Message,
    bool IsRetryable,
    bool IsConfigurationRelated);

public sealed record SharpProofTruncationReason(
    string Code,
    int Limit,
    int Observed,
    string Provenance,
    int? SourceSpanStart);

public sealed record SharpProofBudgetMetadata(
    ImmutableArray<SharpProofTruncationReason> Truncations)
{
    public bool IsExhausted => !Truncations.IsDefaultOrEmpty;
}

public abstract record SharpProofQueryPayload
{
    internal SharpProofPayloadMetadata Metadata { get; init; } = null!;
}

public sealed record SourceQueryPayload(
    int ProgramPointCount,
    string Invariant,
    int ConditionProofCount,
    int UnknownProofCount,
    bool AllConditionsHold,
    int ConservativeUnknownCount,
    int ReachabilityUnknownCount,
    SharpProofSmtMetadata Smt) : SharpProofQueryPayload;

public sealed record ConditionQueryPayload(
    string Condition,
    string Truth,
    string Reason,
    bool IsSolverBacked) : SharpProofQueryPayload;

public sealed record SharpProofHazard(
    string Kind,
    string Status,
    string Reason,
    string ExceptionType,
    string Operation,
    SharpProofLocation Location);

public sealed record RuntimeHazardQueryPayload(
    ImmutableArray<SharpProofHazard> Hazards,
    SharpProofSmtMetadata Smt) : SharpProofQueryPayload;

public sealed record CapabilityQueryPayload(
    string Method,
    string Capabilities,
    ImmutableArray<string> Sites,
    bool HasUnknowns,
    int UnknownCount) : SharpProofQueryPayload;

public sealed record ComplexityQueryPayload(
    string Method,
    string Complexity,
    string Kind,
    bool IsConservative,
    bool IsUnknown,
    bool IsRecursiveUnknown,
    int UnknownCount,
    ImmutableArray<string> Drivers,
    ImmutableArray<string> CalleeSummaries) : SharpProofQueryPayload;

internal static class SharpProofPayloadProjector
{
    internal static SourceQueryPayload From(SymbolicQueryResult value)
    {
        var proofs = value.ProgramPoints.SelectMany(static point => point.ConditionProofs).ToArray();
        return new(
            value.ProgramPointCount,
            value.InvariantInfo.MergedText,
            proofs.Select(static proof => proof.Condition).Distinct(StringComparer.Ordinal).Count(),
            value.Metrics.ProofUnknownCount,
            proofs.GroupBy(static proof => proof.Condition, StringComparer.Ordinal).All(static group =>
                group.Any(static proof => proof.TruthValue == SymbolicTruthValue.ProvenTrue) &&
                group.All(static proof => proof.TruthValue is
                    SymbolicTruthValue.ProvenTrue or SymbolicTruthValue.Unreachable)),
            value.MergedPathFacts.ConservativeUnknownCount,
            value.Metrics.ReachabilityUnknownCount,
            SharpProofSmtMetadata.From(value.SmtDiagnostics))
        { Metadata = SharpProofPayloadMetadata.From(value) };
    }

    internal static ConditionQueryPayload From(SymbolicConditionProofResult value) => new(
        value.Condition,
        value.TruthValue.ToString(),
        value.Reason,
        value.IsSolverBacked)
    { Metadata = SharpProofPayloadMetadata.From(value) };

    internal static RuntimeHazardQueryPayload From(SymbolicRuntimeHazardQueryResult value) => new(
        value.Hazards.Select(static hazard => new SharpProofHazard(
            hazard.Kind.ToString(),
            hazard.Status.ToString(),
            hazard.StatusReason,
            hazard.ExceptionType,
            hazard.OperationText,
            new SharpProofLocation(hazard.FilePath, hazard.Line, hazard.Column, null,
                hazard.SpanStart, hazard.SpanEnd))).ToImmutableArray(),
        SharpProofSmtMetadata.From(value.SmtDiagnostics))
    { Metadata = SharpProofPayloadMetadata.From(value) };

    internal static CapabilityQueryPayload From(SymbolicCapabilityResult value) => new(
        value.MethodDisplayName,
        value.CapabilityText,
        value.Sites.Select(static site => site.OperationText).ToImmutableArray(),
        value.HasUnknowns,
        Math.Max(value.UnknownReasons.Count, value.Sites.Count(static site => site.IsUnknown)))
    { Metadata = SharpProofPayloadMetadata.From(value) };

    internal static ComplexityQueryPayload From(SymbolicComplexityResult value) => new(
        value.MethodDisplayName,
        value.Complexity.Text,
        value.Complexity.Kind.ToString(),
        value.Complexity.IsConservative,
        value.Complexity.IsUnknown,
        value.Complexity.IsRecursiveUnknown,
        Math.Max(value.UnknownReasons.Count,
            value.Complexity.IsUnknown || value.Complexity.IsRecursiveUnknown ? 1 : 0),
        value.Drivers.Select(static driver => driver.Description).ToImmutableArray(),
        value.CalleeSummaries.Select(static callee =>
            $"{callee.MethodDisplayName}: {callee.ComplexityText}").ToImmutableArray())
    { Metadata = SharpProofPayloadMetadata.From(value) };
}

internal sealed record SharpProofPayloadMetadata(
    SharpProofLocation Location,
    ImmutableArray<SharpProofUnknownReason> UnknownReasons,
    SymbolicAnalysisTruncationInfo Truncation,
    ImmutableArray<SharpProofEvidence> Evidence)
{
    internal static SharpProofPayloadMetadata From(SymbolicQueryResult value) => new(
        new SharpProofLocation(value.FilePath, value.Line, value.Column, value.Position, value.SpanStart, value.SpanEnd),
        ConvertUnknownReasons(GetSourceUnknownReasons(value)),
        value.AnalysisTruncation,
        ConvertEvidence(value.ReachabilityWitnesses));

    internal static SharpProofPayloadMetadata From(SymbolicConditionProofResult value) => new(
        new SharpProofLocation(value.FilePath ?? string.Empty, value.Line, value.Column, value.Position,
            value.NodeSpanStart, value.NodeSpanEnd),
        value.TruthValue == SymbolicTruthValue.Unknown
            ? ConvertUnknownReasons(new[]
            {
                SymbolicUnknownReasonTaxonomy.ForProof(SymbolicUnknownReason.Unknown, value.Reason)
            })
            : ImmutableArray<SharpProofUnknownReason>.Empty,
        value.AnalysisTruncation,
        ConvertEvidence(ImmutableArray.Create(value.Witness, value.CounterexampleWitness)));

    internal static SharpProofPayloadMetadata From(SymbolicRuntimeHazardQueryResult value) => new(
        new SharpProofLocation(value.FilePath, value.Line, null, null, value.ScopeStart, value.ScopeEnd),
        ConvertUnknownReasons(value.Hazards
            .Where(static hazard => hazard.Status is SymbolicRuntimeHazardStatus.Unknown or
                SymbolicRuntimeHazardStatus.Unsupported)
            .Select(static hazard => hazard.UnknownReasonInfo)),
        value.AnalysisTruncation,
        ConvertEvidence(value.TriggerWitnesses));

    internal static SharpProofPayloadMetadata From(SymbolicCapabilityResult value) => new(
        FromMethodResult(value),
        ConvertUnknownReasons(value.UnknownReasonDetails),
        SymbolicAnalysisTruncationInfo.None,
        ImmutableArray<SharpProofEvidence>.Empty);

    internal static SharpProofPayloadMetadata From(SymbolicComplexityResult value) => new(
        FromMethodResult(value),
        ConvertUnknownReasons(value.UnknownReasonDetails),
        SymbolicAnalysisTruncationInfo.None,
        ImmutableArray<SharpProofEvidence>.Empty);

    private static IEnumerable<SymbolicUnknownReasonInfo> GetSourceUnknownReasons(SymbolicQueryResult result)
    {
        foreach (var proof in result.ProgramPoints.SelectMany(static point => point.ConditionProofs))
            if (proof.TruthValue == SymbolicTruthValue.Unknown)
                yield return SymbolicUnknownReasonTaxonomy.ForProof(
                    SymbolicUnknownReason.Unknown,
                    proof.Reason);

        foreach (var point in result.ProgramPoints)
            if (point.Reachability == SymbolicReachability.Unknown)
                yield return SymbolicUnknownReasonTaxonomy.ForProof(
                    SymbolicUnknownReason.Unknown,
                    point.ReachabilityReason);
    }

    private static ImmutableArray<SharpProofUnknownReason> ConvertUnknownReasons(
        IEnumerable<SymbolicUnknownReasonInfo> reasons) => reasons
        .Where(static reason => reason.IsUnknown)
        .Select(static reason => new SharpProofUnknownReason(
            reason.Code,
            reason.Category.ToString(),
            reason.RawReason,
            reason.IsRetryable,
            reason.IsConfigurationRelated))
        .Distinct()
        .ToImmutableArray();

    private static ImmutableArray<SharpProofEvidence> ConvertEvidence(IEnumerable<SymbolicInputWitness> evidence) =>
        evidence.Select(static witness =>
            new SharpProofEvidence(witness.Status.ToString(), witness.Reason)).ToImmutableArray();

    private static SharpProofLocation FromMethodResult(SymbolicMethodResult result) => new(
        result.FilePath,
        result.StartLine,
        result.StartColumn,
        result.SpanStart,
        result.SpanStart,
        result.SpanEnd);
}

public sealed record SharpProofSmtMetadata(
    string State,
    string LastFailureCode,
    int ExecutedQueryCount)
{
    internal static SharpProofSmtMetadata From(SymbolicSmtDiagnostics diagnostics) =>
        new(diagnostics.Health.State.ToString(), diagnostics.Health.LastFailureCode,
            diagnostics.ExecutedQueryCount);
}

public sealed record SharpProofEvidence(string Status, string Reason);

public sealed record SharpProofError(
    string Code,
    string Category,
    string Message,
    int RecommendedExitCode,
    bool IsRetryable,
    ImmutableDictionary<string, string> Details);

public sealed record SharpProofQueryResult(
    SharpProofQueryStatus Status,
    SharpProofQuery Query,
    SharpProofLocation Location,
    ImmutableArray<SharpProofUnknownReason> UnknownReasons,
    SharpProofBudgetMetadata Budget,
    ImmutableArray<SharpProofEvidence> Evidence,
    SharpProofQueryPayload? Payload,
    SharpProofError? Error)
{
    public bool IsSuccess => Status is SharpProofQueryStatus.Succeeded or SharpProofQueryStatus.Unknown;
}

public sealed class SharpProofAnalysisSession : IDisposable
{
    private readonly ConcurrentDictionary<SharpProofQuery, Lazy<SharpProofQueryResult>> _results = new();
    private readonly SymbolicQueryExecutor _executor;
    private readonly SmtAnalysisService? _ownedSmtAnalysis;
    private readonly SymbolicSourceInput _source;
    private readonly SymbolicQueryOptions _options;
    private bool _disposed;

    private SharpProofAnalysisSession(
        SymbolicSourceInput source,
        SymbolicQueryOptions options,
        SmtAnalysisService? ownedSmtAnalysis = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ownedSmtAnalysis = ownedSmtAnalysis;
        _executor = new SymbolicQueryExecutor();
    }

    public static SharpProofAnalysisSession FromText(
        string sourceText,
        string? filePath = null,
        SharpProofAnalysisOptions? options = null)
    {
        options ??= SharpProofAnalysisOptions.Default;
        var smtAnalysis = options.EnableSmt
            ? new SmtAnalysisService(SmtAnalysisOptions.Default)
            : null;
        return new SharpProofAnalysisSession(
            SymbolicSourceInput.FromText(sourceText, filePath),
            CreateQueryOptions(options, smtAnalysis),
            smtAnalysis);
    }

    public static SharpProofAnalysisSession FromFile(
        string filePath,
        SharpProofAnalysisOptions? options = null)
    {
        options ??= SharpProofAnalysisOptions.Default;
        var smtAnalysis = options.EnableSmt
            ? new SmtAnalysisService(SmtAnalysisOptions.Default)
            : null;
        return new SharpProofAnalysisSession(
            SymbolicSourceInput.FromFile(filePath),
            CreateQueryOptions(options, smtAnalysis),
            smtAnalysis);
    }

    internal static SharpProofAnalysisSession Create(
        SymbolicSourceInput source,
        SymbolicQueryOptions options)
    {
        return new SharpProofAnalysisSession(source, options);
    }

    public SharpProofQueryResult Analyze(
        SharpProofQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));
        ThrowIfDisposed();
        if (cancellationToken.CanBeCanceled) return Execute(query, cancellationToken);

        var lazy = _results.GetOrAdd(
            query,
            request => new Lazy<SharpProofQueryResult>(
                () => Execute(request, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return lazy.Value;
        }
        catch
        {
            if (_results.TryGetValue(query, out var current) && ReferenceEquals(current, lazy))
                _results.TryRemove(query, out _);
            throw;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _results.Clear();
        _ownedSmtAnalysis?.Dispose();
    }

    private SharpProofQueryResult Execute(SharpProofQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var context = new SymbolicQueryContext(_source, query.Target, _options);
            return query switch
            {
                SharpProofQuery.ConditionQuery condition => FromPayload(
                    query,
                    SharpProofPayloadProjector.From(
                        _executor.Prove(context, condition.ConditionText, cancellationToken))),
                SharpProofQuery.RuntimeHazardQuery hazards => FromPayload(
                    query,
                    SharpProofPayloadProjector.From(
                        _executor.QueryRuntimeHazards(context, hazards.Options.ToEngineOptions(), cancellationToken))),
                { Kind: SharpProofQueryKind.Capabilities } => FromPayload(
                    query,
                    SharpProofPayloadProjector.From(_executor.QueryCapabilities(context, cancellationToken))),
                { Kind: SharpProofQueryKind.Complexity } => FromPayload(
                    query,
                    SharpProofPayloadProjector.From(_executor.QueryComplexity(context, cancellationToken))),
                _ => FromPayload(
                    query,
                    SharpProofPayloadProjector.From(_executor.Query(context, cancellationToken)))
            };
        }
        catch (Exception exception) when (!SymbolicErrorClassifier.IsFatal(exception))
        {
            var error = SymbolicErrorClassifier.FromException(exception);
            return new SharpProofQueryResult(
                error.Category == SymbolicErrorCategory.Cancellation
                    ? SharpProofQueryStatus.Canceled
                    : SharpProofQueryStatus.Failed,
                query,
                CreateLocation(query.Target),
                ImmutableArray<SharpProofUnknownReason>.Empty,
                new SharpProofBudgetMetadata(ImmutableArray<SharpProofTruncationReason>.Empty),
                ImmutableArray<SharpProofEvidence>.Empty,
                null,
                ToError(error));
        }
    }

    private SharpProofQueryResult FromPayload(SharpProofQuery query, SharpProofQueryPayload payload)
    {
        var unknownReasons = payload.Metadata.UnknownReasons;
        var truncation = payload.Metadata.Truncation;
        var status = unknownReasons.IsDefaultOrEmpty && !truncation.IsTruncated
            ? SharpProofQueryStatus.Succeeded
            : SharpProofQueryStatus.Unknown;
        return new SharpProofQueryResult(
            status,
            query,
            GetLocation(query.Target, payload.Metadata.Location),
            unknownReasons,
            new SharpProofBudgetMetadata(
                truncation.Events.Select(static item => new SharpProofTruncationReason(
                    item.Code,
                    item.Limit,
                    item.Observed,
                    item.Provenance,
                    item.SourceSpanStart)).ToImmutableArray()),
            payload.Metadata.Evidence,
            payload,
            null);
    }

    private SharpProofLocation GetLocation(SharpProofTarget target, SharpProofLocation location)
    {
        if (!string.IsNullOrEmpty(location.FilePath)) return location;
        return location with { FilePath = _source.FilePath ?? CreateLocation(target).FilePath };
    }

    private SharpProofLocation CreateLocation(SharpProofTarget target)
    {
        return new SharpProofLocation(
            _source.FilePath ?? string.Empty,
            target.Line ?? target.StartLine,
            target.Column ?? target.StartColumn,
            target.Position,
            target.SpanStart,
            target.SpanEnd);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SharpProofAnalysisSession));
    }

    private static SharpProofError ToError(SymbolicError error) => new(
        error.Code,
        error.Category.ToString(),
        error.Message,
        error.RecommendedExitCode,
        error.IsRetryable,
        error.Details.ToImmutableDictionary(StringComparer.Ordinal));

    private static SymbolicQueryOptions CreateQueryOptions(
        SharpProofAnalysisOptions options,
        SmtAnalysisService? smtAnalysis)
    {
        return new SymbolicQueryOptions(
                smtAnalysis: smtAnalysis,
                impliedConditions: options.ImpliedConditions)
            .WithAnalysisLimits(options.AnalysisBudget.Validate());
    }
}
