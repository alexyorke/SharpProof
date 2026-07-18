using System.Collections.Concurrent;
using System.Collections.Immutable;
using SharpProof.Symbolic.Smt;

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

public sealed record SharpProofTarget
{
    private SharpProofTarget(
        SharpProofTargetKind kind,
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
        Line = line;
        Column = column;
        Position = position;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        IncludeNestedCallables = includeNestedCallables;
    }

    public SharpProofTargetKind Kind { get; }

    public int? Line { get; }

    public int? Column { get; }

    public int? Position { get; }

    public int? SpanStart { get; }

    public int? SpanEnd { get; }

    public int? StartLine { get; }

    public int? StartColumn { get; }

    public int? EndLine { get; }

    public int? EndColumn { get; }

    public bool IncludeNestedCallables { get; }

    public static SharpProofTarget Point(int line, int column = 1)
    {
        ValidatePositive(line, nameof(line));
        ValidatePositive(column, nameof(column));
        return new SharpProofTarget(SharpProofTargetKind.Point, line: line, column: column);
    }

    public static SharpProofTarget AtPosition(int position)
    {
        ValidateNonNegative(position, nameof(position));
        return new SharpProofTarget(SharpProofTargetKind.Position, position: position);
    }

    public static SharpProofTarget LineNumber(int line)
    {
        ValidatePositive(line, nameof(line));
        return new SharpProofTarget(SharpProofTargetKind.Line, line: line);
    }

    public static SharpProofTarget Span(int start, int end)
    {
        ValidateNonNegative(start, nameof(start));
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end));
        return new SharpProofTarget(SharpProofTargetKind.Span, spanStart: start, spanEnd: end);
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
            startLine: startLine,
            startColumn: startColumn,
            endLine: endLine,
            endColumn: endColumn);
    }

    public static SharpProofTarget AllLines() => new(SharpProofTargetKind.AllLines);

    internal static SharpProofTarget Node(bool includeNestedCallables = false) =>
        new(SharpProofTargetKind.Node, includeNestedCallables: includeNestedCallables);

    internal static SharpProofTarget FromSymbolicTarget(SymbolicQueryTarget target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        return new SharpProofTarget(
            (SharpProofTargetKind)target.Kind,
            target.LineNumber,
            target.ColumnNumber,
            target.PositionOffset,
            target.SpanStart,
            target.SpanEnd,
            target.StartLine,
            target.StartColumn,
            target.EndLine,
            target.EndColumn,
            target.IncludeNestedCallables);
    }

    internal SymbolicQueryTarget ToSymbolicTarget()
    {
        return Kind switch
        {
            SharpProofTargetKind.Point => SymbolicQueryTarget.Point(Line!.Value, Column ?? 1),
            SharpProofTargetKind.Position => SymbolicQueryTarget.Position(Position!.Value),
            SharpProofTargetKind.Line => SymbolicQueryTarget.Line(Line!.Value),
            SharpProofTargetKind.Span => SymbolicQueryTarget.Span(SpanStart!.Value, SpanEnd!.Value),
            SharpProofTargetKind.LineSpan => SymbolicQueryTarget.LineSpan(
                StartLine!.Value,
                StartColumn!.Value,
                EndLine!.Value,
                EndColumn!.Value),
            SharpProofTargetKind.AllLines => SymbolicQueryTarget.AllLines(),
            SharpProofTargetKind.Node => SymbolicQueryTarget.Node(IncludeNestedCallables),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind))
        };
    }

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

    internal SymbolicAnalysisLimits ToLegacy() => new(
        MaxMergedIfElseFacts,
        MaxMergedSwitchFacts,
        MaxMergedTryFacts,
        MaxTryCompletionBranches,
        MaxFiniteForeachElementFacts,
        MaxScopedBlockCompletionStatements,
        MaxStructuralNullStateDepth,
        MaxMergedPathConditions,
        MaxMergeableFactsPerTargetPerState,
        MaxFactChoiceCombinationsPerTarget,
        MaxGuardFactsPerTargetPerState);
}

