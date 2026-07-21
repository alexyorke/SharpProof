namespace SharpProof.Symbolic;

internal sealed class SymbolicQueryResult(
    SymbolicQueryScope scope,
    IReadOnlyList<SymbolicProgramPointResult> programPoints,
    SymbolicInvariantResult observedInvariant,
    SymbolicInvariantResult mergedInvariant,
    SymbolicMergedPathFacts mergedPathFacts,
    SymbolicQueryMetrics metrics,
    IReadOnlyList<SymbolicConditionProofSummary> conditionProofs,
    SymbolicSmtDiagnostics smtDiagnostics,
    IReadOnlyList<SymbolicQueryLineGroup>? lineGroups = null) {
    public SymbolicQueryScope Scope { get; } = scope ?? throw new ArgumentNullException(nameof(scope));

    public string ScopeKind => Scope.Kind.ToString().ToLowerInvariant();

    public string FilePath => Scope.FilePath;

    public int? Line => Scope.Line;

    public int? Column => Scope.Column;

    public int? Position => Scope.Position;

    public int? SpanStart => Scope.SpanStart;

    public int? SpanEnd => Scope.SpanEnd;

    public int? LineCount => Scope.LineCount;

    public IReadOnlyList<SymbolicProgramPointResult> ProgramPoints { get; } =
        programPoints ?? throw new ArgumentNullException(nameof(programPoints));

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; } =
        SymbolicAnalysisTruncationInfo.Combine(
            (programPoints ?? throw new ArgumentNullException(nameof(programPoints)))
            .Select(static point => point.AnalysisTruncation));

    public int ProgramPointCount => ProgramPoints.Count;

    public SymbolicInvariantResult ObservedInvariant { get; } =
        observedInvariant ?? throw new ArgumentNullException(nameof(observedInvariant));

    internal SymbolicInvariantResult MergedInvariant { get; } =
        mergedInvariant ?? throw new ArgumentNullException(nameof(mergedInvariant));

    public SymbolicInvariantInfo InvariantInfo { get; } = new(
        mergedInvariant.MergedInvariantText,
        SymbolicFactInfo.Distinct(programPoints.SelectMany(static point => point.SymbolicFacts)),
        programPoints.SelectMany(static point => point.ConditionProofs)
            .Select(static proof => proof.Proof).ToArray(),
        mergedInvariant.MergeKind,
        mergedInvariant.ConditionCount);

    public SymbolicMergedPathFacts MergedPathFacts { get; } =
        mergedPathFacts ?? throw new ArgumentNullException(nameof(mergedPathFacts));

    public SymbolicProgramPointSummary ProgramPointSummary { get; } = new(
        metrics.ProgramPointCount,
        metrics.TotalPathConditionCount,
        metrics.MaxPathConditionCount,
        new SymbolicReachabilitySummary(
            metrics.ReachabilityNotCheckedCount,
            metrics.ReachabilityUnknownCount,
            metrics.ReachableCount,
            metrics.UnreachableCount),
        new SymbolicProofOutcomeSummary(
            metrics.ProofTotalCount,
            metrics.ProofUnknownCount,
            metrics.ProofProvenTrueCount,
            metrics.ProofProvenFalseCount,
            metrics.ProofUnreachableCount));

    public SymbolicReachabilitySummary Reachability { get; } = new(
        metrics.ReachabilityNotCheckedCount,
        metrics.ReachabilityUnknownCount,
        metrics.ReachableCount,
        metrics.UnreachableCount);

    public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs { get; } = conditionProofs;

    internal SymbolicQueryMetrics Metrics { get; } = metrics;

    public SymbolicSmtDiagnostics SmtDiagnostics { get; } =
        smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;

    public IReadOnlyList<SymbolicInputWitness> ReachabilityWitnesses { get; } =
        programPoints.Select(static point => point.ReachabilityWitness).ToArray();

    public SymbolicInputDomainSummary InputDomainSummary { get; } =
        SymbolicInputWitnessFactory.MergeAlternatives(
            programPoints.Select(static point => point.ReachabilityWitness).ToArray());

    internal IReadOnlyList<SymbolicQueryLineGroup> LineGroups { get; } =
        lineGroups ?? Array.Empty<SymbolicQueryLineGroup>();

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

    internal int LinesWithProgramPoints => Scope.Kind switch {
        SymbolicQueryScopeKind.File => LineGroups.Count,
        SymbolicQueryScopeKind.Span => ProgramPoints.Select(static point => point.Line).Distinct().Count(),
        _ => ProgramPointCount == 0 ? 0 : 1
    };

    internal static SymbolicQueryResult FromFile(
        string filePath,
        int lineCount,
        IReadOnlyList<SymbolicQueryLineGroup> lines,
        SymbolicSmtDiagnostics? smtDiagnostics = null) {
        if (lineCount < 0) throw new ArgumentOutOfRangeException(nameof(lineCount));
        if (lines == null) throw new ArgumentNullException(nameof(lines));
        return FromAggregate(
            new SymbolicQueryScope(
                SymbolicQueryScopeKind.File,
                filePath,
                LineCount: lineCount),
            lines.SelectMany(static line => line.ProgramPoints).ToArray(),
            smtDiagnostics,
            lines);
    }

    internal static SymbolicQueryResult FromLine(
        string filePath,
        int line,
        IReadOnlyList<SymbolicProgramPointResult> programPoints,
        SymbolicSmtDiagnostics? smtDiagnostics = null) {
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
        SymbolicSmtDiagnostics? smtDiagnostics = null) {
        if (spanStart < 0) throw new ArgumentOutOfRangeException(nameof(spanStart));
        if (spanEnd < spanStart) throw new ArgumentOutOfRangeException(nameof(spanEnd));
        return FromAggregate(
            new SymbolicQueryScope(
                SymbolicQueryScopeKind.Span,
                filePath,
                SpanStart: spanStart,
                SpanEnd: spanEnd,
                StartLine: startLine,
                StartColumn: startColumn,
                EndLine: endLine,
                EndColumn: endColumn),
            programPoints,
            smtDiagnostics);
    }

    internal static SymbolicQueryResult From(SymbolicProgramPointResult point) {
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
        IReadOnlyList<SymbolicQueryLineGroup>? lineGroups = null) {
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
