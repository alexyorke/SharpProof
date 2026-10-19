using System.Collections.Immutable;
namespace SharpProof.Dataflow.Test;

[TestFixture]
public sealed class FiniteCfgConcreteOracleTests
{
    [Test]
    public void GeneratedNullnessFixpointsMatchConcreteReachability()
    {
        const int seed = 0x4C7F;
        var random = new Random(seed);
        for (var caseIndex = 0; caseIndex < 256; caseIndex++)
        {
            var blockCount = random.Next(2, 8);
            var transfers = Enumerable.Range(0, blockCount)
                .Select(_ => (TransferKind)random.Next(4))
                .ToArray();
            var blocks = transfers
                .Select((transfer, blockId) =>
                    new DataflowBlock<NullnessValue>(
                        blockId,
                        value => ApplyAbstract(transfer, value)))
                .ToArray();
            var edges = ImmutableHashSet.CreateBuilder<DataflowEdge>();
            for (var blockId = 1; blockId < blockCount; blockId++)
            {
                edges.Add(new DataflowEdge(blockId - 1, blockId));
            }

            for (var source = 0; source < blockCount; source++)
            {
                for (var target = 0; target < blockCount; target++)
                {
                    if (random.Next(5) == 0)
                    {
                        edges.Add(new DataflowEdge(source, target));
                    }
                }
            }

            var graph = new DataflowGraph<NullnessValue>(blocks, edges.ToImmutable());

            var abstractResult = ForwardDataflowAnalysis.Analyze(
                graph,
                NullnessDomain.Instance,
                NullnessValue.MaybeNull);
            var concreteResult = ExecuteConcreteFixpoint(
                graph,
                transfers);

            for (var blockId = 0; blockId < blockCount; blockId++)
            {
                Assert.That(
                    ToConcrete(abstractResult.GetInputState(blockId)),
                    Is.EqualTo(concreteResult.Inputs[blockId]),
                    $"Input mismatch for seed {seed}, case {caseIndex}, " +
                    $"block {blockId}.");
                Assert.That(
                    ToConcrete(abstractResult.GetOutputState(blockId)),
                    Is.EqualTo(concreteResult.Outputs[blockId]),
                    $"Output mismatch for seed {seed}, case {caseIndex}, " +
                    $"block {blockId}.");
            }
        }
    }

    [Test]
    public void FinitePowersetModelCheckMatchesConcreteLeastFixpoints()
    {
        var checkedCases = VerifyEveryFinitePowersetCase();

        Assert.That(checkedCases, Is.EqualTo(80_200));
    }

    private static int VerifyEveryFinitePowersetCase()
    {
        var transfers = EnumerateBottomStrictMonotoneTransfers();
        Assert.That(transfers, Has.Length.EqualTo(25));
        var checkedCases = 0;

        for (var blockCount = 1; blockCount <= 2; blockCount++)
        {
            var edgeMaskLimit = 1 << (blockCount * blockCount);
            for (var edgeMask = 0; edgeMask < edgeMaskLimit; edgeMask++)
            {
                var edges = DecodeEdges(blockCount, edgeMask);
                for (var entryBlockId = 0;
                     entryBlockId < blockCount;
                     entryBlockId++)
                {
                    for (var initialBits = 0; initialBits < 4; initialBits++)
                    {
                        var initial = (FiniteSet)initialBits;
                        foreach (var first in transfers)
                        {
                            if (blockCount == 1)
                            {
                                VerifyFinitePowersetCase(
                                    edges,
                                    entryBlockId,
                                    initial,
                                    [first],
                                    edgeMask);
                                checkedCases++;
                                continue;
                            }

                            foreach (var second in transfers)
                            {
                                VerifyFinitePowersetCase(
                                    edges,
                                    entryBlockId,
                                    initial,
                                    [first, second],
                                    edgeMask);
                                checkedCases++;
                            }
                        }
                    }
                }
            }
        }

        return checkedCases;
    }

