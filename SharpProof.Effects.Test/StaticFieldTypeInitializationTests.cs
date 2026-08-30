namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class StaticFieldTypeInitializationTests
{
    [Test]
    public void DivergingStaticConstructorPreventsMethodEntryAndBodyEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class DivergingInitialization {
                static DivergingInitialization() {
                    while (true) { }
                }

                public static void Run() {
                    Probe.Writes++;
                    _ = new object();
                }
            }

            public static class Probe {
                public static int Writes;
            }
            """);
        var method = EffectTestHost.RequireMethod(
            compilation,
            "DivergingInitialization",
            "Run");

        var result = new EffectAnalysisSession(compilation).Analyze(method);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Termination,
                Is.EqualTo(EffectTermination.MayDiverge));
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False);
            Assert.That(
                result.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.None));
            Assert.That(result.DirectWitnesses, Is.Empty);
        }
    }

    [Test]
    public void BeforeFieldInitStaticMethodIncludesInitializerEffectsAndFailure()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class InitializationEffects {
                internal static int Writes;

                internal static int Fail() {
                    Writes++;
                    throw new InvalidOperationException();
                }
            }

            public static class BeforeFieldInitTarget {
                private static readonly int Value =
                    InitializationEffects.Fail();

                public static void Run() { }
            }
            """);
        var method = EffectTestHost.RequireMethod(
            compilation,
            "BeforeFieldInitTarget",
            "Run");

        var result = new EffectAnalysisSession(compilation).Analyze(method);

        var exceptionTypes = result.Summary.Throws.Types.Select(static type =>
            type.ToDisplayString()).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(result.Summary.Throws.IncludesUnknown, Is.False);
            Assert.That(exceptionTypes, Has.Length.EqualTo(1));
            Assert.That(
                exceptionTypes,
                Does.Contain("System.TypeInitializationException"));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void DefinitelyFailingSourceInitializerThrowsTypeInitializationException()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class FailingInitialization {
                internal static int Value = Fail();

                private static int Fail() => throw new Exception();
            }

            public static class Sample {
                public static int Read() => FailingInitialization.Value;
            }
            """);
        var method = EffectTestHost.RequireMethod(compilation, "Sample", "Read");

        var result = new EffectAnalysisSession(compilation).Analyze(method);

        var exceptionTypes = result.Summary.Throws.Types.Select(static type =>
            type.ToDisplayString()).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Throws.IncludesUnknown, Is.False);
            Assert.That(exceptionTypes, Has.Length.EqualTo(1));
            Assert.That(
                exceptionTypes,
                Does.Contain("System.TypeInitializationException"));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }
}
