namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ManagedFlowExceptionRegionRegressionTests
{
    private static readonly CSharpCompilation Compilation =
        EffectTestHost.CreateCompilation(
            """
            using System;
            public static class TX
            {
                public static void Boom() => throw new InvalidOperationException();
                public static void Maybe(int value)
                {
                    if (value > 0) throw new InvalidOperationException();
                }
            }
            public static class Sample
            {
                public static int CatchAssignment()
                {
                    int[] values = new int[3];
                    int index = 0;
                    try { TX.Boom(); }
                    catch (InvalidOperationException) { index = 7; }
                    return values[index];
                }

                public static int FinallyAssignment(bool shouldThrow)
                {
                    int[] values = new int[3];
                    int index = 0;
                    try
                    {
                        if (shouldThrow)
                        {
                            throw new InvalidOperationException();
                        }
                    }
                    finally { index = 7; }
                    return values[index];
                }

                public static int PartialTryAssignment(int value)
                {
                    int[] values = new int[3];
                    int index = 0;
                    try { index = 7; TX.Maybe(value); index = 0; }
                    catch (InvalidOperationException) { }
                    return values[index];
                }

                public static int SafeCatchAssignment()
                {
                    int[] values = new int[3];
                    int index = 0;
                    try { TX.Boom(); }
                    catch (InvalidOperationException) { index = 1; }
                    return values[index];
                }
            }
            """);

    [TestCase("CatchAssignment")]
    [TestCase("FinallyAssignment")]
    [TestCase("PartialTryAssignment")]
    public void ExceptionRegionsDoNotRetainStaleLocalFacts(string methodName)
    {
        var summary = EffectTestHost.AnalyzeSample(Compilation, methodName)
            .Summary;

        Assert.That(
            summary.Throws.Types.Select(static type => type.ToDisplayString()),
            Does.Contain("System.IndexOutOfRangeException"),
            methodName);
    }

    [Test]
    public void CatchThatBoundsTheIndexRemainsSafe()
    {
        var summary = EffectTestHost.AnalyzeSample(
            Compilation,
            "SafeCatchAssignment").Summary;

        Assert.That(
            summary.Throws.Types.Select(static type => type.ToDisplayString()),
            Does.Not.Contain("System.IndexOutOfRangeException"));
    }
}
