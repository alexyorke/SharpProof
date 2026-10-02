using NUnit.Framework;
using System.Collections.Immutable;
using SharpProof.CompilerArtifact;
using SharpProof.Dataflow;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CoreIrScalarIntervalTransferTests
{
    [TestCase(8, true, (long)sbyte.MinValue, (long)sbyte.MaxValue)]
    [TestCase(8, false, 0L, (long)byte.MaxValue)]
    [TestCase(16, true, (long)short.MinValue, (long)short.MaxValue)]
    [TestCase(16, false, 0L, (long)ushort.MaxValue)]
    [TestCase(32, true, (long)int.MinValue, (long)int.MaxValue)]
    [TestCase(32, false, 0L, (long)uint.MaxValue)]
    [TestCase(64, true, long.MinValue, long.MaxValue)]
    public void SingletonArithmeticMatchesTotalInterpreter(int width, bool isSigned, long minimum, long maximum)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, isSigned);
        var info = factory.GetTypeInfo(type);
        Assert.That(CoreIrScalarIntervalTransfer.TryTypeRange(info, out var range), Is.True);
        Assert.That(range, Is.EqualTo(IntervalValue.Range(minimum, maximum)));
        long[] samples = isSigned ? [minimum, minimum + 1, -1, 0, 1, maximum - 1, maximum]
            : [0, 1, maximum - 1, maximum];
        IrBinaryOperator[] operations = [IrBinaryOperator.Add, IrBinaryOperator.Subtract,
            IrBinaryOperator.Multiply, IrBinaryOperator.Divide, IrBinaryOperator.Remainder];
        var x = factory.CreateVariable("x", type);
        var y = factory.CreateVariable("y", type);
        var interpreter = new IrInterpreter(factory);
        var negated = factory.Unary(IrUnaryOperator.Negate, factory.Variable(x));
        foreach (var sample in samples)
        {
            var result = interpreter.Evaluate(negated, ImmutableDictionary<IrVarId, IrValue>.Empty
                .Add(x, factory.CreateIntegerValue(type, sample)));
            Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(CoreIrScalarIntervalTransfer.Negate(info, IntervalValue.Constant(sample)).SingletonValue,
                Is.EqualTo(result.Value!.Integer));
        }
        foreach (var operation in operations)
        {
            var term = factory.Binary(operation, factory.Variable(x), factory.Variable(y));
            foreach (var left in samples)
            {
                foreach (var right in samples)
                {
                    var values = ImmutableDictionary<IrVarId, IrValue>.Empty
                        .Add(x, factory.CreateIntegerValue(type, left))
                        .Add(y, factory.CreateIntegerValue(type, right));
                    var actual = interpreter.Evaluate(term, values);
                    Assert.That(actual.Status, Is.EqualTo(IrEvaluationStatus.Value));
                    var abstractValue = CoreIrScalarIntervalTransfer.Binary(operation, info,
                        IntervalValue.Constant(left), IntervalValue.Constant(right));
                    Assert.That(abstractValue.SingletonValue, Is.EqualTo(actual.Value!.Integer),
                        $"{width}/{isSigned}: {left} {operation} {right}");
                }
            }
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ByteRangesContainEveryConcreteArithmeticResult(bool isSigned)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(8, isSigned);
        var info = factory.GetTypeInfo(type);
        var x = factory.CreateVariable("x", type);
        var y = factory.CreateVariable("y", type);
        var interpreter = new IrInterpreter(factory);
        var minimum = isSigned ? -128 : 0;
        var maximum = isSigned ? 127 : 255;
        IntervalValue[] intervals = [IntervalValue.Range(minimum, minimum + 4),
            IntervalValue.Range(maximum - 4, maximum), IntervalValue.Range(0, 5),
            IntervalValue.Range(minimum, maximum)];
        IrBinaryOperator[] operations = [IrBinaryOperator.Add, IrBinaryOperator.Subtract,
            IrBinaryOperator.Multiply, IrBinaryOperator.Divide, IrBinaryOperator.Remainder];
        foreach (var operation in operations)
        {
            var term = factory.Binary(operation, factory.Variable(x), factory.Variable(y));
            foreach (var left in intervals)
            {
                foreach (var right in intervals)
                {
                    var abstractValue = CoreIrScalarIntervalTransfer.Binary(operation, info, left, right);
                    for (var first = (int)(left.LowerBound ?? minimum); first <= (left.UpperBound ?? maximum); first++)
                    {
                        for (var second = (int)(right.LowerBound ?? minimum); second <= (right.UpperBound ?? maximum); second++)
                        {
                            var values = ImmutableDictionary<IrVarId, IrValue>.Empty
                                .Add(x, factory.CreateIntegerValue(type, first))
                                .Add(y, factory.CreateIntegerValue(type, second));
                            var actual = interpreter.Evaluate(term, values);
                            Assert.That(actual.Status, Is.EqualTo(IrEvaluationStatus.Value));
                            Assert.That(abstractValue.Contains(actual.Value!.Integer), Is.True,
                                $"{isSigned}: {first} {operation} {second} outside {abstractValue}");
                        }
                    }
                }
            }
        }
    }

    [Test]
    public void TightBoundsAndWrappingRemainDistinct()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetTypeInfo(factory.IntegerType);
        Assert.That(CoreIrScalarIntervalTransfer.Binary(IrBinaryOperator.Add, type,
            IntervalValue.Range(0, 10), IntervalValue.Range(2, 3)), Is.EqualTo(IntervalValue.Range(2, 13)));
        Assert.That(CoreIrScalarIntervalTransfer.Binary(IrBinaryOperator.Multiply, type,
            IntervalValue.Range(-3, -1), IntervalValue.Range(2, 4)), Is.EqualTo(IntervalValue.Range(-12, -2)));
        CoreIrScalarIntervalTransfer.TryTypeRange(type, out var full);
        Assert.That(CoreIrScalarIntervalTransfer.Binary(IrBinaryOperator.Add, type,
            IntervalValue.Range(int.MaxValue - 1, int.MaxValue), IntervalValue.Constant(1)), Is.EqualTo(full));
        Assert.That(CoreIrScalarIntervalTransfer.Binary(IrBinaryOperator.Add, type,
            IntervalValue.Bottom, full).IsBottom, Is.True);
    }

    [TestCase(8, true, 32, false, -1L, (long)uint.MaxValue)]
    [TestCase(32, false, 64, true, (long)uint.MaxValue, (long)uint.MaxValue)]
    [TestCase(32, true, 8, false, 256L, 0L)]
    [TestCase(32, false, 32, true, (long)uint.MaxValue, -1L)]
    [TestCase(64, true, 32, true, long.MinValue, 0L)]
    [TestCase(16, true, 8, true, 128L, (long)sbyte.MinValue)]
    [TestCase(8, false, 16, true, (long)byte.MaxValue, (long)byte.MaxValue)]
    [TestCase(32, true, 64, true, (long)int.MinValue, (long)int.MinValue)]
    public void CastsMatchTheInterpreterAndClrBoundaryValues(int sourceWidth, bool sourceIsSigned,
        int targetWidth, bool targetIsSigned, long input, long expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var source = factory.GetOrCreateIntegerType(sourceWidth, sourceIsSigned);
        var target = factory.GetOrCreateIntegerType(targetWidth, targetIsSigned);
        var variable = factory.CreateVariable("value", source);
        var concrete = new IrInterpreter(factory).Evaluate(factory.Cast(target, factory.Variable(variable)),
            ImmutableDictionary<IrVarId, IrValue>.Empty.Add(variable, factory.CreateIntegerValue(source, input)));
        Assert.That(concrete.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(concrete.Value!.Integer, Is.EqualTo(expected));
        Assert.That(CoreIrScalarIntervalTransfer.Cast(factory.GetTypeInfo(source), factory.GetTypeInfo(target),
            IntervalValue.Constant(input)).SingletonValue, Is.EqualTo(expected));
    }

    [Test]
    public void SparseCarrierExtremaRemainContained()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetTypeInfo(factory.GetOrCreateIntegerType(64, true));
        var even = IntervalValue.Congruent(null, null, 2, 0);
        var odd = IntervalValue.Congruent(null, null, 2, 1);
        foreach (var (interval, first, last) in new[] {
            (even, long.MinValue, long.MaxValue - 1), (odd, long.MinValue + 1, long.MaxValue) })
        {
            var sum = CoreIrScalarIntervalTransfer.Binary(IrBinaryOperator.Add, type,
                interval, IntervalValue.Constant(0));
            Assert.That(sum.Contains(first) && sum.Contains(last), Is.True);
            Assert.That(CoreIrScalarIntervalTransfer.Cast(type, type, interval), Is.EqualTo(interval));
            var negated = CoreIrScalarIntervalTransfer.Negate(type, interval);
            Assert.That(negated.Contains(unchecked(-first)) && negated.Contains(unchecked(-last)), Is.True);
        }
    }

    [Test]
    public void CastsPreserveSafeCongruenceAndCompleteWrapping()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var source = factory.GetTypeInfo(factory.IntegerType);
        var wide = factory.GetTypeInfo(factory.GetOrCreateIntegerType(64, true));
        var unsigned = factory.GetTypeInfo(factory.GetOrCreateIntegerType(32, false));
        var octet = factory.GetTypeInfo(factory.GetOrCreateIntegerType(8, false));
        var sparse = IntervalValue.Congruent(0, 10, 2, 0);
        Assert.That(CoreIrScalarIntervalTransfer.Cast(source, wide, sparse), Is.EqualTo(sparse));
        Assert.That(CoreIrScalarIntervalTransfer.Cast(source, octet, IntervalValue.Constant(256)),
            Is.EqualTo(IntervalValue.Constant(0)));
        Assert.That(CoreIrScalarIntervalTransfer.Cast(source, unsigned, IntervalValue.Constant(-1)),
            Is.EqualTo(IntervalValue.Constant(uint.MaxValue)));
        Assert.That(CoreIrScalarIntervalTransfer.Negate(wide, IntervalValue.Constant(long.MinValue)),
            Is.EqualTo(IntervalValue.Constant(long.MinValue)));
        Assert.That(CoreIrScalarIntervalTransfer.Negate(wide, IntervalValue.Range(long.MinValue, 0)),
            Is.EqualTo(IntervalValue.Top));
        Assert.That(CoreIrScalarIntervalTransfer.TryTypeRange(
            factory.GetTypeInfo(factory.GetOrCreateIntegerType(64, false)), out _), Is.False);
        var legacy = new IrFactory();
        Assert.That(CoreIrScalarIntervalTransfer.TryTypeRange(legacy.GetTypeInfo(legacy.IntegerType), out _), Is.False);
    }
}

