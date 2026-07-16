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

internal sealed class SymbolicCompactSourceQueryDescriptor(
    string kind,
    string filePath,
    int? line,
    int? column,
    int? position,
    int? spanStart,
    int? spanEnd,
    int? spanLength,
    int? startLine,
    int? startColumn,
    int? endLine,
    int? endColumn,
    string? nodeKind,
    string? methodName,
    string? programPointKind)
{
    public string Kind { get; } = kind ?? string.Empty;
    public string FilePath { get; } = filePath ?? string.Empty;
    public int? Line { get; } = line;
    public int? Column { get; } = column;
    public int? Position { get; } = position;
    public int? SpanStart { get; } = spanStart;
    public int? SpanEnd { get; } = spanEnd;
    public int? SpanLength { get; } = spanLength;
    public int? StartLine { get; } = startLine;
    public int? StartColumn { get; } = startColumn;
    public int? EndLine { get; } = endLine;
    public int? EndColumn { get; } = endColumn;
    public string? NodeKind { get; } = nodeKind;
    public string? MethodName { get; } = string.IsNullOrWhiteSpace(methodName) ? null : methodName;
    public string? ProgramPointKind { get; } = programPointKind;

    internal static SymbolicCompactSourceQueryDescriptor FromScope(SymbolicCompactQueryScope scope)
    {
        if (scope == null) throw new ArgumentNullException(nameof(scope));

        var isSpan = string.Equals(scope.Kind, "span", StringComparison.Ordinal);

        return new SymbolicCompactSourceQueryDescriptor(
            scope.Kind,
            scope.FilePath,
            scope.Line,
            scope.Column,
            scope.Position,
            isSpan ? scope.NodeSpanStart : null,
            isSpan ? scope.NodeSpanEnd : null,
            isSpan ? scope.NodeSpanLength : null,
            isSpan ? scope.NodeStartLine : null,
            isSpan ? scope.NodeStartColumn : null,
            isSpan ? scope.NodeEndLine : null,
            isSpan ? scope.NodeEndColumn : null,
            scope.NodeKind,
            scope.MethodName,
            scope.ProgramPointKind);
    }
}

