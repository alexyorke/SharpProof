using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class MetricsTests
    {
        [Test]
        public async Task MeterCreateCounter_Diagnostic()
        {
            var test = @"
using System.Diagnostics.Metrics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Counter<int> {|SP0002:TestMethod|}(Meter meter)
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
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(Counter<int> counter)
    {
        counter.Add(1);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
