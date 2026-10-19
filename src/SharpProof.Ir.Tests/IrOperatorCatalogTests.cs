using System.Collections.Immutable;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrOperatorCatalogTests
{
    [Test]
    public void TypeKindVocabularyIsExactAndNumericallyStable()
    {
        var expectedBuilder = ImmutableDictionary.CreateBuilder<IrTypeKind, int>();
        expectedBuilder[IrTypeKind.Boolean] = 0;
        expectedBuilder[IrTypeKind.Integer] = 1;
        expectedBuilder[IrTypeKind.String] = 2;
        expectedBuilder[IrTypeKind.Reference] = 3;
        expectedBuilder[IrTypeKind.Sequence] = 4;
        var expected = expectedBuilder.ToImmutable();

        Assert.That(
            Enum.GetValues<IrTypeKind>().ToImmutableArray(),
            Is.EquivalentTo(expected.Keys));
        Assert.That(
            expected.Keys.Select(static kind => (int)kind),
            Is.Unique);
        foreach (var row in expected)
        {
            Assert.That(
                (int)row.Key,
                Is.EqualTo(row.Value),
                row.Key.ToString());
        }
    }

    [Test]
    public void OpaquePurityKeysAreExactAndFailClosed()
    {
        Assert.That(
            IrOperatorCatalog.GetPurityKey(IrOpaquePurity.Pure),
            Is.EqualTo(0));
        Assert.That(
            IrOperatorCatalog.GetPurityKey(IrOpaquePurity.Impure),
            Is.EqualTo(1));
        Assert.Throws<ArgumentOutOfRangeException>(
            new Action(() => IrOperatorCatalog.GetPurityKey(
                (IrOpaquePurity)int.MaxValue)));
    }

    [Test]
    public void BuiltInTypeAndNullabilityMappingsAreExact()
    {
        var factory = new IrFactory();
        Assert.That(
            IrOperatorCatalog.GetBuiltInType(factory, IrTypeKind.Boolean),
            Is.EqualTo(factory.BooleanType));
        Assert.That(
            IrOperatorCatalog.GetBuiltInType(factory, IrTypeKind.Integer),
            Is.EqualTo(factory.IntegerType));
        Assert.That(
            IrOperatorCatalog.GetBuiltInType(factory, IrTypeKind.String),
            Is.EqualTo(factory.StringType));
        Assert.That(IrOperatorCatalog.IsNullable(IrTypeKind.String), Is.True);
        Assert.That(IrOperatorCatalog.IsNullable(IrTypeKind.Reference), Is.True);
        Assert.That(IrOperatorCatalog.IsNullable(IrTypeKind.Sequence), Is.True);
        Assert.That(IrOperatorCatalog.IsNullable(IrTypeKind.Integer), Is.False);
    }

    [Test]
    public void UnaryMetadataIsExactAndExhaustive()
    {
        var expectedBuilder = ImmutableDictionary.CreateBuilder<IrUnaryOperator,
            (int Key, IrTypeKind Operand, string Token)>();
        expectedBuilder[IrUnaryOperator.Not] = (0, IrTypeKind.Boolean, "!");
        expectedBuilder[IrUnaryOperator.Negate] = (1, IrTypeKind.Integer, "-");
        var expected = expectedBuilder.ToImmutable();

        AssertOperatorMetadata(
            expected,
            Enum.GetValues<IrUnaryOperator>().ToImmutableArray(),
            static @operator => (int)@operator,
            static metadata => metadata.Key,
            static @operator => IrOperatorCatalog.Get(@operator),
            (IrUnaryOperator)int.MaxValue);
    }

    [Test]
    public void BinaryMetadataIsExactAndExhaustive()
    {
        var expectedBuilder = ImmutableDictionary.CreateBuilder<IrBinaryOperator,
            (
                int Key,
                IrTypeKind? Operand,
                IrTypeKind Result,
                string Token)>();
        expectedBuilder[IrBinaryOperator.Add] = (0, IrTypeKind.Integer, IrTypeKind.Integer, "+");
        expectedBuilder[IrBinaryOperator.Subtract] = (1, IrTypeKind.Integer, IrTypeKind.Integer, "-");
        expectedBuilder[IrBinaryOperator.Multiply] = (2, IrTypeKind.Integer, IrTypeKind.Integer, "*");
        expectedBuilder[IrBinaryOperator.Divide] = (3, IrTypeKind.Integer, IrTypeKind.Integer, "/");
        expectedBuilder[IrBinaryOperator.Remainder] = (4, IrTypeKind.Integer, IrTypeKind.Integer, "%");
        expectedBuilder[IrBinaryOperator.AndAlso] = (5, IrTypeKind.Boolean, IrTypeKind.Boolean, "&&");
        expectedBuilder[IrBinaryOperator.OrElse] = (6, IrTypeKind.Boolean, IrTypeKind.Boolean, "||");
        expectedBuilder[IrBinaryOperator.Equal] = (7, null, IrTypeKind.Boolean, "==");
        expectedBuilder[IrBinaryOperator.NotEqual] = (8, null, IrTypeKind.Boolean, "!=");
        expectedBuilder[IrBinaryOperator.LessThan] = (9, IrTypeKind.Integer, IrTypeKind.Boolean, "<");
        expectedBuilder[IrBinaryOperator.LessThanOrEqual] = (10, IrTypeKind.Integer, IrTypeKind.Boolean, "<=");
        expectedBuilder[IrBinaryOperator.GreaterThan] = (11, IrTypeKind.Integer, IrTypeKind.Boolean, ">");
        expectedBuilder[IrBinaryOperator.GreaterThanOrEqual] = (12, IrTypeKind.Integer, IrTypeKind.Boolean, ">=");
        expectedBuilder[IrBinaryOperator.StringConcat] = (13, IrTypeKind.String, IrTypeKind.String, "++");
        expectedBuilder[IrBinaryOperator.StringEquals] = (14, IrTypeKind.String, IrTypeKind.Boolean, "string==");
        expectedBuilder[IrBinaryOperator.BitwiseAnd] = (15, IrTypeKind.Integer, IrTypeKind.Integer, "&");
        expectedBuilder[IrBinaryOperator.ShiftLeft] = (16, IrTypeKind.Integer, IrTypeKind.Integer, "<<");
        expectedBuilder[IrBinaryOperator.ShiftRight] = (17, IrTypeKind.Integer, IrTypeKind.Integer, ">>");
        expectedBuilder[IrBinaryOperator.BitwiseOr] = (18, IrTypeKind.Integer, IrTypeKind.Integer, "|");
        expectedBuilder[IrBinaryOperator.BitwiseXor] = (19, IrTypeKind.Integer, IrTypeKind.Integer, "^");
        var expected = expectedBuilder.ToImmutable();

        AssertOperatorMetadata(
            expected,
            Enum.GetValues<IrBinaryOperator>().ToImmutableArray(),
            static @operator => (int)@operator,
            static metadata => metadata.Key,
            static @operator => IrOperatorCatalog.Get(@operator),
            (IrBinaryOperator)int.MaxValue);
    }

    private static void AssertOperatorMetadata<TEnum, TMetadata>(
        IReadOnlyDictionary<TEnum, TMetadata> expected,
        ImmutableArray<TEnum> values,
        Func<TEnum, int> numericValue,
        Func<TMetadata, int> expectedNumericValue,
        Func<TEnum, TMetadata> getMetadata,
        TEnum invalidValue)
        where TEnum : struct, Enum
    {
        if (values.IsDefault)
        { throw new ArgumentNullException(nameof(values)); }
        System.ArgumentNullException.ThrowIfNull(expected);
        System.ArgumentNullException.ThrowIfNull(expectedNumericValue);
        System.ArgumentNullException.ThrowIfNull(getMetadata);
        System.ArgumentNullException.ThrowIfNull(numericValue);
        Assert.That(values, Is.EquivalentTo(expected.Keys));
        Assert.That(expected.Keys.Select(numericValue), Is.Unique);
        foreach (var row in expected)
        {
            Assert.That(
                numericValue(row.Key),
                Is.EqualTo(expectedNumericValue(row.Value)),
                $"numeric {row.Key}");
            Assert.That(
                getMetadata(row.Key),
                Is.EqualTo(row.Value),
                row.Key.ToString());
        }

        Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(() => getMetadata(invalidValue)));
    }
}
