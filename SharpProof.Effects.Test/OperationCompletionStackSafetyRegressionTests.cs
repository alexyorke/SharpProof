namespace SharpProof.Effects.Test;

using Microsoft.CodeAnalysis.Operations;

[TestFixture]
public sealed class OperationCompletionStackSafetyRegressionTests
{
    [Test]
    public void DeepArithmeticChainAbstainsInsteadOfExhaustingTheStack()
    {
        var chain = string.Join(
            " + ",
            Enumerable.Repeat("value", 5000));
        var compilation = EffectTestHost.CreateCompilation(
            $$"""
            public static class Sample {
                public static long Deep(long value) => {{chain}};
            }
            """);
        var method = EffectTestHost.SampleMethod(compilation, "Deep");
        var operation = EffectTestHost.RootOperation(compilation, method)
            .DescendantsAndSelf()
            .OfType<IBinaryOperation>()
            .First();
        var evaluator = EffectTestHost.CreateCompletionEvaluator(
            compilation,
            method);

        Assert.That(evaluator.CanCompleteNormally(operation), Is.True);
    }

    [Test]
    public void DeepStringConcatenationChainAbstainsInsteadOfExhaustingTheStack()
    {
        var chain = string.Join(
            " + ",
            Enumerable.Repeat("value", 5000));
        var compilation = EffectTestHost.CreateCompilation(
            $$"""
            public static class Sample {
                public static string Deep(string value) => {{chain}};
            }
            """);
        var method = EffectTestHost.SampleMethod(compilation, "Deep");
        var operation = EffectTestHost.RootOperation(compilation, method)
            .DescendantsAndSelf()
            .OfType<IBinaryOperation>()
            .First();
        var evaluator = EffectTestHost.CreateCompletionEvaluator(
            compilation,
            method);

        Assert.That(evaluator.CanCompleteNormally(operation), Is.True);
    }
}
