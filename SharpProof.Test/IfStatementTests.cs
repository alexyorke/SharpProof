using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Analyzer;
using Microsoft.CodeAnalysis;
using System.Threading.Tasks;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    [Parallelizable(ParallelScope.Children)]
    public class IfStatementTests
    {
        private static int _impureField = 0;

        private static int ImpureMethod()
        {
            _impureField++;
            return _impureField;
        }

        private static bool IsEven(int n) => n % 2 == 0;


        [Test]
        public async Task PureIfElse_ShouldPass()
        {
            var testCode = @"
using SharpProof.Attributes;

public class TestClass
{
    // SP0004 expected here
    private static bool IsEven(int n) => n % 2 == 0; // Pure

    [EnforcePure]
    public int PureIfExample(int input)
    {
        if (IsEven(input)) // Pure condition
        {
            return input / 2; // Pure branch
        }
        else
        {
            return input * 2; // Pure branch
        }
    }
}
";
            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                   .WithSpan(7, 25, 7, 31).WithArguments("IsEven");
            await VerifyCS.VerifyAnalyzerAsync(testCode, expected);
        }


        [Test]
        public async Task PureIf_NoElse_ShouldPass()
        {
            var testCode = @"
using SharpProof.Attributes;

public class TestClass
{
    // SP0004 expected here
    private static bool IsAlwaysTrue() => true; // Pure

    [EnforcePure]
    public int PureIfNoElseExample(int input)
    {
        int result = input;
        if (IsAlwaysTrue()) // Pure condition
        {
             result = input + 1; // Pure branch (local assignment)
        }
        return result; // Pure return
    }
}
";
            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                   .WithSpan(7, 25, 7, 37).WithArguments("IsAlwaysTrue");
            await VerifyCS.VerifyAnalyzerAsync(testCode, expected);
        }

        [Test]
        public async Task ImpureCondition_ShouldReportSP0002()
        {
            var testCode = @"
using SharpProof.Attributes;
using System; // For DateTime

public class TestClass
{
     // SP0004 *might* be reported here depending on analysis, but focus is SP0002
     private static bool ImpureCondition() => DateTime.Now.Ticks > 0; // Impure

    [EnforcePure]
    public int ImpureConditionExample(int input)
    {
        if (ImpureCondition()) // Impure condition
        {
            return input / 2;
        }
        else
        {
            return input * 2;
        }
    }
}
";
            var expectedSP0002_Caller = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                            .WithLocation(11, 16)
                                            .WithArguments("ImpureConditionExample");
            await VerifyCS.VerifyAnalyzerAsync(testCode, expectedSP0002_Caller);
        }

        [Test]
        public async Task ImpureIfBranch_ShouldFail()
        {
            var testCode = @"
using SharpProof.Attributes;

public class TestClass
{
    private static int _impureField = 0;
    // Impure method
    private static int ImpureMethod() { _impureField++; return _impureField; }
    // SP0004 expected here
    private static bool IsEven(int n) => n % 2 == 0; // Pure

    [EnforcePure]
    public int ImpureIfBranchExample(int input)
    {
        if (IsEven(input)) // Pure condition
        {
            return ImpureMethod(); // Impure branch
        }
        else
        {
            return input * 2; // Pure branch
        }
    }
}
";

            var expectedSP0002_Caller = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                            .WithLocation(13, 16)
                                            .WithArguments("ImpureIfBranchExample");
            var expectedSP0004_IsEven = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                            .WithSpan(10, 25, 10, 31)
                                            .WithArguments("IsEven");
            await VerifyCS.VerifyAnalyzerAsync(testCode, expectedSP0002_Caller, expectedSP0004_IsEven);
        }

        [Test]
        public async Task ImpureElseBranch_ShouldFail()
        {
            var testCode = @"
using SharpProof.Attributes;

public class TestClass
{
    private static int _impureField = 0;
    // Impure method
    private static int ImpureMethod() { _impureField++; return _impureField; }
     // SP0004 expected here
    private static bool IsEven(int n) => n % 2 == 0; // Pure

    [EnforcePure]
    public int ImpureElseBranchExample(int input)
    {
        if (IsEven(input)) // Pure condition
        {
            return input / 2; // Pure branch
        }
        else
        {
             return ImpureMethod(); // Impure branch
        }
    }
}
";




            var expectedSP0002_Caller = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                             .WithLocation(13, 16)
                                             .WithArguments("ImpureElseBranchExample");
            var expectedSP0004_IsEven = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                             .WithSpan(10, 25, 10, 31)
                                             .WithArguments("IsEven");
            await VerifyCS.VerifyAnalyzerAsync(testCode, expectedSP0002_Caller, expectedSP0004_IsEven);
        }

        [Test]

        public async Task NestedPure_ShouldPass()
        {
            var testCode = @"
using SharpProof.Attributes;

public class TestClass
{
    // SP0004 expected here
    private static bool IsPositive(int n) => n > 0; // Pure
    // SP0004 expected here
    private static bool IsEven(int n) => n % 2 == 0; // Pure

    [EnforcePure]
    public int NestedPureIfExample(int input)
    {
        if (IsPositive(input)) // Pure outer condition
        {
            if (IsEven(input)) // Pure inner condition
            {
                 return 1; // Pure inner branch
            }
            else
            {
                 return -1; // Pure inner branch
            }
        }
        else
        {
            return 0; // Pure outer else
        }
    }
}
";
            var expectedIsPositive = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                             .WithSpan(7, 25, 7, 35).WithArguments("IsPositive");
            var expectedIsEven = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                         .WithSpan(9, 25, 9, 31).WithArguments("IsEven");
            await VerifyCS.VerifyAnalyzerAsync(testCode, expectedIsPositive, expectedIsEven);
        }

        [Test]
        public async Task NestedImpureIf_ShouldReportSP0002()
        {
            var testCode = @"
using SharpProof.Attributes;
using System; // For DateTime

public class TestClass
{
    // SP0004 expected here
    private static bool IsPositive(int n) => n > 0; // Pure
     // SP0004 expected here
    private static bool IsEven(int n) => n % 2 == 0; // Pure
    // No SP0004 expected
    private static bool ImpureCondition() => DateTime.Now.Ticks > 0; // Impure

    [EnforcePure]
    public int NestedImpureIfExample(int input)
    {
        if (IsPositive(input)) // Pure outer condition
        {
            if (ImpureCondition()) // Impure inner condition
            {
                 return 1;
            }
            else
            {
                 return -1;
            }
        }
        else
        {
            return 0;
        }
    }
}
";
            var expectedSP0004_IsPositive = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                               .WithSpan(8, 25, 8, 35)
                                               .WithArguments("IsPositive");
            var expectedSP0004_IsEven = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                               .WithSpan(10, 25, 10, 31)
                                               .WithArguments("IsEven");
            var expectedSP0002_Caller = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                               .WithLocation(15, 16)
                                               .WithArguments("NestedImpureIfExample");

            await VerifyCS.VerifyAnalyzerAsync(testCode, expectedSP0002_Caller, expectedSP0004_IsPositive, expectedSP0004_IsEven);
        }
    }
}
