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
            foreach (var target in nodes[method].Calls
                         .Select(static call => call.Target)
                         .Where(nodes.ContainsKey)
                         .Distinct<IMethodSymbol>(
                             SymbolEqualityComparer.Default)
                         .OrderBy(
                             static target => target,
                             EffectSymbolComparer<IMethodSymbol>.Instance))
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

        foreach (var method in nodes.Keys.OrderBy(
                     static method => method,
                     EffectSymbolComparer<IMethodSymbol>.Instance))
        {
            if (!states.ContainsKey(method))
            {
                Visit(method);
            }
        }

        return recursive;
    }
}
