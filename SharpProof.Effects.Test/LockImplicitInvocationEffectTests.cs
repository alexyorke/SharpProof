namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class LockImplicitInvocationEffectTests
{
    [Test]
    public void CollectionInitializerCallsInsideLocksRetainTheirEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Collections;

            public sealed class Holder {
                public int Value;
            }

            public sealed class Values : IEnumerable {
                public void Add(Holder target) {
                    target.Value = 1;
                    throw new InvalidOperationException();
                }

                public IEnumerator GetEnumerator() =>
                    throw new NotSupportedException();
            }

            public static class Sample {
                public static void Run(
                    object gate,
                    Holder writtenByAdd,
                    Holder unreachable) {
                    lock (gate) {
                        _ = new Values { writtenByAdd };
                        unreachable.Value = 1;
                    }
                }
            }
            """);

        var result = EffectTestHost.AnalyzeSample(compilation, "Run");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Parameter(1)),
                Is.True,
                "write performed by the implicit Add call");
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Parameter(2)),
                Is.False,
                "write after the noncompleting Add call");
            Assert.That(
                result.Summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
                Does.Contain("System.InvalidOperationException"));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }
}
