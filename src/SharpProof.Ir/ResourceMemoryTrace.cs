using System.Numerics;

namespace SharpProof.Ir;

internal enum ResourceMemoryEventKind { Allocate, BindRoot, StoreReference, Escape }

// Object identities are fresh trace-local identities; zero denotes null.
// Slots are root identities for Allocate/BindRoot and field identities for StoreReference.
internal readonly struct ResourceMemoryEvent
{
    internal ResourceMemoryEventKind Kind { get; }
    internal int Object { get; }
    internal int Slot { get; }
    internal int Target { get; }
    internal BigInteger LowerBytes { get; }
    internal BigInteger UpperBytes { get; }

    internal ResourceMemoryEvent(ResourceMemoryEventKind kind, int identity, int slot, int target,
        BigInteger lowerBytes = default, BigInteger upperBytes = default)
    {
        Kind = kind;
        Object = identity;
        Slot = slot;
        Target = target;
        LowerBytes = lowerBytes;
        UpperBytes = upperBytes;
    }
}

internal readonly struct ResourceMemoryBounds
{
    internal BigInteger CumulativeLower { get; }
    internal BigInteger CumulativeUpper { get; }
    internal BigInteger PeakAuxiliaryLower { get; }
    internal BigInteger PeakAuxiliaryUpper { get; }
    internal BigInteger RetainedLower { get; }
    internal BigInteger RetainedUpper { get; }

    internal ResourceMemoryBounds(BigInteger cumulativeLower, BigInteger cumulativeUpper,
        BigInteger peakAuxiliaryLower, BigInteger peakAuxiliaryUpper, BigInteger retainedLower, BigInteger retainedUpper)
    {
        CumulativeLower = cumulativeLower;
        CumulativeUpper = cumulativeUpper;
        PeakAuxiliaryLower = peakAuxiliaryLower;
        PeakAuxiliaryUpper = peakAuxiliaryUpper;
        RetainedLower = retainedLower;
        RetainedUpper = retainedUpper;
    }
}

internal sealed class ResourceMemoryCheck
{
    internal ResourceMemoryBounds? Bounds { get; }
    internal string? UnknownReason { get; }
    internal bool Verified => Bounds.HasValue;

    internal ResourceMemoryCheck(ResourceMemoryBounds? bounds, string? unknownReason)
    {
        if (!bounds.HasValue ? string.IsNullOrWhiteSpace(unknownReason) : unknownReason != null)
        { throw new ArgumentException("A memory result requires bounds or an Unknown reason.", nameof(unknownReason)); }
        Bounds = bounds;
        UnknownReason = unknownReason;
    }
}

// Finite source-owned strong-reference traces, not GC timing or physical heap use.
// Final output objects are excluded from auxiliary storage throughout the trace.
// Source/IR ownership and the allocation layout must be checked by the consumer.
internal static class ResourceMemoryTrace
{
    internal const string ModelId = "finite-owned-strong-reachability-v1";
    internal const int MaximumEvents = 1024;
    private const int MaximumTraversalWork = 1_000_000;

