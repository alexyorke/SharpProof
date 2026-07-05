using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<SharpProof.Analyzer.SharpProofAnalyzer>;
using SharpProof.Attributes;
using System.IO;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace SharpProof.Test
{
    [TestFixture]
    public class BasicPurityTests
    {
        [Test]
        public async Task TestPotentiallyPureMethod_MissingAttributeDiagnostic()
        {
            var testCode = @"
using SharpProof.Attributes;

public class TestClass
{
    // No [Pure] or [EnforcePure] attribute here, but returns constant
    // Analyzer reports SP0004.
    public int GetConstant()
    {
        return 42;
    }
}";

            var expectedSP0004 = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                    .WithSpan(8, 16, 8, 27)
                                    .WithArguments("GetConstant");
            await VerifyCS.VerifyAnalyzerAsync(testCode, expectedSP0004);
        }

        [Test]
        public async Task TestEnforcePureMethodReturningParameter_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int GetParameter(int x)
    {
        return x; // Parameter return is pure.
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestPureMethodReturningConstant_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure] // Mark it for analysis, even though it should pass
    public int GetTheAnswer()
    {
        return 42; // Constant return is pure.
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestPureMethodReturningConstantString_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure]
    public string GetString() => ""Hello""; // String literal is pure.
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestPureMethodReturningConstantBool_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure]
    public bool GetTrue() { return true; }
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestPureMethodReturningConstantNull_NoDiagnostics()
        {
            var testCode = @"
#nullable enable // Enable nullable context for string?
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure]
    public string? GetNull() => null;
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestEnforcePureReturningConstField_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    private const int MyConst = 10;
    [EnforcePure]
    public int GetConst() => MyConst; // Const field access is pure via GetConstantValue
}";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestEnforcePureReturningStaticReadonlyField_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    private static readonly string Greeting = ""Hi"";
    [EnforcePure]
    public string GetGreeting() { return Greeting; }
}";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestEnforcePureReturningSimpleCalculation_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure]
    public int GetTwo() => 1 + 1; // Constant folding makes this pure
}";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestEnforcePureReturningDefault_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure]
    public int GetDefaultInt() => default;
}";


            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestImpureMethod_ShouldBeFlagged_OnMethodName()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class ImpureTest
{
    private int _state = 0; // Mutable state

    [EnforcePure]
    public int {|SP0002:ImpureMethod|}(int input)
    {
        _state += input; // Modifies state, impure
        return _state;
    }
}";


            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TestEnforcePureReturningTypeof_NoDiagnostics()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TypeInfo
{
    [EnforcePure]
    public Type GetIntegerType()
    {
        // typeof is generally pure
        return typeof(int);
    }

    [EnforcePure]
    public Type GetStringType<T>(T value) where T : class
    {
        // typeof with generic parameter might be complex
        return typeof(T);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }


        [Test]
        public async Task TestPotentiallyPureMethod_WithoutAttribute_ShouldWarnSP0004()
        {
            var test = @"
using System;

public class PotentialPurity
{
    // Potentially pure: Returns a constant
    public int GetConstantWithoutAttribute() => 42;

    // Potentially pure: String manipulation
    public string GetGreetingWithoutAttribute(string name) => ""Hello, "" + name;

    // Potentially pure: Simple calculation
    public int GetCalcWithoutAttribute(int x) => x * 2;

    // Potentially pure: Uses nameof
    public string GetNameofWithoutAttribute(int parameter) => nameof(parameter);

    // Impure: Console output
    public void ImpureMethod() => Console.WriteLine(""Side effect!"");

    // Impure: Modifies state
    private int _counter;
    public void ImpureStateChange() => _counter++;
}";


            var expectedConst = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                     .WithSpan(7, 16, 7, 43)
                                     .WithArguments("GetConstantWithoutAttribute");
            var expectedGreeting = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                     .WithSpan(10, 19, 10, 46)
                                     .WithArguments("GetGreetingWithoutAttribute");
            var expectedCalc = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                    .WithSpan(13, 16, 13, 39)
                                    .WithArguments("GetCalcWithoutAttribute");

            var expectedNameof = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                     .WithSpan(16, 19, 16, 44)
                                     .WithArguments("GetNameofWithoutAttribute");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedConst, expectedGreeting, expectedCalc, expectedNameof);
        }


        [Test]
        public async Task TestPotentiallyPureMethodCallingEnforcedPure_ShouldWarnSP0004()
        {
            var test = @"
using SharpProof.Attributes;

public class PurityChain
{
    [EnforcePure]
    public int PureHelper() => 10;

    // Potentially pure, calls an enforced pure method
    public int CallingPureHelper()
    {
        return PureHelper() + 5;
    }
}";


            var expectedSP0004 = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                    .WithSpan(10, 16, 10, 33)
                                    .WithArguments("CallingPureHelper");
            await VerifyCS.VerifyAnalyzerAsync(test, expectedSP0004);
        }


        [Test]
        public async Task TestPotentiallyPureMethodCallingChain_ShouldWarnSP0004()
        {
            var test = @"
public class CallChain
{
    // Method A: Pure base case
    public int MethodA() => 5;

    // Method B: Calls Method A, potentially pure
    public int MethodB() => MethodA() * 2;

    // Method C: Calls Method B, potentially pure
    public int MethodC() => MethodB() + 3;

    // Method D: Calls Method C, potentially pure
    public int MethodD() => MethodC() - 1;
}";


            var expectedA = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                               .WithSpan(5, 16, 5, 23)
                               .WithArguments("MethodA");
            var expectedB = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                               .WithSpan(8, 16, 8, 23)
                               .WithArguments("MethodB");
            var expectedC = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                               .WithSpan(11, 16, 11, 23)
                               .WithArguments("MethodC");
            var expectedD = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                               .WithSpan(14, 16, 14, 23)
                               .WithArguments("MethodD");
            await VerifyCS.VerifyAnalyzerAsync(test, expectedA, expectedB, expectedC, expectedD);
        }


        [Test]
        public async Task TestPotentiallyPureLongerChain_ShouldWarnSP0004()
        {
            var test = @"
public class LongerChain
{
    public int MethodA() => 1;
    public int MethodB() => MethodA() + 1;
    public int MethodC() => MethodB() + 1;
    public int MethodD() => MethodC() + 1;
    public int MethodE() => MethodD() + 1;
    public int MethodF() => MethodE() + 1;
}";

            var expectedA = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(4, 16, 4, 23).WithArguments("MethodA");
            var expectedB = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(5, 16, 5, 23).WithArguments("MethodB");
            var expectedC = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(6, 16, 6, 23).WithArguments("MethodC");
            var expectedD = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(7, 16, 7, 23).WithArguments("MethodD");
            var expectedE = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(8, 16, 8, 23).WithArguments("MethodE");
            var expectedF = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(9, 16, 9, 23).WithArguments("MethodF");
            await VerifyCS.VerifyAnalyzerAsync(test, expectedA, expectedB, expectedC, expectedD, expectedE, expectedF);
        }


        [Test]
        public async Task TestPotentiallyPureCallingTwoPureMethods_ShouldWarnSP0004()
        {
            var test = @"
using SharpProof.Attributes;

public class CombinedPurity
{
    [EnforcePure]
    public int Add(int a, int b) => a + b;

    [EnforcePure]
    public int Multiply(int a, int b) => a * b;

    // Potentially pure, calls two enforced pure methods
    public int GetSum(int x, int y, int z)
    {
        int sum1 = Add(x, y);
        int product = Multiply(sum1, z);
        return product;
    }
}";

            var expectedSP0004 = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                    .WithSpan(13, 16, 13, 22)
                                    .WithArguments("GetSum");
            await VerifyCS.VerifyAnalyzerAsync(test, expectedSP0004);
        }

        [Test]
        public async Task TestEnforcePureWithMultipleReturns_ShouldFlagSP0002()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class ConditionalReturn
{
    private static bool _flag = false; // Static state

    [EnforcePure]
    public int {|SP0002:GetValueBasedOnFlag|}(int input)
    {
        if (_flag) // Reads static state
        {
            return input * 2;
        }
        _flag = true; // Modifies static state
        return input + 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TestEnforcePureWithLocalConstReturn_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure]
    public int GetLocalConst()
    {
        const int x = 5;
        return x; // GetConstantValue works on local const reference
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestEnforcePureWithLocalVarFromPureCall_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure] private int PureHelper() => 10;

    [EnforcePure]
    public int GetValFromPureCall()
    {
        var temp = PureHelper();
        return temp; // Local assigned from a pure helper remains pure.
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestEnforcePureWithLocalVarFromImpureCall_ShouldFlagSP0002()
        {
            var testCode = @"
using SharpProof.Attributes;
using System;
public class TestClass
{
    [EnforcePure] private int {|SP0002:ImpureHelper|}() { Console.WriteLine(); return 0; }
    [EnforcePure]
    public int {|SP0002:GetValFromImpureCall|}()
    {
        var x = ImpureHelper();
        return x;
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestEnforcePureWithAssignment_ShouldFlagSP0002()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class AssignmentTest
{
    private int _instanceField = 0; // Instance state

    [EnforcePure]
    public int {|SP0002:UpdateAndGetValue|}(int value)
    {
        _instanceField = value; // Assignment to instance field (impure)
        return _instanceField;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TestEnforcePureWithIfElseReturn_ShouldFlagSP0002()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class IfElsePurity
{
    private int _threshold = 10; // Instance state

    [EnforcePure]
    public string {|SP0002:CheckThreshold|}(int value)
    {
        if (value > _threshold) // Reads instance state
        {
             _threshold++; // Modifies instance state (impure)
            return ""Above threshold"";
        }
        else
        {
            return ""Below or at threshold"";
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }



        [Test]
        public async Task TestPureMethodReturningConstantDouble_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure]
    public double GetPi() => 3.14159;
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestPureMethodReturningConstantDecimal_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure]
    public decimal GetMoney() { return 123.45m; }
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestPureMethodReturningConstantFloat_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure]
    public float GetRatio() => 0.5f;
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestPureMethodReturningConstantLong_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure]
    public long GetBigNumber() { return 9_000_000_000_000_000_000L; }
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestPureMethodReturningConstantChar_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure]
    public char GetInitial() => 'J';
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestPureMethodReturningConstantEnum_NoDiagnostics()
        {
            var test = @"
using SharpProof.Attributes;
using System;

public enum MyEnum { A, B }

public class TestClass
{
    [EnforcePure]
    public MyEnum GetEnum() => MyEnum.A; // Constant enum access, should be pure
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TestPureMethodReturningNameof_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure]
    public string GetClassName() => nameof(TestClass);

    [EnforcePure]
    public string GetStringName() { return nameof(System.String); }
}";



            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestPureMethodReturningConstantCalculation_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    private const int FIVE = 5;
    [EnforcePure]
    public int GetSum() => 2 + 3; // Pure

    [EnforcePure]
    public int GetProduct() { return FIVE * 10; } // Pure: Reads const field
}";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }



        [Test]
        public async Task TestEnforcePureWithMultipleLocalDecls_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure] private int PureHelper1() => 1;
    [EnforcePure] private int PureHelper2() => 2;

    [EnforcePure]
    public int GetFromMultipleLocals()
    {
        var a = PureHelper1();
        const int b = 10;
        var c = PureHelper2();
        // Returning the combined expression would exercise a broader local/binary purity path.
        return b; // Return simple constant
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }



        [Test]
        public async Task TestEnforcePureWithPureBinaryOp_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure] private int PureHelper() => 5;
    [EnforcePure]
    public int BinaryOpPure(int x)
    {
        const int y = 10;
        var z = PureHelper();
        return x + y + z; // Parameter + Const + Pure Local Var
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestEnforcePureWithImpureBinaryOp_ShouldFlagSP0002()
        {
            var testCode = @"
using SharpProof.Attributes;
using System;
public class TestClass
{
    [EnforcePure] private int {|SP0002:ImpureHelper|}() { Console.WriteLine(); return 0; } 
    [EnforcePure]
    public int {|SP0002:BinaryOpImpure|}(int x)
    {
        return x + ImpureHelper(); // Parameter + Impure Call
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestEnforcePureWithPureUnaryOp_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure]
    public bool UnaryOpPure(bool b)
    {
        return !b; // Pure operand
    }

    [EnforcePure]
    public int UnaryMinusPure(int x)
    {
        return -x; // Pure operand
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestEnforcePureWithImpureUnaryOp_ShouldFlagSP0002()
        {
            var testCode = @"
using SharpProof.Attributes;
using System;
public class TestClass
{
    [EnforcePure] private bool {|SP0002:ImpureBool|}() { Console.WriteLine(); return false; }
    [EnforcePure]
    public bool {|SP0002:UnaryOpImpure|}()
    {
        return !ImpureBool(); // Impure operand
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestEnforcePureWithPureConditionalOp_NoDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;
public class TestClass
{
    [EnforcePure] private int Pure1() => 1;
    [EnforcePure] private int Pure2() => 2;
    [EnforcePure]
    public int ConditionalPure(bool condition)
    {
        return condition ? Pure1() : Pure2(); // Pure condition, pure branches
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestEnforcePureWithImpureConditionalOp_ShouldFlagSP0002()
        {
            var testCode = @"
using SharpProof.Attributes;
using System;
public class TestClass
{
    [EnforcePure] private int Pure1() => 1;
    [EnforcePure] private int {|SP0002:Impure2|}() { Console.WriteLine(); return 2; }
    [EnforcePure] private bool {|SP0002:ImpureCond|}() { Console.WriteLine(); return false; }

    [EnforcePure]
    public int {|SP0002:ConditionalImpureBranch|}(bool condition)
    {
        return condition ? Pure1() : Impure2(); // Impure false branch
    }

    [EnforcePure]
    public int {|SP0002:ConditionalImpureCondition|}()
    {
        return ImpureCond() ? Pure1() : 0; // Impure condition
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task TestEnforcePureWithMiscPureExpr_NoDiagnostics()
        {
            var testCode = @"
#nullable enable // Enable nullable annotations used below
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int MiscPure(int p, bool c)
    {
        const int localConst = 5;
        int x = sizeof(int); // sizeof(int) is a pure compile-time constant
        string? s = default(string?); // Nullable default expression is pure
        int y = default; 
        var z = c ? p : localConst;
        int u = -p;
        return z + u; 
    }
}
";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }


        [Test]
        public async Task ReadonlyRecordStructWithPureConstructor_MissingAttributeDiagnostics()
        {
            var test = @"
using SharpProof.Attributes;
using System;

public readonly record struct Zzz
{
    [Pure]
    public Zzz(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }
    public int Y { get; }
}
";

            var expectedGetX = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                  .WithSpan(14, 16, 14, 17)
                                  .WithArguments("get_X");
            var expectedGetY = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                  .WithSpan(15, 16, 15, 17)
                                  .WithArguments("get_Y");




            await VerifyCS.VerifyAnalyzerAsync(test, expectedGetX, expectedGetY);
        }
    }
}
