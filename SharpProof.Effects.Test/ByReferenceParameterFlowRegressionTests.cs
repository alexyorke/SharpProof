namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ByReferenceParameterFlowRegressionTests
{
    [TestCase("NRef")]
    [TestCase("NIn")]
    [TestCase("NDirect")]
    public void CallsAndAliasedWritesDoNotPreserveByReferenceFacts(string methodName)
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Holder
            {
                public int Value;
            }

            public static class Sample
            {
                private static void Reset(Holder holder) => holder.Value = 0;

                private static int NRef(ref int value, Holder holder)
                {
                    if (value != 0)
                    {
                        Reset(holder);
                        return 10 / value;
                    }

                    return 0;
                }

                private static int NIn(in int value, Holder holder)
                {
                    if (value != 0)
                    {
                        Reset(holder);
                        return 10 / value;
                    }

                    return 0;
                }

                private static int NDirect(ref int value, Holder holder)
                {
                    if (value != 0)
                    {
                        holder.Value = 0;
                        return 10 / value;
                    }

                    return 0;
                }
            }
            """);

        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(compilation, "Sample", methodName));

        Assert.That(
            result.Summary.Throws.Types.Select(static type => type.ToDisplayString()),
            Does.Contain("System.DivideByZeroException"));
    }
}
