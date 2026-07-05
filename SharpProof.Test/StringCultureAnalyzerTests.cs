using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class StringCultureAnalyzerTests
{
    [Test]
    public async Task StringToLower_DefaultCulture_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(string value)
    {
        return value.ToLower();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StringToUpper_DefaultCulture_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(string value)
    {
        return value.ToUpper();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StringToLowerInvariant_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string value)
    {
        return value.ToLowerInvariant();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StringToUpperInvariant_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string value)
    {
        return value.ToUpperInvariant();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
