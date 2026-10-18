using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerScalarSignatureProviderTests
{
    private ImmutableArray<byte> _image = [];

    [OneTimeSetUp]
    public void CompileOrdinarySignatureFixture()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            public struct LocalValue { }
            public static class GenericContainer<T> {
                public static T TypeGeneric(T value) => value;
            }
            public static class Signatures {
                public static bool Boolean(bool value) => value;
                public static char Character(char value) => value;
                public static sbyte SignedByte(sbyte value) => value;
                public static byte UnsignedByte(byte value) => value;
                public static short SignedShort(short value) => value;
                public static ushort UnsignedShort(ushort value) => value;
                public static int SignedInteger(int value) => value;
                public static uint UnsignedInteger(uint value) => value;
                public static long SignedLong(long value) => value;
                public static ulong UnsignedLong(ulong value) => value;
                public static object Object(object value) => value;
                public static string Text(string value) => value;
                public static double Floating(double value) => value;
                public static LocalValue Definition(LocalValue value) => value;
                public static System.DateTime Reference(System.DateTime value) => value;
                public static T MethodGeneric<T>(T value) => value;
                public static System.Collections.Generic.List<int> Constructed(
                    System.Collections.Generic.List<int> value) => value;
                public static int[] Vector(int[] value) => value;
                public static int[,] Matrix(int[,] value) => value;
                public static unsafe int* Pointer(int* value) => value;
                public static unsafe delegate*<int, int> FunctionPointer(
                    delegate*<int, int> value) => value;
                public static ref int ByReference(int[] values) => ref values[0];
                public static ref readonly int ModifiedReference(int[] values) => ref values[0];
                public static unsafe int Pinned(int[] values) {
                    fixed (int* pointer = values) { return *pointer; }
                }
                public static object TypeSpecification() => new System.Collections.Generic.List<int>();
            }
            """, []).WithAssemblyName("CompilerScalarSignatures");
        compilation = compilation.WithOptions(compilation.Options.WithAllowUnsafe(true)
            .WithOptimizationLevel(OptimizationLevel.Debug));
        _image = AnalyzerTestHost.EmitImage(compilation).ToImmutableArray();
    }

    [TestCase("Boolean", SpecialType.System_Boolean)]
    [TestCase("Character", SpecialType.System_Char)]
    [TestCase("SignedByte", SpecialType.System_SByte)]
    [TestCase("UnsignedByte", SpecialType.System_Byte)]
    [TestCase("SignedShort", SpecialType.System_Int16)]
    [TestCase("UnsignedShort", SpecialType.System_UInt16)]
    [TestCase("SignedInteger", SpecialType.System_Int32)]
    [TestCase("UnsignedInteger", SpecialType.System_UInt32)]
    [TestCase("SignedLong", SpecialType.System_Int64)]
    [TestCase("UnsignedLong", SpecialType.System_UInt64)]
    [TestCase("Object", SpecialType.System_Object)]
    [TestCase("Text", SpecialType.System_String)]
    [TestCase("Floating", SpecialType.None)]
    [TestCase("Definition", SpecialType.None)]
    [TestCase("Reference", SpecialType.None)]
    [TestCase("MethodGeneric", SpecialType.None)]
    [TestCase("TypeGeneric", SpecialType.None)]
    [TestCase("Constructed", SpecialType.None)]
    [TestCase("Vector", SpecialType.None)]
    [TestCase("Matrix", SpecialType.None)]
    [TestCase("Pointer", SpecialType.None)]
    [TestCase("FunctionPointer", SpecialType.None)]
    public void GenuineMethodSignaturesClassifyAcceptedAndRejectedTypes(string name, SpecialType expected)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        using var pe = new PEReader(new MemoryStream(_image.ToArray(), writable: false));
        var reader = pe.GetMetadataReader();
        var method = FindMethod(reader, name);
        var signature = method.DecodeSignature(new CompilerScalarSignatureProvider(), genericContext: null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(signature.ReturnType, Is.EqualTo(expected));
            Assert.That(signature.ParameterTypes, Has.Length.EqualTo(1));
            Assert.That(signature.ParameterTypes.Single(), Is.EqualTo(expected));
        }
    }

    [TestCase("ByReference")]
    [TestCase("ModifiedReference")]
    public void GenuineReferenceReturnsRemainOutsideTheScalarSubset(string name)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        using var pe = new PEReader(new MemoryStream(_image.ToArray(), writable: false));
        var reader = pe.GetMetadataReader();
        var signature = FindMethod(reader, name).DecodeSignature(
            new CompilerScalarSignatureProvider(), genericContext: null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(signature.ReturnType, Is.EqualTo(SpecialType.None));
            Assert.That(signature.ParameterTypes.Single(), Is.EqualTo(SpecialType.None));
        }
    }

    [Test]
    public void GenuinePinnedLocalsRemainOutsideTheScalarSubset()
    {
        using var pe = new PEReader(new MemoryStream(_image.ToArray(), writable: false));
        var reader = pe.GetMetadataReader();
        var method = FindMethod(reader, "Pinned");
        var body = pe.GetMethodBody(method.RelativeVirtualAddress);
        Assert.That(body.LocalSignature.IsNil, Is.False);
        var locals = reader.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(
            new CompilerScalarSignatureProvider(), genericContext: null);
        Assert.That(locals, Does.Contain(SpecialType.None));
    }

    [Test]
    public void GenuineConstructedTypeSpecificationRemainsOutsideTheScalarSubset()
    {
        using var pe = new PEReader(new MemoryStream(_image.ToArray(), writable: false));
        var reader = pe.GetMetadataReader();
        var count = reader.GetTableRowCount(TableIndex.TypeSpec);
        var provider = new CompilerScalarSignatureProvider();
        for (var row = 1; row <= count; row++)
        {
            var handle = MetadataTokens.TypeSpecificationHandle(row);
            var blob = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
            if (blob.ReadByte() != (byte)SignatureTypeCode.GenericTypeInstance)
            {
                continue;
            }
            var rawTypeKind = blob.ReadByte();
            Assert.That(provider.GetTypeFromSpecification(reader, null, handle, rawTypeKind),
                Is.EqualTo(SpecialType.None));
            return;
        }
        Assert.Fail("The compiled constructed type must produce a genuine type specification.");
    }

    private static MethodDefinition FindMethod(MetadataReader reader, string name)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        System.ArgumentNullException.ThrowIfNull(reader);
        var handle = reader.MethodDefinitions.Single(candidate =>
            reader.GetString(reader.GetMethodDefinition(candidate).Name) == name);
        return reader.GetMethodDefinition(handle);
    }
}
