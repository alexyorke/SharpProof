using SharpProof.Symbolic;

internal static class SymbolicCliQueryResultAdapter
{
    internal static object ToFullJsonResult(SymbolicQueryResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        return result.Scope.Kind switch
        {
            SymbolicQueryScopeKind.File => new SymbolicCliFileQueryProjection(result),
            SymbolicQueryScopeKind.Line => new SymbolicCliLineQueryProjection(result),
            SymbolicQueryScopeKind.Span => new SymbolicCliSpanQueryProjection(result),
            SymbolicQueryScopeKind.Point when result.ProgramPoints.Count != 0 => result.ProgramPoints[0],
            _ => throw new InvalidOperationException("Symbolic query result has no value for its scope.")
        };
    }
}

internal abstract class SymbolicCliScopedQueryProjection
{
    protected SymbolicCliScopedQueryProjection(SymbolicQueryResult result)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    protected SymbolicQueryResult Result { get; }
    public IReadOnlyList<SymbolicProgramPointResult> ProgramPoints => Result.ProgramPoints;
    public int ProgramPointCount => Result.ProgramPointCount;
    public SymbolicAnalysisTruncationInfo AnalysisTruncation => Result.AnalysisTruncation;
    public IReadOnlyList<string> Facts => Result.Facts;
    public int ObservedFactCount => Result.ObservedFactCount;
    public SymbolicInvariantResult ObservedInvariant => Result.ObservedInvariant;
    public SymbolicMergedPathFacts MergedPathFacts => Result.MergedPathFacts;
    public string MergedInvariantText => Result.MergedInvariantText;
    public IReadOnlyList<SymbolicFactInfo> SymbolicFacts => Result.SymbolicFacts;
    public SymbolicInvariantInfo InvariantInfo => Result.InvariantInfo;
    public SymbolicProgramPointSummary ProgramPointSummary => Result.ProgramPointSummary;
    public SymbolicReachabilitySummary Reachability => Result.Reachability;
    public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs => Result.ConditionProofs;
    public SymbolicSmtDiagnostics SmtDiagnostics => Result.SmtDiagnostics;
    public SymbolicInvariantQueryView InvariantQuery => Result.InvariantQuery;
    public IReadOnlyList<SymbolicInputWitness> ReachabilityWitnesses => Result.ReachabilityWitnesses;
    public SymbolicInputDomainSummary InputDomainSummary => Result.InputDomainSummary;
}

internal sealed class SymbolicCliLineQueryProjection : SymbolicCliScopedQueryProjection
{
    internal SymbolicCliLineQueryProjection(SymbolicQueryResult result) : base(result) { }

    public string FilePath => Result.FilePath;
    public int Line => Result.Line ?? 0;
}

internal sealed class SymbolicCliSpanQueryProjection : SymbolicCliScopedQueryProjection
{
    internal SymbolicCliSpanQueryProjection(SymbolicQueryResult result) : base(result) { }

    public string FilePath => Result.FilePath;
    public int SpanStart => Result.SpanStart ?? 0;
    public int SpanEnd => Result.SpanEnd ?? 0;
    public int SpanLength => SpanEnd - SpanStart;
    public int StartLine => Result.Scope.StartLine ?? 1;
    public int StartColumn => Result.Scope.StartColumn ?? 1;
    public int EndLine => Result.Scope.EndLine ?? 1;
    public int EndColumn => Result.Scope.EndColumn ?? 1;
    public int LinesWithProgramPoints => Result.LinesWithProgramPoints;
}

internal sealed class SymbolicCliFileQueryProjection : SymbolicCliScopedQueryProjection
{
    internal SymbolicCliFileQueryProjection(SymbolicQueryResult result) : base(result)
    {
        Lines = result.Lines.Select(static line => new SymbolicCliLineQueryProjection(line)).ToArray();
    }

    public string FilePath => Result.FilePath;
    public int LineCount => Result.LineCount ?? 0;
    public int LinesWithProgramPoints => Result.LinesWithProgramPoints;
    public IReadOnlyList<SymbolicCliLineQueryProjection> Lines { get; }
    public IReadOnlyList<string> ObservedFacts => Result.ObservedFacts;
}
