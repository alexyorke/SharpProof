namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class UsingInitializerUnwindRegressionTests
{
    [Test]
    public void MixedFailureLaterInitializerUnwindsEarlierResource()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class Resource : IDisposable {
                public int Value;

                public void Dispose() {
                    Value++;
                    throw new InvalidOperationException();
                }
            }

            public sealed class Box {
                public int Value;
            }

            public static class Subject {
                public static void Exercise(
                    Resource first,
                    Resource second,
                    bool fail,
                    Box caught) {
                    try {
                        using (
                            Resource outer = first,
                            inner = MaybeAcquire(second, fail)) {
                            Spin();
                        }
                    }
                    catch (InvalidOperationException) {
                        caught.Value++;
                    }
                    catch (ArgumentException) { }
                }

                private static Resource MaybeAcquire(
                    Resource value,
                    bool fail) {
                    if (fail) {
                        throw new ArgumentException();
                    }
                    return value;
                }

                private static void Spin() {
                    while (true) { }
                }
            }
            """);
        var method = EffectTestHost.RequireType(compilation, "Subject")
            .GetMembers("Exercise")
            .OfType<IMethodSymbol>()
            .Single();

        var summary = new EffectAnalysisSession(compilation)
            .Analyze(method)
            .Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True,
                "the earlier acquired resource is disposed on initializer failure");
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Parameter(1)),
                Is.False,
                "the failing initializer does not acquire the later resource");
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Parameter(3)),
                Is.True,
                "the earlier disposal exception reaches its matching catch");
            Assert.That(
                summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }
}
