namespace SharpProof.Symbolic;

internal static class SymbolicComplexityResultProjector
{
    internal static SymbolicComplexityResult Project(
        ResolvedComplexityTarget target,
        MethodAnalysisSummary summary,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var complexity = new SymbolicComplexityInfo(
            summary.Cost.ToBigOText(target.Symbol),
            summary.Cost.ToPublicKind(),
            summary.Cost.IsConservative,
            summary.Cost.IsUnknown,
            summary.Cost.IsRecursiveUnknown);

        return new SymbolicComplexityResult(
            target.FilePath,
            target.MethodName,
            target.MethodDisplayName,
            target.DeclarationKind,
            target.SpanStart,
            target.SpanEnd,
            target.StartLine,
            target.StartColumn,
            target.EndLine,
            target.EndColumn,
            complexity,
            DistinctDrivers(summary.Drivers),
            DistinctUnknownReasons(summary.UnknownReasons),
            DistinctCalleeSummaries(summary.CalleeSummaries));
    }

    private static IReadOnlyList<SymbolicComplexityDriverInfo> DistinctDrivers(
        IEnumerable<SymbolicComplexityDriverInfo> drivers)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var distinct = new List<SymbolicComplexityDriverInfo>();
        foreach (var driver in drivers)
        {
            var key = string.Join(
                "\u001f",
                driver.Kind,
                driver.Description,
                driver.SourceSpanStart.ToString(CultureInfo.InvariantCulture),
                driver.SourceSpanLength.ToString(CultureInfo.InvariantCulture),
                driver.SourceLine.ToString(CultureInfo.InvariantCulture),
                driver.SourceColumn.ToString(CultureInfo.InvariantCulture));
            if (seen.Add(key)) distinct.Add(driver);
        }

        return distinct;
    }

    private static IReadOnlyList<SymbolicComplexityUnknownReason> DistinctUnknownReasons(
        IEnumerable<SymbolicComplexityUnknownReason> reasons)
    {
        return reasons
            .Where(static reason => reason != SymbolicComplexityUnknownReason.None)
            .Distinct()
            .ToArray();
    }

    private static IReadOnlyList<SymbolicComplexityCalleeInfo> DistinctCalleeSummaries(
        IEnumerable<SymbolicComplexityCalleeInfo> callees)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var distinct = new List<SymbolicComplexityCalleeInfo>();
        foreach (var callee in callees)
        {
            var key = string.Join(
                "\u001f",
                callee.MethodDisplayName,
                callee.ComplexityText,
                callee.Kind.ToString(),
                callee.IsConservative.ToString(),
                callee.UnknownReason.ToString());
            if (seen.Add(key)) distinct.Add(callee);
        }

        return distinct;
    }
}
