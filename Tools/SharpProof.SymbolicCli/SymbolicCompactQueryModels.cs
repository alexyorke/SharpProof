using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

public sealed class SymbolicCompactQueryOptions
{
    public const int DefaultMaxLines = 100;
    public const int DefaultMaxProgramPoints = 250;
    public const int DefaultMaxFacts = 50;
    public const int DefaultMaxConditions = 50;
    public const int DefaultMaxProofs = 50;

    public static readonly SymbolicCompactQueryOptions Default = new();

    public static readonly SymbolicCompactQueryOptions SummaryOnly = new(
        0,
        0);

    public SymbolicCompactQueryOptions(
        int maxLines = DefaultMaxLines,
        int maxProgramPoints = DefaultMaxProgramPoints,
        int maxFacts = DefaultMaxFacts,
        int maxConditions = DefaultMaxConditions,
        int maxProofs = DefaultMaxProofs,
        IEnumerable<string>? invariantTargets = null)
    {
        MaxLines = ValidateNonNegative(maxLines, nameof(maxLines));
        MaxProgramPoints = ValidateNonNegative(maxProgramPoints, nameof(maxProgramPoints));
        MaxFacts = ValidateNonNegative(maxFacts, nameof(maxFacts));
        MaxConditions = ValidateNonNegative(maxConditions, nameof(maxConditions));
        MaxProofs = ValidateNonNegative(maxProofs, nameof(maxProofs));
        InvariantTargets = NormalizeInvariantTargets(invariantTargets);
    }

    public int MaxLines { get; }

    public int MaxProgramPoints { get; }

    public int MaxFacts { get; }

    public int MaxConditions { get; }

    public int MaxProofs { get; }

    public IReadOnlyList<string> InvariantTargets { get; }

    public bool HasInvariantTargetFilter => InvariantTargets.Count != 0;

    private static int ValidateNonNegative(int value, string paramName)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(paramName, "Compact output limits cannot be negative.");

        return value;
    }

    private static IReadOnlyList<string> NormalizeInvariantTargets(IEnumerable<string>? targets)
    {
        if (targets == null) return Array.Empty<string>();

        return targets
            .Where(static target => !string.IsNullOrWhiteSpace(target))
            .Select(static target => target!.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static target => target, StringComparer.Ordinal)
            .ToArray();
    }
}

internal sealed record SymbolicCompactSourceQueryDescriptor(JsonElement Json) : ISymbolicRawJsonProjection
{

    internal static SymbolicCompactSourceQueryDescriptor FromScope(SymbolicCompactQueryScope scope)
    {
        if (scope == null) throw new ArgumentNullException(nameof(scope));

        var isSpan = string.Equals(scope.Kind, "span", StringComparison.Ordinal);

        return new SymbolicCompactSourceQueryDescriptor(SymbolicOrderedJson.Object(
            ("kind", scope.Kind),
            ("filePath", scope.FilePath),
            ("line", scope.Line),
            ("column", scope.Column),
            ("position", scope.Position),
            ("spanStart", isSpan ? scope.NodeSpanStart : null),
            ("spanEnd", isSpan ? scope.NodeSpanEnd : null),
            ("spanLength", isSpan ? scope.NodeSpanLength : null),
            ("startLine", isSpan ? scope.NodeStartLine : null),
            ("startColumn", isSpan ? scope.NodeStartColumn : null),
            ("endLine", isSpan ? scope.NodeEndLine : null),
            ("endColumn", isSpan ? scope.NodeEndColumn : null),
            ("nodeKind", scope.NodeKind),
            ("methodName", string.IsNullOrWhiteSpace(scope.MethodName) ? null : scope.MethodName),
            ("programPointKind", scope.ProgramPointKind)));
    }
}

internal sealed record SymbolicInvariantQueryFocus(JsonElement Json) : ISymbolicRawJsonProjection
{

