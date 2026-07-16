using System.Text.Json;
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

public sealed class SymbolicCompactRuntimeHazardQueryOptions
{
    public const int DefaultMaxHazards = 250;
    public static readonly SymbolicCompactRuntimeHazardQueryOptions Default = new();

    public SymbolicCompactRuntimeHazardQueryOptions(
        int maxHazards = DefaultMaxHazards,
        int maxConditions = SymbolicCompactQueryOptions.DefaultMaxConditions)
    {
        if (maxHazards < 0 || maxConditions < 0)
            throw new ArgumentOutOfRangeException(
                maxHazards < 0 ? nameof(maxHazards) : nameof(maxConditions),
                "Compact runtime hazard output limits cannot be negative.");

        MaxHazards = maxHazards;
        MaxConditions = maxConditions;
    }

    public int MaxHazards { get; }
    public int MaxConditions { get; }
}

internal sealed record SymbolicCompactRuntimeHazardProjection(
    JsonElement Json,
    IReadOnlyList<SymbolicRuntimeHazard> Hazards,
    int HazardCount,
    bool HazardsTruncated,
    bool PathConditionsTruncated,
    SymbolicAnalysisTruncationInfo AnalysisTruncation)
{
    internal static SymbolicCompactRuntimeHazardProjection Create(
        SymbolicRuntimeHazardQueryResult result,
        SymbolicCompactRuntimeHazardQueryOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        options ??= SymbolicCompactRuntimeHazardQueryOptions.Default;

        var hazardProjection = SymbolicCompactProjection.Project(result.Hazards, options.MaxHazards);
        var pathConditionsTruncated = false;
        var hazards = hazardProjection.Items.Select(hazard =>
        {
            var paths = SymbolicCompactProjection.Project(hazard.PathConditions, options.MaxConditions);
            pathConditionsTruncated |= paths.IsTruncated;
            return new
            {
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
                hazard.SpanLength,
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
                PathConditions = paths.Items,
                hazard.Reachability,
                hazard.ReachabilityReason,
                hazard.UnknownReasonInfo,
                hazard.AnalysisTruncation,
                Truncation = new { PathConditions = paths.IsTruncated }
            };
        }).ToArray();

        var provenCount = result.Hazards.Count(static hazard => hazard.Status == SymbolicRuntimeHazardStatus.Proven);
        var unknownCount = result.Hazards.Count(static hazard => hazard.Status == SymbolicRuntimeHazardStatus.Unknown);
        var unreachableCount = result.Hazards.Count(static hazard => hazard.Status == SymbolicRuntimeHazardStatus.Unreachable);
        var unsupportedCount = result.Hazards.Count(static hazard => hazard.Status == SymbolicRuntimeHazardStatus.Unsupported);
        var hasUnprovenHazards = unknownCount != 0 || unreachableCount != 0 || unsupportedCount != 0;
        var json = JsonSerializer.SerializeToElement(new
        {
            Kind = "runtimeHazards",
            SchemaVersion = 1,
            EvidenceSchemaVersion = SharpProofEvidenceSchema.CurrentVersion,
            EvidenceSchemaCompatibility = SharpProofEvidenceSchema.CompatibilityPolicy,
            result.FilePath,
            result.LineCount,
            ScopeKind = result.Line.HasValue ? "line" : result.ScopeStart.HasValue ? "span" : "file",
            result.Line,
            result.ScopeStart,
            result.ScopeEnd,
            ScopeLength = result.ScopeStart.HasValue && result.ScopeEnd.HasValue
                ? result.ScopeEnd - result.ScopeStart
                : null,
            result.HazardCount,
            StatusCounts = SymbolicCliCounts.By(result.Hazards, static hazard => hazard.Status.ToString()),
            KindCounts = SymbolicCliCounts.By(result.Hazards, static hazard => hazard.Kind.ToString()),
            ExceptionTypeCounts = SymbolicCliCounts.By(result.Hazards, static hazard => hazard.ExceptionType),
            CategoryCounts = SymbolicCliCounts.By(result.Hazards, static hazard => hazard.Category),
            AnalysisSummary = new
            {
                result.HazardCount,
                ProvenCount = provenCount,
                UnknownCount = unknownCount,
                UnreachableCount = unreachableCount,
                UnsupportedCount = unsupportedCount,
                Status = result.HazardCount == 0 ? "NoHazards" : hasUnprovenHazards ? "ContainsUnproven" : "ProvenOnly",
                Summary = result.HazardCount == 0
                    ? "No runtime hazards matched the query."
                    : $"{provenCount} proven, {unknownCount} unknown, {unreachableCount} unreachable, {unsupportedCount} unsupported runtime hazards matched the query.",
                HasUnprovenHazards = hasUnprovenHazards,
                SmtConfigured = result.SmtDiagnostics.IsConfigured,
                SmtEnabled = result.SmtDiagnostics.IsEnabled
            },
            Hazards = hazards,
            result.AnalysisTruncation,
            Truncation = new
            {
                Hazards = hazardProjection.IsTruncated,
                PathConditions = pathConditionsTruncated
            },
            SmtDiagnostics = SymbolicCompactSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics)
        }, SymbolicCliProjectionJson.Options);

        return new SymbolicCompactRuntimeHazardProjection(
            json,
            hazardProjection.Items,
            result.HazardCount,
            hazardProjection.IsTruncated,
            pathConditionsTruncated,
            result.AnalysisTruncation);
    }
}

internal static class SymbolicCliCounts
{
    internal static IReadOnlyDictionary<string, int> By<T>(IEnumerable<T> values, Func<T, string> keySelector) =>
        values.GroupBy(keySelector, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
}
