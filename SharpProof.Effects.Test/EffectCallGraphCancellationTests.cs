namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectCallGraphCancellationTests
{
    [Test]
    public void CanceledGraphStopsBeforeEnumeratingNodesForSorting()
    {
        var methods = Methods();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var nodes = new CancellationTrapGraph(
            EmptyNodes(methods),
            cancellation,
            throwWhenKeysAreRead: true);

        Assert.Throws<OperationCanceledException>((Action)(() =>
            EffectCallGraph.FindRecursiveMethods(
                nodes,
                cancellation.Token)));
    }

    [Test]
    public void CancellationBeforeEdgeSortStopsBeforeEnumeratingEdges()
    {
        var methods = Methods();
        using var cancellation = new CancellationTokenSource();
        var calls = methods.Skip(1)
            .Select(static target => new EffectCallSite(
                target,
                EffectRegionSet.Empty,
                EffectRegionSet.Empty,
                [],
                null!))
            .ToImmutableArray();
        var entries = EmptyNodes(methods);
        entries[methods[0]] = new EffectMethodNode(
            EffectSummary.Empty,
            calls,
            []);
        var nodes = new CancellationTrapGraph(
            entries,
            cancellation,
            cancelWhenRead: methods[0],
            throwWhenContainsFollowsCancellation: true);

        Assert.Throws<OperationCanceledException>((Action)(() =>
            EffectCallGraph.FindRecursiveMethods(
                nodes,
                cancellation.Token)));
    }

    private static IMethodSymbol[] Methods()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void A_Root() { }
                public static void B_Target() { }
                public static void C_Target() { }
            }
            """);
        return [.. EffectTestHost.RequireType(compilation, "Sample")
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static method => method.MethodKind == MethodKind.Ordinary)
            .OrderBy(static method => method.Name, StringComparer.Ordinal)];
    }

    private static Dictionary<IMethodSymbol, EffectMethodNode> EmptyNodes(
        IEnumerable<IMethodSymbol> methods)
    {
        var result = new Dictionary<IMethodSymbol, EffectMethodNode>(
            SymbolEqualityComparer.Default);
        foreach (var method in methods)
        {
            result.Add(method, new EffectMethodNode(
                EffectSummary.Empty,
                [],
                []));
        }

        return result;
    }

    private sealed class CancellationTrapGraph(
        Dictionary<IMethodSymbol, EffectMethodNode> entries,
        CancellationTokenSource cancellation,
        bool throwWhenKeysAreRead = false,
        IMethodSymbol? cancelWhenRead = null,
        bool throwWhenContainsFollowsCancellation = false)
        : IReadOnlyDictionary<IMethodSymbol, EffectMethodNode>
    {
        public EffectMethodNode this[IMethodSymbol key]
        {
            get
            {
                if (SymbolEqualityComparer.Default.Equals(
                        key,
                        cancelWhenRead))
                {
                    cancellation.Cancel();
                }

                return entries[key];
            }
        }

        public IEnumerable<IMethodSymbol> Keys =>
            throwWhenKeysAreRead
                ? throw new AssertionException(
                    "Canceled node ordering enumerated graph keys.")
                : entries.Keys;

        public IEnumerable<EffectMethodNode> Values => entries.Values;

        public int Count => entries.Count;

        public bool ContainsKey(IMethodSymbol key)
        {
            if (throwWhenContainsFollowsCancellation &&
                cancellation.IsCancellationRequested)
            {
                throw new AssertionException(
                    "Canceled edge ordering continued enumerating targets.");
            }

            return entries.ContainsKey(key);
        }

        public IEnumerator<KeyValuePair<IMethodSymbol, EffectMethodNode>>
            GetEnumerator()
        {
            return entries.GetEnumerator();
        }

        public bool TryGetValue(
            IMethodSymbol key,
            out EffectMethodNode value)
        {
            return entries.TryGetValue(key, out value);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable
            .GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
