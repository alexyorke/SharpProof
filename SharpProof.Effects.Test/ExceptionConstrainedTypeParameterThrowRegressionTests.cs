namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ExceptionConstrainedTypeParameterThrowRegressionTests
{
    [Test]
    public void GenericThrowKeepsMatchingCatchEffectsReachable()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                private static int s_state;

                public static void Handle<T>(T exception)
                    where T : Exception {
                    try {
                        throw exception;
                    }
                    catch (Exception) {
                        s_state++;
                    }
                }
            }
            """);
        var method = EffectTestHost.SampleMethod(compilation, "Handle");

        var result = new EffectAnalysisSession(compilation).Analyze(method);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(result.Summary.Throws.IsEmpty, Is.True);
        }
    }
}
