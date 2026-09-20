namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class IteratorReturnNullabilityRegressionTests
{
    [Test]
    public void YieldingNullDoesNotHideExceptionsFromTheForeachBody()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Collections.Generic;

            public static class Sample
            {
                private static int s_state;

                private static IEnumerable<object?> Items()
                {
                    yield return null;
                }

                private static void Work() =>
                    throw new InvalidOperationException();

                public static void Run()
                {
                    try
                    {
                        foreach (var item in Items())
                        {
                            Work();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        s_state++;
                    }
                }
            }
            """);

        var result = EffectTestHost.AnalyzeSample(compilation, "Run");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "the reachable catch body must retain its static write");
        }
    }
}
