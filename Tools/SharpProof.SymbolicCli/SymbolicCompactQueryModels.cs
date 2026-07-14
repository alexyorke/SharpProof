using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
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

public sealed class SymbolicCompactSourceQueryDescriptor
{
    private SymbolicCompactSourceQueryDescriptor(
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
        Kind = kind ?? string.Empty;
        FilePath = filePath ?? string.Empty;
        Line = line;
        Column = column;
        Position = position;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        SpanLength = spanLength;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        NodeKind = nodeKind;
        MethodName = string.IsNullOrWhiteSpace(methodName) ? null : methodName;
        ProgramPointKind = programPointKind;
    }

    public string Kind { get; }

    public string FilePath { get; }

    public int? Line { get; }

    public int? Column { get; }

    public int? Position { get; }

    public int? SpanStart { get; }

    public int? SpanEnd { get; }

    public int? SpanLength { get; }

    public int? StartLine { get; }

    public int? StartColumn { get; }

    public int? EndLine { get; }

    public int? EndColumn { get; }

    public string? NodeKind { get; }

    public string? MethodName { get; }

    public string? ProgramPointKind { get; }

    internal static SymbolicCompactSourceQueryDescriptor FromCompactResult(SymbolicCompactQueryResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        return new SymbolicCompactSourceQueryDescriptor(
            result.Kind,
            result.FilePath,
            result.Line,
            result.Column,
            result.Position,
            result.QuerySpanStart,
            result.QuerySpanEnd,
            result.QuerySpanLength,
            result.QueryStartLine,
            result.QueryStartColumn,
            result.QueryEndLine,
            result.QueryEndColumn,
            result.NodeKind,
            result.MethodName,
            result.ProgramPointKind);
    }
}

public sealed class SymbolicInvariantQueryFocus
{
    private readonly SymbolicCompactQueryResult _result;

    private SymbolicInvariantQueryFocus(
        SymbolicCompactQueryResult result,
        string reachabilityStatus,
        string reachabilityReason,
        int reachabilityKnownCount)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        ReachabilityStatus = reachabilityStatus ?? string.Empty;
        ReachabilityReason = reachabilityReason ?? string.Empty;
        ReachabilityKnownCount = reachabilityKnownCount;
    }

    public string ScopeKind => _result.Kind;

    public string FilePath => _result.FilePath;

    public bool HasSourceLocation =>
        Line.HasValue ||
        Position.HasValue ||
        SpanStart.HasValue ||
        StartLine.HasValue;

    public int? Line => _result.Line;

    public int? Column => _result.Column;

    public int? Position => _result.Position;

    public int? RequestedLine => _result.RequestedLine;

    public int? RequestedColumn => _result.RequestedColumn;

    public int? RequestedPosition => _result.RequestedPosition;

    public int? RequestedPositionDistance => _result.RequestedPositionDistance;

    public bool? ContainsRequestedPosition => _result.ContainsRequestedPosition;

    public int? SpanStart => _result.QuerySpanStart;

    public int? SpanEnd => _result.QuerySpanEnd;

    public int? SpanLength => _result.QuerySpanLength;

    public int? StartLine => _result.QueryStartLine;

    public int? StartColumn => _result.QueryStartColumn;

    public int? EndLine => _result.QueryEndLine;

    public int? EndColumn => _result.QueryEndColumn;

    public string? NodeKind => _result.NodeKind;

    public string? MethodName => _result.MethodName;

    public string? ProgramPointKind => _result.ProgramPointKind;

    public string ReachabilityStatus { get; }

    public string ReachabilityReason { get; }

    public int ProgramPointCount => _result.ProgramPointCount;

    public int ReachabilityKnownCount { get; }

    public bool HasKnownReachability => ReachabilityKnownCount != 0;

    internal static SymbolicInvariantQueryFocus FromCompactResult(SymbolicCompactQueryResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        var reachabilityStatus = ResolveReachabilityStatus(result);
        return new SymbolicInvariantQueryFocus(
            result,
            reachabilityStatus,
            ResolveReachabilityReason(result, reachabilityStatus),
            result.Reachability.ReachableCount + result.Reachability.UnreachableCount);
    }

    private static string ResolveReachabilityStatus(SymbolicCompactQueryResult result)
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
        SymbolicCompactQueryResult result,
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
    internal static SymbolicCompactQueryScope FromPoint(SymbolicProgramPointResult result)
    {
        return new SymbolicCompactQueryScope(
            "point",
            result.FilePath,
            Line: result.Line,
            Column: result.Column,
            Position: result.Position,
            NodeKind: result.NodeKind,
            MethodName: result.MethodName,
            ProgramPointKind: result.ProgramPointKind,
            NodeSpanStart: result.NodeSpanStart,
            NodeSpanEnd: result.NodeSpanEnd,
            NodeSpanLength: result.NodeSpanLength,
            NodeStartLine: result.NodeStartLine,
            NodeStartColumn: result.NodeStartColumn,
            NodeEndLine: result.NodeEndLine,
            NodeEndColumn: result.NodeEndColumn,
            PointReachability: result.Reachability.ToString(),
            ReachabilityReason: result.ReachabilityReason,
            RequestedLine: result.RequestedLine,
            RequestedColumn: result.RequestedColumn,
            RequestedPosition: result.RequestedPosition,
            RequestedPositionDistance: result.RequestedPositionDistance,
            ContainsRequestedPosition: result.ContainsRequestedPosition);
    }

    internal static SymbolicCompactQueryScope FromLine(SymbolicLineQueryResult result)
    {
        return new SymbolicCompactQueryScope("line", result.FilePath, Line: result.Line);
    }

    internal static SymbolicCompactQueryScope FromSpan(SymbolicSpanQueryResult result)
    {
        return new SymbolicCompactQueryScope(
            "span",
            result.FilePath,
            NodeSpanStart: result.SpanStart,
            NodeSpanEnd: result.SpanEnd,
            NodeSpanLength: result.SpanLength,
            NodeStartLine: result.StartLine,
            NodeStartColumn: result.StartColumn,
            NodeEndLine: result.EndLine,
            NodeEndColumn: result.EndColumn);
    }

    internal static SymbolicCompactQueryScope FromFile(SymbolicFileQueryResult result)
    {
        return new SymbolicCompactQueryScope("file", result.FilePath);
    }
}

