namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class TerminalObjectInitializerEffectTests
{
    [Test]
    public void NonCompletingArgumentRetainsInitializerWrite()
    {
        var summary = AnalyzeConstructor("TerminalHolder");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.Writes.IsUnknown, Is.False);
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
        }
    }

    [Test]
    public void ExternalExceptionRetainsInitializerArgumentThrow()
    {
        var summary = AnalyzeConstructor("ExternalExceptionHolder");
        var exceptions = summary.Throws.Types
            .Select(static type => type.ToDisplayString())
            .ToArray();

        Assert.That(
            exceptions,
            Does.Contain("System.ApplicationException"));
    }

    private static EffectSummary AnalyzeConstructor(string typeName)
    {
        var metadataException = EffectTestHost.EmitReference(
            """
            public sealed class MetadataException : System.Exception {
                public MetadataException() {
                }
            }
            """,
            "TerminalObjectInitializerMetadata");
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class EffectState {
                public static int Value;
            }

            public sealed class TerminalInitializer {
                public int Value { get; set; }
            }

            public sealed class TerminalHolder {
                private readonly TerminalInitializer _value =
                    new TerminalInitializer { Value = WriteThenFail() };

                private static int WriteThenFail() {
                    EffectState.Value++;
                    throw new ApplicationException();
                }

                public TerminalHolder() {
                }
            }

            public sealed class ExternalExceptionHolder {
                private readonly object _value = true
                    ? throw new MetadataException {
                        HelpLink = Fail()
                    }
                    : new object();

                private static string Fail() =>
                    throw new ApplicationException();

                public ExternalExceptionHolder() {
                }
            }
            """,
            metadataException);
        return new EffectAnalysisSession(compilation)
            .Analyze(EffectTestHost.RequireType(compilation, typeName)
                .InstanceConstructors
                .Single(static constructor =>
                    !constructor.IsImplicitlyDeclared))
            .Summary;
    }
}
