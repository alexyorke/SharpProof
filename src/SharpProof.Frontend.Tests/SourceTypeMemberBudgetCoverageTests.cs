using System.Text;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceTypeMemberBudgetCoverageTests
{
    [TestCase(8, false)]
    [TestCase(4100, true)]
    public void OrdinaryConstantMembersRespectSharedSourceExpansionBudget(int memberCount, bool expectedLimit)
    {
        var additional = new StringBuilder("public static class External { ");
        for (var index = 0; index < memberCount; index++)
        {
            additional.Append("public const int Padding").Append(index).Append(" = 0; ");
        }
        additional.Append("public static int Step(int value) => value + 1; }");
        using var subject = TypedProgramSubject.Create("int Target(int input) { int value = External.Step(input); value++; return value; }", additional.ToString());
        Assert.That(subject.Invoke([4]), Is.EqualTo(6));
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.ConstructionLimitExceeded, Is.EqualTo(expectedLimit));
        Assert.That(lowering.IsExact, Is.EqualTo(!expectedLimit));
        if (!expectedLimit)
        {
            var execution = subject.Execute(lowering, [4]);
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(6)));
        }
    }
}
