using System.Text.Json.Serialization;

namespace SharpProof.Tools.CorpusReport;

public sealed record CorpusReportSummary(
    ImmutableArray<string> Inputs,
    int EnforcePureFailureCount,
    int AllocationContractFailureCount,
    int CapabilityContractFailureCount,
    int ExceptionDiagnosticCount,
    int TotalSharpProofDiagnostics,
    ImmutableArray<DiagnosticEvidenceItem> Diagnostics,
    ImmutableDictionary<string, int> EffectCategories,
    ImmutableDictionary<string, int> EffectFlags,
    ImmutableDictionary<string, int> CapabilityFlags,
    ImmutableDictionary<string, int> DerivedVerdicts,
    ImmutableArray<RankedItem> UnknownBoundaries,
    ImmutableArray<RankedItem> ExceptionSources) {
    public const string CurrentSchemaVersion = "2.0";

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;

    public static CorpusReportSummary Empty { get; } = new(
        ImmutableArray<string>.Empty,
        0, 0, 0, 0, 0,
        ImmutableArray<DiagnosticEvidenceItem>.Empty,
        ImmutableDictionary<string, int>.Empty,
        ImmutableDictionary<string, int>.Empty,
        ImmutableDictionary<string, int>.Empty,
        ImmutableDictionary<string, int>.Empty,
        ImmutableArray<RankedItem>.Empty,
        ImmutableArray<RankedItem>.Empty);
}

public sealed record RankedItem(string Value, int Count, string? Category = null);

public sealed record DiagnosticEvidenceItem(
    string Input,
    string RuleId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EffectCategory,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EffectFlags,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CapabilityFlags,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Verdict,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UnknownReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Symbol,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExceptionTypes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExceptionCategories,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExceptionSources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExceptionEdges);
