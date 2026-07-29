namespace SharpProof.Contracts;

[Flags]
internal enum ContractSelectionFeatures
{
    None = 0,
    Contracts = 1,
    Effects = 2
}

internal sealed class ContractSelectionInventory
{
    private static readonly ConditionalWeakTable<
        Compilation, ContractSelectionInventory> Cache = new();

    internal const string ContractForMetadataName =
        ContractApiMetadata.ContractFor;

    private ContractSelectionInventory(Compilation compilation)
    {
        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        ContractFor = compilation.GetTypeByMetadataName(
            ContractApiMetadata.ContractFor);
        EnforcePure = Resolve(compilation, ContractApiMetadata.EnforcePure);
        ZeroAllocations = Resolve(compilation, ContractApiMetadata.ZeroAllocations);
        AllowedCapabilities = Resolve(compilation, ContractApiMetadata.AllowedCapabilities);
        DoesNotThrow = Resolve(compilation, ContractApiMetadata.DoesNotThrow);
        AllowedExceptions = Resolve(compilation, ContractApiMetadata.AllowedExceptions);
        EffectContract = Resolve(compilation, ContractApiMetadata.EffectContract);
        NotNull = Resolve(compilation, ContractApiMetadata.NotNull);
        Positive = Resolve(compilation, ContractApiMetadata.Positive);
        InRange = Resolve(compilation, ContractApiMetadata.InRange);
        Suppress = Resolve(compilation, ContractApiMetadata.Suppress);
        Trusted = Resolve(compilation, ContractApiMetadata.Trusted);
    }

    internal static ContractSelectionInventory ForCompilation(
        Compilation compilation)
    {
        return Cache.GetValue(compilation, static value => new(value));
    }

    internal INamedTypeSymbol? ContractFor
    {
        get;
    }
    internal INamedTypeSymbol? EnforcePure
    {
        get;
    }
    internal INamedTypeSymbol? ZeroAllocations
    {
        get;
    }
    internal INamedTypeSymbol? AllowedCapabilities
    {
        get;
    }
    internal INamedTypeSymbol? DoesNotThrow
    {
        get;
    }
    internal INamedTypeSymbol? AllowedExceptions
    {
        get;
    }
    internal INamedTypeSymbol? EffectContract
    {
        get;
    }
    internal INamedTypeSymbol? NotNull
    {
        get;
    }
    internal INamedTypeSymbol? Positive
    {
        get;
    }
    internal INamedTypeSymbol? InRange
    {
        get;
    }
    internal INamedTypeSymbol? Suppress
    {
        get;
    }
    internal INamedTypeSymbol? Trusted
    {
        get;
    }

    internal bool IsClosedContract(AttributeData attribute)
    {
        return Is(attribute, NotNull) ||
        Is(attribute, Positive) ||
        Is(attribute, InRange);
    }

    internal bool IsEffectContract(AttributeData attribute)
    {
        return Is(attribute, EnforcePure) ||
        Is(attribute, ZeroAllocations) ||
        Is(attribute, AllowedCapabilities) ||
        Is(attribute, DoesNotThrow) ||
        Is(attribute, AllowedExceptions) ||
        Is(attribute, EffectContract);
    }

    internal ContractSelectionFeatures Select(
        IMethodSymbol method,
        bool hasContractClause = false,
        bool trusted = false)
    {
        var selected = ContractSelectionFeatures.None;
        if (hasContractClause ||
            method.Parameters.Any(parameter =>
                parameter.GetAttributes().Any(IsClosedContract)) ||
            method.GetReturnTypeAttributes().Any(IsClosedContract))
        {
            selected |= ContractSelectionFeatures.Contracts;
        }

        if (GetCallableAttributes(method).Any(IsEffectContract))
        {
            selected |= ContractSelectionFeatures.Effects;
        }

        return trusted
            ? ContractSelectionFeatures.Contracts |
              ContractSelectionFeatures.Effects
            : selected;
    }

    internal static bool Is(
        AttributeData attribute,
        INamedTypeSymbol? expected)
    {
        return expected != null &&
        SymbolEqualityComparer.Default.Equals(
            attribute.AttributeClass?.OriginalDefinition,
            expected.OriginalDefinition);
    }

    internal static IEnumerable<AttributeData> GetCallableAttributes(
        IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            yield return attribute;
        }

        if (method.AssociatedSymbol is IPropertySymbol property)
        {
            foreach (var attribute in property.GetAttributes())
            {
                yield return attribute;
            }
        }
    }

    private static INamedTypeSymbol? Resolve(
        Compilation compilation,
        string metadataName)
    {
        return compilation.GetTypeByMetadataName(metadataName);
    }
}
