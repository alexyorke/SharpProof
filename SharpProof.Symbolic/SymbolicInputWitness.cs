namespace SharpProof.Symbolic;

internal enum SymbolicWitnessStatus {
    None,
    Exact,
    Approximate,
    Unsupported
}
internal sealed record SymbolicSatisfyingAssignment(string SourceName, string Value);

internal sealed record SymbolicInputWitness(
    SymbolicWitnessStatus Status,
    string Reason,
    IReadOnlyList<SymbolicSatisfyingAssignment> Assignments) {
    public bool IsAvailable => Status is SymbolicWitnessStatus.Exact or SymbolicWitnessStatus.Approximate;
}
internal static class SymbolicInputWitnessFactory {
    internal static SymbolicInputWitness CreateReachability(
        SmtSatisfyingWitness? witness,
        SemanticModel? semanticModel,
        int position,
        SymbolicReachability reachability,
        string reason) {
        if (reachability == SymbolicReachability.Unreachable) return None(reason);
        if (reachability == SymbolicReachability.Reachable && witness == null) return Unconstrained();
        return Create(witness, semanticModel, position, SymbolicWitnessStatus.Unsupported,
            string.IsNullOrWhiteSpace(reason) ? "reachability_witness_unavailable" : reason);
    }
    internal static SymbolicInputWitness Create(
        SmtSatisfyingWitness? witness,
        SemanticModel? semanticModel,
        int position,
        SymbolicWitnessStatus missingStatus,
        string missingReason) {
        var names = SymbolicInputNameMap.Create(semanticModel, position);
        var assignments = witness?.Assignments
            .Select(assignment => new SymbolicSatisfyingAssignment(names.Resolve(assignment.Name), assignment.Value))
            .ToArray() ?? [];
        return new SymbolicInputWitness(
            witness == null ? missingStatus : MapStatus(witness.Status),
            witness?.Reason ?? missingReason,
            assignments);
    }
    internal static SymbolicInputWitness Unconstrained() => CreateEmpty(SymbolicWitnessStatus.Exact, "unconstrained_inputs");
    internal static SymbolicInputWitness None(string reason) => CreateEmpty(SymbolicWitnessStatus.None, reason);
    internal static SymbolicInputWitness Unsupported(string reason) => CreateEmpty(SymbolicWitnessStatus.Unsupported, reason);
    private static SymbolicInputWitness CreateEmpty(SymbolicWitnessStatus status, string reason) =>
        new(status, reason, Array.Empty<SymbolicSatisfyingAssignment>());

    private static SymbolicWitnessStatus MapStatus(SmtWitnessStatus status) => status switch {
        SmtWitnessStatus.Exact => SymbolicWitnessStatus.Exact,
        SmtWitnessStatus.Approximate => SymbolicWitnessStatus.Approximate,
        SmtWitnessStatus.Unsupported => SymbolicWitnessStatus.Unsupported,
        _ => SymbolicWitnessStatus.None
    };
}
internal sealed class SymbolicInputNameMap {
    private readonly Dictionary<string, string> _names;

    private SymbolicInputNameMap(Dictionary<string, string> names) => _names = names;

    internal static SymbolicInputNameMap Create(SemanticModel? semanticModel, int position) {
        var names = new Dictionary<string, string>(StringComparer.Ordinal) { ["this"] = "this" };
        if (semanticModel == null) return new SymbolicInputNameMap(names);
        try {
            foreach (var symbol in semanticModel.LookupSymbols(position)) {
                if (symbol is not (IParameterSymbol or ILocalSymbol or IFieldSymbol or IPropertySymbol)) continue;
                names[symbol.Name] = symbol.Name;
                names[SymbolicFactFactory.GetSmtVariableName(symbol)] = symbol.Name;
                if (!SymbolEqualityComparer.Default.Equals(symbol, symbol.OriginalDefinition))
                    names[SymbolicFactFactory.GetSmtVariableName(symbol.OriginalDefinition)] = symbol.Name;
            }
        }
        catch (ArgumentOutOfRangeException) {
            // Preserve witness data when a synthetic query position cannot be looked up.
        }
        return new SymbolicInputNameMap(names);
    }
    internal string Resolve(string symbolicName) {
        if (symbolicName.StartsWith("this.", StringComparison.Ordinal))
            return RemoveNumericLocationSuffix(symbolicName, "this.".Length);
        var root = GetRootName(symbolicName);
        if (_names.TryGetValue(root, out var name)) {
            var suffixStart = GetRootSegmentEnd(symbolicName);
            return name + (suffixStart < symbolicName.Length ? symbolicName.Substring(suffixStart) : string.Empty);
        }
        return string.Equals(root, "this", StringComparison.Ordinal) ? "this" : RemoveNumericLocationSuffix(root, 0);
    }
    private static string GetRootName(string symbolicName) {
        var end = GetRootSegmentEnd(symbolicName);
        var root = symbolicName.Substring(0, end);
        var versionIndex = root.LastIndexOf("@v", StringComparison.Ordinal);
        return versionIndex > 0 && root.Substring(versionIndex + 2).All(char.IsDigit)
            ? root.Substring(0, versionIndex)
            : root;
    }
    private static int GetRootSegmentEnd(string symbolicName) {
        var end = symbolicName.Length;
        foreach (var marker in new[] { ".", "[", "?" }) {
            var markerIndex = symbolicName.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0 && markerIndex < end) end = markerIndex;
        }
        return end;
    }
    private static string RemoveNumericLocationSuffix(string value, int minimumPrefixLength) {
        var locationIndex = value.LastIndexOf('#');
        return locationIndex > minimumPrefixLength && value.Substring(locationIndex + 1).All(char.IsDigit)
            ? value.Substring(0, locationIndex)
            : value;
    }
}
