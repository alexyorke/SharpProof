using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class DateTimeTests
{
    public sealed record DateTimeOperationCase(string Name, string Source);

    private static readonly DateTimeOperationCase[] Cases =
    {
        new("DateTimeToday_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}()
    {
        return DateTime.Today;
    }
}"),
        new("DateTimeNow_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}()
    {
        return DateTime.Now;
    }
}"),
        new("DateTimeUtcNow_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}()
    {
        return DateTime.UtcNow;
    }
}"),
        new("DateTimeToString_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DateTime value)
    {
        return value.ToString();
    }
}"),
        new("DateTimeAddTicks_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime TestMethod(DateTime value)
    {
        return value.AddTicks(1);
    }
}"),
        new("DateTimeConstructorsAndIsLeapYear_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod()
    {
        var first = new DateTime(637000000000000000L);
        var second = new DateTime(2024, 2, 29);
        return first.Ticks + second.Ticks + (DateTime.IsLeapYear(2024) ? 1 : 0);
    }
}"),
        new("DateTimeStaticComparisonHelpers_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(DateTime left, DateTime right)
    {
        return DateTime.Compare(left, right) == 0 ||
            DateTime.Equals(left, right);
    }
}"),
        new("DateTimeEqualsObject_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(DateTime value)
    {
        return value.Equals((object)value);
    }
}"),
        new("DateTimeSubtract_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan TestMethod(DateTime left, DateTime right)
    {
        return left.Subtract(right);
    }
}"),
        new("DateTimeToBinary_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod(DateTime value)
    {
        return value.ToBinary();
    }
}"),
        new("DateTimeToFileTime_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}(DateTime value)
    {
        return value.ToFileTime();
    }
}"),
        new("DateTimeToLocalTime_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(DateTime value)
    {
        return value.ToLocalTime();
    }
}"),
        new("DateTimeBinaryRoundTripHelpers_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(DateTime value)
    {
        return DateTime.FromBinary(value.ToBinary());
    }
}"),
        new("DateTimeDaysInMonth_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(DateTime value)
    {
        return DateTime.DaysInMonth(value.Year, value.Month);
    }
}"),
        new("DateTimeOADateRoundTrip_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public double TestMethod(DateTime value)
    {
        return DateTime.FromOADate(value.ToOADate()).ToOADate();
    }
}"),
        new("DateTimeDate_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod(DateTime value)
    {
        return value.Date.Ticks;
    }
}"),
        new("DateTimeOffsetNow_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}()
    {
        return DateTimeOffset.Now;
    }
}"),
        new("DateTimeOffsetUtcNow_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}()
    {
        return DateTimeOffset.UtcNow;
    }
}"),
        new("DateTimeOffsetToString_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DateTimeOffset value)
    {
        return value.ToString();
    }
}"),
        new("DateTimeOffsetLongAndOffsetConstructor_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod()
    {
        var value = new DateTimeOffset(637000000000000000L, TimeSpan.Zero);
        return value.Ticks + value.UtcTicks;
    }
}"),
        new("DateTimeOffsetToUnixTimeMilliseconds_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod(DateTimeOffset value)
    {
        return value.ToUnixTimeMilliseconds();
    }
}"),
        new("DateTimeOffsetToUnixTimeSeconds_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod(DateTimeOffset value)
    {
        return value.ToUnixTimeSeconds();
    }
}"),
        new("DateTimeOffsetFromUnixTimeSeconds_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}(long value)
    {
        return DateTimeOffset.FromUnixTimeSeconds(value);
    }
}"),
        new("DateTimeOffsetFromUnixTimeMilliseconds_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}(long value)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(value);
    }
}"),
        new("DateTimeOffsetDeterministicValueProperties_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod(DateTimeOffset value)
    {
        return value.Ticks +
            value.UtcTicks +
            value.Offset.Ticks +
            value.DateTime.Ticks +
            value.UtcDateTime.Ticks;
    }
}"),
        new("DateTimeOffsetComponentProperties_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(DateTimeOffset value)
    {
        return value.Year +
            value.Month +
            value.Day +
            value.DayOfYear +
            (int)value.DayOfWeek +
            value.Hour +
            value.Minute +
            value.Second +
            value.Millisecond;
    }
}"),
        new("DateTimeOffsetStaticComparisonHelpers_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(DateTimeOffset left, DateTimeOffset right)
    {
        return DateTimeOffset.Compare(left, right) == 0 ||
            DateTimeOffset.Equals(left, right);
    }
}"),
    };

    private static IEnumerable<TestCaseData> DateTimeOperationCaseData()
    {
        if (Cases.Length != 27 ||
            Cases.Select(static testCase => testCase.Name).Distinct(StringComparer.Ordinal).Count() != 27)
        {
            throw new InvalidOperationException("DateTimeTests case invariants failed.");
        }

        return Cases.Select(static testCase => new TestCaseData(testCase).SetName(testCase.Name));
    }

    [TestCaseSource(nameof(DateTimeOperationCaseData))]
    public async Task DateTimeOperationCaseCases(DateTimeOperationCase testCase)
    {
        await VerifyCS.VerifyAnalyzerAsync(testCase.Source);
    }






    [TestCase("value.Add(offset)")]
    [TestCase("value.AddDays(1)")]
    [TestCase("value.AddHours(1)")]
    [TestCase("value.AddMilliseconds(2)")]
    [TestCase("value.AddMinutes(3)")]
    [TestCase("value.AddSeconds(5)")]
    [TestCase("value.Subtract(offset)")]
    public async Task DateTimeDeterministicAddMethods_NoDiagnostic(string expression)
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime TestMethod(DateTime value, TimeSpan offset)
    {
        return " + expression + @";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    [TestCase("value.AddMonths(4)")]
    [TestCase("value.AddYears(6)")]
    public async Task DateTimeCalendarAddMethods_Diagnostic(string expression)
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(DateTime value)
    {
        return " + expression + @";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }















    [Test]
    [TestCase("value.Add(offset)")]
    [TestCase("value.AddDays(1)")]
    [TestCase("value.AddHours(2)")]
    [TestCase("value.AddMilliseconds(3)")]
    [TestCase("value.AddMinutes(4)")]
    [TestCase("value.AddSeconds(6)")]
    [TestCase("value.AddTicks(7)")]
    public async Task DateTimeOffsetDeterministicAddMethods_NoDiagnostic(string expression)
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset TestMethod(DateTimeOffset value, TimeSpan offset)
    {
        return " + expression + @";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    [TestCase("value.AddMonths(5)")]
    [TestCase("value.AddYears(8)")]
    public async Task DateTimeOffsetCalendarAddMethods_NoDiagnostic(string expression)
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset TestMethod(DateTimeOffset value)
    {
        return " + expression + @";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }








}
