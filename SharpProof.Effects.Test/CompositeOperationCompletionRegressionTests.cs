using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class CompositeOperationCompletionRegressionTests
{
    [Test]
    public void TerminalArrayInitializerSuppressesSuffixEffect()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int state;

                private static int NeverReturns() {
                    while (true) { }
                }

                public static void Run() {
                    _ = new[] { NeverReturns() };
                    state++;
                }
            }
            """);
        var run = EffectTestHost.SampleMethod(compilation, "Run");
        var initializer = EffectTestHost.RootOperation(compilation, run)
            .DescendantsAndSelf()
            .OfType<IArrayInitializerOperation>()
            .Single();

        AssertTerminalComposite(compilation, run, initializer);
    }

    [Test]
    public void TerminalInterpolationFormatterSuppressesSuffixEffect()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Value {
                public override string ToString() {
                    while (true) { }
                }
            }

            public static class Sample {
                private static int state;

                public static void Run() {
                    _ = $"{new Value()}";
                    state++;
                }
            }
            """);
        var run = EffectTestHost.SampleMethod(compilation, "Run");
        var interpolation = EffectTestHost.RootOperation(compilation, run)
            .DescendantsAndSelf()
            .OfType<IInterpolatedStringOperation>()
            .Single();

        AssertTerminalComposite(compilation, run, interpolation);
    }

    private static void AssertTerminalComposite(
        Compilation compilation,
        IMethodSymbol run,
        IOperation composite)
    {
        var completion = EffectTestHost.CreateCompletionEvaluator(
            compilation,
            run);
        var result = new EffectAnalysisSession(compilation).Analyze(run);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(completion.CanCompleteNormally(composite), Is.False);
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False);
        }
    }

}
