using System.Runtime.CompilerServices;

namespace SharpProof.Effects;

/// <summary>
/// Resolves the existing compiler-bound trust declaration at every supported
/// scope. Trust is usable only when its constructor carries a nonblank reason.
/// </summary>
internal sealed class TrustedBoundaryPolicy
{
    private static readonly ConditionalWeakTable<Compilation, TrustedBoundaryPolicy>
        Policies = new();
    private readonly INamedTypeSymbol? _trustedAttribute;

    private TrustedBoundaryPolicy(Compilation compilation)
    {
        var identity = ContractApiIdentityResolver.ForCompilation(
            ArgumentNullGuard.NotNull(compilation, nameof(compilation)));
        _trustedAttribute = identity.ResolveAttribute(
            EffectContractMetadata.TrustedAttributeMetadataName);
    }

    internal static TrustedBoundaryPolicy ForCompilation(
        Compilation compilation)
    {
        return Policies.GetValue(
            ArgumentNullGuard.NotNull(compilation, nameof(compilation)),
            static value => new TrustedBoundaryPolicy(value));
    }

    internal bool AuthorizesDeclaredContracts(
        IMethodSymbol method)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));

        if (_trustedAttribute == null)
        {
            return false;
        }

        return EnumerateScopes(method)
            .SelectMany(static symbol => symbol.GetAttributes())
            .Any(attribute =>
                IsTrusted(attribute) &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string reason &&
                !string.IsNullOrWhiteSpace(reason));
    }

    private static IEnumerable<ISymbol> EnumerateScopes(
        IMethodSymbol method)
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

        for (var type = method.ContainingType; type != null;
             type = type.ContainingType)
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

    private bool IsTrusted(
        AttributeData attribute)
    {
        return SymbolEqualityComparer.Default.Equals(
            attribute.AttributeClass?.OriginalDefinition,
            _trustedAttribute?.OriginalDefinition);
    }
}