public sealed record SharpProofRuntimeHazardOptions(
    bool IncludeUnprovenCandidates = false,
    ImmutableArray<string> Kinds = default)
{
    public static SharpProofRuntimeHazardOptions Default { get; } = new();

    internal SymbolicRuntimeHazardQueryOptions ToLegacy() => new(
        IncludeUnprovenCandidates,
        Kinds.IsDefaultOrEmpty
            ? null
            : Kinds.Select(static kind =>
                (SymbolicRuntimeHazardKind)Enum.Parse(typeof(SymbolicRuntimeHazardKind), kind, true)));

    internal static SharpProofRuntimeHazardOptions FromLegacy(SymbolicRuntimeHazardQueryOptions options) =>
        new(options.IncludeUnprovenCandidates,
            options.Kinds.Select(static kind => kind.ToString()).ToImmutableArray());
}

public abstract record SharpProofQuery(SharpProofQueryKind Kind, SharpProofTarget Target)
{
    public static SharpProofQuery SourceLocation(SharpProofTarget target) =>
        new SourceLocationQuery(target);

    public static SharpProofQuery Method(SharpProofTarget target) =>
        new MethodQuery(target);

    public static SharpProofQuery Invariant(SharpProofTarget target) =>
        new InvariantQuery(target);

    public static SharpProofQuery Reachability(SharpProofTarget target) =>
        new ReachabilityQuery(target);

    public static SharpProofQuery Condition(SharpProofTarget target, string conditionText) =>
        new ConditionQuery(target, conditionText);

    public static SharpProofQuery RuntimeHazards(
        SharpProofTarget target,
        SharpProofRuntimeHazardOptions? options = null) =>
        new RuntimeHazardQuery(target, options ?? SharpProofRuntimeHazardOptions.Default);

    public static SharpProofQuery Capabilities(SharpProofTarget target) =>
        new CapabilityQuery(target);

    public static SharpProofQuery Complexity(SharpProofTarget target) =>
        new ComplexityQuery(target);
}

public sealed record SourceLocationQuery : SharpProofQuery
{
    public SourceLocationQuery(SharpProofTarget target)
        : base(SharpProofQueryKind.SourceLocation, target ?? throw new ArgumentNullException(nameof(target)))
    {
    }
}

public sealed record MethodQuery : SharpProofQuery
{
    public MethodQuery(SharpProofTarget target)
        : base(SharpProofQueryKind.Method, target ?? throw new ArgumentNullException(nameof(target)))
    {
    }
}

public sealed record InvariantQuery : SharpProofQuery
{
    public InvariantQuery(SharpProofTarget target)
        : base(SharpProofQueryKind.Invariant, target ?? throw new ArgumentNullException(nameof(target)))
    {
    }
}

public sealed record ReachabilityQuery : SharpProofQuery
{
    public ReachabilityQuery(SharpProofTarget target)
        : base(SharpProofQueryKind.Reachability, target ?? throw new ArgumentNullException(nameof(target)))
    {
    }
}

public sealed record ConditionQuery : SharpProofQuery
{
    public ConditionQuery(SharpProofTarget target, string conditionText)
        : base(SharpProofQueryKind.Condition, target ?? throw new ArgumentNullException(nameof(target)))
    {
        if (string.IsNullOrWhiteSpace(conditionText))
            throw new ArgumentException("Condition text is required.", nameof(conditionText));
        ConditionText = conditionText;
    }

    public string ConditionText { get; }
}

public sealed record RuntimeHazardQuery : SharpProofQuery
{
    public RuntimeHazardQuery(
        SharpProofTarget target,
        SharpProofRuntimeHazardOptions options)
        : base(SharpProofQueryKind.RuntimeHazards, target ?? throw new ArgumentNullException(nameof(target)))
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public SharpProofRuntimeHazardOptions Options { get; }
}

