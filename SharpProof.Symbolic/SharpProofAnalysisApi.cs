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

public sealed record SharpProofAnalysisOptions
{
    public static SharpProofAnalysisOptions Default { get; } = new();

    public SharpProofAnalysisOptions(
        bool enableSmt = false,
        IEnumerable<string>? impliedConditions = null,
        SymbolicAnalysisLimits? analysisLimits = null)
    {
        EnableSmt = enableSmt;
        ImpliedConditions = impliedConditions?
            .Where(static condition => !string.IsNullOrWhiteSpace(condition))
            .Select(static condition => condition.Trim())
            .ToImmutableArray() ?? ImmutableArray<string>.Empty;
        AnalysisLimits = analysisLimits ?? SymbolicAnalysisLimits.Default;
    }

    public bool EnableSmt { get; }

    public ImmutableArray<string> ImpliedConditions { get; }

    public SymbolicAnalysisLimits AnalysisLimits { get; }
}

public abstract record SharpProofQuery(SharpProofQueryKind Kind, SymbolicQueryTarget Target)
{
    public static SharpProofQuery SourceLocation(SymbolicQueryTarget target) =>
        new SourceLocationQuery(target);

    public static SharpProofQuery Method(SymbolicQueryTarget target) =>
        new MethodQuery(target);

    public static SharpProofQuery Invariant(SymbolicQueryTarget target) =>
        new InvariantQuery(target);

    public static SharpProofQuery Reachability(SymbolicQueryTarget target) =>
        new ReachabilityQuery(target);

    public static SharpProofQuery Condition(SymbolicQueryTarget target, string conditionText) =>
        new ConditionQuery(target, conditionText);

    public static SharpProofQuery RuntimeHazards(
        SymbolicQueryTarget target,
        SymbolicRuntimeHazardQueryOptions? options = null) =>
        new RuntimeHazardQuery(target, options ?? SymbolicRuntimeHazardQueryOptions.Default);

    public static SharpProofQuery Capabilities(SymbolicQueryTarget target) =>
        new CapabilityQuery(target);

    public static SharpProofQuery Complexity(SymbolicQueryTarget target) =>
        new ComplexityQuery(target);
}

public sealed record SourceLocationQuery : SharpProofQuery
{
    public SourceLocationQuery(SymbolicQueryTarget target)
        : base(SharpProofQueryKind.SourceLocation, target ?? throw new ArgumentNullException(nameof(target)))
    {
    }
}

public sealed record MethodQuery : SharpProofQuery
{
    public MethodQuery(SymbolicQueryTarget target)
        : base(SharpProofQueryKind.Method, target ?? throw new ArgumentNullException(nameof(target)))
    {
    }
}

public sealed record InvariantQuery : SharpProofQuery
{
    public InvariantQuery(SymbolicQueryTarget target)
        : base(SharpProofQueryKind.Invariant, target ?? throw new ArgumentNullException(nameof(target)))
    {
    }
}

public sealed record ReachabilityQuery : SharpProofQuery
{
    public ReachabilityQuery(SymbolicQueryTarget target)
        : base(SharpProofQueryKind.Reachability, target ?? throw new ArgumentNullException(nameof(target)))
    {
    }
}

public sealed record ConditionQuery : SharpProofQuery
{
    public ConditionQuery(SymbolicQueryTarget target, string conditionText)
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
        SymbolicQueryTarget target,
        SymbolicRuntimeHazardQueryOptions options)
        : base(SharpProofQueryKind.RuntimeHazards, target ?? throw new ArgumentNullException(nameof(target)))
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public SymbolicRuntimeHazardQueryOptions Options { get; }
}

public sealed record CapabilityQuery : SharpProofQuery
{
    public CapabilityQuery(SymbolicQueryTarget target)
        : base(SharpProofQueryKind.Capabilities, target ?? throw new ArgumentNullException(nameof(target)))
    {
    }
}

public sealed record ComplexityQuery : SharpProofQuery
{
    public ComplexityQuery(SymbolicQueryTarget target)
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
    SymbolicUnknownReasonCategory Category,
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
    SymbolicAnalysisLimits Limits,
    ImmutableArray<SharpProofTruncationReason> Truncations)
{
    public bool IsExhausted => !Truncations.IsDefaultOrEmpty;
}

public abstract record SharpProofQueryPayload;

public sealed record SourceQueryPayload(SymbolicQueryResult Value) : SharpProofQueryPayload;

public sealed record ConditionQueryPayload(SymbolicConditionProofResult Value) : SharpProofQueryPayload;

public sealed record RuntimeHazardQueryPayload(SymbolicRuntimeHazardQueryResult Value) : SharpProofQueryPayload;

public sealed record CapabilityQueryPayload(SymbolicCapabilityResult Value) : SharpProofQueryPayload;

public sealed record ComplexityQueryPayload(SymbolicComplexityResult Value) : SharpProofQueryPayload;

