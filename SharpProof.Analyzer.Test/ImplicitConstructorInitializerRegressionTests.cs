using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ImplicitConstructorInitializerRegressionTests
{
    [Test]
    public async Task InitializersAreModeledAndTheirExceptionsRemainVisible()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public sealed class Options {
                public int Retries = 3;
            }

            public static class Thrower {
                public static int Fail() =>
                    throw new InvalidOperationException();
            }

            public sealed class ThrowingOptions {
                public int Value = Thrower.Fail();
            }

            public static class Sample {
                [EnforcePure]
                public static int ReadOptions() => new Options().Retries;

                [DoesNotThrow]
                public static int ReadThrowingOptions() =>
                    new ThrowingOptions().Value;
            }
            """,
            "effects",
            ["SP0002", "SP0046"]);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0046");
        AnalyzerTestHost.AssertMessageContains(
            diagnostics.Single(),
            "ReadThrowingOptions");
    }
}
