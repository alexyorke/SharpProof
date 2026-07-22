namespace SharpProof.Symbolic;
internal enum SymbolicAnalysisLimitKind {
    IfElseFactMerge,
    SwitchFactMerge,
    TryFactMerge,
    TryCompletionBranches,
    ForeachElementFacts,
    ScopedBlockCompletionStatements,
    StructuralNullStateDepth,
    MergedPathConditions,
    MergeableFactsPerTargetPerState,
    FactChoiceCombinationsPerTarget,
    GuardFactsPerTargetPerState
}
internal sealed record SymbolicAnalysisTruncationEvent(
    SymbolicAnalysisLimitKind Kind,
    int Limit,
    int Observed,
    string Provenance,
    int? SourceSpanStart) {
    public string Code { get; } = GetCode(Kind);
    private static string GetCode(SymbolicAnalysisLimitKind value) {
        var text = value.ToString();
        var snakeBuilder = new System.Text.StringBuilder(text.Length + 8);
        for (var index = 0; index < text.Length; index++) {
            var character = text[index];
            if (index != 0 && char.IsUpper(character)) snakeBuilder.Append('_');
            snakeBuilder.Append(char.ToLowerInvariant(character));
        }
        var snake = snakeBuilder.ToString();
        return "analysis_limit." + (Enum.IsDefined(typeof(SymbolicAnalysisLimitKind), value) ? snake : "unknown");
    }
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
    internal static bool CanAddMergedSwitchFact(int addedCount, SyntaxNode sourceNode, string provenance) {
        var limit = Limits.MaxMergedSwitchFacts;
        if (addedCount < limit) return true;
        Record(SymbolicAnalysisLimitKind.SwitchFactMerge, limit, addedCount + 1, sourceNode, provenance);
        return false;
    }
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
