namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class NewHidingToStringRegressionTests
{
    [Test]
    public void StringConcatenationUsesTheObjectVirtualSlot()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public class BaseValue {
                private static int s_state;

                public override string ToString() {
                    s_state++;
                    throw new InvalidOperationException();
                }
            }

            public sealed class DerivedValue : BaseValue {
                public new string ToString() => "hidden";
            }

            public static class Sample {
                public static string Format(DerivedValue value) =>
                    "value=" + value;
            }
            """);
        var result = EffectTestHost.AnalyzeSample(compilation, "Format");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                result.Summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
                Does.Contain("System.InvalidOperationException"));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void ValueTypeFormattingSkipsNewHidingToString()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public readonly struct HiddenValue {
                public new string ToString() => "hidden";
            }

            public static class Sample {
                public static string Format(HiddenValue value) =>
                    "value=" + value;
            }
            """);
        var result = EffectTestHost.AnalyzeSample(compilation, "Format");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Unknown));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(
                result.Summary.Uncertainty & EffectUncertainty.UnmodeledCall,
                Is.EqualTo(EffectUncertainty.UnmodeledCall));
        }
    }
}
