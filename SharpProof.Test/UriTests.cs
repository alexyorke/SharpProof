using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class UriTests
{
    [Test]
    public async Task UriIsWellFormedUriString_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string value)
    {
        return Uri.IsWellFormedUriString(value, UriKind.Absolute);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UriEscapeAndUnescapeDataString_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string value)
    {
        return Uri.UnescapeDataString(Uri.EscapeDataString(value));
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UriToString_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(Uri value)
    {
        return value.ToString();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}