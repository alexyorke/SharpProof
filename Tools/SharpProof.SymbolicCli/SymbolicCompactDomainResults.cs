using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

public interface ISymbolicCompactResult
{
    string Kind { get; }

    int SchemaVersion { get; }

    int EvidenceSchemaVersion { get; }

    string EvidenceSchemaCompatibility { get; }
}

public sealed class SymbolicCompactComplexityResult : ISymbolicCompactResult
{
    private SymbolicCompactComplexityResult(
        string filePath,
        string methodDisplayName,
        string declarationKind,
        int spanStart,
        int spanEnd,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        SymbolicComplexityInfo complexity,
        IReadOnlyList<SymbolicComplexityDriverInfo> drivers,
        IReadOnlyList<SymbolicComplexityUnknownReason> unknownReasons,
        IReadOnlyList<SymbolicUnknownReasonInfo> unknownReasonDetails,
        IReadOnlyList<SymbolicComplexityCalleeInfo> calleeSummaries)
    {
        FilePath = filePath;
        MethodDisplayName = methodDisplayName;
        DeclarationKind = declarationKind;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        Complexity = complexity;
        Drivers = drivers;
        UnknownReasons = unknownReasons;
        UnknownReasonDetails = unknownReasonDetails;
        CalleeSummaries = calleeSummaries;
    }

    public int SchemaVersion => 1;

    public int EvidenceSchemaVersion => SharpProofEvidenceSchema.CurrentVersion;

    public string EvidenceSchemaCompatibility => SharpProofEvidenceSchema.CompatibilityPolicy;

    public string Kind => "complexity";

    public string FilePath { get; }

    public string MethodDisplayName { get; }

    public string DeclarationKind { get; }

    public int SpanStart { get; }

    public int SpanEnd { get; }

    public int StartLine { get; }

    public int StartColumn { get; }

    public int EndLine { get; }

    public int EndColumn { get; }

    public SymbolicComplexityInfo Complexity { get; }

    public IReadOnlyList<SymbolicComplexityDriverInfo> Drivers { get; }

    public IReadOnlyList<SymbolicComplexityUnknownReason> UnknownReasons { get; }

    public IReadOnlyList<SymbolicUnknownReasonInfo> UnknownReasonDetails { get; }

    public IReadOnlyList<SymbolicComplexityCalleeInfo> CalleeSummaries { get; }

    public static SymbolicCompactComplexityResult FromResult(SymbolicComplexityResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        return new SymbolicCompactComplexityResult(
            result.FilePath,
            result.MethodDisplayName,
            result.DeclarationKind,
            result.SpanStart,
            result.SpanEnd,
            result.StartLine,
            result.StartColumn,
            result.EndLine,
            result.EndColumn,
            result.Complexity,
            result.Drivers,
            result.UnknownReasons,
            result.UnknownReasonDetails,
            result.CalleeSummaries);
    }
}

public sealed class SymbolicCompactCapabilityResult : ISymbolicCompactResult
{
    private SymbolicCompactCapabilityResult(
        string filePath,
        string methodDisplayName,
        string declarationKind,
        int spanStart,
        int spanEnd,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        SymbolicCapability capabilities,
        string capabilityText,
        bool hasUnknowns,
        IReadOnlyList<SymbolicCapabilityUnknownReason> unknownReasons,
        IReadOnlyList<SymbolicUnknownReasonInfo> unknownReasonDetails,
        IReadOnlyList<SymbolicCapabilitySite> sites)
    {
        FilePath = filePath;
        MethodDisplayName = methodDisplayName;
        DeclarationKind = declarationKind;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        Capabilities = capabilities;
        CapabilityText = capabilityText;
        HasUnknowns = hasUnknowns;
        UnknownReasons = unknownReasons;
        UnknownReasonDetails = unknownReasonDetails;
        Sites = sites;
    }

    public int SchemaVersion => 1;

    public int EvidenceSchemaVersion => SharpProofEvidenceSchema.CurrentVersion;

    public string EvidenceSchemaCompatibility => SharpProofEvidenceSchema.CompatibilityPolicy;

    public string Kind => "capabilities";

    public string FilePath { get; }

    public string MethodDisplayName { get; }

    public string DeclarationKind { get; }

    public int SpanStart { get; }

    public int SpanEnd { get; }

    public int StartLine { get; }

    public int StartColumn { get; }

    public int EndLine { get; }

    public int EndColumn { get; }

    public SymbolicCapability Capabilities { get; }

    public string CapabilityText { get; }

    public bool HasUnknowns { get; }

    public IReadOnlyList<SymbolicCapabilityUnknownReason> UnknownReasons { get; }

    public IReadOnlyList<SymbolicUnknownReasonInfo> UnknownReasonDetails { get; }

    public IReadOnlyList<SymbolicCapabilitySite> Sites { get; }

