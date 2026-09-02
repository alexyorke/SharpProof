using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class MemberInitializerConstructorPathRegressionTests
{
    [Test]
    public async Task ReachableConstructorKeepsInitializerViolationVisible()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Guard {
                public static int RequireNull(object value) {
                    Contract.Requires(value == null);
                    return 0;
                }
            }

            public sealed class Subject {
                private int _value = Guard.RequireNull(new object());

                public Subject() {
                    Contract.Requires(false);
                }

                public Subject(int marker) {
                }
            }
            """,
            "contracts",
            ["SP0027"]);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0027");
    }

    [Test]
    public async Task ThisDelegatingConstructorDoesNotReplayInitializerWhenRootIsSuppressed()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Guard {
                public static int RequireNull(object value) {
                    Contract.Requires(value == null);
                    return 0;
                }
            }

            public sealed class Subject {
                private int _value = Guard.RequireNull(new object());

                [SharpProofSuppress("reviewed root constructor")]
                public Subject() {
                }

                public Subject(int marker) : this() {
                }
            }
            """,
            "contracts",
            ["SP0027"]);

        Assert.That(diagnostics, Is.Empty);
    }
}
