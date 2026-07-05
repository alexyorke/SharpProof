using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;
using SharpProof.Attributes;

namespace SharpProof.Test
{
    [TestFixture]
    public class UsingTests
    {
        [Test]
        public async Task PureMethodWithUsing_MissingAttributeDiagnostic()
        {
            var code = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        using (var disposable = new PureDisposable()) // Pure disposable, Dispose is pure
        {
            return 1; // Body is pure
        }
    }
}

public class PureDisposable : IDisposable
{
    // Dispose is implicitly pure (empty body)
    public void Dispose() { }
}";

            await VerifyCS.VerifyAnalyzerAsync(code);
        }

        [Test]
        public async Task ImpureMethodWithUsing_Diagnostic()
        {
            var test = @$"
using System;
using SharpProof.Attributes;
using System.IO;

public class TestClass
{{
    [EnforcePure]
    public void TestMethod()
    {{
        using (var file = File.OpenRead(""test.txt"")) // Impure resource acquisition
        {{
            // Some operation
        }}
    }}
}}";


            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                  .WithSpan(9, 17, 9, 27)
                                  .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task PureMethodWithUsingAndImpureOperation_Diagnostic()
        {
            var test = @$"
using System;
using SharpProof.Attributes;
using System.IO;

public class PureDisposable : IDisposable
{{
    public void Dispose() {{ }} // Empty dispose method is pure
}}

public class TestClass
{{
    [EnforcePure]
    public void TestMethod()
    {{
        using (var disposable = new PureDisposable())
        {{
            Console.WriteLine(""Inside using""); // Impure operation inside body
        }}
    }}
}}";


            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                   .WithSpan(14, 17, 14, 27)
                                   .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }
    }
}


