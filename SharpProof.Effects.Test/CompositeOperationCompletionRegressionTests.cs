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
        var run = EffectTestHost.RequireMethod(compilation, "Sample", "Run");
        var initializer = GetOperation(compilation, run)
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
        var run = EffectTestHost.RequireMethod(compilation, "Sample", "Run");
        var interpolation = GetOperation(compilation, run)
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
        var completion = new OperationCompletionEvaluator(
            new EffectAnalysisSession(compilation),
            run,
            static (_, _) => false,
            static (_, _) => false,
            static _ => false);
        var result = new EffectAnalysisSession(compilation).Analyze(run);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(completion.CanCompleteNormally(composite), Is.False);
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False);
        }
    }

    private static IOperation GetOperation(
        Compilation compilation,
        IMethodSymbol method)
    {
        var syntax = method.DeclaringSyntaxReferences.Single().GetSyntax();
        return compilation.GetSemanticModel(syntax.SyntaxTree)
            .GetOperation(syntax) ??
            throw new InvalidOperationException(
                $"Operation for '{method.Name}' was not found.");
    }
}
