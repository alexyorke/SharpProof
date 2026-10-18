using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrPrimitiveBoundaryTests
{
    [Test]
    public void NumericGuardsPreserveBoundsAndParameterNames()
    {
        Assert.That(ArgumentNullGuard.RequireIndex(0, 1, "index"), Is.Zero);
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>((Action)(() => ArgumentNullGuard.RequireIndex(-1, 1, "index")))!.ParamName, Is.EqualTo("index"));
        Assert.That(ArgumentNullGuard.RequireNonnegative(0L, "count"), Is.Zero);
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>((Action)(() => ArgumentNullGuard.RequireNonnegative(-1L, "count")))!.ParamName, Is.EqualTo("count"));
        Assert.That(ArgumentNullGuard.RequirePositive(1L, "limit"), Is.EqualTo(1L));
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>((Action)(() => ArgumentNullGuard.RequirePositive(0L, "limit")))!.ParamName, Is.EqualTo("limit"));
        Assert.That(ArgumentNullGuard.RequirePositive(1U, "limit"), Is.EqualTo(1U));
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>((Action)(() => ArgumentNullGuard.RequirePositive(0U, "limit")))!.ParamName, Is.EqualTo("limit"));
    }

    [Test]
    public void Utf16ValidationRejectsNullAndUnpairedSurrogates()
    {
        Assert.Throws<ArgumentNullException>((Action)(() => Utf16WellFormedness.IsWellFormed(null!)));
        Assert.That(Utf16WellFormedness.IsWellFormed("\ud800"), Is.False);
        Assert.That(Utf16WellFormedness.IsWellFormed("\udc00"), Is.False);
        Assert.That(Utf16WellFormedness.IsWellFormed("\ud800\udc00"), Is.True);
    }

    [Test]
    public void ArrayStorageCompatibilityRequiresSequenceTypes()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var sequence = factory.GetOrCreateSequenceType(factory.IntegerType);
        Assert.That(IrArrayStorage.CompatibleIntegerViews(factory, factory.IntegerType, sequence), Is.False);
        Assert.That(IrArrayStorage.CompatibleIntegerViews(factory, sequence, factory.IntegerType), Is.False);
    }

    [Test]
    public void FactoryRejectsBlankSemanticNamesAndMixedEqualityTypes()
    {
        var factory = new IrFactory();
        Assert.Throws<ArgumentException>((Action)(() => factory.CreateVariable(" ", factory.IntegerType)));
        Assert.Throws<ArgumentException>((Action)(() => factory.Binary(IrBinaryOperator.Equal, factory.Integer(0), factory.Boolean(false))));
        var integer = (IrIntegerTerm)factory.Integer(3);
        Assert.That(integer.Width, Is.Zero);
        Assert.That(integer.Value, Is.EqualTo(3));
        Assert.That(factory.CreateVariable("valid", factory.IntegerType).IsDefault, Is.False);
    }
}
