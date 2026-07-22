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
        var orderedConditions = new List<(string Text, string Target)>();
        var conditionSets = new List<HashSet<string>>();
        foreach (var point in candidates) {
            var conditionSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var formula in point.PathConditions) {
                var condition = (
                    Text: SymbolicFormulaDisplay.Format(formula),
                    Target: SymbolicFormulaDisplay.GetMergeTarget(formula));
                if (string.IsNullOrWhiteSpace(condition.Text)) continue;

                if (conditionSet.Add(condition.Text) && seenConditionTexts.Add(condition.Text))
                    orderedConditions.Add(condition);
            }
            conditionSets.Add(conditionSet);
        }
        var commonTexts = new HashSet<string>(conditionSets[0], StringComparer.Ordinal);
        for (var index = 1; index < conditionSets.Count; index++) commonTexts.IntersectWith(conditionSets[index]);

        var mergedFacts = orderedConditions
            .Where(condition => commonTexts.Contains(condition.Text))
            .Select(static condition => condition.Text)
            .Concat(CreateConservativeUnknowns(orderedConditions.Where(condition => !commonTexts.Contains(condition.Text))))
            .ToArray();
        return SymbolicInvariantFactSummary.FormatMergedInvariantFacts(mergedFacts);
    }
    private static IEnumerable<string> CreateConservativeUnknowns(IEnumerable<(string Text, string Target)> conditions) {
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var condition in conditions) {
            var target = string.IsNullOrWhiteSpace(condition.Target) ? "path" : condition.Target;
            if (seenTargets.Add(target)) yield return "unknown(" + target + ")";
        }
    }
}
