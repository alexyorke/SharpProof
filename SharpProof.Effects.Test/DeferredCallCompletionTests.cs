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

    [Test]
    public void NonreturningAwaitOperandSuppressesAsyncSuffix()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System.Threading.Tasks;

            public static class Sample {
                private static int state;

                private static Task NeverReturns() {
                    while (true) { }
                }

                public static async Task Run() {
                    await NeverReturns();
                    state++;
                }
            }
            """);
        var run = EffectTestHost.SampleMethod(compilation, "Run");
        var root = EffectTestHost.RootOperation(compilation, run);
        var awaitOperation = root.DescendantsAndSelf()
            .OfType<IAwaitOperation>()
            .Single();
        var completion = EffectTestHost.CreateCompletionEvaluator(
            compilation,
            run);
        var result = new EffectAnalysisSession(compilation).Analyze(run);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                completion.CanCompleteNormally(awaitOperation),
                Is.False);
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False);
        }
    }

    private static void AssertCallReturnsBeforeSuffix(string source)
    {
        var compilation = EffectTestHost.CreateCompilation(source);
        var run = EffectTestHost.SampleMethod(compilation, "Run");
        var root = EffectTestHost.RootOperation(compilation, run);
        var invocation = root.DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .Single(operation => operation.TargetMethod.Name == "Deferred");
        var completion = EffectTestHost.CreateCompletionEvaluator(
            compilation,
            run);
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
