namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ConditionalAssignmentEffectRegressionTests
{
    private static readonly Compilation Compilation =
        EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int Conditional(int value) {
                    var result = 1;
                    result = value > 0 ? 2 : 3;
                    return result;
                }

                public static bool Logical(bool value) {
                    var result = false;
                    result = value && false;
                    return result;
                }

                public static string? Coalesce(string? value, string? fallback) {
                    value ??= fallback;
                    return value;
                }
            }
            """);

    [TestCase("Conditional")]
    [TestCase("Logical")]
    [TestCase("Coalesce")]
    public void AssignmentFlowCapturesResolveToLocalStorage(string methodName)
    {
        var method = EffectTestHost.RequireMethod(
            Compilation,
            "Sample",
            methodName);
        var result = new EffectAnalysisSession(Compilation).Analyze(method);

        Assert.That(
            result.Summary.Completeness,
            Is.EqualTo(EffectCompleteness.Complete),
            methodName);
        Assert.That(
            result.Summary.Uncertainty,
            Is.EqualTo(EffectUncertainty.None),
            methodName);
    }
}
