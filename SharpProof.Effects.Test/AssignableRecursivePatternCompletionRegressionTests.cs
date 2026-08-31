using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class AssignableRecursivePatternCompletionRegressionTests
{
    [TestCase("BasePattern")]
    [TestCase("InterfacePattern")]
    public void GuaranteedAssignablePatternsHonorNonreturningAccessors(
        string methodName)
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                public int Value;
            }

            public class BaseSource {
                public int Value {
                    get { while (true) { } }
                }
            }

            public sealed class DerivedSource : BaseSource { }

            public interface ISource {
                public int Value {
                    get { while (true) { } }
                }
            }

            public sealed class InterfaceSource : ISource { }

            public static class Sample {
                public static void BasePattern(Box suffix) {
                    _ = new DerivedSource() switch {
                        BaseSource { Value: _ } => 0,
                        _ => 1
                    };
                    suffix.Value++;
                }

                public static void InterfacePattern(Box suffix) {
                    _ = new InterfaceSource() switch {
                        ISource { Value: _ } => 0,
                        _ => 1
                    };
                    suffix.Value++;
                }
            }
            """);
        var method = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            methodName);
        var pattern = GetOperation(compilation, method)
            .DescendantsAndSelf()
            .OfType<ISwitchExpressionOperation>()
            .Single()
            .Arms[0]
            .Pattern;
        var recursive = (IRecursivePatternOperation)pattern;
        var conversion = compilation.ClassifyCommonConversion(
            pattern.InputType!,
            recursive.MatchedType!);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                SymbolEqualityComparer.Default.Equals(
                    pattern.InputType,
                    recursive.MatchedType),
                Is.False,
                methodName);
            Assert.That(
                conversion.IsImplicit && conversion.IsReference,
                Is.True,
                methodName);
            Assert.That(
                CreateCompletionEvaluator(compilation, method)
                    .CanCompleteNormally(pattern),
                Is.False,
                methodName);
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
}
