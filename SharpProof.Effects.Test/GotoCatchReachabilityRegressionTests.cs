namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class GotoCatchReachabilityRegressionTests
{
    [Test]
    public void GotoFromNestedBlockRetainsOuterThrowingOperation()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample
            {
                private static int s_state;

                public static void Run(bool condition, int[] values, int index)
                {
                    try
                    {
                        {
                            if (condition)
                            {
                                goto Target;
                            }

                            throw new ArgumentException();

                        Target:
                            ;
                        }

                        _ = values[index];
                    }
                    catch (IndexOutOfRangeException)
                    {
                        s_state++;
                    }
                }
            }
            """);

        var result = new EffectAnalysisSession(compilation)
            .Analyze(EffectTestHost.SampleMethod(compilation, "Run"));

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.True);
    }

    [Test]
    public void GotoIntoNonCompletingLoopDoesNotReachAfterLoop()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample
            {
                private static int s_state;

                public static void Run(int[] values, int index)
                {
                    try
                    {
                        while (true)
                        {
                            goto Target;

                        Target:
                            ;
                        }

                        _ = values[index];
                    }
                    catch (IndexOutOfRangeException)
                    {
                        s_state++;
                    }
                }
            }
            """);

        var result = new EffectAnalysisSession(compilation)
            .Analyze(EffectTestHost.SampleMethod(compilation, "Run"));

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.False);
    }
}
