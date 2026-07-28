using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class RequiresAndControlTests {
    [Test]
    public async Task CompilerBoundFalseRequiresIsReportedAfterConcreteReplay() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
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
        Assert.That(
            diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("false"));
    }

    [Test]
    public async Task UnknownInvocationArgumentAndEnsuresAbstainSilently() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
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
            ["SP0027"]);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task NonCompletingCallPrefixCannotProduceARefutation() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
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

                private static class Initialization {
                    static Initialization() => throw new System.Exception();
                    internal static int Identity(int value) => value;
                }

                private static class StaticTarget {
                    static StaticTarget() => throw new System.Exception();
                    internal static void Positive(int value) {
                        Contract.Requires(value > 0);
                    }
                }

                private sealed class ConstructedTarget {
                    static ConstructedTarget() => throw new System.Exception();
                    internal ConstructedTarget(int value) {
                        Contract.Requires(value > 0);
                    }
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

                public static void TypeInitializerPriorStatement() {
                    _ = Initialization.Identity(0);
                    Positive(0, -1);
                }

                public static void TargetTypeInitializer() {
                    StaticTarget.Positive(-1);
                }

                public static void ConstructorTypeInitializer() {
                    new ConstructedTarget(-1);
                }
            }
            """,
            "contracts",
            ["SP0027"]);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task AllNormallyEvaluatedArgumentsCanProduceARefutation() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
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
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
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
    public async Task DirectLocalInitializersAndAssignmentsReplayPreconditions() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            #nullable enable
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static void LocalInitializer() {
                    var first = Positive(-1);
                }

                public static void LocalAssignment() {
                    var first = 0;
                    first = Positive(-2);
                }

                public static void DiscardAssignment() {
                    _ = Positive(-3);
                }

                public static void PotentiallyThrowingTarget(int[]? values) {
                    values[0] = Positive(-4);
                }
            }
            """,
            "contracts",
            ["SP0027"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0027", "SP0027", "SP0027"]));
    }

    [Test]
    public async Task ExpressionBodiedPropertiesReplayConcretePreconditions() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public sealed class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Property => Positive(-1);
                public int this[int index] => Positive(-2);
            }
            """,
            "contracts",
            ["SP0027"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0027", "SP0027"]));
    }

    [Test]
    public async Task ConstructorInitializersReplayConcretePreconditions() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public class Base {
                protected Base(int value) {
                    Contract.Requires(value > 0);
                }
            }

            public sealed class Derived : Base {
                public Derived() : base(-1) {
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
    public async Task ImplicitThisReceiverReplaysConcretePreconditions() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public sealed class Fixture {
                private int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public int Call() => Positive(-1);
            }
            """,
            "contracts",
            ["SP0027"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0027"]));
    }

    [Test]
    public async Task DirectClauseSourceDoesNotMixInCompanionPreconditions() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                public static int Positive(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() == value);
                    return value;
                }

                public static void Call() {
                    Positive(-1);
                }
            }

            [ContractFor(typeof(Fixture))]
            public static class FixtureContracts {
                public static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """,
            "contracts",
            ["SP0027"]);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task UnsupportedCallableAbstainsSilently() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
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
    public async Task UnsupportedCallableStillReportsMalformedAttributes() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using System.Threading.Tasks;
            using SharpProof.Attributes;

            public static class Fixture {
                [AllowedCapabilities((SharpProofCapability)(1 << 30))]
                [AllowedExceptions(typeof(string))]
                [AllowedExceptions(typeof(int))]
                public static async Task Unsupported() {
                    await Task.Yield();
                }
            }
            """,
            "effects",
            ["SP0024"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0024", "SP0024", "SP0024"]));
    }

    [Test]
    public async Task UnsupportedCallableReportsEveryMalformedClosedContract() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System.Threading.Tasks;
            using SharpProof.Attributes;

            public static class Fixture {
                [return: Positive]
                public static async Task Unsupported(
                    [Positive] string text,
                    [NotNull] int count,
                    [InRange(5, 1)] int range) {
                    await Task.Yield();
                }

                [return: NotNull]
                public static async Task<string> Valid(
                    [NotNull] string text,
                    [Positive] int count,
                    [InRange(1, 5)] int range) {
                    await Task.Yield();
                    return text;
                }
            }
            """,
            "contracts",
            ["SP0024"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("SP0024", 4)));
    }

    [Test]
    public async Task BodylessDeclarationsReportEveryMalformedAttribute() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public interface IFixture {
                [AllowedExceptions(typeof(string))]
                [return: Positive]
                string InterfaceMethod(
                    [NotNull] int count,
                    [InRange(5, 1)] int range);
            }

            public abstract class Fixture {
                [SharpProofTrusted(" ")]
                public abstract void AbstractMethod();
            }
            """,
            "all-experimental",
            ["SP0024"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("SP0024", 5)));
    }

    [Test]
    public async Task EveryMisplacedContractClauseIsDiagnosed() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                public static void Valid(bool condition) {
                    Contract.Requires(condition);
                    Contract.Ensures(condition);
                    Contract.Assume(condition);
                    _ = condition;
                }

                public static void Conditional(bool condition) {
                    if (condition) {
                        Contract.Requires(condition);
                    }
                }

                public static void Late(bool condition) {
                    _ = condition;
                    Contract.Ensures(condition);
                }

                public static void Nested(bool condition) {
                    void Local() {
                        Contract.Assume(condition);
                    }
                    Local();
                }

                public static void Unreachable(bool condition) {
                    return;
                    Contract.Requires(condition);
                }

                public static void Misplaced(bool condition) {
                    {
                        Contract.Ensures(condition);
                    }
                }
            }
            """,
            "contracts",
            ["SP0024"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("SP0024", 5)));
        Assert.That(
            diagnostics.Select(diagnostic =>
                diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Has.All.Contain("<placement>"));
    }

    [Test]
    public async Task SuppressionOnlyChangesReportingAndTrustDoesNotSharpen() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
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
            diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("TrustedWithoutSummary"));
    }

    [Test]
    public async Task EmptyControlReasonsReportUsageAndDoNotSuppress() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
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
