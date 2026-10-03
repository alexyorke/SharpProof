using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Instance field reads (and properties that only return a field) run no code.
// Their one fault is a null receiver; the value read is an approximation.
[TestFixture]
public sealed class NativeFieldReadTests
{
    private const string Cell = "public class Cell { public int Value; public volatile int Shared; " +
        "private int _count; public int Count { get { return _count; } } public bool Flag { get; set; } " +
        "public virtual int Virtual => _count; }";

    [TestCase("Contract.Requires(cell != null); return cell.Value;", true)]
    [TestCase("return cell == null ? 0 : cell.Value;", true)]
    [TestCase("Contract.Requires(cell != null); return cell.Count;", true)]
    [TestCase("Contract.Requires(cell != null); return cell.Flag ? 1 : 0;", true)]
    [TestCase("return cell.Value;", false)]
    [TestCase("return cell.Count;", false)]
    public async Task NullReceiverIsTheOnlyFault(string body, bool proven)
    {
        var preparation = Prepare("[DoesNotThrow] public static int Target(Cell cell) { " + body + " }");
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, proven ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        if (!proven)
        { Assert.That(result.ExceptionWitness!.Kind, Is.EqualTo(IrExceptionKind.NullReference)); }
    }

    [Test]
    public async Task ImplicitThisReadsNeitherThrowNorAllocateNorWrite()
    {
        const string source = "using SharpProof.Attributes; public sealed class C { private int _count; " +
            "private bool Ready { get; set; } public int Count { get { return _count; } } " +
            "[DoesNotThrow, ZeroAllocations, EnforcePure] public int Target() { return Ready ? Count : _count; } }";
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(source));
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exceptions.Outcome, Is.TypeOf<ProvenOutcome>(), exceptions.Reason.ToString());
            Assert.That(allocations.Outcome, Is.TypeOf<ProvenOutcome>(), allocations.Reason.ToString());
            Assert.That(purity.Outcome, Is.TypeOf<ProvenOutcome>(), purity.Reason.ToString());
        }
    }

    [Test]
    public async Task CounterexampleThatReadsTheHeapIsNotReplayable()
    {
        // Z3 can pick Value == 0, but nothing concrete establishes that value.
        var preparation = Prepare("[DoesNotThrow] public static int Target(Cell cell) { " +
            "Contract.Requires(cell != null); return 10 / cell.Value; }");
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.Not.TypeOf<RefutedOutcome>());
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>());
    }

    [TestCase("return cell.Shared;")]
    [TestCase("return cell.Virtual;")]
    public async Task VolatileAndDispatchedReadsRemainUnsupported(string body)
    {
        var preparation = Prepare("[DoesNotThrow] public static int Target(Cell cell) { " +
            "Contract.Requires(cell != null); " + body + " }");
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    private static CompilerCallablePreparation Prepare(string method)
    {
        return Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; " + Cell + " public static class C { " + method + " }"));
    }

    private static CompilerCallablePreparation Prepare(CompilerManifestArtifact artifact)
    {
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single(preparation => preparation.EffectClaims.Length != 0);
    }
}
