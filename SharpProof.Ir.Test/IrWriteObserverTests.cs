using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrWriteObserverTests
{
    [Test]
    public void InvalidFieldStoreDoesNotNotifyWriteObservers()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", factory.IntegerType, false);
        builder.FieldStore(block, factory.CreateOperation(), IrWriteRegion.Field,
            factory.Null(factory.ObjectType), field, factory.Integer(1));
        builder.Return(block, factory.CreateOperation());
        var observed = 0;
        var prefixes = 0;
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 64, new IrProgramReplayOptions(_ => null)
        {
            WriteObserver = _ => observed++,
            WritePrefixObserver = (_, _) => prefixes++
        });
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        Assert.That(observed, Is.Zero);
        Assert.That(prefixes, Is.Zero);
    }
}
