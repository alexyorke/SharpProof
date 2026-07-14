using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class WhileLoopTests
{
    [Test]
    public async Task PureWhileLoop_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public int PureMethod(int limit)
        {
            int i = 0;
            int sum = 0;
            while (i < limit) // Pure condition
            {
                sum += i; // Pure body
                i++;      // Pure body
            }
            return sum;
        }
    }
}
" + MathAndAttributeTestSources.MinimalEnforcePureAttribute;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }


    [Test]
    public async Task ImpureConditionInWhileLoop_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        private int _counter = 0;

        // Marked with EnforcePure but is impure due to field modification
        [EnforcePure]
        private bool {|SP0002:IsConditionMet|}()
        {
            _counter++; // Impure operation
            return _counter < 5;
        }

        // Marked with EnforcePure, calls impure method in loop condition
        [EnforcePure]
        public void {|SP0002:TestMethod|}()
        {
            while (IsConditionMet()) // Impure call in condition
            {
                // Loop body doesn't matter if condition is impure
            }
        }
    }
}
" + MathAndAttributeTestSources.MinimalEnforcePureAttribute;


        await VerifyCS.VerifyAnalyzerAsync(test);
    }


    [Test]
    public async Task ImpureBodyInWhileLoop_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        private int _state = 0; // Mutable state

        [EnforcePure]
        public void TestMethod(int limit)
        {
            int i = 0;
            while (i < limit) // Pure condition
            {
                _state += i; // Impure operation in body
                i++;
            }
        }
    }
}
" + MathAndAttributeTestSources.MinimalEnforcePureAttribute;


        var expected = new[]
        {
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                .WithSpan(12, 21, 12, 31)
                .WithArguments("TestMethod")
        };

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task DoWhileFalse_StillAnalyzesBody_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public void {|SP0002:TestMethod|}()
        {
            do
            {
                Console.WriteLine(""runs once"");
            }
            while (false);
        }
    }
}
" + MathAndAttributeTestSources.MinimalEnforcePureAttribute;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
