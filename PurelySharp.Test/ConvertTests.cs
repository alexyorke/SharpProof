using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ConvertTests
    {
        [Test]
        public async Task ConvertFromBase64String_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(string value)
    {
        return Convert.FromBase64String(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConvertFromBase64String_LocalNonEscapingUse_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string value)
    {
        var bytes = Convert.FromBase64String(value);
        return bytes.Length;
    }
            }";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConvertFromBase64String_LocalReturned_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(string value)
    {
        var bytes = Convert.FromBase64String(value);
        return bytes;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConvertFromHexString_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(string value)
    {
        return Convert.FromHexString(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConvertFromHexString_LocalNonEscapingUse_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string value)
    {
        var bytes = Convert.FromHexString(value);
        return bytes.Length;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConvertFromHexString_LocalReturned_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(string value)
    {
        var bytes = Convert.FromHexString(value);
        return bytes;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConvertFromBase64CharArray_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(string value)
    {
        var chars = value.ToCharArray();
        return Convert.FromBase64CharArray(chars, 0, chars.Length);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConvertFromBase64CharArray_LocalNonEscapingUse_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string value)
    {
        var chars = value.ToCharArray();
        var bytes = Convert.FromBase64CharArray(chars, 0, chars.Length);
        return bytes.Length;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConvertFromBase64CharArray_LocalReturned_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(string value)
    {
        var chars = value.ToCharArray();
        var bytes = Convert.FromBase64CharArray(chars, 0, chars.Length);
        return bytes;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConvertToBase64StringSegment_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(byte[] bytes)
    {
        return Convert.ToBase64String(bytes, 0, bytes.Length);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConvertToHexStringSpan_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(bytes);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
