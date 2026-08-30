namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class DelegateArgumentFlowRegressionTests
{
    [Test]
    public void SynchronouslyInvokedDelegateArgumentInvalidatesCapturedFacts()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public static class Sample {
                private static int s_state;

                [SharpProofTrusted("reviewed synchronous callback boundary")]
                [EffectContract(
                    SharpProofEffect.WritesCapturedState,
                    PreconditionFree = true,
                    Complete = true)]
                private static extern void InvokeSynchronously(
                    Action callback);

                public static int Evaluate() {
                    var divisor = 1;
                    string? text = "ready";
                    void Mutate() {
                        divisor = 0;
                        text = null;
                    }
                    InvokeSynchronously(Mutate);

                    try {
                        _ = text.Length;
                    }
                    catch (NullReferenceException) {
                        s_state++;
                    }

                    return 1 / divisor;
                }
            }
            """);

        var session = new EffectAnalysisSession(compilation);
        var result = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                "Evaluate"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Regions,
                Does.Contain(EffectRegionId.Static()));
            Assert.That(
                result.Summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
                Does.Contain("System.DivideByZeroException"));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }
}
