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
    public async Task OpaqueCallsMakeElementReadsApproximations(string body)
    {
        // The callee may write any array, so no read may assume the entry value.
        var preparation = Prepare("public static int Target(int[] items) { " +
            "Contract.Requires(items != null && items.Length == 2 && items[0] == 2 && items[1] == 1); " +
            "Contract.Ensures(Contract.Result<int>() == 2); " + body + " }");
        var outcome = await PostconditionAsync(preparation);
        Assert.That(outcome, Is.Not.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
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
    public async Task NarrowHandlersAroundOpaqueCallsMayMatch()
    {
        // The unknown exception may or may not be an ArgumentException.
        var narrow = await ExceptionsAsync("try { return version.GetHashCode(); } catch (System.ArgumentException) { return 0; }");
        var covered = await ExceptionsAsync("try { return version.GetHashCode(); } catch (System.ArgumentException) { return 0; } " +
            "catch (System.Exception) { return 1; }");
        var rethrown = await ExceptionsAsync("try { return version.GetHashCode(); } catch (System.ArgumentException) { throw; } " +
            "catch (System.Exception) { return 1; }");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(narrow.Outcome, Is.Not.TypeOf<ProvenOutcome>());
            Assert.That(narrow.Outcome, Is.Not.TypeOf<RefutedOutcome>());
            Assert.That(covered.Outcome, Is.TypeOf<ProvenOutcome>(), covered.Reason.ToString());
            Assert.That(rethrown.Outcome, Is.Not.TypeOf<ProvenOutcome>());
            Assert.That(rethrown.Outcome, Is.Not.TypeOf<RefutedOutcome>());
        }
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

    [TestCase("var ignored = time.ToBinary(); return value;", WorkerClaimOutcome.Proven)]
    [TestCase("var copy = time; var ignored = copy.AddTicks(1).Ticks; return value;", WorkerClaimOutcome.Proven)]
    [TestCase("var created = new System.DateTime(1); return value;", WorkerClaimOutcome.Unknown)]
    public async Task StructValuesAreOpaque(string body, WorkerClaimOutcome outcome)
    {
        // Only opaque calls read a struct, so mutation through `this` is
        // unobservable; constructing one stays unsupported.
        var preparation = Prepare("public static int Target(System.DateTime time, int value) { " +
            "Contract.Ensures(Contract.Result<int>() == value); " + body + " }");
        Assert.That(await PostconditionAsync(preparation), Is.EqualTo(outcome));
    }

    [TestCase(true, typeof(ProvenOutcome))]
    [TestCase(false, null)]
    public async Task CatchAllHandlesDispatchedAndConstrainedCalls(bool guarded, Type? outcome)
    {
        var body = "var entry = _collection.Find(item.Key); return entry.Value.Equals(item.Value);";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using System.Collections.Generic;
            using SharpProof.Attributes;
            public class Tree<TKey, TValue> { public virtual KeyValuePair<TKey, TValue> Find(TKey key) { throw new System.Exception(); } }
            public class Map<TKey, TValue> {
                private Tree<TKey, TValue> _collection { get; set; }
                [DoesNotThrow] public bool Contains(KeyValuePair<TKey, TValue> item) {
            """ + (guarded ? "try { " + body + " } catch (System.Exception) { return false; }" : body) + " } }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(
            preparations.Single(preparation => preparation.EffectClaims.Length != 0), new WorkerBudgets());
        if (outcome != null)
        { Assert.That(result.Outcome, Is.TypeOf(outcome), result.Reason.ToString()); }
        else
        {
            Assert.That(result.Outcome, Is.Null);
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
        }
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
