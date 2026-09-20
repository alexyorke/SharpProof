using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ContractAssumeEffectRegressionTests
{
    [Test]
    public async Task AssumedDivisorDischargesDoesNotThrow()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [DoesNotThrow]
                public static int Divide(int divisor) {
                    Contract.Assume(divisor != 0);
                    return 10 / divisor;
                }
            }
            """,
            "effects",
            ["SP0046"]);

        Assert.That(diagnostics, Is.Empty);
    }
}
