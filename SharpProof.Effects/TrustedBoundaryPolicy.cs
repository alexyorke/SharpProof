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
        yield return method;
        if (method.AssociatedSymbol is IPropertySymbol property)
        {
            yield return property;
        }

        for (var type = method.ContainingType; type != null;
             type = type.ContainingType)
        {
            yield return type;
        }

        if (method.ContainingAssembly != null)
        {
            yield return method.ContainingAssembly;
        }
    }

    private bool IsTrusted(
        AttributeData attribute)
    {
        return SymbolEqualityComparer.Default.Equals(
            attribute.AttributeClass?.OriginalDefinition,
            _trustedAttribute?.OriginalDefinition);
    }
}
