using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Element stores fault on a null array and an index outside it, and write
// Element state. Once a body writes elements, element reads are approximations.
[TestFixture]
public sealed class NativeArrayElementTests
{
    private const string Guarded = "Contract.Requires(values != null && values.Length > 2); ";

    [TestCase("Contract.Requires(values == null); values[0] = 1;", IrExceptionKind.NullReference)]
    [TestCase("Contract.Requires(values != null); values[0] = 1;", IrExceptionKind.IndexOutOfRange)]
    [TestCase("Contract.Requires(values != null); values[0]++;", IrExceptionKind.IndexOutOfRange)]
    public async Task StoresFaultOnNullAndBounds(string body, IrExceptionKind kind)
    {
        var result = await NativeExceptionEffectVerifier.VerifyAsync(Prepare("[DoesNotThrow] public static void Target(int[] values) { " +
            body + " }"), new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.ExceptionWitness!.Kind, Is.EqualTo(kind));
    }

    [Test]
    public async Task GuardedStoresNeitherThrowNorAllocate()
    {
        var preparation = Prepare("[DoesNotThrow, ZeroAllocations] public static void Target(int[] values) { " + Guarded +
            "values[1] = 5; values[2]++; values[0] += values[1]; }");
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exceptions.Outcome, Is.TypeOf<ProvenOutcome>(), exceptions.Reason.ToString());
            Assert.That(allocations.Outcome, Is.TypeOf<ProvenOutcome>(), allocations.Reason.ToString());
        }
    }

    [Test]
    public async Task StoresAreImpure()
    {
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare("[EnforcePure] public static void Target(int[] values) { " +
            Guarded + "values[0] = 1; }"), new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.WriteWitness, Is.Not.Null);
    }

    [TestCase("Contract.Ensures(Contract.Result<int>() == 2); values[0] = 1; return values[0];")]
    [TestCase("Contract.Ensures(values[0] == 2); values[0] = 1; return 0;")]
    public async Task ReadsAfterStoresAreNeverStale(string body)
    {
        // The entry value is 2; a pure read model would prove the stale value.
        var preparation = Prepare("public static int Target(int[] values) { " +
            "Contract.Requires(values != null && values.Length == 1 && values[0] == 2); " + body + " }", effects: false);
        Assert.That(await PostconditionAsync(preparation), Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    // Non-scalar elements are approximations, but bounds stay exact.
    [TestCase("string")]
    [TestCase("object")]
    [TestCase("T")]
    public async Task NonScalarElementReadsKeepExactBounds(string element)
    {
        var result = await NativeExceptionEffectVerifier.VerifyAsync(Prepare("[DoesNotThrow] public static " + element +
            " Target<T>(" + element + "[] values) { Contract.Requires(values != null && values.Length > 0); return values[0]; }"),
            new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    [Test]
    public async Task MultidimensionalReadsAreApproximations()
    {
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(Prepare(
            "[DoesNotThrow] public static long Target(long[,] grid) { Contract.Requires(grid != null); return grid[0, 1]; }"), new WorkerBudgets());
        var capabilities = await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(Prepare(
            "[AllowedCapabilities(SharpProofCapability.None)] public static long Target(long[,] grid) { return grid[0, 1]; }"), new WorkerBudgets());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exceptions.Outcome, Is.Not.TypeOf<ProvenOutcome>());
            Assert.That(exceptions.Outcome, Is.Not.TypeOf<RefutedOutcome>());
            Assert.That(capabilities.Outcome, Is.TypeOf<ProvenOutcome>(), capabilities.Reason.ToString());
        }
    }

    private static async Task<WorkerClaimOutcome> PostconditionAsync(CompilerCallablePreparation preparation)
    {
        var results = new List<WorkerClaimResult>();
        await TotalCallableVerifier.VerifyAsync(preparation, new WorkerBudgets(),
            check => results.Add(CallableClaimResultAssembler.FromTotal(preparation, check)), null, CancellationToken.None);
        return results.Count == 0 ? WorkerClaimOutcome.Unknown : results[^1].Outcome;
    }

    private static CompilerCallablePreparation Prepare(string method, bool effects = true)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; public static class C { " + method + " }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single(preparation => effects ? preparation.EffectClaims.Length != 0
            : preparation.Entry.CallableId.Contains("C.Target", StringComparison.Ordinal));
    }
}
