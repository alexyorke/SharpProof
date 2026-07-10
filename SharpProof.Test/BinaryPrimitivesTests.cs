using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class BinaryPrimitivesTests
{
    [Test]
    public async Task BinaryPrimitivesIntegerReads_NoDiagnostic()
    {
        var test = @"
using System.Buffers.Binary;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod(byte[] data)
    {
        return
            BinaryPrimitives.ReadInt16BigEndian(data) +
            BinaryPrimitives.ReadInt16LittleEndian(data) +
            BinaryPrimitives.ReadInt32BigEndian(data) +
            BinaryPrimitives.ReadInt32LittleEndian(data) +
            BinaryPrimitives.ReadInt64BigEndian(data) +
            BinaryPrimitives.ReadInt64LittleEndian(data) +
            BinaryPrimitives.ReadUInt16BigEndian(data) +
            BinaryPrimitives.ReadUInt16LittleEndian(data) +
            BinaryPrimitives.ReadUInt32BigEndian(data) +
            BinaryPrimitives.ReadUInt32LittleEndian(data) +
            (long)BinaryPrimitives.ReadUInt64BigEndian(data) +
            (long)BinaryPrimitives.ReadUInt64LittleEndian(data);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task BinaryPrimitivesReverseEndianness_NoDiagnostic()
    {
        var test = @"
using System.Buffers.Binary;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod(short s, ushort us, int i, uint ui, long l, ulong ul)
    {
        return
            BinaryPrimitives.ReverseEndianness(s) +
            BinaryPrimitives.ReverseEndianness(us) +
            BinaryPrimitives.ReverseEndianness(i) +
            BinaryPrimitives.ReverseEndianness(ui) +
            BinaryPrimitives.ReverseEndianness(l) +
            (long)BinaryPrimitives.ReverseEndianness(ul);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [TestCase("BinaryPrimitives.WriteInt16BigEndian(destination, 1);")]
    [TestCase("BinaryPrimitives.WriteInt16LittleEndian(destination, 1);")]
    [TestCase("BinaryPrimitives.WriteInt32BigEndian(destination, 1);")]
    [TestCase("BinaryPrimitives.WriteInt32LittleEndian(destination, 1);")]
    [TestCase("BinaryPrimitives.WriteInt64BigEndian(destination, 1L);")]
    [TestCase("BinaryPrimitives.WriteInt64LittleEndian(destination, 1L);")]
    [TestCase("BinaryPrimitives.WriteInt128BigEndian(destination, (Int128)1);")]
    [TestCase("BinaryPrimitives.WriteInt128LittleEndian(destination, (Int128)1);")]
    [TestCase("BinaryPrimitives.WriteIntPtrBigEndian(destination, (nint)1);")]
    [TestCase("BinaryPrimitives.WriteIntPtrLittleEndian(destination, (nint)1);")]
    [TestCase("BinaryPrimitives.WriteUInt16BigEndian(destination, 1);")]
    [TestCase("BinaryPrimitives.WriteUInt16LittleEndian(destination, 1);")]
    [TestCase("BinaryPrimitives.WriteUInt32BigEndian(destination, 1U);")]
    [TestCase("BinaryPrimitives.WriteUInt32LittleEndian(destination, 1U);")]
    [TestCase("BinaryPrimitives.WriteUInt64BigEndian(destination, 1UL);")]
    [TestCase("BinaryPrimitives.WriteUInt64LittleEndian(destination, 1UL);")]
    [TestCase("BinaryPrimitives.WriteUInt128BigEndian(destination, (UInt128)1);")]
    [TestCase("BinaryPrimitives.WriteUInt128LittleEndian(destination, (UInt128)1);")]
    [TestCase("BinaryPrimitives.WriteUIntPtrBigEndian(destination, (nuint)1);")]
    [TestCase("BinaryPrimitives.WriteUIntPtrLittleEndian(destination, (nuint)1);")]
    public async Task BinaryPrimitivesIntegerWrites_NoDiagnostic(string statement)
    {
        var methodParameters = "Span<byte> destination";
        var bodyStatement = statement;

        if (bodyStatement.Contains("(Int128)1", StringComparison.Ordinal))
        {
            methodParameters += ", Int128 int128";
            bodyStatement = bodyStatement.Replace("(Int128)1", "int128", StringComparison.Ordinal);
        }

        if (bodyStatement.Contains("(UInt128)1", StringComparison.Ordinal))
        {
            methodParameters += ", UInt128 uint128";
            bodyStatement = bodyStatement.Replace("(UInt128)1", "uint128", StringComparison.Ordinal);
        }

        var test = @"
using System;
using System.Buffers.Binary;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(" + methodParameters + @")
    {
        " + bodyStatement + @"
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [TestCase("BinaryPrimitives.TryWriteInt16BigEndian(destination, 1)")]
    [TestCase("BinaryPrimitives.TryWriteInt16LittleEndian(destination, 1)")]
    [TestCase("BinaryPrimitives.TryWriteInt32BigEndian(destination, 1)")]
    [TestCase("BinaryPrimitives.TryWriteInt32LittleEndian(destination, 1)")]
    [TestCase("BinaryPrimitives.TryWriteInt64BigEndian(destination, 1L)")]
    [TestCase("BinaryPrimitives.TryWriteInt64LittleEndian(destination, 1L)")]
    [TestCase("BinaryPrimitives.TryWriteInt128BigEndian(destination, (Int128)1)")]
    [TestCase("BinaryPrimitives.TryWriteInt128LittleEndian(destination, (Int128)1)")]
    [TestCase("BinaryPrimitives.TryWriteIntPtrBigEndian(destination, (nint)1)")]
    [TestCase("BinaryPrimitives.TryWriteIntPtrLittleEndian(destination, (nint)1)")]
    [TestCase("BinaryPrimitives.TryWriteUInt16BigEndian(destination, 1)")]
    [TestCase("BinaryPrimitives.TryWriteUInt16LittleEndian(destination, 1)")]
    [TestCase("BinaryPrimitives.TryWriteUInt32BigEndian(destination, 1U)")]
    [TestCase("BinaryPrimitives.TryWriteUInt32LittleEndian(destination, 1U)")]
    [TestCase("BinaryPrimitives.TryWriteUInt64BigEndian(destination, 1UL)")]
    [TestCase("BinaryPrimitives.TryWriteUInt64LittleEndian(destination, 1UL)")]
    [TestCase("BinaryPrimitives.TryWriteUInt128BigEndian(destination, (UInt128)1)")]
    [TestCase("BinaryPrimitives.TryWriteUInt128LittleEndian(destination, (UInt128)1)")]
    [TestCase("BinaryPrimitives.TryWriteUIntPtrBigEndian(destination, (nuint)1)")]
    [TestCase("BinaryPrimitives.TryWriteUIntPtrLittleEndian(destination, (nuint)1)")]
    public async Task BinaryPrimitivesIntegerTryWrites_NoDiagnostic(string expression)
    {
        var methodParameters = "Span<byte> destination";
        var bodyExpression = expression;

        if (bodyExpression.Contains("(Int128)1", StringComparison.Ordinal))
        {
            methodParameters += ", Int128 int128";
            bodyExpression = bodyExpression.Replace("(Int128)1", "int128", StringComparison.Ordinal);
        }

        if (bodyExpression.Contains("(UInt128)1", StringComparison.Ordinal))
        {
            methodParameters += ", UInt128 uint128";
            bodyExpression = bodyExpression.Replace("(UInt128)1", "uint128", StringComparison.Ordinal);
        }

        var test = @"
using System;
using System.Buffers.Binary;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(" + methodParameters + @")
    {
        return " + bodyExpression + @";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [TestCase("BinaryPrimitives.TryWriteSingleBigEndian(destination, 1.0f)")]
    [TestCase("BinaryPrimitives.TryWriteSingleLittleEndian(destination, 1.0f)")]
    [TestCase("BinaryPrimitives.TryWriteDoubleBigEndian(destination, 1.0)")]
    [TestCase("BinaryPrimitives.TryWriteDoubleLittleEndian(destination, 1.0)")]
    [TestCase("BinaryPrimitives.TryWriteHalfBigEndian(destination, (Half)1)")]
    [TestCase("BinaryPrimitives.TryWriteHalfLittleEndian(destination, (Half)1)")]
    public async Task BinaryPrimitivesFloatingPointTryWrites_NoDiagnostic(string expression)
    {
        var methodParameters = "Span<byte> destination";
        var bodyExpression = expression;

        if (bodyExpression.Contains("(Half)1", StringComparison.Ordinal))
        {
            methodParameters += ", Half half";
            bodyExpression = bodyExpression.Replace("(Half)1", "half", StringComparison.Ordinal);
        }

        var test = @"
using System;
using System.Buffers.Binary;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(" + methodParameters + @")
    {
        return " + bodyExpression + @";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [TestCase("BinaryPrimitives.WriteSingleBigEndian(destination, 1.0f);")]
    [TestCase("BinaryPrimitives.WriteSingleLittleEndian(destination, 1.0f);")]
    [TestCase("BinaryPrimitives.WriteDoubleBigEndian(destination, 1.0);")]
    [TestCase("BinaryPrimitives.WriteDoubleLittleEndian(destination, 1.0);")]
    [TestCase("BinaryPrimitives.WriteHalfBigEndian(destination, (Half)1);")]
    [TestCase("BinaryPrimitives.WriteHalfLittleEndian(destination, (Half)1);")]
    public async Task BinaryPrimitivesFloatingPointWrites_NoDiagnostic(string statement)
    {
        var methodParameters = "Span<byte> destination";
        var bodyStatement = statement;

        if (bodyStatement.Contains("(Half)1", StringComparison.Ordinal))
        {
            methodParameters += ", Half half";
            bodyStatement = bodyStatement.Replace("(Half)1", "half", StringComparison.Ordinal);
        }

        var test = @"
using System;
using System.Buffers.Binary;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(" + methodParameters + @")
    {
        " + bodyStatement + @"
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}