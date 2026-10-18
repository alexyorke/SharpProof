using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class NestedLocalCallableAdmissionCoverageTests
{
    [Test]
    public void ArgumentOnlyLocalCalleePreservesCompiledResult()
    {
        using var subject = TypedProgramSubject.Create("int Target(int input) { return Local(input); static int Local(int value) => value + 1; }");
        Assert.That(subject.Invoke([4]), Is.EqualTo(5));
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [4]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(5)));
    }

    [Test]
    public void LocalCalleeWithNestedCallableKeepsConservativeAdmission()
    {
        using var subject = TypedProgramSubject.Create("int Target(int input) { return Local(input); static int Local(int value) { static int Nested(int other) => other + 1; return Nested(value); } }");
        Assert.That(subject.Invoke([4]), Is.EqualTo(5));
        Assert.That(subject.LowerSourceCalls().IsExact, Is.False);
    }
}
