namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class RefLocalEffectRegressionTests
{
    [Test]
    public void RefLocalMutationsRetainCallerReadAndWriteEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void Assign(ref int value) {
                    ref int alias = ref value;
                    alias = 1;
                }

                public static void Add(ref int value) {
                    ref int alias = ref value;
                    alias += 1;
                }

                public static void Rebind(
                    ref int first,
                    ref int second) {
                    ref int alias = ref first;
                    alias = ref second;
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var assign = session.Analyze(EffectTestHost.SampleMethod(compilation, "Assign"));
        var add = session.Analyze(EffectTestHost.SampleMethod(compilation, "Add"));
        var rebind = session.Analyze(EffectTestHost.SampleMethod(compilation, "Rebind"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                assign.Summary.Writes.Regions,
                Does.Contain(EffectRegionId.Parameter(0)));
            Assert.That(
                EffectContractMappings.IsObservablePure(assign.Summary),
                Is.False);
            Assert.That(
                add.Summary.Reads.Regions,
                Does.Contain(EffectRegionId.Parameter(0)));
            Assert.That(
                add.Summary.Writes.Regions,
                Does.Contain(EffectRegionId.Parameter(0)));
            Assert.That(
                rebind.Summary.Writes.IsEmpty,
                Is.True,
                "Ref reassignment changes the alias, not either pointee.");
        }
    }
}
