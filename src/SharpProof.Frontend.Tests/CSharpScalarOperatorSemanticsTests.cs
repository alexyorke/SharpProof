using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CSharpScalarOperatorSemanticsTests
{
    [Test]
    public void BinaryMappingsAndArithmeticCategoriesAreExhaustive()
    {
        var mappingsBuilder = ImmutableDictionary.CreateBuilder<BinaryOperatorKind, IrBinaryOperator>();
        mappingsBuilder[BinaryOperatorKind.Add] = IrBinaryOperator.Add;
        mappingsBuilder[BinaryOperatorKind.Subtract] = IrBinaryOperator.Subtract;
        mappingsBuilder[BinaryOperatorKind.Multiply] = IrBinaryOperator.Multiply;
        mappingsBuilder[BinaryOperatorKind.Divide] = IrBinaryOperator.Divide;
        mappingsBuilder[BinaryOperatorKind.Remainder] = IrBinaryOperator.Remainder;
        mappingsBuilder[BinaryOperatorKind.And] = IrBinaryOperator.BitwiseAnd;
        mappingsBuilder[BinaryOperatorKind.Or] = IrBinaryOperator.BitwiseOr;
        mappingsBuilder[BinaryOperatorKind.ExclusiveOr] = IrBinaryOperator.BitwiseXor;
        mappingsBuilder[BinaryOperatorKind.ConditionalAnd] = IrBinaryOperator.AndAlso;
        mappingsBuilder[BinaryOperatorKind.ConditionalOr] = IrBinaryOperator.OrElse;
        mappingsBuilder[BinaryOperatorKind.Equals] = IrBinaryOperator.Equal;
        mappingsBuilder[BinaryOperatorKind.NotEquals] = IrBinaryOperator.NotEqual;
        mappingsBuilder[BinaryOperatorKind.LessThan] = IrBinaryOperator.LessThan;
        mappingsBuilder[BinaryOperatorKind.LessThanOrEqual] = IrBinaryOperator.LessThanOrEqual;
        mappingsBuilder[BinaryOperatorKind.GreaterThan] = IrBinaryOperator.GreaterThan;
        mappingsBuilder[BinaryOperatorKind.GreaterThanOrEqual] = IrBinaryOperator.GreaterThanOrEqual;
        var mappings = mappingsBuilder.ToImmutable();
        var arithmetic = ImmutableHashSet.Create<BinaryOperatorKind>(
            BinaryOperatorKind.Add,
            BinaryOperatorKind.Subtract,
            BinaryOperatorKind.Multiply,
            BinaryOperatorKind.Divide,
            BinaryOperatorKind.Remainder
        );
        var checkedArithmetic = ImmutableHashSet.Create<BinaryOperatorKind>(
            BinaryOperatorKind.Add,
            BinaryOperatorKind.Subtract,
            BinaryOperatorKind.Multiply
        );

        var supportedKinds = CSharpOperationSemantics.SupportedBinaryOperators
            .Select(static semantics => semantics.Kind)
            .ToImmutableArray();
        Assert.That(supportedKinds, Is.EquivalentTo(mappings.Keys));
        foreach (var kind in Enum.GetValues<BinaryOperatorKind>())
        {
            var expected = mappings.TryGetValue(kind, out var mapped)
                ? mapped
                : (IrBinaryOperator?)null;
            Assert.That(
                CSharpOperationSemantics.MapBinary(kind, SpecialType.None),
                Is.EqualTo(expected),
                kind.ToString());
            Assert.That(
                CSharpOperationSemantics.IsIntegerArithmetic(kind),
                Is.EqualTo(arithmetic.Contains(kind)),
                kind.ToString());
            Assert.That(
                CSharpOperationSemantics.RequiresCheckedArithmetic(kind),
                Is.EqualTo(checkedArithmetic.Contains(kind)),
                kind.ToString());
        }
        foreach (var mapping in mappings)
        {
            Assert.That(
                CSharpOperationSemantics.MapBinaryToRoslyn(mapping.Value),
                Is.EqualTo(mapping.Key),
                mapping.Value.ToString());
        }
        Assert.That(
            CSharpOperationSemantics.MapBinary(
                BinaryOperatorKind.Add,
                SpecialType.System_String),
            Is.EqualTo(IrBinaryOperator.StringConcat));
        Assert.That(
            CSharpOperationSemantics.MapBinaryToRoslyn(
                IrBinaryOperator.StringConcat),
            Is.EqualTo(BinaryOperatorKind.None));
    }

    [Test]
    public void BinaryReverseAndNegationRelationsAreExactAndInvolutive()
    {
        var reversedBuilder = ImmutableDictionary.CreateBuilder<
            BinaryOperatorKind,
            BinaryOperatorKind>();
        reversedBuilder[BinaryOperatorKind.LessThan] = BinaryOperatorKind.GreaterThan;
        reversedBuilder[BinaryOperatorKind.LessThanOrEqual] = BinaryOperatorKind.GreaterThanOrEqual;
        reversedBuilder[BinaryOperatorKind.GreaterThan] = BinaryOperatorKind.LessThan;
        reversedBuilder[BinaryOperatorKind.GreaterThanOrEqual] = BinaryOperatorKind.LessThanOrEqual;
        var reversed = reversedBuilder.ToImmutable();
        var negatedBuilder = ImmutableDictionary.CreateBuilder<
            BinaryOperatorKind,
            BinaryOperatorKind>();
        negatedBuilder[BinaryOperatorKind.Equals] = BinaryOperatorKind.NotEquals;
        negatedBuilder[BinaryOperatorKind.NotEquals] = BinaryOperatorKind.Equals;
        negatedBuilder[BinaryOperatorKind.LessThan] = BinaryOperatorKind.GreaterThanOrEqual;
        negatedBuilder[BinaryOperatorKind.LessThanOrEqual] = BinaryOperatorKind.GreaterThan;
        negatedBuilder[BinaryOperatorKind.GreaterThan] = BinaryOperatorKind.LessThanOrEqual;
        negatedBuilder[BinaryOperatorKind.GreaterThanOrEqual] = BinaryOperatorKind.LessThan;
        var negated = negatedBuilder.ToImmutable();

        foreach (var kind in Enum.GetValues<BinaryOperatorKind>())
        {
            var reverse = CSharpOperationSemantics.ReverseBinary(kind);
            var negate = CSharpOperationSemantics.NegateBinary(kind);
            Assert.That(
                reverse,
                Is.EqualTo(
                    reversed.TryGetValue(kind, out var expectedReverse)
                        ? expectedReverse
                        : kind),
                $"reverse {kind}");
            Assert.That(
                negate,
                Is.EqualTo(
                    negated.TryGetValue(kind, out var expectedNegation)
                        ? expectedNegation
                        : kind),
                $"negate {kind}");
            Assert.That(
                CSharpOperationSemantics.ReverseBinary(reverse),
                Is.EqualTo(kind),
                $"reverse involution {kind}");
            Assert.That(
                CSharpOperationSemantics.NegateBinary(negate),
                Is.EqualTo(kind),
                $"negation involution {kind}");
        }
    }

    [Test]
    public void IntegerConversionRangesAreExhaustive()
    {
        ImmutableArray<SpecialType> integers = [
            SpecialType.System_SByte,
            SpecialType.System_Byte,
            SpecialType.System_Int16,
            SpecialType.System_UInt16,
            SpecialType.System_Char,
            SpecialType.System_Int32,
            SpecialType.System_UInt32,
            SpecialType.System_Int64
        ];
        var rangesBuilder = ImmutableDictionary.CreateBuilder<SpecialType, (long Minimum, long Maximum)>();
        rangesBuilder[SpecialType.System_SByte] = (sbyte.MinValue, sbyte.MaxValue);
        rangesBuilder[SpecialType.System_Byte] = (byte.MinValue, byte.MaxValue);
        rangesBuilder[SpecialType.System_Int16] = (short.MinValue, short.MaxValue);
        rangesBuilder[SpecialType.System_UInt16] = (ushort.MinValue, ushort.MaxValue);
        rangesBuilder[SpecialType.System_Char] = (char.MinValue, char.MaxValue);
        rangesBuilder[SpecialType.System_Int32] = (int.MinValue, int.MaxValue);
        rangesBuilder[SpecialType.System_UInt32] = (uint.MinValue, uint.MaxValue);
        rangesBuilder[SpecialType.System_Int64] = (long.MinValue, long.MaxValue);
        var ranges = rangesBuilder.ToImmutable();

        Assert.That(
            CSharpOperationSemantics.SupportedIntegerConversions.Select(
                static conversion /* SPINT001: all values valid - all conversion descriptions are valid for metadata comparison */ => (
                    conversion.Source,
                    conversion.Target)),
            Is.Unique);
        Assert.That(
            CSharpOperationSemantics.SupportedIntegerConversions,
            Has.Length.EqualTo(integers.Length * integers.Length));
        foreach (var source in integers)
        {
            foreach (var target in integers)
            {
                var sourceRange = ranges[source];
                var targetRange = ranges[target];
                Assert.That(
                    CSharpOperationSemantics.IsValuePreservingIntegerConversion(
                        source,
                        target),
                    Is.EqualTo(
                        sourceRange.Minimum >= targetRange.Minimum &&
                        sourceRange.Maximum <= targetRange.Maximum),
                    source + " -> " + target);
            }
        }
        Assert.That(
            CSharpOperationSemantics.IsValuePreservingIntegerConversion(
                SpecialType.System_String,
                SpecialType.System_Int64),
            Is.False);
        Assert.That(
            CSharpOperationSemantics.IsValuePreservingIntegerConversion(
                SpecialType.System_Int64,
                SpecialType.System_String),
            Is.False);
    }

    [Test]
    public void UnaryMappingsAndCheckedPoliciesAreExhaustive()
    {
        var expectedBuilder = ImmutableDictionary.CreateBuilder<
            UnaryOperatorKind,
            (IrUnaryOperator? Ir, bool Identity, bool Checked, bool ExactInteger)>();
        expectedBuilder[UnaryOperatorKind.Not] = (IrUnaryOperator.Not, false, false, false);
        expectedBuilder[UnaryOperatorKind.Plus] = (null, true, false, false);
        expectedBuilder[UnaryOperatorKind.Minus] = (IrUnaryOperator.Negate, false, true, true);
        var expected = expectedBuilder.ToImmutable();

        var supportedKinds = CSharpOperationSemantics.SupportedUnaryOperators
            .Select(static semantics => semantics.Kind)
            .ToImmutableArray();
        Assert.That(supportedKinds, Is.EquivalentTo(expected.Keys));
        foreach (var kind in Enum.GetValues<UnaryOperatorKind>())
        {
            var present = CSharpOperationSemantics.TryGetUnary(
                kind,
                out var semantics);
            Assert.That(
                present,
                Is.EqualTo(expected.TryGetValue(kind, out var row)),
                kind.ToString());
            if (!present)
            {
                continue;
            }

            using (Assert.EnterMultipleScope())
            {
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
