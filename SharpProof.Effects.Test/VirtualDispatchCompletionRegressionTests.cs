using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class VirtualDispatchCompletionRegressionTests
{
    [Test]
    public void ReturningOverrideMakesVirtualCallMayComplete()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public class DivergingBase {
                public virtual void Invoke() {
                    while (true) { }
                }
            }

            public sealed class ReturningDerived : DivergingBase {
                public override void Invoke() {
                }
            }

            public static class Sample {
                private static int state;

                public static void Run(DivergingBase value) {
                    value.Invoke();
                    state++;
                }
            }
            """);
        var baseMethod = EffectTestHost.RequireMethod(
            compilation,
            "DivergingBase",
            "Invoke");
        var derivedMethod = EffectTestHost.RequireMethod(
            compilation,
            "ReturningDerived",
            "Invoke");
        var caller = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "Run");
        var invocation = GetInvocation(compilation, caller);
        var completion = new DefiniteOperationFacts(
            compilation,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(invocation.IsVirtual, Is.True);
            Assert.That(
                completion.MethodCanCompleteNormally(baseMethod),
                Is.False);
            Assert.That(
                completion.MethodCanCompleteNormally(derivedMethod),
                Is.True);
            Assert.That(
                completion.MethodCanCompleteNormally(caller),
                Is.True,
                "the returning runtime override reaches the caller suffix");
            Assert.That(
                CreateEvaluator(compilation, caller)
                    .CanCompleteNormally(invocation),
                Is.True,
                "virtual dispatch must not inherit base-body noncompletion");
        }
    }

    [Test]
    public void BaseQualifiedCallStillUsesTheBaseBody()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public class DivergingBase {
                public virtual void Invoke() {
                    while (true) { }
                }
            }

            public sealed class Derived : DivergingBase {
                private static int state;

                public void Run() {
                    base.Invoke();
                    state++;
                }
            }
            """);
        var caller = EffectTestHost.RequireMethod(
            compilation,
            "Derived",
            "Run");
        var invocation = GetInvocation(compilation, caller);
        var completion = new DefiniteOperationFacts(
            compilation,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(invocation.IsVirtual, Is.False);
            Assert.That(
                completion.MethodCanCompleteNormally(caller),
                Is.False);
            Assert.That(
                CreateEvaluator(compilation, caller)
                    .CanCompleteNormally(invocation),
                Is.False);
        }
    }

    private static IInvocationOperation GetInvocation(
        Compilation compilation,
        IMethodSymbol method)
    {
        var declaration = method.DeclaringSyntaxReferences.Single()
            .GetSyntax();
        return compilation.GetSemanticModel(declaration.SyntaxTree)
            .GetOperation(declaration)!
            .DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .Single();
    }

    private static OperationCompletionEvaluator CreateEvaluator(
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
