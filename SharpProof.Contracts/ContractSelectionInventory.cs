namespace SharpProof.Contracts;

[Flags]
internal enum ContractSelectionFeatures {
    None = 0,
    Contracts = 1,
    Effects = 2
}

internal sealed class ContractSelectionInventory {
    private static readonly ConditionalWeakTable<
        Compilation, ContractSelectionInventory> Cache = new();

    internal const string ContractForMetadataName =
        "SharpProof.Attributes.ContractForAttribute";

    private ContractSelectionInventory(Compilation compilation) {
        if (compilation == null)
            throw new ArgumentNullException(nameof(compilation));
        ContractFor = compilation.GetTypeByMetadataName(
            ContractForMetadataName);
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

    internal static ContractSelectionInventory ForCompilation(
        Compilation compilation) =>
        Cache.GetValue(compilation, static value => new(value));

    internal INamedTypeSymbol? ContractFor { get; }
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

    internal bool IsClosedContract(AttributeData attribute) =>
        Is(attribute, NotNull) ||
        Is(attribute, Positive) ||
        Is(attribute, InRange);

    internal bool IsEffectContract(AttributeData attribute) =>
        Is(attribute, EnforcePure) ||
        Is(attribute, ZeroAllocations) ||
        Is(attribute, AllowedCapabilities) ||
        Is(attribute, DoesNotThrow) ||
        Is(attribute, AllowedExceptions) ||
        Is(attribute, EffectContract);

    internal ContractSelectionFeatures Select(
        IMethodSymbol method,
        bool hasContractClause = false,
        bool trusted = false) {
        var selected = ContractSelectionFeatures.None;
        if (hasContractClause ||
            method.Parameters.Any(parameter =>
                parameter.GetAttributes().Any(IsClosedContract)) ||
            method.GetReturnTypeAttributes().Any(IsClosedContract))
            selected |= ContractSelectionFeatures.Contracts;
        if (GetCallableAttributes(method).Any(IsEffectContract))
            selected |= ContractSelectionFeatures.Effects;
        return trusted
            ? ContractSelectionFeatures.Contracts |
              ContractSelectionFeatures.Effects
            : selected;
    }

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
        string name) =>
        compilation.GetTypeByMetadataName(
            "SharpProof.Attributes." + name + "Attribute");
}
