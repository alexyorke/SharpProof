namespace SharpProof.Effects;

internal static class EffectCallGraph
{
    /// <summary>
    /// Ceiling on call-chain nesting walked recursively. Depth is bounded by the
    /// number of distinct methods, so only a pathologically long chain reaches
    /// this — but StackOverflowException is uncatchable, so the walk stops and
    /// over-approximates rather than risking it.
    /// </summary>
    internal const int MaximumCallGraphDepth = 512;

    internal static HashSet<IMethodSymbol> FindRecursiveMethods(
        IReadOnlyDictionary<IMethodSymbol, EffectMethodNode> nodes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = new Dictionary<IMethodSymbol, byte>(
            SymbolEqualityComparer.Default);
        var stack = new List<IMethodSymbol>();
        var recursive = new HashSet<IMethodSymbol>(
            SymbolEqualityComparer.Default);

        void Visit(IMethodSymbol method)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stack.Count >= MaximumCallGraphDepth)
            {
                // Stopping the descent hides any cycle below this point, so the
                // method is reported as recursive: callers then carry the
                // Recursion uncertainty instead of a summary we did not verify.
                states[method] = 2;
                recursive.Add(method);
                return;
            }

            states.Add(method, 1);
            stack.Add(method);
            foreach (var target in OrderMethods(
                         nodes[method].Calls.Select(
                             static call => call.Target),
                         nodes,
                         requireKnownNode: true,
                         cancellationToken))
            {
                if (!states.TryGetValue(target, out var state))
                {
                    Visit(target);
                }
                else if (state == 1)
                {
                    for (var index = stack.Count - 1;
                         index >= 0;
                         index--)
                    {
                        recursive.Add(stack[index]);
                        if (SymbolEqualityComparer.Default.Equals(
                                stack[index],
                                target))
                        {
                            break;
                        }
                    }
                }
            }
            stack.RemoveAt(stack.Count - 1);
            states[method] = 2;
        }

        foreach (var method in OrderMethods(
                     nodes.Keys,
                     nodes,
                     requireKnownNode: false,
                     cancellationToken))
        {
            if (!states.ContainsKey(method))
            {
                Visit(method);
            }
        }

        return recursive;
    }

    private static ImmutableArray<IMethodSymbol> OrderMethods(
        IEnumerable<IMethodSymbol> methods,
        IReadOnlyDictionary<IMethodSymbol, EffectMethodNode> nodes,
        bool requireKnownNode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var distinct = new HashSet<IMethodSymbol>(
            SymbolEqualityComparer.Default);
        var ordered = new List<IMethodSymbol>();
        using var enumerator = methods.GetEnumerator();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!enumerator.MoveNext())
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var method = enumerator.Current;
            cancellationToken.ThrowIfCancellationRequested();
            if ((!requireKnownNode || nodes.ContainsKey(method)) &&
                distinct.Add(method))
            {
                ordered.Add(method);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        ordered.Sort(new CancellationAwareMethodComparer(cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        return [.. ordered];
    }

    private sealed class CancellationAwareMethodComparer(
        CancellationToken cancellationToken) : IComparer<IMethodSymbol>
    {
        public int Compare(IMethodSymbol? left, IMethodSymbol? right)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = EffectSymbolComparer<IMethodSymbol>.Instance.Compare(
                left,
                right);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
    }
}
