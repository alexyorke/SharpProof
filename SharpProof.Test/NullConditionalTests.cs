using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class NullConditionalTests
    {
        [Test]
        public async Task PureMethodWithNullConditional_MissingAttributeAndUnknownPurityDiagnostics()
        {

            var test = """
using System;
using SharpProof.Attributes;

public class TestClass
{
    public string Value { get; set; }

    [EnforcePure]
    public int? TestMethod(TestClass obj)
    {
        // Pure: Null conditional access to a property length
        return obj?.Value?.Length;
    }
}
""";



            var expectedGet = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(6, 19, 6, 24).WithArguments("get_Value");
            var expectedMethod = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(9, 17, 9, 27).WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expectedGet, expectedMethod);
        }

        [Test]
        public async Task PureMethodWithNullConditionalAndImpureOperation_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    private int _field;

    [EnforcePure]
    public string {|SP0002:TestMethod|}(TestClass obj)
    {
        // Null conditional is pure, but field increment is impure
        var result = obj?.ToString() ?? ""null"";
        _field++;
        return result;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}


