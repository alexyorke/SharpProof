using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class BitConverterTests
    {
        [Test]
        public async Task BitConverterGetBytesInt_ReturnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
    }
}
