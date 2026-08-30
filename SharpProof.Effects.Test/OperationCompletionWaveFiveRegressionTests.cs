using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class OperationCompletionWaveFiveRegressionTests
{
    [Test]
    public void NonexhaustiveSwitchExpressionWithReturningArmMayComplete()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int state;

                private static int Choose(int value) =>
                    value switch { 0 => 1 };

                public static void Run(int value) {
                    _ = Choose(value);
                    state++;
                }
            }
            """);
        var choose = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "Choose");
        var run = EffectTestHost.RequireMethod(compilation, "Sample", "Run");
        var facts = new DefiniteOperationFacts(
            compilation,
            System.Threading.CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(facts.MethodCanCompleteNormally(choose), Is.True);
            Assert.That(HasStaticWrite(compilation, run), Is.True);
        }
    }

    [Test]
    public void ConstantNullConversionToNullableMayComplete()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int state;

                private static void Observe(int? value) {
                }

                public static void Run() {
                    Observe((int?)null);
                    state++;
                }
            }
            """);
        var run = EffectTestHost.RequireMethod(compilation, "Sample", "Run");
        var conversion = GetOperation(compilation, run)
            .DescendantsAndSelf()
            .OfType<IConversionOperation>()
            .Single(operation =>
                operation.Type is INamedTypeSymbol
                {
                    OriginalDefinition.SpecialType:
                        SpecialType.System_Nullable_T
                } &&
                operation.Operand.ConstantValue is
                { HasValue: true, Value: null });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CreateCompletionEvaluator(compilation, run)
                    .CanCompleteNormally(conversion),
                Is.True);
            Assert.That(HasStaticWrite(compilation, run), Is.True);
        }
    }

    [Test]
    public void UserDefinedConstantNullConversionToStructMayComplete()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable

            public readonly struct Token {
                public static implicit operator Token(string? value) =>
                    default;
            }

            public static class Sample {
                private static int state;

                public static void Run() {
                    _ = (Token)(string?)null;
                    state++;
                }
            }
            """);
        var run = EffectTestHost.RequireMethod(compilation, "Sample", "Run");
        var conversion = GetOperation(compilation, run)
            .DescendantsAndSelf()
            .OfType<IConversionOperation>()
            .Single(operation => operation.OperatorMethod != null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                conversion.Operand.ConstantValue,
                Is.EqualTo(new Optional<object?>(null)));
            Assert.That(
                CreateCompletionEvaluator(compilation, run)
                    .CanCompleteNormally(conversion),
                Is.True);
            Assert.That(HasStaticWrite(compilation, run), Is.True);
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

    private static OperationCompletionEvaluator CreateCompletionEvaluator(
        Compilation compilation,
        IMethodSymbol caller)
    {
        return new OperationCompletionEvaluator(
            new EffectAnalysisSession(compilation),
            caller,
            static (_, _) => false,
            static (_, _) => false,
            static _ => false);
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
