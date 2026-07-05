using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class CheckedUserDefinedOperatorsTests
    {
        [Test]
        [NonParallelizable]
        public async Task CheckedUserDefinedOperator_BasicArithmetic_MissingAttributeAndUnknownPurityDiagnostics()
        {
            var test = @"
using System;
using SharpProof.Attributes;



namespace TestNamespace
{
    public readonly struct Money
    {
        public decimal Amount { get; }

        public Money(decimal amount)
        {
            Amount = amount;
        }

        // Regular operator for addition
        public static Money operator +(Money left, Money right)
        {
            return new Money(left.Amount + right.Amount);
        }

        // Checked operator for addition
        public static Money operator checked +(Money left, Money right)
        {
            return new Money(checked(left.Amount + right.Amount));
        }

        // Regular operator for subtraction
        public static Money operator -(Money left, Money right)
        {
            return new Money(left.Amount - right.Amount);
        }

        // Checked operator for subtraction
        public static Money operator checked -(Money left, Money right)
        {
            return new Money(checked(left.Amount - right.Amount));
        }

        // Regular operator for multiplication
        public static Money operator *(Money left, decimal multiplier)
        {
            return new Money(left.Amount * multiplier);
        }

        // Checked operator for multiplication
        public static Money operator checked *(Money left, decimal multiplier)
        {
            return new Money(checked(left.Amount * multiplier));
        }

        // Regular operator for division
        public static Money operator /(Money left, decimal divisor)
        {
            return new Money(left.Amount / divisor);
        }

        // Checked operator for division
        public static Money operator checked /(Money left, decimal divisor)
        {
            return new Money(checked(left.Amount / divisor));
        }
    }

    public class CheckedOperationsTest
    {
        [EnforcePure]
        public Money AddMoney(Money a, Money b)
        {
            // Operator source is available and pure, so this is pure. Remove marker.
            return checked(a + b);
        }

        [EnforcePure]
        public Money CalculateOrderTotal(Money[] prices, decimal taxRate)
        {
            // Operators source is available and pure, so this is pure. Remove markers.
            Money total = new Money(0);
            foreach (var price in prices)
            {
                total = checked(total + price);
            }
            return checked(total * (1 + taxRate));
        }
    }
}";


            var expectedGetValue = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(11, 24, 11, 30).WithArguments("get_Amount");
            var expectedCtor = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(13, 16, 13, 21).WithArguments(".ctor");
            var expectedOpAdd = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(19, 38, 19, 39).WithArguments("op_Addition");
            var expectedOpCheckedAdd = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(25, 46, 25, 47).WithArguments("op_CheckedAddition");
            var expectedOpSub = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(31, 38, 31, 39).WithArguments("op_Subtraction");
            var expectedOpCheckedSub = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(37, 46, 37, 47).WithArguments("op_CheckedSubtraction");
            var expectedOpMul = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(43, 38, 43, 39).WithArguments("op_Multiply");
            var expectedOpCheckedMul = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(49, 46, 49, 47).WithArguments("op_CheckedMultiply");
            var expectedOpDiv = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(55, 38, 55, 39).WithArguments("op_Division");
            var expectedOpCheckedDiv = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(61, 46, 61, 47).WithArguments("op_CheckedDivision");
            await VerifyCS.VerifyAnalyzerAsync(test, new[] {
                expectedGetValue, expectedCtor, expectedOpAdd, expectedOpCheckedAdd,
                expectedOpSub, expectedOpCheckedSub, expectedOpMul, expectedOpCheckedMul,
                expectedOpDiv, expectedOpCheckedDiv
            });
        }

        [Test]
        public async Task CheckedUserDefinedOperator_WithRegularOperator_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;



namespace TestNamespace
{
    public readonly struct Vector2D
    {
        public double X { get; }
        public double Y { get; }

        public Vector2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        // Regular operator
        public static Vector2D operator +(Vector2D left, Vector2D right)
        {
            return new Vector2D(left.X + right.X, left.Y + right.Y);
        }

        // Checked operator
        public static Vector2D operator checked +(Vector2D left, Vector2D right)
        {
            return new Vector2D(checked(left.X + right.X), checked(left.Y + right.Y));
        }

        // Regular subtraction operator
        public static Vector2D operator -(Vector2D left, Vector2D right)
        {
            return new Vector2D(left.X - right.X, left.Y - right.Y);
        }

        // Checked subtraction operator
        public static Vector2D operator checked -(Vector2D left, Vector2D right)
        {
            return new Vector2D(checked(left.X - right.X), checked(left.Y - right.Y));
        }

        // Magnitude property (readonly)
        public double Magnitude => Math.Sqrt(X * X + Y * Y);
    }

    public class CheckedAndRegularOperationsTest
    {
        [EnforcePure]
        public Vector2D AddVectors(Vector2D a, Vector2D b, bool useChecked)
        {
            // Both branches use pure operators defined in source.
            return useChecked ? checked(a + b) : a + b;
        }

        [EnforcePure]
        public double CalculateDistance(Vector2D a, Vector2D b)
        {
            // Operator source is now found and pure.
            Vector2D difference = checked(a - b);
            return difference.Magnitude;
        }
    }
}";
            var expected = new DiagnosticResult[] {
                VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(11, 23, 11, 24).WithArguments("get_X"),
                VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(12, 23, 12, 24).WithArguments("get_Y"),
                VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(14, 16, 14, 24).WithArguments(".ctor"),
                VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(21, 41, 21, 42).WithArguments("op_Addition"),
                VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(27, 49, 27, 50).WithArguments("op_CheckedAddition"),
                VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(33, 41, 33, 42).WithArguments("op_Subtraction"),
                VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(39, 49, 39, 50).WithArguments("op_CheckedSubtraction")
            };
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task CheckedUserDefinedOperator_ComplexExpression_Diagnostic()
        {
            var test = @$"
using System;
using System.Threading;
using SharpProof.Attributes;

public static class Operations
{{
    public static int AddChecked(int x, int y)
    {{
        return checked(x + y);
    }}
}}

public struct ComplexValue
{{
    public int Real {{ get; }}
    public int Imaginary {{ get; }}

    public ComplexValue(int real, int imaginary)
    {{
        Real = real;
        Imaginary = imaginary;
    }}

    // Checked addition operator
    public static ComplexValue operator +(ComplexValue a, ComplexValue b)
    {{
        // Use System.HashCode which is marked impure
        HashCode hash = default;
        hash.Add(a.Real);
        hash.Add(b.Real);
        int realSum = checked(a.Real + b.Real); // Use checked context

        // Use Operations.AddChecked
        int imaginarySum = Operations.AddChecked(a.Imaginary, b.Imaginary);

        return new ComplexValue(realSum, imaginarySum);
    }}

     // Checked subtraction operator
    public static ComplexValue operator -(ComplexValue a, ComplexValue b)
    {{
        int realDiff = checked(a.Real - b.Real); // Use checked context
        int imaginaryDiff = checked(a.Imaginary - b.Imaginary); // Use checked context
        return new ComplexValue(realDiff, imaginaryDiff);
    }}

     // Checked unary negation operator
    public static ComplexValue operator -(ComplexValue a)
    {{
        return new ComplexValue(checked(-a.Real), checked(-a.Imaginary)); // Use checked context
    }}

     // Example method using checked operators within a complex expression
    [EnforcePure]
    public static ComplexValue ComplexCalculationChecked(ComplexValue c1, ComplexValue c2, ComplexValue c3)
    {{
        // Nested checked operations
        ComplexValue intermediate = checked(c1 + c2);
        return checked(intermediate - c3);
    }}

    // Example method using checked operators within a complex expression
    [EnforcePure]
    public static ComplexValue FibonacciChecked(int n)
    {{
         if (n < 0) throw new ArgumentOutOfRangeException(nameof(n), ""Input must be non-negative."");
         if (n == 0) return new ComplexValue(0, 0);

         ComplexValue a = new ComplexValue(0, 0);
         ComplexValue b = new ComplexValue(1, 0);

         for (int i = 1; i < n; i++)
         {{
            ComplexValue temp = checked(a + b); // Checked operator used here
            a = b;
            b = temp;
         }}
         return b;
    }}
}}
            ";



            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                    .WithSpan(65, 32, 65, 48)
                                    .WithArguments("FibonacciChecked");


            var expected2 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                    .WithSpan(56, 32, 56, 57)
                                    .WithArguments("ComplexCalculationChecked");


            var expectedAddChecked = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(8, 23, 8, 33).WithArguments("AddChecked");
            var expectedGetReal = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(16, 16, 16, 20).WithArguments("get_Real");
            var expectedGetImaginary = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(17, 16, 17, 25).WithArguments("get_Imaginary");
            var expectedCtor = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(19, 12, 19, 24).WithArguments(".ctor");
            var expectedOpSub = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(41, 41, 41, 42).WithArguments("op_Subtraction");
            var expectedOpNeg = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(49, 41, 49, 42).WithArguments("op_UnaryNegation");

            await VerifyCS.VerifyAnalyzerAsync(test, new[] {
                expected, expected2, expectedAddChecked, expectedGetReal, expectedGetImaginary, expectedCtor,
                expectedOpSub, expectedOpNeg
            });
        }

        [Test]
        public async Task CheckedUserDefinedOperator_WithExceptionHandling_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;



namespace TestNamespace
{
    public readonly struct SafeInteger
    {
        public int Value { get; }

        public SafeInteger(int value)
        {
            Value = value;
        }

        // Regular addition
        public static SafeInteger operator +(SafeInteger left, SafeInteger right)
        {
            return new SafeInteger(left.Value + right.Value);
        }

        // Checked addition with potential overflow
        public static SafeInteger operator checked +(SafeInteger left, SafeInteger right)
        {
            return new SafeInteger(checked(left.Value + right.Value));
        }

        // Regular multiplication
        public static SafeInteger operator *(SafeInteger left, SafeInteger right)
        {
            return new SafeInteger(left.Value * right.Value);
        }

        // Checked multiplication with potential overflow
        public static SafeInteger operator checked *(SafeInteger left, SafeInteger right)
        {
            return new SafeInteger(checked(left.Value * right.Value));
        }
    }

    public class ExceptionHandlingTest
    {
        [EnforcePure]
        public SafeInteger TryOperation(SafeInteger a, SafeInteger b, bool multiply)
        {
            // Try/catch flow uses source-visible pure operators, so this stays pure.
            try
            {
                return multiply ? checked(a * b) : checked(a + b);
            }
            catch (OverflowException) // Catching exception is pure
            {
                return new SafeInteger(0); // Returning value is pure
            }
        }

        [EnforcePure]
        public (bool Success, SafeInteger Result) SafeAdd(SafeInteger a, SafeInteger b)
        {
            // Try/catch flow uses source-visible pure operators, so this stays pure.
           try
            {
                return (true, checked(a + b));
            }
            catch (OverflowException) // Catching exception is pure
            {
                return (false, new SafeInteger(0)); // Returning value is pure
            }
        }
    }
}";

            var expectedGetValue = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(11, 20, 11, 25).WithArguments("get_Value");
            var expectedCtor = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(13, 16, 13, 27).WithArguments(".ctor");
            var expectedOpAdd = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(19, 44, 19, 45).WithArguments("op_Addition");
            var expectedOpCheckedAdd = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(25, 52, 25, 53).WithArguments("op_CheckedAddition");
            var expectedOpMul = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(31, 44, 31, 45).WithArguments("op_Multiply");
            var expectedOpCheckedMul = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(37, 52, 37, 53).WithArguments("op_CheckedMultiply");
            await VerifyCS.VerifyAnalyzerAsync(test, new[] {
                expectedGetValue, expectedCtor, expectedOpAdd, expectedOpCheckedAdd,
                expectedOpMul, expectedOpCheckedMul
            });
        }

        [Test]
        public async Task CheckedUserDefinedOperator_ImpureConstructor_ReportsPureGetterOnly()
        {
            var test = @"
using System;
using System.IO;

// Define a custom type with a checked user-defined operator
public struct Percentage
{
    public double Value { get; private set; }

    public Percentage(double value)
    {
        if (value < 0 || value > 100)
            throw new ArgumentOutOfRangeException(nameof(value), ""Percentage must be between 0 and 100."" );
        Value = value;
    }

    // Checked multiplication operator
    public static Percentage operator *(Percentage p, double multiplier)
    {
        double newValue = p.Value * multiplier;
        // Potentially checked logic (could throw OverflowException if enabled project-wide or scope)
        return new Percentage(newValue);
    }
}

public class Calculator
{
    public void LogPercentageCalculation(Percentage initial, double multiplier)
    {
        // Impure operation despite using checked operator
        Percentage result = checked(initial * multiplier);
        File.WriteAllText(""calculation.log"", $""Result: {result.Value}"");
    }
}
";

            var expectedGetValue = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(8, 19, 8, 24).WithArguments("get_Value");

            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedGetValue });
        }

        [Test]
        public async Task CheckedUserDefinedOperator_WithExceptionHandling_AllMembersPure_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public readonly struct SafeIntegerPure
{
    public int Value
    {
        [Pure]
        get;
    }

    [Pure]
    public SafeIntegerPure(int value)
    {
        Value = value;
    }

    [Pure]
    public static SafeIntegerPure operator +(SafeIntegerPure left, SafeIntegerPure right)
    {
        return new SafeIntegerPure(left.Value + right.Value);
    }

    [Pure]
    public static SafeIntegerPure operator checked +(SafeIntegerPure left, SafeIntegerPure right)
    {
        return new SafeIntegerPure(checked(left.Value + right.Value));
    }

    [Pure]
    public static SafeIntegerPure operator *(SafeIntegerPure left, SafeIntegerPure right)
    {
        return new SafeIntegerPure(left.Value * right.Value);
    }

    [Pure]
    public static SafeIntegerPure operator checked *(SafeIntegerPure left, SafeIntegerPure right)
    {
        return new SafeIntegerPure(checked(left.Value * right.Value));
    }
}

