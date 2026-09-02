namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ReadModifyWriteSetterRegionRegressionTests
{
    [Test]
    public void SyntheticStoredValuesRetainSetterParameterEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                public int State;

                public void Mutate() => State++;

                public static Box operator +(Box left, Box right) =>
                    right;

                public static Box operator ++(Box value) => value;
            }

            public sealed class Holder {
                private readonly Box _value = new();

                public Box Value {
                    get => _value;
                    set => value.Mutate();
                }
            }

            public static class Sample {
                public static void Compound(Holder holder, Box value) =>
                    holder.Value += value;

                public static void Increment(Holder holder) =>
                    holder.Value++;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            AssertStoredValueWrite(session, compilation, "Compound");
            AssertStoredValueWrite(session, compilation, "Increment");
        }
    }

    private static void AssertStoredValueWrite(
        EffectAnalysisSession session,
        Compilation compilation,
        string methodName)
    {
        var summary = session.Analyze(EffectTestHost.SampleMethod(compilation, methodName)).Summary;

        Assert.That(
            summary.Writes.IsUnknown,
            Is.True,
            $"{methodName} must retain the synthetic stored-value region.");
        Assert.That(
            summary.Completeness,
            Is.EqualTo(EffectCompleteness.Complete),
            methodName);
    }
}
