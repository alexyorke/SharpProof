namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ModuleInitializerEffectTests
{
    [Test]
    public void SourceModuleInitializerEffectsPrecedeOrdinaryEntryPoints()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Runtime.CompilerServices;

            internal static class Startup {
                private static volatile int s_state;

                [ModuleInitializer]
                internal static void Initialize() {
                    s_state = 1;
                    throw new InvalidOperationException();
                }
            }

            public static class Sample {
                public static void Entry() {
                }

                public static void CallsHelper() => Helper();

                private static void Helper() {
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] { "Entry", "CallsHelper" })
        {
            var result = session.Analyze(
                EffectTestHost.RequireMethod(
                    compilation,
                    "Sample",
                    methodName));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Summary.Writes.Contains(
                        EffectRegionId.Static()),
                    Is.True,
                    methodName);
                Assert.That(
                    result.Summary.Capabilities.Contains(
                        EffectCapabilityKind.Synchronization),
                    Is.True,
                    methodName);
                Assert.That(
                    result.Summary.Throws.Types.Select(
                        static type => type.ToDisplayString()),
                    Does.Contain("System.InvalidOperationException"),
                    methodName);
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    methodName);
            }
        }
    }

    [Test]
    public void ThrowingModuleInitializerBlocksDirectBodyWitnesses()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Runtime.CompilerServices;

            internal static class Startup {
                [ModuleInitializer]
                internal static void Initialize() =>
                    throw new InvalidOperationException();
            }

            public static class Sample {
                public static object Allocate() => new object();
            }
            """);

        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                "Allocate"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Throws.Types.Select(
                    static type => type.ToDisplayString()),
                Does.Contain("System.InvalidOperationException"));
            Assert.That(result.DirectWitnesses, Is.Empty);
        }
    }

    [Test]
    public void TypeInitializersRemainTypeScopedWithoutAModuleInitializer()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class HasTypeInitializer {
                static HasTypeInitializer() =>
                    throw new InvalidOperationException();

                public static void Trigger() {
                }
            }

            public static class Sample {
                public static void Entry() {
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var unrelated = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                "Entry"));
        var triggering = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "HasTypeInitializer",
                "Trigger"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unrelated.Summary.Writes.IsEmpty, Is.True);
            Assert.That(unrelated.Summary.Capabilities.IsEmpty, Is.True);
            Assert.That(unrelated.Summary.Throws.IsEmpty, Is.True);
            Assert.That(
                unrelated.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                triggering.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(
                triggering.Summary.Uncertainty &
                    EffectUncertainty.UnmodeledCall,
                Is.EqualTo(EffectUncertainty.UnmodeledCall));
        }
    }

    [Test]
    public void ModuleInitializerAnalysisDoesNotReenterThroughItsCallees()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System.Runtime.CompilerServices;

            internal static class Startup {
                private static volatile int s_state;

                [ModuleInitializer]
                internal static void Initialize() => Touch();

                private static void Touch() => s_state = 1;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var result = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Startup",
                "Initialize"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(
                    EffectRegionId.Static()),
                Is.True);
            Assert.That(
                result.Summary.Capabilities.Contains(
                    EffectCapabilityKind.Synchronization),
                Is.True);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                result.Summary.Uncertainty &
                    EffectUncertainty.Recursion,
                Is.EqualTo(EffectUncertainty.None));
            Assert.That(session.AnalyzedSourceMethodCount, Is.EqualTo(2));
        }
    }
}
