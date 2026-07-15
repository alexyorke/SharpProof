using System.Text.Json.Serialization;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

public abstract class SymbolicSchemaResultBase
{
    public abstract string Kind { get; }

    [JsonPropertyOrder(-3)]
    public int SchemaVersion => 1;

    [JsonPropertyOrder(-2)]
    public int EvidenceSchemaVersion => SharpProofEvidenceSchema.CurrentVersion;

    [JsonPropertyOrder(-1)]
    public string EvidenceSchemaCompatibility => SharpProofEvidenceSchema.CompatibilityPolicy;
}

public abstract class SymbolicCompactMethodResult<T>(T result) : SymbolicSchemaResultBase
    where T : SymbolicMethodResult
{
    protected T Result { get; } = result ?? throw new ArgumentNullException(nameof(result));

    public string FilePath => Result.FilePath;

    public string MethodDisplayName => Result.MethodDisplayName;

    public string DeclarationKind => Result.DeclarationKind;

    public int SpanStart => Result.SpanStart;

    public int SpanEnd => Result.SpanEnd;

    public int StartLine => Result.StartLine;

    public int StartColumn => Result.StartColumn;

    public int EndLine => Result.EndLine;

    public int EndColumn => Result.EndColumn;
}

public sealed class SymbolicCompactComplexityResult(SymbolicComplexityResult result)
    : SymbolicCompactMethodResult<SymbolicComplexityResult>(result)
{

    public override string Kind => "complexity";

    [JsonPropertyOrder(1)]
    public SymbolicComplexityInfo Complexity => Result.Complexity;

    [JsonPropertyOrder(1)]
    public IReadOnlyList<SymbolicComplexityDriverInfo> Drivers => Result.Drivers;

    [JsonPropertyOrder(1)]
    public IReadOnlyList<SymbolicComplexityUnknownReason> UnknownReasons => Result.UnknownReasons;

    [JsonPropertyOrder(1)]
    public IReadOnlyList<SymbolicUnknownReasonInfo> UnknownReasonDetails => Result.UnknownReasonDetails;

    [JsonPropertyOrder(1)]
    public IReadOnlyList<SymbolicComplexityCalleeInfo> CalleeSummaries => Result.CalleeSummaries;

    public static SymbolicCompactComplexityResult FromResult(SymbolicComplexityResult result)
    {
        return new SymbolicCompactComplexityResult(result);
    }
}

public sealed class SymbolicCompactCapabilityResult(SymbolicCapabilityResult result)
    : SymbolicCompactMethodResult<SymbolicCapabilityResult>(result)
{
    public override string Kind => "capabilities";

    [JsonPropertyOrder(1)]
    public SharpProof.Attributes.SharpProofCapability Capabilities => Result.Capabilities;

    [JsonPropertyOrder(1)]
    public string CapabilityText => Result.CapabilityText;

    [JsonPropertyOrder(1)]
    public bool HasUnknowns => Result.HasUnknowns;

    [JsonPropertyOrder(1)]
    public IReadOnlyList<SymbolicCapabilityUnknownReason> UnknownReasons => Result.UnknownReasons;

    [JsonPropertyOrder(1)]
    public IReadOnlyList<SymbolicUnknownReasonInfo> UnknownReasonDetails => Result.UnknownReasonDetails;

    [JsonPropertyOrder(1)]
    public IReadOnlyList<SymbolicCapabilitySite> Sites => Result.Sites;

    public static SymbolicCompactCapabilityResult FromResult(SymbolicCapabilityResult result)
    {
        return new SymbolicCompactCapabilityResult(result);
    }
}

public sealed class SymbolicCompactRuntimeHazardQueryOptions
{
    public const int DefaultMaxHazards = 250;

    public static readonly SymbolicCompactRuntimeHazardQueryOptions Default = new();

    public SymbolicCompactRuntimeHazardQueryOptions(
        int maxHazards = DefaultMaxHazards,
        int maxConditions = SymbolicCompactQueryOptions.DefaultMaxConditions)
    {
        if (maxHazards < 0)
            throw new ArgumentOutOfRangeException(nameof(maxHazards),
                "Compact runtime hazard output limits cannot be negative.");

        if (maxConditions < 0)
            throw new ArgumentOutOfRangeException(nameof(maxConditions),
                "Compact runtime hazard output limits cannot be negative.");

        MaxHazards = maxHazards;
        MaxConditions = maxConditions;
    }

    public int MaxHazards { get; }

    public int MaxConditions { get; }
}

public sealed class SymbolicCompactRuntimeHazardQueryResult : SymbolicSchemaResultBase
{
    private readonly SymbolicRuntimeHazardQueryResult _result;

