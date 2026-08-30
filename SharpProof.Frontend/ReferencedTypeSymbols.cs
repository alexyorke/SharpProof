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
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(container);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = pending.Pop();
            if (current is INamedTypeSymbol type)
            {
                yield return type;
            }

            if (current is INamespaceSymbol @namespace)
            {
                var namespaces = @namespace.GetNamespaceMembers()
                    .ToImmutableArray();
                for (var index = namespaces.Length - 1; index >= 0; index--)
                {
                    pending.Push(namespaces[index]);
                }
            }

            var types = current.GetTypeMembers();
            for (var index = types.Length - 1; index >= 0; index--)
            {
                pending.Push(types[index]);
            }
        }
    }
}
