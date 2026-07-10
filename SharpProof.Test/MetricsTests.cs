using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using static SharpProof.Test.AnalyzerTestHost;

namespace SharpProof.Test;

[TestFixture]
public class MetricsTests
{
    private static readonly ImmutableArray<MetadataReference> MetricsFrameworkReferences =
        GetMinimalFrameworkReferences()
            .Add(MetadataReference.CreateFromFile(typeof(Meter).Assembly.Location));

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
        await AssertSingleSp0002Async(
            markedSource,
            MetricsFrameworkReferences,
            true);
    }
}