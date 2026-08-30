using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class DeferredCallCompletionTests
{
    [Test]
    public void AsyncCallReturnsBeforeItsDeferredBodyTerminates()
    {
        AssertCallReturnsBeforeSuffix(
            """
            using System;
            using System.Threading.Tasks;

            public static class Sample {
                private static int state;

                private static async Task Deferred() {
                    throw new InvalidOperationException();
                }

                public static void Run() {
                    _ = Deferred();
                    state++;
                }
            }
            """);
    }

    [Test]
    public void IteratorCallReturnsBeforeItsDeferredBodyTerminates()
    {
        AssertCallReturnsBeforeSuffix(
            """
            using System;
            using System.Collections.Generic;

            public static class Sample {
                private static int state;

                private static IEnumerable<int> Deferred() {
                    throw new InvalidOperationException();
                    yield break;
                }

                public static void Run() {
                    _ = Deferred();
                    state++;
                }
            }
            """);
    }

    private static void AssertCallReturnsBeforeSuffix(string source)
    {
        var compilation = EffectTestHost.CreateCompilation(source);
        var run = EffectTestHost.RequireMethod(compilation, "Sample", "Run");
        var syntax = run.DeclaringSyntaxReferences.Single().GetSyntax();
        var root = compilation.GetSemanticModel(syntax.SyntaxTree)
            .GetOperation(syntax) ??
            throw new InvalidOperationException("Run operation was not found.");
        var invocation = root.DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .Single(operation => operation.TargetMethod.Name == "Deferred");
        var completion = new OperationCompletionEvaluator(
            new EffectAnalysisSession(compilation),
            run,
            static (_, _) => false,
            static (_, _) => false,
            static _ => false);
        var result = new EffectAnalysisSession(compilation).Analyze(run);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(completion.CanCompleteNormally(invocation), Is.True);
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
        }
    }
}
