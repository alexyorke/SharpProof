using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrOperatorCatalogTests
{
    [Test]
    public void TypeKindVocabularyIsExactAndNumericallyStable()
    {
        var expected = new Dictionary<IrTypeKind, int>
        {
            [IrTypeKind.Boolean] = 0,
            [IrTypeKind.Integer] = 1,
            [IrTypeKind.String] = 2,
            [IrTypeKind.Reference] = 3,
            [IrTypeKind.Sequence] = 4
        };

        Assert.That(
            Enum.GetValues<IrTypeKind>(),
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
        var expected = new Dictionary<
            IrUnaryOperator,
            (int Key, IrTypeKind Operand, string Token)>
        {
            [IrUnaryOperator.Not] =
                (0, IrTypeKind.Boolean, "!"),
            [IrUnaryOperator.Negate] =
                (1, IrTypeKind.Integer, "-")
        };

        Assert.That(
            Enum.GetValues<IrUnaryOperator>(),
            Is.EquivalentTo(expected.Keys));
        Assert.That(
            expected.Keys.Select(static @operator => (int)@operator),
            Is.Unique);
        foreach (var row in expected)
        {
            Assert.That(
                (int)row.Key,
                Is.EqualTo(row.Value.Key),
                $"numeric {row.Key}");
            Assert.That(
                IrOperatorCatalog.Get(row.Key),
                Is.EqualTo(row.Value),
                row.Key.ToString());
        }
        Assert.Throws<ArgumentOutOfRangeException>(
            new Action(() => IrOperatorCatalog.Get(
                (IrUnaryOperator)int.MaxValue)));
    }

    [Test]
    public void BinaryMetadataIsExactAndExhaustive()
    {
        var expected = new Dictionary<
            IrBinaryOperator,
            (
                int Key,
                IrTypeKind? Operand,
                IrTypeKind Result,
                string Token)>
        {
            [IrBinaryOperator.Add] =
                (0, IrTypeKind.Integer, IrTypeKind.Integer, "+"),
            [IrBinaryOperator.Subtract] =
                (1, IrTypeKind.Integer, IrTypeKind.Integer, "-"),
            [IrBinaryOperator.Multiply] =
                (2, IrTypeKind.Integer, IrTypeKind.Integer, "*"),
            [IrBinaryOperator.Divide] =
                (3, IrTypeKind.Integer, IrTypeKind.Integer, "/"),
            [IrBinaryOperator.Remainder] =
                (4, IrTypeKind.Integer, IrTypeKind.Integer, "%"),
            [IrBinaryOperator.AndAlso] =
                (5, IrTypeKind.Boolean, IrTypeKind.Boolean, "&&"),
            [IrBinaryOperator.OrElse] =
                (6, IrTypeKind.Boolean, IrTypeKind.Boolean, "||"),
            [IrBinaryOperator.Equal] =
                (7, null, IrTypeKind.Boolean, "=="),
            [IrBinaryOperator.NotEqual] =
                (8, null, IrTypeKind.Boolean, "!="),
            [IrBinaryOperator.LessThan] =
                (9, IrTypeKind.Integer, IrTypeKind.Boolean, "<"),
            [IrBinaryOperator.LessThanOrEqual] =
                (10, IrTypeKind.Integer, IrTypeKind.Boolean, "<="),
            [IrBinaryOperator.GreaterThan] =
                (11, IrTypeKind.Integer, IrTypeKind.Boolean, ">"),
            [IrBinaryOperator.GreaterThanOrEqual] =
                (12, IrTypeKind.Integer, IrTypeKind.Boolean, ">="),
            [IrBinaryOperator.StringConcat] =
                (13, IrTypeKind.String, IrTypeKind.String, "++")
        };

        Assert.That(
            Enum.GetValues<IrBinaryOperator>(),
            Is.EquivalentTo(expected.Keys));
        Assert.That(
            expected.Keys.Select(static @operator => (int)@operator),
            Is.Unique);
        foreach (var row in expected)
        {
            Assert.That(
                (int)row.Key,
                Is.EqualTo(row.Value.Key),
                $"numeric {row.Key}");
            Assert.That(
                IrOperatorCatalog.Get(row.Key),
                Is.EqualTo(row.Value),
                row.Key.ToString());
        }
        Assert.Throws<ArgumentOutOfRangeException>(
            new Action(() => IrOperatorCatalog.Get(
                (IrBinaryOperator)int.MaxValue)));
    }
}