internal sealed class SymbolicInvariantQueryFocus(
    SymbolicCompactQueryProjection result,
    string reachabilityStatus,
    string reachabilityReason,
    int reachabilityKnownCount)
{
    public string ScopeKind => result.Kind;
    public string FilePath => result.FilePath;

    public bool HasSourceLocation =>
        Line.HasValue ||
        Position.HasValue ||
        SpanStart.HasValue ||
        StartLine.HasValue;

    public int? Line => result.Line;

    public int? Column => result.Column;

    public int? Position => result.Position;

    public int? RequestedLine => result.RequestedLine;

    public int? RequestedColumn => result.RequestedColumn;

    public int? RequestedPosition => result.RequestedPosition;

    public int? RequestedPositionDistance => result.RequestedPositionDistance;

    public bool? ContainsRequestedPosition => result.ContainsRequestedPosition;

    public int? SpanStart => result.QuerySpanStart;

    public int? SpanEnd => result.QuerySpanEnd;

    public int? SpanLength => result.QuerySpanLength;

    public int? StartLine => result.QueryStartLine;

    public int? StartColumn => result.QueryStartColumn;

    public int? EndLine => result.QueryEndLine;

    public int? EndColumn => result.QueryEndColumn;

    public string? NodeKind => result.NodeKind;

    public string? MethodName => result.MethodName;

    public string? ProgramPointKind => result.ProgramPointKind;

    public string ReachabilityStatus { get; } = reachabilityStatus ?? string.Empty;

    public string ReachabilityReason { get; } = reachabilityReason ?? string.Empty;

    public int ProgramPointCount => result.ProgramPointCount;

    public int ReachabilityKnownCount { get; } = reachabilityKnownCount;

    public bool HasKnownReachability => ReachabilityKnownCount != 0;

    internal static SymbolicInvariantQueryFocus FromProjection(SymbolicCompactQueryProjection result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        var reachabilityStatus = ResolveReachabilityStatus(result);
        return new SymbolicInvariantQueryFocus(
            result,
            reachabilityStatus,
            ResolveReachabilityReason(result, reachabilityStatus),
            result.Reachability.ReachableCount + result.Reachability.UnreachableCount);
    }

    private static string ResolveReachabilityStatus(SymbolicCompactQueryProjection result)
    {
        if (!string.IsNullOrWhiteSpace(result.PointReachability)) return result.PointReachability!;

        if (result.ProgramPointCount == 0) return "NoProgramPoints";

        var reachability = result.Reachability;
        if (reachability.ReachableCount == result.ProgramPointCount) return SymbolicReachability.Reachable.ToString();

        if (reachability.UnreachableCount == result.ProgramPointCount)
            return SymbolicReachability.Unreachable.ToString();

        if (reachability.UnknownCount == result.ProgramPointCount) return SymbolicReachability.Unknown.ToString();

        if (reachability.NotCheckedCount == result.ProgramPointCount) return SymbolicReachability.NotChecked.ToString();

        return "Mixed";
    }

    private static string ResolveReachabilityReason(
        SymbolicCompactQueryProjection result,
        string reachabilityStatus)
    {
        if (!string.IsNullOrWhiteSpace(result.ReachabilityReason)) return result.ReachabilityReason!;

        if (result.ProgramPointCount == 0) return "no_program_points";

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
    internal string Kind => Scope.Kind;
    internal string FilePath => Scope.FilePath;
    internal int? Line => Scope.Line;
    internal int? Column => Scope.Column;
    internal int? Position => Scope.Position;
    internal int? RequestedLine => Scope.RequestedLine;
    internal int? RequestedColumn => Scope.RequestedColumn;
    internal int? RequestedPosition => Scope.RequestedPosition;
    internal int? RequestedPositionDistance => Scope.RequestedPositionDistance;
    internal bool? ContainsRequestedPosition => Scope.ContainsRequestedPosition;
    internal string? NodeKind => Scope.NodeKind;
    internal string? MethodName => Scope.MethodName;
    internal string? ProgramPointKind => Scope.ProgramPointKind;
    internal int? NodeSpanStart => Scope.NodeSpanStart;
    internal int? NodeSpanEnd => Scope.NodeSpanEnd;
    internal int? NodeSpanLength => Scope.NodeSpanLength;
    internal int? NodeStartLine => Scope.NodeStartLine;
    internal int? NodeStartColumn => Scope.NodeStartColumn;
    internal int? NodeEndLine => Scope.NodeEndLine;
    internal int? NodeEndColumn => Scope.NodeEndColumn;
    internal int? QuerySpanStart => IsSpan ? Scope.NodeSpanStart : null;
    internal int? QuerySpanEnd => IsSpan ? Scope.NodeSpanEnd : null;
    internal int? QuerySpanLength => IsSpan ? Scope.NodeSpanLength : null;
    internal int? QueryStartLine => IsSpan ? Scope.NodeStartLine : null;
    internal int? QueryStartColumn => IsSpan ? Scope.NodeStartColumn : null;
    internal int? QueryEndLine => IsSpan ? Scope.NodeEndLine : null;
    internal int? QueryEndColumn => IsSpan ? Scope.NodeEndColumn : null;
    internal string? PointReachability => Scope.PointReachability;
    internal string? ReachabilityReason => Scope.ReachabilityReason;
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

    private bool IsSpan => string.Equals(Kind, "span", StringComparison.Ordinal);

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
    internal string ScopeKind => Compact.Kind;
    internal string FilePath => Compact.FilePath;
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
            ScopeKind = compact.Kind,
            compact.FilePath,
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

internal sealed class SymbolicInvariantQuerySummary(
    int outputMaxFacts,
    int outputMaxConditions,
    int outputMaxProofs,
    bool hasTruncatedOutput,
    bool factsTruncated,
    bool conditionsTruncated,
    bool proofsTruncated,
    bool hasUnresolvedAnalysis,
    int programPointCount,
    int totalPathConditionCount,
    int maxPathConditionCount,
    int proofTotalCount,
    int proofUnknownCount,
    int conservativeUnknownCount,
    int targetCount,
    IReadOnlyList<string> targets,
    bool targetsTruncated,
    int reasonCount,
    IReadOnlyList<string> reasons,
    bool reasonsTruncated,
    SymbolicCompactSmtDiagnostics smtDiagnostics,
    bool pathConditionBudgetExceeded)
    : SymbolicSmtDiagnosticsProjectionBase(smtDiagnostics)
{
    private const int MaxSummaryReasons = 16;
    private const int MaxSummaryTargets = 32;

    public int OutputMaxFacts { get; } = outputMaxFacts;
    public int OutputMaxConditions { get; } = outputMaxConditions;
    public int OutputMaxProofs { get; } = outputMaxProofs;
    public bool HasTruncatedOutput { get; } = hasTruncatedOutput;
    public bool FactsTruncated { get; } = factsTruncated;
    public bool ConditionsTruncated { get; } = conditionsTruncated;
    public bool ProofsTruncated { get; } = proofsTruncated;
    public bool HasUnresolvedAnalysis { get; } = hasUnresolvedAnalysis;
    public int ProgramPointCount { get; } = programPointCount;
    public int TotalPathConditionCount { get; } = totalPathConditionCount;
    public int MaxPathConditionCount { get; } = maxPathConditionCount;
    public int ProofTotalCount { get; } = proofTotalCount;
    public int ProofUnknownCount { get; } = proofUnknownCount;
    public int ConservativeUnknownCount { get; } = conservativeUnknownCount;
    public int TargetCount { get; } = targetCount;
    public IReadOnlyList<string> Targets { get; } = targets;
    public bool TargetsTruncated { get; } = targetsTruncated;
    public int ReasonCount { get; } = reasonCount;
    public IReadOnlyList<string> Reasons { get; } = reasons;
    public bool ReasonsTruncated { get; } = reasonsTruncated;
    [JsonPropertyOrder(108)] public bool PathConditionBudgetExceeded { get; } = pathConditionBudgetExceeded;

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

        return new SymbolicInvariantQuerySummary(
            options.MaxFacts,
            options.MaxConditions,
            options.MaxProofs,
            hasTruncatedOutput,
            truncation.Facts,
            truncation.Conditions || result.InvariantQuery.IsTruncated,
            truncation.Proofs,
            analysisSummary.HasUnresolvedAnalysis || result.InvariantQuery.HasUnresolvedAnalysis,
            result.ProgramPointCount,
            analysisSummary.TotalPathConditionCount,
            analysisSummary.MaxPathConditionCount,
            analysisSummary.ProofTotalCount,
            analysisSummary.ProofUnknownCount,
            analysisSummary.ConservativeUnknownCount,
            targetCount,
            targetView,
            targetTruncated,
            reasons.Length,
            reasonView,
            reasons.Length > reasonView.Count,
            smtDiagnostics,
            smtDiagnostics.MaxPathConditions > 0 &&
            analysisSummary.MaxPathConditionCount > smtDiagnostics.MaxPathConditions);
    }

    private static IEnumerable<string> GetTargets(SymbolicCompactQueryProjection result)
    {
        var targets = result.ConservativeInvariant.Targets
            .Concat(result.ObservedInvariant.Targets)
            .Concat(result.ConditionProofs.Select(static proof => proof.Target))
            .Concat(result.InvariantQuery.TargetPathSummaries.Select(static summary => summary.Target))
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

        foreach (var diagnostic in result.InvariantQuery.Diagnostics)
            AddReason(reasons, diagnostic.Code + ": " + diagnostic.Message);

        foreach (var diagnostic in result.InvariantQuery.UnknownDiagnostics)
            AddReason(reasons, diagnostic.UnknownText + ": " + diagnostic.Reason);

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
