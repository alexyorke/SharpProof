namespace SharpProof.Analyzer;

internal sealed class AnalyzerAttributeSymbols {
    internal AnalyzerAttributeSymbols(Compilation compilation) {
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
        EnforcePure = Resolve(compilation, "EnforcePure");
        ZeroAllocations = Resolve(compilation, "ZeroAllocations");
        AllowedCapabilities = Resolve(compilation, "AllowedCapabilities");
        DoesNotThrow = Resolve(compilation, "DoesNotThrow");
        AllowedExceptions = Resolve(compilation, "AllowedExceptions");
        EffectContract = Resolve(compilation, "EffectContract");
        NotNull = Resolve(compilation, "NotNull");
        Positive = Resolve(compilation, "Positive");
        InRange = Resolve(compilation, "InRange");
        Suppress = Resolve(compilation, "SharpProofSuppress");
        Trusted = Resolve(compilation, "SharpProofTrusted");
    }

    internal INamedTypeSymbol? EnforcePure { get; }
    internal INamedTypeSymbol? ZeroAllocations { get; }
    internal INamedTypeSymbol? AllowedCapabilities { get; }
    internal INamedTypeSymbol? DoesNotThrow { get; }
    internal INamedTypeSymbol? AllowedExceptions { get; }
    internal INamedTypeSymbol? EffectContract { get; }
    internal INamedTypeSymbol? NotNull { get; }
    internal INamedTypeSymbol? Positive { get; }
    internal INamedTypeSymbol? InRange { get; }
    internal INamedTypeSymbol? Suppress { get; }
    internal INamedTypeSymbol? Trusted { get; }

    internal static bool Is(
        AttributeData attribute,
        INamedTypeSymbol? expected) =>
        expected != null &&
        SymbolEqualityComparer.Default.Equals(
            attribute.AttributeClass?.OriginalDefinition,
            expected.OriginalDefinition);

    internal static IEnumerable<AttributeData> GetCallableAttributes(
        IMethodSymbol method) {
        foreach (var attribute in method.GetAttributes())
            yield return attribute;
        if (method.AssociatedSymbol is IPropertySymbol property)
            foreach (var attribute in property.GetAttributes())
                yield return attribute;
    }

    private static INamedTypeSymbol? Resolve(
        Compilation compilation,
        string attributeName) =>
        compilation.GetTypeByMetadataName(
            "SharpProof.Attributes." + attributeName + "Attribute");
}