public sealed class SymbolicCompactQueryResult : ISymbolicCompactResult
{
    private readonly SymbolicCompactScopeProjection _projection;
    private readonly SymbolicCompactQueryScope _scope;

    private SymbolicCompactQueryResult(
        SymbolicCompactQueryScope scope,
        int? lineCount,
        int linesWithProgramPoints,
        int programPointCount,
        SymbolicCompactScopeProjection projection,
        IReadOnlyList<SymbolicCompactLineResult> lines,
        SymbolicAnalysisTruncationInfo analysisTruncation)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        LineCount = lineCount;
        LinesWithProgramPoints = linesWithProgramPoints;
        ProgramPointCount = programPointCount;
        Lines = lines ?? throw new ArgumentNullException(nameof(lines));
        AnalysisTruncation = analysisTruncation ?? throw new ArgumentNullException(nameof(analysisTruncation));
        AnalysisSummary = SymbolicCompactAnalysisSummary.From(
            InvariantQuery,
            ProgramPointSummary,
            SmtDiagnostics,
            AnalysisTruncation);
        QueryDescriptor = SymbolicCompactSourceQueryDescriptor.FromCompactResult(this);
    }

    public string Kind => _scope.Kind;

    public int SchemaVersion => 1;

    public int EvidenceSchemaVersion => SharpProofEvidenceSchema.CurrentVersion;

    public string EvidenceSchemaCompatibility => SharpProofEvidenceSchema.CompatibilityPolicy;

    public string FilePath => _scope.FilePath;

    public SymbolicCompactSourceQueryDescriptor QueryDescriptor { get; }

    public int? Line => _scope.Line;

    public int? Column => _scope.Column;

    public int? Position => _scope.Position;

    public int? RequestedLine => _scope.RequestedLine;

    public int? RequestedColumn => _scope.RequestedColumn;

    public int? RequestedPosition => _scope.RequestedPosition;

    public int? RequestedPositionDistance => _scope.RequestedPositionDistance;

    public bool? ContainsRequestedPosition => _scope.ContainsRequestedPosition;

    public string? NodeKind => _scope.NodeKind;

    public string? MethodName => _scope.MethodName;

    public string? ProgramPointKind => _scope.ProgramPointKind;

    public int? NodeSpanStart => _scope.NodeSpanStart;

    public int? NodeSpanEnd => _scope.NodeSpanEnd;

    public int? NodeSpanLength => _scope.NodeSpanLength;

    public int? NodeStartLine => _scope.NodeStartLine;

    public int? NodeStartColumn => _scope.NodeStartColumn;

    public int? NodeEndLine => _scope.NodeEndLine;

    public int? NodeEndColumn => _scope.NodeEndColumn;

    public int? QuerySpanStart => string.Equals(Kind, "span", StringComparison.Ordinal) ? NodeSpanStart : null;

    public int? QuerySpanEnd => string.Equals(Kind, "span", StringComparison.Ordinal) ? NodeSpanEnd : null;

    public int? QuerySpanLength => string.Equals(Kind, "span", StringComparison.Ordinal) ? NodeSpanLength : null;

    public int? QueryStartLine => string.Equals(Kind, "span", StringComparison.Ordinal) ? NodeStartLine : null;

    public int? QueryStartColumn => string.Equals(Kind, "span", StringComparison.Ordinal) ? NodeStartColumn : null;

    public int? QueryEndLine => string.Equals(Kind, "span", StringComparison.Ordinal) ? NodeEndLine : null;

    public int? QueryEndColumn => string.Equals(Kind, "span", StringComparison.Ordinal) ? NodeEndColumn : null;

    public string? PointReachability => _scope.PointReachability;

    public string? ReachabilityReason => _scope.ReachabilityReason;

    public int? LineCount { get; }

    public int LinesWithProgramPoints { get; }

    public int ProgramPointCount { get; }

    public SymbolicCompactInvariantSummary ObservedInvariant => _projection.ObservedInvariant;

    public SymbolicCompactInvariantSummary ConservativeInvariant => _projection.ConservativeInvariant;

    public SymbolicCompactInvariantQueryView InvariantQuery => _projection.InvariantQuery;

    public string MergedInvariantText => ConservativeInvariant.Text;

    public SymbolicReachabilitySummary Reachability => _projection.Reachability;

    public SymbolicProgramPointSummary ProgramPointSummary => _projection.ProgramPointSummary;

    public SymbolicProofOutcomeSummary ProofOutcomes => ProgramPointSummary.ProofOutcomes;

    public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs => _projection.ConditionProofs;

    public IReadOnlyList<SymbolicCompactLineResult> Lines { get; }

    public IReadOnlyList<SymbolicCompactProgramPointResult> ProgramPoints => _projection.ProgramPoints;

    public SymbolicCompactSmtDiagnostics SmtDiagnostics => _projection.SmtDiagnostics;

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; }

    public SymbolicCompactAnalysisSummary AnalysisSummary { get; }

    public SymbolicCompactOutputTruncation Truncation => _projection.Truncation;

    public static SymbolicCompactQueryResult FromPoint(
        SymbolicProgramPointResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        var normalizedOptions = options ?? SymbolicCompactQueryOptions.Default;
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
            normalizedOptions,
            normalizedOptions.MaxProgramPoints);

        return new SymbolicCompactQueryResult(
            SymbolicCompactQueryScope.FromPoint(result),
            null,
            1,
            1,
            projection,
            Array.Empty<SymbolicCompactLineResult>(),
            result.AnalysisTruncation);
    }

    internal static SymbolicCompactQueryResult FromLine(
        SymbolicLineQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        var normalizedOptions = options ?? SymbolicCompactQueryOptions.Default;
        var lineResult = SymbolicCompactLineResult.FromResult(
            result,
            normalizedOptions,
            normalizedOptions.MaxProgramPoints);

        return new SymbolicCompactQueryResult(
            SymbolicCompactQueryScope.FromLine(result),
            null,
            result.ProgramPoints.Count == 0 ? 0 : 1,
            result.ProgramPoints.Count,
            lineResult.Projection,
            Array.Empty<SymbolicCompactLineResult>(),
            result.AnalysisTruncation);
    }

    internal static SymbolicCompactQueryResult FromSpan(
        SymbolicSpanQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        var normalizedOptions = options ?? SymbolicCompactQueryOptions.Default;
        var projection = SymbolicCompactScopeProjection.Create(
            result.ObservedInvariant,
            result.Facts,
            result.MergedInvariant,
            result.MergedPathFacts,
            result.InvariantQuery,
            result.Reachability,
            result.ProgramPointSummary,
            result.ConditionProofs,
            result.ProgramPoints,
            result.SmtDiagnostics,
            normalizedOptions,
            normalizedOptions.MaxProgramPoints);

        return new SymbolicCompactQueryResult(
            SymbolicCompactQueryScope.FromSpan(result),
            null,
            result.LinesWithProgramPoints,
            result.ProgramPointCount,
            projection,
            Array.Empty<SymbolicCompactLineResult>(),
            result.AnalysisTruncation);
    }

    internal static SymbolicCompactQueryResult FromFile(
        SymbolicFileQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        var normalizedOptions = options ?? SymbolicCompactQueryOptions.Default;
        var lineResults = new List<SymbolicCompactLineResult>();
        var remainingProgramPoints = normalizedOptions.MaxProgramPoints;
        foreach (var line in result.Lines)
        {
            if (lineResults.Count >= normalizedOptions.MaxLines) break;

            var pointLimit = remainingProgramPoints;
            lineResults.Add(SymbolicCompactLineResult.FromResult(line, normalizedOptions, pointLimit));
            if (remainingProgramPoints > 0) remainingProgramPoints -= Math.Min(line.ProgramPoints.Count, pointLimit);
        }

        var projection = SymbolicCompactScopeProjection.Create(
            result.ObservedInvariant,
            result.ObservedFacts,
            result.MergedInvariant,
            result.MergedPathFacts,
            result.InvariantQuery,
            result.Reachability,
            result.ProgramPointSummary,
            result.ConditionProofs,
            Array.Empty<SymbolicProgramPointResult>(),
            result.SmtDiagnostics,
            normalizedOptions,
            0);
        var selectedProgramPointCount = lineResults.Sum(static line => line.ProgramPoints.Count);
        var truncation = SymbolicCompactOutputTruncation.Combine(
            new SymbolicCompactOutputTruncation(
                result.Lines.Count > lineResults.Count,
                result.ProgramPointCount > selectedProgramPointCount,
                false,
                false,
                false),
            projection.Truncation,
            SymbolicCompactOutputTruncation.Combine(lineResults.Select(static line => line.Truncation)));

        return new SymbolicCompactQueryResult(
            SymbolicCompactQueryScope.FromFile(result),
            result.LineCount,
            result.LinesWithProgramPoints,
            result.ProgramPointCount,
            projection with { Truncation = truncation },
            lineResults,
            result.AnalysisTruncation);
    }
}

