using System.Text;
using System.Globalization;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class CompletionFactMemoizationRegressionTests
{
    [Test]
    public void SharedCalleeChainDoesNotRewalkEachCallPath()
    {
        const int depth = 30;
        var sourceBuilder = new StringBuilder()
            .AppendLine("public static class Sample {");
        for (var index = depth - 1; index >= 0; index--)
        {
            var callee = $"M{index + 1}";
            sourceBuilder.AppendLine(
                CultureInfo.InvariantCulture,
                $"    private static int M{index}(int value) => " +
                $"{callee}(value) + {callee}(value + 1);");
        }
        sourceBuilder.AppendLine("    private static int M30(int value) => value;");
        sourceBuilder.AppendLine("}");

        var compilation = EffectTestHost.CreateCompilation(
            sourceBuilder.ToString());
        var root = EffectTestHost.SampleMethod(compilation, "M0");
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds(5));
        var facts = new DefiniteOperationFacts(
            compilation,
            cancellation.Token);

        Assert.That(facts.MethodCanCompleteNormally(root), Is.True);
    }
}
