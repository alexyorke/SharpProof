using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class BitConverterTests
    {
        [Test]
        public async Task BitConverterGetBytesInt_ReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(int value)
    {
        return BitConverter.GetBytes(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesInt_LocalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(int value)
    {
        var bytes = BitConverter.GetBytes(value);
        return bytes;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesInt_ConditionalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(bool useLeft, int value)
    {
        return useLeft
            ? BitConverter.GetBytes(value)
            : BitConverter.GetBytes(value + 1);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesLong_ReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(long value)
    {
        return BitConverter.GetBytes(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesLong_LocalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(long value)
    {
        var bytes = BitConverter.GetBytes(value);
        return bytes;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesLong_ConditionalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(bool useLeft, long value)
    {
        return useLeft
            ? BitConverter.GetBytes(value)
            : BitConverter.GetBytes(value + 1);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesFloat_ReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(float value)
    {
        return BitConverter.GetBytes(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesFloat_LocalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(float value)
    {
        var bytes = BitConverter.GetBytes(value);
        return bytes;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesFloat_ConditionalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(bool useLeft, float value)
    {
        return useLeft
            ? BitConverter.GetBytes(value)
            : BitConverter.GetBytes(value + 1);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesUInt_ReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(uint value)
    {
        return BitConverter.GetBytes(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesUInt_LocalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        return bytes;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesUInt_ConditionalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(bool useLeft, uint value)
    {
        return useLeft
            ? BitConverter.GetBytes(value)
            : BitConverter.GetBytes(value + 1);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesULong_ReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(ulong value)
    {
        return BitConverter.GetBytes(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesULong_LocalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(ulong value)
    {
        var bytes = BitConverter.GetBytes(value);
        return bytes;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesULong_ConditionalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(bool useLeft, ulong value)
    {
        return useLeft
            ? BitConverter.GetBytes(value)
            : BitConverter.GetBytes(value + 1);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesHalf_ReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(Half value)
    {
        return BitConverter.GetBytes(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesShort_ReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(short value)
    {
        return BitConverter.GetBytes(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesShort_LocalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(short value)
    {
        var bytes = BitConverter.GetBytes(value);
        return bytes;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesShort_ConditionalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(bool useLeft, short value)
    {
        return useLeft
            ? BitConverter.GetBytes(value)
            : BitConverter.GetBytes((short)(value + 1));
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesUShort_ReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(ushort value)
    {
        return BitConverter.GetBytes(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesUShort_LocalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(ushort value)
    {
        var bytes = BitConverter.GetBytes(value);
        return bytes;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesUShort_ConditionalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(bool useLeft, ushort value)
    {
        return useLeft
            ? BitConverter.GetBytes(value)
            : BitConverter.GetBytes((ushort)(value + 1));
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesBool_ReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(bool value)
    {
        return BitConverter.GetBytes(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesBool_LocalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(bool value)
    {
        var bytes = BitConverter.GetBytes(value);
        return bytes;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesBool_ConditionalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(bool useLeft, bool value)
    {
        return useLeft
            ? BitConverter.GetBytes(value)
            : BitConverter.GetBytes(!value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesChar_ReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(char value)
    {
        return BitConverter.GetBytes(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesChar_LocalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(char value)
    {
        var bytes = BitConverter.GetBytes(value);
        return bytes;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterGetBytesChar_ConditionalReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(bool useLeft, char value)
    {
        return useLeft
            ? BitConverter.GetBytes(value)
            : BitConverter.GetBytes((char)(value + 1));
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterToInt32Span_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ReadOnlySpan<byte> bytes)
    {
        return BitConverter.ToInt32(bytes);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitConverterToDoubleSpan_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public double TestMethod(ReadOnlySpan<byte> bytes)
    {
        return BitConverter.ToDouble(bytes);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
