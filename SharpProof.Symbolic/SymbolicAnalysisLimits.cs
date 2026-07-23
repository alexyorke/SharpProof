namespace SharpProof.Symbolic;
internal enum SymbolicAnalysisLimitKind {
    TryFactMerge
}
internal sealed record SymbolicAnalysisTruncationEvent(
    SymbolicAnalysisLimitKind Kind,
    int Limit,
    int Observed,
    string Provenance,
    int? SourceSpanStart) {
    public string Code { get; } = GetCode(Kind);
    private static string GetCode(SymbolicAnalysisLimitKind value) => value switch {
        SymbolicAnalysisLimitKind.TryFactMerge => "analysis_limit.try_fact_merge",
        _ => "analysis_limit.unknown"
    };
}
internal sealed record SymbolicAnalysisTruncationInfo(IReadOnlyList<SymbolicAnalysisTruncationEvent> Events) {
    public static readonly SymbolicAnalysisTruncationInfo None = new(Array.Empty<SymbolicAnalysisTruncationEvent>());
    public bool IsTruncated => Events.Count != 0;
    internal static SymbolicAnalysisTruncationInfo Combine(IEnumerable<SymbolicAnalysisTruncationInfo> truncations) {
        if (truncations == null) throw new ArgumentNullException(nameof(truncations));
        var events = new SymbolicAnalysisTruncationEventAccumulator();
        foreach (var truncation in truncations) {
            if (truncation == null) continue;
            foreach (var item in truncation.Events) events.Add(item);
        }
        return events.ToInfo();
    }
}
internal static class SymbolicAnalysisLimitContext {
    private static readonly AsyncLocal<Scope?> CurrentScope = new();
    internal static SharpProofAnalysisBudget Limits => CurrentScope.Value?.Limits ?? SharpProofAnalysisBudget.Default;
    internal static Scope Push(SharpProofAnalysisBudget? limits = null, SyntaxNode? sourceNode = null)
        => Push(limits, sourceNode, propagateEvents: true);
    private static Scope Push(SharpProofAnalysisBudget? limits, SyntaxNode? sourceNode, bool propagateEvents) {
        var parent = CurrentScope.Value;
        var scope = new Scope(
            parent,
            limits ?? parent?.Limits ?? SharpProofAnalysisBudget.Default,
            sourceNode?.SpanStart ?? parent?.DefaultSourceSpanStart,
            propagateEvents);
        CurrentScope.Value = scope;
        return scope;
    }
    internal static void Record(SymbolicAnalysisLimitKind kind, int limit, int observed, SyntaxNode? sourceNode, string provenance)
        => CurrentScope.Value?.Record(kind, limit, observed, sourceNode, provenance);
    internal sealed class Scope(Scope? parent, SharpProofAnalysisBudget limits, int? defaultSourceSpanStart,
        bool propagateEvents) : IDisposable {
        private readonly SymbolicAnalysisTruncationEventAccumulator _events = new();
        private readonly Scope? _parent = parent;
        private readonly bool _propagateEvents = propagateEvents;
        private bool _disposed;
        internal SharpProofAnalysisBudget Limits { get; } = limits;
        internal int? DefaultSourceSpanStart { get; } = defaultSourceSpanStart;
        internal SymbolicAnalysisTruncationInfo Snapshot() =>
            _events.ToInfo();
        internal void Record(SymbolicAnalysisLimitKind kind, int limit, int observed, SyntaxNode? sourceNode,
            string provenance) => Record(kind, limit, observed, sourceNode?.SpanStart ?? DefaultSourceSpanStart, provenance);
        private void Record(SymbolicAnalysisLimitKind kind, int limit, int observed, int? sourceSpanStart, string provenance)
            => _events.Add(new SymbolicAnalysisTruncationEvent(kind, limit, observed, provenance, sourceSpanStart));
        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            CurrentScope.Value = _parent;
            if (_parent == null || !_propagateEvents) return;
            foreach (var item in _events.Events)
                _parent.Record(item.Kind, item.Limit, item.Observed, item.SourceSpanStart, item.Provenance);
        }
    }
}
