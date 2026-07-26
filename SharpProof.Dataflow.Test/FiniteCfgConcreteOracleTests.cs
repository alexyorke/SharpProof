namespace SharpProof.Dataflow.Test;

[TestFixture]
public sealed class FiniteCfgConcreteOracleTests {
    [Test]
    public void GeneratedNullnessFixpointsMatchConcreteReachability() {
        const int seed = 0x4C7F;
        var random = new Random(seed);
        for (var caseIndex = 0; caseIndex < 256; caseIndex++) {
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
            var edges = new HashSet<DataflowEdge>();
            for (var blockId = 1; blockId < blockCount; blockId++)
                edges.Add(new DataflowEdge(blockId - 1, blockId));
            for (var source = 0; source < blockCount; source++)
                for (var target = 0; target < blockCount; target++)
                    if (random.Next(5) == 0)
                        edges.Add(new DataflowEdge(source, target));
            var graph = new DataflowGraph<NullnessValue>(blocks, edges);

            var abstractResult = ForwardDataflowAnalysis.Analyze(
                graph,
                NullnessDomain.Instance,
                NullnessValue.MaybeNull);
            var concreteResult = ExecuteConcreteFixpoint(
                graph,
                transfers);

            for (var blockId = 0; blockId < blockCount; blockId++) {
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

    private static ConcreteFixpoint ExecuteConcreteFixpoint(
        DataflowGraph<NullnessValue> graph,
        IReadOnlyList<TransferKind> transfers) {
        var inputs = new ConcreteNullness[graph.Blocks.Length];
        var outputs = new ConcreteNullness[graph.Blocks.Length];
        inputs[graph.EntryBlockId] = ConcreteNullness.MaybeNull;
        var changed = true;
        var iterations = 0;
        while (changed) {
            Assert.That(++iterations, Is.LessThan(100));
            changed = false;
            for (var blockId = 0; blockId < graph.Blocks.Length; blockId++) {
                var transferred = ApplyConcrete(
                    transfers[blockId],
                    inputs[blockId]);
                var output = outputs[blockId] | transferred;
                if (output == outputs[blockId]) continue;
                outputs[blockId] = output;
                changed = true;
                foreach (var successor in graph.GetSuccessors(blockId))
                    inputs[successor] |= output;
            }
        }
        return new ConcreteFixpoint(inputs, outputs);
    }

    private static NullnessValue ApplyAbstract(
        TransferKind transfer,
        NullnessValue value) =>
        transfer switch {
            TransferKind.Identity => value,
            TransferKind.AssumeNull =>
                NullnessDomain.Instance.AssumeNull(value),
            TransferKind.AssumeNonNull =>
                NullnessDomain.Instance.AssumeNonNull(value),
            TransferKind.Havoc => NullnessDomain.Instance.Havoc(value),
            _ => throw new ArgumentOutOfRangeException(nameof(transfer))
        };

    private static ConcreteNullness ApplyConcrete(
        TransferKind transfer,
        ConcreteNullness value) =>
        transfer switch {
            TransferKind.Identity => value,
            TransferKind.AssumeNull => value & ConcreteNullness.Null,
            TransferKind.AssumeNonNull => value & ConcreteNullness.NonNull,
            TransferKind.Havoc => value == ConcreteNullness.Bottom
                ? ConcreteNullness.Bottom
                : ConcreteNullness.MaybeNull,
            _ => throw new ArgumentOutOfRangeException(nameof(transfer))
        };

    private static ConcreteNullness ToConcrete(NullnessValue value) =>
        value switch {
            NullnessValue.Bottom => ConcreteNullness.Bottom,
            NullnessValue.Null => ConcreteNullness.Null,
            NullnessValue.NonNull => ConcreteNullness.NonNull,
            NullnessValue.MaybeNull => ConcreteNullness.MaybeNull,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private enum TransferKind {
        Identity,
        AssumeNull,
        AssumeNonNull,
        Havoc
    }

    [Flags]
    private enum ConcreteNullness {
        Bottom = 0,
        Null = 1,
        NonNull = 2,
        MaybeNull = Null | NonNull
    }

    private sealed record ConcreteFixpoint(
        ConcreteNullness[] Inputs,
        ConcreteNullness[] Outputs);
}
