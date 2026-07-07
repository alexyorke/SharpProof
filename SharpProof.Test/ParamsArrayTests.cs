using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;
using SharpProof.Attributes;
using System;
using System.Linq;

namespace SharpProof.Test
{
    [TestFixture]
    [Parallelizable(ParallelScope.Children)]
    public class ParamsArrayTests
    {
        [Test]
        public async Task PureMethodWithParamsArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Sum(params int[] numbers)
    {
        int total = 0;
        foreach (var num in numbers)
        {
            total += num;
        }
        return total;
    }
}";

            await AssertNoAnalyzerDiagnosticsAsync(test);
        }

        [Test]
        public async Task PureMethodWithParamsArrayCalledWithMultipleArguments_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Sum(params int[] numbers)
    {
        int total = 0;
        foreach (var num in numbers)
        {
            total += num;
        }
        return total;
    }

    [EnforcePure]
    public int TestMethod()
    {
        return Sum(1, 2, 3, 4, 5);
    }
}";
            await AssertNoAnalyzerDiagnosticsAsync(test);
        }

        [Test]
        public async Task PureMethodWithParamsArrayCalledWithFreshLocalArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Sum(params int[] numbers)
    {
        int total = 0;
        foreach (var num in numbers)
        {
            total += num;
        }
        return total;
    }

    [EnforcePure]
    public int TestMethod()
    {
        int[] myArray = new int[] { 1, 2, 3, 4, 5 };
        return Sum(myArray);
    }
}";
            await AssertNoAnalyzerDiagnosticsAsync(test);
        }

        [Test]
        public async Task PureMethodWithParamsArrayCalledWithNoArguments_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Sum(params int[] numbers)
    {
        int total = 0;
        foreach (var num in numbers)
        {
            total += num;
        }
        return total;
    }

    [EnforcePure]
    public int TestMethod()
    {
        return Sum();
    }
}";

            await AssertNoAnalyzerDiagnosticsAsync(test);
        }

        [Test]
        public async Task PureMethodWithParamsArrayOfReferenceType_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Linq;

public class TestClass
{
    [EnforcePure]
    public string Concatenate(params string[] strings)
    {
        return string.Join("" "", strings);
    }

    [EnforcePure]
    public string TestMethod()
    {
        return Concatenate(""Hello"", ""World"", ""!"");
    }
}";

            await AssertNoAnalyzerDiagnosticsAsync(test);
        }

        [Test]
        public async Task PureMethodWithParamsArrayAndRegularParameters_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string FormatMessage(string prefix, params object[] args)
    {
        string result = prefix;
        foreach (var arg in args)
        {
            result += "" "" + arg?.ToString();
        }
        return result;
    }

    [EnforcePure]
    public string TestMethod()
    {
        return FormatMessage(""Info: "", 1, ""text"", true);
    }
}";

            var expectedFM = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                   .WithSpan(8, 19, 8, 32)
                                   .WithArguments("FormatMessage");
            var expectedTM = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                   .WithSpan(19, 19, 19, 29)
                                   .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedFM, expectedTM);
        }

        [Test]
        public async Task PureMethodWithParamsArrayCopyingIntoFreshLocalArray_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] {|SP0002:ProcessArray|}(params int[] numbers)
    {
        int[] result = new int[numbers.Length];
        for (int i = 0; i < numbers.Length; i++)
        {
            result[i] = numbers[i] * 2;
        }
        return result;
    }
}";

            await AssertSinglePurityDiagnosticAsync(test);
        }

        [Test]
        public async Task PureMethodModifyingParamsArray_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] {|SP0002:ProcessArray|}(params int[] numbers)
    {
        for (int i = 0; i < numbers.Length; i++)
        {
            numbers[i] = numbers[i] * 2;
        }
        return numbers;
    }
}";

            await AssertSinglePurityDiagnosticAsync(test);
        }

        [Test]
        public async Task ParamsWithImpureDelegate_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public delegate void ProcessAction(int number);

public class TestClass
{
    [EnforcePure]
    public static void ImpureAction(int n) => Console.WriteLine(n);

    [EnforcePure]
    public static void ProcessNumbers(ProcessAction processor, params int[] numbers)
    {
        foreach (var number in numbers)
        {
            processor(number); // Impure call via delegate
        }
    }

    [EnforcePure]
    public static void TestMethod()
    {
        ProcessNumbers(ImpureAction, 1, 2, 3);
    }
}
";


            var expectedDiagSP0002_Process = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(10, 24, 10, 36).WithArguments("ImpureAction");


            var expectedDiagSP0002_TestMethod = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(21, 24, 21, 34).WithArguments("TestMethod");


            var expectedDiagSP0002_ImpureAction = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(9, 24, 9, 36).WithArguments("ImpureAction");


            var expectedImpureAction = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(10, 24, 10, 36).WithArguments("ImpureAction");
            var expectedProcessNumbers = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(13, 24, 13, 38).WithArguments("ProcessNumbers");
            var expectedTestMethod = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(22, 24, 22, 34).WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(testCode, expectedImpureAction, expectedProcessNumbers, expectedTestMethod);
        }

        private static async Task AssertNoAnalyzerDiagnosticsAsync(string source)
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source, concurrentAnalysis: true);
            Assert.That(diagnostics, Is.Empty);
        }

        private static async Task AssertSinglePurityDiagnosticAsync(string markedSource)
        {
            var (source, expectedSpanText) = StripSp0002Markup(markedSource);
            Assert.That(expectedSpanText, Is.Not.Null);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source, concurrentAnalysis: true);
            var purityDiagnostics = diagnostics
                .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId)
                .ToArray();

            Assert.That(purityDiagnostics, Has.Length.EqualTo(1));
            Assert.That(diagnostics, Has.Length.EqualTo(1));

            var diagnostic = purityDiagnostics[0];
            var actualSpanText = source.Substring(
                diagnostic.Location.SourceSpan.Start,
                diagnostic.Location.SourceSpan.Length);
            Assert.That(actualSpanText, Is.EqualTo(expectedSpanText));
        }

        private static (string Source, string? ExpectedSpanText) StripSp0002Markup(string markedSource)
        {
            const string prefix = "{|SP0002:";
            const string suffix = "|}";
            var start = markedSource.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return (markedSource, null);
            }

            var contentStart = start + prefix.Length;
            var end = markedSource.IndexOf(suffix, contentStart, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThanOrEqualTo(0), "Unterminated SP0002 markup.");

            var expectedSpanText = markedSource.Substring(contentStart, end - contentStart);
            var source = markedSource.Remove(end, suffix.Length).Remove(start, prefix.Length);
            return (source, expectedSpanText);
        }
    }
}