public sealed record CapabilityQuery : SharpProofQuery
{
    public CapabilityQuery(SharpProofTarget target)
        : base(SharpProofQueryKind.Capabilities, target ?? throw new ArgumentNullException(nameof(target)))
    {
    }
}

public sealed record ComplexityQuery : SharpProofQuery
{
    public ComplexityQuery(SharpProofTarget target)
        : base(SharpProofQueryKind.Complexity, target ?? throw new ArgumentNullException(nameof(target)))
    {
    }
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

public abstract record SharpProofQueryPayload;

public sealed record SourceQueryPayload : SharpProofQueryPayload
{
    internal SourceQueryPayload(SymbolicQueryResult value)
    {
        LegacyValue = value;
        ProgramPointCount = value.ProgramPointCount;
        Invariant = value.InvariantInfo.MergedText;
        ConditionProofCount = value.ConditionProofs.Count;
        UnknownProofCount = value.ConditionProofs.Sum(static proof => proof.UnknownCount);
        AllConditionsHold = value.ConditionProofs.All(static proof => proof.HoldsOnAllReachablePoints);
        Smt = SharpProofSmtMetadata.From(value.SmtDiagnostics);
    }

    internal SymbolicQueryResult LegacyValue { get; }
    public int ProgramPointCount { get; }
    public string Invariant { get; }
    public int ConditionProofCount { get; }
    public int UnknownProofCount { get; }
    public bool AllConditionsHold { get; }
    public SharpProofSmtMetadata Smt { get; }
}

public sealed record ConditionQueryPayload : SharpProofQueryPayload
{
    internal ConditionQueryPayload(SymbolicConditionProofResult value)
    {
        LegacyValue = value;
        Condition = value.Condition;
        Truth = value.TruthValue.ToString();
        Reason = value.Reason;
        IsSolverBacked = value.IsSolverBacked;
    }

    internal SymbolicConditionProofResult LegacyValue { get; }
    public string Condition { get; }
    public string Truth { get; }
    public string Reason { get; }
    public bool IsSolverBacked { get; }
}

public sealed record SharpProofHazard(
    string Kind,
    string Status,
    string Reason,
    string ExceptionType,
    string Operation,
    SharpProofLocation Location);

public sealed record RuntimeHazardQueryPayload : SharpProofQueryPayload
{
    internal RuntimeHazardQueryPayload(SymbolicRuntimeHazardQueryResult value)
    {
        LegacyValue = value;
        Hazards = value.Hazards.Select(static hazard => new SharpProofHazard(
            hazard.Kind.ToString(),
            hazard.Status.ToString(),
            hazard.StatusReason,
            hazard.ExceptionType,
            hazard.OperationText,
            new SharpProofLocation(hazard.FilePath, hazard.Line, hazard.Column, null,
                hazard.SpanStart, hazard.SpanEnd))).ToImmutableArray();
        Smt = SharpProofSmtMetadata.From(value.SmtDiagnostics);
    }

    internal SymbolicRuntimeHazardQueryResult LegacyValue { get; }
    public ImmutableArray<SharpProofHazard> Hazards { get; }
    public SharpProofSmtMetadata Smt { get; }
}

public sealed record CapabilityQueryPayload : SharpProofQueryPayload
{
    internal CapabilityQueryPayload(SymbolicCapabilityResult value)
    {
        LegacyValue = value;
        Method = value.MethodDisplayName;
        Capabilities = value.CapabilityText;
        Sites = value.Sites.Select(static site => site.OperationText).ToImmutableArray();
        HasUnknowns = value.HasUnknowns;
    }

