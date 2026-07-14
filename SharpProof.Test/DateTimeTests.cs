using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class DateTimeTests
{
    [Test]
    public async Task DateTimeToday_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}()
    {
        return DateTime.Today;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeNow_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}()
    {
        return DateTime.Now;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeUtcNow_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}()
    {
        return DateTime.UtcNow;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeToString_Diagnostic()
    {
        var test = @"
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

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeAddTicks_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime TestMethod(DateTime value)
    {
        return value.AddTicks(1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
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
    public async Task DateTimeConstructorsAndIsLeapYear_NoDiagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeStaticComparisonHelpers_NoDiagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeEqualsObject_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(DateTime value)
    {
        return value.Equals((object)value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeSubtract_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan TestMethod(DateTime left, DateTime right)
    {
        return left.Subtract(right);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeToBinary_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod(DateTime value)
    {
        return value.ToBinary();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeToFileTime_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}(DateTime value)
    {
        return value.ToFileTime();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeToLocalTime_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(DateTime value)
    {
        return value.ToLocalTime();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeBinaryRoundTripHelpers_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|SP0002:TestMethod|}(DateTime value)
    {
        return DateTime.FromBinary(value.ToBinary());
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeDaysInMonth_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(DateTime value)
    {
        return DateTime.DaysInMonth(value.Year, value.Month);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeOADateRoundTrip_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public double TestMethod(DateTime value)
    {
        return DateTime.FromOADate(value.ToOADate()).ToOADate();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeDate_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod(DateTime value)
    {
        return value.Date.Ticks;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetNow_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}()
    {
        return DateTimeOffset.Now;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetUtcNow_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}()
    {
        return DateTimeOffset.UtcNow;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetToString_Diagnostic()
    {
        var test = @"
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

    [Test]
    public async Task DateTimeOffsetLongAndOffsetConstructor_NoDiagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetToUnixTimeMilliseconds_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod(DateTimeOffset value)
    {
        return value.ToUnixTimeMilliseconds();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetToUnixTimeSeconds_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod(DateTimeOffset value)
    {
        return value.ToUnixTimeSeconds();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetFromUnixTimeSeconds_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}(long value)
    {
        return DateTimeOffset.FromUnixTimeSeconds(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetFromUnixTimeMilliseconds_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}(long value)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetDeterministicValueProperties_NoDiagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetComponentProperties_NoDiagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DateTimeOffsetStaticComparisonHelpers_NoDiagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}