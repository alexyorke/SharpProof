using Microsoft.CodeAnalysis;
using System.Threading.Tasks;
using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;
using static SharpProof.Test.AnalyzerTestHost;

namespace SharpProof.Test
{
    [TestFixture]
    public class MetricsTests
    {
        private static readonly ImmutableArray<MetadataReference> MetricsFrameworkReferences =
            GetMinimalFrameworkReferences()
                .Add(MetadataReference.CreateFromFile(typeof(System.Diagnostics.Metrics.Meter).Assembly.Location));

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

            await AssertPurityDiagnosticAsync(test);
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

            await AssertPurityDiagnosticAsync(test);
        }

        private static async Task AssertPurityDiagnosticAsync(string markedSource)
        {
            var (source, expectedSpanText) = AnalyzerTestHost.StripRequiredSp0002Markup(markedSource);
            var diagnostics = await GetDiagnosticsAsync(
                source,
                frameworkReferences: MetricsFrameworkReferences,
                concurrentAnalysis: true);

            Assert.That(diagnostics, Has.Length.EqualTo(1));
            var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);
            Assert.That(
                source.Substring(
                    diagnostic.Location.SourceSpan.Start,
                    diagnostic.Location.SourceSpan.Length),
                Is.EqualTo(expectedSpanText));
        }
    }
}
