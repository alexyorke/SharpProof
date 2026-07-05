using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;
using SharpProof.Attributes;
using System;

namespace SharpProof.Test
{







    [TestFixture]
    public class RefReadonlyParameterTests
    {
        [Test]
        public async Task PureMethodWithRefReadonlyParameter_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    // Reading ref readonly parameters is pure.
    public int Sum(ref readonly int x, ref readonly int y)
    {
        // Only reading values, no modifications - this should be pure
        return x + y;
    }
}";


            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodWithRefReadonlyParameter_AccessingFields_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public struct Point
{
    public int X;
    public int Y;
}

public class TestClass
{
    [EnforcePure]
    public int CalculateDistance(ref readonly Point p1, ref readonly Point p2)
    {
        // Accessing struct fields through ref readonly parameter - this is pure
        int dx = p1.X - p2.X;
        int dy = p1.Y - p2.Y;
        return (int)Math.Sqrt(dx * dx + dy * dy);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodWithRefReadonlyParameter_AssigningLocally_NoDiagnostic()
        {


            var test = @$"
using SharpProof.Attributes;

public struct LargeStruct
{{
    public int Value1;
    public int Value2;
    public int Value3;
}}

public class TestClass
{{
    [EnforcePure]
    public int GetMax(ref readonly LargeStruct data)
    {{
        LargeStruct localCopy = data;
        // Reading fields from a by-value local struct copy is pure.
        int max = localCopy.Value1;
        if (localCopy.Value2 > max) max = localCopy.Value2;
        if (localCopy.Value3 > max) max = localCopy.Value3;
        return max;
    }}
}}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodAttemptingToModifyRefReadonlyParameter_CompilationError()
        {
            var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void Increment(ref readonly int x)
    {
        // Cannot modify ref readonly parameter
        x++; 
    }
}";


            var expectedAnalyzer = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                         .WithSpan(7, 17, 7, 26)
                                         .WithArguments("Increment");
            var expectedCompiler = DiagnosticResult.CompilerError("CS8331")
                                           .WithSpan(10, 9, 10, 10)
                                           .WithArguments("variable", "x");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedAnalyzer, expectedCompiler);
        }

        [Test]
        public async Task PureMethodWithRefReadonlyParameter_PassingToAnotherMethodWithRef_CompilationError()
        {
            var test = @"
using SharpProof.Attributes;

public struct Point { public int X; public int Y; }

public class TestClass
{
    private void ModifyPoint(ref Point p) { p.X++; }

    [EnforcePure]
    public void TestModify(ref readonly Point p)
    {
        // Cannot pass 'ref readonly' to 'ref'
        ModifyPoint(ref p);
    }
}";

            var expectedTestModify = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                            .WithSpan(11, 17, 11, 27)
                                            .WithArguments("TestModify");

            var expectedCompiler = DiagnosticResult.CompilerError("CS8329")
                                           .WithSpan(14, 25, 14, 26)
                                           .WithArguments("variable", "p");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedTestModify, expectedCompiler);
        }

        [Test]
        public async Task PureMethodWithRefReadonlyStruct_AccessingMethodsOnStruct_NoDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System;

public readonly struct ReadOnlyValue
{
    private readonly int _value;

    [Pure] // Assume constructor is pure
    public ReadOnlyValue(int value) { _value = value; }

    [EnforcePure] // Marked pure
    public int GetValue() => _value;

    [EnforcePure] // Marked pure
    public ReadOnlyValue Add(int amount) => new ReadOnlyValue(_value + amount);
}

public class TestClass
{
    [EnforcePure]
    public int Process(ref readonly ReadOnlyValue value)
    {
        // Calls pure methods on ref readonly struct
        return value.GetValue() + value.Add(5).GetValue();
    }
}";




            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodPassingRefReadonlyToImpureMethod_DiagnosticAndCompilationError()
        {
            var test = @"
using SharpProof.Attributes;
using System;

public static class GlobalState
{
    public static int Value = 0;
}

public class TestClass
{
    [EnforcePure]
    public void Process(ref readonly int value)
    {
        // Tries to pass ref readonly to ref method - Compiler Error CS8329
        // Also calls ModifyGlobalState which is impure
        ModifyGlobalState(ref value);
    }

    // Marked as [EnforcePure] but is actually impure
    [EnforcePure]
    private void ModifyGlobalState(ref int stateValue)
    {
        GlobalState.Value += stateValue;
    }
}";


            var expectedProcess = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                         .WithSpan(13, 17, 13, 24)
                                         .WithArguments("Process");

            var expectedModify = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                         .WithSpan(22, 18, 22, 35)
                                         .WithArguments("ModifyGlobalState");
            var expectedCompiler = DiagnosticResult.CompilerError("CS8329")
                                           .WithSpan(17, 31, 17, 36)
                                           .WithArguments("variable", "value");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedProcess, expectedModify, expectedCompiler);
        }
    }
}


