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

    private readonly ContractApiIdentityResolver _identity;

    private ContractSelectionInventory(Compilation compilation)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));

        _identity = ContractApiIdentityResolver.ForCompilation(compilation);
        ContractFor = _identity.ResolveAttribute(ContractApiMetadata.ContractFor);
        EnforcePure = _identity.ResolveAttribute(ContractApiMetadata.EnforcePure);
        ZeroAllocations = _identity.ResolveAttribute(ContractApiMetadata.ZeroAllocations);
        ResourceUpperBound = _identity.ResolveAttribute(ContractApiMetadata.ResourceUpperBound);
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
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        return Cache.GetValue(compilation, static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (new(value)));
    }

    internal INamedTypeSymbol? ContractFor
    {
        get;
    }

    internal bool IsContractForCandidate(AttributeData attribute)
    {
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
        return Is(attribute, ContractFor) ||
            _identity.TryGetRejectedAttributeMetadataName(
                attribute,
                out var metadataName) &&
            metadataName == ContractApiMetadata.ContractFor;
    }
    internal INamedTypeSymbol? EnforcePure
    {
        get;
    }
    internal INamedTypeSymbol? ZeroAllocations
    {
        get;
    }
    internal INamedTypeSymbol? ResourceUpperBound { get; }
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
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
        return GetClosedContractKind(attribute) !=
            ClosedContractAttributeKind.None;
    }

    internal ClosedContractAttributeKind GetClosedContractKind(
        AttributeData attribute)
    {
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
        return GetClosedContractClassification(attribute)?.Kind ?? ClosedContractAttributeKind.None;
    }

    internal ClosedContractAttributeClassification? GetClosedContractClassification(
        AttributeData attribute)
    {
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
        return Is(attribute, NotNull)
            ? ClosedContractAttributeClassification.NotNull
            : Is(attribute, Positive)
                ? ClosedContractAttributeClassification.Positive
                : Is(attribute, InRange)
                    ? ClosedContractAttributeClassification.InRange
                    : null;
    }

    internal bool IsRejectedClosedContract(AttributeData attribute)
    {
        if (!_identity.TryGetRejectedAttributeMetadataName(
                attribute,
                out var metadataName))
        {
            return false;
        }

        return metadataName is
            ContractApiMetadata.NotNull or
            ContractApiMetadata.Positive or
            ContractApiMetadata.InRange;
    }

    internal bool IsEffectContract(AttributeData attribute)
    {
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
        return Is(attribute, EnforcePure) ||
        Is(attribute, ZeroAllocations) ||
        Is(attribute, ResourceUpperBound) ||
        Is(attribute, AllowedCapabilities) ||
        Is(attribute, DoesNotThrow) ||
        Is(attribute, AllowedExceptions) ||
        Is(attribute, EffectContract);
    }

    internal ContractSelectionFeatures Select(
        IMethodSymbol method,
        bool hasContractClause = false)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        var selected = ContractSelectionFeatures.None;
        if (hasContractClause ||
            method.Parameters.Any(parameter =>
                parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.GetAttributes().Any(IsClosedContract))) ||
            method.GetReturnTypeAttributes().Any(IsClosedContract))
        {
            selected |= ContractSelectionFeatures.Contracts;
        }

        var callableAttributes = GetCallableAttributes(method).ToImmutableArray();
        if (callableAttributes.Any(IsEffectContract))
        {
            selected |= ContractSelectionFeatures.Effects;
        }

        return selected | GetRejectedSelectionFeatures(method, callableAttributes);
    }

    internal ContractSelectionFeatures GetRejectedSelectionFeatures(
        IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return GetRejectedSelectionFeatures(
            method,
            out _);
    }

    internal ContractSelectionFeatures GetRejectedSelectionFeatures(
        IMethodSymbol method,
        out ContractSelectionFeatures callable)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        callable = GetRejectedCallableSelectionFeatures(method);
        return callable | GetRejectedControlFeatures(method);
    }

    private ContractSelectionFeatures GetRejectedSelectionFeatures(
        IMethodSymbol method,
        ImmutableArray<AttributeData> callableAttributes)
    {

        RepositoryParameterGuard.RequireInitialized(callableAttributes.IsDefault, nameof(callableAttributes), "Array must be initialized.");
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        var selected = GetRejectedCallableSelectionFeatures(
            method,
            callableAttributes);
        return selected | GetRejectedControlFeatures(method);
    }

    private ContractSelectionFeatures GetRejectedControlFeatures(
        IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        var selected = ContractSelectionFeatures.None;
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
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return GetRejectedCallableSelectionFeatures(
            method,
            GetCallableAttributes(method));
    }

    private ContractSelectionFeatures GetRejectedCallableSelectionFeatures(
        IMethodSymbol method,
        IEnumerable<AttributeData> callableAttributes)
    {
        callableAttributes = RepositoryParameterGuard.NotNull(callableAttributes, nameof(callableAttributes));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        var selected = ContractSelectionFeatures.None;
        foreach (var attribute in callableAttributes)
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
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
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

        RepositoryParameterGuard.RequireInitialized(attributes.IsDefault, nameof(attributes), "Array must be initialized.");
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
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
        return ContractApiMetadata.IsAttribute(attribute, expected);
    }

    internal static bool? GetControlSelection(
        INamedTypeSymbol? attributeType,
        INamedTypeSymbol? suppress,
        INamedTypeSymbol? trusted)
    {
        var normalized = attributeType?.OriginalDefinition;
        if (normalized is null)
        {
            return null;
        }

        return SymbolEqualityComparer.Default.Equals(
                normalized,
                suppress?.OriginalDefinition)
            ? true
            : SymbolEqualityComparer.Default.Equals(
                normalized,
                trusted?.OriginalDefinition)
                ? false
                : null;
    }

    internal static IEnumerable<AttributeData> GetCallableAttributes(
        IMethodSymbol method)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
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
