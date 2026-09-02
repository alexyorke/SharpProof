namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ModuleInitializerOrderingRegressionTests
{
    private static readonly string[] FirstExceptionOnly = ["FirstException"];

    [Test]
    public void TerminalInitializerSuppressesLaterInitializerAndEntryEffects()
    {
        var compilation = CreateTerminalCompilation();
        var session = new EffectAnalysisSession(compilation);

        var first = session.Analyze(EffectTestHost.RequireMethod(
            compilation,
            "Startup",
            "ZFirst"));
        var second = session.Analyze(EffectTestHost.RequireMethod(
            compilation,
            "Startup",
            "ASecond"));
        var entry = session.Analyze(EffectTestHost.SampleMethod(compilation, "Entry"));

        AssertOnlyFirstInitializerEffects(first);
        AssertOnlyFirstInitializerEffects(second);
        AssertOnlyFirstInitializerEffects(entry);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.DirectWitnesses, Is.Empty);
            Assert.That(entry.DirectWitnesses, Is.Empty);
        }
    }

    [Test]
    public void ConditionallyThrowingInitializerStillPermitsLaterEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Runtime.CompilerServices;

            internal static class Startup {
                private static bool s_stop;
                private static volatile int s_state;

                [ModuleInitializer]
                internal static void ZMaybeStops() {
                    if (s_stop) {
                        throw new FirstException();
                    }
                }

                [ModuleInitializer]
                internal static void ASecond() => s_state = 1;
            }

            internal sealed class FirstException : Exception {
            }

            public static class Sample {
                public static void Entry() {
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var first = session.Analyze(EffectTestHost.RequireMethod(
            compilation,
            "Startup",
            "ZMaybeStops"));
        var second = session.Analyze(EffectTestHost.RequireMethod(
            compilation,
            "Startup",
            "ASecond"));
        var entry = session.Analyze(EffectTestHost.SampleMethod(compilation, "Entry"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Summary.Writes.IsEmpty, Is.True);
            Assert.That(first.Summary.Capabilities.IsEmpty, Is.True);
            Assert.That(
                second.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                second.Summary.Capabilities.Contains(
                    EffectCapabilityKind.Synchronization),
                Is.True);
            Assert.That(
                entry.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                entry.Summary.Capabilities.Contains(
                    EffectCapabilityKind.Synchronization),
                Is.True);
            Assert.That(
                second.Summary.Throws.Types.Select(
                    static type => type.Name),
                Is.EqualTo(FirstExceptionOnly));
            Assert.That(
                entry.Summary.Throws.Types.Select(
                    static type => type.Name),
                Is.EqualTo(FirstExceptionOnly));
            Assert.That(second.DirectWitnesses, Is.Empty);
            Assert.That(entry.DirectWitnesses, Is.Empty);
        }
    }

    [Test]
    public void AnalyzeAllUsesTheSameOrderedTerminalInitializerPrefix()
    {
        var compilation = CreateTerminalCompilation();

        var results = new EffectAnalysisSession(compilation).AnalyzeAll();

        foreach (var methodName in new[] { "ZFirst", "ASecond", "Entry" })
        {
            AssertOnlyFirstInitializerEffects(results.Single(result =>
                result.Method.Name == methodName));
        }
    }

    private static CSharpCompilation CreateTerminalCompilation()
    {
        return EffectTestHost.CreateCompilation(
            [
                CSharpSyntaxTree.ParseText(
                    """
                    using System;
                    using System.Runtime.CompilerServices;

                    internal static partial class Startup {
                        [ModuleInitializer]
                        internal static void ZFirst() =>
                            throw new FirstException();
                    }

                    internal sealed class FirstException : Exception {
                    }
                    """,
                    CSharpParseOptions.Default.WithLanguageVersion(
                        LanguageVersion.CSharp12),
                    path: "ZFirst.cs"),
                CSharpSyntaxTree.ParseText(
                    """
                    using System;
                    using System.Runtime.CompilerServices;

                    internal static partial class Startup {
                        private static volatile int s_state;

                        [ModuleInitializer]
                        internal static void ASecond() {
                            s_state = 1;
                            throw new SecondException();
                        }
                    }

                    internal sealed class SecondException : Exception {
                    }

                    public static class Sample {
                        private static int s_entryState;

                        public static void Entry() => s_entryState = 1;
                    }
                    """,
                    CSharpParseOptions.Default.WithLanguageVersion(
                        LanguageVersion.CSharp12),
                    path: "ASecond.cs")
            ]);
    }

    private static void AssertOnlyFirstInitializerEffects(
        EffectMethodResult result)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed),
                result.Method.Name);
            Assert.That(result.Summary.Writes.IsEmpty,
                Is.True,
                result.Method.Name);
            Assert.That(result.Summary.Capabilities.IsEmpty,
                Is.True,
                result.Method.Name);
            Assert.That(
                result.Summary.Throws.Types.Select(
                    static type => type.Name),
                Is.EqualTo(FirstExceptionOnly),
                result.Method.Name);
            Assert.That(result.Summary.Termination,
                Is.EqualTo(EffectTermination.Unknown),
                result.Method.Name);
            Assert.That(result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete),
                result.Method.Name);
        }
    }
}
