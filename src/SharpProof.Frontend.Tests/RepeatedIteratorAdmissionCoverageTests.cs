using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class RepeatedIteratorAdmissionCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void RepeatedCallsReuseIteratorAdmissionWithoutExhaustingBudget(bool iterator)
    {
        var helper = iterator
            ? "static System.Collections.Generic.IEnumerable<int> Values() { yield return 17; }"
            : "static System.Collections.Generic.IEnumerable<int> Values() => null!;";
        using var subject = TypedProgramSubject.Create("int Target() { _ = Values(); _ = Values(); return 17; } " + helper);
        Assert.That(subject.Invoke([]), Is.EqualTo(17));
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        Assert.That(lowering.IsExact, Is.EqualTo(!iterator));
        if (!iterator)
        {
            var execution = subject.Execute(lowering, []);
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ConsumedApproximation, Is.False);
            Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(17)));
        }
    }
}