    public static SymbolicCompactCapabilityResult FromResult(SymbolicCapabilityResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        return new SymbolicCompactCapabilityResult(
            result.FilePath,
            result.MethodDisplayName,
            result.DeclarationKind,
            result.SpanStart,
            result.SpanEnd,
            result.StartLine,
            result.StartColumn,
            result.EndLine,
            result.EndColumn,
            result.Capabilities,
            result.CapabilityText,
            result.HasUnknowns,
            result.UnknownReasons,
            result.UnknownReasonDetails,
            result.Sites);
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

public sealed class SymbolicCompactRuntimeHazardQueryResult : ISymbolicCompactResult
{
    private SymbolicCompactRuntimeHazardQueryResult(
        string filePath,
        int lineCount,
        int? line,
        int? scopeStart,
        int? scopeEnd,
        int hazardCount,
        IReadOnlyDictionary<string, int> statusCounts,
        IReadOnlyDictionary<string, int> kindCounts,
        IReadOnlyDictionary<string, int> exceptionTypeCounts,
        IReadOnlyDictionary<string, int> categoryCounts,
        SymbolicCompactRuntimeHazardStatusSummary analysisSummary,
        IReadOnlyList<SymbolicCompactRuntimeHazard> hazards,
        SymbolicAnalysisTruncationInfo analysisTruncation,
        SymbolicCompactRuntimeHazardOutputTruncation truncation,
        SymbolicCompactRuntimeHazardSmtDiagnostics smtDiagnostics)
    {
        FilePath = filePath;
        LineCount = lineCount;
        Line = line;
        ScopeStart = scopeStart;
        ScopeEnd = scopeEnd;
        HazardCount = hazardCount;
        StatusCounts = statusCounts;
        KindCounts = kindCounts;
        ExceptionTypeCounts = exceptionTypeCounts;
        CategoryCounts = categoryCounts;
        AnalysisSummary = analysisSummary;
        Hazards = hazards;
        AnalysisTruncation = analysisTruncation;
        Truncation = truncation;
        SmtDiagnostics = smtDiagnostics;
    }

    public string Kind => "runtimeHazards";

    public int SchemaVersion => 1;

    public int EvidenceSchemaVersion => SharpProofEvidenceSchema.CurrentVersion;

    public string EvidenceSchemaCompatibility => SharpProofEvidenceSchema.CompatibilityPolicy;

    public string FilePath { get; }

    public int LineCount { get; }

    public string ScopeKind => Line.HasValue
        ? "line"
        : ScopeStart.HasValue && ScopeEnd.HasValue
            ? "span"
            : "file";

    public int? Line { get; }

    public int? ScopeStart { get; }

    public int? ScopeEnd { get; }

    public int? ScopeLength => ScopeStart.HasValue && ScopeEnd.HasValue
        ? ScopeEnd.Value - ScopeStart.Value
        : null;

    public int HazardCount { get; }

    public IReadOnlyDictionary<string, int> StatusCounts { get; }

    public IReadOnlyDictionary<string, int> KindCounts { get; }

    public IReadOnlyDictionary<string, int> ExceptionTypeCounts { get; }

    public IReadOnlyDictionary<string, int> CategoryCounts { get; }

    public SymbolicCompactRuntimeHazardStatusSummary AnalysisSummary { get; }

    public IReadOnlyList<SymbolicCompactRuntimeHazard> Hazards { get; }

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; }

    public SymbolicCompactRuntimeHazardOutputTruncation Truncation { get; }

    public SymbolicCompactRuntimeHazardSmtDiagnostics SmtDiagnostics { get; }

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
            result.FilePath,
            result.LineCount,
            result.Line,
            result.ScopeStart,
            result.ScopeEnd,
            result.HazardCount,
            CountBy(result.Hazards, static hazard => hazard.Status.ToString()),
            CountBy(result.Hazards, static hazard => hazard.Kind.ToString()),
            CountBy(result.Hazards, static hazard => hazard.ExceptionType),
            CountBy(result.Hazards, static hazard => hazard.Category),
            SymbolicCompactRuntimeHazardStatusSummary.FromHazards(result.Hazards, result.SmtDiagnostics),
            hazards,
            result.AnalysisTruncation,
            new SymbolicCompactRuntimeHazardOutputTruncation(
                hazardProjection.IsTruncated,
                hazards.Any(static hazard => hazard.Truncation.PathConditions)),
            SymbolicCompactRuntimeHazardSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics));
    }

    private static IReadOnlyDictionary<string, int> CountBy(
        IEnumerable<SymbolicRuntimeHazard> hazards,
        Func<SymbolicRuntimeHazard, string> keySelector)
    {
        return hazards
            .GroupBy(keySelector, StringComparer.Ordinal)
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
    private SymbolicCompactRuntimeHazard(
        SymbolicRuntimeHazardKind kind,
        SymbolicRuntimeHazardStatus status,
        string statusReason,
        string exceptionType,
        string category,
        string filePath,
        int line,
        int column,
        int spanStart,
        int spanEnd,
        int nodeStartLine,
        int nodeStartColumn,
        int nodeEndLine,
        int nodeEndColumn,
        string nodeKind,
        string operationText,
        string triggerCondition,
        SymbolicFactInfo? triggerPrecondition,
        string mergedInvariantText,
        int pathConditionCount,
        IReadOnlyList<string> pathConditions,
        SymbolicReachability reachability,
        string reachabilityReason,
        SymbolicUnknownReasonInfo unknownReasonInfo,
        SymbolicAnalysisTruncationInfo analysisTruncation,
        SymbolicCompactRuntimeHazardItemTruncation truncation)
    {
        Kind = kind;
        Status = status;
        StatusReason = statusReason;
        ExceptionType = exceptionType;
        Category = category;
        FilePath = filePath;
        Line = line;
        Column = column;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        SpanLength = spanEnd - spanStart;
        NodeStartLine = nodeStartLine;
        NodeStartColumn = nodeStartColumn;
        NodeEndLine = nodeEndLine;
        NodeEndColumn = nodeEndColumn;
        NodeKind = nodeKind;
        OperationText = operationText;
        TriggerCondition = triggerCondition;
        TriggerPrecondition = triggerPrecondition;
        MergedInvariantText = mergedInvariantText;
        PathConditionCount = pathConditionCount;
        PathConditions = pathConditions;
        Reachability = reachability;
        ReachabilityReason = reachabilityReason;
        UnknownReasonInfo = unknownReasonInfo;
        AnalysisTruncation = analysisTruncation;
        Truncation = truncation;
    }

    public SymbolicRuntimeHazardKind Kind { get; }

    public SymbolicRuntimeHazardStatus Status { get; }

    public string StatusReason { get; }

    public string ExceptionType { get; }

    public string Category { get; }

    public string FilePath { get; }

    public int Line { get; }

    public int Column { get; }

    public int SpanStart { get; }

    public int SpanEnd { get; }

    public int SpanLength { get; }

    public int NodeStartLine { get; }

    public int NodeStartColumn { get; }

    public int NodeEndLine { get; }

    public int NodeEndColumn { get; }

    public string NodeKind { get; }

    public string OperationText { get; }

    public string TriggerCondition { get; }

    public SymbolicFactInfo? TriggerPrecondition { get; }

    public string MergedInvariantText { get; }

    public int PathConditionCount { get; }

    public IReadOnlyList<string> PathConditions { get; }

    public SymbolicReachability Reachability { get; }

    public string ReachabilityReason { get; }

    public SymbolicUnknownReasonInfo UnknownReasonInfo { get; }

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; }

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
            hazard.Kind,
            hazard.Status,
            hazard.StatusReason,
            hazard.ExceptionType,
            hazard.Category,
            hazard.FilePath,
            hazard.Line,
            hazard.Column,
            hazard.SpanStart,
            hazard.SpanEnd,
            hazard.NodeStartLine,
            hazard.NodeStartColumn,
            hazard.NodeEndLine,
            hazard.NodeEndColumn,
            hazard.NodeKind,
            hazard.OperationText,
            hazard.TriggerCondition,
            hazard.TriggerPrecondition,
            hazard.MergedInvariantText,
            hazard.PathConditionCount,
            pathConditionProjection.Items,
            hazard.Reachability,
            hazard.ReachabilityReason,
            hazard.UnknownReasonInfo,
            hazard.AnalysisTruncation,
            new SymbolicCompactRuntimeHazardItemTruncation(pathConditionProjection.IsTruncated));
    }
}

