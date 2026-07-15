using System.Text.Json.Serialization;
using SharpProof.Symbolic;

internal sealed class SymbolicCliScopedQueryProjection
{
    internal SymbolicCliScopedQueryProjection(SymbolicQueryResult result)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
        Lines = Is(SymbolicQueryScopeKind.File)
            ? result.Lines.Select(static line => new SymbolicCliScopedQueryProjection(line)).ToArray()
            : null;
    }

    private SymbolicQueryResult Result { get; }
    public string FilePath => Result.FilePath;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Line => Is(SymbolicQueryScopeKind.Line) ? Result.Line ?? 0 : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SpanStart => Is(SymbolicQueryScopeKind.Span) ? Result.SpanStart ?? 0 : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SpanEnd => Is(SymbolicQueryScopeKind.Span) ? Result.SpanEnd ?? 0 : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SpanLength => Is(SymbolicQueryScopeKind.Span) ? SpanEnd - SpanStart : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StartLine => Is(SymbolicQueryScopeKind.Span) ? Result.Scope.StartLine ?? 1 : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StartColumn => Is(SymbolicQueryScopeKind.Span) ? Result.Scope.StartColumn ?? 1 : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EndLine => Is(SymbolicQueryScopeKind.Span) ? Result.Scope.EndLine ?? 1 : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EndColumn => Is(SymbolicQueryScopeKind.Span) ? Result.Scope.EndColumn ?? 1 : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LineCount => Is(SymbolicQueryScopeKind.File) ? Result.LineCount ?? 0 : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LinesWithProgramPoints => Result.Scope.Kind is SymbolicQueryScopeKind.Span or SymbolicQueryScopeKind.File
        ? Result.LinesWithProgramPoints
        : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SymbolicCliScopedQueryProjection>? Lines { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ObservedFacts => Is(SymbolicQueryScopeKind.File) ? Result.ObservedFacts : null;
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

    private bool Is(SymbolicQueryScopeKind kind) => Result.Scope.Kind == kind;
}
