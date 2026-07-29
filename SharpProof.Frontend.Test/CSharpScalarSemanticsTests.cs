using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CSharpScalarSemanticsTests {
    private static readonly Dictionary<
        SpecialType,
        (bool IsSigned, int BitWidth, long Minimum, long Maximum)> Expected =
        new() {
            [SpecialType.System_SByte] =
                (true, 8, sbyte.MinValue, sbyte.MaxValue),
            [SpecialType.System_Byte] =
                (false, 8, byte.MinValue, byte.MaxValue),
            [SpecialType.System_Int16] =
                (true, 16, short.MinValue, short.MaxValue),
            [SpecialType.System_UInt16] =
                (false, 16, ushort.MinValue, ushort.MaxValue),
            [SpecialType.System_Char] =
                (false, 16, char.MinValue, char.MaxValue),
            [SpecialType.System_Int32] =
                (true, 32, int.MinValue, int.MaxValue),
            [SpecialType.System_UInt32] =
                (false, 32, uint.MinValue, uint.MaxValue),
            [SpecialType.System_Int64] =
                (true, 64, long.MinValue, long.MaxValue)
        };

    [Test]
    public void SupportedIntegerCatalogIsExactAndExhaustive() {
        Assert.That(
            CSharpScalarSemantics.SupportedIntegers.Select(
                static semantics => semantics.SpecialType),
            Is.Unique);
        foreach (var type in Enum.GetValues<SpecialType>()) {
            var expected = Expected.TryGetValue(type, out var values);
            var actual = CSharpScalarSemantics.TryGetInteger(
                type,
                out var semantics);
            Assert.That(actual, Is.EqualTo(expected), type.ToString());
            Assert.That(
                CSharpScalarSemantics.IsSupportedInteger(type),
                Is.EqualTo(expected),
                type.ToString());
            if (!expected) continue;
            using (Assert.EnterMultipleScope()) {
                Assert.That(
                    semantics.SpecialType,
                    Is.EqualTo(type),
                    type.ToString());
                Assert.That(
                    semantics.IsSigned,
                    Is.EqualTo(values.IsSigned),
                    type.ToString());
                Assert.That(
                    semantics.BitWidth,
                    Is.EqualTo(values.BitWidth),
                    type.ToString());
                Assert.That(
                    semantics.Minimum,
                    Is.EqualTo(values.Minimum),
                    type.ToString());
                Assert.That(
                    semantics.Maximum,
                    Is.EqualTo(values.Maximum),
                    type.ToString());
                Assert.That(
                    semantics.SupportsExactIrArithmetic,
                    Is.EqualTo(type == SpecialType.System_Int64),
                    type.ToString());
            }
        }
    }

    [Test]
    public void RoslynTypeLoweringUsesExactlyTheCatalogIntegerSet() {
        var compilation = CSharpCompilation.Create(
            "ScalarCatalog",
            references: [
                MetadataReference.CreateFromFile(
                    typeof(object).Assembly.Location)
            ]);
        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);

        foreach (var type in Expected.Keys)
            Assert.That(
                lowerer.GetTypeId(compilation.GetSpecialType(type)),
                Is.EqualTo(factory.IntegerType),
                type.ToString());
        foreach (var type in new[] {
                     SpecialType.System_UInt64,
                     SpecialType.System_IntPtr,
                     SpecialType.System_UIntPtr
                 })
            Assert.That(
                lowerer.GetTypeId(compilation.GetSpecialType(type)),
                Is.Not.EqualTo(factory.IntegerType),
                type.ToString());
    }
}
