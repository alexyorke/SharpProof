namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class DivergingDisposeEffectRegressionTests
{
    [Test]
    public void DivergingDisposeRetainsEffectsAndTermination()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sink
            {
                public static int Count;
            }

            public sealed class Pin : IDisposable
            {
                public void Dispose()
                {
                    Sink.Count++;
                    while (true) { }
                }
            }

            public static class Sample
            {
                public static void Declaration()
                {
                    using var pin = new Pin();
                }

                public static void Statement()
                {
                    using (new Pin()) { }
                }
            }
            """);

        foreach (var methodName in new[] { "Declaration", "Statement" })
        {
            var summary = EffectTestHost.AnalyzeSample(compilation, methodName)
                .Summary;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    summary.Writes.Contains(EffectRegionId.Static()),
                    Is.True,
                    $"{methodName}: Dispose writes are retained");
                Assert.That(
                    summary.Termination,
                    Is.EqualTo(EffectTermination.MayDiverge),
                    $"{methodName}: Dispose termination is retained");
                Assert.That(
                    summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    $"{methodName}: summary remains complete");
            }
        }
    }
}
