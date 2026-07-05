using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;
using SharpProof.Attributes;
using System;
using System.IO;

namespace SharpProof.Test
{
    [TestFixture]
    public class NumericIntPtrTests
    {

        private const string AttributeSource = @"
[System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Constructor | System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface)]
public sealed class EnforcePureAttribute : System.Attribute { }
";


        private static string CreateTestWithAttribute(string testCode)
        {

            return $@"
using System;
using SharpProof.Attributes;
{AttributeSource}

namespace TestNamespace
{{
{testCode}
}}";
        }


        [Test]
        public async Task NativeInt_PureMethod_ReportsMissingAttributeDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes; // Assuming AttributeSource is prepended elsewhere or implicitly

    public class NativeIntArithmetic
    {
        // Should trigger SP0004 as it appears pure but lacks attribute
        public nint Add(nint a, nint b)
        {
            return a + b;
        }

        // Should trigger SP0004
        public nint Multiply(nint a, nint b)
        {
            return a * b;
        }
    }";
            var test = CreateTestWithAttribute(testCode);
            var expectedAdd = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(17, 21, 17, 24).WithArguments("Add");
            var expectedMultiply = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(23, 21, 23, 29).WithArguments("Multiply");
            await VerifyCS.VerifyAnalyzerAsync(test, expectedAdd, expectedMultiply);
        }


        [Test]
        public async Task UnsignedNativeInt_PureMethod_ReportsMissingAttributeDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;

    public class UnsignedNativeIntArithmetic
    {
        // Should trigger SP0004
        public nuint Add(nuint a, nuint b)
        {
            return a + b;
        }

        // Should trigger SP0004
        public nuint Multiply(nuint a, nuint b)
        {
            return a * b;
        }
    }";
            var test = CreateTestWithAttribute(testCode);
            var expectedAdd = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(17, 22, 17, 25).WithArguments("Add");
            var expectedMultiply = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(23, 22, 23, 30).WithArguments("Multiply");
            await VerifyCS.VerifyAnalyzerAsync(test, expectedAdd, expectedMultiply);
        }


        [Test]
        public async Task NativeIntWithConversions_PureMethod_ReportsMissingAttributeDiagnostics()
        {
            var testCode = @"
    public class NativeIntConversions
    {
        // Casts were handled, expect no diagnostic for the method itself if pure
        // SP0004 expected because the method is pure but lacks the attribute.
        public int ConvertToInt(nint value)
        {
             return (int)value;
        }

        // SP0004 expected.
        public nint ConvertToNInt(int value)
        {
            return (nint)value;
        }

         // SP0004 expected.
         public uint ConvertToUInt(nuint value)
        {
             return (uint)value;
        }

        // SP0004 expected.
        public nuint ConvertToNUInt(uint value)
        {
            return (nuint)value;
        }
    }";
            var expectedToInt = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(16, 20, 16, 32).WithArguments("ConvertToInt");
            var expectedToNInt = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(22, 21, 22, 34).WithArguments("ConvertToNInt");
            var expectedToUInt = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(28, 22, 28, 35).WithArguments("ConvertToUInt");
            var expectedToNUInt = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(34, 22, 34, 36).WithArguments("ConvertToNUInt");
            await VerifyCS.VerifyAnalyzerAsync(CreateTestWithAttribute(testCode), expectedToInt, expectedToNInt, expectedToUInt, expectedToNUInt);
        }


        [Test]
        public async Task NativeIntWithComparisons_PureMethod_ReportsMissingAttributeDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;

    public class NativeIntComparer
    {
        // SP0004 expected
        public bool IsGreaterThan(nint a, nint b)
        {
            return a > b;
        }

        // SP0004 expected
        public bool IsLessThan(nint a, nint b)
        {
            return a < b;
        }

        // SP0004 expected
        public bool AreEqual(nint a, nint b)
        {
            return a == b;
        }

        // SP0004 expected (overload)
        public bool IsGreaterThan(nuint a, nuint b)
        {
            return a > b;
        }
    }";
            var test = CreateTestWithAttribute(testCode);
            var expectedGT = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(17, 21, 17, 34).WithArguments("IsGreaterThan");
            var expectedLT = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(23, 21, 23, 31).WithArguments("IsLessThan");
            var expectedEQ = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(29, 21, 29, 29).WithArguments("AreEqual");
            var expectedGTU = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(35, 21, 35, 34).WithArguments("IsGreaterThan");
            await VerifyCS.VerifyAnalyzerAsync(test, expectedGT, expectedLT, expectedEQ, expectedGTU);
        }


        [Test]
        public async Task NativeIntWithConstants_PureMethod_ReportsMissingAttributeDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;

    public class NativeIntConstants
    {
        // SP0004 expected
        public nint GetLargePositiveValue()
        {
            return (nint)1000000000;
        }

        // SP0004 expected
        public nint GetNegativeValue()
        {
            return (nint)(-1000000000);
        }

        // SP0004 expected
        public nuint GetLargeUnsignedValue()
        {
            return (nuint)4000000000;
        }

        // SP0004 expected
        public nuint GetZeroValue()
        {
            return (nuint)0;
        }
    }";
            var test = CreateTestWithAttribute(testCode);
            var expectedPos = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(17, 21, 17, 42).WithArguments("GetLargePositiveValue");
            var expectedNeg = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(23, 21, 23, 37).WithArguments("GetNegativeValue");
            var expectedUnsigned = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(29, 22, 29, 43).WithArguments("GetLargeUnsignedValue");
            var expectedZero = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(35, 22, 35, 34).WithArguments("GetZeroValue");
            await VerifyCS.VerifyAnalyzerAsync(test, expectedPos, expectedNeg, expectedUnsigned, expectedZero);
        }


        [Test]
        public async Task NativeIntWithBitwiseOperations_PureMethod_ReportsMissingAttributeDiagnostics()
        {
            var testCode = @"
using SharpProof.Attributes;

    public class NativeIntBitwise
    {
        // SP0004 expected
        public nint BitwiseAnd(nint a, nint b)
        {
            return a & b;
        }

        // SP0004 expected
        public nint BitwiseOr(nint a, nint b)
        {
            return a | b;
        }

        // SP0004 expected
        public nint BitwiseXor(nint a, nint b)
        {
            return a ^ b;
        }

        // SP0004 expected
        public nint BitwiseNot(nint a)
        {
            return ~a;
        }

        // SP0004 expected (overload)
        public nuint BitwiseAnd(nuint a, nuint b)
        {
            return a & b;
        }
    }";
            var test = CreateTestWithAttribute(testCode);
            var expectedAnd = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(17, 21, 17, 31).WithArguments("BitwiseAnd");
            var expectedOr = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(23, 21, 23, 30).WithArguments("BitwiseOr");
            var expectedXor = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(29, 21, 29, 31).WithArguments("BitwiseXor");
            var expectedNot = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(35, 21, 35, 31).WithArguments("BitwiseNot");
            var expectedAndUnsigned = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(41, 22, 41, 32).WithArguments("BitwiseAnd");

            await VerifyCS.VerifyAnalyzerAsync(CreateTestWithAttribute(testCode),
                                             expectedAnd, expectedOr, expectedXor, expectedNot, expectedAndUnsigned);
        }

        [Test]
        public async Task NativeIntWithLogging_ImpureMethod_Diagnostic()
        {
            var testCode = @"
using System.IO; // Needed for File
using SharpProof.Attributes;

namespace TestNamespace 
{
    public class NativeIntWithLogging
    {
        // The method identifier is where SP0002 should be reported
        [EnforcePure]
        public nint {|SP0002:AddAndLog|}(nint a, nint b) 
        {
            nint result = a + b; 
            File.AppendAllText(""log.txt"", $""{result}""); // File I/O is impure and triggers SP0002.
            return result;
        }
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task NativeIntImpureMethod_Diagnostic()
        {


            var testCode = @"
using SharpProof.Attributes;

namespace TestNamespace
{
    public class NativeIntImpureOperations
    {
        private static nint _state = 0;

        [EnforcePure]
        public nint {|SP0002:ImpureAdd|}(nint a)
        {
            // The compound assignment modifies state, making method impure.
            _state += a; 
            return _state;
        }
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }
    }
}
