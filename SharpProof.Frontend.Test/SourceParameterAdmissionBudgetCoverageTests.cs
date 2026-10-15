using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceParameterAdmissionBudgetCoverageTests
{
    [TestCase(8, false, false)]
    [TestCase(2050, false, true)]
    [TestCase(8, true, false)]
    [TestCase(2050, true, true)]
    public void ActualMethodParametersRespectFlatAndRegionAdmissionLimits(int parameterCount, bool withFinally, bool expectedLimit)
    {
        var parameters = string.Join(", ", Enumerable.Range(0, parameterCount).Select(index => $"int p{index}"));
        var body = withFinally ? "try { return p0; } finally { p0++; }" : "return p0;";
        using var subject = TypedProgramSubject.Create("int Target(" + parameters + ") { " + body + " }");
        var arguments = Enumerable.Range(0, parameterCount).Select(_ => (object)17).ToArray();
        Assert.That(subject.Invoke(arguments), Is.EqualTo(17));
        var lowering = subject.Lower();
        Assert.That(lowering.ConstructionLimitExceeded, Is.EqualTo(expectedLimit));
        Assert.That(lowering.IsExact, Is.EqualTo(!expectedLimit));
        if (!expectedLimit)
        {
            var execution = subject.Execute(lowering, arguments);
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ConsumedApproximation, Is.False);
            Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(17)));
        }
    }
}
