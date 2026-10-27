using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CSharpScalarSemanticsTests
{
    private static readonly ImmutableDictionary<SpecialType, Type> ExpectedIntegerTypes =
        ImmutableDictionary.CreateRange<SpecialType, Type>(
        [
            new(SpecialType.System_SByte, typeof(sbyte)),
            new(SpecialType.System_Byte, typeof(byte)),
            new(SpecialType.System_Int16, typeof(short)),
            new(SpecialType.System_UInt16, typeof(ushort)),
            new(SpecialType.System_Char, typeof(char)),
            new(SpecialType.System_Int32, typeof(int)),
            new(SpecialType.System_UInt32, typeof(uint)),
            new(SpecialType.System_Int64, typeof(long))
                ]);

    [Test]
    public void SupportedIntegerCatalogIsExactAndExhaustive()
    {
        Assert.That(
            CSharpOperationSemantics.SupportedIntegers.Select(
                static semantics => semantics.SpecialType),
            Is.Unique);
        foreach (var type in Enum.GetValues<SpecialType>())
        {
            var expected = ExpectedIntegerTypes.TryGetValue(type, out var expectedType);
            var actual = CSharpOperationSemantics.TryGetInteger(
                type,
                out var semantics);
            Assert.That(actual, Is.EqualTo(expected), type.ToString());
            Assert.That(
                CSharpOperationSemantics.IsSupportedInteger(type),
                Is.EqualTo(expected),
                type.ToString());
            if (!expected)
            {
                continue;
            }

            var expectedSemantics = GetExpectedSemantics(expectedType!);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    semantics.SpecialType,
                    Is.EqualTo(type),
                    type.ToString());
                Assert.That(
                    semantics.IsSigned,
                    Is.EqualTo(expectedSemantics.IsSigned),
                    type.ToString());
                Assert.That(
                    semantics.BitWidth,
                    Is.EqualTo(expectedSemantics.BitWidth),
                    type.ToString());
                Assert.That(
                    semantics.Minimum,
                    Is.EqualTo(expectedSemantics.Minimum),
                    type.ToString());
                Assert.That(
                    semantics.Maximum,
                    Is.EqualTo(expectedSemantics.Maximum),
                    type.ToString());
                Assert.That(
                    semantics.SupportsExactIrArithmetic,
                    Is.EqualTo(expectedType == typeof(long)),
                    type.ToString());
            }
        }
    }

    private static (bool IsSigned, int BitWidth, long Minimum, long Maximum)
        GetExpectedSemantics(Type type)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        var typeCode = Type.GetTypeCode(type);
        var bitWidth = typeCode switch
        {
            TypeCode.SByte or TypeCode.Byte => 8,
            TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Char => 16,
            TypeCode.Int32 or TypeCode.UInt32 => 32,
            TypeCode.Int64 => 64,
            _ => throw new ArgumentException("Expected an integer primitive.", nameof(type))
        };
        return (
            typeCode is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64,
            bitWidth,
            Convert.ToInt64(
                type.GetField("MinValue")!.GetValue(null),
                CultureInfo.InvariantCulture),
            Convert.ToInt64(
                type.GetField("MaxValue")!.GetValue(null),
                CultureInfo.InvariantCulture));
    }
}