public sealed class SymbolicInvariantQueryResult : ISymbolicCompactResult
{
    private readonly SymbolicCompactQueryResult _result;

    private SymbolicInvariantQueryResult(
        SymbolicCompactQueryResult result,
        SymbolicInvariantQuerySummary querySummary,
        SymbolicInvariantQueryFocus focus)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        QuerySummary = querySummary ?? throw new ArgumentNullException(nameof(querySummary));
        Focus = focus ?? throw new ArgumentNullException(nameof(focus));
    }

    public string Kind => "invariantQuery";

    public int SchemaVersion => 1;

    public int EvidenceSchemaVersion => SharpProofEvidenceSchema.CurrentVersion;

    public string EvidenceSchemaCompatibility => SharpProofEvidenceSchema.CompatibilityPolicy;

    public string ScopeKind => _result.Kind;

    public string FilePath => _result.FilePath;

    public SymbolicCompactSourceQueryDescriptor QueryDescriptor => _result.QueryDescriptor;

    public SymbolicInvariantQuerySummary QuerySummary { get; }

    public SymbolicInvariantQueryFocus Focus { get; }

    public string MergedInvariantText => _result.InvariantQuery.Text;

    public SymbolicCompactInvariantQueryView InvariantQuery => _result.InvariantQuery;

    public SymbolicCompactAnalysisSummary AnalysisSummary => _result.AnalysisSummary;

    public SymbolicReachabilitySummary Reachability => _result.Reachability;

    public SymbolicProgramPointSummary ProgramPointSummary => _result.ProgramPointSummary;

    public SymbolicProofOutcomeSummary ProofOutcomes => _result.ProofOutcomes;

    public int ConditionProofCount => ConditionProofs.Count;

    public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs => _result.ConditionProofs;

    public bool ConditionProofsTruncated => _result.Truncation.Proofs;

    public SymbolicCompactSmtDiagnostics SmtDiagnostics => _result.SmtDiagnostics;

    public SymbolicAnalysisTruncationInfo AnalysisTruncation => _result.AnalysisTruncation;

    public int? LineCount => _result.LineCount;

    public int LinesWithProgramPoints => _result.LinesWithProgramPoints;

    public int ProgramPointCount => _result.ProgramPointCount;

    public static SymbolicInvariantQueryResult FromPoint(
        SymbolicProgramPointResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        var normalizedOptions = NormalizeOptions(options);
        return FromCompactResult(SymbolicCompactQueryResult.FromPoint(result, normalizedOptions), normalizedOptions);
    }

    internal static SymbolicInvariantQueryResult FromLine(
        SymbolicLineQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        var normalizedOptions = NormalizeOptions(options);
        return FromCompactResult(SymbolicCompactQueryResult.FromLine(result, normalizedOptions), normalizedOptions);
    }

    internal static SymbolicInvariantQueryResult FromSpan(
        SymbolicSpanQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        var normalizedOptions = NormalizeOptions(options);
        return FromCompactResult(SymbolicCompactQueryResult.FromSpan(result, normalizedOptions), normalizedOptions);
    }

    internal static SymbolicInvariantQueryResult FromFile(
        SymbolicFileQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        var normalizedOptions = NormalizeOptions(options);
        return FromCompactResult(SymbolicCompactQueryResult.FromFile(result, normalizedOptions), normalizedOptions);
    }

    private static SymbolicInvariantQueryResult FromCompactResult(
        SymbolicCompactQueryResult result,
        SymbolicCompactQueryOptions options)
    {
        return new SymbolicInvariantQueryResult(
            result,
            SymbolicInvariantQuerySummary.FromCompactResult(result, options),
            SymbolicInvariantQueryFocus.FromCompactResult(result));
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

public sealed class SymbolicInvariantQuerySummary
{
    private const int MaxSummaryReasons = 16;
    private const int MaxSummaryTargets = 32;
    private readonly SymbolicCompactSmtDiagnostics _smtDiagnostics;

    private SymbolicInvariantQuerySummary(
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
    {
        OutputMaxFacts = outputMaxFacts;
        OutputMaxConditions = outputMaxConditions;
        OutputMaxProofs = outputMaxProofs;
        HasTruncatedOutput = hasTruncatedOutput;
        FactsTruncated = factsTruncated;
        ConditionsTruncated = conditionsTruncated;
        ProofsTruncated = proofsTruncated;
        HasUnresolvedAnalysis = hasUnresolvedAnalysis;
        ProgramPointCount = programPointCount;
        TotalPathConditionCount = totalPathConditionCount;
        MaxPathConditionCount = maxPathConditionCount;
        ProofTotalCount = proofTotalCount;
        ProofUnknownCount = proofUnknownCount;
        ConservativeUnknownCount = conservativeUnknownCount;
        TargetCount = targetCount;
        Targets = targets ?? throw new ArgumentNullException(nameof(targets));
        TargetsTruncated = targetsTruncated;
        ReasonCount = reasonCount;
        Reasons = reasons ?? throw new ArgumentNullException(nameof(reasons));
        ReasonsTruncated = reasonsTruncated;
        _smtDiagnostics = smtDiagnostics ?? throw new ArgumentNullException(nameof(smtDiagnostics));
        PathConditionBudgetExceeded = pathConditionBudgetExceeded;
    }

    public int OutputMaxFacts { get; }

    public int OutputMaxConditions { get; }

    public int OutputMaxProofs { get; }

    public bool HasTruncatedOutput { get; }

    public bool FactsTruncated { get; }

    public bool ConditionsTruncated { get; }

    public bool ProofsTruncated { get; }

    public bool HasUnresolvedAnalysis { get; }

    public int ProgramPointCount { get; }

    public int TotalPathConditionCount { get; }

    public int MaxPathConditionCount { get; }

    public int ProofTotalCount { get; }

    public int ProofUnknownCount { get; }

    public int ConservativeUnknownCount { get; }

    public int TargetCount { get; }

    public IReadOnlyList<string> Targets { get; }

    public bool TargetsTruncated { get; }

    public int ReasonCount { get; }

    public IReadOnlyList<string> Reasons { get; }

    public bool ReasonsTruncated { get; }

    public bool SmtConfigured => _smtDiagnostics.IsConfigured;

    public bool SmtEnabled => _smtDiagnostics.IsEnabled;

    public int SmtExecutedQueryCount => _smtDiagnostics.ExecutedQueryCount;

    public int SmtCacheEntryCount => _smtDiagnostics.CacheEntryCount;

    public int SmtQueryTimeoutMs => _smtDiagnostics.QueryTimeoutMs;

    public int SmtMethodBudgetMs => _smtDiagnostics.MethodBudgetMs;

    public int SmtMaxPathConditions => _smtDiagnostics.MaxPathConditions;

    public int SmtMaxExpressionNodes => _smtDiagnostics.MaxExpressionNodes;

    public bool PathConditionBudgetExceeded { get; }

    internal static SymbolicInvariantQuerySummary FromCompactResult(
        SymbolicCompactQueryResult result,
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

    private static IEnumerable<string> GetTargets(SymbolicCompactQueryResult result)
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

    private static IEnumerable<string> GetReasons(SymbolicCompactQueryResult result)
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
