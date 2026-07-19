internal static class BclFallbackInventoryBuilder
{
    public static BclFallbackInventoryReport Build(AssemblyEffectReport[] assemblies)
    {
        var entries = assemblies
            .SelectMany(assembly => GetInventoryMethods(assembly)
                .Select(method => TryCreateEntry(assembly, method, out var entry) ? entry : null))
            .Where(static entry => entry != null)
            .Cast<BclFallbackInventoryEntry>()
            .GroupBy(static entry => (entry.AssemblyName, entry.Identity))
            .Select(static group => group.First())
            .OrderBy(static entry => entry.AssemblyName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.DisplayName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.CanonicalKey, StringComparer.Ordinal)
            .ToArray();

        return new BclFallbackInventoryReport(
            EffectSummarySchemaContract.CurrentVersion,
            entries.Length,
            CountGuess(entries, BclPurityFallbackHeuristics.ProbablyPure),
            CountGuess(entries, BclPurityFallbackHeuristics.ProbablyImpure),
            CountGuess(entries, BclPurityFallbackHeuristics.Unknown),
            entries);
    }

    private static IEnumerable<MethodEffectSummary> GetInventoryMethods(AssemblyEffectReport assembly)
    {
        return assembly.ClassificationMethods.Length == 0
            ? assembly.Methods
            : assembly.ClassificationMethods;
    }

    private static bool TryCreateEntry(
        AssemblyEffectReport assembly,
        MethodEffectSummary method,
        out BclFallbackInventoryEntry? entry)
    {
        entry = null;
        if (!TryCreateShape(assembly, method, out var shape)) return false;

        if (!BclPurityFallbackHeuristics.TryClassify(shape, out var classification)) return false;

        entry = new BclFallbackInventoryEntry(
            assembly.AssemblyName,
            method.Symbol,
            method.Identity,
            method.CanonicalKey,
            classification.Guess,
            classification.Confidence,
            classification.Reason,
            classification.Category,
            method.PurityClassification?.Classification);
        return true;
    }

    private static bool TryCreateShape(
        AssemblyEffectReport assembly,
        MethodEffectSummary method,
        out BclPurityFallbackHeuristics.Shape shape)
    {
        shape = default;
        if (!BclPurityFallbackHeuristics.IsFrameworkSystemAssemblyName(assembly.AssemblyName) ||
            !BclPurityFallbackHeuristics.IsSystemNamespace(GetNamespaceName(method.Identity.ContainingMetadataType)))
            return false;

        var isGetter = string.Equals(method.Identity.MethodKind, "property-get", StringComparison.Ordinal);
        var isSetter = string.Equals(method.Identity.MethodKind, "property-set", StringComparison.Ordinal);
        var isProperty = isGetter || isSetter;
        var returnTypeName = GetHeuristicTypeName(method.Identity.ReturnType);
        var containingTypeName = method.Identity.ContainingMetadataType;
        var returnsVoid = string.Equals(returnTypeName, "System.Void", StringComparison.Ordinal);
        var hasRefOrOutParameter = method.Identity.Parameters.Any(static parameter =>
            !string.Equals(parameter.RefKind, "none", StringComparison.Ordinal));
        var normalizedReturnType = BclPurityFallbackHeuristics.NormalizeTypeName(returnTypeName);

        shape = new BclPurityFallbackHeuristics.Shape(
            GetNamespaceName(containingTypeName),
            containingTypeName,
            method.Identity.Name,
            true,
            isProperty,
            false,
            string.Equals(method.Identity.MethodKind, "constructor", StringComparison.Ordinal),
            method.IsStatic,
            returnsVoid,
            !string.Equals(normalizedReturnType, returnTypeName, StringComparison.Ordinal),
            hasRefOrOutParameter,
            BclPurityFallbackHeuristics.IsValueLikeTypeName(returnTypeName),
            BclPurityFallbackHeuristics.IsKnownValueTypeName(containingTypeName),
            method.Identity.Parameters.All(static parameter =>
            {
                var typeName = GetHeuristicTypeName(parameter.Type);
                return BclPurityFallbackHeuristics.IsValueLikeTypeName(typeName) ||
                       BclPurityFallbackHeuristics.IsReadOnlyViewTypeName(typeName);
            }),
            isSetter);
        return true;
    }

    private static string GetHeuristicTypeName(string structuralType)
    {
        if (structuralType.StartsWith("named:", StringComparison.Ordinal))
        {
            var typeName = structuralType.Substring("named:".Length);
            var argumentsStart = typeName.IndexOf('[', StringComparison.Ordinal);
            return argumentsStart < 0 ? typeName : typeName.Substring(0, argumentsStart);
        }

        return structuralType;
    }

    private static string GetNamespaceName(string typeName)
    {
        var lastSeparator = typeName.LastIndexOf('.');
        return lastSeparator <= 0 ? string.Empty : typeName.Substring(0, lastSeparator);
    }

    private static int CountGuess(IReadOnlyList<BclFallbackInventoryEntry> entries, string guess)
    {
        return entries.Count(entry => string.Equals(entry.Guess, guess, StringComparison.Ordinal));
    }

}

internal sealed record BclFallbackInventoryReport(
    int SchemaVersion,
    int CandidateCount,
    int ProbablyPureCount,
    int ProbablyImpureCount,
    int UnknownCount,
    BclFallbackInventoryEntry[] Entries);

internal sealed record BclFallbackInventoryEntry(
    string AssemblyName,
    string DisplayName,
    StructuralMethodIdentity Identity,
    string CanonicalKey,
    string Guess,
    string Confidence,
    string Reason,
    string Category,
    string? StrongerPurityClassification);
