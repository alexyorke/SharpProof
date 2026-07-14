using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class WebUtilityTests
{
    private static IEnumerable<TestCaseData> ImpureCalls()
    {
        foreach (var method in new[] { "HtmlEncode", "UrlDecode", "HtmlDecode", "UrlEncode" })
            yield return new TestCaseData("string", "string value", $"WebUtility.{method}(value)")
                .SetName($"WebUtility{method}_Diagnostic");

        foreach (var method in new[] { "UrlEncodeToBytes", "UrlDecodeToBytes" })
            yield return new TestCaseData("byte[]", "byte[] value", $"WebUtility.{method}(value, 0, value.Length)")
                .SetName($"WebUtility{method}_ReturnedArray_Diagnostic");
    }

    [TestCaseSource(nameof(ImpureCalls))]
    public async Task WebUtilityCall_Diagnostic(string returnType, string parameter, string expression)
    {
        var test = $@"
using System.Net;
using SharpProof.Attributes;

public class TestClass
{{
    [EnforcePure]
    public {returnType} {{|SP0002:TestMethod|}}({parameter})
    {{
        return {expression};
    }}
}}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
