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
    private readonly ContractApiIdentityResolver _identity;

    private ContractSelectionInventory(Compilation compilation)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));

        _identity = ContractApiIdentityResolver.ForCompilation(compilation);
        ContractFor = _identity.ResolveAttribute(ContractApiMetadata.ContractFor);
        EnforcePure = _identity.ResolveAttribute(ContractApiMetadata.EnforcePure);
        ZeroAllocations = _identity.ResolveAttribute(ContractApiMetadata.ZeroAllocations);
        AllowedCapabilities = _identity.ResolveAttribute(ContractApiMetadata.AllowedCapabilities);
        DoesNotThrow = _identity.ResolveAttribute(ContractApiMetadata.DoesNotThrow);
        AllowedExceptions = _identity.ResolveAttribute(ContractApiMetadata.AllowedExceptions);
        EffectContract = _identity.ResolveAttribute(ContractApiMetadata.EffectContract);
        NotNull = _identity.ResolveAttribute(ContractApiMetadata.NotNull);
        Positive = _identity.ResolveAttribute(ContractApiMetadata.Positive);
        InRange = _identity.ResolveAttribute(ContractApiMetadata.InRange);
        Suppress = _identity.ResolveAttribute(ContractApiMetadata.Suppress);
        Trusted = _identity.ResolveAttribute(ContractApiMetadata.Trusted);
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

        if (trusted)
        {
            selected |= ContractSelectionFeatures.Contracts |
                ContractSelectionFeatures.Effects;
        }

        return selected | GetRejectedSelectionFeatures(method);
    }

    internal ContractSelectionFeatures GetRejectedSelectionFeatures(
        IMethodSymbol method)
    {
        var selected = GetRejectedCallableSelectionFeatures(method);
        for (var type = method.ContainingType;
             type != null;
             type = type.ContainingType)
        {
            selected |= GetRejectedControlFeatures(type.GetAttributes());
        }

        selected |= GetRejectedControlFeatures(
            method.ContainingAssembly.GetAttributes());
        return selected;
    }

    internal ContractSelectionFeatures GetRejectedCallableSelectionFeatures(
        IMethodSymbol method)
    {
        var selected = ContractSelectionFeatures.None;
        foreach (var attribute in GetCallableAttributes(method))
        {
            selected |= GetRejectedFeature(attribute);
        }

        foreach (var parameter in method.Parameters)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                selected |= GetRejectedFeature(attribute);
            }
        }

        foreach (var attribute in method.GetReturnTypeAttributes())
        {
            selected |= GetRejectedFeature(attribute);
        }
        return selected;
    }

    internal bool IsRejectedControlAttribute(AttributeData attribute)
    {
        return _identity.TryGetRejectedAttributeMetadataName(
                attribute,
                out var metadataName) &&
            metadataName is
                ContractApiMetadata.Trusted or
                ContractApiMetadata.Suppress;
    }

    private ContractSelectionFeatures GetRejectedControlFeatures(
        ImmutableArray<AttributeData> attributes)
    {
        var selected = ContractSelectionFeatures.None;
        foreach (var attribute in attributes)
        {
            if (IsRejectedControlAttribute(attribute))
            {
                selected |= ContractSelectionFeatures.Contracts |
                    ContractSelectionFeatures.Effects;
            }
        }

        return selected;
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

    private ContractSelectionFeatures GetRejectedFeature(
        AttributeData attribute)
    {
        if (!_identity.TryGetRejectedAttributeMetadataName(
                attribute,
                out var metadataName))
        {
            return ContractSelectionFeatures.None;
        }

        return ContractApiMetadata.TryGetAttribute(
                metadataName,
                out var descriptor) ?
            (ContractSelectionFeatures)(int)descriptor.Selection :
            ContractSelectionFeatures.Contracts;
    }
}