    internal static SymbolicInvariantQueryFocus FromProjection(SymbolicCompactQueryProjection result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        var scope = result.Scope;
        var reachabilityStatus = ResolveReachabilityStatus(scope, result.ProgramPointCount, result.Reachability);
        var knownCount = result.Reachability.ReachableCount + result.Reachability.UnreachableCount;
        return new SymbolicInvariantQueryFocus(SymbolicOrderedJson.Object(
            ("scopeKind", scope.Kind), ("filePath", scope.FilePath),
            ("hasSourceLocation", scope.Line.HasValue || scope.Position.HasValue || scope.NodeSpanStart.HasValue),
            ("line", scope.Line), ("column", scope.Column), ("position", scope.Position),
            ("requestedLine", scope.RequestedLine), ("requestedColumn", scope.RequestedColumn),
            ("requestedPosition", scope.RequestedPosition),
            ("requestedPositionDistance", scope.RequestedPositionDistance),
            ("containsRequestedPosition", scope.ContainsRequestedPosition),
            ("spanStart", scope.Kind == "span" ? scope.NodeSpanStart : null),
            ("spanEnd", scope.Kind == "span" ? scope.NodeSpanEnd : null),
            ("spanLength", scope.Kind == "span" ? scope.NodeSpanLength : null),
            ("startLine", scope.Kind == "span" ? scope.NodeStartLine : null),
            ("startColumn", scope.Kind == "span" ? scope.NodeStartColumn : null),
            ("endLine", scope.Kind == "span" ? scope.NodeEndLine : null),
            ("endColumn", scope.Kind == "span" ? scope.NodeEndColumn : null),
            ("nodeKind", scope.NodeKind), ("methodName", scope.MethodName),
            ("programPointKind", scope.ProgramPointKind),
            ("reachabilityStatus", reachabilityStatus),
            ("reachabilityReason", ResolveReachabilityReason(scope, result.ProgramPointCount, reachabilityStatus)),
            ("programPointCount", result.ProgramPointCount),
            ("reachabilityKnownCount", knownCount),
            ("hasKnownReachability", knownCount != 0)));
    }

    private static string ResolveReachabilityStatus(
        SymbolicCompactQueryScope scope, int programPointCount, SymbolicReachabilitySummary reachability)
    {
        if (!string.IsNullOrWhiteSpace(scope.PointReachability)) return scope.PointReachability!;

        if (programPointCount == 0) return "NoProgramPoints";

        if (reachability.ReachableCount == programPointCount) return SymbolicReachability.Reachable.ToString();

        if (reachability.UnreachableCount == programPointCount)
            return SymbolicReachability.Unreachable.ToString();

        if (reachability.UnknownCount == programPointCount) return SymbolicReachability.Unknown.ToString();

        if (reachability.NotCheckedCount == programPointCount) return SymbolicReachability.NotChecked.ToString();

        return "Mixed";
    }

    private static string ResolveReachabilityReason(
        SymbolicCompactQueryScope scope,
        int programPointCount,
        string reachabilityStatus)
    {
        if (!string.IsNullOrWhiteSpace(scope.ReachabilityReason)) return scope.ReachabilityReason!;

        if (programPointCount == 0) return "no_program_points";

        if (string.Equals(reachabilityStatus, "Mixed", StringComparison.Ordinal))
            return "mixed_program_point_reachability";

        return "uniform_program_point_reachability";
    }
}

internal sealed record SymbolicCompactQueryScope(
    string Kind,
    string FilePath,
    int? Line = null,
    int? Column = null,
    int? Position = null,
    string? NodeKind = null,
    string? MethodName = null,
    string? ProgramPointKind = null,
    int? NodeSpanStart = null,
    int? NodeSpanEnd = null,
    int? NodeSpanLength = null,
    int? NodeStartLine = null,
    int? NodeStartColumn = null,
    int? NodeEndLine = null,
    int? NodeEndColumn = null,
    string? PointReachability = null,
    string? ReachabilityReason = null,
    int? RequestedLine = null,
    int? RequestedColumn = null,
    int? RequestedPosition = null,
    int? RequestedPositionDistance = null,
    bool? ContainsRequestedPosition = null)
{
    internal static SymbolicCompactQueryScope FromResult(SymbolicQueryResult result)
    {
        if (result.Scope.Kind != SymbolicQueryScopeKind.Point)
            return new SymbolicCompactQueryScope(
                result.ScopeKind,
                result.FilePath,
                Line: result.Line,
                NodeSpanStart: result.SpanStart,
                NodeSpanEnd: result.SpanEnd,
                NodeSpanLength: result.SpanEnd - result.SpanStart,
                NodeStartLine: result.Scope.StartLine,
                NodeStartColumn: result.Scope.StartColumn,
                NodeEndLine: result.Scope.EndLine,
                NodeEndColumn: result.Scope.EndColumn);

        var point = result.ProgramPoints.Single();
        return new SymbolicCompactQueryScope(
            "point", point.FilePath,
            Line: point.Line, Column: point.Column, Position: point.Position,
            NodeKind: point.NodeKind, MethodName: point.MethodName, ProgramPointKind: point.ProgramPointKind,
            NodeSpanStart: point.NodeSpanStart, NodeSpanEnd: point.NodeSpanEnd,
            NodeSpanLength: point.NodeSpanLength, NodeStartLine: point.NodeStartLine,
            NodeStartColumn: point.NodeStartColumn, NodeEndLine: point.NodeEndLine,
            NodeEndColumn: point.NodeEndColumn, PointReachability: point.Reachability.ToString(),
            ReachabilityReason: point.ReachabilityReason, RequestedLine: point.RequestedLine,
            RequestedColumn: point.RequestedColumn, RequestedPosition: point.RequestedPosition,
            RequestedPositionDistance: point.RequestedPositionDistance,
            ContainsRequestedPosition: point.ContainsRequestedPosition);
    }
}