    internal SymbolicCapabilityResult LegacyValue { get; }
    public string Method { get; }
    public string Capabilities { get; }
    public ImmutableArray<string> Sites { get; }
    public bool HasUnknowns { get; }
}

public sealed record ComplexityQueryPayload : SharpProofQueryPayload
{
    internal ComplexityQueryPayload(SymbolicComplexityResult value)
    {
        LegacyValue = value;
        Method = value.MethodDisplayName;
        Complexity = value.Complexity.Text;
        IsConservative = value.Complexity.IsConservative;
        Drivers = value.Drivers.Select(static driver => driver.Description).ToImmutableArray();
        CalleeSummaries = value.CalleeSummaries.Select(static callee =>
            $"{callee.MethodDisplayName}: {callee.ComplexityText}").ToImmutableArray();
    }

    internal SymbolicComplexityResult LegacyValue { get; }
    public string Method { get; }
    public string Complexity { get; }
    public bool IsConservative { get; }
    public ImmutableArray<string> Drivers { get; }
    public ImmutableArray<string> CalleeSummaries { get; }
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
            var context = new SymbolicQueryContext(_source, query.Target.ToSymbolicTarget(), _options);
            return query switch
            {
                ConditionQuery condition => FromPayload(
                    query,
                    new ConditionQueryPayload(_executor.Prove(context, condition.ConditionText, cancellationToken))),
                RuntimeHazardQuery hazards => FromPayload(
                    query,
                    new RuntimeHazardQueryPayload(
                        _executor.QueryRuntimeHazards(context, hazards.Options.ToLegacy(), cancellationToken))),
                CapabilityQuery => FromPayload(
                    query,
                    new CapabilityQueryPayload(_executor.QueryCapabilities(context, cancellationToken))),
                ComplexityQuery => FromPayload(
                    query,
                    new ComplexityQueryPayload(_executor.QueryComplexity(context, cancellationToken))),
                _ => FromPayload(
                    query,
                    new SourceQueryPayload(_executor.Query(context, cancellationToken)))
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
        var unknownReasons = GetUnknownReasons(payload);
        var truncation = GetTruncation(payload);
        var status = unknownReasons.IsDefaultOrEmpty && !truncation.IsTruncated
            ? SharpProofQueryStatus.Succeeded
            : SharpProofQueryStatus.Unknown;
        return new SharpProofQueryResult(
            status,
            query,
            GetLocation(query.Target, payload),
            unknownReasons,
            new SharpProofBudgetMetadata(
                truncation.Events.Select(static item => new SharpProofTruncationReason(
                    item.Code,
                    item.Limit,
                    item.Observed,
                    item.Provenance,
                    item.SourceSpanStart)).ToImmutableArray()),
            GetEvidence(payload),
            payload,
            null);
    }

    private static ImmutableArray<SharpProofUnknownReason> GetUnknownReasons(SharpProofQueryPayload payload)
    {
        IEnumerable<SymbolicUnknownReasonInfo> reasons = payload switch
        {
            CapabilityQueryPayload capability => capability.LegacyValue.UnknownReasonDetails,
            ComplexityQueryPayload complexity => complexity.LegacyValue.UnknownReasonDetails,
            RuntimeHazardQueryPayload hazards => hazards.LegacyValue.Hazards
                .Where(static hazard => hazard.Status is SymbolicRuntimeHazardStatus.Unknown or
                    SymbolicRuntimeHazardStatus.Unsupported)
                .Select(static hazard => hazard.UnknownReasonInfo),
            ConditionQueryPayload condition when condition.LegacyValue.TruthValue == SymbolicTruthValue.Unknown =>
                new[] { SymbolicUnknownReasonTaxonomy.ForProof(SymbolicUnknownReason.Unknown,
                    condition.LegacyValue.Reason) },
            SourceQueryPayload source => GetSourceUnknownReasons(source.LegacyValue),
            _ => Array.Empty<SymbolicUnknownReasonInfo>()
        };

        return reasons
            .Where(static reason => reason.IsUnknown)
            .Select(static reason => new SharpProofUnknownReason(
                reason.Code,
                reason.Category.ToString(),
                reason.RawReason,
                reason.IsRetryable,
                reason.IsConfigurationRelated))
            .Distinct()
            .ToImmutableArray();
    }

