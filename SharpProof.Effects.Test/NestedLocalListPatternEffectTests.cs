namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class NestedLocalListPatternEffectTests
{
    [Test]
    public void NestedLocalFunctionsDoNotHideListPatternIndexerEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class Holder {
                public int Value;
            }

            public static class Global {
                public static int State;
            }

            public sealed class Values {
                public int Length {
                    get {
                        int Nested() => 0;
                        return 1;
                    }
                }

                public int this[int index] {
                    get {
                        Global.State++;
                        throw new InvalidOperationException();
                    }
                }
            }

            public static class Sample {
                public static void Run(Holder unreachable) {
                    _ = new Values() switch {
                        [0] => true,
                        _ => false
                    };
                    unreachable.Value = 1;
                }
            }
            """);

        var result = EffectTestHost.AnalyzeSample(compilation, "Run");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "write performed by the list-pattern indexer");
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.False,
                "write after the noncompleting indexer");
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
