using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

public sealed class DictionaryKeyedFieldWireTests
{
    [TestCase(0L)]
    [TestCase(long.MinValue)]
    [TestCase(long.MaxValue)]
    public void KeyedFieldStoresAndReadsSurvivePortableRoundTrip(long key)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var keyType = factory.GetOrCreateIntegerType(64, true);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:values", factory.IntegerType, false, keyType);
        var receiver = factory.CreateVariable("dictionary", factory.ObjectType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        builder.Allocate(block, factory.CreateOperation(), factory.ObjectType, receiver);
        builder.FieldStore(block, factory.CreateOperation(), IrWriteRegion.Field, factory.Variable(receiver), field, factory.Integer(23), factory.Integer(keyType, key));
        builder.Return(block, factory.CreateOperation(), factory.PureOpaque(field, factory.Variable(receiver), factory.Integer(keyType, key)));
        var encoded = PortableIrGraphCodec.Encode(factory, builder.Build(), [], variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var decoded = PortableIrGraphCodec.Decode(encoded.Graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var result = new IrProgramInterpreter(decoded.Factory).Execute(decoded.Program!);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ConsumedApproximation, Is.False);
        Assert.That(result.ReturnValue!.Integer, Is.EqualTo(23));
    }
}