    private static IEnumerable<SymbolicUnknownReasonInfo> GetSourceUnknownReasons(SymbolicQueryResult result)
    {
        foreach (var proof in result.ConditionProofs)
            if (proof.Proof.Status == SymbolicProofStatus.Unknown)
                yield return SymbolicUnknownReasonTaxonomy.ForProof(
                    SymbolicUnknownReason.Unknown,
                    proof.Proof.Reason);

        foreach (var point in result.ProgramPoints)
            if (point.Reachability == SymbolicReachability.Unknown)
                yield return SymbolicUnknownReasonTaxonomy.ForProof(
                    SymbolicUnknownReason.Unknown,
                    point.ReachabilityReason);
    }

    private static SymbolicAnalysisTruncationInfo GetTruncation(SharpProofQueryPayload payload)
    {
        return payload switch
        {
            SourceQueryPayload source => source.LegacyValue.AnalysisTruncation,
            ConditionQueryPayload condition => condition.LegacyValue.AnalysisTruncation,
            RuntimeHazardQueryPayload hazards => hazards.LegacyValue.AnalysisTruncation,
            _ => SymbolicAnalysisTruncationInfo.None
        };
    }

    private static ImmutableArray<SharpProofEvidence> GetEvidence(SharpProofQueryPayload payload)
    {
        IEnumerable<SymbolicInputWitness> evidence = payload switch
        {
            SourceQueryPayload source => source.LegacyValue.ReachabilityWitnesses,
            ConditionQueryPayload condition => ImmutableArray.Create(
                condition.LegacyValue.Witness,
                condition.LegacyValue.CounterexampleWitness),
            RuntimeHazardQueryPayload hazards => hazards.LegacyValue.TriggerWitnesses,
            _ => Array.Empty<SymbolicInputWitness>()
        };
        return evidence.Select(static witness =>
            new SharpProofEvidence(witness.Status.ToString(), witness.Reason)).ToImmutableArray();
    }

    private SharpProofLocation GetLocation(SharpProofTarget target, SharpProofQueryPayload payload)
    {
        return payload switch
        {
            SourceQueryPayload source => new SharpProofLocation(
                source.LegacyValue.FilePath,
                source.LegacyValue.Line,
                source.LegacyValue.Column,
                source.LegacyValue.Position,
                source.LegacyValue.SpanStart,
                source.LegacyValue.SpanEnd),
            ConditionQueryPayload condition => new SharpProofLocation(
                condition.LegacyValue.FilePath ?? _source.FilePath ?? string.Empty,
                condition.LegacyValue.Line,
                condition.LegacyValue.Column,
                condition.LegacyValue.Position,
                condition.LegacyValue.NodeSpanStart,
                condition.LegacyValue.NodeSpanEnd),
            RuntimeHazardQueryPayload hazards => new SharpProofLocation(
                hazards.LegacyValue.FilePath,
                hazards.LegacyValue.Line,
                null,
                null,
                hazards.LegacyValue.ScopeStart,
                hazards.LegacyValue.ScopeEnd),
            CapabilityQueryPayload capability => FromMethodResult(capability.LegacyValue),
            ComplexityQueryPayload complexity => FromMethodResult(complexity.LegacyValue),
            _ => CreateLocation(target)
        };
    }

    private static SharpProofLocation FromMethodResult(SymbolicMethodResult result)
    {
        return new SharpProofLocation(
            result.FilePath,
            result.StartLine,
            result.StartColumn,
            result.SpanStart,
            result.SpanStart,
            result.SpanEnd);
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
            .WithAnalysisLimits(options.AnalysisBudget.ToLegacy());
    }
}
