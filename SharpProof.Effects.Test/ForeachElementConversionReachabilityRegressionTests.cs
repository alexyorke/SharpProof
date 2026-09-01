namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ForeachElementConversionReachabilityRegressionTests
{
    [Test]
    public void ThrowingElementConversionKeepsCatchWriteReachable()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public readonly struct Element {
                public static implicit operator int(Element value) =>
                    throw new InvalidOperationException();
            }

            public readonly struct Elements {
                public Enumerator GetEnumerator() => default;

                public readonly struct Enumerator {
                    public Element Current => default;

                    public bool MoveNext() => true;
                }
            }

            public static class Sample {
                private static int s_state;

                public static void Convert(Elements values) {
                    try {
                        foreach (int value in values) {
                            _ = value;
                        }
                    }
                    catch (InvalidOperationException) {
                        s_state++;
                    }
                }
            }
            """);
        var method = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "Convert");

        var result = new EffectAnalysisSession(compilation).Analyze(method);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }
}
