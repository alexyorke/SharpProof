using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class AssignableRecursivePatternCompletionRegressionTests
{
    private static readonly Compilation SharedCompilation = CreateCompilation();

    [TestCase("BasePattern")]
    [TestCase("InterfacePattern")]
    public void GuaranteedAssignablePatternsHonorNonreturningAccessors(
        string methodName)
    {
        var compilation = SharedCompilation;
        var method = EffectTestHost.SampleMethod(compilation, methodName);
        var pattern = EffectTestHost.RootOperation(compilation, method)
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
                EffectTestHost.CreateCompletionEvaluator(compilation, method)
                    .CanCompleteNormally(pattern),
                Is.False,
                methodName);
        }
    }

    private static CSharpCompilation CreateCompilation()
    {
        return EffectTestHost.CreateCompilation(
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
    }

}
