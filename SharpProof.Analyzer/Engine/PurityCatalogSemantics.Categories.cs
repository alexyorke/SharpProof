namespace SharpProof.Analyzer.Engine;

internal static partial class ImpurityCatalog {
    internal static string GetKnownImpureCatalogHitCategory(ISymbol symbol, bool includeSynchronizationCategory = false) {
        var containingType = symbol.ContainingType?.ToDisplayString() ?? string.Empty;
        var containingNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        if (includeSynchronizationCategory &&
            (containingType == "System.Threading.Interlocked" ||
             containingType == "System.Threading.Monitor" ||
             containingType == "System.Threading.Mutex" ||
             containingType == "System.Threading.Semaphore" ||
             containingType == "System.Threading.SemaphoreSlim" ||
             containingType == "System.Collections.Immutable.ImmutableInterlocked"))
            return "synchronization";

        if (containingNamespace.StartsWith("System.Reflection", StringComparison.Ordinal) ||
            containingType.StartsWith("System.Reflection.", StringComparison.Ordinal) ||
            containingType == "System.Type" ||
            IsAssemblyLoadContextTypeOrDerived(symbol.ContainingType) ||
            containingType == "System.Environment" ||
            containingType == "System.DateTime" ||
            containingType == "System.DateTimeOffset" ||
            containingType == "System.TimeProvider" ||
            containingType == "System.TimeZoneInfo" ||
            containingType == "System.Diagnostics.Stopwatch")
            return "reflection_environment_source";

        return "catalog_hit";
    }

    private static bool IsAssemblyLoadContextTypeOrDerived(INamedTypeSymbol? type) {
        for (var current = type; current != null; current = current.BaseType)
            if (string.Equals(
                    current.OriginalDefinition.ToDisplayString(),
                    "System.Runtime.Loader.AssemblyLoadContext",
                    StringComparison.Ordinal))
                return true;

        return false;
    }

}
