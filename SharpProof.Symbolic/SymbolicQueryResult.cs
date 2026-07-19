using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal sealed class SymbolicQueryResult
{
    private SymbolicQueryResult(
        SymbolicQueryScope scope,
        IReadOnlyList<SymbolicProgramPointResult> programPoints,
        SymbolicInvariantResult observedInvariant,
        SymbolicInvariantResult mergedInvariant,
        SymbolicMergedPathFacts mergedPathFacts,
        SymbolicQueryMetrics metrics,
        IReadOnlyList<SymbolicConditionProofSummary> conditionProofs,
        SymbolicSmtDiagnostics smtDiagnostics,
        IReadOnlyList<SymbolicQueryLineGroup>? lineGroups = null)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        ProgramPoints = programPoints ?? throw new ArgumentNullException(nameof(programPoints));
        AnalysisTruncation = SymbolicAnalysisTruncationInfo.Combine(
            ProgramPoints.Select(static point => point.AnalysisTruncation));
        ObservedInvariant = observedInvariant ?? throw new ArgumentNullException(nameof(observedInvariant));
        MergedInvariant = mergedInvariant ?? throw new ArgumentNullException(nameof(mergedInvariant));
        MergedPathFacts = mergedPathFacts ?? throw new ArgumentNullException(nameof(mergedPathFacts));
        Metrics = metrics;
        Reachability = new SymbolicReachabilitySummary(
            metrics.ReachabilityNotCheckedCount,
            metrics.ReachabilityUnknownCount,
            metrics.ReachableCount,
            metrics.UnreachableCount);
        ProgramPointSummary = new SymbolicProgramPointSummary(
            metrics.ProgramPointCount,
            metrics.TotalPathConditionCount,
            metrics.MaxPathConditionCount,
            Reachability,
            new SymbolicProofOutcomeSummary(
                metrics.ProofTotalCount,
                metrics.ProofUnknownCount,
                metrics.ProofProvenTrueCount,
                metrics.ProofProvenFalseCount,
                metrics.ProofUnreachableCount));
        ConditionProofs = conditionProofs;
        SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        LineGroups = lineGroups ?? Array.Empty<SymbolicQueryLineGroup>();
        InvariantInfo = new SymbolicInvariantInfo(
            MergedInvariant.MergedInvariantText,
            SymbolicFactInfo.Distinct(ProgramPoints.SelectMany(static point => point.SymbolicFacts)),
            ProgramPoints.SelectMany(static point => point.ConditionProofs)
                .Select(static proof => proof.Proof).ToArray(),
            MergedInvariant.MergeKind,
            MergedInvariant.ConditionCount);
        ReachabilityWitnesses = ProgramPoints.Select(static point => point.ReachabilityWitness).ToArray();
        InputDomainSummary = SymbolicInputWitnessFactory.MergeAlternatives(ReachabilityWitnesses);
    }

    public SymbolicQueryScope Scope { get; }

    public string ScopeKind => Scope.Kind.ToString().ToLowerInvariant();

    public string FilePath => Scope.FilePath;

    public int? Line => Scope.Line;

    public int? Column => Scope.Column;

    public int? Position => Scope.Position;

    public int? SpanStart => Scope.SpanStart;

    public int? SpanEnd => Scope.SpanEnd;

    public int? LineCount => Scope.LineCount;

    public IReadOnlyList<SymbolicProgramPointResult> ProgramPoints { get; }

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; }

    public int ProgramPointCount => ProgramPoints.Count;

    public SymbolicInvariantResult ObservedInvariant { get; }

    internal SymbolicInvariantResult MergedInvariant { get; }

    public SymbolicInvariantInfo InvariantInfo { get; }

    public SymbolicMergedPathFacts MergedPathFacts { get; }

    public SymbolicProgramPointSummary ProgramPointSummary { get; }

    public SymbolicReachabilitySummary Reachability { get; }

    public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs { get; }

    internal SymbolicQueryMetrics Metrics { get; }

    public SymbolicSmtDiagnostics SmtDiagnostics { get; }

    public IReadOnlyList<SymbolicInputWitness> ReachabilityWitnesses { get; }

    public SymbolicInputDomainSummary InputDomainSummary { get; }

    internal IReadOnlyList<SymbolicQueryLineGroup> LineGroups { get; }

    internal IReadOnlyList<string> Facts =>
        ObservedInvariant.Conditions.Select(static condition => condition.Text).ToArray();

    internal IReadOnlyList<string> ObservedFacts => Facts;

    internal int ObservedFactCount => ObservedInvariant.ConditionCount;

    internal string MergedInvariantText => MergedPathFacts.MergedInvariantText;

    internal int? StartLine => Scope.StartLine;

    internal int? StartColumn => Scope.StartColumn;

    internal int? EndLine => Scope.EndLine;

    internal int? EndColumn => Scope.EndColumn;

    internal IReadOnlyList<SymbolicFactInfo> SymbolicFacts => InvariantInfo.Facts;

    internal IReadOnlyList<SymbolicQueryResult> Lines => LineGroups
        .Select(group => FromLine(FilePath, group.Line, group.ProgramPoints, SmtDiagnostics))
        .ToArray();

    internal int LinesWithProgramPoints => Scope.Kind switch
    {
        SymbolicQueryScopeKind.File => LineGroups.Count,
        SymbolicQueryScopeKind.Span => ProgramPoints.Select(static point => point.Line).Distinct().Count(),
        _ => ProgramPointCount == 0 ? 0 : 1
    };

    public SymbolicQueryResult Filter(SymbolicSourceQueryFilter filter)
    {
        if (filter == null) throw new ArgumentNullException(nameof(filter));

        var points = ProgramPoints.Where(filter.Matches).ToArray();
        return Scope.Kind switch
        {
            SymbolicQueryScopeKind.File => FromFile(
                FilePath,
                LineCount ?? 0,
                LineGroups
                    .Select(group => new SymbolicQueryLineGroup(
                        group.Line,
                        group.ProgramPoints.Where(filter.Matches).ToArray()))
                    .Where(static group => group.ProgramPoints.Count != 0)
                    .ToArray(),
                SmtDiagnostics),
            SymbolicQueryScopeKind.Line => FromLine(
                FilePath,
                Line ?? 0,
                points,
                SmtDiagnostics),
            SymbolicQueryScopeKind.Span => FromSpan(
                FilePath,
                SpanStart ?? 0,
                SpanEnd ?? 0,
                Scope.StartLine ?? 1,
                Scope.StartColumn ?? 1,
                Scope.EndLine ?? 1,
                Scope.EndColumn ?? 1,
                points,
                SmtDiagnostics),
            SymbolicQueryScopeKind.Point when points.Length != 0 => From(points[0]),
            SymbolicQueryScopeKind.Point => FromLine(
                FilePath,
                Line ?? 0,
                points,
                SmtDiagnostics),
            _ => throw new InvalidOperationException("Unexpected symbolic query scope.")
        };
    }

    internal static SymbolicQueryResult FromFile(
        string filePath,
        int lineCount,
        IReadOnlyList<SymbolicQueryLineGroup> lines,
        SymbolicSmtDiagnostics? smtDiagnostics = null)
    {
        if (lineCount < 0) throw new ArgumentOutOfRangeException(nameof(lineCount));
        if (lines == null) throw new ArgumentNullException(nameof(lines));
        return FromAggregate(
            new SymbolicQueryScope(
                SymbolicQueryScopeKind.File,
                filePath,
                lineCount: lineCount),
            lines.SelectMany(static line => line.ProgramPoints).ToArray(),
            smtDiagnostics,
            lines);
    }

    internal static SymbolicQueryResult FromLine(
        string filePath,
        int line,
        IReadOnlyList<SymbolicProgramPointResult> programPoints,
        SymbolicSmtDiagnostics? smtDiagnostics = null)
    {
        return FromAggregate(
            new SymbolicQueryScope(SymbolicQueryScopeKind.Line, filePath, line),
            programPoints,
            smtDiagnostics);
    }

    internal static SymbolicQueryResult FromSpan(
        string filePath,
        int spanStart,
        int spanEnd,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        IReadOnlyList<SymbolicProgramPointResult> programPoints,
        SymbolicSmtDiagnostics? smtDiagnostics = null)
    {
        if (spanStart < 0) throw new ArgumentOutOfRangeException(nameof(spanStart));
        if (spanEnd < spanStart) throw new ArgumentOutOfRangeException(nameof(spanEnd));
        return FromAggregate(
            new SymbolicQueryScope(
                SymbolicQueryScopeKind.Span,
                filePath,
                spanStart: spanStart,
                spanEnd: spanEnd,
                startLine: startLine,
                startColumn: startColumn,
                endLine: endLine,
                endColumn: endColumn),
            programPoints,
            smtDiagnostics);
    }

    internal static SymbolicQueryResult From(SymbolicProgramPointResult point)
    {
        if (point == null) throw new ArgumentNullException(nameof(point));

        return new SymbolicQueryResult(
            new SymbolicQueryScope(
                SymbolicQueryScopeKind.Point,
                point.FilePath,
                point.Line,
                point.Column,
                point.Position),
            new[] { point },
            point.Invariant,
            point.Invariant,
            SymbolicMergedPathFacts.FromProgramPoints(new[] { point }),
            SymbolicQueryMetrics.FromProgramPoints(new[] { point }),
            SymbolicConditionProofProjection.FromProgramPoints(new[] { point }),
            point.SmtDiagnostics);
    }

    private static SymbolicQueryResult FromAggregate(
        SymbolicQueryScope scope,
        IReadOnlyList<SymbolicProgramPointResult> programPoints,
        SymbolicSmtDiagnostics? smtDiagnostics,
        IReadOnlyList<SymbolicQueryLineGroup>? lineGroups = null)
    {
        if (programPoints == null) throw new ArgumentNullException(nameof(programPoints));
        var factSummary = SymbolicInvariantFactSummary.Merge(
            programPoints.Select(static point => point.Facts));
        var observedInvariant = SymbolicInvariantResult.FromFacts(
            factSummary.Facts,
            factSummary.MergedInvariantText);
        var mergedPathFacts = SymbolicMergedPathFacts.FromProgramPoints(programPoints);
        var mergedInvariant = SymbolicInvariantResult.FromMergedPathFacts(mergedPathFacts);
        var metrics = SymbolicQueryMetrics.FromProgramPoints(programPoints);
        var conditionProofs = SymbolicConditionProofProjection.FromProgramPoints(programPoints);
        var diagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        return new SymbolicQueryResult(
            scope,
            programPoints,
            observedInvariant,
            mergedInvariant,
            mergedPathFacts,
            metrics,
            conditionProofs,
            diagnostics,
            lineGroups);
    }
}

internal sealed record SymbolicQueryLineGroup(int Line, IReadOnlyList<SymbolicProgramPointResult> ProgramPoints);
