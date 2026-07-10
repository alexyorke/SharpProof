using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class JsonTests
{
    [Test]
    public async Task JsonDocumentParse_Diagnostic()
    {
        var test = @"
using System.Text.Json;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public JsonDocument {|SP0002:TestMethod|}()
    {
        return JsonDocument.Parse(""{}"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task JsonElementGetString_Diagnostic()
    {
        var test = @"
#nullable enable
using System.Text.Json;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|SP0002:TestMethod|}(JsonElement element)
    {
        return element.GetString();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}