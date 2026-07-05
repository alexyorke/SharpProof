using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Microsoft.CodeAnalysis.Testing;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class MethodCallTests
    {
        [Test]
        public async Task PureMethodCallingPureMethod_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public int PureHelperMethod(int x)
    {
        return x * 2;
    }

    [EnforcePure]
    public int TestMethod(int x)
    {
        // Call to pure method should be considered pure
        return PureHelperMethod(x) + 5;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodCallingImpureMethod_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    // Note: ImpureHelperMethod lacks [EnforcePure]
    public void ImpureHelperMethod()
    {
        Console.WriteLine(""This is impure""); // Impure
    }

    [EnforcePure]
    public void TestMethod()
    {
        // Call to impure method should trigger diagnostic on TestMethod
        ImpureHelperMethod();
    }
}";



            var expectedTestMethod = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(16, 17, 16, 27).WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedTestMethod });
        }

        [Test]
        public async Task ImpureMethodCallingPureMethod_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public int PureHelperMethod(int x)
    {
        return x * 2;
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        // Pure method call is fine, but console write makes method impure
        int result = PureHelperMethod(5);
        Console.WriteLine(result); // This makes the method impure
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task LinqTakeWithImpureCountArgument_Diagnostic()
        {
            var test = @"
using System;
using System.Linq;
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> {|SP0002:TestMethod|}(int[] values)
    {
        return values.Take(Console.Read());
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}


