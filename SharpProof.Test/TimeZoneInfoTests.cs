using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class TimeZoneInfoTests
{
    [Test]
    public async Task TimeZoneInfoLocal_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeZoneInfo {|SP0002:TestMethod|}()
    {
        return TimeZoneInfo.Local;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task TimeZoneInfoFindSystemTimeZoneById_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeZoneInfo {|SP0002:TestMethod|}()
    {
        return TimeZoneInfo.FindSystemTimeZoneById(""UTC"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task TimeZoneInfoClearCachedData_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        TimeZoneInfo.ClearCachedData();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task TimeZoneInfoConvertTime_DateTimeOffset_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|SP0002:TestMethod|}(DateTimeOffset value, TimeZoneInfo timeZone)
    {
        return TimeZoneInfo.ConvertTime(value, timeZone);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}