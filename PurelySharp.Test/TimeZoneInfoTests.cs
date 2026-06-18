using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class TimeZoneInfoTests
    {
        [Test]
        public async Task TimeZoneInfoLocal_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeZoneInfo {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeZoneInfo {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        TimeZoneInfo.ClearCachedData();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
