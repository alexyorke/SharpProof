using NUnit.Framework;

using System.Globalization;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ConditionallyElidedCallContractRegressionTests
{
    [Test]
    public async Task AllowedExceptionsSeesDivisionAfterElidedThrowArgument()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using System.Diagnostics;
            using SharpProof.Attributes;

            public static class Fixture {
                [Conditional("TRACE")]
                private static void Log(int value) { }

                [AllowedExceptions(typeof(ArgumentOutOfRangeException))]
                public static int Scale(int count) {
                    Log(count > 0 ? count : throw new ArgumentOutOfRangeException());
                    return 100 / count;
                }
            }
            """,
            "effects",
            ["SP0046"]);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0046");
        Assert.That(
            diagnostics.Single().GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("Scale"));
    }
}
