using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace PurelySharp.Tools.CorpusReport;

public sealed record CorpusReportSummary(
    ImmutableArray<string> Inputs,
    int Ps0002Count,
    int Ps0004Count,
    int Ps0009Count,
    int Ps0010Count,
    int Ps0011Count,
    int TotalPurelySharpDiagnostics,
    ImmutableArray<DiagnosticEvidenceItem> Diagnostics,
    ImmutableDictionary<string, int> ImpurityCategories,
    ImmutableDictionary<string, int> ExceptionCategories,
    ImmutableDictionary<string, int> RuleNames,
    ImmutableDictionary<string, int> OperationKinds,
    ImmutableDictionary<string, int> UnknownOperationKinds,
    ImmutableArray<RankedItem> TopImpureApis,
    ImmutableArray<RankedItem> ExceptionSources,
    ImmutableArray<RankedItem> CatalogMisses,
    ImmutableArray<RankedItem> FalsePositiveCandidates)
{
    public const string CurrentSchemaVersion = "1.4";

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;

    public static CorpusReportSummary Empty { get; } = new(
        ImmutableArray<string>.Empty,
        0,
        0,
        0,
        0,
        0,
        0,
        ImmutableArray<DiagnosticEvidenceItem>.Empty,
        ImmutableDictionary<string, int>.Empty,
        ImmutableDictionary<string, int>.Empty,
        ImmutableDictionary<string, int>.Empty,
        ImmutableDictionary<string, int>.Empty,
        ImmutableDictionary<string, int>.Empty,
        ImmutableArray<RankedItem>.Empty,
        ImmutableArray<RankedItem>.Empty,
        ImmutableArray<RankedItem>.Empty,
        ImmutableArray<RankedItem>.Empty);
}

public sealed record RankedItem(
    string Value,
    int Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category = null);

public sealed record DiagnosticEvidenceItem(
    string Input,
    string RuleId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RuleName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OperationKind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Symbol,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CatalogSource,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CalleeChain,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExceptionSymbol,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExceptionTypes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExceptionCategories,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExceptionSources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExceptionEdges = null);
