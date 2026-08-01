using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ArgumentNullGuardBoundaryTests
{
    private enum GuardProbe
    {
        Value
    }

    [Test]
    public void DefinedEnumGuardRejectsUnknownValuesWithTheOriginalParameterName()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(() => ArgumentNullGuard.RequireDefined(
                (GuardProbe)99,
                "probe")));

        Assert.That(error!.ParamName, Is.EqualTo("probe"));
        Assert.That(
            ArgumentNullGuard.RequireDefined(GuardProbe.Value, "probe"),
            Is.EqualTo(GuardProbe.Value));
    }

    [Test]
    public void PublicGuardsPreserveParameterNamesAndGenericIdentitySupport()
    {
        var factory = new IrFactory();
        var identityError = Assert.Throws<ArgumentNullException>(
            (Action)(() => factory.InternExternalIdentity<object>(
                null!,
                EqualityComparer<object>.Default)));
        var valueError = Assert.Throws<ArgumentNullException>(
            (Action)(() => factory.InternString(null!)));

        var valueIdentity = factory.InternExternalIdentity(
            42,
            EqualityComparer<int>.Default);

        Assert.That(identityError!.ParamName, Is.EqualTo("identity"));
        Assert.That(valueError!.ParamName, Is.EqualTo("value"));
        Assert.That(valueIdentity.IsDefault, Is.False);
    }

    [Test]
    public void OpaqueInstanceReceiverGuardPreservesParameterNameAndDetail()
    {
        var factory = new IrFactory();
        var receiverType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "Receiver");
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            receiverType,
            "Read",
            factory.IntegerType,
            isStatic: false);

        var error = Assert.Throws<ArgumentNullException>(
            (Action)(() => factory.PureOpaque(member, receiver: null)));

        Assert.That(error!.ParamName, Is.EqualTo("receiver"));
        Assert.That(
            error.Message,
            Does.Contain("An instance member requires a receiver."));
    }
}
