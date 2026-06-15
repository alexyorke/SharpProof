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
    }
}
