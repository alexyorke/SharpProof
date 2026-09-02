namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ArrayAccessCompletionRegressionTests
{
    [TestCase("DefinitelyOutOfRange", false)]
    [TestCase("UnknownIndex", true)]
    public void ArrayAccessControlsSuffixWrite(
        string methodName,
        bool expected)
    {
        var compilation = CreateCompilation();
        var method = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            methodName);

        Assert.That(
            EffectTestHost.HasStaticWrite(compilation, method),
            Is.EqualTo(expected));
    }

    private static CSharpCompilation CreateCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int state;

                public static void DefinitelyOutOfRange() {
                    _ = (new int[0])[0];
                    state++;
                }

                public static void UnknownIndex(
                    int[] values,
                    int index) {
                    _ = values[index];
                    state++;
                }
            }
            """);
    }

}
