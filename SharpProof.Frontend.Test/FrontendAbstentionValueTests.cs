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
        var exception = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            _ = new FrontendSubsetClassification(
                FrontendSubsetDecision.ClosedAbstention,
                UndefinedReason)));

        Assert.That(exception!.ParamName, Is.EqualTo("abstention"));
    }

    [Test]
    public void ClassificationFactoryRejectsUndefinedReason()
    {
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            FrontendSubsetClassification.Abstain(UndefinedReason)));
    }

    [Test]
    public void ProgramAbstentionRejectsUndefinedReason()
    {
        var operation = new IrFactory().CreateOperation("undefined abstention");

        var exception = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            _ = new FrontendProgramAbstention(operation, UndefinedReason)));

        Assert.That(exception!.ParamName, Is.EqualTo("reason"));
    }
}