internal sealed record SymbolicCompactQueryProjection(
    SymbolicCompactQueryScope Scope,
    int? LineCount,
    int LinesWithProgramPoints,
    int ProgramPointCount,
    SymbolicCompactScopeProjection Projection,
    IReadOnlyList<SymbolicCompactLineResult> Lines,
    SymbolicAnalysisTruncationInfo AnalysisTruncation,
    SymbolicCompactAnalysisSummary AnalysisSummary,
    SymbolicCompactSourceQueryDescriptor QueryDescriptor,
    JsonElement Json)
{
    internal int SchemaVersion => 1;
    internal int EvidenceSchemaVersion => SharpProofEvidenceSchema.CurrentVersion;
    internal string EvidenceSchemaCompatibility => SharpProofEvidenceSchema.CompatibilityPolicy;
    internal SymbolicCompactInvariantSummary ObservedInvariant => Projection.ObservedInvariant;
    internal SymbolicCompactInvariantSummary ConservativeInvariant => Projection.ConservativeInvariant;
    internal SymbolicCompactInvariantQueryView InvariantQuery => Projection.InvariantQuery;
    internal string MergedInvariantText => ConservativeInvariant.Text;
    internal SymbolicReachabilitySummary Reachability => Projection.Reachability;
    internal SymbolicProgramPointSummary ProgramPointSummary => Projection.ProgramPointSummary;
    internal SymbolicProofOutcomeSummary ProofOutcomes => ProgramPointSummary.ProofOutcomes;
    internal IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs => Projection.ConditionProofs;
    internal IReadOnlyList<SymbolicCompactProgramPointResult> ProgramPoints => Projection.ProgramPoints;
    internal SymbolicCompactSmtDiagnostics SmtDiagnostics => Projection.SmtDiagnostics;
    internal SymbolicCompactOutputTruncation Truncation => Projection.Truncation;

    internal static SymbolicCompactQueryProjection Create(
        SymbolicQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        var normalizedOptions = options ?? SymbolicCompactQueryOptions.Default;
        var scope = SymbolicCompactQueryScope.FromResult(result);
        var (lineCount, linesWithProgramPoints, programPointCount, projection, lines, truncation) =
            result.Scope.Kind == SymbolicQueryScopeKind.Point
                ? CreatePoint(result.ProgramPoints.Single(), normalizedOptions)
                : CreateAggregate(result, normalizedOptions);
        var analysisSummary = SymbolicCompactAnalysisSummary.From(
            projection.InvariantQuery,
            projection.ProgramPointSummary,
            projection.SmtDiagnostics,
            truncation);
        var queryDescriptor = SymbolicCompactSourceQueryDescriptor.FromScope(scope);
        var isSpan = string.Equals(scope.Kind, "span", StringComparison.Ordinal);
        var json = JsonSerializer.SerializeToElement(new
        {
            Kind = scope.Kind,
            SchemaVersion = 1,
            EvidenceSchemaVersion = SharpProofEvidenceSchema.CurrentVersion,
            EvidenceSchemaCompatibility = SharpProofEvidenceSchema.CompatibilityPolicy,
            scope.FilePath,
            QueryDescriptor = queryDescriptor,
            scope.Line,
            scope.Column,
            scope.Position,
            scope.RequestedLine,
            scope.RequestedColumn,
            scope.RequestedPosition,
            scope.RequestedPositionDistance,
            scope.ContainsRequestedPosition,
            scope.NodeKind,
            scope.MethodName,
            scope.ProgramPointKind,
            scope.NodeSpanStart,
            scope.NodeSpanEnd,
            scope.NodeSpanLength,
            scope.NodeStartLine,
            scope.NodeStartColumn,
            scope.NodeEndLine,
            scope.NodeEndColumn,
            QuerySpanStart = isSpan ? scope.NodeSpanStart : null,
            QuerySpanEnd = isSpan ? scope.NodeSpanEnd : null,
            QuerySpanLength = isSpan ? scope.NodeSpanLength : null,
            QueryStartLine = isSpan ? scope.NodeStartLine : null,
            QueryStartColumn = isSpan ? scope.NodeStartColumn : null,
            QueryEndLine = isSpan ? scope.NodeEndLine : null,
            QueryEndColumn = isSpan ? scope.NodeEndColumn : null,
            scope.PointReachability,
            scope.ReachabilityReason,
            LineCount = lineCount,
            LinesWithProgramPoints = linesWithProgramPoints,
            ProgramPointCount = programPointCount,
            projection.ObservedInvariant,
            projection.ConservativeInvariant,
            projection.InvariantQuery,
            MergedInvariantText = projection.ConservativeInvariant.Text,
            projection.Reachability,
            projection.ProgramPointSummary,
            ProofOutcomes = projection.ProgramPointSummary.ProofOutcomes,
            projection.ConditionProofs,
            Lines = lines,
            projection.ProgramPoints,
            projection.SmtDiagnostics,
            AnalysisTruncation = truncation,
            AnalysisSummary = analysisSummary,
            projection.Truncation
        }, SymbolicCliProjectionJson.Options);
        return new SymbolicCompactQueryProjection(
            scope,
            lineCount,
            linesWithProgramPoints,
            programPointCount,
            projection,
            lines,
            truncation,
            analysisSummary,
            queryDescriptor,
            json);
    }

    private static (int? LineCount, int LinesWithProgramPoints, int ProgramPointCount,
        SymbolicCompactScopeProjection Projection, IReadOnlyList<SymbolicCompactLineResult> Lines,
        SymbolicAnalysisTruncationInfo AnalysisTruncation) CreatePoint(
        SymbolicProgramPointResult result,
        SymbolicCompactQueryOptions options)
    {
        var sourcePoints = new[] { result };
        var projection = SymbolicCompactScopeProjection.Create(
            SymbolicInvariantResult.FromFacts(result.Facts),
            result.Facts,
            result.Invariant,
            null,
            result.InvariantQuery,
            SymbolicReachabilitySummary.FromProgramPoints(sourcePoints),
            SymbolicProgramPointSummary.FromProgramPoints(sourcePoints),
            SymbolicConditionProofSummary.FromProgramPoints(sourcePoints),
            sourcePoints,
            result.SmtDiagnostics,
            options,
            options.MaxProgramPoints);
        return (null, 1, 1, projection, Array.Empty<SymbolicCompactLineResult>(), result.AnalysisTruncation);
    }

    private static (int? LineCount, int LinesWithProgramPoints, int ProgramPointCount,
        SymbolicCompactScopeProjection Projection, IReadOnlyList<SymbolicCompactLineResult> Lines,
        SymbolicAnalysisTruncationInfo AnalysisTruncation) CreateAggregate(
        SymbolicQueryResult result,
        SymbolicCompactQueryOptions options)
    {
        var sourceLines = result.Scope.Kind == SymbolicQueryScopeKind.File ? result.Lines : null;
        var lines = new List<SymbolicCompactLineResult>();
        var remainingProgramPoints = options.MaxProgramPoints;
        foreach (var line in sourceLines ?? Array.Empty<SymbolicQueryResult>())
        {
            if (lines.Count >= options.MaxLines) break;
            var pointLimit = remainingProgramPoints;
            lines.Add(SymbolicCompactLineResult.FromResult(line, options, pointLimit));
            if (remainingProgramPoints > 0) remainingProgramPoints -= Math.Min(line.ProgramPoints.Count, pointLimit);
        }

        var isFile = sourceLines != null;
        var projection = SymbolicCompactScopeProjection.Create(
            result.ObservedInvariant,
            result.ObservedInvariant.Conditions.Select(static condition => condition.Text).ToArray(),
            result.MergedInvariant,
            result.MergedPathFacts,
            result.InvariantQuery,
            result.Reachability,
            result.ProgramPointSummary,
            result.ConditionProofs,
            isFile ? Array.Empty<SymbolicProgramPointResult>() : result.ProgramPoints,
            result.SmtDiagnostics,
            options,
            isFile ? 0 : options.MaxProgramPoints);
        if (isFile)
        {
            var selectedProgramPointCount = lines.Sum(static line => line.ProgramPoints.Count);
            projection = projection with
            {
                Truncation = SymbolicCompactOutputTruncation.Combine(
                    new SymbolicCompactOutputTruncation(
                        sourceLines!.Count > lines.Count,
                        result.ProgramPointCount > selectedProgramPointCount,
                        false, false, false),
                    projection.Truncation,
                    SymbolicCompactOutputTruncation.Combine(lines.Select(static line => line.Truncation)))
            };
        }

        var lineCount = result.Scope.Kind == SymbolicQueryScopeKind.File ? result.LineCount : null;
        var linesWithProgramPoints = result.Scope.Kind switch
        {
            SymbolicQueryScopeKind.File => sourceLines!.Count,
            SymbolicQueryScopeKind.Span => result.ProgramPoints.Select(static point => point.Line).Distinct().Count(),
            _ => result.ProgramPointCount == 0 ? 0 : 1
        };
        return (lineCount, linesWithProgramPoints, result.ProgramPointCount, projection, lines,
            result.AnalysisTruncation);
    }
}

