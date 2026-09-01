using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class EffectContractAttributeDiagnosticsTests
{
    [Test]
    public async Task EveryMalformedAttributeIsDiagnosedExactlyOnce()
    {
        const string source =
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [EffectContract(SharpProofEffect.None, Complete = true)]
                [EffectContract((SharpProofEffect)(1L << 40), Complete = true)]
                [EffectContract(SharpProofEffect.None, Complete = true)]
                [EffectContract((SharpProofEffect)(1L << 41), Complete = true)]
                public static void Execute() {
                }
            }
            """;

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            source,
            mode: null,
            ["SP0024"],
            features: "contracts");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0024", "SP0024"]));
            Assert.That(
                diagnostics.Select(static diagnostic =>
                    diagnostic.Location.SourceSpan.Start),
                Is.EqualTo(new[]
                {
                    source.IndexOf(
                        "EffectContract((SharpProofEffect)(1L << 40)",
                        StringComparison.Ordinal),
                    source.IndexOf(
                        "EffectContract((SharpProofEffect)(1L << 41)",
                        StringComparison.Ordinal)
                }));
        }
    }
}
