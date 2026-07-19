using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class StringOperationsTests
{
    public sealed record StringOperationCase(string Name, string Source);

    private static readonly StringOperationCase[] StringOperationCasesPart1 =
    {
        new("ComplexStringOperations_WithSplit_NoDiagnostic", @"
using System;
using SharpProof.Attributes;
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
}"),
        new("StringSplitReturnedArray_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string[] TestMethod(string input)
    {
        return input.Split(' ');
    }
}"),
        new("StringStartsWithChar_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string input)
    {
        return input.StartsWith('a') && input.EndsWith('z');
    }
}"),
        new("StringStartsWithString_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(string input)
    {
        return input.StartsWith(""abc"");
    }
}"),
        new("StringIsNullOrWhiteSpace_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string input)
    {
        return string.IsNullOrWhiteSpace(input);
    }
}"),
        new("CharConvertFromUtf32_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(int codePoint)
    {
        return char.ConvertFromUtf32(codePoint);
    }
}"),
        new("CharConvertToUtf32_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(char highSurrogate, char lowSurrogate)
    {
        return char.ConvertToUtf32(highSurrogate, lowSurrogate);
    }
}"),
        new("StringLengthAndTrimHelpers_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string input)
    {
        return input.Length + input.Trim().Length + input.TrimStart().Length + input.TrimEnd().Length;
    }
}"),
        new("StringEqualsStringComparisonOrdinal_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string input)
    {
        return input.Equals(""abc"", StringComparison.Ordinal);
    }
}"),
        new("StringEqualsStringComparisonCurrentCulture_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(string input)
    {
        return input.Equals(""abc"", StringComparison.CurrentCulture);
    }
}"),
        new("StringStaticEqualsStringComparisonOrdinal_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}"),
        new("StringInvariantCasingAndHashCode_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string input)
    {
        var lower = input.ToLowerInvariant();
        var upper = input.ToUpperInvariant();
        return lower.GetHashCode() + upper.Length;
    }
}"),
        new("StringConcatReplaceSubstring_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string input, string suffix, string[] parts)
    {
        var combined = string.Concat(input, suffix);
        var flattened = string.Concat(parts);
        var replaced = combined.Replace(suffix, flattened);
        var tail = replaced.Substring(1);
        return tail.Substring(0, 1);
    }
}"),
        new("StringCloneCompareToAndIndexOfChar_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string input, string other)
    {
        var clone = (string)input.Clone();
        var normalized = clone.ToString();
        var comparison = normalized.CompareTo(other);
        return normalized.IndexOf('a') + comparison;
    }
}"),
    };

    private static IEnumerable<TestCaseData> StringOperationCaseData()
    {
        var cases = StringOperationCasesPart1
            .Concat(StringOperationCasesPart2)
            .Concat(StringOperationCasesPart3)
            .ToArray();

        if (cases.Length != 41 ||
            cases.Select(static testCase => testCase.Name).Distinct(StringComparer.Ordinal).Count() != 41)
        {
            throw new InvalidOperationException("StringOperation case invariants failed.");
        }

        return cases.Select(static testCase => new TestCaseData(testCase).SetName(testCase.Name));
    }

    [TestCaseSource(nameof(StringOperationCaseData))]
    public async Task StringOperationCases(StringOperationCase testCase)
    {
        await VerifyCS.VerifyAnalyzerAsync(testCase.Source);
    }



























    private static readonly StringOperationCase[] StringOperationCasesPart2 =
    {
        new("StringIndexOfString_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(string input)
    {
        return input.IndexOf(""abc"");
    }
}"),
        new("StringIndexOfStringComparisonOrdinal_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string input)
    {
        return input.IndexOf(""abc"", StringComparison.OrdinalIgnoreCase);
    }
}"),
        new("StringIndexOfStringComparisonCurrentCulture_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(string input)
    {
        return input.IndexOf(""abc"", StringComparison.CurrentCulture);
    }
}"),
        new("StringInsertPadLeftAndRemoveRange_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string input)
    {
        var padded = input.PadLeft(5);
        var inserted = padded.Insert(1, ""-"");
        return inserted.Remove(0, 1);
    }
}"),
        new("StringRemoveSingleIndex_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(string input)
    {
        return input.Remove(1);
    }
}"),
        new("StringStartsWithStringComparisonOrdinal_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string input)
    {
        return input.StartsWith(""abc"", StringComparison.Ordinal);
    }
}"),
        new("StringStartsWithStringComparisonCurrentCulture_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(string input)
    {
        return input.StartsWith(""abc"", StringComparison.CurrentCulture);
    }
}"),
        new("StringToCharArray_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public char[] TestMethod(string input)
    {
        return input.ToCharArray();
    }
}"),
        new("StringToCharArray_LocalNonEscapingUse_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string input)
    {
        var chars = input.ToCharArray();
        return chars.Length;
    }
}"),
        new("StringToCharArray_LocalReturned_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public char[] TestMethod(string input)
    {
        var chars = input.ToCharArray();
        return chars;
    }
}"),
        new("StringCtorFromReadOnlySpan_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(string input)
    {
        ReadOnlySpan<char> chars = input.AsSpan();
        return new string(chars);
    }
}"),
        new("StringCtorFromReadOnlySpan_LocalNonEscapingUse_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(string input)
    {
        ReadOnlySpan<char> chars = input.AsSpan();
        var copy = new string(chars);
        return copy.Length;
    }
}"),
        new("StringCtorFromReadOnlySpan_LocalReturned_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(string input)
    {
        ReadOnlySpan<char> chars = input.AsSpan();
        var copy = new string(chars);
        return copy;
    }
}"),
        new("StringInterpolation_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int x, string y)
    {
        return $""Value: {x}, Text: {y.ToUpperInvariant()}"";
    }
}"),
    };



























    private static readonly StringOperationCase[] StringOperationCasesPart3 =
    {
        new("StringInterpolation_WithFormatSpecifier_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(decimal value)
    {
        return $""Amount: {value:C2}"";
    }
}"),
        new("StringInterpolation_WithAlignment_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(int value)
    {
        return $""Value: {value,10}"";
    }
}"),
        new("FormattableStringInvariant_WithFormatSpecifier_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(double value)
    {
        return FormattableString.Invariant($""{value,10:N2}"");
    }
}"),
        new("FormattableStringToString_WithInvariantCulture_NoDiagnostic", @"
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(double value)
    {
        return FormattableString.Invariant($""{value,10:N2}"").ToString(CultureInfo.InvariantCulture);
    }
}"),
        new("FormattableStringFormatProperty_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(FormattableString formatted)
    {
        return formatted.Format;
    }
}"),
        new("FormattableStringInvariant_WithImpureInterpolationExpression_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return FormattableString.Invariant($""{Console.Read():N2}"");
    }
}"),
        new("PureMethodWithStringBuilderToString_Diagnostic", @"
using System;
using SharpProof.Attributes;
using System.Text;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(StringBuilder sb)
    {
        return sb.ToString();
    }
}"),
        new("PureMethodWithStringBuilderLength_NoDiagnostic", @"
using System;
using SharpProof.Attributes;
using System.Text;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(StringBuilder sb)
    {
        return sb.Length;
    }
}"),
        new("PureMethodWithLocalStringBuilderToString_Diagnostic", @"
using System;
using SharpProof.Attributes;
using System.Text;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        StringBuilder sb = new StringBuilder(""initial"");
        return sb.ToString();
    }
}"),
        new("StringContains_Overloads_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

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
            ),
        new("StringContains_String_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string value, string search)
    {
        return value.Contains(search);
    }
}"),
        new("StringContains_StringComparisonCurrentCulture_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(string value, string search)
    {
        return value.Contains(search, StringComparison.CurrentCulture);
    }
}"),
        new("StringJoinWithImpureEnumerable_Diagnostic", @"
using System;
using System.Collections;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class ImpureEnumerable : IEnumerable<string>
{
    public IEnumerator<string> GetEnumerator()
    {
        _ = DateTime.Now;
        return ((IEnumerable<string>)Array.Empty<string>()).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(ImpureEnumerable values)
    {
        return string.Join("" "", values);
    }
}"),
    };











    [Test]
    public async Task StringBuilderOperations_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
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

        var expected = VerifyCS.Diagnostic("SP0002")
            .WithSpan(9, 19, 9, 29)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task StringFormatting_ImpureFormat_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int x, double y)
    {
        return string.Format(""X = {0:D}, Y = {1:F2}"", x, y);
    }
}";

        var expected = VerifyCS.Diagnostic("SP0002")
            .WithSpan(8, 19, 8, 29)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task StringFormatting_OneArgument_ImpureFormat_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int x)
    {
        return string.Format(""{0:D}"", x);
    }
}";

        var expected = VerifyCS.Diagnostic("SP0002")
            .WithSpan(8, 19, 8, 29)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task StringFormatting_ThreeArgument_ImpureFormat_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int x, int y, int z)
    {
        return string.Format(""{0} {1} {2}"", x, y, z);
    }
}";

        var expected = VerifyCS.Diagnostic("SP0002")
            .WithSpan(8, 19, 8, 29)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task StringFormatting_ParamsArray_ImpureFormat_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int a, int b, int c, int d)
    {
        return string.Format(""{0} {1} {2} {3}"", a, b, c, d);
    }
}";

        var expected = VerifyCS.Diagnostic("SP0002")
            .WithSpan(8, 19, 8, 29)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task MethodWithStringBuilderAppend_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Text;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(StringBuilder sb)
    {
        sb.Append(""hello"");
    }
}";

        var expected = VerifyCS.Diagnostic(AnalyzerDiagnosticCatalog.Get("PurityNotVerifiedRule"))
            .WithSpan(9, 17, 9, 27)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task MethodWithStringBuilderAppend_OnLocal_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
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

        var expected = VerifyCS.Diagnostic("SP0002")
            .WithSpan(9, 19, 9, 29)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }














}
