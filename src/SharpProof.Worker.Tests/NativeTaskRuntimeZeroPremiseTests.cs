using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeTaskRuntimeZeroPremiseTests
{
    private const string DeploymentHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string DeploymentEvidence = "synthetic conditional deployment declaration including cold initialization";
    private static readonly ImmutableArray<string> QuantityNames =
    [
        "RawTraceArrayAttemptsPerFaultEpisode", "RawTracePayloadBytesPerArrayAttempt",
        "ResolverArrayAttemptsPerFaultEpisode", "ResolverSlotsPerArrayAttempt",
        "FaultConstructionBytesPerFaultEpisode", "ResolverReferentBytesPerFaultEpisode",
        "SchedulingBytesPerChildStartAttempt", "JoinBytesPerJoinAttempt", "AwaitBytesPerConfiguredAwait",
        "InitializationBytesPerInvocation", "TotalFaultEpisodesPerInvocation"
    ];

    [TestCase(OptimizationLevel.Debug, false)]
    [TestCase(OptimizationLevel.Release, false)]
    [TestCase(OptimizationLevel.Debug, true)]
    [TestCase(OptimizationLevel.Release, true)]
    public async Task ExplicitZeroRuntimeQuantitiesRetainCompilerCostsAndEveryUsedPremise(OptimizationLevel optimization, bool configured)
    {
        // These are explicit synthetic hypotheses, not observed zero runtime
        // allocation or defaults inferred from the empty source child.
        ImmutableArray<long> quantities = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        var target = Prepare(optimization, configured, quantities);
        await AssertConditionalWorkflow(target, quantities, BigInteger.Zero);
    }

    [TestCase(OptimizationLevel.Debug, false)]
    [TestCase(OptimizationLevel.Release, false)]
    [TestCase(OptimizationLevel.Debug, true)]
    [TestCase(OptimizationLevel.Release, true)]
    public async Task NonzeroAttemptsWithZeroPayloadStillChargeEmptyArrayHeaders(OptimizationLevel optimization, bool configured)
    {
        ImmutableArray<long> quantities = [2, 0, 3, 0, 0, 0, 0, 0, 0, 0, 5];
        var target = Prepare(optimization, configured, quantities);
        // Under the declared x64 layout, byte[0] and object[0] each have a
        // 24-byte header. Five episodes attempt two raw and three resolver arrays.
        var runtimeBytes = new BigInteger(5) * (2 * 24 + 3 * 24);
        await AssertConditionalWorkflow(target, quantities, runtimeBytes);
    }

    private static async Task AssertConditionalWorkflow(CompilerCallablePreparation target,
        ImmutableArray<long> quantities, BigInteger runtimeBytes)
    {
        ArgumentNullException.ThrowIfNull(target);
        var projection = target.ForkJoinProjection!;
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x64", 8);
        var sites = NativeTaskEmittedFaultSiteVerifier.Check(projection, target.TotalEntry!, layout, cancellation);
        Assert.That(sites.HasSites, Is.True, sites.UnknownReason);
        Assert.That(sites.Sites!.Value, Is.Empty);
        Assert.That(projection.Carriers!.Shapes!.EmittedFaultBodies!.Inventory!.Bodies, Has.Length.EqualTo(1));
        var source = NativeForkJoinSourceCostVerifier.CheckChildStorage(projection, target.TotalEntry!, layout, cancellation);
        Assert.That(source.UnknownReason, Is.Null);
        Assert.That(source.CumulativeUpper!.Constant, Is.EqualTo(BigInteger.Zero));
        var compilerBytes = CompilerCarrierUpper(projection);
        Assert.That(compilerBytes, Is.GreaterThan(BigInteger.Zero));
        var declarations = projection.RuntimePremises!.Inventory!.Declarations;
        Assert.That(declarations.Quantities, Has.Length.EqualTo(11));
        var expectedIds = declarations.Quantities.Select(declaration => declaration is null ? throw new ArgumentNullException(nameof(declaration)) :
            ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(declarations, declaration)).ToHashSet(StringComparer.Ordinal);
        expectedIds.Add(ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(declarations));
        Assert.That(expectedIds, Has.Count.EqualTo(12));
        Assert.That(target.Entry.Assumptions.Where(static assumption => assumption is not null && assumption.Kind == WorkerAssumptionKind.UserAssume)
            .All(static assumption => assumption is not null && !assumption.Used), Is.True);

        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), cancellation)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.UnknownReason, Is.Null);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.EqualTo((compilerBytes + runtimeBytes).ToString(CultureInfo.InvariantCulture)));
        Assert.That(result.ResourceBound.ExecutionScope, Is.EqualTo(WorkerResourceModels.TaskRuntimeScope));
        Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(2));
        Assert.That(result.ProofCore, Does.Contain("resource-task-compiler-carriers:" + NativeTaskCarrierStorageVerifier.Model));
        Assert.That(result.ProofCore, Does.Contain("resource-task-runtime-model:" + NativeTaskRuntimeCostVerifier.Model));
        var evidence = result.ResourceBound.RuntimePremises!;
        Assert.That(evidence, Is.Not.Null);
        Assert.That(evidence.DeploymentManifestSha256, Is.EqualTo(DeploymentHash));
        Assert.That(evidence.DeploymentEvidence, Is.EqualTo(DeploymentEvidence));
        Assert.That(evidence.DeploymentAssumptionId, Is.EqualTo(ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(declarations)));
        Assert.That(evidence.Quantities, Has.Length.EqualTo(11));
        for (var index = 0; index < QuantityNames.Length; index++)
        {
            Assert.That(declarations.Quantities[index].Quantity.ToString(), Is.EqualTo(QuantityNames[index]));
            Assert.That(declarations.Quantities[index].UpperBound, Is.EqualTo(quantities[index]));
            var quantity = evidence.Quantities[index];
            Assert.That(quantity.Quantity.ToString(), Is.EqualTo(QuantityNames[index]));
            Assert.That(quantity.UpperBound, Is.EqualTo(quantities[index]));
            Assert.That(quantity.Evidence, Is.EqualTo("explicit synthetic quantity " + QuantityNames[index]));
            Assert.That(quantity.AssumptionId, Is.EqualTo(ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(declarations, declarations.Quantities[index])));
        }
        var usedIds = result.Assumptions.Where(static assumption => assumption is not null &&
                assumption.Kind == WorkerAssumptionKind.UserAssume && assumption.Used)
            .Select(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id).ToArray();
        Assert.That(usedIds, Has.Length.EqualTo(12));
        Assert.That(usedIds, Is.EquivalentTo(expectedIds));
    }

    private static BigInteger CompilerCarrierUpper(CompilerForkJoinProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var shapes = projection.Carriers!.Shapes!;
        // Independently sum the owned Task[] header/references, delegate
        // instances and pointer-slot upper layout for compiler carrier fields.
        BigInteger bytes = 24 + 8 * new BigInteger(projection.Children.Length) + 64 * new BigInteger(shapes.MatchedCallbacks);
        foreach (var frame in shapes.CallbackFrames)
        { bytes += BigInteger.Max(24, 16 + 8 * new BigInteger(frame.Fields.Length)); }
        if (shapes.StateMachine is { IsValueType: false } state)
        { bytes += BigInteger.Max(24, 16 + 8 * new BigInteger(state.Fields.Length)); }
        return bytes;
    }

    private static CompilerCallablePreparation Prepare(OptimizationLevel optimization, bool configured, ImmutableArray<long> quantities)
    {
        if (quantities.IsDefault || quantities.Length != QuantityNames.Length || quantities.Any(static value => value < 0))
        { throw new ArgumentException("Every synthetic runtime quantity must be supplied explicitly.", nameof(quantities)); }
        var attributes = new StringBuilder("[ResourceTaskDeployment(ResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed,\"");
        attributes.Append(DeploymentHash).Append("\",\"").Append(DeploymentEvidence).Append("\")]");
        for (var index = 0; index < QuantityNames.Length; index++)
        {
            attributes.Append("[ResourceTaskRuntimeUpperBound(ResourceTaskRuntimeQuantity.").Append(QuantityNames[index])
                .Append(',').Append(quantities[index].ToString(CultureInfo.InvariantCulture))
                .Append("L,\"explicit synthetic quantity ").Append(QuantityNames[index]).Append("\")]");
        }
        var source = "using SharpProof.Attributes;using System.Threading.Tasks;public static class C{" + attributes +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes,1000000L)]public static " + (configured ? "async " : "") +
            "Task Target(int n)=>" + (configured ? "await " : "") +
            "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(n)))" + (configured ? ".ConfigureAwait(false)" : "") +
            ";static void Child(int n){}}";
        var initial = TestCompilation.Create("TaskRuntimeZeroPremises", [("Subject.cs", source)]);
        const string references = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.32/ref/net8.0";
        var compilation = initial.WithReferences(Directory.GetFiles(references, "*.dll")
            .Append(typeof(ResourceUpperBoundAttribute).Assembly.Location)
            .Select(static path => path is null ? throw new ArgumentNullException(nameof(path)) : MetadataReference.CreateFromFile(path)))
            .WithOptions(initial.Options.WithPlatform(Platform.X64).WithOptimizationLevel(optimization));
        TestCompilation.AssertNoErrors(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All, cancellation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net8.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, cancellation);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellation);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, cancellation);
        var target = preparations.Single();
        Assert.That(target.ForkJoinProjection, Is.Not.Null, target.ResourceUnknownReason);
        Assert.That(target.TotalEntry, Is.Not.Null);
        return target;
    }
}