internal sealed record SymbolicInvariantQueryProjection(
    JsonElement Json,
    SymbolicCompactQueryProjection Compact,
    SymbolicInvariantQuerySummary QuerySummary,
    SymbolicInvariantQueryFocus Focus)
{
    internal string Kind => "invariantQuery";
    internal int SchemaVersion => 1;
    internal int EvidenceSchemaVersion => SharpProofEvidenceSchema.CurrentVersion;
    internal string EvidenceSchemaCompatibility => SharpProofEvidenceSchema.CompatibilityPolicy;
    internal bool HasTruncatedOutput => QuerySummary.HasTruncatedOutput;
    internal string ScopeKind => Compact.Scope.Kind;
    internal string FilePath => Compact.Scope.FilePath;
    internal SymbolicCompactSourceQueryDescriptor QueryDescriptor => Compact.QueryDescriptor;
    internal string MergedInvariantText => Compact.InvariantQuery.Text;
    internal SymbolicCompactInvariantQueryView InvariantQuery => Compact.InvariantQuery;
    internal SymbolicCompactAnalysisSummary AnalysisSummary => Compact.AnalysisSummary;
    internal SymbolicReachabilitySummary Reachability => Compact.Reachability;
    internal SymbolicProgramPointSummary ProgramPointSummary => Compact.ProgramPointSummary;
    internal SymbolicProofOutcomeSummary ProofOutcomes => Compact.ProofOutcomes;
    internal int ConditionProofCount => Compact.ConditionProofs.Count;
    internal IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs => Compact.ConditionProofs;
    internal bool ConditionProofsTruncated => Compact.Truncation.Proofs;
    internal SymbolicCompactSmtDiagnostics SmtDiagnostics => Compact.SmtDiagnostics;
    internal SymbolicAnalysisTruncationInfo AnalysisTruncation => Compact.AnalysisTruncation;
    internal int? LineCount => Compact.LineCount;
    internal int LinesWithProgramPoints => Compact.LinesWithProgramPoints;
    internal int ProgramPointCount => Compact.ProgramPointCount;

    internal static SymbolicInvariantQueryProjection Create(
        SymbolicQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        var normalizedOptions = NormalizeOptions(options);
        var compact = SymbolicCompactQueryProjection.Create(result, normalizedOptions);
        var querySummary = SymbolicInvariantQuerySummary.FromCompactResult(compact, normalizedOptions);
        var focus = SymbolicInvariantQueryFocus.FromProjection(compact);
        var json = JsonSerializer.SerializeToElement(new
        {
            Kind = "invariantQuery",
            SchemaVersion = 1,
            EvidenceSchemaVersion = SharpProofEvidenceSchema.CurrentVersion,
            EvidenceSchemaCompatibility = SharpProofEvidenceSchema.CompatibilityPolicy,
            ScopeKind = compact.Scope.Kind,
            FilePath = compact.Scope.FilePath,
            compact.QueryDescriptor,
            QuerySummary = querySummary,
            Focus = focus,
            MergedInvariantText = compact.InvariantQuery.Text,
            compact.InvariantQuery,
            compact.AnalysisSummary,
            compact.Reachability,
            compact.ProgramPointSummary,
            compact.ProofOutcomes,
            ConditionProofCount = compact.ConditionProofs.Count,
            compact.ConditionProofs,
            ConditionProofsTruncated = compact.Truncation.Proofs,
            compact.SmtDiagnostics,
            compact.AnalysisTruncation,
            compact.LineCount,
            compact.LinesWithProgramPoints,
            compact.ProgramPointCount
        }, SymbolicCliProjectionJson.Options);
        return new SymbolicInvariantQueryProjection(json, compact, querySummary, focus);
    }

    private static SymbolicCompactQueryOptions NormalizeOptions(SymbolicCompactQueryOptions? options)
    {
        var normalizedOptions = options ?? SymbolicCompactQueryOptions.Default;
        return new SymbolicCompactQueryOptions(
            0,
            0,
            normalizedOptions.MaxFacts,
            normalizedOptions.MaxConditions,
            normalizedOptions.MaxProofs,
            normalizedOptions.InvariantTargets);
    }
}

