using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class WebUtilityTests
{
    [Test]
    public async Task WebUtilityHtmlEncode_Diagnostic()
    {
        var test = @"
using System.Net;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task WebUtilityUrlDecode_Diagnostic()
    {
        var test = @"
using System.Net;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(string value)
    {
        return WebUtility.UrlDecode(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task WebUtilityHtmlDecode_Diagnostic()
    {
        var test = @"
using System.Net;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(string value)
    {
        return WebUtility.HtmlDecode(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task WebUtilityUrlEncode_Diagnostic()
    {
        var test = @"
using System.Net;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(string value)
    {
        return WebUtility.UrlEncode(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task WebUtilityUrlEncodeToBytes_ReturnedArray_Diagnostic()
    {
        var test = @"
using System.Net;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] {|SP0002:TestMethod|}(byte[] value)
    {
        return WebUtility.UrlEncodeToBytes(value, 0, value.Length);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task WebUtilityUrlDecodeToBytes_ReturnedArray_Diagnostic()
    {
        var test = @"
using System.Net;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] {|SP0002:TestMethod|}(byte[] value)
    {
        return WebUtility.UrlDecodeToBytes(value, 0, value.Length);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}