public sealed record SharpProofQueryResult(
    SharpProofQueryStatus Status,
    SharpProofQuery Query,
    SharpProofLocation Location,
    ImmutableArray<SharpProofUnknownReason> UnknownReasons,
    SharpProofBudgetMetadata Budget,
    ImmutableArray<SymbolicInputWitness> Evidence,
    SharpProofQueryPayload? Payload,
    SymbolicError? Error)
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
                ConditionQuery condition => FromPayload(
                    query,
                    new ConditionQueryPayload(_executor.Prove(context, condition.ConditionText, cancellationToken))),
                RuntimeHazardQuery hazards => FromPayload(
                    query,
                    new RuntimeHazardQueryPayload(
                        _executor.QueryRuntimeHazards(context, hazards.Options, cancellationToken))),
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
                new SharpProofBudgetMetadata(_options.AnalysisLimits,
                    ImmutableArray<SharpProofTruncationReason>.Empty),
                ImmutableArray<SymbolicInputWitness>.Empty,
                null,
                error);
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
                _options.AnalysisLimits,
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
            CapabilityQueryPayload capability => capability.Value.UnknownReasonDetails,
            ComplexityQueryPayload complexity => complexity.Value.UnknownReasonDetails,
            RuntimeHazardQueryPayload hazards => hazards.Value.Hazards
                .Where(static hazard => hazard.Status is SymbolicRuntimeHazardStatus.Unknown or
                    SymbolicRuntimeHazardStatus.Unsupported)
                .Select(static hazard => hazard.UnknownReasonInfo),
            ConditionQueryPayload condition when condition.Value.TruthValue == SymbolicTruthValue.Unknown =>
                new[] { SymbolicUnknownReasonTaxonomy.ForProof(SymbolicUnknownReason.Unknown,
                    condition.Value.Reason) },
            SourceQueryPayload source => GetSourceUnknownReasons(source.Value),
            _ => Array.Empty<SymbolicUnknownReasonInfo>()
        };

        return reasons
            .Where(static reason => reason.IsUnknown)
            .Select(static reason => new SharpProofUnknownReason(
                reason.Code,
                reason.Category,
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
            SourceQueryPayload source => source.Value.AnalysisTruncation,
            ConditionQueryPayload condition => condition.Value.AnalysisTruncation,
            RuntimeHazardQueryPayload hazards => hazards.Value.AnalysisTruncation,
            _ => SymbolicAnalysisTruncationInfo.None
        };
    }

    private static ImmutableArray<SymbolicInputWitness> GetEvidence(SharpProofQueryPayload payload)
    {
        return payload switch
        {
            SourceQueryPayload source => source.Value.ReachabilityWitnesses.ToImmutableArray(),
            ConditionQueryPayload condition => ImmutableArray.Create(
                condition.Value.Witness,
                condition.Value.CounterexampleWitness),
            RuntimeHazardQueryPayload hazards => hazards.Value.TriggerWitnesses.ToImmutableArray(),
            _ => ImmutableArray<SymbolicInputWitness>.Empty
        };
    }

    private SharpProofLocation GetLocation(SymbolicQueryTarget target, SharpProofQueryPayload payload)
    {
        return payload switch
        {
            SourceQueryPayload source => new SharpProofLocation(
                source.Value.FilePath,
                source.Value.Line,
                source.Value.Column,
                source.Value.Position,
                source.Value.SpanStart,
                source.Value.SpanEnd),
            ConditionQueryPayload condition => new SharpProofLocation(
                condition.Value.FilePath ?? _source.FilePath ?? string.Empty,
                condition.Value.Line,
                condition.Value.Column,
                condition.Value.Position,
                condition.Value.NodeSpanStart,
                condition.Value.NodeSpanEnd),
            RuntimeHazardQueryPayload hazards => new SharpProofLocation(
                hazards.Value.FilePath,
                hazards.Value.Line,
                null,
                null,
                hazards.Value.ScopeStart,
                hazards.Value.ScopeEnd),
            CapabilityQueryPayload capability => FromMethodResult(capability.Value),
            ComplexityQueryPayload complexity => FromMethodResult(complexity.Value),
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

    private SharpProofLocation CreateLocation(SymbolicQueryTarget target)
    {
        return new SharpProofLocation(
            _source.FilePath ?? string.Empty,
            target.LineNumber ?? target.StartLine,
            target.ColumnNumber ?? target.StartColumn,
            target.PositionOffset,
            target.SpanStart,
            target.SpanEnd);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SharpProofAnalysisSession));
    }

    private static SymbolicQueryOptions CreateQueryOptions(
        SharpProofAnalysisOptions options,
        SmtAnalysisService? smtAnalysis)
    {
        return new SymbolicQueryOptions(
                smtAnalysis: smtAnalysis,
                impliedConditions: options.ImpliedConditions)
            .WithAnalysisLimits(options.AnalysisLimits);
    }
}
