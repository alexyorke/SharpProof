using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class TimeProviderTests
    {
        [Test]
        public async Task TimeProviderSystemGetUtcNow_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset {|PS0002:TestMethod|}()
    {
        return TimeProvider.System.GetUtcNow();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TimeProviderSystem_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeProvider TestMethod()
    {
        return TimeProvider.System;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TimeProviderLocalTimeZone_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeZoneInfo {|PS0002:TestMethod|}(TimeProvider provider)
    {
        return provider.LocalTimeZone;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TimeProviderTimestampFrequency_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|PS0002:TestMethod|}(TimeProvider provider)
    {
        return provider.TimestampFrequency;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
