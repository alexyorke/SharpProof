namespace SharpProof.CompilerArtifact;

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
        var argumentAssemblies = new List<string>();
        AddNamedTypeArgumentAssemblies(type, argumentAssemblies);
        if (argumentAssemblies.Count == 0)
        {
            return identity;
        }

        return identity + "::generic-argument-assemblies[" +
            string.Concat(argumentAssemblies.Select(static assembly =>
                assembly.Length.ToString(CultureInfo.InvariantCulture) +
                ":" + assembly)) + "]";
    }

    internal static string[] EncodeHierarchy(INamedTypeSymbol? type)
    {
        var identities = new List<string>();
        for (var current = type; current != null; current = current.BaseType)
        {
            identities.Add(Encode(current));
        }

        return [.. identities.OrderBy(static value => value, StringComparer.Ordinal)];
    }

    private static void AddNamedTypeArgumentAssemblies(
        INamedTypeSymbol type,
        ICollection<string> identities)
    {
        if (type.ContainingType is { } containingType)
        {
            AddNamedTypeArgumentAssemblies(containingType, identities);
        }

        foreach (var argument in type.TypeArguments)
        {
            AddTypeAssemblyIdentities(argument, identities);
        }
    }

    private static void AddTypeAssemblyIdentities(
        ITypeSymbol type,
        ICollection<string> identities)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                AddTypeAssemblyIdentities(array.ElementType, identities);
                break;
            case IPointerTypeSymbol pointer:
                AddTypeAssemblyIdentities(pointer.PointedAtType, identities);
                break;
            case INamedTypeSymbol named:
                if (named.ContainingAssembly is { } assembly)
                {
                    identities.Add(assembly.Identity.ToString());
                }
                AddNamedTypeArgumentAssemblies(named, identities);
                break;
        }
    }
}
