using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class RegisteredTotalDefaultDomainCoverageTests
{
    [Test]
    public void EveryRegisteredKindPreservesItsExactTypedDefault()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var sequence = factory.GetOrCreateSequenceType(factory.IntegerType);
        var types = new[] { factory.BooleanType, factory.IntegerType, factory.StringType, factory.ObjectType, sequence };
        Assert.That(types.Select(type => type.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(type)) : (factory.GetTypeInfo(type).Kind)),
            Is.EquivalentTo(Enum.GetValues<IrTypeKind>()));
        foreach (var type in types)
        {
            var term = CSharpOperationSemantics.DefaultValue(factory, type);
            Assert.That(term.Type, Is.EqualTo(type));
            var actual = new IrInterpreter(factory).Evaluate(term);
            Assert.That(actual.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(actual.Value!.Type, Is.EqualTo(type));
            if (type == factory.BooleanType)
            { Assert.That(actual.Value.Boolean, Is.False); }
            else if (type == factory.IntegerType)
            { Assert.That(actual.Value.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.Zero)); }
            else
            { Assert.That(actual.Value.Kind, Is.EqualTo(IrValueKind.Null)); }
        }
    }

    [TestCase(8, true)]
    [TestCase(8, false)]
    [TestCase(16, true)]
    [TestCase(16, false)]
    [TestCase(32, true)]
    [TestCase(32, false)]
    [TestCase(64, true)]
    [TestCase(64, false)]
    public void EverySupportedIntegerStorageWidthDefaultsToItsOwnZero(int width, bool isSigned)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, isSigned);
        var term = CSharpOperationSemantics.DefaultValue(factory, type);
        Assert.That(term.Type, Is.EqualTo(type));
        var actual = new IrInterpreter(factory).Evaluate(term);
        Assert.That(actual.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(actual.Value!.Type, Is.EqualTo(type));
        Assert.That(actual.Value.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.Zero));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InvalidOrForeignIdsAreRejectedBeforeTypeDispatch(bool foreign)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = foreign ? new IrFactory(IrExecutionSemantics.Total).IntegerType : default;
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            _ = CSharpOperationSemantics.DefaultValue(factory, type)));
        Assert.That(error!.ParamName, Is.EqualTo("id"));
    }
}
