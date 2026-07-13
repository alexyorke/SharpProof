using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class GlobalizationTests
{
    // NUnit supplies case-level concurrency; nested Roslyn concurrency for these
    // single-tree compilations only oversubscribes the test lane.
    private static readonly ImmutableArray<MetadataReference> GlobalizationFrameworkReferences =
        AnalyzerTestHost.GetMinimalFrameworkReferences();

    [Test]
    public async Task CultureInfo_InvariantCultureName_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        CultureInfo invariant = CultureInfo.InvariantCulture;
        return invariant.Name;
    }
}";
        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeParse_InvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(string dateStr)
    {
        return DateTime.Parse(dateStr, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeParse_InvariantCultureWithStyles_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(string dateStr)
    {
        return DateTime.Parse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(string dateStr)
    {
        return DateTime.Parse(dateStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeParse_SpanInvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateTime.Parse(span, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeParse_SpanInvariantCultureWithStyles_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateTime.Parse(span, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeParse_SpanCurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateTime.Parse(span);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeParseExact_InvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(string dateStr)
    {
        // DateTime exact parsing can normalize offset-bearing input to local time.
        return DateTime.ParseExact(dateStr, ""O"", CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeParseExact_WithStyles_InvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(string dateStr)
    {
        // DateTime exact parsing can normalize offset-bearing input to local time.
        return DateTime.ParseExact(dateStr, ""O"", CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeParseExact_MultipleFormats_InvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(string dateStr)
    {
        return DateTime.ParseExact(dateStr, new[] { ""O"", ""yyyy-MM-ddTHH:mm:ss"" }, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}(string dateStr)
    {
        return DateTimeOffset.Parse(dateStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetParse_InvariantCultureWithStyles_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}(string dateStr)
    {
        return DateTimeOffset.Parse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetParse_InvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}(string dateStr)
    {
        return DateTimeOffset.Parse(dateStr, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetParse_SpanInvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateTimeOffset.Parse(span, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetParse_SpanInvariantCultureWithStyles_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateTimeOffset.Parse(span, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetParse_SpanCurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateTimeOffset.Parse(span);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetParseExact_SingleRoundtripInvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset TestMethod(string dateStr)
    {
        return DateTimeOffset.ParseExact(dateStr, ""O"", CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetParseExact_WithNoneStylesInvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset TestMethod(string dateStr)
    {
        return DateTimeOffset.ParseExact(dateStr, ""O"", CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetParseExact_MultipleFormats_InvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}(string dateStr)
    {
        return DateTimeOffset.ParseExact(dateStr, new[] { ""O"", ""yyyy-MM-ddTHH:mm:sszzz"" }, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetParseExact_SpanSingleRoundtripInvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset TestMethod(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateTimeOffset.ParseExact(span, ""O"", CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetParseExact_SpanMultipleFormats_InvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateTimeOffset.ParseExact(span, new[] { ""O"", ""yyyy-MM-ddTHH:mm:sszzz"" }, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeSpanParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan {|SP0002:TestMethod|}(string value)
    {
        return TimeSpan.Parse(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeSpanParse_InvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan TestMethod(string value)
    {
        return TimeSpan.Parse(value, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeSpanParseExact_ConstantFormatInvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan TestMethod(string value)
    {
        return TimeSpan.ParseExact(value, ""c"", CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeSpanParseExact_MultipleFormatsInvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan {|SP0002:TestMethod|}(string value)
    {
        return TimeSpan.ParseExact(value, new[] { ""c"", ""g"" }, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeSpanParseExact_WithStylesInvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan TestMethod(string value)
    {
        return TimeSpan.ParseExact(value, ""c"", CultureInfo.InvariantCulture, TimeSpanStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeSpanParseExact_MultipleFormatsWithStyles_InvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan {|SP0002:TestMethod|}(string value)
    {
        return TimeSpan.ParseExact(value, new[] { ""c"", ""g"" }, CultureInfo.InvariantCulture, TimeSpanStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeSpanParseExact_SpanSingleFormatInvariantCultureWithStyles_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan TestMethod(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        return TimeSpan.ParseExact(span, ""c"", CultureInfo.InvariantCulture, TimeSpanStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeSpanParseExact_SpanMultipleFormatsWithStyles_InvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan {|SP0002:TestMethod|}(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        return TimeSpan.ParseExact(span, new[] { ""c"", ""g"" }, CultureInfo.InvariantCulture, TimeSpanStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    private static IEnumerable<TestCaseData> TryParseDiagnosticCases()
    {
        yield return TryParseDiagnosticCase(
            "TimeSpanTryParseExact_InvariantCulture_Diagnostic",
            @"        return TimeSpan.TryParseExact(input, ""c"", CultureInfo.InvariantCulture, out _);");
        yield return TryParseDiagnosticCase(
            "TimeSpanTryParseExact_MultipleFormatsInvariantCulture_Diagnostic",
            @"        return TimeSpan.TryParseExact(input, new[] { ""c"", ""g"" }, CultureInfo.InvariantCulture, out _);");
        yield return TryParseDiagnosticCase(
            "TimeSpanTryParseExact_WithStyles_InvariantCulture_Diagnostic",
            @"        return TimeSpan.TryParseExact(input, ""c"", CultureInfo.InvariantCulture, TimeSpanStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "TimeSpanTryParseExact_SpanWithStyles_InvariantCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return TimeSpan.TryParseExact(span, ""c"", CultureInfo.InvariantCulture, TimeSpanStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "TimeSpanTryParseExact_SpanInvariantCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return TimeSpan.TryParseExact(span, ""c"", CultureInfo.InvariantCulture, out _);");
        yield return TryParseDiagnosticCase(
            "TimeSpanTryParseExact_MultipleFormatsWithStyles_InvariantCulture_Diagnostic",
            @"        return TimeSpan.TryParseExact(input, new[] { ""c"", ""g"" }, CultureInfo.InvariantCulture, TimeSpanStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "TimeSpanTryParseExact_SpanMultipleFormatsWithStyles_InvariantCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return TimeSpan.TryParseExact(span, new[] { ""c"", ""g"" }, CultureInfo.InvariantCulture, TimeSpanStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "TimeSpanTryParseExact_SpanMultipleFormatsInvariantCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return TimeSpan.TryParseExact(span, new[] { ""c"", ""g"" }, CultureInfo.InvariantCulture, out _);");
        yield return TryParseDiagnosticCase(
            "DoubleTryParse_CurrentCulture_Diagnostic",
            @"        return double.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "DoubleTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return double.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "IntTryParse_CurrentCulture_Diagnostic",
            @"        return int.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "IntTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return int.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "DecimalTryParse_CurrentCulture_Diagnostic",
            @"        return decimal.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "DecimalTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return decimal.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "LongTryParse_CurrentCulture_Diagnostic",
            @"        return long.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "LongTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return long.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "ByteTryParse_CurrentCulture_Diagnostic",
            @"        return byte.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "ByteTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return byte.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "ShortTryParse_CurrentCulture_Diagnostic",
            @"        return short.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "ShortTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return short.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "FloatTryParse_CurrentCulture_Diagnostic",
            @"        return float.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "FloatTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return float.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "UShortTryParse_CurrentCulture_Diagnostic",
            @"        return ushort.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "UShortTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return ushort.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "UIntTryParse_CurrentCulture_Diagnostic",
            @"        return uint.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "UIntTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return uint.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "ULongTryParse_CurrentCulture_Diagnostic",
            @"        return ulong.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "ULongTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return ulong.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "SByteTryParse_CurrentCulture_Diagnostic",
            @"        return sbyte.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "SByteTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return sbyte.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "HalfTryParse_CurrentCulture_Diagnostic",
            @"        return Half.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "HalfTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return Half.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "BigIntegerTryParse_CurrentCulture_Diagnostic",
            @"        return BigInteger.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "BigIntegerTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return BigInteger.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeTryParse_CurrentCulture_Diagnostic",
            @"        return DateTime.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeTryParse_InvariantCulture_Diagnostic",
            @"        return DateTime.TryParse(input, CultureInfo.InvariantCulture, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeTryParse_InvariantCultureWithStyles_Diagnostic",
            @"        return DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateTime.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeTryParse_SpanInvariantCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateTime.TryParse(span, CultureInfo.InvariantCulture, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeTryParse_SpanInvariantCultureWithStyles_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateTime.TryParse(span, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeTryParseExact_InvariantCulture_Diagnostic",
            @"        return DateTime.TryParseExact(input, ""O"", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeTryParseExact_MultipleFormats_InvariantCulture_Diagnostic",
            @"        return DateTime.TryParseExact(input, new[] { ""O"", ""yyyy-MM-ddTHH:mm:ss"" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeTryParseExact_SpanSingleFormat_InvariantCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateTime.TryParseExact(span, ""O"", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeTryParseExact_SpanMultipleFormats_InvariantCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateTime.TryParseExact(span, new[] { ""O"", ""yyyy-MM-ddTHH:mm:ss"" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateOnlyTryParse_CurrentCulture_Diagnostic",
            @"        return DateOnly.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "DateOnlyTryParse_InvariantCulture_Diagnostic",
            @"        return DateOnly.TryParse(input, CultureInfo.InvariantCulture, out _);");
        yield return TryParseDiagnosticCase(
            "DateOnlyTryParse_InvariantCultureWithStyles_Diagnostic",
            @"        return DateOnly.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateOnlyTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateOnly.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "DateOnlyTryParse_SpanInvariantCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateOnly.TryParse(span, CultureInfo.InvariantCulture, out _);");
        yield return TryParseDiagnosticCase(
            "DateOnlyTryParse_SpanInvariantCultureWithStyles_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateOnly.TryParse(span, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateOnlyTryParseExact_CurrentCulture_Diagnostic",
            @"        return DateOnly.TryParseExact(input, ""d"", out _);");
        yield return TryParseDiagnosticCase(
            "DateOnlyTryParseExact_InvariantCultureWithStyles_Diagnostic",
            @"        return DateOnly.TryParseExact(input, ""d"", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateOnlyTryParseExact_MultipleFormatsInvariantCultureWithStyles_Diagnostic",
            @"        return DateOnly.TryParseExact(input, new[] { ""d"", ""yyyy-MM-dd"" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateOnlyTryParseExact_SpanSingleFormat_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateOnly.TryParseExact(span, ""d"", out _);");
        yield return TryParseDiagnosticCase(
            "DateOnlyTryParseExact_SpanSingleFormatInvariantCultureWithStyles_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateOnly.TryParseExact(span, ""d"", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateOnlyTryParseExact_MultipleFormats_CurrentCulture_Diagnostic",
            @"        return DateOnly.TryParseExact(input, new[] { ""d"", ""yyyy-MM-dd"" }, out _);");
        yield return TryParseDiagnosticCase(
            "DateOnlyTryParseExact_SpanMultipleFormats_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateOnly.TryParseExact(span, new[] { ""d"", ""yyyy-MM-dd"" }, out _);");
        yield return TryParseDiagnosticCase(
            "DateOnlyTryParseExact_SpanMultipleFormatsInvariantCultureWithStyles_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateOnly.TryParseExact(span, new[] { ""d"", ""yyyy-MM-dd"" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeOffsetTryParse_CurrentCulture_Diagnostic",
            @"        return DateTimeOffset.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeOffsetTryParse_InvariantCulture_Diagnostic",
            @"        return DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeOffsetTryParse_InvariantCultureWithStyles_Diagnostic",
            @"        return DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeOffsetTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateTimeOffset.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeOffsetTryParse_SpanInvariantCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateTimeOffset.TryParse(span, CultureInfo.InvariantCulture, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeOffsetTryParse_SpanInvariantCultureWithStyles_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateTimeOffset.TryParse(span, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeOffsetTryParseExact_InvariantCulture_Diagnostic",
            @"        return DateTimeOffset.TryParseExact(input, ""O"", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeOffsetTryParseExact_MultipleFormats_InvariantCulture_Diagnostic",
            @"        return DateTimeOffset.TryParseExact(input, new[] { ""O"", ""yyyy-MM-ddTHH:mm:sszzz"" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeOffsetTryParseExact_SpanSingleFormat_InvariantCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateTimeOffset.TryParseExact(span, ""O"", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "DateTimeOffsetTryParseExact_SpanMultipleFormats_InvariantCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return DateTimeOffset.TryParseExact(span, new[] { ""O"", ""yyyy-MM-ddTHH:mm:sszzz"" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "TimeSpanTryParse_CurrentCulture_Diagnostic",
            @"        return TimeSpan.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "TimeSpanTryParse_InvariantCulture_Diagnostic",
            @"        return TimeSpan.TryParse(input, CultureInfo.InvariantCulture, out _);");
        yield return TryParseDiagnosticCase(
            "TimeSpanTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return TimeSpan.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "TimeSpanTryParse_SpanInvariantCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return TimeSpan.TryParse(span, CultureInfo.InvariantCulture, out _);");
        yield return TryParseDiagnosticCase(
            "TimeOnlyTryParse_CurrentCulture_Diagnostic",
            @"        return TimeOnly.TryParse(input, out _);");
        yield return TryParseDiagnosticCase(
            "TimeOnlyTryParse_InvariantCulture_Diagnostic",
            @"        return TimeOnly.TryParse(input, CultureInfo.InvariantCulture, out _);");
        yield return TryParseDiagnosticCase(
            "TimeOnlyTryParse_InvariantCultureWithStyles_Diagnostic",
            @"        return TimeOnly.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "TimeOnlyTryParse_Span_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return TimeOnly.TryParse(span, out _);");
        yield return TryParseDiagnosticCase(
            "TimeOnlyTryParse_SpanInvariantCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return TimeOnly.TryParse(span, CultureInfo.InvariantCulture, out _);");
        yield return TryParseDiagnosticCase(
            "TimeOnlyTryParse_SpanInvariantCultureWithStyles_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return TimeOnly.TryParse(span, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "TimeOnlyTryParseExact_CurrentCulture_Diagnostic",
            @"        return TimeOnly.TryParseExact(input, ""t"", out _);");
        yield return TryParseDiagnosticCase(
            "TimeOnlyTryParseExact_InvariantCultureWithStyles_Diagnostic",
            @"        return TimeOnly.TryParseExact(input, ""t"", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "TimeOnlyTryParseExact_MultipleFormatsInvariantCultureWithStyles_Diagnostic",
            @"        return TimeOnly.TryParseExact(input, new[] { ""t"", ""HH:mm:ss"" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "TimeOnlyTryParseExact_SpanSingleFormat_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return TimeOnly.TryParseExact(span, ""t"", out _);");
        yield return TryParseDiagnosticCase(
            "TimeOnlyTryParseExact_SpanSingleFormatInvariantCultureWithStyles_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return TimeOnly.TryParseExact(span, ""t"", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
        yield return TryParseDiagnosticCase(
            "TimeOnlyTryParseExact_MultipleFormats_CurrentCulture_Diagnostic",
            @"        return TimeOnly.TryParseExact(input, new[] { ""t"", ""HH:mm:ss"" }, out _);");
        yield return TryParseDiagnosticCase(
            "TimeOnlyTryParseExact_SpanMultipleFormats_CurrentCulture_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return TimeOnly.TryParseExact(span, new[] { ""t"", ""HH:mm:ss"" }, out _);");
        yield return TryParseDiagnosticCase(
            "TimeOnlyTryParseExact_SpanMultipleFormatsInvariantCultureWithStyles_Diagnostic",
            @"        ReadOnlySpan<char> span = input.AsSpan();
        return TimeOnly.TryParseExact(span, new[] { ""t"", ""HH:mm:ss"" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);");
    }

    private static TestCaseData TryParseDiagnosticCase(string name, string methodBody) =>
        new TestCaseData(methodBody).SetName(name);

    [TestCaseSource(nameof(TryParseDiagnosticCases))]
    public async Task TryParseOverload_Diagnostic(string methodBody)
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using System.Numerics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(string input)
    {
" + methodBody + @"
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }















    [Test]
    public async Task DoubleParse_InvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public double TestMethod(string numStr)
    {
        // Pure: Explicitly uses InvariantCulture
        return double.Parse(numStr, CultureInfo.InvariantCulture);
    }
}";


        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DoubleParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public double {|SP0002:TestMethod|}(string numStr)
    {
        return double.Parse(numStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }





    [Test]
    public async Task DoubleToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(double value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DoubleToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(double value)
    {
        return value.ToString(""N"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task FloatToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(float value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task FloatToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(float value)
    {
        return value.ToString(""N"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task IntToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(int value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task IntToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(int value)
    {
        return value.ToString(""N"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task LongToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(long value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task LongToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(long value)
    {
        return value.ToString(""N"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ShortToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(short value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ShortToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(short value)
    {
        return value.ToString(""N"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ByteToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(byte value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ByteToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(byte value)
    {
        return value.ToString(""N"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task SByteToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(sbyte value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task SByteToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(sbyte value)
    {
        return value.ToString(""N"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task UShortToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(ushort value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task UShortToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(ushort value)
    {
        return value.ToString(""N"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task UIntToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(uint value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task UIntToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(uint value)
    {
        return value.ToString(""N"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ULongToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(ulong value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ULongToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(ulong value)
    {
        return value.ToString(""N"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task HalfToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(Half value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task HalfToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(Half value)
    {
        return value.ToString(""G"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DecimalToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(decimal value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DecimalToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(decimal value)
    {
        return value.ToString(""N"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeSpanToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(TimeSpan value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task IntParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(string numStr)
    {
        return int.Parse(numStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }





    [Test]
    public async Task LongParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}(string numStr)
    {
        return long.Parse(numStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task BigIntegerParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System.Numerics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public BigInteger {|SP0002:TestMethod|}(string numStr)
    {
        return BigInteger.Parse(numStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ByteParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte {|SP0002:TestMethod|}(string numStr)
    {
        return byte.Parse(numStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task HalfParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Half {|SP0002:TestMethod|}(string numStr)
    {
        return Half.Parse(numStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DecimalParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public decimal {|SP0002:TestMethod|}(string numStr)
    {
        return decimal.Parse(numStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }













    [Test]
    public async Task ConvertToSingle_Object_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public float {|SP0002:TestMethod|}(object value)
    {
        return Convert.ToSingle(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToSingle_String_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public float {|SP0002:TestMethod|}(string value)
    {
        return Convert.ToSingle(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToSingle_String_UsesCurrentCultureSemanticSource()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public float TestMethod(string value)
    {
        return Convert.ToSingle(value);
    }
}",
            frameworkReferences: GlobalizationFrameworkReferences,
            concurrentAnalysis: false);

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityRuleProperty],
            Is.EqualTo("MethodInvocationPurityRule"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.EqualTo("current_culture_semantic_rule"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty],
            Does.Contain("System.Convert.ToSingle"));
    }

    [Test]
    public async Task ConvertToDouble_Object_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public double {|SP0002:TestMethod|}(object value)
    {
        return Convert.ToDouble(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToDouble_String_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public double {|SP0002:TestMethod|}(string value)
    {
        return Convert.ToDouble(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToDouble_String_UsesCurrentCultureSemanticSource()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public double TestMethod(string value)
    {
        return Convert.ToDouble(value);
    }
}",
            frameworkReferences: GlobalizationFrameworkReferences,
            concurrentAnalysis: false);

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityRuleProperty],
            Is.EqualTo("MethodInvocationPurityRule"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.EqualTo("current_culture_semantic_rule"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty],
            Does.Contain("System.Convert.ToDouble"));
    }

    [Test]
    public async Task ConvertToDecimal_Object_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public decimal {|SP0002:TestMethod|}(object value)
    {
        return Convert.ToDecimal(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToDecimal_String_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public decimal {|SP0002:TestMethod|}(string value)
    {
        return Convert.ToDecimal(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToByte_Object_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte {|SP0002:TestMethod|}(object value)
    {
        return Convert.ToByte(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToByte_String_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte {|SP0002:TestMethod|}(string value)
    {
        return Convert.ToByte(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToDateTime_Object_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(object value)
    {
        return Convert.ToDateTime(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToDateTime_String_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(string value)
    {
        return Convert.ToDateTime(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToDateTime_String_UsesCurrentCultureSemanticSource()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync("""
                                                                     #nullable enable
                                                                     using System;
                                                                     using SharpProof.Attributes;

                                                                     public class TestClass
                                                                     {
                                                                         [EnforcePure]
                                                                         public DateTime TestMethod(string value)
                                                                         {
                                                                             return Convert.ToDateTime(value);
                                                                         }
                                                                     }
                                                                     """,
            frameworkReferences: GlobalizationFrameworkReferences,
            concurrentAnalysis: false);

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityRuleProperty],
            Is.EqualTo("MethodInvocationPurityRule"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.EqualTo("current_culture_semantic_rule"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty],
            Does.Contain("System.Convert.ToDateTime"));
    }

    [Test]
    public async Task ConvertToString_Object_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|SP0002:TestMethod|}(object value)
    {
        return Convert.ToString(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToSByte_Object_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public sbyte {|SP0002:TestMethod|}(object value)
    {
        return Convert.ToSByte(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToSByte_String_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public sbyte {|SP0002:TestMethod|}(string value)
    {
        return Convert.ToSByte(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToInt32_Object_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(object value)
    {
        return Convert.ToInt32(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToInt32_String_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(string value)
    {
        return Convert.ToInt32(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToInt64_Object_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}(object value)
    {
        return Convert.ToInt64(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToInt64_String_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}(string value)
    {
        return Convert.ToInt64(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToInt16_Object_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public short {|SP0002:TestMethod|}(object value)
    {
        return Convert.ToInt16(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToInt16_String_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public short {|SP0002:TestMethod|}(string value)
    {
        return Convert.ToInt16(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToUInt16_Object_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ushort {|SP0002:TestMethod|}(object value)
    {
        return Convert.ToUInt16(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToUInt16_String_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ushort {|SP0002:TestMethod|}(string value)
    {
        return Convert.ToUInt16(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToUInt32_Object_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public uint {|SP0002:TestMethod|}(object value)
    {
        return Convert.ToUInt32(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToUInt32_String_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public uint {|SP0002:TestMethod|}(string value)
    {
        return Convert.ToUInt32(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToUInt64_Object_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ulong {|SP0002:TestMethod|}(object value)
    {
        return Convert.ToUInt64(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConvertToUInt64_String_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ulong {|SP0002:TestMethod|}(string value)
    {
        return Convert.ToUInt64(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    [TestCase("byte", "Convert.ToByte(value)", "System.Convert.ToByte")]
    [TestCase("decimal", "Convert.ToDecimal(value)", "System.Convert.ToDecimal")]
    [TestCase("short", "Convert.ToInt16(value)", "System.Convert.ToInt16")]
    [TestCase("int", "Convert.ToInt32(value)", "System.Convert.ToInt32")]
    [TestCase("long", "Convert.ToInt64(value)", "System.Convert.ToInt64")]
    [TestCase("sbyte", "Convert.ToSByte(value)", "System.Convert.ToSByte")]
    [TestCase("ushort", "Convert.ToUInt16(value)", "System.Convert.ToUInt16")]
    [TestCase("uint", "Convert.ToUInt32(value)", "System.Convert.ToUInt32")]
    [TestCase("ulong", "Convert.ToUInt64(value)", "System.Convert.ToUInt64")]
    public async Task ConvertNumericString_UsesCurrentCultureSemanticSource(
        string returnType,
        string expression,
        string expectedSymbol)
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync($$"""
                                                                       #nullable enable
                                                                       using System;
                                                                       using SharpProof.Attributes;

                                                                       public class TestClass
                                                                       {
                                                                           [EnforcePure]
                                                                           public {{returnType}} TestMethod(string value)
                                                                           {
                                                                               return {{expression}};
                                                                           }
                                                                       }
                                                                       """,
            frameworkReferences: GlobalizationFrameworkReferences,
            concurrentAnalysis: false);

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityRuleProperty],
            Is.EqualTo("MethodInvocationPurityRule"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.EqualTo("current_culture_semantic_rule"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty], Does.Contain(expectedSymbol));
    }

    [Test]
    public async Task FloatParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public float {|SP0002:TestMethod|}(string numStr)
    {
        return float.Parse(numStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task ShortParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public short {|SP0002:TestMethod|}(string numStr)
    {
        return short.Parse(numStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }









    [Test]
    public async Task UShortParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ushort {|SP0002:TestMethod|}(string numStr)
    {
        return ushort.Parse(numStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }





    [Test]
    public async Task UIntParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public uint {|SP0002:TestMethod|}(string numStr)
    {
        return uint.Parse(numStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }





    [Test]
    public async Task ULongParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ulong {|SP0002:TestMethod|}(string numStr)
    {
        return ulong.Parse(numStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }





    [Test]
    public async Task SByteParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public sbyte {|SP0002:TestMethod|}(string numStr)
    {
        return sbyte.Parse(numStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }













    [Test]
    public async Task DateTimeParseExact_SpanSingleFormat_InvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        // DateTime exact parsing can normalize offset-bearing input to local time.
        return DateTime.ParseExact(span, ""O"", CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeParseExact_SpanMultipleFormats_InvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateTime.ParseExact(span, new[] { ""O"", ""yyyy-MM-ddTHH:mm:ss"" }, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }





















    [Test]
    public async Task DateTimeToLongDateString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DateTime value)
    {
        return value.ToLongDateString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeToLongTimeString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DateTime value)
    {
        return value.ToLongTimeString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeToShortDateString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DateTime value)
    {
        return value.ToShortDateString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeToShortTimeString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DateTime value)
    {
        return value.ToShortTimeString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DateTime value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DateTime value)
    {
        return value.ToString(""g"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly {|SP0002:TestMethod|}(string dateStr)
    {
        return DateOnly.Parse(dateStr);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParse_InvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly TestMethod(string dateStr)
    {
        return DateOnly.Parse(dateStr, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParse_InvariantCultureWithNoneStyles_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly TestMethod(string dateStr)
    {
        return DateOnly.Parse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParse_SpanInvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly TestMethod(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateOnly.Parse(span, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParse_SpanInvariantCultureWithNoneStyles_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly TestMethod(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateOnly.Parse(span, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParse_SpanCurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly {|SP0002:TestMethod|}(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateOnly.Parse(span);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParseExact_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly {|SP0002:TestMethod|}(string dateStr)
    {
        return DateOnly.ParseExact(dateStr, ""d"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParseExact_WithNoneStylesInvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly TestMethod(string dateStr)
    {
        return DateOnly.ParseExact(dateStr, ""d"", CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParseExact_SingleFormatInvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly TestMethod(string dateStr)
    {
        return DateOnly.ParseExact(dateStr, ""d"", CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParseExact_MultipleFormatsInvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly {|SP0002:TestMethod|}(string dateStr)
    {
        return DateOnly.ParseExact(dateStr, new[] { ""d"", ""yyyy-MM-dd"" }, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParseExact_MultipleFormatsInvariantCultureWithStyles_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly {|SP0002:TestMethod|}(string dateStr)
    {
        return DateOnly.ParseExact(dateStr, new[] { ""d"", ""yyyy-MM-dd"" }, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParseExact_MultipleFormats_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly {|SP0002:TestMethod|}(string dateStr)
    {
        return DateOnly.ParseExact(dateStr, new[] { ""d"", ""yyyy-MM-dd"" });
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParseExact_SpanSingleFormat_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly {|SP0002:TestMethod|}(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateOnly.ParseExact(span, ""d"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParseExact_SpanSingleFormatInvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly TestMethod(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateOnly.ParseExact(span, ""d"", CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyParseExact_SpanMultipleFormats_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly {|SP0002:TestMethod|}(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateOnly.ParseExact(span, new[] { ""d"", ""yyyy-MM-dd"" });
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }



























    [Test]
    public async Task DateOnlyParseExact_SpanMultipleFormatsInvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly {|SP0002:TestMethod|}(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateOnly.ParseExact(span, new[] { ""d"", ""yyyy-MM-dd"" }, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }



    [Test]
    public async Task DateOnlyParseExact_SpanMultipleFormatsInvariantCultureWithStyles_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly {|SP0002:TestMethod|}(string dateStr)
    {
        ReadOnlySpan<char> span = dateStr.AsSpan();
        return DateOnly.ParseExact(span, new[] { ""d"", ""yyyy-MM-dd"" }, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DateOnly value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyToLongDateString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DateOnly value)
    {
        return value.ToLongDateString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyToShortDateString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DateOnly value)
    {
        return value.ToShortDateString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateOnlyToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DateOnly value)
    {
        return value.ToString(""d"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }





















    [Test]
    public async Task DateTimeOffsetToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DateTimeOffset value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DateTimeOffset value)
    {
        return value.ToString(""g"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }









    [Test]
    public async Task TimeSpanParse_SpanInvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan TestMethod(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        return TimeSpan.Parse(span, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeSpanParse_SpanCurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan {|SP0002:TestMethod|}(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        return TimeSpan.Parse(span);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParse_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly {|SP0002:TestMethod|}(string value)
    {
        return TimeOnly.Parse(value);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParse_InvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly TestMethod(string value)
    {
        return TimeOnly.Parse(value, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParse_InvariantCultureWithNoneStyles_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly TestMethod(string value)
    {
        return TimeOnly.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParse_SpanInvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly TestMethod(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        return TimeOnly.Parse(span, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParse_SpanInvariantCultureWithNoneStyles_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly TestMethod(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        return TimeOnly.Parse(span, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParse_SpanCurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly {|SP0002:TestMethod|}(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        return TimeOnly.Parse(span);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParseExact_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly {|SP0002:TestMethod|}(string value)
    {
        return TimeOnly.ParseExact(value, ""t"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParseExact_WithNoneStylesInvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly TestMethod(string value)
    {
        return TimeOnly.ParseExact(value, ""t"", CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParseExact_SingleFormatInvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly TestMethod(string value)
    {
        return TimeOnly.ParseExact(value, ""t"", CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParseExact_MultipleFormatsInvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly {|SP0002:TestMethod|}(string value)
    {
        return TimeOnly.ParseExact(value, new[] { ""t"", ""HH:mm:ss"" }, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParseExact_MultipleFormatsInvariantCultureWithStyles_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly {|SP0002:TestMethod|}(string value)
    {
        return TimeOnly.ParseExact(value, new[] { ""t"", ""HH:mm:ss"" }, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParseExact_MultipleFormats_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly {|SP0002:TestMethod|}(string value)
    {
        return TimeOnly.ParseExact(value, new[] { ""t"", ""HH:mm:ss"" });
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParseExact_SpanSingleFormat_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly {|SP0002:TestMethod|}(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        return TimeOnly.ParseExact(span, ""t"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParseExact_SpanSingleFormatInvariantCulture_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly TestMethod(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        return TimeOnly.ParseExact(span, ""t"", CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParseExact_SpanMultipleFormats_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly {|SP0002:TestMethod|}(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        return TimeOnly.ParseExact(span, new[] { ""t"", ""HH:mm:ss"" });
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParseExact_SpanMultipleFormatsInvariantCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly {|SP0002:TestMethod|}(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        return TimeOnly.ParseExact(span, new[] { ""t"", ""HH:mm:ss"" }, CultureInfo.InvariantCulture);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyToString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(TimeOnly value)
    {
        return value.ToString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyParseExact_SpanMultipleFormatsInvariantCultureWithStyles_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeOnly {|SP0002:TestMethod|}(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        return TimeOnly.ParseExact(span, new[] { ""t"", ""HH:mm:ss"" }, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyToLongTimeString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(TimeOnly value)
    {
        return value.ToLongTimeString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyToString_FormatString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(TimeOnly value)
    {
        return value.ToString(""t"");
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }

    [Test]
    public async Task TimeOnlyToShortTimeString_CurrentCulture_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(TimeOnly value)
    {
        return value.ToShortTimeString();
    }
}";

        await AssertGlobalizationDiagnosticsAsync(test);
    }





























    private static async Task AssertGlobalizationDiagnosticsAsync(string markedSource)
    {
        await AnalyzerTestHost.AssertOptionalSingleSp0002Async(
            markedSource,
            GlobalizationFrameworkReferences,
            false);
    }
}