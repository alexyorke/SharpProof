namespace SharpProof.Compiler.Semantics;

internal static class CompilerExceptionTypeIdentity
{
    internal static string Encode(INamedTypeSymbol type)
    {
        type = ArgumentNullGuard.NotNull(type, nameof(type));

        if (DocumentationCommentId.CreateReferenceId(type) is not { Length: > 0 })
        {
            throw new InvalidOperationException(
                "An exception type does not have a reference documentation ID.");
        }

        var identity = CompilerIdentityBridge.CreateTypeDisplay(type);
        var argumentAssemblies = AddNamedTypeArgumentAssemblies(type, ImmutableList<string>.Empty);
        if (argumentAssemblies.Count == 0)
        {
            return identity;
        }

        return identity + "::generic-argument-assemblies[" +
            string.Concat(argumentAssemblies.Select(static assembly =>
                assembly is null ? throw new System.ArgumentNullException(nameof(assembly)) : (assembly.Length.ToString(CultureInfo.InvariantCulture) +
                ":" + assembly))) + "]";
    }

    internal static ImmutableArray<string> EncodeHierarchy(INamedTypeSymbol? type)
    {
        var identities = ImmutableList<string>.Empty;
        for (var current = type; current != null; current = current.BaseType)
        {
            identities = identities.Add(Encode(current));
        }

        return [.. identities.OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value), StringComparer.Ordinal)];
    }

    private static ImmutableList<string> AddNamedTypeArgumentAssemblies(
        INamedTypeSymbol type,
        ImmutableList<string> identities)
    {
        identities = RepositoryParameterGuard.NotNull(identities, nameof(identities));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        if (type.ContainingType is { } containingType)
        {
            identities = AddNamedTypeArgumentAssemblies(containingType, identities);
        }

        foreach (var argument in type.TypeArguments)
        {
            identities = AddTypeAssemblyIdentities(argument, identities);
        }
        return identities;
    }

    private static ImmutableList<string> AddTypeAssemblyIdentities(
        ITypeSymbol type,
        ImmutableList<string> identities)
    {
        identities = RepositoryParameterGuard.NotNull(identities, nameof(identities));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        switch (type)
        {
            case IArrayTypeSymbol array:
                identities = AddTypeAssemblyIdentities(array.ElementType, identities);
                break;
            case IPointerTypeSymbol pointer:
                identities = AddTypeAssemblyIdentities(pointer.PointedAtType, identities);
                break;
            case INamedTypeSymbol named:
                if (named.ContainingAssembly is { } assembly)
                {
                    identities = identities.Add(assembly.Identity.ToString());
                }
                identities = AddNamedTypeArgumentAssemblies(named, identities);
                break;
        }
        return identities;
    }
}
