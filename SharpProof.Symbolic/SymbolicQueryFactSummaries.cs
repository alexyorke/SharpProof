namespace SharpProof.Symbolic;
internal static class SymbolicMergedPathFactMerger {
    internal static string MergeInvariantText(IEnumerable<SymbolicProgramPointAnalysis> programPoints) {
        if (programPoints == null) throw new ArgumentNullException(nameof(programPoints));
        var points = programPoints.ToArray();
        if (points.Length == 0) return "true";
        var candidates = points
            .Where(static point => point.Reachability != SymbolicReachability.Unreachable)
            .ToArray();
        if (candidates.Length == 0) return "false";
        var seenConditionTexts = new HashSet<string>(StringComparer.Ordinal);
        var orderedConditions = new List<string>();
        var conditionSets = new List<HashSet<string>>();
        foreach (var point in candidates) {
            var conditionSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var formula in point.PathConditions) {
                var condition = SymbolicFormulaDisplay.Format(formula);
                if (string.IsNullOrWhiteSpace(condition)) continue;
                if (conditionSet.Add(condition) && seenConditionTexts.Add(condition))
                    orderedConditions.Add(condition);
            }
            conditionSets.Add(conditionSet);
        }
        var commonTexts = new HashSet<string>(conditionSets[0], StringComparer.Ordinal);
        for (var index = 1; index < conditionSets.Count; index++) commonTexts.IntersectWith(conditionSets[index]);
        var mergedFacts = orderedConditions
            .Where(commonTexts.Contains)
            .Concat(CreateConservativeUnknowns(orderedConditions.Where(condition => !commonTexts.Contains(condition))))
            .ToArray();
        return FormatMergedInvariantFacts(mergedFacts);
    }
    private static string FormatMergedInvariantFacts(IReadOnlyList<string> facts) => facts.Count switch {
        0 => "true",
        1 => facts[0],
        _ => string.Join(" && ", facts.Select(static fact => "(" + fact + ")"))
    };
    private static IEnumerable<string> CreateConservativeUnknowns(IEnumerable<string> conditions) {
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var condition in conditions)
            if (seenTargets.Add(condition))
                yield return "unknown(" + condition + ")";
    }
}