    private SymbolicCompactRuntimeHazardQueryResult(
        SymbolicRuntimeHazardQueryResult result,
        IReadOnlyDictionary<string, int> statusCounts,
        IReadOnlyDictionary<string, int> kindCounts,
        IReadOnlyDictionary<string, int> exceptionTypeCounts,
        IReadOnlyDictionary<string, int> categoryCounts,
        SymbolicCompactRuntimeHazardStatusSummary analysisSummary,
        IReadOnlyList<SymbolicCompactRuntimeHazard> hazards,
        SymbolicCompactRuntimeHazardOutputTruncation truncation,
        SymbolicCompactSmtDiagnostics smtDiagnostics)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        StatusCounts = statusCounts;
        KindCounts = kindCounts;
        ExceptionTypeCounts = exceptionTypeCounts;
        CategoryCounts = categoryCounts;
        AnalysisSummary = analysisSummary;
        Hazards = hazards;
        Truncation = truncation;
        SmtDiagnostics = smtDiagnostics;
    }

    [JsonPropertyOrder(-4)]
    public override string Kind => "runtimeHazards";

    public string FilePath => _result.FilePath;

    public int LineCount => _result.LineCount;

    public string ScopeKind => Line.HasValue
        ? "line"
        : ScopeStart.HasValue && ScopeEnd.HasValue
            ? "span"
            : "file";

    public int? Line => _result.Line;

    public int? ScopeStart => _result.ScopeStart;

    public int? ScopeEnd => _result.ScopeEnd;

    public int? ScopeLength => ScopeStart.HasValue && ScopeEnd.HasValue
        ? ScopeEnd.Value - ScopeStart.Value
        : null;

    public int HazardCount => _result.HazardCount;

    public IReadOnlyDictionary<string, int> StatusCounts { get; }

    public IReadOnlyDictionary<string, int> KindCounts { get; }

    public IReadOnlyDictionary<string, int> ExceptionTypeCounts { get; }

    public IReadOnlyDictionary<string, int> CategoryCounts { get; }

    public SymbolicCompactRuntimeHazardStatusSummary AnalysisSummary { get; }

    public IReadOnlyList<SymbolicCompactRuntimeHazard> Hazards { get; }

    public SymbolicAnalysisTruncationInfo AnalysisTruncation => _result.AnalysisTruncation;

    public SymbolicCompactRuntimeHazardOutputTruncation Truncation { get; }

    public SymbolicCompactSmtDiagnostics SmtDiagnostics { get; }

    public static SymbolicCompactRuntimeHazardQueryResult FromResult(
        SymbolicRuntimeHazardQueryResult result,
        SymbolicCompactRuntimeHazardQueryOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        options ??= SymbolicCompactRuntimeHazardQueryOptions.Default;
        var hazardProjection = SymbolicCompactProjection.Project(result.Hazards, options.MaxHazards);
        var hazards = hazardProjection.Items
            .Select(hazard => SymbolicCompactRuntimeHazard.FromHazard(hazard, options))
            .ToArray();

        return new SymbolicCompactRuntimeHazardQueryResult(
            result,
            SymbolicCliCounts.By(result.Hazards, static hazard => hazard.Status.ToString()),
            SymbolicCliCounts.By(result.Hazards, static hazard => hazard.Kind.ToString()),
            SymbolicCliCounts.By(result.Hazards, static hazard => hazard.ExceptionType),
            SymbolicCliCounts.By(result.Hazards, static hazard => hazard.Category),
            SymbolicCompactRuntimeHazardStatusSummary.FromHazards(result.Hazards, result.SmtDiagnostics),
            hazards,
            new SymbolicCompactRuntimeHazardOutputTruncation(
                hazardProjection.IsTruncated,
                hazards.Any(static hazard => hazard.Truncation.PathConditions)),
            SymbolicCompactSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics));
    }

}

internal static class SymbolicCliCounts
{
    internal static IReadOnlyDictionary<string, int> By<T>(IEnumerable<T> values, Func<T, string> keySelector)
    {
        return values.GroupBy(keySelector, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
    }
}

public sealed class SymbolicCompactRuntimeHazardStatusSummary
{
    private SymbolicCompactRuntimeHazardStatusSummary(
        int hazardCount,
        int provenCount,
        int unknownCount,
        int unreachableCount,
        int unsupportedCount,
        string status,
        string summary,
        bool hasUnprovenHazards,
        bool smtConfigured,
        bool smtEnabled)
    {
        HazardCount = hazardCount;
        ProvenCount = provenCount;
        UnknownCount = unknownCount;
        UnreachableCount = unreachableCount;
        UnsupportedCount = unsupportedCount;
        Status = status;
        Summary = summary;
        HasUnprovenHazards = hasUnprovenHazards;
        SmtConfigured = smtConfigured;
        SmtEnabled = smtEnabled;
    }

    public int HazardCount { get; }

    public int ProvenCount { get; }

    public int UnknownCount { get; }

    public int UnreachableCount { get; }

    public int UnsupportedCount { get; }

    public string Status { get; }

    public string Summary { get; }

    public bool HasUnprovenHazards { get; }

    public bool SmtConfigured { get; }

    public bool SmtEnabled { get; }

