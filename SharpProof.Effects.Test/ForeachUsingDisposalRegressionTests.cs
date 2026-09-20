namespace SharpProof.Effects.Test;

using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

[TestFixture]
public sealed class ForeachUsingDisposalRegressionTests
{
    [Test]
    public void ForeachCleanupIsNotMistakenForUsingCleanup()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class Resource : IDisposable {
                public void Dispose() { }
            }

            public sealed class Items {
                public Enumerator GetEnumerator() => new();

                public ref struct Enumerator {
                    public int Current => 0;
                    public bool MoveNext() => false;
                    public void Dispose() { }
                }
            }

            public static class Sample {
                public static Resource Create() => new();

                public static void Run() {
                    using (Create()) {
                        foreach (var item in new Items()) {
                            _ = item;
                        }
                    }
                }

                public static void Declaration() {
                    using var resource = Create();
                    foreach (var item in new Items()) {
                        _ = item;
                    }
                }
            }
            """);
        foreach (var methodName in new[] { "Run", "Declaration" })
        {
            var method = EffectTestHost.SampleMethod(compilation, methodName);
            var syntax = method.DeclaringSyntaxReferences.Single().GetSyntax();
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            var body = (IMethodBodyOperation)model.GetOperation(syntax)!;
            var disposals = ControlFlowGraph.Create(body).Blocks
                .SelectMany(static block => block.Operations)
                .SelectMany(static operation => operation.DescendantsAndSelf())
                .OfType<IInvocationOperation>()
                .Where(static invocation =>
                    invocation.IsImplicit &&
                    invocation.TargetMethod.Name == "Dispose")
                .ToArray();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(disposals, Has.Length.EqualTo(2), methodName);
                Assert.That(
                    disposals.Count(UsingDisposalEffectResolver
                        .IsSynthesizedSynchronousDispose),
                    Is.EqualTo(1),
                    methodName);
            }
        }
    }

    [Test]
    public void ForeachEnumeratorDisposeInsideUsingIsIncluded()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sink {
                public static int Count;
            }

            public sealed class Resource : IDisposable {
                public void Dispose() { }
            }

            public sealed class Items {
                public Enumerator GetEnumerator() => new();

                public ref struct Enumerator {
                    public int Current => 0;
                    public bool MoveNext() => false;
                    public void Dispose() {
                        Sink.Count++;
                        throw new ApplicationException();
                    }
                }
            }

            public static class Sample {
                public static void Run() {
                    using (new Resource()) {
                        foreach (var item in new Items()) {
                            _ = item;
                        }
                    }
                }
            }
            """);
        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.SampleMethod(compilation, "Run"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "enumerator Dispose write");
            Assert.That(
                result.Summary.Throws.Types.Select(
                    static type => type.ToDisplayString()),
                Does.Contain("System.ApplicationException"),
                "enumerator Dispose exception");
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void ForeachEnumeratorDisposeInUsingResourceExpressionIsIncluded()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sink {
                public static int Count;
            }

            public sealed class Resource : IDisposable {
                public void Dispose() { }
            }

            public sealed class Items {
                public Enumerator GetEnumerator() => new();

                public ref struct Enumerator {
                    public int Current => 0;
                    public bool MoveNext() => false;
                    public void Dispose() {
                        Sink.Count++;
                        throw new ApplicationException();
                    }
                }
            }

            public static class Sample {
                public static Resource Create(Func<Resource> factory) => factory();

                public static void Run() {
                    using (Create(() => {
                        foreach (var item in new Items()) {
                            _ = item;
                        }
                        return new Resource();
                    })) { }
                }
            }
            """);
        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.SampleMethod(compilation, "Run"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "enumerator Dispose write");
            Assert.That(
                result.Summary.Throws.IncludesUnknown,
                Is.True,
                "delegate invocation remains conservatively unknown");
        }
    }
}
