namespace SharpProof.Analyzer;

internal sealed class AnalyzerAttributeSymbols {
    internal AnalyzerAttributeSymbols(Compilation compilation) {
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
        EnforcePure = Resolve(compilation, "SharpProof.Attributes.EnforcePureAttribute");
        ZeroAllocations = Resolve(compilation, "SharpProof.Attributes.ZeroAllocationsAttribute");
        AllowedCapabilities = Resolve(
            compilation,
            "SharpProof.Attributes.AllowedCapabilitiesAttribute");
        DoesNotThrow = Resolve(compilation, "SharpProof.Attributes.DoesNotThrowAttribute");
        AllowedExceptions = Resolve(
            compilation,
            "SharpProof.Attributes.AllowedExceptionsAttribute");
        EffectContract = Resolve(compilation, "SharpProof.Attributes.EffectContractAttribute");
        Suppress = Resolve(compilation, "SharpProof.Attributes.SharpProofSuppressAttribute");
        Trusted = Resolve(compilation, "SharpProof.Attributes.SharpProofTrustedAttribute");
    }

    internal INamedTypeSymbol? EnforcePure { get; }
    internal INamedTypeSymbol? ZeroAllocations { get; }
    internal INamedTypeSymbol? AllowedCapabilities { get; }
    internal INamedTypeSymbol? DoesNotThrow { get; }
    internal INamedTypeSymbol? AllowedExceptions { get; }
    internal INamedTypeSymbol? EffectContract { get; }
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
        string metadataName) =>
        compilation.GetTypeByMetadataName(metadataName);
}
