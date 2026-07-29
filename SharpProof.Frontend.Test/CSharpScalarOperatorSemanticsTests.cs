using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CSharpScalarOperatorSemanticsTests {
    [Test]
    public void BinaryMappingsAndArithmeticCategoriesAreExhaustive() {
        var mappings = new Dictionary<BinaryOperatorKind, IrBinaryOperator> {
            [BinaryOperatorKind.Add] = IrBinaryOperator.Add,
            [BinaryOperatorKind.Subtract] = IrBinaryOperator.Subtract,
            [BinaryOperatorKind.Multiply] = IrBinaryOperator.Multiply,
            [BinaryOperatorKind.Divide] = IrBinaryOperator.Divide,
            [BinaryOperatorKind.Remainder] = IrBinaryOperator.Remainder,
            [BinaryOperatorKind.ConditionalAnd] = IrBinaryOperator.AndAlso,
            [BinaryOperatorKind.ConditionalOr] = IrBinaryOperator.OrElse,
            [BinaryOperatorKind.Equals] = IrBinaryOperator.Equal,
            [BinaryOperatorKind.NotEquals] = IrBinaryOperator.NotEqual,
            [BinaryOperatorKind.LessThan] = IrBinaryOperator.LessThan,
            [BinaryOperatorKind.LessThanOrEqual] =
                IrBinaryOperator.LessThanOrEqual,
            [BinaryOperatorKind.GreaterThan] = IrBinaryOperator.GreaterThan,
            [BinaryOperatorKind.GreaterThanOrEqual] =
                IrBinaryOperator.GreaterThanOrEqual
        };
        var arithmetic = new HashSet<BinaryOperatorKind> {
            BinaryOperatorKind.Add,
            BinaryOperatorKind.Subtract,
            BinaryOperatorKind.Multiply,
            BinaryOperatorKind.Divide,
            BinaryOperatorKind.Remainder
        };
        var checkedArithmetic = new HashSet<BinaryOperatorKind> {
            BinaryOperatorKind.Add,
            BinaryOperatorKind.Subtract,
            BinaryOperatorKind.Multiply
        };

        Assert.That(
            CSharpScalarSemantics.SupportedBinaryOperators.Select(
                static semantics => semantics.Kind),
            Is.Unique);
        Assert.That(
            CSharpScalarSemantics.SupportedBinaryOperators.Select(
                static semantics => semantics.Kind),
            Is.EquivalentTo(mappings.Keys));
        foreach (var kind in Enum.GetValues<BinaryOperatorKind>()) {
            var expected = mappings.TryGetValue(kind, out var mapped)
                ? mapped
                : (IrBinaryOperator?)null;
            Assert.That(
                CSharpScalarSemantics.MapBinary(kind, SpecialType.None),
                Is.EqualTo(expected),
                kind.ToString());
            Assert.That(
                CSharpScalarSemantics.IsIntegerArithmetic(kind),
                Is.EqualTo(arithmetic.Contains(kind)),
                kind.ToString());
            Assert.That(
                CSharpScalarSemantics.RequiresCheckedArithmetic(kind),
                Is.EqualTo(checkedArithmetic.Contains(kind)),
                kind.ToString());
        }
        Assert.That(
            CSharpScalarSemantics.MapBinary(
                BinaryOperatorKind.Add,
                SpecialType.System_String),
            Is.EqualTo(IrBinaryOperator.StringConcat));
    }

    [Test]
    public void IntegerConversionRangesAreExhaustive() {
        SpecialType[] integers = [
            SpecialType.System_SByte,
            SpecialType.System_Byte,
            SpecialType.System_Int16,
            SpecialType.System_UInt16,
            SpecialType.System_Char,
            SpecialType.System_Int32,
            SpecialType.System_UInt32,
            SpecialType.System_Int64
        ];
        var ranges = new Dictionary<SpecialType, (long Minimum, long Maximum)> {
            [SpecialType.System_SByte] = (sbyte.MinValue, sbyte.MaxValue),
            [SpecialType.System_Byte] = (byte.MinValue, byte.MaxValue),
            [SpecialType.System_Int16] = (short.MinValue, short.MaxValue),
            [SpecialType.System_UInt16] = (ushort.MinValue, ushort.MaxValue),
            [SpecialType.System_Char] = (char.MinValue, char.MaxValue),
            [SpecialType.System_Int32] = (int.MinValue, int.MaxValue),
            [SpecialType.System_UInt32] = (uint.MinValue, uint.MaxValue),
            [SpecialType.System_Int64] = (long.MinValue, long.MaxValue)
        };

        Assert.That(
            CSharpScalarSemantics.SupportedIntegerConversions.Select(
                static conversion => (
                    conversion.Source,
                    conversion.Target)),
            Is.Unique);
        Assert.That(
            CSharpScalarSemantics.SupportedIntegerConversions,
            Has.Length.EqualTo(integers.Length * integers.Length));
        foreach (var source in integers) {
            foreach (var target in integers) {
                var sourceRange = ranges[source];
                var targetRange = ranges[target];
                Assert.That(
                    CSharpScalarSemantics.IsValuePreservingIntegerConversion(
                        source,
                        target),
                    Is.EqualTo(
                        sourceRange.Minimum >= targetRange.Minimum &&
                        sourceRange.Maximum <= targetRange.Maximum),
                    source + " -> " + target);
            }
        }
        Assert.That(
            CSharpScalarSemantics.IsValuePreservingIntegerConversion(
                SpecialType.System_String,
                SpecialType.System_Int64),
            Is.False);
        Assert.That(
            CSharpScalarSemantics.IsValuePreservingIntegerConversion(
                SpecialType.System_Int64,
                SpecialType.System_String),
            Is.False);
    }

    [Test]
    public void UnaryMappingsAndCheckedPoliciesAreExhaustive() {
        var expected = new Dictionary<
            UnaryOperatorKind,
            (IrUnaryOperator? Ir, bool Identity, bool Checked, bool ExactInteger)> {
            [UnaryOperatorKind.Not] =
                    (IrUnaryOperator.Not, false, false, false),
            [UnaryOperatorKind.Plus] =
                    (null, true, false, false),
            [UnaryOperatorKind.Minus] =
                    (IrUnaryOperator.Negate, false, true, true)
        };

        Assert.That(
            CSharpScalarSemantics.SupportedUnaryOperators.Select(
                static semantics => semantics.Kind),
            Is.Unique);
        Assert.That(
            CSharpScalarSemantics.SupportedUnaryOperators.Select(
                static semantics => semantics.Kind),
            Is.EquivalentTo(expected.Keys));
        foreach (var kind in Enum.GetValues<UnaryOperatorKind>()) {
            var present = CSharpScalarSemantics.TryGetUnary(
                kind,
                out var semantics);
            Assert.That(
                present,
                Is.EqualTo(expected.TryGetValue(kind, out var row)),
                kind.ToString());
            if (!present) continue;
            using (Assert.EnterMultipleScope()) {
                Assert.That(semantics.IrOperator, Is.EqualTo(row.Ir));
                Assert.That(semantics.IsIdentity, Is.EqualTo(row.Identity));
                Assert.That(
                    semantics.RequiresCheckedArithmetic,
                    Is.EqualTo(row.Checked));
                Assert.That(
                    semantics.RequiresExactIntegerDomain,
                    Is.EqualTo(row.ExactInteger));
            }
        }
    }
}
