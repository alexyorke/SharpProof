namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ImplicitConstructorInitializerRegressionTests
{
    [Test]
    public void ImplicitScalarFieldInitializerMatchesExplicitConstructor()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class Child { }

            public sealed class ImplicitInit {
                public int Value = 5;
            }

            public sealed class ExplicitInit {
                public int Value = 5;
                public ExplicitInit() { }
            }

            public sealed class ImplicitArrayInit {
                public int[] Values = new int[] { 1 };
            }

            public sealed class ExplicitArrayInit {
                public int[] Values = new int[] { 1 };
                public ExplicitArrayInit() { }
            }

            public sealed class ImplicitObjectInit {
                public Child Value = new Child();
            }

            public sealed class ExplicitObjectInit {
                public Child Value = new Child();
                public ExplicitObjectInit() { }
            }

            public static class Thrower {
                public static int Fail() =>
                    throw new InvalidOperationException();
            }

            public sealed class ImplicitThrowInit {
                public int Value = Thrower.Fail();
            }

            public sealed class ExplicitThrowInit {
                public int Value = Thrower.Fail();
                public ExplicitThrowInit() { }
            }

            public sealed class Observer {
                public int Value;
            }

            public sealed class PrimaryInit(int value) {
                public int Value = value;
            }

            public static class Sample {
                public static ImplicitInit Implicit() => new();
                public static ExplicitInit Explicit() => new();
                public static ImplicitArrayInit ImplicitArray() => new();
                public static ExplicitArrayInit ExplicitArray() => new();
                public static ImplicitObjectInit ImplicitObject() => new();
                public static ExplicitObjectInit ExplicitObject() => new();
                public static ImplicitThrowInit ImplicitThrow() => new();
                public static ExplicitThrowInit ExplicitThrow() => new();
                public static void AfterThrow(Observer observer) {
                    _ = new ImplicitThrowInit();
                    observer.Value++;
                }
                public static PrimaryInit Primary(int value) => new(value);
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        foreach (var (implicitMethod, explicitMethod) in new[]
                 {
                     ("Implicit", "Explicit"),
                     ("ImplicitArray", "ExplicitArray"),
                     ("ImplicitObject", "ExplicitObject"),
                     ("ImplicitThrow", "ExplicitThrow")
                 })
        {
            var implicitResult = session.Analyze(
                EffectTestHost.SampleMethod(compilation, implicitMethod));
            var explicitResult = session.Analyze(
                EffectTestHost.SampleMethod(compilation, explicitMethod));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    implicitResult.Summary.Completeness,
                    Is.EqualTo(explicitResult.Summary.Completeness),
                    implicitMethod);
                Assert.That(
                    implicitResult.Summary.Uncertainty &
                        (EffectUncertainty.UnmodeledCall |
                         EffectUncertainty.UnsupportedOperation),
                    Is.EqualTo(EffectUncertainty.None),
                    implicitMethod);
                Assert.That(
                    implicitResult.Summary.Writes.IsUnknown,
                    Is.False,
                    implicitMethod);
                if (implicitMethod == "ImplicitThrow")
                {
                    Assert.That(
                        implicitResult.Summary.Throws.Types.Select(
                            static type => type.ToDisplayString()),
                        Does.Contain("System.InvalidOperationException"));
                    Assert.That(
                        implicitResult.Summary.Throws.IncludesUnknown,
                        Is.False);
                }
            }
        }

        var afterThrow = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "AfterThrow"))
            .Summary;
        Assert.That(
            afterThrow.Writes.Contains(EffectRegionId.Parameter(0)),
            Is.False,
            "the throwing field initializer prevents later caller writes");

        var primary = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "Primary"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                primary.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                primary.Summary.Uncertainty & EffectUncertainty.UnsupportedOperation,
                Is.EqualTo(EffectUncertainty.None));
            Assert.That(primary.Summary.Writes.IsUnknown, Is.False);
        }
    }

    [Test]
    public void PrimaryConstructorFieldInitializerIsModeled()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class PrimaryInit(int value) {
                public int Value = value;
            }

            public static class Sample {
                public static PrimaryInit Primary(int value) => new(value);
            }
            """);
        var summary = new EffectAnalysisSession(compilation)
            .Analyze(EffectTestHost.SampleMethod(compilation, "Primary"))
            .Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                summary.Uncertainty & EffectUncertainty.UnsupportedOperation,
                Is.EqualTo(EffectUncertainty.None));
            Assert.That(summary.Writes.IsUnknown, Is.False);
        }
    }
}
