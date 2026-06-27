using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class MetricsTests
    {
        [Test]
        public async Task MeterCreateCounter_Diagnostic()
        {
            var test = @"
using System.Diagnostics.Metrics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Counter<int> {|PS0002:TestMethod|}(Meter meter)
    {
        return meter.CreateCounter<int>(""requests"", ""count"", ""Request count"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CounterAdd_Diagnostic()
        {
            var test = @"
using System.Diagnostics.Metrics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(Counter<int> counter)
    {
        counter.Add(1);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
