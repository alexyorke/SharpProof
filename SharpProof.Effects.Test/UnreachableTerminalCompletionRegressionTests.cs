namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class UnreachableTerminalCompletionRegressionTests
{
    [Test]
    public void DeadThrowAfterReturnDoesNotHideTheCallersSuffixEffect()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class Box {
                public int Value;
            }

            public static class Sample {
                private static int ReturnValue() {
                    return 1;
                    throw new InvalidOperationException();
                }

                public static void Run(Box suffix) {
                    _ = ReturnValue();
                    suffix.Value++;
                }
            }
            """);
        var returnValue = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "ReturnValue");
        var run = EffectTestHost.RequireMethod(compilation, "Sample", "Run");
        var completion = new DefiniteOperationFacts(
            compilation,
            CancellationToken.None);
        var summary = new EffectAnalysisSession(compilation)
            .Analyze(run)
            .Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                completion.MethodCanCompleteNormally(returnValue),
                Is.True,
                "the dead throw cannot veto the reachable return");
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True,
                "the returning call must retain its caller's suffix");
            Assert.That(
                summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }
}
