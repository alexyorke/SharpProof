using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrConstantInitializerControls
{
    [TestCase(IrTypeKind.Boolean)]
    [TestCase(IrTypeKind.Integer)]
    [TestCase(IrTypeKind.String)]
    [TestCase(IrTypeKind.Reference)]
    public void RegisteredConstantInitializersReturnTheirExactValue(IrTypeKind kind)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        IrTerm literal = kind switch
        {
            IrTypeKind.Boolean => factory.Boolean(true),
            IrTypeKind.Integer => factory.Integer(42),
            IrTypeKind.String => factory.String("element"),
            IrTypeKind.Reference => factory.Null(factory.ObjectType),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var sequenceType = factory.GetOrCreateSequenceType(literal.Type);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var target = factory.CreateVariable("array", sequenceType);
        var variable = factory.Variable(factory.CreateVariable("nonconstant", literal.Type));
        var exception = Assert.Throws<ArgumentException>((Action)(() =>
            builder.Allocate(entry, factory.CreateOperation(), sequenceType, target,
                factory.Integer(1), [variable])));
        Assert.That(exception!.ParamName, Is.EqualTo("initialValues"));
        Assert.That(exception.Message, Does.StartWith("Array initializers require matching constant elements."));

        builder.Allocate(entry, factory.CreateOperation(), sequenceType, target,
            factory.Integer(1), [literal]);
        builder.Return(entry, factory.CreateOperation(),
            factory.SequenceAccess(factory.Variable(target), factory.Integer(0)));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build());
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Type, Is.EqualTo(literal.Type));
        switch (kind)
        {
            case IrTypeKind.Boolean:
                Assert.That(result.ReturnValue.Boolean, Is.True);
                break;
            case IrTypeKind.Integer:
                Assert.That(result.ReturnValue.Integer, Is.EqualTo(42));
                break;
            case IrTypeKind.String:
                Assert.That(result.ReturnValue.String, Is.EqualTo("element"));
                break;
            case IrTypeKind.Reference:
                Assert.That(result.ReturnValue.Kind, Is.EqualTo(IrValueKind.Null));
                break;
        }
    }
}
