using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeTaskRuntimeCostTests
{
    [TestCase(2, false, OptimizationLevel.Debug)]
    [TestCase(2, true, OptimizationLevel.Debug)]
    [TestCase(3, false, OptimizationLevel.Release)]
    [TestCase(3, true, OptimizationLevel.Release)]
    [TestCase(16, false, OptimizationLevel.Release)]
    [TestCase(16, true, OptimizationLevel.Release)]
    public void CompilerOwnedProjectionComposesDeclaredRuntimeQuantitiesWithIndependentLayoutOracles(int children,
        bool configured, OptimizationLevel optimization)
    {
        var target = Prepare(children, configured, optimization);
        var inputs = Inputs();
        var check = Check(target, inputs);
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.EqualTo(NativeTaskRuntimeCostVerifier.Model));
        for (var n = 0; n <= 7; n++)
        {
            Assert.That(check.CumulativeUpper!.Evaluate(_ => n, TestContext.CurrentContext.CancellationToken),
                Is.EqualTo(ManualSum(target.ForkJoinProjection!, inputs, n)));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ZeroFaultEpisodesStillChargesSourceCarriersSchedulingJoinAwaitAndInitialization(bool configured)
    {
        var target = Prepare(2, configured, OptimizationLevel.Release);
        var inputs = Inputs() with { TotalFaultEpisodesPerInvocation = 0 };
        var check = Check(target, inputs);
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.CumulativeUpper!.Evaluate(_ => 4, TestContext.CurrentContext.CancellationToken),
            Is.EqualTo(ManualSum(target.ForkJoinProjection!, inputs, 4)));
        Assert.That(check.CumulativeUpper.Evaluate(_ => 4, TestContext.CurrentContext.CancellationToken), Is.GreaterThan(BigInteger.Zero));
    }

    [Test]
    public void EveryNegativeQuantityIsRejectedEvenWhenItsMultiplierWouldBeZero()
    {
        var target = Prepare(2, false, OptimizationLevel.Release);
        var original = Inputs() with { TotalFaultEpisodesPerInvocation = 0 };
        for (var index = 0; index < 11; index++)
        {
            var changed = index switch
            {
                0 => original with { RawTraceArrayAttemptsPerFaultEpisode = -1 },
                1 => original with { RawTraceArrayPayloadBytes = -1 },
                2 => original with { ResolverTraceArrayAttemptsPerFaultEpisode = -1 },
                3 => original with { ResolverTraceArraySlots = -1 },
                4 => original with { FaultConstructionBytesPerFaultEpisode = -1 },
                5 => original with { FreshResolverReferentBytesPerFaultEpisode = -1 },
                6 => original with { SchedulingBytesPerChildStart = -1 },
                7 => original with { JoinBytesPerAttempt = -1 },
                8 => original with { AwaitBytesPerConfiguredAwait = -1 },
                9 => original with { InitializationBytesPerInvocation = -1 },
                _ => original with { TotalFaultEpisodesPerInvocation = -1 }
            };
            AssertUnknown(Check(target, changed), "TaskRuntimeCostInputsInvalid");
        }
    }

    [Test]
    public void DeclaredAttemptAndPayloadProductsUseMathematicalArithmeticBeyondInt64()
    {
        var target = Prepare(16, true, OptimizationLevel.Release);
        var inputs = new NativeTaskRuntimeCostInputs(long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue,
            long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue);
        var check = Check(target, inputs);
        Assert.That(check.UnknownReason, Is.Null);
        var actual = check.CumulativeUpper!.Evaluate(_ => int.MaxValue, TestContext.CurrentContext.CancellationToken);
        Assert.That(actual, Is.GreaterThan(new BigInteger(long.MaxValue)));
        Assert.That(actual, Is.EqualTo(ManualSum(target.ForkJoinProjection!, inputs, int.MaxValue)));
    }

    [TestCase("carriers")]
    [TestCase("shapes")]
    [TestCase("root")]
    [TestCase("module")]
    [TestCase("emitted-inventory")]
    [TestCase("later-emitted-body")]
    [TestCase("later-child-owner")]
    [TestCase("later-child-storage")]
    public void MissingOrMalformedOwnershipNeverReturnsAnEarlierPartialBound(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var target = Prepare(2, true, OptimizationLevel.Release, distinctLastChild: true);
        var projection = target.ForkJoinProjection!;
        var shapes = projection.Carriers!.Shapes!;
        var inventory = shapes.EmittedFaultBodies!.Inventory!;
        Assert.That(inventory.Bodies, Has.Length.EqualTo(2));
        switch (mutation)
        {
            case "carriers":
                projection = projection with { Carriers = null };
                break;
            case "shapes":
                projection = projection with { Carriers = projection.Carriers! with { Shapes = null } };
                break;
            case "root":
                projection = projection with { RootCallableId = "foreign" };
                break;
            case "module":
                shapes = shapes with { ModuleSha256 = new string('a', 64) };
                break;
            case "emitted-inventory":
                shapes = shapes with { EmittedFaultBodies = null };
                break;
            case "later-emitted-body":
                var later = inventory.Bodies[1];
                shapes = shapes with
                {
                    EmittedFaultBodies = new(inventory with
                    {
                        Bodies = inventory.Bodies.SetItem(1,
                    later with { CodeSha256 = new string('a', 64) })
                    }, null)
                };
                break;
            case "later-child-owner":
                var child = projection.Children[1];
                projection = projection with
                {
                    Children = projection.Children.SetItem(1,
                    child with { Owner = child.Owner with { EffectsCompleteAtEntry = false } })
                };
                break;
            case "later-child-storage":
                projection = projection with
                {
                    Children = projection.Children.SetItem(1,
                    projection.Children[1] with { Cost = ResourceProgram.Array(ResourceExpression.One) })
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        if (mutation is "module" or "emitted-inventory" or "later-emitted-body")
        { projection = projection with { Carriers = projection.Carriers! with { Shapes = shapes } }; }
        var check = NativeTaskRuntimeCostVerifier.Check(projection, target.TotalEntry!, Layout(), Inputs(),
            TestContext.CurrentContext.CancellationToken);
        Assert.That(check.UnknownReason, Is.Not.Null.And.Not.Empty);
        Assert.That(check.CumulativeUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReturnedFactoryAndConfiguredAsyncCatchKeepTheirDistinctCompilerObligations(bool configured)
    {
        var target = Prepare(2, configured, OptimizationLevel.Release);
        var projection = target.ForkJoinProjection!;
        var shapes = projection.Carriers!.Shapes! with { RootExceptionBoundary = null };
        projection = projection with { Carriers = projection.Carriers! with { Shapes = shapes } };
        var check = NativeTaskRuntimeCostVerifier.Check(projection, target.TotalEntry!, Layout(), Inputs(),
            TestContext.CurrentContext.CancellationToken);
        if (configured)
        { AssertUnknown(check, "TaskRootExceptionBoundaryUnavailable"); }
        else
        {
            // The owned returned expression has no authored async catch. Its
            // factory/helper fault costs are still declared runtime quantities.
            Assert.That(check.UnknownReason, Is.Null);
            Assert.That(check.CumulativeUpper, Is.Not.Null);
        }
    }

    [Test]
    public void IncompatibleLayoutAndCancellationCannotPublishConditionalRuntimeEvidence()
    {
        var target = Prepare(2, true, OptimizationLevel.Release);
        var check = NativeTaskRuntimeCostVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!,
            new("CoreCLR", "8.0.32", "arm64", 8), Inputs(), TestContext.CurrentContext.CancellationToken);
        AssertUnknown(check, "IncompatibleResourceRuntimeLayout");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Action verify = () =>
        {
            _ = NativeTaskRuntimeCostVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!,
            Layout(), Inputs(), canceled.Token);
        };
        Assert.Throws<OperationCanceledException>(verify);
    }

    private static NativeTaskRuntimeCostCheck Check(CompilerCallablePreparation target, NativeTaskRuntimeCostInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(inputs);
        return NativeTaskRuntimeCostVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!, Layout(), inputs,
            TestContext.CurrentContext.CancellationToken);
    }

    private static NativeTaskRuntimeCostInputs Inputs()
    {
        // Synthetic arithmetic premises for this fixture, not deployment defaults.
        return new(2, 5, 3, 7, 11, 13, 17, 19, 23, 29, 31);
    }

    private static ResourceRuntimeLayoutContext Layout()
    {
        return new("CoreCLR", "8.0.32", "x64", 8);
    }

    private static BigInteger ManualSum(CompilerForkJoinProjection projection, NativeTaskRuntimeCostInputs inputs, int n)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(inputs);
        var shapes = projection.Carriers!.Shapes!;
        var compiler = ArrayBytes(projection.Children.Length, 8) + 64 * new BigInteger(shapes.MatchedCallbacks);
        foreach (var frame in shapes.CallbackFrames)
        { compiler += ObjectBytes(frame.Fields.Length); }
        if (shapes.StateMachine is { IsValueType: false } machine)
        { compiler += ObjectBytes(machine.Fields.Length); }
        var episode = new BigInteger(inputs.RawTraceArrayAttemptsPerFaultEpisode) * ArrayBytes(inputs.RawTraceArrayPayloadBytes, 1) +
            new BigInteger(inputs.ResolverTraceArrayAttemptsPerFaultEpisode) * ArrayBytes(inputs.ResolverTraceArraySlots, 8) +
            inputs.FaultConstructionBytesPerFaultEpisode + inputs.FreshResolverReferentBytesPerFaultEpisode;
        // Every source child runs n entries and allocates int[3] on each entry.
        var source = new BigInteger(projection.Children.Length) * n * 40;
        return source + compiler + new BigInteger(projection.Children.Length) * inputs.SchedulingBytesPerChildStart +
            inputs.JoinBytesPerAttempt + (projection.ConfiguredAwait ? inputs.AwaitBytesPerConfiguredAwait : 0) +
            new BigInteger(inputs.TotalFaultEpisodesPerInvocation) * episode + inputs.InitializationBytesPerInvocation;
    }

    private static BigInteger ArrayBytes(BigInteger count, int width)
    {
        return ((24 + count * width + 7) / 8) * 8;
    }

    private static BigInteger ObjectBytes(int fields)
    {
        return BigInteger.Max(24, ((16 + new BigInteger(fields) * 8 + 7) / 8) * 8);
    }

    private static void AssertUnknown(NativeTaskRuntimeCostCheck check, string reason)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(reason);
        Assert.That(check.UnknownReason, Is.EqualTo(reason));
        Assert.That(check.CumulativeUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
    }

    private static CompilerCallablePreparation Prepare(int children, bool configured, OptimizationLevel optimization,
        bool distinctLastChild = false)
    {
        var calls = Enumerable.Range(0, children).Select(index => "Task.Run(()=>" +
            (distinctLastChild && index == children - 1 ? "Other" : "Child") + "(n))");
        var source = "using SharpProof.Attributes;using System.Threading.Tasks;public static class C{" +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes,long.MaxValue)]public static " + (configured ? "async " : "") +
            "Task Target(int n)=>" + (configured ? "await " : "") + "Task.WhenAll(" + string.Join(",", calls) + ")" +
            (configured ? ".ConfigureAwait(false)" : "") + ";static void Child(int n){for(int i=0;i<n;i++){var values=new int[3];}}" +
            (distinctLastChild ? "static void Other(int n){for(int i=0;i<n;i++){var values=new int[3];}}" : "") + "}";
        var initial = TestCompilation.Create("TaskRuntimeCost", [("Subject.cs", source)]);
        var compilation = initial.WithReferences(ReviewedRuntimeReferences.Net8)
            .WithOptions(initial.Options.WithPlatform(Platform.X64).WithOptimizationLevel(optimization));
        TestCompilation.AssertNoErrors(compilation);
        var token = TestContext.CurrentContext.CancellationToken;
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All, token).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net8.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, token);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, token);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, token);
        var target = preparations.Single();
        Assert.That(target.ForkJoinProjection, Is.Not.Null, target.ResourceUnknownReason);
        Assert.That(target.TotalEntry, Is.Not.Null);
        Assert.That(target.ForkJoinProjection!.Carriers!.Shapes, Is.Not.Null);
        return target;
    }
}
