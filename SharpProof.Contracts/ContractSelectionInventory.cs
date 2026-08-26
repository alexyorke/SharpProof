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

    internal bool IsContractForCandidate(AttributeData attribute)
    {
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
        foreach (var scope in EnumerateScopes(method))
        {
            selected |= GetRejectedControlFeatures(scope.GetAttributes());
        }

        return selected;
    }

    internal static IEnumerable<ISymbol> EnumerateScopes(IMethodSymbol method)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        if (seen.Add(method))
        {
            yield return method;
        }

        if (method.AssociatedSymbol is { } associated &&
            seen.Add(associated))
        {
            yield return associated;
        }

        for (var type = method.ContainingType; type != null; type = type.ContainingType)
        {
            if (seen.Add(type))
            {
                yield return type;
            }
        }

        if (method.ContainingType is { } containingType)
        {
            foreach (var interfaceType in containingType.AllInterfaces)
            {
                foreach (var member in interfaceType.GetMembers())
                {
                    if (member is not (IMethodSymbol or IPropertySymbol or IEventSymbol) ||
                        !IsImplementedBy(method, method.AssociatedSymbol, containingType, member))
                    {
                        continue;
                    }

                    if (seen.Add(interfaceType))
                    {
                        yield return interfaceType;
                    }

                    if (seen.Add(member))
                    {
                        yield return member;
                    }
                }
            }
        }

        if (method.ContainingAssembly is { } assembly &&
            seen.Add(assembly))
        {
            yield return assembly;
        }
    }

    private static bool IsImplementedBy(
        IMethodSymbol method,
        ISymbol? associated,
        INamedTypeSymbol containingType,
        ISymbol interfaceMember)
    {
        var implementation = containingType.FindImplementationForInterfaceMember(
            interfaceMember);
        if (implementation == null)
        {
            return false;
        }

        if (implementation is IMethodSymbol implementationMethod)
        {
            for (var candidate = method;
                 candidate != null;
                 candidate = candidate.OverriddenMethod)
            {
                if (SymbolEqualityComparer.Default.Equals(
                        implementationMethod,
                        candidate))
                {
                    return true;
                }
            }
        }

        if (associated is IPropertySymbol property &&
            implementation is IPropertySymbol implementationProperty)
        {
            for (var candidate = property;
                 candidate != null;
                 candidate = candidate.OverriddenProperty)
            {
                if (SymbolEqualityComparer.Default.Equals(
                        implementationProperty,
                        candidate))
                {
                    return true;
                }
            }
        }

        if (associated is IEventSymbol @event &&
            implementation is IEventSymbol implementationEvent)
        {
            for (var candidate = @event;
                 candidate != null;
                 candidate = candidate.OverriddenEvent)
            {
                if (SymbolEqualityComparer.Default.Equals(
                        implementationEvent,
                        candidate))
                {
                    return true;
                }
            }
        }

        return false;
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
