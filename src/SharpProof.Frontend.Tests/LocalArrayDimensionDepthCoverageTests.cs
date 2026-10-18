using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class LocalArrayDimensionDepthCoverageTests
{
    [TestCase(8, true)]
    [TestCase(270, false)]
    public void ValidConstantArrayDimensionPreservesTheProductionDepthBoundary(int depth, bool exact)
    {
        var dimension = string.Concat(Enumerable.Repeat("0 + (", depth)) + "1" + new string(')', depth);
        using var subject = TypedProgramSubject.Create(
            "int Target(int input) { int local = input; var values = new int[" + dimension +
            "] { local }; return values.Length; }");
        Assert.That(subject.Invoke([5]), Is.EqualTo(1));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.EqualTo(exact), lowering.Classification.Abstention.ToString());
        if (exact)
        {
            var execution = subject.Execute(lowering, [5]);
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.One));
        }
        else
        {
            Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.ExpressionDepthLimit));
        }
    }
}
