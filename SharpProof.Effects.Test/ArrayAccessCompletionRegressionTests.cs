namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ArrayAccessCompletionRegressionTests
{
    private static readonly CSharpCompilation SharedCompilation =
        CreateCompilation();

    [TestCase("DefinitelyOutOfRange", false)]
    [TestCase("UnknownIndex", true)]
    public void ArrayAccessControlsSuffixWrite(
        string methodName,
        bool expected)
    {
        var compilation = SharedCompilation;
        var method = EffectTestHost.SampleMethod(compilation, methodName);

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
