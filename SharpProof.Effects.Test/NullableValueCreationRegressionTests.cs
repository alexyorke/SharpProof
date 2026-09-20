namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class NullableValueCreationRegressionTests
{
    [Test]
    public void EmptyNullableCreationFlowsAsNullThroughCoalesceAndBoxing()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable
            public static class Sample {
                private static int s_state;

                private static int Mutate() {
                    s_state++;
                    return s_state;
                }

                public static int Coalesce() =>
                    new System.Nullable<int>() ?? Mutate();

                public static object? BoxEmpty() =>
                    (object?)new System.Nullable<int>();

                public static int NullReceiver() =>
                    ((object?)new System.Nullable<int>()).GetHashCode();
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var coalesce = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "Coalesce"));
        var boxed = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "BoxEmpty"));
        var receiver = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "NullReceiver"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                coalesce.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                coalesce.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                boxed.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.None));
            Assert.That(
                boxed.Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                receiver.Summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
                Does.Contain("System.NullReferenceException"));
        }
    }
}
