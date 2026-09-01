namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ArrayAccessCompletionRegressionTests
{
    [Test]
    public void DefinitelyOutOfRangeAccessSuppressesSuffixWrite()
    {
        var compilation = CreateCompilation();
        var method = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "DefinitelyOutOfRange");

        Assert.That(HasStaticWrite(compilation, method), Is.False);
    }

    [Test]
    public void UnknownIndexRetainsSuffixWrite()
    {
        var compilation = CreateCompilation();
        var method = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "UnknownIndex");

        Assert.That(HasStaticWrite(compilation, method), Is.True);
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

    private static bool HasStaticWrite(
        Compilation compilation,
        IMethodSymbol method)
    {
        return new EffectAnalysisSession(compilation)
            .Analyze(method)
            .Summary.Writes.Contains(EffectRegionId.Static());
    }
}
