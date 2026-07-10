using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class RecordTests
{
    private const string MinimalEnforcePureAttributeSource = """
        namespace SharpProof.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Constructor | System.AttributeTargets.Property | System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface)]
            public sealed class EnforcePureAttribute : System.Attribute { }
        }
        """;

    [Test]
    public async Task ImmutableRecord_NoDiagnostic()
    {
        var isExternalInit = """
                             namespace System.Runtime.CompilerServices { internal static class IsExternalInit {} }
                             """;

        var testCode = """
                       // Requires C# 9+ and IsExternalInit polyfill
                       #nullable enable
                       using System;
                       using SharpProof.Attributes;
                       using System.Runtime.CompilerServices;

                       // No CS0518 expected here due to polyfill
                       public record Person(string Name, int Age);

                       public class TestClass
                       {
                           [EnforcePure]
                           public string GetPersonInfo(Person person)
                           {
                               // Accessing properties of an immutable record should be pure
                               return $"{ person.Name} is { person.Age } years old";
                           }
                       }
                       """;

        var verifierTest = new VerifyCS.Test
        {
            TestState =
            {
                Sources = { testCode, isExternalInit, MinimalEnforcePureAttributeSource }
            }
        };

        await verifierTest.RunAsync();
    }

    [Test]
    public async Task RecordWithPureMethod_NoDiagnostic()
    {
        var test = """
                   // Requires C# 9+
                   #nullable enable
                   using System;
                   using SharpProof.Attributes;
                   using System.Runtime.CompilerServices;

                   """ + MinimalEnforcePureAttributeSource + """
                                                             public record Calculator
                                                             {
                                                                 [EnforcePure]
                                                                 public int Add(int x, int y) => x + y; // Add is pure
                                                             }

                                                             public class TestClass
                                                             {
                                                                 [EnforcePure]
                                                                 // UseCalculator calls a pure method, so it should be considered pure by the analyzer
                                                                 public int UseCalculator(Calculator calc, int a, int b)
                                                                 {
                                                                     return calc.Add(a, b);
                                                                 }
                                                             }
                                                             """;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task MutableRecord_ShouldProduceDiagnostic()
    {
        var source = """
                     #nullable enable
                     using System;
                     using SharpProof.Attributes;

                     // Define the record within the test string
                     public record MutablePerson
                     {
                         // CS8618 is on the property name - MARKUP REMOVED
                         public string Name { get; set; }
                         public int Age { get; set; }
                     }

                     public class TestClass
                     {
                         [EnforcePure] // Needs EnforcePureAttribute defined below
                         public void UpdatePerson(MutablePerson person)
                         {
                             person.Name = "John"; // Escaped quote needed for verbatim is ""
                         }
                     }

                     // Define EnforcePureAttribute locally
                     namespace SharpProof.Attributes
                     {
                         [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
                         public sealed class EnforcePureAttribute : Attribute { }
                     }
                     """;

        var expected = new[]
        {
            DiagnosticResult.CompilerError("CS8618")
                .WithSpan(9, 19, 9, 23)
                .WithSpan(9, 19, 9, 23)
                .WithArguments("property", "Name"),

            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule).WithSpan(16, 17, 16, 29)
                .WithArguments("UpdatePerson"),

            VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(9, 19, 9, 23)
                .WithArguments("get_Name"),
            VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(10, 16, 10, 19)
                .WithArguments("get_Age")
        };

        await VerifyCS.VerifyAnalyzerAsync(source, expected);
    }
}
