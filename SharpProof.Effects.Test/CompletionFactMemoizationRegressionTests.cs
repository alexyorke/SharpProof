using System.Text;
using System.Globalization;
using Microsoft.CodeAnalysis.Operations;

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
            TimeSpan.FromSeconds(10));
        var facts = new DefiniteOperationFacts(
            compilation,
            cancellation.Token);

        Assert.That(facts.MethodCanCompleteNormally(root), Is.True);
        var invocation = EffectTestHost.RootOperation(compilation, root)
            .DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .First();
        Assert.That(
            facts.CompletesNormally(invocation),
            Is.True,
            "Definite completion should memoize shared callees as well.");
    }

    [Test]
    public void RecursiveCallGraphIsSolvedWithoutEnumeratingPaths()
    {
        const int depth = 20;
        var sourceBuilder = new StringBuilder()
            .AppendLine("public static class Sample {");
        for (var index = 0; index < depth; index++)
        {
            var callee = (index + 1) % depth;
            var methodSource = FormattableString.Invariant(
                $"    private static int Level{index}(int value) {{ if (value <= 0) return 0; int first = Level{callee}(value - 1); int second = Level{callee}(value - 2); return first + second; }}");
            sourceBuilder.AppendLine(methodSource);
        }
        sourceBuilder.AppendLine("}");

        var compilation = EffectTestHost.CreateCompilation(
            sourceBuilder.ToString());
        var root = EffectTestHost.SampleMethod(compilation, "Level0");
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));
        var facts = new DefiniteOperationFacts(
            compilation,
            cancellation.Token);

        Assert.That(facts.MethodCanCompleteNormally(root), Is.True);
    }

    [Test]
    public void DirectSelfRecursionWithoutBaseCaseRemainsNoncompleting()
    {
        var compilation = EffectTestHost.CreateCompilation(
            "public static class Sample { " +
            "private static void Spin() => Spin(); }");
        var spin = EffectTestHost.SampleMethod(compilation, "Spin");
        var facts = EffectTestHost.CreateCompletionFacts(compilation);

        Assert.That(facts.MethodCanCompleteNormally(spin), Is.False);
    }
}