[TestFixture]
public sealed class EffectSummaryFixpointTests
{
    [Test]
    public void EffectFreeRecursionDoesNotInventEffectsOrNormalCompletion()
    {
        var graph = Graph("[ZeroAllocations] public static int Root(int x) => Root(x);");
        var summary = EffectSummaryFixpoint.ComputeValidated(graph).Values.Single();
        Assert.That(summary.MayEffects, Is.EqualTo(SourceMayEffect.None));
        Assert.That(summary.UnknownEffects, Is.EqualTo(SourceMayEffect.None));
        Assert.That(summary.MayDiverge, Is.True);
        Assert.That(summary.CompletionUnknown, Is.True);
    }

    [Test]
    public void AllocationPropagatesThroughAnEntireRecursiveComponentAndItsCaller()
    {
        var graph = Graph("""
            [ZeroAllocations] public static object Root(int x) => First(x);
            static object First(int x) => Second(x);
            static object Second(int x) { object allocated = new object(); return First(x); }
            """);
        var summaries = EffectSummaryFixpoint.ComputeValidated(graph);
        Assert.That(summaries, Has.Count.EqualTo(3));
        Assert.That(summaries.Values.All(summary => summary.MayEffects.HasFlag(SourceMayEffect.Allocation) &&
            summary.UnknownEffects == SourceMayEffect.None && summary.MayDiverge), Is.True);
        var expected = summaries.ToArray();
        graph.Bodies = graph.Bodies.Reverse().ToArray();
        foreach (var body in graph.Bodies)
        { body.Callees = body.Callees.Reverse().ToArray(); }
        Assert.That(EffectSummaryFixpoint.ComputeValidated(graph).ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public void UnknownExternalBoundaryPropagatesThroughRecursion()
    {
        var graph = Graph("""
            [ZeroAllocations] public static int Root(int x) => First(x);
            static int First(int x) => Second(x);
            static int Second(int x) => First(System.Math.Sign(x));
            """);
        Assert.That(EffectSummaryFixpoint.ComputeValidated(graph).Values.All(summary =>
            summary.UnknownEffects == SourceMayEffect.All && summary.UnknownExceptions && summary.MayDiverge), Is.True);
    }

    [Test]
    public void CaughtCalleeFaultsStayConservativeAndRetainExceptionAllocation()
    {
        var graph = Graph("""
            [ZeroAllocations] public static int Root(int x) {
                try { return Divide(x); } catch (System.DivideByZeroException) { return 0; }
            }
            static int Divide(int x) => 10 / x;
            """);
        var root = graph.Roots.Single().BodyId;
        var summary = EffectSummaryFixpoint.ComputeValidated(graph)[root];
        Assert.That(summary.ExceptionKinds & (1 << (int)IrExceptionKind.DivideByZero), Is.Not.Zero);
        Assert.That(summary.MayEffects.HasFlag(SourceMayEffect.Allocation), Is.True);
        Assert.That(summary.CompletionUnknown, Is.True);
    }

    [Test]
    public void IncompleteEntryInitializationCannotBecomeEffectFree()
    {
        var graph = Graph("""
            static readonly object State = new object();
            [ZeroAllocations] public static int Root() => 1;
            """);
        Assert.That(graph.Bodies.Single().EffectsCompleteAtEntry, Is.False);
        Assert.That(EffectSummaryFixpoint.ComputeValidated(graph).Values.Single().UnknownEffects,
            Is.EqualTo(SourceMayEffect.All));
    }

    [Test]
    public void LocalLoopsRemainPossiblyDivergent()
    {
        var graph = Graph("[ZeroAllocations] public static int Root(int x) { while (x > 0) { x--; } return x; }");
        var summary = EffectSummaryFixpoint.ComputeValidated(graph).Values.Single();
        Assert.That(summary.MayDiverge, Is.True);
        Assert.That(summary.CompletionUnknown, Is.True);
    }

    [TestCase("static int State; [ZeroAllocations] public static int Root(int x) { State = x; return x; }", (int)SourceMayEffect.NonlocalWrite)]
    [TestCase("[ZeroAllocations] public static int Root(object gate) { lock (gate) {} return 1; }", (int)SourceMayEffect.Synchronization)]
    public void LocalNonlocalWritesAndLocksRetainTheirEffectFacet(string members, int expected)
    {
        var summary = EffectSummaryFixpoint.ComputeValidated(Graph(members)).Values.Single();
        Assert.That(summary.MayEffects.HasFlag((SourceMayEffect)expected), Is.True);
        Assert.That(summary.UnknownEffects, Is.EqualTo(SourceMayEffect.None));
    }

    [Test]
    public void LocalAssignmentsDoNotBecomeNonlocalWrites()
    {
        var summary = EffectSummaryFixpoint.ComputeValidated(
            Graph("[ZeroAllocations] public static int Root(int x) { x++; return x; }")).Values.Single();
        Assert.That(summary.MayEffects.HasFlag(SourceMayEffect.NonlocalWrite), Is.False);
        Assert.That(summary.UnknownEffects, Is.EqualTo(SourceMayEffect.None));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MaximumDepthGraphsUseAnIterativeBoundedTraversal(bool cycle)
    {
        var leaf = Graph("[ZeroAllocations] public static int Root() => 1;").Bodies.Single();
        // Exercise the algorithm's graph bound independently of compilation;
        // the same already validated, effect-free local IR is reused.
        var bodies = Enumerable.Range(0, 4096).Select(index => new CompilerSourceBodyArtifact
        {
            BodyId = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CallsComplete = true,
            EffectsCompleteAtEntry = true,
            Graph = leaf.Graph,
            Callees = index < 4095 ? [(index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)] : cycle ? ["0"] : []
        }).ToArray();
        var summaries = EffectSummaryFixpoint.ComputeValidated(new CompilerReachableSourceArtifact { Bodies = bodies });
        Assert.That(summaries, Has.Count.EqualTo(4096));
        Assert.That(summaries.Values.All(summary => summary.MayEffects == SourceMayEffect.None && summary.MayDiverge == cycle), Is.True);
    }

    [Test]
    public void CancelledSummaryConstructionStopsImmediately()
    {
        var graph = Graph("[ZeroAllocations] public static int Root() => 1;");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() => EffectSummaryFixpoint.ComputeValidated(graph, cancelled.Token)));
    }

    private static CompilerReachableSourceArtifact Graph(string members)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; static class Subject { " + members + " }");
        return CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _).ReachableSource!;
    }
}
