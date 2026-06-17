using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class DateTimeTests
    {
        [Test]
        public async Task DateTimeToday_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(DateTime value)
    {
        return value.ToString();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DateTimeAddTicks_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|PS0002:TestMethod|}(DateTime value)
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
        [TestCase("value.AddMonths(4)")]
        [TestCase("value.AddSeconds(5)")]
        [TestCase("value.AddYears(6)")]
        public async Task DateTimeDeterministicAddMethods_Diagnostic(string expression)
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|PS0002:TestMethod|}(DateTime value, TimeSpan offset)
    {
        return " + expression + @";
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DateTimeStaticComparisonHelpers_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

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
        public async Task DateTimeSubtract_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime TestMethod(DateTime value)
    {
        return value.ToBinary();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DateTimeBinaryRoundTripHelpers_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime {|PS0002:TestMethod|}(DateTime value)
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
using PurelySharp.Attributes;

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
        public async Task DateTimeOADateRoundTrip_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public double {|PS0002:TestMethod|}(DateTime value)
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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(DateTimeOffset value)
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
        [TestCase("value.AddMonths(5)")]
        [TestCase("value.AddSeconds(6)")]
        [TestCase("value.AddTicks(7)")]
        [TestCase("value.AddYears(8)")]
        public async Task DateTimeOffsetAddMethods_Diagnostic(string expression)
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|PS0002:TestMethod|}(DateTimeOffset value, TimeSpan offset)
    {
        return " + expression + @";
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DateTimeOffsetToUnixTimeMilliseconds_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|PS0002:TestMethod|}(long value)
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|PS0002:TestMethod|}(long value)
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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
}
