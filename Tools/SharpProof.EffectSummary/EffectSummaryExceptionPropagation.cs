internal static class EffectSummaryExceptionPropagation
{
    internal static List<MethodEffectSummary> AddTransitiveRootCandidates(
        MetadataReader reader,
        IReadOnlyList<MethodEffectSummary> summaries,
        int maxExceptionEdges,
        Func<MetadataReader, ExceptionPropagationSite, string, bool> exceptionEscapesPropagationSite)
    {
        var bySymbol = summaries
            .GroupBy(summary => summary.Identity)
            .ToDictionary(group => group.Key, group => group.First());

        var rootMemo = new Dictionary<StructuralMethodIdentity, string[]>();
        var rootVisiting = new HashSet<StructuralMethodIdentity>();
        var exceptionSccIndex = BuildExceptionPropagationSccIndex(bySymbol);
        var exceptionSccMemo = new Dictionary<
            int,
            IReadOnlyDictionary<StructuralMethodIdentity, ThrownExceptionTraversalResult>>();

        return summaries
            .Select(summary =>
            {
                var transitiveExceptionResult = VisitThrownExceptionEdges(
                    reader,
                    summary.Identity,
                    bySymbol,
                    exceptionSccIndex,
                    exceptionSccMemo,
                    maxExceptionEdges,
                    exceptionEscapesPropagationSite);
                var transitiveExceptionEdges = transitiveExceptionResult.Result;
                var transitiveExceptionSources = OrderExceptionSourcePaths(
                    transitiveExceptionEdges
                        .Select(edge => new ExceptionProvenance(
                            edge.ExceptionType,
                            edge.SourcePath,
                            edge.CallChain))
                        .DistinctBy(CreateExceptionSourcePathKey));
                return summary with
                {
                    TransitiveRootCandidates =
                    VisitRootCandidates(summary.Identity, bySymbol, rootMemo, rootVisiting).Result,
                    TransitiveThrownExceptionTypes = transitiveExceptionSources
                        .Select(source => source.ExceptionType)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(type => type, StringComparer.Ordinal)
                        .ToArray(),
                    TransitiveThrownExceptionProvenance = transitiveExceptionSources,
                    TransitiveThrownExceptionEdges = transitiveExceptionEdges,
                    TransitiveThrownExceptionEdgesTruncated = transitiveExceptionResult.IsTruncated
                };
            })
            .ToList();
    }

    private static (string[] Result, bool DependsOnCycle) VisitRootCandidates(
        StructuralMethodIdentity identity,
        IReadOnlyDictionary<StructuralMethodIdentity, MethodEffectSummary> bySymbol,
        Dictionary<StructuralMethodIdentity, string[]> memo,
        HashSet<StructuralMethodIdentity> visiting)
    {
        if (memo.TryGetValue(identity, out var cached)) return (cached, false);

        if (!bySymbol.TryGetValue(identity, out var summary))
            return (Array.Empty<string>(), false);

        var roots = new SortedSet<string>(summary.RootCandidates, StringComparer.Ordinal);
        if (!visiting.Add(identity)) return (roots.ToArray(), true);

        var dependsOnCycle = false;
        foreach (var callIdentity in summary.CallIdentities)
            if (bySymbol.ContainsKey(callIdentity))
            {
                var nestedResult = VisitRootCandidates(callIdentity, bySymbol, memo, visiting);
                roots.UnionWith(nestedResult.Result);
                dependsOnCycle |= nestedResult.DependsOnCycle;
            }

        visiting.Remove(identity);
        var result = roots.ToArray();
        if (!dependsOnCycle) memo[identity] = result;

        return (result, dependsOnCycle);
    }

    private static ThrownExceptionTraversalResult VisitThrownExceptionEdges(
        MetadataReader reader,
        StructuralMethodIdentity identity,
        IReadOnlyDictionary<StructuralMethodIdentity, MethodEffectSummary> bySymbol,
        ExceptionPropagationSccIndex sccIndex,
        Dictionary<int, IReadOnlyDictionary<StructuralMethodIdentity, ThrownExceptionTraversalResult>> componentMemo,
        int maxExceptionEdges,
        Func<MetadataReader, ExceptionPropagationSite, string, bool> exceptionEscapesPropagationSite)
    {
        if (!sccIndex.ComponentByIdentity.TryGetValue(identity, out var componentId))
            return new ThrownExceptionTraversalResult(
                Array.Empty<ThrownExceptionEdgeSummary>(),
                false,
                false);

        EnsureExceptionPropagationComponentResolved(
            reader,
            componentId,
            bySymbol,
            sccIndex,
            componentMemo,
            maxExceptionEdges,
            exceptionEscapesPropagationSite);
        return componentMemo[componentId][identity];
    }

    private static void EnsureExceptionPropagationComponentResolved(
        MetadataReader reader,
        int rootComponentId,
        IReadOnlyDictionary<StructuralMethodIdentity, MethodEffectSummary> bySymbol,
        ExceptionPropagationSccIndex sccIndex,
        Dictionary<int, IReadOnlyDictionary<StructuralMethodIdentity, ThrownExceptionTraversalResult>> componentMemo,
        int maxExceptionEdges,
        Func<MetadataReader, ExceptionPropagationSite, string, bool> exceptionEscapesPropagationSite)
    {
        var pending = new Stack<(int ComponentId, bool DependenciesVisited)>();
        pending.Push((rootComponentId, false));
        while (pending.Count != 0)
        {
            var (componentId, dependenciesVisited) = pending.Pop();
            if (componentMemo.ContainsKey(componentId)) continue;

            if (!dependenciesVisited)
            {
                pending.Push((componentId, true));
                foreach (var dependency in sccIndex.Dependencies[componentId].Reverse())
                    if (!componentMemo.ContainsKey(dependency))
                        pending.Push((dependency, false));
                continue;
            }

            componentMemo[componentId] = EvaluateExceptionPropagationComponent(
                reader,
                componentId,
                bySymbol,
                sccIndex,
                componentMemo,
                maxExceptionEdges,
                exceptionEscapesPropagationSite);
        }
    }

    private static IReadOnlyDictionary<StructuralMethodIdentity, ThrownExceptionTraversalResult>
        EvaluateExceptionPropagationComponent(
            MetadataReader reader,
            int componentId,
            IReadOnlyDictionary<StructuralMethodIdentity, MethodEffectSummary> bySymbol,
            ExceptionPropagationSccIndex sccIndex,
            IReadOnlyDictionary<int,
                IReadOnlyDictionary<StructuralMethodIdentity, ThrownExceptionTraversalResult>> componentMemo,
            int maxExceptionEdges,
            Func<MetadataReader, ExceptionPropagationSite, string, bool> exceptionEscapesPropagationSite)
    {
        var component = sccIndex.Components[componentId];
        var sourcesByIdentity = component.ToDictionary(
            static identity => identity,
            static _ => new Dictionary<string, ThrownExceptionEdgeSummary>(StringComparer.Ordinal));
        var truncatedByIdentity = component.ToDictionary(static identity => identity, static _ => false);
        var dependsOnCycle = component.Length > 1 ||
                             sccIndex.Graph[component[0]].Contains(component[0]);

        foreach (var identity in component)
        {
            var summary = bySymbol[identity];
            foreach (var directSource in summary.ThrownExceptionProvenance)
            {
                var directEdge = new ThrownExceptionEdgeSummary(
                    directSource.ExceptionType,
                    directSource.SourcePath,
                    directSource.CallChain,
                    null,
                    0);
                var isTruncated = truncatedByIdentity[identity];
                TryAddThrownExceptionEdge(
                    sourcesByIdentity[identity],
                    directEdge,
                    maxExceptionEdges,
                    ref isTruncated);
                truncatedByIdentity[identity] = isTruncated;
            }
        }

        foreach (var identity in component)
        {
            var summary = bySymbol[identity];
            foreach (var propagationSite in summary.ExceptionPropagationSites)
            {
                if (propagationSite.CalleeIdentity == null ||
                    !sccIndex.ComponentByIdentity.TryGetValue(
                        propagationSite.CalleeIdentity,
                        out var calleeComponentId) ||
                    calleeComponentId == componentId)
                    continue;

                AddPropagatedThrownExceptionEdges(
                    reader,
                    summary,
                    propagationSite,
                    componentMemo[calleeComponentId][propagationSite.CalleeIdentity],
                    sourcesByIdentity[identity],
                    truncatedByIdentity,
                    identity,
                    maxExceptionEdges,
                    exceptionEscapesPropagationSite);
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var identity in component)
            {
                var summary = bySymbol[identity];
                foreach (var propagationSite in summary.ExceptionPropagationSites)
                {
                    if (propagationSite.CalleeIdentity == null ||
                        !sccIndex.ComponentByIdentity.TryGetValue(
                            propagationSite.CalleeIdentity,
                            out var calleeComponentId) ||
                        calleeComponentId != componentId)
                        continue;

                    var calleeSources = sourcesByIdentity[propagationSite.CalleeIdentity];
                    var nestedResult = new ThrownExceptionTraversalResult(
                        OrderThrownExceptionEdges(calleeSources.Values),
                        true,
                        truncatedByIdentity[propagationSite.CalleeIdentity]);
                    var beforeCount = sourcesByIdentity[identity].Count;
                    AddPropagatedThrownExceptionEdges(
                        reader,
                        summary,
                        propagationSite,
                        nestedResult,
                        sourcesByIdentity[identity],
                        truncatedByIdentity,
                        identity,
                        maxExceptionEdges,
                        exceptionEscapesPropagationSite,
                        stopAtRepeatedIdentity: true);
                    changed |= sourcesByIdentity[identity].Count != beforeCount;
                }
            }
        }

        return component.ToDictionary(
            static identity => identity,
            identity => new ThrownExceptionTraversalResult(
                OrderThrownExceptionEdges(sourcesByIdentity[identity].Values),
                dependsOnCycle,
                truncatedByIdentity[identity]));
    }

    private static void AddPropagatedThrownExceptionEdges(
        MetadataReader reader,
        MethodEffectSummary summary,
        ExceptionPropagationSite propagationSite,
        ThrownExceptionTraversalResult nestedResult,
        Dictionary<string, ThrownExceptionEdgeSummary> thrownSources,
        Dictionary<StructuralMethodIdentity, bool> truncatedByIdentity,
        StructuralMethodIdentity identity,
        int maxExceptionEdges,
        Func<MetadataReader, ExceptionPropagationSite, string, bool> exceptionEscapesPropagationSite,
        bool stopAtRepeatedIdentity = false)
    {
        var isTruncated = truncatedByIdentity[identity] || nestedResult.IsTruncated;
        foreach (var nestedSource in nestedResult.Result)
        {
            if (!exceptionEscapesPropagationSite(reader, propagationSite, nestedSource.ExceptionType) ||
                stopAtRepeatedIdentity && nestedSource.CallChain.Contains(summary.Identity))
                continue;

            var callChain = new[] { summary.Identity }
                .Concat(nestedSource.CallChain)
                .ToArray();
            var edge = nestedSource.CalleeIdentity != null
                ? new ThrownExceptionEdgeSummary(
                    nestedSource.ExceptionType,
                    nestedSource.SourcePath,
                    callChain,
                    nestedSource.CalleeIdentity,
                    nestedSource.Depth + 1)
                : new ThrownExceptionEdgeSummary(
                    nestedSource.ExceptionType,
                    nestedSource.SourcePath,
                    callChain,
                    propagationSite.CalleeIdentity,
                    1);
            TryAddThrownExceptionEdge(
                thrownSources,
                edge,
                maxExceptionEdges,
                ref isTruncated);
        }

        truncatedByIdentity[identity] = isTruncated;
    }

    private static ExceptionPropagationSccIndex BuildExceptionPropagationSccIndex(
        IReadOnlyDictionary<StructuralMethodIdentity, MethodEffectSummary> bySymbol)
    {
        var graph = bySymbol.ToDictionary(
            static pair => pair.Key,
            pair => pair.Value.ExceptionPropagationSites
                .Select(static site => site.CalleeIdentity)
                .Where(identity => identity != null && bySymbol.ContainsKey(identity))
                .Select(static identity => identity!)
                .Distinct()
                .OrderBy(static identity => identity.ToCanonicalKey(), StringComparer.Ordinal)
                .ToArray());
        var components = ComputeExceptionPropagationSccsIteratively(graph);
        var componentByIdentity = new Dictionary<StructuralMethodIdentity, int>();
        for (var componentId = 0; componentId < components.Length; componentId++)
            foreach (var identity in components[componentId])
                componentByIdentity[identity] = componentId;

        var componentDependencies = new int[components.Length][];
        for (var componentId = 0; componentId < components.Length; componentId++)
            componentDependencies[componentId] = components[componentId]
                .SelectMany(identity => graph[identity])
                .Select(identity => componentByIdentity[identity])
                .Where(dependency => dependency != componentId)
                .Distinct()
                .OrderBy(static dependency => dependency)
                .ToArray();

        return new ExceptionPropagationSccIndex(
            graph,
            components,
            componentByIdentity,
            componentDependencies);
    }

    private static StructuralMethodIdentity[][] ComputeExceptionPropagationSccsIteratively(
        IReadOnlyDictionary<StructuralMethodIdentity, StructuralMethodIdentity[]> graph)
    {
        var nextIndex = 0;
        var indexes = new Dictionary<StructuralMethodIdentity, int>();
        var lowLinks = new Dictionary<StructuralMethodIdentity, int>();
        var componentStack = new Stack<StructuralMethodIdentity>();
        var onComponentStack = new HashSet<StructuralMethodIdentity>();
        var components = new List<StructuralMethodIdentity[]>();

        foreach (var root in graph.Keys.OrderBy(static identity => identity.ToCanonicalKey(), StringComparer.Ordinal))
        {
            if (indexes.ContainsKey(root)) continue;

            var traversal = new Stack<ExceptionPropagationTarjanFrame>();
            traversal.Push(new ExceptionPropagationTarjanFrame(root, null));
            while (traversal.Count != 0)
            {
                var frame = traversal.Peek();
                if (!frame.IsEntered)
                {
                    frame.IsEntered = true;
                    indexes[frame.Identity] = nextIndex;
                    lowLinks[frame.Identity] = nextIndex;
                    nextIndex++;
                    componentStack.Push(frame.Identity);
                    onComponentStack.Add(frame.Identity);
                }

                var neighbors = graph[frame.Identity];
                if (frame.NextNeighborIndex < neighbors.Length)
                {
                    var neighbor = neighbors[frame.NextNeighborIndex++];
                    if (!indexes.ContainsKey(neighbor))
                    {
                        traversal.Push(new ExceptionPropagationTarjanFrame(neighbor, frame.Identity));
                        continue;
                    }

                    if (onComponentStack.Contains(neighbor))
                        lowLinks[frame.Identity] = Math.Min(lowLinks[frame.Identity], indexes[neighbor]);
                    continue;
                }

                traversal.Pop();
                if (frame.Parent != null)
                    lowLinks[frame.Parent] = Math.Min(lowLinks[frame.Parent], lowLinks[frame.Identity]);
                if (lowLinks[frame.Identity] != indexes[frame.Identity]) continue;

                var component = new List<StructuralMethodIdentity>();
                StructuralMethodIdentity member;
                do
                {
                    member = componentStack.Pop();
                    onComponentStack.Remove(member);
                    component.Add(member);
                } while (!member.Equals(frame.Identity));

                components.Add(component
                    .OrderBy(static identity => identity.ToCanonicalKey(), StringComparer.Ordinal)
                    .ToArray());
            }
        }

        return components.ToArray();
    }

    private static void TryAddThrownExceptionEdge(
        Dictionary<string, ThrownExceptionEdgeSummary> thrownSources,
        ThrownExceptionEdgeSummary edge,
        int maxExceptionEdges,
        ref bool isTruncated)
    {
        var key = CreateThrownExceptionEdgeKey(edge);
        if (thrownSources.ContainsKey(key)) return;

        if (thrownSources.Count >= maxExceptionEdges)
        {
            isTruncated = true;
            return;
        }

        thrownSources[key] = edge;
    }

    private static string CreateExceptionSourcePathKey(ExceptionProvenance sourcePath)
    {
        return sourcePath.ExceptionType + "|" +
               (sourcePath.SourcePath ?? string.Empty) + "|" +
               string.Join(">", sourcePath.CallChain.Select(static identity => identity.ToCanonicalKey()));
    }

    private static ExceptionProvenance[] OrderExceptionSourcePaths(IEnumerable<ExceptionProvenance> sourcePaths)
    {
        return sourcePaths
            .OrderBy(sourcePath => sourcePath.ExceptionType, StringComparer.Ordinal)
            .ThenBy(sourcePath => sourcePath.SourcePath, StringComparer.Ordinal)
            .ThenBy(
                sourcePath => string.Join(
                    ">",
                    sourcePath.CallChain.Select(static identity => identity.ToCanonicalKey())),
                StringComparer.Ordinal)
            .ToArray();
    }

    private static string CreateThrownExceptionEdgeKey(ThrownExceptionEdgeSummary edge)
    {
        return edge.ExceptionType + "|" +
               (edge.SourcePath ?? string.Empty) + "|" +
               string.Join(">", edge.CallChain.Select(static identity => identity.ToCanonicalKey())) + "|" +
               (edge.CalleeIdentity?.ToCanonicalKey() ?? string.Empty) + "|" +
               edge.Depth;
    }

    private static ThrownExceptionEdgeSummary[] OrderThrownExceptionEdges(IEnumerable<ThrownExceptionEdgeSummary> edges)
    {
        return edges
            .OrderBy(edge => edge.ExceptionType, StringComparer.Ordinal)
            .ThenBy(edge => edge.SourcePath, StringComparer.Ordinal)
            .ThenBy(
                edge => string.Join(">", edge.CallChain.Select(static identity => identity.ToCanonicalKey())),
                StringComparer.Ordinal)
            .ThenBy(edge => edge.CalleeIdentity?.ToCanonicalKey(), StringComparer.Ordinal)
            .ThenBy(edge => edge.Depth)
            .ToArray();
    }
}
