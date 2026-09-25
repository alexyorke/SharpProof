using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class DeferredCallCompletionTests
{
    [Test]
    public void AsyncCallReturnsBeforeItsDeferredBodyTerminates()
    {
        AssertCallReturnsBeforeSuffix(BuildDeferredSource(
            "using System.Threading.Tasks;",
            """
            private static async Task Deferred() {
                throw new InvalidOperationException();
            }
            """));
    }

    [Test]
    public void IteratorCallReturnsBeforeItsDeferredBodyTerminates()
    {
        AssertCallReturnsBeforeSuffix(BuildDeferredSource(
            "using System.Collections.Generic;",
            """
            private static IEnumerable<int> Deferred() {
                throw new InvalidOperationException();
                yield break;
            }
            """));
    }

    [Test]
    public void AsyncCallDefersFaultButKeepsSynchronousPrefixWrites()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Threading.Tasks;

            public static class Sample {
                private static int state;
                private static Task _;

                private static class InitializationFailure {
                    static InitializationFailure() {
                        throw new InvalidOperationException();
                    }

                    public static async Task Start() {
                        await Task.CompletedTask;
                    }
                }

                private static async Task Deferred() {
                    state++;
                    await Task.CompletedTask;
                    throw new InvalidOperationException();
                }

                private static async ValueTask DeferredValueTask() {
                    await ValueTask.CompletedTask;
                    throw new InvalidOperationException();
                }

                private static async ValueTask<int> DeferredGenericValueTask() {
                    await ValueTask.CompletedTask;
                    throw new InvalidOperationException();
                }

                public static Task Start() => Deferred();
                public static ValueTask StartValueTask() => DeferredValueTask();
                public static ValueTask<int> StartGenericValueTask() => DeferredGenericValueTask();
                public static Task AssignToNamedUnderscore() {
                    _ = Deferred();
                    return _;
                }
                public static Task StartWithTypeInitialization() =>
                    InitializationFailure.Start();

                public static async Task Observe() {
                    await Deferred();
                }
            }
            """);

        var session = new EffectAnalysisSession(compilation);
        var start = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "Start")).Summary;
        var startValueTask = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "StartValueTask")).Summary;
        var startGenericValueTask = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "StartGenericValueTask")).Summary;
        var assignedUnderscore = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "AssignToNamedUnderscore")).Summary;
        var withTypeInitialization = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "StartWithTypeInitialization")).Summary;
        var observe = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "Observe")).Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(start.Writes.Contains(EffectRegionId.Static()), Is.True);
            Assert.That(start.Throws.IsEmpty, Is.True);
            Assert.That(startValueTask.Throws.IsEmpty, Is.True);
            Assert.That(startGenericValueTask.Throws.IsEmpty, Is.True);
            Assert.That(
                assignedUnderscore.Throws.Types.Select(static type => type.Name),
                Does.Contain("InvalidOperationException"));
            Assert.That(withTypeInitialization.Throws.IsEmpty, Is.False);
            Assert.That(
                observe.Throws.Types.Select(static type => type.Name),
                Does.Contain("InvalidOperationException"));
        }
    }

    [Test]
    public void IteratorBodyIsDeferredUntilSequenceEnumeration()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Collections.Generic;

            public static class Sample {
                private static int state;

                private static IEnumerable<int> Deferred() {
                    state++;
                    throw new InvalidOperationException();
                    yield break;
                }

                public static int CreateUnused() {
                    var sequence = Deferred();
                    return 0;
                }

                public static int Enumerate() {
                    foreach (var value in Deferred()) {
                        return value;
                    }
                    return 0;
                }

                public static IEnumerable<int> ReturnSequence() => Deferred();
            }
            """);

        var session = new EffectAnalysisSession(compilation);
        var unused = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "CreateUnused")).Summary;
        var enumerated = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "Enumerate")).Summary;
        var returned = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "ReturnSequence")).Summary;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                unused.Writes.Contains(EffectRegionId.Static()),
                Is.False);
            Assert.That(unused.Throws.IsEmpty, Is.True);
            Assert.That(
                enumerated.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                enumerated.Throws.Types.Select(static type => type.Name),
                Does.Contain("InvalidOperationException"));
            Assert.That(
                returned.Throws.Types.Select(static type => type.Name),
                Does.Contain("InvalidOperationException"));
        }
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

    private static string BuildDeferredSource(
        string additionalUsing,
        string deferredMethod)
    {
        return $$"""
            using System;
            {{additionalUsing}}

            public static class Sample {
                private static int state;

                {{deferredMethod}}

                public static void Run() {
                    _ = Deferred();
                    state++;
                }
            }
            """;
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
