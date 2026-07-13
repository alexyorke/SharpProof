namespace SharpProof.Symbolic;

internal static class SymbolicAnalysisTruncationEventOrdering
{
    internal static IComparer<SymbolicAnalysisTruncationEvent> Canonical { get; } =
        Comparer<SymbolicAnalysisTruncationEvent>.Create(Compare);

    private static int Compare(
        SymbolicAnalysisTruncationEvent? left,
        SymbolicAnalysisTruncationEvent? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;

        var comparison = (left.SourceSpanStart ?? int.MaxValue)
            .CompareTo(right.SourceSpanStart ?? int.MaxValue);
        if (comparison != 0) return comparison;

        comparison = string.Compare(left.Code, right.Code, StringComparison.Ordinal);
        if (comparison != 0) return comparison;

        comparison = string.Compare(left.Provenance, right.Provenance, StringComparison.Ordinal);
        if (comparison != 0) return comparison;

        comparison = left.Kind.CompareTo(right.Kind);
        if (comparison != 0) return comparison;

        comparison = left.Limit.CompareTo(right.Limit);
        return comparison != 0 ? comparison : left.Observed.CompareTo(right.Observed);
    }
}

internal sealed class SymbolicAnalysisTruncationEventAccumulator
{
    private readonly Dictionary<
        (SymbolicAnalysisLimitKind Kind, int? SourceSpanStart, string Provenance),
        SymbolicAnalysisTruncationEvent> _events = new();

    internal int Count => _events.Count;

    internal IEnumerable<SymbolicAnalysisTruncationEvent> Events => _events.Values;

    internal void Add(SymbolicAnalysisTruncationEvent item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        var key = (item.Kind, item.SourceSpanStart, item.Provenance);
        if (!_events.TryGetValue(key, out var existing) || item.Observed > existing.Observed)
            _events[key] = item;
    }

    internal SymbolicAnalysisTruncationInfo ToInfo()
    {
        return Count == 0
            ? SymbolicAnalysisTruncationInfo.None
            : new SymbolicAnalysisTruncationInfo(
                _events.Values.OrderBy(static item => item, SymbolicAnalysisTruncationEventOrdering.Canonical)
                    .ToArray());
    }
}
