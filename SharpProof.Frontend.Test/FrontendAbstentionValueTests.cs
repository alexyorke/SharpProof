using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class FrontendAbstentionValueTests
{
    private const FrontendAbstention UndefinedReason =
        (FrontendAbstention)int.MaxValue;

    [Test]
    public void ClassificationRejectsUndefinedAbstention()
    {
        AssertUndefinedReasonRejected(
            () => _ = new FrontendSubsetClassification(
                FrontendSubsetDecision.ClosedAbstention,
                UndefinedReason),
            "abstention");
    }

    [Test]
    public void ClassificationFactoryRejectsUndefinedReason()
    {
        AssertUndefinedReasonRejected(
            () => FrontendSubsetClassification.Abstain(UndefinedReason),
            parameterName: null);
    }

    [Test]
    public void ProgramAbstentionRejectsUndefinedReason()
    {
        var operation = new IrFactory().CreateOperation("undefined abstention");

        AssertUndefinedReasonRejected(
            () => _ = new FrontendProgramAbstention(operation, UndefinedReason),
            "reason");
    }

    private static void AssertUndefinedReasonRejected(
        Action action,
        string? parameterName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(action);
        if (parameterName != null)
        {
            Assert.That(exception!.ParamName, Is.EqualTo(parameterName));
        }
    }
}
