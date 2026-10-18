using Microsoft.Z3;

namespace SharpProof.Smt.Test;

public sealed class KeyedFieldEncodingBoundaryTests
{
    [Test]
    public void MathematicalIntegerKeysCannotBeEncodedAsFixedWidthHeapKeys()
    {
        var factory = new IrFactory();
        var receiver = factory.CreateVariable("owner", factory.ObjectType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:presence", factory.BooleanType, false, factory.IntegerType);
        var read = factory.PureOpaque(field, factory.Variable(receiver), factory.Integer(0));
        using var context = new Context();
        using var owner = new Z3ExpressionOwner();
        var encoder = new BvEncoder(context, factory, owner);
        try
        {
            var meter = new SmtQueryResourceMeter(IrSmtBackendOptions.DefaultQueryRlimit, TestContext.CurrentContext.CancellationToken);
            Assert.Throws<UnsupportedIrEncodingException>((Action)(() => encoder.EncodeBoolean(read, meter)));
        }
        finally
        { encoder.DisposeReferences(); }
    }
}
