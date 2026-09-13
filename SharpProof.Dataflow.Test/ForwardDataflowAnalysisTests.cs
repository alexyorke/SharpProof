using System.Diagnostics;

namespace SharpProof.Dataflow.Test;

[TestFixture]
public sealed class ForwardDataflowAnalysisTests
{
    [Test]
    public void DiamondJoinsPredecessorStates()
    {
        var domain = NullnessDomain.Instance;
        var graph = new DataflowGraph<NullnessValue>(
            [
                new(0, value => value),
                new(1, domain.AssumeNull),
                new(2, domain.AssumeNonNull),
                new(3, value => value)
            ],
            [
                new(0, 1),
                new(0, 2),
                new(1, 3),
                new(2, 3)
            ]);

        var result = ForwardDataflowAnalysis.Analyze(
            graph,
            domain,
            NullnessValue.MaybeNull);

        Assert.That(result.GetOutputState(1), Is.EqualTo(NullnessValue.Null));
        Assert.That(result.GetOutputState(2), Is.EqualTo(NullnessValue.NonNull));
        Assert.That(result.GetInputState(3), Is.EqualTo(NullnessValue.MaybeNull));
        Assert.That(result.GetOutputState(3), Is.EqualTo(NullnessValue.MaybeNull));
    }

    [Test]
    public void ReachableNonBottomStrictTransferRunsAtBottomInput()
    {
        var domain = NullnessDomain.Instance;
        var graph = new DataflowGraph<NullnessValue>(
            [
                new(0, domain.AssumeNonNull),
                new(1, _ => NullnessValue.Null)
            ],
            [new(0, 1)]);

        var result = ForwardDataflowAnalysis.Analyze(
            graph,
            domain,
            NullnessValue.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.GetInputState(1),
                Is.EqualTo(NullnessValue.Bottom));
            Assert.That(
                result.GetOutputState(1),
                Is.EqualTo(NullnessValue.Null));
        }
    }

    [Test]
    public void CanonicalDomainStoresStrictGrowthWithoutJoining()
    {
        var domain = new TrackingCanonicalDomain();
        var graph = new DataflowGraph<int>(
            [new(0, _ => 1)],
            []);

        var result = ForwardDataflowAnalysis.Analyze(graph, domain, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetOutputState(0), Is.EqualTo(1));
            Assert.That(domain.JoinCallCount, Is.EqualTo(0));
        }
    }

    [Test]
    public void CanonicalDomainStoresLaterStrictGrowthWithoutJoining()
    {
        var domain = new TrackingCanonicalDomain();
        var graph = new DataflowGraph<int>(
            [
                new(0, _ => 1),
                new(1, value => value == 0 ? 1 : 2)
            ],
            [new(0, 1)]);

        var result = ForwardDataflowAnalysis.Analyze(graph, domain, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetOutputState(1), Is.EqualTo(2));
            // The two joins are the predecessor/input propagation joins. No
            // strict-growth output update invokes Join.
            Assert.That(domain.JoinCallCount, Is.EqualTo(2));
        }
    }

    [Test]
    public void CanonicalDomainRetainsJoinNormalizationForNonCanonicalTransfer()
    {
        var domain = new NormalizingDomain();
        var graph = new DataflowGraph<int>(
            [new(0, _ => 1)],
            []);

        var result = ForwardDataflowAnalysis.Analyze(graph, domain, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetOutputState(0), Is.EqualTo(2));
            Assert.That(domain.JoinCallCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void LoopUsesWideningAndTerminates()
    {
        var domain = IntervalDomain.Instance;
        var graph = CreateAscendingIntervalGraph(domain);
        var options = new ForwardDataflowAnalysisOptions(widenAfter: 1, maxIterations: 100);

        var result = ForwardDataflowAnalysis.Analyze(
            graph,
            domain,
            IntervalValue.Constant(0),
            options);

        Assert.That(result.Iterations, Is.LessThan(100));
        Assert.That(result.GetInputState(3).UpperBound, Is.Null);
        Assert.That(result.GetOutputState(4).UpperBound, Is.Null);
    }

    [Test]
    public void AcyclicJoinsDoNotWiden()
    {
        var domain = IntervalDomain.Instance;
        var graph = new DataflowGraph<IntervalValue>(
            [
                new(0, value => value),
                new(1, value => AddConstant(domain, value, 1)),
                new(2, value => AddConstant(domain, value, 1)),
                new(3, value => AddConstant(domain, value, 1)),
                new(4, value => value)
            ],
            [
                new(0, 1),
                new(0, 2),
                new(1, 4),
                new(2, 3),
                new(3, 4)
            ]);

        var result = ForwardDataflowAnalysis.Analyze(
            graph,
            domain,
            IntervalValue.Constant(0),
            new ForwardDataflowAnalysisOptions(widenAfter: 0));

        Assert.That(graph.IsCyclicBlock(4), Is.False);
        Assert.That(result.GetInputState(4), Is.EqualTo(IntervalValue.Range(1, 2)));
    }

    [Test]
    public void CycleClassificationMarksOnlyCyclicComponents()
    {
        var graph = new DataflowGraph<int>(
            Enumerable.Range(0, 9)
                .Select(static id => new DataflowBlock<int>(id, value => value)),
            [
                new(0, 1),
                new(1, 2),
                new(2, 1),
                new(2, 3),
                new(3, 4),
                new(4, 5),
                new(5, 4),
                new(6, 6),
                new(7, 8)
            ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(graph.IsCyclicBlock(0), Is.False);
            Assert.That(graph.IsCyclicBlock(1), Is.True);
            Assert.That(graph.IsCyclicBlock(2), Is.True);
            Assert.That(graph.IsCyclicBlock(3), Is.False);
            Assert.That(graph.IsCyclicBlock(4), Is.True);
            Assert.That(graph.IsCyclicBlock(5), Is.True);
            Assert.That(graph.IsCyclicBlock(6), Is.True);
            Assert.That(graph.IsCyclicBlock(7), Is.False);
            Assert.That(graph.IsCyclicBlock(8), Is.False);
        }
    }

    [Test]
    public void SparseAcyclicCycleClassificationCompletesWithinLinearBudget()
    {
        const int blockCount = 30_000;
        var blocks = Enumerable.Range(0, blockCount)
            .Select(static id => new DataflowBlock<int>(id, value => value));
        var edges = Enumerable.Range(0, blockCount - 1)
            .Select(static id => new DataflowEdge(id, id + 1));

        var stopwatch = Stopwatch.StartNew();
        var graph = new DataflowGraph<int>(blocks, edges);
        stopwatch.Stop();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(graph.IsCyclicBlock(0), Is.False);
            Assert.That(
                graph.IsCyclicBlock(blockCount / 2),
                Is.False);
            Assert.That(
                graph.IsCyclicBlock(blockCount - 1),
                Is.False);
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(TimeSpan.FromSeconds(5)),
                $"Sparse DAG construction took {stopwatch.Elapsed}.");
        }
    }

    [Test]
    public void NonmonotoneTransferIsRejectedBeforeStaleOutputPropagates()
    {
        var domain = NullnessDomain.Instance;
        var graph = new DataflowGraph<NullnessValue>(
            [
                new(0, value => value),
                new(1, value => value == NullnessValue.Bottom
                    ? NullnessValue.Bottom
                    : NullnessValue.NonNull),
                new(2, value => value),
                new(3, value => value switch
                {
                    NullnessValue.Bottom => NullnessValue.Bottom,
                    NullnessValue.Null => NullnessValue.NonNull,
                    _ => NullnessValue.Null
                }),
                new(4, value => value)
            ],
            [
                new(0, 1),
                new(0, 3),
                new(1, 2),
                new(2, 3),
                new(3, 4)
            ]);

        var failure = Assert.Throws<InvalidOperationException>((Action)(() =>
            ForwardDataflowAnalysis.Analyze(
                graph,
                domain,
                NullnessValue.Null)));

        Assert.That(
            failure!.Message,
            Does.Contain("Block 3").And.Contain("monotone"));
    }

    [Test]
    public void GraphCanonicalizesEdgesAndRejectsNonContiguousBlocks()
    {
        var graph = new DataflowGraph<NullnessValue>(
            [
                new(0, value => value),
                new(1, value => value)
            ],
            [
                new(0, 1),
                new(0, 1)
            ]);

        Assert.That(graph.Edges, Has.Length.EqualTo(1));
        Assert.Throws<ArgumentException>((Action)(() => new DataflowGraph<NullnessValue>(
                [
                    new(0, value => value),
                    new(2, value => value)
                ],
                [])));
    }

    private static DataflowGraph<IntervalValue> CreateAscendingIntervalGraph(
        IntervalDomain domain)
    {
        return new(
            [
                new(0, value => value),
                new(1, value => AddConstant(domain, value, 1)),
                new(2, value => AddConstant(domain, value, 2)),
                new(3, value => AddConstant(domain, value, 1)),
                new(4, value => value)
            ],
            [
                new(0, 1),
                new(0, 2),
                new(1, 3),
                new(2, 3),
                new(3, 3),
                new(3, 4)
            ]);
    }

    [Test]
    public void NonConvergenceRaisesATypedConvergenceFailure()
    {
        var domain = IntervalDomain.Instance;

        // A self-loop that keeps incrementing never reaches a fixed point while
        // widening is disabled, so the solver must hit its iteration bound.
        var graph = new DataflowGraph<IntervalValue>(
            [
                new(0, value => value),
                new(1, value => AddConstant(domain, value, 1))
            ],
            [
                new(0, 1),
                new(1, 1)
            ]);

        var failure = Assert.Throws<DataflowConvergenceException>((Action)(() =>
            ForwardDataflowAnalysis.Analyze(
                graph,
                domain,
                IntervalValue.Constant(0),
                new ForwardDataflowAnalysisOptions(
                    widenAfter: int.MaxValue,
                    maxIterations: 8))));

        Assert.That(failure!.Message, Does.Contain("did not converge"));
    }

    [Test]
    public void ConvergenceFailureExposesTheStandardExceptionSurface()
    {
        // CA1032 requires the full constructor set on a public exception type,
        // so the surface is exercised rather than left as untested ceremony.
        var inner = new InvalidOperationException("inner");
        var withMessage = new DataflowConvergenceException("explicit");
        var withInner = new DataflowConvergenceException("wrapped", inner);

        Assert.That(
            new DataflowConvergenceException().Message,
            Does.Contain("did not converge"));
        Assert.That(withMessage.Message, Is.EqualTo("explicit"));
        Assert.That(withInner.Message, Is.EqualTo("wrapped"));
        Assert.That(withInner.InnerException, Is.SameAs(inner));
    }

    private static IntervalValue AddConstant(
        IntervalDomain domain, IntervalValue value, long addend)
    {
        if (value.IsBottom)
        {
            return domain.Bottom;
        }

        try
        {
            return domain.Range(
                value.LowerBound.HasValue
                    ? checked(value.LowerBound.Value + addend)
                    : null,
                value.UpperBound.HasValue
                    ? checked(value.UpperBound.Value + addend)
                    : null);
        }
        catch (OverflowException)
        {
            return domain.Top;
        }
    }

    private sealed class TrackingCanonicalDomain : CanonicalAbstractDomain<int>
    {
        public int JoinCallCount { get; private set; }

        public override int Bottom => 0;
        public override int Top => 2;

        protected override bool IsCanonical(int value)
        {
            return value is >= 0 and <= 2;
        }

        public override bool LessThanOrEqual(int left, int right)
        {
            return left <= right;
        }

        public override int Join(int left, int right)
        {
            JoinCallCount++;
            return Math.Max(left, right);
        }

        public override int Havoc(int value)
        {
            return value == Bottom ? Bottom : Top;
        }
    }

    private sealed class NormalizingDomain : CanonicalAbstractDomain<int>
    {
        public int JoinCallCount { get; private set; }

        public override int Bottom => 0;
        public override int Top => 3;

        protected override bool IsCanonical(int value)
        {
            return Normalize(value) == value;
        }

        public override bool LessThanOrEqual(int left, int right)
        {
            return Normalize(left) <= Normalize(right);
        }

        public override int Join(int left, int right)
        {
            JoinCallCount++;
            return Math.Max(Normalize(left), Normalize(right));
        }

        public override int Widen(int previous, int candidate)
        {
            return Join(previous, candidate);
        }

        public override int Havoc(int value)
        {
            return value == Bottom ? Bottom : Top;
        }

        private static int Normalize(int value)
        {
            return value == 1 ? 2 : value;
        }
    }
}
