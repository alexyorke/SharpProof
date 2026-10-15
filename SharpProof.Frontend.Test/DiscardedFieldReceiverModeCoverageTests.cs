using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class DiscardedFieldReceiverModeCoverageTests
{
    [TestCase(false, 17)]
    [TestCase(true, 19)]
    public void ClaimReadModePreservesDiscardedFieldReceiverAndNullFault(bool missing, int expected)
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(bool missing) { Box receiver = missing ? null! : new Box(); try { receiver.Value++; return 17; } catch (System.NullReferenceException) { return 19; } }",
            "public sealed class Box { public int Value; }");
        Assert.That(subject.Invoke([missing]), Is.EqualTo(expected));
        var lowering = new RoslynProgramLowerer(subject.Factory).LowerCandidate(subject.Graph, subject.Context,
            static _ => true, resolveIl: null, cancellationToken: default, approximateElementReads: true);
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [missing]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }
}
