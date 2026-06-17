using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using PurelySharp.Analyzer;
using System.Text.Json;
using PurelySharp.Attributes;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class SerializationTests
    {
        private const string TestSetup = @"
#nullable enable
using System;
using System.Text.Json;
using PurelySharp.Attributes;

public class SimplePoco 
{ 
    public int Id { get; set; } 
    public string? Name { get; set; }
}
";

        [Test]
        public async Task JsonSerializePoco_Diagnostic()
        {
            var test = TestSetup + @"

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(SimplePoco poco)
    {
        // Serialization is impure because the runtime implementation reads serializer state and can throw.
        return JsonSerializer.Serialize(poco);
    }
}";





            var expectedGetterId = VerifyCS.Diagnostic(PurelySharpDiagnostics.MissingEnforcePureAttributeId)
                                          .WithSpan(9, 16, 9, 18)
                                          .WithArguments("get_Id");
            var expectedGetterName = VerifyCS.Diagnostic(PurelySharpDiagnostics.MissingEnforcePureAttributeId)
                                            .WithSpan(10, 20, 10, 24)
                                            .WithArguments("get_Name");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedGetterId, expectedGetterName);
        }

        [Test]
        public async Task ImpureMethodWithJsonDeserializePoco_Diagnostic()
        {
            var test = TestSetup + @"

public class TestClass
{
    [EnforcePure]
    public SimplePoco? TestMethod(string json)
    {
        // Deserialization should be flagged as impure
        return JsonSerializer.Deserialize<SimplePoco>(json);
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(17, 24, 17, 34)
                                 .WithArguments("TestMethod");


            var expectedGetterId = VerifyCS.Diagnostic(PurelySharpDiagnostics.MissingEnforcePureAttributeId)
                                          .WithSpan(9, 16, 9, 18)
                                          .WithArguments("get_Id");
            var expectedGetterName = VerifyCS.Diagnostic(PurelySharpDiagnostics.MissingEnforcePureAttributeId)
                                            .WithSpan(10, 20, 10, 24)
                                            .WithArguments("get_Name");

            await VerifyCS.VerifyAnalyzerAsync(test, expected, expectedGetterId, expectedGetterName);
        }




    }
}