internal sealed record SymbolicInvariantQuerySummary(
    JsonElement Json,
    bool HasTruncatedOutput) : ISymbolicRawJsonProjection
{
    private const int MaxSummaryReasons = 16;
    private const int MaxSummaryTargets = 32;

    internal static SymbolicInvariantQuerySummary FromCompactResult(
        SymbolicCompactQueryProjection result,
        SymbolicCompactQueryOptions options)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        if (options == null) throw new ArgumentNullException(nameof(options));

        var targetLimit = Math.Min(options.MaxConditions, MaxSummaryTargets);
        var reasonLimit = Math.Min(options.MaxConditions, MaxSummaryReasons);
        var targets = GetTargets(result).ToArray();
        var targetCount = result.InvariantQuery.HasTargetFilter
            ? targets.Length
            : Math.Max(
                targets.Length,
                Math.Max(result.ConservativeInvariant.TargetCount, result.ObservedInvariant.TargetCount));
        var targetView = SymbolicCompactProjection.Take(targets, targetLimit);
        var targetTruncated =
            targetCount > targetView.Count ||
            (!result.InvariantQuery.HasTargetFilter &&
             (result.ConservativeInvariant.TargetsTruncated ||
              result.ObservedInvariant.TargetsTruncated));

        var reasons = GetReasons(result).ToArray();
        var reasonView = SymbolicCompactProjection.Take(reasons, reasonLimit);
        var truncation = result.Truncation;
        var analysisSummary = result.AnalysisSummary;
        var smtDiagnostics = result.SmtDiagnostics;
        var hasTruncatedOutput =
            truncation.Lines ||
            truncation.ProgramPoints ||
            truncation.Facts ||
            truncation.Conditions ||
            truncation.Proofs ||
            result.InvariantQuery.IsTruncated;

        var conditionsTruncated = truncation.Conditions || result.InvariantQuery.IsTruncated;
        var unresolved = analysisSummary.HasUnresolvedAnalysis || result.InvariantQuery.HasUnresolvedAnalysis;
        var pathBudgetExceeded = smtDiagnostics.MaxPathConditions > 0 &&
                                 analysisSummary.MaxPathConditionCount > smtDiagnostics.MaxPathConditions;
        return new SymbolicInvariantQuerySummary(
            SymbolicOrderedJson.Object(
                ("outputMaxFacts", options.MaxFacts),
                ("outputMaxConditions", options.MaxConditions),
                ("outputMaxProofs", options.MaxProofs),
                ("hasTruncatedOutput", hasTruncatedOutput),
                ("factsTruncated", truncation.Facts),
                ("conditionsTruncated", conditionsTruncated),
                ("proofsTruncated", truncation.Proofs),
                ("hasUnresolvedAnalysis", unresolved),
                ("programPointCount", result.ProgramPointCount),
                ("totalPathConditionCount", analysisSummary.TotalPathConditionCount),
                ("maxPathConditionCount", analysisSummary.MaxPathConditionCount),
                ("proofTotalCount", analysisSummary.ProofTotalCount),
                ("proofUnknownCount", analysisSummary.ProofUnknownCount),
                ("conservativeUnknownCount", analysisSummary.ConservativeUnknownCount),
                ("targetCount", targetCount),
                ("targets", targetView),
                ("targetsTruncated", targetTruncated),
                ("reasonCount", reasons.Length),
                ("reasons", reasonView),
                ("reasonsTruncated", reasons.Length > reasonView.Count),
                ("smtConfigured", smtDiagnostics.IsConfigured),
                ("smtEnabled", smtDiagnostics.IsEnabled),
                ("smtExecutedQueryCount", smtDiagnostics.ExecutedQueryCount),
                ("smtCacheEntryCount", smtDiagnostics.CacheEntryCount),
                ("smtQueryTimeoutMs", smtDiagnostics.QueryTimeoutMs),
                ("smtMethodBudgetMs", smtDiagnostics.MethodBudgetMs),
                ("smtMaxPathConditions", smtDiagnostics.MaxPathConditions),
                ("smtMaxExpressionNodes", smtDiagnostics.MaxExpressionNodes),
                ("pathConditionBudgetExceeded", pathBudgetExceeded)),
            hasTruncatedOutput);
    }

    private static IEnumerable<string> GetTargets(SymbolicCompactQueryProjection result)
    {
        var targets = result.ConservativeInvariant.Targets
            .Concat(result.ObservedInvariant.Targets)
            .Concat(result.ConditionProofs.Select(static proof => proof.Target))
            .Concat(result.InvariantQuery.TargetPathTargets)
            .Where(static target => IsSummaryTarget(target))
            .Where(static target => !string.IsNullOrWhiteSpace(target));

        if (result.InvariantQuery.HasTargetFilter)
            targets = targets.Where(target => result.InvariantQuery.TargetFilters.Contains(
                NormalizeTarget(target),
                StringComparer.Ordinal));

        return targets
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static target => target, StringComparer.Ordinal);
    }

    private static bool IsSummaryTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;

        var trimmed = target!.Trim();
        return SyntaxFacts.IsValidIdentifier(trimmed) ||
               trimmed.EndsWith(".Length", StringComparison.Ordinal);
    }

    private static string NormalizeTarget(string? target)
    {
        return string.IsNullOrWhiteSpace(target)
            ? "path"
            : target!.Trim();
    }

    private static IEnumerable<string> GetReasons(SymbolicCompactQueryProjection result)
    {
        var reasons = new List<string>();
        AddReason(reasons, result.InvariantQuery.StatusReason);

        foreach (var reason in result.InvariantQuery.ReasonDetails) AddReason(reasons, reason);

        if (result.AnalysisSummary.ReachabilityUnknownCount != 0)
            AddReason(reasons,
                "reachability_unknown=" +
                result.AnalysisSummary.ReachabilityUnknownCount.ToString(CultureInfo.InvariantCulture));

        if (result.AnalysisSummary.ReachabilityNotCheckedCount != 0)
            AddReason(reasons,
                "reachability_not_checked=" +
                result.AnalysisSummary.ReachabilityNotCheckedCount.ToString(CultureInfo.InvariantCulture));

        if (result.AnalysisSummary.ProofUnknownCount != 0)
            AddReason(reasons,
                "proof_unknown=" + result.AnalysisSummary.ProofUnknownCount.ToString(CultureInfo.InvariantCulture));

        if (!result.SmtDiagnostics.IsConfigured)
            AddReason(reasons, "smt_not_configured");
        else if (!result.SmtDiagnostics.IsEnabled) AddReason(reasons, "smt_disabled");

        if (result.Truncation.Facts) AddReason(reasons, "fact_output_truncated");

        if (result.Truncation.Conditions || result.InvariantQuery.IsTruncated)
            AddReason(reasons, "condition_output_truncated");

        if (result.Truncation.Proofs) AddReason(reasons, "proof_output_truncated");

        return reasons
            .Where(static reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static reason => reason, StringComparer.Ordinal);
    }

    private static void AddReason(List<string> reasons, string? reason)
    {
        if (!string.IsNullOrWhiteSpace(reason)) reasons.Add(reason!.Trim());
    }
}

internal static class SymbolicOrderedJson
{
    internal static JsonElement Object(params (string Name, object? Value)[] properties)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in properties)
            {
                if (value == null) continue;
                writer.WritePropertyName(name);
                JsonSerializer.Serialize(writer, value, value.GetType(), SymbolicCliProjectionJson.Options);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
