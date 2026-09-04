using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CSharpScalarSemanticsTests
{
    private static readonly Dictionary<SpecialType, Type> ExpectedIntegerTypes =
        new()
        {
            [SpecialType.System_SByte] = typeof(sbyte),
            [SpecialType.System_Byte] = typeof(byte),
            [SpecialType.System_Int16] = typeof(short),
            [SpecialType.System_UInt16] = typeof(ushort),
            [SpecialType.System_Char] = typeof(char),
            [SpecialType.System_Int32] = typeof(int),
            [SpecialType.System_UInt32] = typeof(uint),
            [SpecialType.System_Int64] = typeof(long)
        };

    [Test]
    public void SupportedIntegerCatalogIsExactAndExhaustive()
    {
        Assert.That(
            CSharpScalarSemantics.SupportedIntegers.Select(
                static semantics => semantics.SpecialType),
            Is.Unique);
        foreach (var type in Enum.GetValues<SpecialType>())
        {
            var expected = ExpectedIntegerTypes.TryGetValue(type, out var expectedType);
            var actual = CSharpScalarSemantics.TryGetInteger(
                type,
                out var semantics);
            Assert.That(actual, Is.EqualTo(expected), type.ToString());
            Assert.That(
                CSharpScalarSemantics.IsSupportedInteger(type),
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

    [Test]
    public void RoslynTypeLoweringUsesExactlyTheCatalogIntegerSet()
    {
        var compilation = CSharpCompilation.Create(
            "ScalarCatalog",
            references: [
                MetadataReference.CreateFromFile(
                    typeof(object).Assembly.Location)
            ]);
        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);

        foreach (var type in ExpectedIntegerTypes.Keys)
        {
            Assert.That(
                lowerer.GetTypeId(compilation.GetSpecialType(type)),
                Is.EqualTo(factory.IntegerType),
                type.ToString());
        }

        foreach (var type in new[] {
                     SpecialType.System_UInt64,
                     SpecialType.System_IntPtr,
                     SpecialType.System_UIntPtr
                 })
        {
            Assert.That(
                lowerer.GetTypeId(compilation.GetSpecialType(type)),
                Is.Not.EqualTo(factory.IntegerType),
                type.ToString());
        }
    }

    private static (bool IsSigned, int BitWidth, long Minimum, long Maximum)
        GetExpectedSemantics(Type type)
    {
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