    public static SymbolicCompactRuntimeHazardStatusSummary FromHazards(
        IReadOnlyList<SymbolicRuntimeHazard> hazards,
        SymbolicSmtDiagnostics smtDiagnostics)
    {
        if (hazards == null) throw new ArgumentNullException(nameof(hazards));

        if (smtDiagnostics == null) throw new ArgumentNullException(nameof(smtDiagnostics));

        var provenCount = hazards.Count(static hazard => hazard.Status == SymbolicRuntimeHazardStatus.Proven);
        var unknownCount = hazards.Count(static hazard => hazard.Status == SymbolicRuntimeHazardStatus.Unknown);
        var unreachableCount = hazards.Count(static hazard => hazard.Status == SymbolicRuntimeHazardStatus.Unreachable);
        var unsupportedCount = hazards.Count(static hazard => hazard.Status == SymbolicRuntimeHazardStatus.Unsupported);
        var hasUnprovenHazards = unknownCount != 0 || unreachableCount != 0 || unsupportedCount != 0;
        var status = hazards.Count == 0
            ? "NoHazards"
            : hasUnprovenHazards
                ? "ContainsUnproven"
                : "ProvenOnly";
        var summary = hazards.Count == 0
            ? "No runtime hazards matched the query."
            : $"{provenCount} proven, {unknownCount} unknown, {unreachableCount} unreachable, {unsupportedCount} unsupported runtime hazards matched the query.";

        return new SymbolicCompactRuntimeHazardStatusSummary(
            hazards.Count,
            provenCount,
            unknownCount,
            unreachableCount,
            unsupportedCount,
            status,
            summary,
            hasUnprovenHazards,
            smtDiagnostics.IsConfigured,
            smtDiagnostics.IsEnabled);
    }
}

public sealed class SymbolicCompactRuntimeHazard
{
    private readonly SymbolicRuntimeHazard _hazard;

    private SymbolicCompactRuntimeHazard(
        SymbolicRuntimeHazard hazard,
        IReadOnlyList<string> pathConditions,
        SymbolicCompactRuntimeHazardItemTruncation truncation)
    {
        _hazard = hazard ?? throw new ArgumentNullException(nameof(hazard));
        PathConditions = pathConditions ?? throw new ArgumentNullException(nameof(pathConditions));
        Truncation = truncation ?? throw new ArgumentNullException(nameof(truncation));
    }

    public SymbolicRuntimeHazardKind Kind => _hazard.Kind;

    public SymbolicRuntimeHazardStatus Status => _hazard.Status;

    public string StatusReason => _hazard.StatusReason;

    public string ExceptionType => _hazard.ExceptionType;

    public string Category => _hazard.Category;

    public string FilePath => _hazard.FilePath;

    public int Line => _hazard.Line;

    public int Column => _hazard.Column;

    public int SpanStart => _hazard.SpanStart;

    public int SpanEnd => _hazard.SpanEnd;

    public int SpanLength => _hazard.SpanLength;

    public int NodeStartLine => _hazard.NodeStartLine;

    public int NodeStartColumn => _hazard.NodeStartColumn;

    public int NodeEndLine => _hazard.NodeEndLine;

    public int NodeEndColumn => _hazard.NodeEndColumn;

    public string NodeKind => _hazard.NodeKind;

    public string OperationText => _hazard.OperationText;

    public string TriggerCondition => _hazard.TriggerCondition;

    public SymbolicFactInfo? TriggerPrecondition => _hazard.TriggerPrecondition;

    public string MergedInvariantText => _hazard.MergedInvariantText;

    public int PathConditionCount => _hazard.PathConditionCount;

    public IReadOnlyList<string> PathConditions { get; }

    public SymbolicReachability Reachability => _hazard.Reachability;

    public string ReachabilityReason => _hazard.ReachabilityReason;

    public SymbolicUnknownReasonInfo UnknownReasonInfo => _hazard.UnknownReasonInfo;

    public SymbolicAnalysisTruncationInfo AnalysisTruncation => _hazard.AnalysisTruncation;

    public SymbolicCompactRuntimeHazardItemTruncation Truncation { get; }

    public static SymbolicCompactRuntimeHazard FromHazard(
        SymbolicRuntimeHazard hazard,
        SymbolicCompactRuntimeHazardQueryOptions options)
    {
        if (hazard == null) throw new ArgumentNullException(nameof(hazard));

        if (options == null) throw new ArgumentNullException(nameof(options));

        var pathConditionProjection = SymbolicCompactProjection.Project(
            hazard.PathConditions,
            options.MaxConditions);

        return new SymbolicCompactRuntimeHazard(
            hazard,
            pathConditionProjection.Items,
            new SymbolicCompactRuntimeHazardItemTruncation(pathConditionProjection.IsTruncated));
    }
}

public sealed class SymbolicCompactRuntimeHazardOutputTruncation
{
    public SymbolicCompactRuntimeHazardOutputTruncation(
        bool hazards,
        bool pathConditions)
    {
        Hazards = hazards;
        PathConditions = pathConditions;
    }

    public bool Hazards { get; }

    public bool PathConditions { get; }
}

public sealed class SymbolicCompactRuntimeHazardItemTruncation
{
    public SymbolicCompactRuntimeHazardItemTruncation(bool pathConditions)
    {
        PathConditions = pathConditions;
    }

    public bool PathConditions { get; }
}
