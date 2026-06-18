using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using System.Text;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;
using PurelySharp.Attributes;
using System;

namespace PurelySharp.Test
{
    [TestFixture]
    public class StringOperationsTests
    {
        [Test]
        public async Task ComplexStringOperations_WithSplit_NoDiagnostic()
        {


            var test = @"
using System;
using PurelySharp.Attributes;
using System.Linq;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string input)
    {
        // Split is allowed here because the mutable array result is consumed locally.
        var words = input.Split(' ')
            .Where(w => !string.IsNullOrEmpty(w))
            .Select(w => w.Trim().ToLowerInvariant())
            .OrderBy(w => w.Length)
            .ThenBy(w => w, StringComparer.Ordinal);

        return string.Join("" "", words);
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringSplitReturnedArray_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string[] {|PS0002:TestMethod|}(string input)
    {
        return input.Split(' ');
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringStartsWithChar_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string input)
    {
        return input.StartsWith('a') && input.EndsWith('z');
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringStartsWithString_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(string input)
    {
        return input.StartsWith(""abc"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringIsNullOrWhiteSpace_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string input)
    {
        return string.IsNullOrWhiteSpace(input);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringLengthAndTrimHelpers_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string input)
    {
        return input.Length + input.Trim().Length + input.TrimStart().Length + input.TrimEnd().Length;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringEqualsStringComparisonOrdinal_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string input)
    {
        return input.Equals(""abc"", StringComparison.Ordinal);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringEqualsStringComparisonCurrentCulture_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(string input)
    {
        return input.Equals(""abc"", StringComparison.CurrentCulture);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringStaticEqualsStringComparisonOrdinal_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringStartsWithStringComparisonOrdinal_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string input)
    {
        return input.StartsWith(""abc"", StringComparison.Ordinal);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringStartsWithStringComparisonCurrentCulture_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(string input)
    {
        return input.StartsWith(""abc"", StringComparison.CurrentCulture);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringToCharArray_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public char[] TestMethod(string input)
    {
        return input.ToCharArray();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringToCharArray_LocalNonEscapingUse_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string input)
    {
        var chars = input.ToCharArray();
        return chars.Length;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringToCharArray_LocalReturned_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public char[] TestMethod(string input)
    {
        var chars = input.ToCharArray();
        return chars;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringCtorFromReadOnlySpan_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string input)
    {
        ReadOnlySpan<char> chars = input.AsSpan();
        return new string(chars);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringCtorFromReadOnlySpan_LocalNonEscapingUse_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string input)
    {
        ReadOnlySpan<char> chars = input.AsSpan();
        var copy = new string(chars);
        return copy.Length;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringCtorFromReadOnlySpan_LocalReturned_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string input)
    {
        ReadOnlySpan<char> chars = input.AsSpan();
        var copy = new string(chars);
        return copy;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringInterpolation_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int x, string y)
    {
        return $""Value: {x}, Text: {y.ToUpperInvariant()}"";
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringInterpolation_WithFormatSpecifier_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(decimal value)
    {
        return $""Amount: {value:C2}"";
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringInterpolation_WithAlignment_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(int value)
    {
        return $""Value: {value,10}"";
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task FormattableStringInvariant_WithFormatSpecifier_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(double value)
    {
        return FormattableString.Invariant($""{value,10:N2}"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task FormattableStringInvariant_WithImpureInterpolationExpression_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}()
    {
        return FormattableString.Invariant($""{Console.Read():N2}"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringBuilderOperations_Diagnostic()
        {

            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string input)
    {
        var sb = new StringBuilder();
        sb.Append(input);
        return sb.ToString();
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId)
                                    .WithSpan(9, 19, 9, 29)
                                    .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task StringFormatting_ImpureFormat_Diagnostic()
        {

            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int x, double y)
    {
        return string.Format(""X = {0:D}, Y = {1:F2}"", x, y);
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId)
                                    .WithSpan(8, 19, 8, 29)
                                    .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task StringFormatting_OneArgument_ImpureFormat_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int x)
    {
        return string.Format(""{0:D}"", x);
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId)
                                    .WithSpan(8, 19, 8, 29)
                                    .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task StringFormatting_ThreeArgument_ImpureFormat_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int x, int y, int z)
    {
        return string.Format(""{0} {1} {2}"", x, y, z);
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId)
                                    .WithSpan(8, 19, 8, 29)
                                    .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task StringFormatting_ParamsArray_ImpureFormat_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int a, int b, int c, int d)
    {
        return string.Format(""{0} {1} {2} {3}"", a, b, c, d);
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId)
                                    .WithSpan(8, 19, 8, 29)
                                    .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithStringBuilderAppend_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(StringBuilder sb)
    {
        sb.Append(""hello"");
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                  .WithSpan(9, 17, 9, 27)
                                  .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithStringBuilderAppend_OnLocal_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        var sb = new StringBuilder();
        sb.Append(""hello"");
        return sb.ToString();
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId)
                                    .WithSpan(9, 19, 9, 29)
                                    .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task PureMethodWithStringBuilderToString_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(StringBuilder sb)
    {
        return sb.ToString();
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodWithLocalStringBuilderToString_NoDiagnostic()
        {

            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        StringBuilder sb = new StringBuilder(""initial"");
        return sb.ToString();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringContains_Overloads_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool ContainsChar(string value, char needle)
    {
        return value.Contains(needle);
    }

    [EnforcePure]
    public bool ContainsStringWithComparison(string value, string search)
    {
        return value.Contains(search, StringComparison.Ordinal);
    }
}"

            ;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringContains_String_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string value, string search)
    {
        return value.Contains(search);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringContains_StringComparisonCurrentCulture_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(string value, string search)
    {
        return value.Contains(search, StringComparison.CurrentCulture);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
