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

    [TestCase("ArrayIndexAfterConditionalAdd", "System.IndexOutOfRangeException")]
    [TestCase("DivideAfterConditionalSubtract", "System.DivideByZeroException")]
    [TestCase("ArrayIndexAfterBranchingAdd", "System.IndexOutOfRangeException")]
    [TestCase("ArrayIndexAfterCoalescingAdd", "System.IndexOutOfRangeException")]
    public void CompoundAssignmentLvalueCaptureInvalidatesOriginalStorageFacts(
        string methodName,
        string expectedException)
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample
            {
                public static int ArrayIndexAfterConditionalAdd()
                {
                    int[] values = new int[3];
                    int index = 0;
                    index += index > -1 ? 7 : 0;
                    return values[index];
                }

                public static int DivideAfterConditionalSubtract()
                {
                    int divisor = 1;
                    divisor -= divisor > 0 ? 1 : 0;
                    return 10 / divisor;
                }

                public static int ArrayIndexAfterBranchingAdd(bool chooseFirst)
                {
                    int[] values = new int[3];
                    int index = 0;
                    index += chooseFirst ? 7 : 8;
                    return values[index];
                }

                public static int ArrayIndexAfterCoalescingAdd(int? value)
                {
                    int[] values = new int[3];
                    int index = 0;
                    index += value ?? 7;
                    return values[index];
                }

                public static int SafeArrayIndexAfterSimpleAssignment()
                {
                    int[] values = new int[3];
                    int index = 0;
                    index = index + (index > -1 ? 1 : 0);
                    return values[index];
                }
            }
            """);
        var summary = EffectTestHost.AnalyzeSample(compilation, methodName)
            .Summary;

        Assert.That(
            summary.Throws.Types.Select(static type => type.ToDisplayString()),
            Does.Contain(expectedException),
            methodName);
        Assert.That(
            EffectTestHost.AnalyzeSample(
                compilation,
                "SafeArrayIndexAfterSimpleAssignment").Summary.Throws.Types
                .Select(static type => type.ToDisplayString()),
            Does.Not.Contain("System.IndexOutOfRangeException"));
    }
}
