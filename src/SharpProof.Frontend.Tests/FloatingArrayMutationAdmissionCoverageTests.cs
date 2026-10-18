using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class FloatingArrayMutationAdmissionCoverageTests
{
    [TestCase("++")]
    [TestCase(" += 1f")]
    public void ActualFloatingArrayMutationKeepsIntegerMutationClosure(string mutation)
    {
        System.ArgumentNullException.ThrowIfNull(mutation);
        using var subject = TypedProgramSubject.Create("bool Target(float[] values) { values[0]" + mutation +
            "; return values[0] == 2f; }");
        float[] values = [1f];
        Assert.That(subject.Invoke([values]), Is.True);
        Assert.That(values[0], Is.EqualTo(2f));
        var result = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(result.IsExact, Is.False);
        Assert.That(result.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedMutation));
        Assert.That(result.ConstructionLimitExceeded, Is.False);
    }
}
