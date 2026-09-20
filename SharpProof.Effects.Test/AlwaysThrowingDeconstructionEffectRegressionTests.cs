namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class AlwaysThrowingDeconstructionEffectRegressionTests
{
    [TestCase("Direct")]
    [TestCase("ThroughHelper")]
    public void AlwaysThrowingDeconstructRetainsCalleeEffects(string methodName)
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Global
            {
                public static int State;
            }

            public sealed class AlwaysThrowsD
            {
                public void Deconstruct(out int left, out int right)
                {
                    Global.State++;
                    left = right = 0;
                    throw new InvalidOperationException();
                }
            }

            public static class Sample
            {
                public static int Direct(AlwaysThrowsD value)
                {
                    var (left, right) = value;
                    return left + right;
                }

                public static int ThroughHelper(AlwaysThrowsD value) =>
                    Helper(value);

                private static int Helper(AlwaysThrowsD value)
                {
                    var (left, right) = value;
                    return left + right;
                }
            }
            """);

        var result = EffectTestHost.AnalyzeSample(compilation, methodName);
        var exceptionNames = result.Summary.Throws.Types.Select(
            static type => type.ToDisplayString()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "the Deconstruct body increments Global.State");
            Assert.That(
                exceptionNames,
                Does.Contain("System.InvalidOperationException"),
                "the Deconstruct exception must escape");
            Assert.That(result.Summary.Throws.IncludesUnknown, Is.False);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(result.Projection.IsComplete, Is.True);
        }
    }
}
