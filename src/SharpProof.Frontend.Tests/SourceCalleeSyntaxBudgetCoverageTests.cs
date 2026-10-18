using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceCalleeSyntaxBudgetCoverageTests
{
    [TestCase(8, false)]
    [TestCase(1400, true)]
    public void OrdinaryCalleeBodyRespectsSharedEligibilityWork(int increments, bool expectedLimit)
    {
        var steps = string.Concat(Enumerable.Repeat("value++; ", increments));
        using var subject = TypedProgramSubject.Create(
            "int Target(int input) { int value = Step(input); value++; return value; } static int Step(int value) { " + steps + "return value; }");
        var expected = 4 + increments + 1;
        Assert.That(subject.Invoke([4]), Is.EqualTo(expected));
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.ConstructionLimitExceeded, Is.EqualTo(expectedLimit));
        Assert.That(lowering.IsExact, Is.EqualTo(!expectedLimit));
        if (!expectedLimit)
        {
            var execution = subject.Execute(lowering, [4]);
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
        }
    }
}
