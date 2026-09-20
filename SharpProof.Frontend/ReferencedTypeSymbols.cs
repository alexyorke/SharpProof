using System.Runtime.CompilerServices;

namespace SharpProof.Frontend;

internal static class ReferencedTypeSymbols
{
    private sealed class TypeSnapshot(
        ImmutableArray<INamedTypeSymbol> types)
    {
        internal ImmutableArray<INamedTypeSymbol> Types { get; } = types;
    }

    private static readonly ConditionalWeakTable<
        IAssemblySymbol,
        TypeSnapshot> CachedSnapshots = new();

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

    internal static IEnumerable<INamedTypeSymbol> GetAllCached(
        Compilation compilation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var assembly in GetAssemblies(compilation))
        {
            foreach (var type in GetAllCached(assembly, cancellationToken))
            {
                yield return type;
            }
        }
    }

    internal static IEnumerable<INamedTypeSymbol> GetAllCached(
        Compilation compilation,
        IAssemblySymbol referencedAssembly,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var assembly in GetAssemblies(compilation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferencesAssembly(assembly, referencedAssembly))
            {
                foreach (var type in GetAllCached(assembly, cancellationToken))
                {
                    yield return type;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetAllCached(
        IAssemblySymbol assembly,
        CancellationToken cancellationToken)
    {
        var snapshot = CachedSnapshots.GetValue(
            assembly,
            value => new TypeSnapshot(
                GetAll(value.GlobalNamespace, cancellationToken)
                    .ToImmutableArray()));
        foreach (var type in snapshot.Types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return type;
        }
    }

    private static IEnumerable<IAssemblySymbol> GetAssemblies(
        Compilation compilation)
    {
        yield return compilation.Assembly;
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            yield return assembly;
        }
    }

    private static bool ReferencesAssembly(
        IAssemblySymbol assembly,
        IAssemblySymbol referencedAssembly)
    {
        foreach (var module in assembly.Modules)
        {
            foreach (var reference in module.ReferencedAssemblySymbols)
            {
                if (SymbolEqualityComparer.Default.Equals(
                        reference,
                        referencedAssembly))
                {
                    return true;
                }
            }
        }
        return false;
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
