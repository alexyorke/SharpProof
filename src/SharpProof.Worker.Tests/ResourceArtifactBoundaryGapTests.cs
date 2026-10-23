using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [Test]
    public void ResourceArtifactProducerRejectsSimultaneousProofAndGapBeforePublishingACallable()
    {
        var preparation = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 3)]", "var allocated = new int[3];")));
        Assert.That(preparation.ResourceProgram, Is.Not.Null);
        var contradictory = preparation with { ResourceUnknownReason = "UnprovedProducerBoundary" };
        CompilerCallableArtifact? encoded = null;
        Action encode = () => { encoded = CompilerLoweredArtifact.Encode(contradictory, TestContext.CurrentContext.CancellationToken); };
        var exception = Assert.Throws<InvalidDataException>(encode);
        Assert.That(exception!.Message, Is.EqualTo("Resource proof and gap are mutually exclusive."));
        Assert.That(encoded, Is.Null);
    }

    [Test]
    public void ResourceArtifactConsumerRejectsSimultaneousProofAndGapInTheActualEnvelope()
    {
        var artifact = CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 3)]", "var allocated = new int[3];"));
        Assert.That(Read(artifact).ResourceProgram, Is.Not.Null);
        var callable = artifact.Callables.Single() with { ResourceUnknownReason = "UnprovedProducerBoundary" };
        artifact = artifact with { Callables = artifact.Callables.SetItem(0, callable) };
        Rejects(() => { _ = Read(artifact); });
    }

    [Test]
    public async Task ResourceArtifactWireBudgetDropsTheEntireProofAndReportsUnknown()
    {
        var artifact = CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 3)]", "var allocated = new int[3];"));
        var preparation = Read(artifact);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var admitted = CompilerLoweredArtifact.Encode(preparation, cancellation);
        Assert.That(admitted.ResourceProgram, Is.Not.Null.And.Not.Empty);
        Assert.That(admitted.ResourceUnknownReason, Is.Null);

        // Every literal fits its own representation budget. Doubling a shared
        // immutable subtree expands its wire representation, while its counted
        // nodes and depth remain below the structural program limits.
        var program = ResourceProgram.Array(ResourceExpression.Literal(BigInteger.Pow(10, 4000)));
        Assert.That(ResourceProgramCodec.Encode(program, cancellation).Length, Is.LessThan(ResourceProgramCodec.MaximumPayloadBytes));
        for (var level = 0; level < 8; level++)
        { program = ResourceProgram.Sequence(program, program); }
        ResourceProgramOwnership.Validate(program, preparation.Entry.CallableId, preparation.Total!, cancellation);
        Action oversized = () => { _ = ResourceProgramCodec.Encode(program, cancellation); };
        Assert.Throws<ResourceExpressionLimitException>(oversized);

        var encoded = CompilerLoweredArtifact.Encode(preparation with { ResourceProgram = program }, cancellation);
        Assert.That(encoded.ResourceProgram, Is.Null);
        Assert.That(encoded.ResourceUnknownReason, Is.EqualTo("ResourceProofBudgetExceeded"));
        artifact = artifact with { Callables = artifact.Callables.SetItem(0, encoded) };
        var decoded = Read(artifact);
        Assert.That(decoded.ResourceProgram, Is.Null);
        Assert.That(decoded.ResourceUnknownReason, Is.EqualTo("ResourceProofBudgetExceeded"));
        var result = (await NativeEffectClaims.VerifyAsync(decoded, CallableEntryFeasibility.Feasible, new WorkerBudgets(), cancellation)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceProofBudgetExceeded"));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.ResourceBound.Asymptotic, Is.Null);
        Assert.That(result.ResourceBound.Expected, Is.Null);
    }

    [TestCase("child")]
    [TestCase("owner")]
    [TestCase("cost")]
    [TestCase("arguments")]
    public void ResourceArtifactProjectionRejectsAMalformedLaterChildWithoutPublishingItsPrefix(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var preparation = ResourceArtifactTaskPreparation();
        var projection = preparation.ForkJoinProjection!;
        var root = preparation.TotalEntry!;
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var payload = CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation);
        var admitted = CompilerForkJoinProjectionCodec.Decode(payload, root, cancellation);
        Assert.That(NativeForkJoinSourceCostVerifier.Check(admitted, root, cancellation).HasSourceCosts, Is.True);
        var child = projection.Children[1];
        if (mutation == "owner")
        {
            Action construct = () => { _ = new CompilerForkJoinChild(null!, child.Cost, child.Arguments); };
            Assert.Throws<ArgumentNullException>(construct);
        }
        else if (mutation == "cost")
        {
            Action construct = () => { _ = new CompilerForkJoinChild(child.Owner, null!, child.Arguments); };
            Assert.Throws<ArgumentNullException>(construct);
        }
        else if (mutation == "arguments")
        {
            Action construct = () => { _ = new CompilerForkJoinChild(child.Owner, child.Cost, default); };
            Assert.Throws<ArgumentException>(construct);
        }
        // Constructor checks run before an ordinary malformed child can exist.
        // Record updates are a supported mutation boundary and must also be
        // rejected by the encoder after a valid first child has been written.
        var malformed = mutation switch
        {
            "child" => null!,
            "owner" => child with { Owner = null! },
            "cost" => child with { Cost = null! },
            "arguments" => child with { Arguments = default },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        projection = projection with { Children = projection.Children.SetItem(1, malformed) };
        byte[]? encoded = null;
        Action encode = () => { encoded = CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation); };
        var exception = Assert.Throws<InvalidDataException>(encode);
        Assert.That(exception!.Message, Is.EqualTo("Fork-join child is malformed."));
        Assert.That(encoded, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ResourceArtifactJsonRejectsAMalformedLaterChildWithoutPublishingADecodedPrefix(bool arrayInsteadOfNull)
    {
        var preparation = ResourceArtifactTaskPreparation();
        var root = preparation.TotalEntry!;
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var payload = CompilerForkJoinProjectionCodec.Encode(preparation.ForkJoinProjection!, root, cancellation);
        var json = JsonNode.Parse(payload)!;
        json["children"]![1] = arrayInsteadOfNull ? new JsonArray() : null;
        CompilerForkJoinProjection? decoded = null;
        Action decode = () => { decoded = CompilerForkJoinProjectionCodec.Decode(Encoding.UTF8.GetBytes(json.ToJsonString()), root, cancellation); };
        var exception = Assert.Throws<InvalidDataException>(decode);
        Assert.That(exception!.Message, Is.EqualTo("Fork-join projection object is malformed."));
        Assert.That(decoded, Is.Null);
    }

    private static CompilerCallablePreparation ResourceArtifactTaskPreparation()
    {
        var artifact = CreateArtifact("using SharpProof.Attributes; using System.Threading.Tasks; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,100)] public static Task Target(int n) => " +
            "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); " +
            "static void Child(int count) {for(int i=0;i<count;i++){var values=new int[2];}} }", reviewedRuntime: true);
        var preparation = Read(artifact);
        Assert.That(preparation.ForkJoinProjection, Is.Not.Null);
        Assert.That(preparation.TotalEntry, Is.Not.Null);
        return preparation;
    }
}
