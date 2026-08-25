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
                new(1, value => domain.AddConstant(value, 1)),
                new(2, value => domain.AddConstant(value, 1)),
                new(3, value => domain.AddConstant(value, 1)),
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
    public void AcyclicFeedsDoNotConsumeLoopWideningBudget()
    {
        var domain = new TrackingDomain();
        var graph = new DataflowGraph<int>(
            [
                new(0, value => value),
                new(1, value => value + 1),
                new(2, value => value + 1),
                new(3, value => value + 2),
                new(4, value => value + 1),
                new(5, value => value)
            ],
            [
                new(0, 1),
                new(1, 2),
                new(2, 4),
                new(0, 3),
                new(3, 4),
                new(4, 4),
                new(4, 5)
            ]);

        var result = ForwardDataflowAnalysis.Analyze(
            graph,
            domain,
            1,
            new ForwardDataflowAnalysisOptions(widenAfter: 2));

        Assert.That(graph.IsCyclicBlock(4), Is.True);
        Assert.That(domain.FirstWidenPrevious, Is.EqualTo(5));
    }

    [Test]
    public void RandomizedBatchOrderDoesNotChangeFixpoint()
    {
        var domain = IntervalDomain.Instance;
        var graph = CreateAscendingIntervalGraph(domain);
        var options = new ForwardDataflowAnalysisOptions(widenAfter: 1, maxIterations: 100);
        var expected = ForwardDataflowAnalysis.Analyze(
            graph,
            domain,
            IntervalValue.Constant(0),
            options);

        for (var seed = 0; seed < 32; seed++)
        {
            var random = new Random(seed);
            var actual = ForwardDataflowAnalysis.AnalyzeWithWorklistOrderForTesting(
                graph,
                domain,
                IntervalValue.Constant(0),
                options,
                pending => [.. pending.OrderBy(_ => random.Next())]);

            Assert.That(actual.Iterations, Is.EqualTo(expected.Iterations));
            for (var blockId = 0; blockId < graph.Blocks.Length; blockId++)
            {
                Assert.That(
                    domain.AreEquivalent(
                        actual.GetInputState(blockId),
                        expected.GetInputState(blockId)),
                    Is.True,
                    $"Input state differs at block {blockId} for seed {seed}.");
                Assert.That(
                    domain.AreEquivalent(
                        actual.GetOutputState(blockId),
                        expected.GetOutputState(blockId)),
                    Is.True,
                    $"Output state differs at block {blockId} for seed {seed}.");
            }
        }
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
                new(1, value => domain.AddConstant(value, 1)),
                new(2, value => domain.AddConstant(value, 2)),
                new(3, value => domain.AddConstant(value, 1)),
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

    private sealed class TrackingDomain : IAbstractDomain<int>
    {
        internal int FirstWidenPrevious
        {
            get;
            private set;
        } = -1;

        public int Bottom
        {
            get { return 0; }
        }
        public int Top
        {
            get { return int.MaxValue; }
        }
        public bool LessThanOrEqual(int left, int right)
        {
            return left <= right;
        }
        public bool AreEquivalent(int left, int right)
        {
            return left == right;
        }
        public int Join(int left, int right)
        {
            return Math.Max(left, right);
        }
        public int Widen(int previous, int candidate)
        {
            if (FirstWidenPrevious < 0)
            {
                FirstWidenPrevious = previous;
            }

            return Top;
        }

        public int Havoc(int value)
        {
            return Top;
        }
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
                new(1, value => domain.AddConstant(value, 1))
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

        // Callers that must degrade gracefully catch this specific type rather
        // than every InvalidOperationException.
        Assert.That(failure, Is.InstanceOf<InvalidOperationException>());
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
}
