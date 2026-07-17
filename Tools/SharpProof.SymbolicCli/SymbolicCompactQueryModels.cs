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
    SymbolicCompactInvariantSummary ObservedInvariant,
    SymbolicCompactInvariantSummary ConservativeInvariant,
    SymbolicCompactInvariantQueryView InvariantQuery,
    SymbolicReachabilitySummary Reachability,
    SymbolicProgramPointSummary ProgramPointSummary,
    IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs,
    IReadOnlyList<SymbolicCompactProgramPointResult> ProgramPoints,
    SymbolicCompactSmtDiagnostics SmtDiagnostics,
    SymbolicCompactOutputTruncation Truncation,
    IReadOnlyList<SymbolicCompactLineResult> Lines,
    SymbolicAnalysisTruncationInfo AnalysisTruncation,
    SymbolicCompactAnalysisSummary AnalysisSummary,
    SymbolicCompactSourceQueryDescriptor QueryDescriptor,
    JsonElement Json)
{
    internal int SchemaVersion => 1;
    internal int EvidenceSchemaVersion => SharpProofEvidenceSchema.CurrentVersion;
    internal string EvidenceSchemaCompatibility => SharpProofEvidenceSchema.CompatibilityPolicy;
    internal string MergedInvariantText => ConservativeInvariant.Text;
    internal SymbolicProofOutcomeSummary ProofOutcomes => ProgramPointSummary.ProofOutcomes;

    internal static SymbolicCompactQueryProjection Create(
        SymbolicQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        var normalizedOptions = options ?? SymbolicCompactQueryOptions.Default;
        var scope = SymbolicCompactQueryScope.FromResult(result);
        var point = result.Scope.Kind == SymbolicQueryScopeKind.Point
            ? result.ProgramPoints.Single()
            : null;
        var sourceLines = result.Scope.Kind == SymbolicQueryScopeKind.File ? result.Lines : null;
        var lines = new List<SymbolicCompactLineResult>();
        var remainingProgramPoints = normalizedOptions.MaxProgramPoints;
        foreach (var line in sourceLines ?? Array.Empty<SymbolicQueryResult>())
        {
            if (lines.Count >= normalizedOptions.MaxLines) break;
            var pointLimit = remainingProgramPoints;
            lines.Add(CreateLineResult(line, normalizedOptions, pointLimit));
            if (remainingProgramPoints > 0)
                remainingProgramPoints -= Math.Min(line.ProgramPoints.Count, pointLimit);
        }

        var isFile = sourceLines != null;
        IReadOnlyList<SymbolicProgramPointResult> sourceProgramPoints = point != null
            ? new[] { point }
            : isFile ? Array.Empty<SymbolicProgramPointResult>() : result.ProgramPoints;
        var observedInvariant = point != null
            ? SymbolicInvariantResult.FromFacts(point.Facts)
            : result.ObservedInvariant;
        var observedFacts = point != null
            ? point.Facts
            : result.ObservedInvariant.Conditions.Select(static condition => condition.Text).ToArray();
        var conservativeInvariant = point?.Invariant ?? result.MergedInvariant;
        var mergedPathFacts = point == null ? result.MergedPathFacts : null;
        var sourceInvariantQuery = point?.InvariantQuery ?? result.InvariantQuery;
        var reachability = point == null
            ? result.Reachability
            : SymbolicReachabilitySummary.FromProgramPoints(sourceProgramPoints);
        var programPointSummary = point == null
            ? result.ProgramPointSummary
            : SymbolicProgramPointSummary.FromProgramPoints(sourceProgramPoints);
        IReadOnlyList<SymbolicConditionProofSummary> conditionProofSummaries = point == null
            ? result.ConditionProofs
            : SymbolicConditionProofSummary.FromProgramPoints(sourceProgramPoints);
        var smtDiagnostics = point?.SmtDiagnostics ?? result.SmtDiagnostics;
        var maxProgramPoints = isFile ? 0 : normalizedOptions.MaxProgramPoints;
        var (compactObservedInvariant, compactConservativeInvariant, compactInvariantQuery,
            compactConditionProofs, compactProgramPoints, compactSmtDiagnostics, outputTruncation) =
            CreateScopeData(
                observedInvariant, observedFacts, conservativeInvariant, mergedPathFacts,
                sourceInvariantQuery, conditionProofSummaries, sourceProgramPoints,
                smtDiagnostics, normalizedOptions, maxProgramPoints);
        if (isFile)
        {
            var selectedProgramPointCount = lines.Sum(static line => line.ProgramPoints.Count);
            outputTruncation = SymbolicCompactOutputTruncation.Combine(
                new SymbolicCompactOutputTruncation(
                    sourceLines!.Count > lines.Count,
                    result.ProgramPointCount > selectedProgramPointCount,
                    false, false, false),
                outputTruncation,
                SymbolicCompactOutputTruncation.Combine(lines.Select(static line => line.Truncation)));
        }

        var lineCount = isFile ? result.LineCount : null;
        var linesWithProgramPoints = result.Scope.Kind switch
        {
            SymbolicQueryScopeKind.Point => 1,
            SymbolicQueryScopeKind.File => sourceLines!.Count,
            SymbolicQueryScopeKind.Span => result.ProgramPoints.Select(static value => value.Line).Distinct().Count(),
            _ => result.ProgramPointCount == 0 ? 0 : 1
        };
        var programPointCount = point == null ? result.ProgramPointCount : 1;
        var analysisTruncation = result.AnalysisTruncation;
        var analysisSummary = SymbolicCompactAnalysisSummary.From(
            compactInvariantQuery,
            programPointSummary,
            compactSmtDiagnostics,
            analysisTruncation);
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
            ObservedInvariant = compactObservedInvariant,
            ConservativeInvariant = compactConservativeInvariant,
            InvariantQuery = compactInvariantQuery,
            MergedInvariantText = compactConservativeInvariant.Text,
            Reachability = reachability,
            ProgramPointSummary = programPointSummary,
            ProofOutcomes = programPointSummary.ProofOutcomes,
            ConditionProofs = compactConditionProofs,
            Lines = lines,
            ProgramPoints = compactProgramPoints,
            SmtDiagnostics = compactSmtDiagnostics,
            AnalysisTruncation = analysisTruncation,
            AnalysisSummary = analysisSummary,
            Truncation = outputTruncation
        }, SymbolicCliProjectionJson.Options);
        return new SymbolicCompactQueryProjection(
            scope,
            lineCount,
            linesWithProgramPoints,
            programPointCount,
            compactObservedInvariant,
            compactConservativeInvariant,
            compactInvariantQuery,
            reachability,
            programPointSummary,
            compactConditionProofs,
            compactProgramPoints,
            compactSmtDiagnostics,
            outputTruncation,
            lines,
            analysisTruncation,
            analysisSummary,
            queryDescriptor,
            json);
    }

    private static SymbolicCompactLineResult CreateLineResult(
        SymbolicQueryResult result,
        SymbolicCompactQueryOptions options,
        int maxProgramPoints)
    {
        var (observedInvariant, conservativeInvariant, invariantQuery,
            conditionProofs, programPoints, smtDiagnostics, truncation) = CreateScopeData(
            result.ObservedInvariant, result.Facts, result.MergedInvariant, result.MergedPathFacts,
            result.InvariantQuery,
            SymbolicConditionProofSummary.FromProgramPoints(result.ProgramPoints),
            result.ProgramPoints,
            result.SmtDiagnostics,
            options,
            maxProgramPoints);
        return new SymbolicCompactLineResult(
            SymbolicOrderedJson.Object(
                ("filePath", result.FilePath), ("line", result.Line),
                ("programPointCount", result.ProgramPoints.Count),
                ("observedInvariant", observedInvariant),
                ("conservativeInvariant", conservativeInvariant),
                ("invariantQuery", invariantQuery),
                ("mergedInvariantText", conservativeInvariant.Text),
                ("reachability", result.ProgramPointSummary.Reachability),
                ("programPointSummary", result.ProgramPointSummary),
                ("proofOutcomes", result.ProgramPointSummary.ProofOutcomes),
                ("conditionProofs", conditionProofs),
                ("programPoints", programPoints),
                ("smtDiagnostics", smtDiagnostics),
                ("truncation", truncation)),
            programPoints,
            truncation);
    }

    private static (
        SymbolicCompactInvariantSummary ObservedInvariant,
        SymbolicCompactInvariantSummary ConservativeInvariant,
        SymbolicCompactInvariantQueryView InvariantQuery,
        IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs,
        IReadOnlyList<SymbolicCompactProgramPointResult> ProgramPoints,
        SymbolicCompactSmtDiagnostics SmtDiagnostics,
        SymbolicCompactOutputTruncation Truncation) CreateScopeData(
        SymbolicInvariantResult observedInvariant,
        IReadOnlyList<string> observedFacts,
        SymbolicInvariantResult conservativeInvariant,
        SymbolicMergedPathFacts? mergedPathFacts,
        SymbolicInvariantQueryView invariantQuery,
        IReadOnlyList<SymbolicConditionProofSummary> conditionProofSummaries,
        IReadOnlyList<SymbolicProgramPointResult> sourceProgramPoints,
        SymbolicSmtDiagnostics smtDiagnostics,
        SymbolicCompactQueryOptions options,
        int maxProgramPoints)
    {
        var compactObservedInvariant = SymbolicCompactInvariantSummary.FromObservedFacts(
            observedInvariant, observedFacts, options);
        var compactConservativeInvariant = SymbolicCompactInvariantSummary.FromInvariant(
            conservativeInvariant, mergedPathFacts, options);
        var programPoints = SymbolicCompactProjection.Take(sourceProgramPoints, maxProgramPoints)
            .Select(point => SymbolicCompactProgramPointResult.FromResult(point, options))
            .ToArray();
        var filteredProofs = SymbolicInvariantTargetFilter.ApplyToProofSummaries(
            conditionProofSummaries, options.InvariantTargets);
        var conditionProofs = SymbolicCompactProjection.Take(filteredProofs, options.MaxProofs);
        var truncation = SymbolicCompactOutputTruncation.Combine(
            new SymbolicCompactOutputTruncation(
                false, sourceProgramPoints.Count > programPoints.Length, false, false,
                filteredProofs.Count > options.MaxProofs),
            SymbolicCompactOutputTruncation.FromInvariant(compactObservedInvariant),
            SymbolicCompactOutputTruncation.FromInvariant(compactConservativeInvariant),
            SymbolicCompactOutputTruncation.Combine(programPoints.Select(static value => value.Truncation)));
        return (
            compactObservedInvariant,
            compactConservativeInvariant,
            SymbolicCompactInvariantQueryView.FromQueryView(invariantQuery, options),
            conditionProofs,
            programPoints,
            SymbolicCompactSmtDiagnostics.FromDiagnostics(smtDiagnostics),
            truncation);
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
