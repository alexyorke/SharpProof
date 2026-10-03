using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A call that is neither inlined nor modeled is opaque: unknown result, an
// exception of unknown type, and unknown effects.
[TestFixture]
public sealed class NativeOpaqueCallTests
{
    [TestCase("version.GetHashCode()")]
    [TestCase("version.Major")]
    [TestCase("System.Environment.TickCount")]
    public async Task UnknownResultsDoNotBlockUnrelatedPostconditions(string call)
    {
        var preparation = Prepare("public static int Target(System.Version version, int value) { " +
            "Contract.Ensures(Contract.Result<int>() == value); var ignored = " + call + "; return value; }");
        Assert.That(await PostconditionAsync(preparation), Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [Test]
    public async Task PostconditionOnAnUnknownResultIsNeverRefuted()
    {
        var preparation = Prepare("public static int Target(System.Version version) { " +
            "Contract.Ensures(Contract.Result<int>() == 1); return version.GetHashCode(); }");
        Assert.That(await PostconditionAsync(preparation), Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase("System.Array.Sort(items); return items[0];")]
    [TestCase("var first = items[0]; System.Array.Sort(items); return first;")]
    public async Task OpaqueCallsKeepNoArrayElementReads(string body)
    {
        // The callee may write any array, and element reads are pure in the IR.
        var preparation = Prepare("public static int Target(int[] items) { " +
            "Contract.Requires(items != null && items.Length == 2 && items[0] == 2 && items[1] == 1); " +
            "Contract.Ensures(Contract.Result<int>() == 2); " + body + " }");
        Assert.That(preparation.Total?.IsBodyAbstraction != false, Is.True);
        Assert.That(await PostconditionAsync(preparation), Is.Not.EqualTo(WorkerClaimOutcome.Refuted));
    }

    [Test]
    public async Task OpaqueCallsMayThrowUnlessACatchAllHandlesThem()
    {
        var uncaught = await ExceptionsAsync("Contract.Requires(version != null); return version.GetHashCode();");
        var caught = await ExceptionsAsync("try { return version.GetHashCode(); } catch (System.Exception) { return 0; }");
        var bare = await ExceptionsAsync("try { return version.GetHashCode(); } catch { return 0; }");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(uncaught.Outcome, Is.Not.TypeOf<ProvenOutcome>());
            Assert.That(uncaught.Outcome, Is.Not.TypeOf<RefutedOutcome>());
            Assert.That(caught.Outcome, Is.TypeOf<ProvenOutcome>(), caught.Reason.ToString());
            Assert.That(bare.Outcome, Is.TypeOf<ProvenOutcome>(), bare.Reason.ToString());
        }
    }

    [Test]
    public async Task NarrowHandlersAroundOpaqueCallsAbstain()
    {
        // The unknown exception may or may not be an ArgumentException.
        var result = await ExceptionsAsync("try { return version.GetHashCode(); } catch (System.ArgumentException) { return 0; }");
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    [TestCase("Contract.Requires(x > 0); return x > 0 ? x : version.GetHashCode();", true)]
    [TestCase("Contract.Requires(version != null); return version.GetHashCode();", false)]
    public async Task ReachableOpaqueCallsBlockAllocationAndPurityProofs(string body, bool proven)
    {
        var preparation = Prepare("[ZeroAllocations, EnforcePure] public static int Target(System.Version version, int x) { " + body + " }");
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        using (Assert.EnterMultipleScope())
        {
            foreach (var result in new[] { allocations, purity })
            {
                if (proven)
                { Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString()); }
                else
                {
                    Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>());
                    Assert.That(result.Outcome, Is.Not.TypeOf<RefutedOutcome>());
                }
            }
        }
    }

    [Test]
    public async Task StructReceiversStayUnsupported()
    {
        // A struct method may mutate the caller's copy through `this`.
        var preparation = Prepare("public static int Target(int value) { " +
            "Contract.Ensures(Contract.Result<int>() == value); var span = new System.DateTime(1); var ignored = span.ToBinary(); return value; }");
        Assert.That(await PostconditionAsync(preparation), Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    private static async Task<PassiveCallableCheckResult> ExceptionsAsync(string body)
    {
        var preparation = Prepare("[DoesNotThrow] public static int Target(System.Version version) { " + body + " }");
        return await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
    }

    private static async Task<WorkerClaimOutcome> PostconditionAsync(CompilerCallablePreparation preparation)
    {
        var results = new List<WorkerClaimResult>();
        await TotalCallableVerifier.VerifyAsync(preparation, new WorkerBudgets(),
            check => results.Add(CallableClaimResultAssembler.FromTotal(preparation, check)), null, CancellationToken.None);
        return results.Count == 0 ? WorkerClaimOutcome.Unknown : results[^1].Outcome;
    }

    private static CompilerCallablePreparation Prepare(string method)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; public static class C { " + method + " }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single(preparation => preparation.Entry.CallableId.Contains("C.Target", StringComparison.Ordinal));
    }
}