public sealed class SymbolicCompactRuntimeHazardSmtDiagnostics
{
    private readonly SymbolicSmtDiagnosticsSnapshot snapshot;

    private SymbolicCompactRuntimeHazardSmtDiagnostics(SymbolicSmtDiagnosticsSnapshot snapshot)
    {
        this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public bool IsConfigured => snapshot.IsConfigured;

    public string Mode => snapshot.Mode.ToString();

    public bool IsEnabled => snapshot.IsEnabled;

    public int QueryTimeoutMs => snapshot.QueryTimeoutMs;

    public int MethodBudgetMs => snapshot.MethodBudgetMs;

    public int MaxPathConditions => snapshot.MaxPathConditions;

    public int MaxExpressionNodes => snapshot.MaxExpressionNodes;

    public int ExecutedQueryCount => snapshot.ExecutedQueryCount;

    public int CacheEntryCount => snapshot.CacheEntryCount;

    public SmtAnalysisHealth Health => snapshot.Health;

    public SmtSolverLifecycleOptions Lifecycle => snapshot.Lifecycle;

    public static SymbolicCompactRuntimeHazardSmtDiagnostics FromDiagnostics(SymbolicSmtDiagnostics diagnostics)
    {
        if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));

        return new SymbolicCompactRuntimeHazardSmtDiagnostics(diagnostics.Snapshot);
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
