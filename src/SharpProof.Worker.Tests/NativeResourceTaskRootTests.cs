using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Specs;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    private static CompilerCallablePreparation TaskRoot(string attributes, string child = "for(int i=0;i<count;i++){}", bool configuredAwait = false, bool x64 = false)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(child);
        return Read(CreateArtifact("using SharpProof.Attributes; using System.Threading.Tasks; public static class C { " +
            attributes + " public static " + (configuredAwait ? "async " : "") + "Task Target(int n) => " + (configuredAwait ? "await " : "") +
            "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3)))" + (configuredAwait ? ".ConfigureAwait(false)" : "") +
            "; static void Child(int count) {" + child + "} }", reviewedRuntime: true, x64: x64));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TaskRootPublishesCheckedLoopUpperWithExplicitConditionalScope(bool configuredAwait)
    {
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric.LoopBodyEntries,3,nameof(n),1)]", configuredAwait: configuredAwait);
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(target.Total, Is.Null);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("(max(0, n) + 3)"));
        Assert.That(result.ResourceBound.ExecutionScope, Is.EqualTo(WorkerResourceModels.TaskJoinScope));
        Assert.That(result.ResourceBound.Asymptotic!.UpperBound, Is.EqualTo("O(n)"));
        Assert.That(result.ResourceBound.Asymptotic.Lower!.UnknownReason, Is.EqualTo("ResourceLowerTaskDomainUnproved"));
        Assert.That(result.ResourceBound.Expected!.UnknownReason, Is.EqualTo("ResourceExpectedTaskModelUnsupported"));
        Assert.That(result.ResourceBound.Accounting, Is.Empty);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        var dependency = result.ResourceBound.MetadataAssumptions.Single();
        Assert.That(target.ForkJoinProjection!.ConfiguredAwait, Is.EqualTo(configuredAwait));
        Assert.That(dependency.RuntimeModel, Is.EqualTo(configuredAwait ? WorkerResourceModels.ConfiguredTaskJoinModel : WorkerResourceModels.TaskJoinModel));
        Assert.That(result.Assumptions.Single().Id, Is.EqualTo(dependency.AssumptionId));
        Assert.That(result.Assumptions.Single().Used, Is.True);
        Assert.That(result.ProofCore, Has.Some.StartsWith("resource-task-projection:"));
    }

    [TestCase("throw new System.InvalidOperationException();", "")]
    [TestCase("Shared++;", "static int Shared;")]
    [TestCase("lock(Gate) { Shared++; }", "static readonly object Gate = new(); static int Shared;")]
    [TestCase("System.Threading.Interlocked.Increment(ref Shared);", "static int Shared;")]
    [TestCase("System.Threading.CancellationToken.None.ThrowIfCancellationRequested();", "")]
    public async Task TaskChildrenWithUnmodeledCompletionOrSharedStatePublishNoResourceProof(string child, string extra)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(extra);
        var target = Read(CreateArtifact("using SharpProof.Attributes; using System.Threading.Tasks; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,100)] " +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes,1000)] " +
            "[ResourceUpperBound(ResourceMetric.PeakAuxiliaryBytes,1000)] " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,1000)] " +
            "public static Task Target(int n) => Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); " +
            "static void Child(int count) {" + child + "} " + extra + "}", reviewedRuntime: true, x64: true));
        Assert.That(target.ForkJoinProjection, Is.Null);
        var results = await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        Assert.That(results, Has.Length.EqualTo(4));
        foreach (var result in results)
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(result.ResourceBound!.UnknownReason, Is.Not.Null.And.Not.Empty);
            Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
            Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
            Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RepeatedTaskVerificationKeepsBoundsPerInvocationAndDoesNotCacheAnInvalidChild(bool configured)
    {
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric.PeakAuxiliaryBytes,1000)]",
            configuredAwait: configured, x64: true);
        var projection = target.ForkJoinProjection!;
        var child = projection.Children[1];
        var invalid = target with
        {
            ForkJoinProjection = projection with
            { Children = [projection.Children[0], child with { Owner = child.Owner with { EffectsCompleteAtEntry = false } }] }
        };
        string? firstBound = null;
        for (var invocation = 0; invocation < 4; invocation++)
        {
            var rejected = (await NativeEffectClaims.VerifyAsync(invalid, CallableEntryFeasibility.Feasible,
                new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
            Assert.That(rejected.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(rejected.ResourceBound!.InferredUpperBound, Is.Null);
            var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
                new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
            firstBound ??= result.ResourceBound!.InferredUpperBound;
            Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo(firstBound));
            Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(3));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TaskRootCannotExchangeConfiguredAndReturnedRuntimePremises(bool configuredAwait)
    {
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric.LoopBodyEntries,3,nameof(n),1)]", configuredAwait: configuredAwait);
        Assert.That(target.ForkJoinProjection, Is.Not.Null);
        target = target with { ForkJoinProjection = target.ForkJoinProjection! with { ConfiguredAwait = !configuredAwait } };
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ForkJoinRuntimePremiseNotBound"));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
    }

    [Test]
    public async Task ConfiguredTaskResourceEntryDoesNotBypassAdditionalSourceContracts()
    {
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric.LoopBodyEntries,3,nameof(n),1)] [DoesNotThrow]", configuredAwait: true);
        Assert.That(target.Total, Is.Null);
        Assert.That(target.TotalEntry, Is.Null);
        Assert.That(target.ForkJoinProjection, Is.Null);
        var results = await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        var resource = results.Single(static result => result is null ? throw new ArgumentNullException(nameof(result)) : result.ResourceBound != null);
        Assert.That(resource.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(resource.ResourceBound!.InferredUpperBound, Is.Null);
    }

    [TestCase("AllocatedBytes")]
    [TestCase("SourceActivationDepth")]
    [TestCase("SourceStackBytes")]
    public async Task TaskRootDoesNotPublishUnmodeledMetricBounds(string metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric." + metric + ",100)]");
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ForkJoinResourceMetricUnsupported"));
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
    }

    [TestCase("ArrayAllocations", 4, 1, "((max(0, n) + 3) + 1)", false)]
    [TestCase("ArrayAllocations", 4, 1, "((max(0, n) + 3) + 1)", true)]
    [TestCase("AllocatedElements", 8, 2, "(((max(0, n) * 2) + 6) + 2)", false)]
    [TestCase("AllocatedElements", 8, 2, "(((max(0, n) * 2) + 6) + 2)", true)]
    public async Task TaskSourceAllocationPrefixesIncludeTheParamsArray(string metric, int constant, int coefficient, string expected, bool configured)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentNullException.ThrowIfNull(expected);
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric." + metric + "," + constant + ",nameof(n)," + coefficient + ")]",
            child: "for(int i=0;i<count;i++){var data=new int[2];}", configuredAwait: configured);
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(),
            TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ExecutionScope, Is.EqualTo(WorkerResourceModels.TaskSourceAllocationScope));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.EqualTo(expected));
        Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(1));
        Assert.That(result.ResourceBound.Asymptotic!.UpperBound, Is.EqualTo("O(n)"));
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.UnknownReason, Is.Null);
    }

    [TestCase("ArrayAllocations", 1, true)]
    [TestCase("ArrayAllocations", 0, false)]
    [TestCase("AllocatedElements", 2, true)]
    [TestCase("AllocatedElements", 1, false)]
    public async Task EmptyTaskCallbacksStillRequireTheSourceParamsAllocation(string metric, int limit, bool proven)
    {
        ArgumentNullException.ThrowIfNull(metric);
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric." + metric + "," + limit + ")]", child: "");
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(),
            TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(proven ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
        if (proven)
        {
            Assert.That(result.ResourceBound.InferredUpperBound, Is.EqualTo(limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(result.ResourceBound.Asymptotic!.UpperBound, Is.EqualTo("O(1)"));
            Assert.That(result.ResourceBound.UnknownReason, Is.Null);
        }
        else
        {
            Assert.That(result.ResourceBound.UnknownReason, Is.EqualTo("ForkJoinBoundComparisonUnproved"));
            Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        }
    }

    [TestCase(-5, false)]
    [TestCase(0, false)]
    [TestCase(7, false)]
    [TestCase(-5, true)]
    [TestCase(0, true)]
    [TestCase(7, true)]
    public async Task TaskSourceAllocationBoundsCoverIndependentConcurrentExecutions(int input, bool configured)
    {
        const string join = "Task.WhenAll(new Task[] { Task.Run(()=>Child(n)),Task.Run(()=>Child(3)) })";
        var source = "using SharpProof.Attributes; using System.Threading.Tasks; public static class C { " +
            "public static int Arrays, Elements; " +
            "static T[] Observe<T>(T[] value) {System.Threading.Interlocked.Increment(ref Arrays); System.Threading.Interlocked.Add(ref Elements,value.Length); return value;} " +
            "[ResourceUpperBound(ResourceMetric.ArrayAllocations,4,nameof(n),1)] " +
            "[ResourceUpperBound(ResourceMetric.AllocatedElements,8,nameof(n),2)] public static " +
            (configured ? "async " : "") + "Task Target(int n) => " + (configured ? "await " : "") + join +
            (configured ? ".ConfigureAwait(false)" : "") + "; static void Child(int count) {for(int i=0;i<count;i++){var data=new int[2];}} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true));
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var results = await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), cancellation);
        Assert.That(results, Has.Length.EqualTo(2));
        Assert.That(results.All(static result => result is null ? throw new ArgumentNullException(nameof(result)) : result.Outcome == WorkerClaimOutcome.Proven), Is.True);
        var cost = NativeForkJoinSourceCostVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!, cancellation).Upper!;
        var arrayUpper = cost.Arrays.Evaluate(size => size is null ? throw new ArgumentNullException(nameof(size)) : input, cancellation) + 1;
        var elementUpper = cost.Elements.Evaluate(size => size is null ? throw new ArgumentNullException(nameof(size)) : input, cancellation) + 2;
        // Observe successful source allocation events in a separately emitted
        // execution. Instrumentation is not submitted as proof evidence.
        var observed = source.Replace(join, "Task.WhenAll(Observe(new Task[] { Task.Run(()=>Child(n)),Task.Run(()=>Child(3)) }))", StringComparison.Ordinal)
            .Replace("new int[2]", "Observe(new int[2])", StringComparison.Ordinal);
        var compilation = TestCompilation.Create("TaskSourceObservation", ("Observed.cs", observed));
        using var stream = new System.IO.MemoryStream();
        var emitted = compilation.Emit(stream, cancellationToken: cancellation);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("TaskSourceObservation", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(stream).GetType("C")!;
            var method = type.GetMethod("Target")!;
            var arrays = type.GetField("Arrays")!;
            var elements = type.GetField("Elements")!;
            for (var invocation = 0; invocation < 3; invocation++)
            {
                arrays.SetValue(null, 0);
                elements.SetValue(null, 0);
                await (Task)method.Invoke(null, new object[] { input })!;
                Assert.That(new System.Numerics.BigInteger((int)arrays.GetValue(null)!), Is.EqualTo(arrayUpper));
                Assert.That(new System.Numerics.BigInteger((int)elements.GetValue(null)!), Is.EqualTo(elementUpper));
            }
        }
        finally { context.Unload(); }
    }
    [TestCase("PeakAuxiliaryBytes", false)]
    [TestCase("PeakAuxiliaryBytes", true)]
    [TestCase("RetainedBytes", false)]
    [TestCase("RetainedBytes", true)]
    public async Task TaskLifetimePublishesOnlyWithOwnedRuntimeLayoutAndCompilerEvidence(string metric, bool configured)
    {
        ArgumentNullException.ThrowIfNull(metric);
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric." + metric + ",1000)]", configuredAwait: configured, x64: true);
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(),
            TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        var memory = NativeTaskStorageVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        var expected = metric == "PeakAuxiliaryBytes" ? memory.PeakUpper : memory.RetainedUpper;
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo(expected!.ToDisplayString()));
        Assert.That(result.ResourceBound.ExecutionScope, Is.EqualTo(WorkerResourceModels.TaskStorageScope));
        Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(3));
        Assert.That(result.Assumptions.Count(static assumption => assumption is not null && assumption.Used), Is.EqualTo(3));
        Assert.That(result.ResourceBound.MetadataAssumptions, Has.Some.Matches<WorkerResourceMetadataAssumption>(dependency =>
            dependency is not null && dependency.RuntimeModel == (configured ? ResourceTaskStorageCatalog.ConfiguredModel : ResourceTaskStorageCatalog.Model)));
        Assert.That(result.ProofCore, Does.Contain("resource-task-compiler-carriers:" + NativeTaskCarrierStorageVerifier.Model));
        Assert.That(result.ResourceBound.Asymptotic!.UpperBound, Is.EqualTo("O(1)"));
        Assert.That(result.ResourceBound.Asymptotic.Lower!.UnknownReason, Is.Not.Null);
        Assert.That(result.ResourceBound.Expected!.UnknownReason, Is.Not.Null);
        Assert.That(result.ResourceBound.Accounting, Is.Empty);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
    }

    [TestCase("layout", "TaskStorageLayoutPremiseNotBound")]
    [TestCase("storage-premise", "TaskStorageRuntimePremiseNotBound")]
    [TestCase("source-premise", "ForkJoinRuntimePremiseNotBound")]
    [TestCase("missing-capture", "TaskCompilerCarrierEvidenceUnavailable")]
    [TestCase("failed-capture", "TaskCompilerCarrierEvidenceUnavailable")]
    [TestCase("later-child", "TaskCompilerCarrierOwnershipUnproved")]
    [TestCase("later-frame", "TaskCompilerCarrierShapeUnsupported")]
    [TestCase("bound", "ForkJoinLifetimeBoundComparisonUnproved")]
    public async Task TaskLifetimeFailuresReturnUnknownWithoutPartialNumericEvidence(string mutation, string reason)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(reason);
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric.PeakAuxiliaryBytes," + (mutation == "bound" ? "100" : "1000") + ")]",
            x64: mutation != "layout");
        var projection = target.ForkJoinProjection!;
        var storageId = ResourceTaskStorageCatalog.AssumptionId(target.Entry.CallableId, projection.FrameworkAssemblyIdentity, false);
        var sourceId = ResourceTaskCatalog.AssumptionId(target.Entry.CallableId, projection.FrameworkAssemblyIdentity);
        if (mutation is "storage-premise" or "source-premise")
        {
            var id = mutation == "storage-premise" ? storageId : sourceId;
            target.Entry.Assumptions = target.Entry.Assumptions.Where(assumption => assumption is not null && assumption.Id != id).ToArray();
        }
        if (mutation == "missing-capture")
        { target = target with { ForkJoinProjection = projection with { Carriers = null } }; }
        if (mutation == "failed-capture")
        { target = target with { ForkJoinProjection = projection with { Carriers = new(null, "TaskCarrierEmissionFailed") } }; }
        if (mutation == "later-child")
        {
            var child = projection.Children[1];
            target = target with { ForkJoinProjection = projection with { Children = [projection.Children[0], child with { Owner = child.Owner with { EffectsCompleteAtEntry = false } }] } };
        }
        if (mutation == "later-frame")
        {
            var capture = projection.Carriers!;
            var shapes = capture.Shapes!;
            var frames = shapes.CallbackFrames.SetItem(1, shapes.CallbackFrames[1] with { Fields = [ResourceStorageKind.Unspecified] });
            target = target with { ForkJoinProjection = projection with { Carriers = capture with { Shapes = shapes with { CallbackFrames = frames } } } };
        }
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(),
            TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.EffectWitness, Is.Null);
    }

    [TestCase("bound", "ForkJoinBoundComparisonUnproved")]
    [TestCase("premise", "ForkJoinRuntimePremiseNotBound")]
    [TestCase("child", "ForkJoinChildOwnershipInvalid")]
    public async Task TaskRootFailuresReturnUnknownWithoutPartialOrExactEvidence(string mutation, string reason)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(reason);
        var target = TaskRoot(mutation == "bound" ? "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,2,nameof(n),1)]" :
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,3,nameof(n),1)]");
        if (mutation == "premise")
        { target.Entry.Assumptions = []; }
        if (mutation == "child")
        {
            var projection = target.ForkJoinProjection!;
            var child = projection.Children[1];
            target = target with { ForkJoinProjection = projection with { Children = [projection.Children[0], child with { Owner = child.Owner with { EffectsCompleteAtEntry = false } }] } };
        }
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.EffectWitness, Is.Null);
    }

    [TestCase(-5)]
    [TestCase(0)]
    [TestCase(7)]
    [TestCase(100)]
    public async Task TaskLoopSourceUpperContainsIndependentlyCountedConcurrentExecutions(int count)
    {
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric.LoopBodyEntries,3,nameof(n),1)]");
        var checkedCost = NativeForkJoinSourceCostVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!, TestContext.CurrentContext.CancellationToken);
        var observed = 0;
        void Count(int bound)
        {
            for (var index = 0; index < bound; index++)
            { Interlocked.Increment(ref observed); }
        }
        await Task.WhenAll(Task.Run(() => Count(count)), Task.Run(() => Count(3)));
        var upper = checkedCost.Upper!.LoopEntries.Evaluate(input =>
        {
            ArgumentNullException.ThrowIfNull(input);
            return new System.Numerics.BigInteger(Math.Max(0, count));
        }, TestContext.CurrentContext.CancellationToken);
        Assert.That(new System.Numerics.BigInteger(observed), Is.LessThanOrEqualTo(upper));
    }

    [Test]
    public void TaskDeclaredLimitBindsToOwnedEntryWithoutInventingATotalBody()
    {
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric.LoopBodyEntries,3,nameof(n),1)]");
        var entry = target.TotalEntry!;
        var expression = ResourceExpressionCodec.DecodeForCallable(target.EffectClaims.Single().Constraint.ResourceUpperBound!.UpperBound,
            target.Entry.CallableId, entry, TestContext.CurrentContext.CancellationToken);
        Assert.That(expression.ToDisplayString(), Does.Contain("n"));
        Action wrongOwner = () => ResourceExpressionOwnership.Validate(expression, "foreign", entry, default);
        Assert.Throws<InvalidDataException>(wrongOwner);
        var badInput = ResourceExpression.InputSize(new ResourceInput(2, ResourceInputKind.NonNegativeScalar, "foreign", int.MaxValue));
        Action wrongInput = () => ResourceExpressionOwnership.Validate(badInput, entry.CallableId, entry, default);
        Assert.Throws<InvalidDataException>(wrongInput);
    }
}
