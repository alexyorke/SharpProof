namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class CatchFinallyUsingDisposalRegressionTests
{
    [Test]
    public void UsingInsideCatchAndFinallyPreservesDisposeEffects()
    {
        var compilation = CreateCompilation();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                EffectTestHost.HasStaticWrite(
                    compilation,
                    EffectTestHost.SampleMethod(compilation, "CatchStatement")),
                Is.True,
                "using statement in a typed catch");
            Assert.That(
                EffectTestHost.HasStaticWrite(
                    compilation,
                    EffectTestHost.SampleMethod(compilation, "CatchDeclaration")),
                Is.True,
                "using declaration in a bare catch");
            Assert.That(
                EffectTestHost.HasStaticWrite(
                    compilation,
                    EffectTestHost.SampleMethod(compilation, "FinallyStatement")),
                Is.True,
                "using statement in finally");
            Assert.That(
                EffectTestHost.HasStaticWrite(
                    compilation,
                    EffectTestHost.SampleMethod(compilation, "FinallyDeclaration")),
                Is.True,
                "using declaration in finally");
            Assert.That(
                EffectTestHost.HasStaticWrite(
                    compilation,
                    EffectTestHost.SampleMethod(compilation, "AfterHandledCatch")),
                Is.True,
                "using after a normally completing catch");
        }
    }

    [Test]
    public void ThrowingDisposeInsideFinallyPreservesThrownEffect()
    {
        var result = EffectTestHost.AnalyzeSample(
            CreateCompilation(),
            "ThrowingDisposeInFinally");

        Assert.That(
            result.Summary.Throws.Types.Select(
                static type => type.ToDisplayString()),
            Does.Contain("System.InvalidOperationException"));
    }

    [Test]
    public void MismatchedCatchDoesNotIncludeUsingDisposeEffects()
    {
        var compilation = CreateCompilationWithMismatchedCatch();

        Assert.That(
            EffectTestHost.HasStaticWrite(
                compilation,
                EffectTestHost.SampleMethod(compilation, "MismatchedCatch")),
            Is.False);
    }

    private static CSharpCompilation CreateCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sink
            {
                public static int Count;
            }

            public static class Helper
            {
                public static void Touch()
                {
                    Sink.Count++;
                }
            }

            public sealed class WritingResource : IDisposable
            {
                public void Dispose()
                {
                    Helper.Touch();
                }
            }

            public sealed class ThrowingResource : IDisposable
            {
                public void Dispose()
                {
                    throw new InvalidOperationException();
                }
            }

            public static class Sample
            {
                public static void CatchStatement()
                {
                    try
                    {
                        throw new InvalidOperationException();
                    }
                    catch (InvalidOperationException)
                    {
                        using (new WritingResource()) { }
                    }
                }

                public static void CatchDeclaration()
                {
                    try
                    {
                        throw new InvalidOperationException();
                    }
                    catch
                    {
                        using var resource = new WritingResource();
                    }
                }

                public static int FinallyStatement()
                {
                    try
                    {
                        return 1;
                    }
                    finally
                    {
                        using (new WritingResource()) { }
                    }
                }

                public static int FinallyDeclaration()
                {
                    try
                    {
                        return 1;
                    }
                    finally
                    {
                        using var resource = new WritingResource();
                    }
                }

                public static void AfterHandledCatch()
                {
                    try
                    {
                        throw new InvalidOperationException();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    using (new WritingResource()) { }
                }

                public static int ThrowingDisposeInFinally()
                {
                    try
                    {
                        return 1;
                    }
                    finally
                    {
                        using (new ThrowingResource()) { }
                    }
                }
            }
            """);
    }

    private static CSharpCompilation CreateCompilationWithMismatchedCatch()
    {
        return EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sink
            {
                public static int Count;
            }

            public sealed class Resource : IDisposable
            {
                public void Dispose()
                {
                    Sink.Count++;
                }
            }

            public static class Sample
            {
                public static void MismatchedCatch()
                {
                    try
                    {
                        throw new InvalidOperationException();
                    }
                    catch (ArgumentException)
                    {
                        using (new Resource()) { }
                    }
                }
            }
            """);
    }
}
