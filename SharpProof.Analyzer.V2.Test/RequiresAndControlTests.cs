using NUnit.Framework;

namespace SharpProof.Analyzer.V2.Test;

[TestFixture]
public sealed class RequiresAndControlTests {
    [Test]
    public async Task CompilerBoundFalseRequiresIsReportedAfterConcreteReplay() {
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                public static void Positive(int value) {
                    Contract.Requires(value > 0);
                    Contract.Ensures(UnsupportedPostcondition());
                }

                private static bool UnsupportedPostcondition() => false;

                public static void Call() {
                    Positive(-1);
                }
            }
            """,
            "contracts",
            ["SP0027"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0027"]));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("false"));
    }

    [Test]
    public async Task UnknownInvocationArgumentAndEnsuresAbstainSilently() {
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                public static void Positive(int value) {
                    Contract.Requires(value > 0);
                    Contract.Ensures(false);
                }

                private static int Unknown() => -1;

                public static void Call() {
                    Positive(Unknown());
                }
            }
            """,
            "contracts",
            ["SP0018", "SP0019", "SP0027", "SP0028"]);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task NonCompletingCallPrefixCannotProduceARefutation() {
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            """
            #nullable enable
            using SharpProof.Attributes;

            public static class Fixture {
                private sealed class Receiver {
                    public void Positive(int ignored, int value) {
                        Contract.Requires(value > 0);
                    }

                    public int Identity(int value) => value;
                }

                private static void Positive(int ignored, int value) {
                    Contract.Requires(value > 0);
                }

                public static void ThrowingEarlierArgument() {
                    Positive(((string)null!).Length, -1);
                }

                public static void NullReceiver() {
                    ((Receiver)null!).Positive(0, -1);
                }

                public static void ThrowingPriorStatement() {
                    var zero = 0;
                    _ = 1 / zero;
                    Positive(0, -1);
                }

                public static void NullReceiverPriorStatement() {
                    Receiver receiver = null!;
                    _ = receiver.Identity(0);
                    Positive(0, -1);
                }
            }
            """,
            "contracts",
            ["SP0027"]);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task AllNormallyEvaluatedArgumentsCanProduceARefutation() {
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static void Positive(int ignored, int value) {
                    Contract.Requires(value > 0);
                }

                public static void Call() {
                    Positive(1, -1);
                }
            }
            """,
            "contracts",
            ["SP0027"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0027"]));
    }

    [Test]
    public async Task DefinitelyNonThrowingSourcePrefixPreservesRefutation() {
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Identity(int value) => value;

                private static void Positive(int value) {
                    Contract.Requires(value > 0);
                }

                public static void Call(int value) {
                    var probe = Identity(value);
                    _ = probe;
                    Positive(-1);
                }
            }
            """,
            "contracts",
            ["SP0027"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0027"]));
    }

    [Test]
    public async Task UnsupportedCallableAbstainsSilently() {
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            """
            using System.Threading.Tasks;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int state;

                [EnforcePure]
                public static async Task Unsupported() {
                    state = 1;
                    await Task.Yield();
                }
            }
            """,
            "effects",
            ["SP0002"]);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task SuppressionOnlyChangesReportingAndTrustDoesNotSharpen() {
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int state;

                [SharpProofSuppress("reviewed elsewhere")]
                [EnforcePure]
                public static void Suppressed() {
                    state = 1;
                }

                [SharpProofTrusted("reviewed implementation")]
                [EnforcePure]
                public static void TrustedWithoutSummary() {
                    state = 2;
                }
            }
            """,
            "effects",
            ["SP0002", "SP0024"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0002"]));
        Assert.That(
            diagnostics[0].GetMessage(),
            Does.Contain("TrustedWithoutSummary"));
    }

    [Test]
    public async Task EmptyControlReasonsReportUsageAndDoNotSuppress() {
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int state;

                [SharpProofSuppress("")]
                [SharpProofTrusted("")]
                [EnforcePure]
                public static void InvalidReasons() {
                    state = 1;
                }
            }
            """,
            "effects",
            ["SP0002", "SP0024"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EquivalentTo(["SP0002", "SP0024", "SP0024"]));
    }
}
