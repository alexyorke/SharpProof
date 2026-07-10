using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class EncodingLookupTests
{
    [Test]
    public async Task EncodingGetEncoding_Diagnostic()
    {
        var test = @"
using System.Text;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Encoding {|SP0002:TestMethod|}()
    {
        return Encoding.GetEncoding(""utf-8"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EncodingGetBytes_Diagnostic()
    {
        var test = @"
using System.Text;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] {|SP0002:TestMethod|}(string value)
    {
        return Encoding.UTF8.GetBytes(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EncodingUtf8Getter_NoDiagnostic()
    {
        var test = @"
using System.Text;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Encoding TestMethod()
    {
        return Encoding.UTF8;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EncodingAsciiGetter_NoDiagnostic()
    {
        var test = @"
using System.Text;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Encoding TestMethod()
    {
        return Encoding.ASCII;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EncodingUtf8GetString_Diagnostic()
    {
        var test = @"
using System.Text;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(byte[] bytes)
    {
        return Encoding.UTF8.GetString(bytes);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}