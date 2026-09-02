namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class InvocationEvaluationOrderRegressionTests
{
    [Test]
    public void NullReceiverDoesNotSuppressEarlierArgumentEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Target {
                public void Consume(int value) {
                }
            }

            public static class Sample {
                private static int state;

                public static void Invoke() {
                    Target target = null!;
                    target.Consume(Mutate());
                }

                private static int Mutate() {
                    state++;
                    return state;
                }
            }
            """);
        var method = EffectTestHost.SampleMethod(compilation, "Invoke");

        var summary = new EffectAnalysisSession(compilation)
            .Analyze(method)
            .Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "C# evaluates invocation arguments before the callvirt null check.");
            Assert.That(
                summary.Throws.Types.Select(static type => type.ToDisplayString()),
                Does.Contain("System.NullReferenceException"));
            Assert.That(
                summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }
}
