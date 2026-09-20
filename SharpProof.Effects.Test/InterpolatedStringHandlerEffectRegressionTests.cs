namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class InterpolatedStringHandlerEffectRegressionTests
{
    [Test]
    public void HandlerConstructorWritesFlowThroughDirectAndHelperCalls()
    {
        var compilation = CreateCompilation();
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            foreach (var methodName in new[] { "DirectWrite", "HelperWrite" })
            {
                Assert.That(
                    session.Analyze(Method(compilation, methodName))
                        .Summary.Writes.Contains(EffectRegionId.Static()),
                    Is.True,
                    methodName);
            }
        }
    }

    [Test]
    public void HandlerConstructorThrowsFlowThroughDirectAndHelperCalls()
    {
        var compilation = CreateCompilation();
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            foreach (var methodName in new[] { "DirectThrow", "HelperThrow" })
            {
                var summary = session.Analyze(Method(compilation, methodName))
                    .Summary;
                Assert.That(
                    summary.Throws.Types.Select(static type =>
                        type.ToDisplayString()),
                    Does.Contain("System.InvalidOperationException"),
                    methodName);
            }
        }
    }

    [Test]
    public void HandlerConstructorAllocationsFlowThroughDirectAndHelperCalls()
    {
        var compilation = CreateCompilation();
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            foreach (var methodName in new[] { "DirectAllocate", "HelperAllocate" })
            {
                Assert.That(
                    session.Analyze(Method(compilation, methodName))
                        .Summary.Allocation,
                    Is.EqualTo(EffectAllocationKind.Managed),
                    methodName);
            }
        }
    }

    private static CSharpCompilation CreateCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Runtime.CompilerServices;

            public static class State {
                public static int Value;
            }

            [InterpolatedStringHandler]
            public ref struct WriteHandler {
                public WriteHandler(int literalLength, int formattedCount) {
                    State.Value++;
                }

                public void AppendLiteral(string value) { }
                public void AppendFormatted<T>(T value) { }
            }

            [InterpolatedStringHandler]
            public ref struct ThrowHandler {
                public ThrowHandler(int literalLength, int formattedCount) {
                    throw new InvalidOperationException();
                }

                public void AppendLiteral(string value) { }
                public void AppendFormatted<T>(T value) { }
            }

            [InterpolatedStringHandler]
            public ref struct AllocateHandler {
                public AllocateHandler(int literalLength, int formattedCount) {
                    _ = new object();
                }

                public void AppendLiteral(string value) { }
                public void AppendFormatted<T>(T value) { }
            }

            public static class Sample {
                private static int TakeWrite(ref WriteHandler handler) => 0;
                private static int TakeThrow(ref ThrowHandler handler) => 0;
                private static int TakeAllocate(ref AllocateHandler handler) => 0;

                public static int DirectWrite(int value) =>
                    TakeWrite($"value={value}");
                public static int HelperWrite(int value) => DirectWrite(value);

                public static int DirectThrow(int value) =>
                    TakeThrow($"value={value}");
                public static int HelperThrow(int value) => DirectThrow(value);

                public static int DirectAllocate(int value) =>
                    TakeAllocate($"value={value}");
                public static int HelperAllocate(int value) => DirectAllocate(value);
            }
            """);
    }

    private static IMethodSymbol Method(
        Compilation compilation,
        string methodName)
    {
        return EffectTestHost.SampleMethod(compilation, methodName);
    }
}
