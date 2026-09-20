using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class LocalFunctionEffectTests
{
    [Test]
    public async Task LocalFunctionOwnedEffectAttributesAreAnalyzed()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int state;

                public static void Configure() {
                    [EnforcePure]
                    static int StaticPure() => state++;

                    [EnforcePure]
                    int CapturingPure() => state++;

                    [DoesNotThrow]
                    static int Divide(int value) => 10 / value;

                    [ZeroAllocations]
                    static object Allocate() => new object();
                }
            }
            """,
            "effects",
            ["SP0047"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EquivalentTo(["SP0047", "SP0047", "SP0047", "SP0047"]));
            AnalyzerTestHost.AssertMessageContains(
                diagnostics.Single(static diagnostic =>
                    diagnostic.Id == "SP0047" &&
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)
                        .Contains("StaticPure", StringComparison.Ordinal)),
                "StaticPure");
            AnalyzerTestHost.AssertMessageContains(
                diagnostics.Single(static diagnostic =>
                    diagnostic.Id == "SP0047" &&
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)
                        .Contains("CapturingPure", StringComparison.Ordinal)),
                "CapturingPure");
            Assert.That(
                diagnostics.Select(static diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)),
                Has.All.Contains("UnsupportedCallable"));
        }
    }
}