    private static void VerifyFinitePowersetCase(
        IReadOnlyList<DataflowEdge> edges,
        int entryBlockId,
        FiniteSet initial,
        IReadOnlyList<FiniteTransfer> transfers,
        int edgeMask)
    {
        System.ArgumentNullException.ThrowIfNull(edges);
        System.ArgumentNullException.ThrowIfNull(transfers);
        var blocks = transfers
            .Select((transfer, blockId) =>
                transfer is null ? throw new System.ArgumentNullException(nameof(transfer)) : (new DataflowBlock<FiniteSet>(blockId, transfer.Apply)))
            .ToArray();
        var graph = new DataflowGraph<FiniteSet>(
            blocks,
            edges,
            entryBlockId);
        var expected = ExecuteFinitePowersetFixpoint(
            transfers.Count,
            edges,
            entryBlockId,
            initial,
            transfers);
        var options = new ForwardDataflowAnalysisOptions(
            widenAfter: 0,
            maxIterations: 32);
        var actual = ForwardDataflowAnalysis.Analyze(
            graph,
            FinitePowersetDomain.Instance,
            initial,
            options);

        for (var blockId = 0; blockId < transfers.Count; blockId++)
        {
            if (actual.GetInputState(blockId) != expected.Inputs[blockId])
            {
                Assert.Fail(
                    $"Input mismatch for blocks={transfers.Count}, " +
                    $"edges=0x{edgeMask:X}, entry={entryBlockId}, " +
                    $"initial={initial}, transfers={FormatTransfers(transfers)}, " +
                    $"block={blockId}: " +
                    $"expected {expected.Inputs[blockId]}, " +
                    $"actual {actual.GetInputState(blockId)}.");
            }

            if (actual.GetOutputState(blockId) != expected.Outputs[blockId])
            {
                Assert.Fail(
                    $"Output mismatch for blocks={transfers.Count}, " +
                    $"edges=0x{edgeMask:X}, entry={entryBlockId}, " +
                    $"initial={initial}, transfers={FormatTransfers(transfers)}, " +
                    $"block={blockId}: " +
                    $"expected {expected.Outputs[blockId]}, " +
                    $"actual {actual.GetOutputState(blockId)}.");
            }
        }
    }

