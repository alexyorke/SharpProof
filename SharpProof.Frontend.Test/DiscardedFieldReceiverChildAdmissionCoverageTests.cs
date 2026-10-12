using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class DiscardedFieldReceiverChildAdmissionCoverageTests
{
    [Test]
    public void UnsupportedOrdinaryReceiverInvocationPreservesChildAbstention()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target() { System.Activator.CreateInstance<Box>().Value++; return 17; }",
            "public sealed class Box { public int Value; }");
        Assert.That(subject.Invoke([]), Is.EqualTo(17));
        var lowering = new RoslynProgramLowerer(subject.Factory).LowerCandidate(subject.Graph, subject.Context,
            static _ => true, resolveIl: null, cancellationToken: default, approximateElementReads: true);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        Assert.That(lowering.Abstentions.Select(abstention => abstention.Reason),
            Does.Contain(FrontendAbstention.UnsupportedInvocationShape));
    }
}