public class CheckedFlowTest
{
    [EnforcePure]
    public SafeIntegerPure ComputeNoTry(SafeIntegerPure a, SafeIntegerPure b)
    {
        return checked(a + b);
    }

    [EnforcePure]
        public SafeIntegerPure TryCompute(SafeIntegerPure a, SafeIntegerPure b, bool useMultiply)
        {
            try
            {
                return useMultiply ? checked(a * b) : checked(a + b);
            }
            catch (OverflowException)
            {
                return new SafeIntegerPure(0);
            }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CheckedUserDefinedOperator_WithMutableState_ImpureMethod_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;



namespace TestNamespace
{
    public readonly struct Counter
    {
        public int Value { get; }

        public Counter(int value)
        {
            Value = value;
        }

        // Regular addition operator
        public static Counter operator +(Counter left, Counter right)
        {
            return new Counter(left.Value + right.Value);
        }

        // Checked addition operator
        public static Counter operator checked +(Counter left, Counter right)
        {
            return new Counter(checked(left.Value + right.Value));
        }
    }

    public class MutableStateTest
    {
        private int _count;

        [EnforcePure]
        public Counter IncrementCounter(Counter counter)
        {
            // Impure operation that modifies instance state
            _count++; // This makes the method impure
            return checked(counter + new Counter(1)); // checked operator call is pure
        }

        [EnforcePure]
        public Counter Add(Counter a, Counter b)
        {
            // Using a checked user-defined operator here stays pure.
            return checked(a + b);
        }
    }
}";

            var expectedGetValue = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(11, 20, 11, 25).WithArguments("get_Value");
            var expectedCtor = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(13, 16, 13, 23).WithArguments(".ctor");
            var expectedOpAdd = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(19, 40, 19, 41).WithArguments("op_Addition");
            var expectedOpCheckedAdd = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(25, 48, 25, 49).WithArguments("op_CheckedAddition");
            var expectedIncrementCounter = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(36, 24, 36, 40).WithArguments("IncrementCounter");
            await VerifyCS.VerifyAnalyzerAsync(test, new[] {
                expectedGetValue, expectedCtor, expectedOpAdd, expectedOpCheckedAdd, expectedIncrementCounter
            });
        }
    }
}