    private static FinitePowersetFixpoint ExecuteFinitePowersetFixpoint(
        int blockCount,
        IReadOnlyList<DataflowEdge> edges,
        int entryBlockId,
        FiniteSet initial,
        IReadOnlyList<FiniteTransfer> transfers)
    {
        if (blockCount < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(blockCount)); }
        if (entryBlockId < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(entryBlockId)); }

        System.ArgumentNullException.ThrowIfNull(edges);
        System.ArgumentNullException.ThrowIfNull(transfers);
        var inputs = Enumerable.Repeat(FiniteSet.Bottom, blockCount).ToImmutableArray().ToBuilder();
        var outputs = Enumerable.Repeat(FiniteSet.Bottom, blockCount).ToImmutableArray().ToBuilder();
        inputs[entryBlockId] = initial;

        for (var iteration = 0; iteration < 32; iteration++)
        {
            var changed = false;
            for (var blockId = 0; blockId < blockCount; blockId++)
            {
                var transferred = transfers[blockId].Apply(inputs[blockId]);
                var joined = outputs[blockId] | transferred;
                if (joined == outputs[blockId])
                {
                    continue;
                }

                outputs[blockId] = joined;
                changed = true;
            }

            for (var blockId = 0; blockId < blockCount; blockId++)
            {
                var incoming = blockId == entryBlockId
                    ? initial
                    : FiniteSet.Bottom;
                foreach (var edge in edges)
                {
                    if (edge.TargetId == blockId)
                    {
                        incoming |= outputs[edge.SourceId];
                    }
                }

                var joined = inputs[blockId] | incoming;
                if (joined == inputs[blockId])
                {
                    continue;
                }

                inputs[blockId] = joined;
                changed = true;
            }

            if (!changed)
            {
                return new FinitePowersetFixpoint(inputs.ToImmutable(), outputs.ToImmutable());
            }
        }

        Assert.Fail("The independent finite powerset solver did not converge.");
        throw new InvalidOperationException();
    }

    private static ImmutableArray<FiniteTransfer>
        EnumerateBottomStrictMonotoneTransfers()
    {
        var result = ImmutableArray.CreateBuilder<FiniteTransfer>();
        for (var encoded = 0; encoded < 64; encoded++)
        {
            var remaining = encoded;
            var outputs = Enumerable.Repeat(FiniteSet.Bottom, 4).ToImmutableArray().ToBuilder();
            outputs[0] = FiniteSet.Bottom;
            for (var input = 1; input < outputs.Count; input++)
            {
                outputs[input] = (FiniteSet)(remaining & 3);
                remaining >>= 2;
            }

            var monotone = true;
            for (var lower = 0; lower < 4 && monotone; lower++)
            {
                for (var upper = 0; upper < 4; upper++)
                {
                    var lowerSet = (FiniteSet)lower;
                    var upperSet = (FiniteSet)upper;
                    if (!IsSubset(lowerSet, upperSet) ||
                        IsSubset(outputs[lower], outputs[upper]))
                    {
                        continue;
                    }

                    monotone = false;
                    break;
                }
            }

            if (monotone)
            {
                result.Add(new FiniteTransfer(encoded, outputs.ToImmutable()));
            }
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<DataflowEdge> DecodeEdges(int blockCount, int edgeMask)
    {
        var edges = ImmutableArray.CreateBuilder<DataflowEdge>();
        for (var source = 0; source < blockCount; source++)
        {
            for (var target = 0; target < blockCount; target++)
            {
                var bit = source * blockCount + target;
                if ((edgeMask & (1 << bit)) != 0)
                {
                    edges.Add(new DataflowEdge(source, target));
                }
            }
        }

        return edges.ToImmutable();
    }

    private static bool IsSubset(FiniteSet lower, FiniteSet upper)
    {
        return (lower & upper) == lower;
    }

    private static string FormatTransfers(IReadOnlyList<FiniteTransfer> transfers)
    {
        System.ArgumentNullException.ThrowIfNull(transfers);
        return string.Join("/", transfers.Select(static transfer => transfer is null ? throw new System.ArgumentNullException(nameof(transfer)) : (transfer.Encoded)));
    }

    private static ConcreteFixpoint ExecuteConcreteFixpoint(
        DataflowGraph<NullnessValue> graph,
        IReadOnlyList<TransferKind> transfers)
    {
        System.ArgumentNullException.ThrowIfNull(graph);
        System.ArgumentNullException.ThrowIfNull(transfers);
        var inputs = Enumerable.Repeat(ConcreteNullness.Bottom, graph.Blocks.Length).ToImmutableArray().ToBuilder();
        var outputs = Enumerable.Repeat(ConcreteNullness.Bottom, graph.Blocks.Length).ToImmutableArray().ToBuilder();
        inputs[graph.EntryBlockId] = ConcreteNullness.MaybeNull;
        var changed = true;
        var iterations = 0;
        while (changed)
        {
            Assert.That(++iterations, Is.LessThan(100));
            changed = false;
            for (var blockId = 0; blockId < graph.Blocks.Length; blockId++)
            {
                var transferred = ApplyConcrete(
                    transfers[blockId],
                    inputs[blockId]);
                var output = outputs[blockId] | transferred;
                if (output == outputs[blockId])
                {
                    continue;
                }

                outputs[blockId] = output;
                changed = true;
                foreach (var successor in graph.GetSuccessors(blockId))
                {
                    inputs[successor] = inputs[successor] | output;
                }
            }
        }
        return new ConcreteFixpoint(inputs.ToImmutable(), outputs.ToImmutable());
    }

    private static NullnessValue ApplyAbstract(
        TransferKind transfer,
        NullnessValue value)
    {
        return transfer switch
        {
            TransferKind.Identity => value,
            TransferKind.AssumeNull =>
                NullnessDomain.Instance.AssumeNull(value),
            TransferKind.AssumeNonNull =>
                NullnessDomain.Instance.AssumeNonNull(value),
            TransferKind.Havoc => NullnessDomain.Instance.Havoc(value),
            _ => throw new ArgumentOutOfRangeException(nameof(transfer))
        };
    }

    private static ConcreteNullness ApplyConcrete(
        TransferKind transfer,
        ConcreteNullness value)
    {
        return transfer switch
        {
            TransferKind.Identity => value,
            TransferKind.AssumeNull => value & ConcreteNullness.Null,
            TransferKind.AssumeNonNull => value & ConcreteNullness.NonNull,
            TransferKind.Havoc => value == ConcreteNullness.Bottom
                ? ConcreteNullness.Bottom
                : ConcreteNullness.MaybeNull,
            _ => throw new ArgumentOutOfRangeException(nameof(transfer))
        };
    }

    private static ConcreteNullness ToConcrete(NullnessValue value)
    {
        return value switch
        {
            NullnessValue.Bottom => ConcreteNullness.Bottom,
            NullnessValue.Null => ConcreteNullness.Null,
            NullnessValue.NonNull => ConcreteNullness.NonNull,
            NullnessValue.MaybeNull => ConcreteNullness.MaybeNull,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    private enum TransferKind
    {
        Identity,
        AssumeNull,
        AssumeNonNull,
        Havoc
    }

    [Flags]
    private enum ConcreteNullness
    {
        Bottom = 0,
        Null = 1,
        NonNull = 2,
        MaybeNull = Null | NonNull
    }

    private sealed record ConcreteFixpoint(
        ImmutableArray<ConcreteNullness> Inputs,
        ImmutableArray<ConcreteNullness> Outputs)
    {
        public ImmutableArray<ConcreteNullness> Inputs { get; init; } = Inputs.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Inputs)) : Inputs;
        public ImmutableArray<ConcreteNullness> Outputs { get; init; } = Outputs.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Outputs)) : Outputs;
    }

    [Flags]
    private enum FiniteSet
    {
        Bottom = 0,
        First = 1,
        Second = 2,
        Top = First | Second
    }

    private sealed class FinitePowersetDomain : IAbstractDomain<FiniteSet>
    {
        public static FinitePowersetDomain Instance { get; } = new();

        private FinitePowersetDomain()
        {
        }

        public FiniteSet Bottom => FiniteSet.Bottom;
        public FiniteSet Top => FiniteSet.Top;

        public bool LessThanOrEqual(FiniteSet left, FiniteSet right)
        {
            return IsSubset(left, right);
        }

        public bool AreEquivalent(FiniteSet left, FiniteSet right)
        {
            return left == right;
        }

        public FiniteSet Join(FiniteSet left, FiniteSet right)
        {
            return left | right;
        }

        public FiniteSet Widen(FiniteSet previous, FiniteSet next)
        {
            return previous | next;
        }

        public FiniteSet Havoc(FiniteSet value)
        {
            return value == FiniteSet.Bottom ? FiniteSet.Bottom : FiniteSet.Top;
        }
    }

    private sealed class FiniteTransfer(
        int encoded,
        ImmutableArray<FiniteSet> outputs)
    {
        public int Encoded { get; } = (outputs.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(outputs)) : encoded);

        public FiniteSet Apply(FiniteSet input)
        {
            if ((uint)input >= (uint)outputs.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(input));
            }

            return outputs[(int)input];
        }
    }

    private sealed record FinitePowersetFixpoint(
        ImmutableArray<FiniteSet> Inputs,
        ImmutableArray<FiniteSet> Outputs)
    {
        public ImmutableArray<FiniteSet> Inputs { get; init; } = Inputs.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Inputs)) : Inputs;
        public ImmutableArray<FiniteSet> Outputs { get; init; } = Outputs.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Outputs)) : Outputs;
    }
}
