using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class MemberInitializerConstructorPathRegressionTests
{
    [Test]
    public async Task ReachableConstructorKeepsInitializerViolationVisible()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            CreateSource(
                """
                public Subject() {
                    Contract.Requires(false);
                }

                public Subject(int marker) {
                }
                """),
            "contracts",
            []);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0027");
    }

    [Test]
    public async Task ThisDelegatingConstructorDoesNotReplayInitializerWhenRootIsSuppressed()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            CreateSource(
                """
                [SharpProofSuppress("reviewed root constructor")]
                public Subject() {
                }

                public Subject(int marker) : this() {
                }
                """),
            "contracts",
            ["SP0027"]);

        Assert.That(diagnostics, Is.Empty);
    }

    private static string CreateSource(string constructors)
    {
        return $$"""
        using SharpProof.Attributes;

        public static class Guard {
            public static int RequireNull(object value) {
                Contract.Requires(value == null);
                return 0;
            }
        }

        public sealed class Subject {
            private int _value = Guard.RequireNull(new object());

        {{constructors}}
        }
        """;
    }
}