    internal static ResourceMemoryCheck Evaluate(ImmutableArray<ResourceMemoryEvent> events, int output,
        CancellationToken cancellation)
    {
        RepositoryParameterGuard.RequireInitialized(events.IsDefault, nameof(events), "Events must be initialized.");
        cancellation.ThrowIfCancellationRequested();
        if (events.Length > MaximumEvents)
        { return new(null, "MemoryTraceBudgetExceeded"); }
        if (output < 0)
        { return new(null, "MalformedMemoryOutput"); }
        var sizes = new Dictionary<int, (BigInteger Lower, BigInteger Upper)>();
        var roots = new Dictionary<int, int>();
        var edges = new Dictionary<int, Dictionary<int, int>>();
        var snapshots = new List<HashSet<int>>();
        var live = new HashSet<int>();
        var work = MaximumTraversalWork;
        BigInteger cumulativeLower = 0, cumulativeUpper = 0;
        try
        {
            foreach (var item in events)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!Enum.IsDefined(typeof(ResourceMemoryEventKind), item.Kind))
                { return new(null, "MalformedMemoryEvent"); }
                if (item.Kind == ResourceMemoryEventKind.Escape)
                { return new(null, "MemoryEscapeUnsupported"); }
                if (item.Slot < 0 || item.Object < 0 || item.Target < 0 ||
                    item.Kind != ResourceMemoryEventKind.Allocate && (item.LowerBytes != 0 || item.UpperBytes != 0))
                { return new(null, "MalformedMemoryEvent"); }
                switch (item.Kind)
                {
                    case ResourceMemoryEventKind.Allocate:
                        if (item.Object == 0 || sizes.ContainsKey(item.Object) || item.Target != 0 ||
                            item.LowerBytes < 0 || item.UpperBytes < item.LowerBytes || item.UpperBytes.ToByteArray().Length > 1024)
                        { return new(null, "MalformedMemoryAllocation"); }
                        sizes.Add(item.Object, (item.LowerBytes, item.UpperBytes));
                        edges.Add(item.Object, []);
                        // Allocation occurs before the destination root is overwritten.
                        snapshots.Add(Reach(roots.Values.Concat([item.Object]), edges, ref work, cancellation));
                        roots[item.Slot] = item.Object;
                        cumulativeLower += item.LowerBytes;
                        cumulativeUpper += item.UpperBytes;
                        break;
                    case ResourceMemoryEventKind.BindRoot:
                        if (item.Object != 0 || item.Target != 0 && !live.Contains(item.Target))
                        { return new(null, "UnownedMemoryReference"); }
                        roots[item.Slot] = item.Target;
                        break;
                    case ResourceMemoryEventKind.StoreReference:
                        if (!live.Contains(item.Object) || item.Target != 0 && !live.Contains(item.Target))
                        { return new(null, "UnownedMemoryReference"); }
                        edges[item.Object][item.Slot] = item.Target;
                        break;
                }
                live = Reach(roots.Values, edges, ref work, cancellation);
                snapshots.Add(live);
            }
            if (output != 0 && !live.Contains(output))
            { return new(null, "UnownedMemoryOutput"); }
            var retained = Reach([output], edges, ref work, cancellation);
            var retainedSize = Sum(retained, sizes, ref work, cancellation);
            BigInteger peakLower = 0, peakUpper = 0;
            foreach (var snapshot in snapshots)
            {
                cancellation.ThrowIfCancellationRequested();
                snapshot.ExceptWith(retained);
                var size = Sum(snapshot, sizes, ref work, cancellation);
                peakLower = BigInteger.Max(peakLower, size.Lower);
                peakUpper = BigInteger.Max(peakUpper, size.Upper);
            }
            return new(new(cumulativeLower, cumulativeUpper, peakLower, peakUpper, retainedSize.Lower, retainedSize.Upper), null);
        }
        catch (ResourceExpressionLimitException)
        { return new(null, "MemoryTraceBudgetExceeded"); }
    }

    private static HashSet<int> Reach(IEnumerable<int> roots, Dictionary<int, Dictionary<int, int>> edges,
        ref int work, CancellationToken cancellation)
    {
        roots = ArgumentNullGuard.NotNull(roots, nameof(roots));
        edges = ArgumentNullGuard.NotNull(edges, nameof(edges));
        var pending = new Stack<int>(roots);
        var reached = new HashSet<int>();
        while (pending.Count != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (--work < 0)
            { throw new ResourceExpressionLimitException(); }
            var current = pending.Pop();
            if (current == 0 || !reached.Add(current))
            { continue; }
            foreach (var target in edges[current].Values)
            { pending.Push(target); }
        }
        return reached;
    }

    private static (BigInteger Lower, BigInteger Upper) Sum(HashSet<int> objects,
        Dictionary<int, (BigInteger Lower, BigInteger Upper)> sizes, ref int work, CancellationToken cancellation)
    {
        objects = ArgumentNullGuard.NotNull(objects, nameof(objects));
        sizes = ArgumentNullGuard.NotNull(sizes, nameof(sizes));
        BigInteger lower = 0, upper = 0;
        foreach (var identity in objects)
        {
            cancellation.ThrowIfCancellationRequested();
            if (--work < 0)
            { throw new ResourceExpressionLimitException(); }
            lower += sizes[identity].Lower;
            upper += sizes[identity].Upper;
        }
        return (lower, upper);
    }
}
