namespace SharpProof.Frontend;

internal static class ReferencedTypeSymbols
{
    internal static IEnumerable<INamedTypeSymbol> GetAll(
        Compilation compilation,
        CancellationToken cancellationToken = default)
    {
        foreach (var type in GetAll(
                     compilation.Assembly.GlobalNamespace,
                     cancellationToken))
        {
            yield return type;
        }

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            foreach (var type in GetAll(
                         assembly.GlobalNamespace,
                         cancellationToken))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetAll(
        INamespaceOrTypeSymbol container,
        CancellationToken cancellationToken)
    {
        foreach (var type in container.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return type;
            foreach (var nested in GetAll(type, cancellationToken))
            {
                yield return nested;
            }
        }

        if (container is not INamespaceSymbol @namespace)
        {
            yield break;
        }

        foreach (var child in @namespace.GetNamespaceMembers())
        {
            foreach (var type in GetAll(child, cancellationToken))
            {
                yield return type;
            }
        }
    }
}
