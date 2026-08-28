using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class RequiresReplaySoundnessTests
{
    [Test]
    public async Task SynthesizedExtensionAliasesUseCallEntryState()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Extensions {
                public static void EqualRef(
                    this ref int receiver,
                    int second) {
                    Contract.Requires(receiver == second);
                }

                public static void DifferentRef(
                    this ref int receiver,
                    int second) {
                    Contract.Requires(receiver != second);
                }

                public static void EqualIn(
                    this in int receiver,
                    int second) {
                    Contract.Requires(receiver == second);
                }

                public static void DifferentIn(
                    this in int receiver,
                    int second) {
                    Contract.Requires(receiver != second);
                }
            }

            public static class Fixture {
                private static int Field;

                public static void ReducedRefSatisfied() {
                    var value = 1;
                    value.EqualRef(value = 2);
                }

                public static void ReducedRefViolated() {
                    var value = 1;
                    value.DifferentRef(value = 2);
                }

                public static void ReducedInSatisfied() {
                    var value = 1;
                    value.EqualIn(value = 2);
                }

                public static void ReducedInViolated() {
                    var value = 1;
                    value.DifferentIn(value = 2);
                }

                public static void ReducedNamedLaterMutationSatisfied() {
                    var value = 1;
                    value.EqualRef(second: value = 2);
                }

                public static void ParameterReceiverSatisfied(int value) {
                    value.EqualRef(value = 2);
                }

                public static void ExplicitStaticRefSatisfied() {
                    var value = 1;
                    Extensions.EqualRef(ref value, value = 2);
                }

                public static void ExplicitStaticNamedSatisfied() {
                    var value = 1;
                    Extensions.EqualRef(
                        second: value = 2,
                        receiver: ref value);
                }

                public static void ExplicitStaticInSnapshotViolated() {
                    var value = 1;
                    Extensions.EqualIn(value, value = 2);
                }

                public static void NonLocalReducedAliasIsUnknown() {
                    Field = 1;
                    Field.EqualRef(Field = 2);
                }
            }
            """,
            "contracts",
            ["SP0027"],
            new SharpProofAnalyzer(factory));

        var messages = diagnostics.Select(static diagnostic =>
            diagnostic.GetMessage(CultureInfo.InvariantCulture)).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(messages, Has.Length.EqualTo(3));
            Assert.That(
                messages.Count(static message =>
                    message.StartsWith(
                        "Call to 'DifferentRef'",
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(
                messages.Count(static message =>
                    message.StartsWith(
                        "Call to 'DifferentIn'",
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(
                messages.Count(static message =>
                    message.StartsWith(
                        "Call to 'EqualIn'",
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(
                factory.Outcomes["ReducedRefSatisfied"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["ReducedRefViolated"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["ReducedInSatisfied"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["ReducedInViolated"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["ReducedNamedLaterMutationSatisfied"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["ParameterReceiverSatisfied"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["ExplicitStaticRefSatisfied"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["ExplicitStaticNamedSatisfied"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["ExplicitStaticInSnapshotViolated"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["NonLocalReducedAliasIsUnknown"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task ExplicitRefAndInAliasesUseCallEntryState()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Field;

                private static void EqualRef(
                    ref int first,
                    int second) {
                    Contract.Requires(first == second);
                }

                private static void DifferentRef(
                    ref int first,
                    int second) {
                    Contract.Requires(first != second);
                }

                private static void EqualIn(
                    in int first,
                    int second) {
                    Contract.Requires(first == second);
                }

                private static void DifferentIn(
                    in int first,
                    int second) {
                    Contract.Requires(first != second);
                }

                public static void RefSatisfied() {
                    var value = 1;
                    EqualRef(ref value, value = 2);
                }

                public static void RefViolated() {
                    var value = 1;
                    DifferentRef(ref value, value = 2);
                }

                public static void ExplicitInSatisfied() {
                    var value = 1;
                    EqualIn(in value, value = 2);
                }

                public static void ExplicitInViolated() {
                    var value = 1;
                    DifferentIn(in value, value = 2);
                }

                public static void ImplicitInSnapshotViolated() {
                    var value = 1;
                    EqualIn(value, value = 2);
                }

                public static void ImplicitInSnapshotSatisfied() {
                    var value = 1;
                    DifferentIn(value, value = 2);
                }

                public static void NamedRefAssignmentFirstSatisfied() {
                    var value = 1;
                    EqualRef(second: value = 2, first: ref value);
                }

                public static void NamedInAssignmentFirstViolated() {
                    var value = 1;
                    DifferentIn(second: value = 2, first: in value);
                }

                public static void NonLocalAliasIsUnknown() {
                    Field = 1;
                    EqualRef(ref Field, Field = 2);
                }
            }
            """,
            "contracts",
            ["SP0027"],
            new SharpProofAnalyzer(factory));

        var messages = diagnostics.Select(static diagnostic =>
            diagnostic.GetMessage(CultureInfo.InvariantCulture)).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(messages, Has.Length.EqualTo(4));
            Assert.That(
                messages.Count(static message =>
                    message.StartsWith(
                        "Call to 'DifferentRef'",
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(
                messages.Count(static message =>
                    message.StartsWith(
                        "Call to 'DifferentIn'",
                        StringComparison.Ordinal)),
                Is.EqualTo(2));
            Assert.That(
                messages.Count(static message =>
                    message.StartsWith(
                        "Call to 'EqualIn'",
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(
                factory.Outcomes["RefSatisfied"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["RefViolated"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["ExplicitInSatisfied"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["ExplicitInViolated"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["ImplicitInSnapshotViolated"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["ImplicitInSnapshotSatisfied"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["NamedRefAssignmentFirstSatisfied"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["NamedInAssignmentFirstViolated"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["NonLocalAliasIsUnknown"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task InRvaluesSnapshotButReadonlyAliasesUseCallEntryState()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static void RequireZero(
                    in int first,
                    int ignored) {
                    Contract.Requires(first == 0);
                }

                private static void RequireZeroReadonly(
                    ref readonly int first,
                    int ignored) {
                    Contract.Requires(first == 0);
                }

                public static void InRvalueSnapshot() {
                    var value = 0;
                    RequireZero(value + 0, value = 1);
                }

                public static void ExplicitInAlias() {
                    var value = 1;
                    RequireZero(in value, value = 0);
                }

                public static void ExplicitReadonlyAlias() {
                    var value = 1;
                    RequireZeroReadonly(in value, value = 0);
                }
            }
            """,
            "contracts",
            ["SP0027"],
            new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                factory.Outcomes["InRvalueSnapshot"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["ExplicitInAlias"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["ExplicitReadonlyAlias"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
        }
    }

    [Test]
    public async Task MutationBearingArgumentSnapshotCannotBeRecomputed()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static void RequireZero(int value) {
                    Contract.Requires(value == 0);
                }

                public static void Call() {
                    var value = 1;
                    RequireZero(value + (value = 0));
                }
            }
            """,
            "contracts",
            ["SP0027"],
            new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            factory.Outcomes["Call"],
            Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }

    [Test]
    public async Task ExpandedParamsElementsCannotStandInForTheArray()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static void RequireNull(
                    params string?[] values) {
                    Contract.Requires(values == null);
                }

                public static void Call() =>
                    RequireNull((string?)null);
            }
            """,
            "contracts",
            ["SP0027"],
            new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            factory.Outcomes["Call"],
            Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }

    [Test]
    public async Task StringDowncastRequiresUsesOnlyDefiniteRuntimeTypeEvidence()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static void NeedString(object value) {
                    Contract.Requires((string)value != null);
                }

                public static void StringValue() {
                    NeedString("value");
                }

                public static void NullValue() {
                    NeedString(null);
                }

                public static void NonStringValue() {
                    NeedString(new object());
                }

                public static void ObjectTypedStringValue() {
                    object value = "value";
                    NeedString(value);
                }
            }
            """,
            "contracts",
            ["SP0027"],
            new SharpProofAnalyzer(factory));

        var messages = diagnostics.Select(static diagnostic =>
            diagnostic.GetMessage(CultureInfo.InvariantCulture)).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(messages, Has.Length.EqualTo(1));
            Assert.That(
                messages[0],
                Does.StartWith("Call to 'NeedString'"));
            Assert.That(
                factory.Outcomes["StringValue"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["NullValue"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["NonStringValue"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["ObjectTypedStringValue"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task ProvenFailingExplicitCastDoesNotRefuteAFollowingCall()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static void Positive(string text, int value) {
                    Contract.Requires(value > 0);
                }

                public static void FailingCast() {
                    Positive((string)new object(), -1);
                }

                public static void ValidCast() {
                    Positive("text", -1);
                }
            }
            """,
            "contracts",
            ["SP0027"],
            new SharpProofAnalyzer(factory));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics, Has.Length.EqualTo(1));
            Assert.That(
                factory.Outcomes["FailingCast"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["ValidCast"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
        }
    }

    [Test]
    public async Task SelectedUnknownRequiresAnalysisReportsOneIncompleteDiagnostic()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static void Positive(int value) {
                    Contract.Requires(value > 0);
                }

                public static void KnownBranch(bool condition) {
                    Contract.Requires(condition);
                    if (condition) {
                        Positive(-1);
                    }
                }

                public static void MixedRefutedAndUnknown(bool condition) {
                    Contract.Requires(condition);
                    Positive(-1);
                    if (condition) {
                        Positive(-1);
                    }
                }

                public static void BothBranches(bool condition) {
                    Contract.Requires(condition || !condition);
                    if (condition) {
                        Positive(-1);
                    } else {
                        Positive(-1);
                    }
                }

                public static void UnselectedBranch(bool condition) {
                    if (condition) {
                        Positive(-1);
                    }
                }

                public static void SelectedWithoutCalls(bool condition) {
                    Contract.Requires(condition || !condition);
                }

                public static void Unsupported<T>(bool condition) {
                    Contract.Requires(condition);
                    if (condition) {
                        Positive(-1);
                    }
                }
            }
            """,
            "contracts",
            ["SP0027", "SP0047"],
            new SharpProofAnalyzer(factory));

        var incomplete = diagnostics
            .Where(static diagnostic => diagnostic.Id == "SP0047")
            .Select(static diagnostic =>
                diagnostic.GetMessage(CultureInfo.InvariantCulture))
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics, Has.Length.EqualTo(5));
            Assert.That(
                incomplete.Count(static message =>
                    message.Contains(
                        "'KnownBranch'",
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(
                incomplete.Count(static message =>
                    message.Contains(
                        "'BothBranches'",
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(
                incomplete.Count(static message =>
                    message.Contains(
                        "'Unsupported'",
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(
                incomplete.Count(static message =>
                    message.Contains(
                        "'MixedRefutedAndUnknown'",
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(
                incomplete,
                Has.None.Contain("'UnselectedBranch'"));
            Assert.That(
                incomplete,
                Has.None.Contain("'SelectedWithoutCalls'"));
            Assert.That(
                incomplete.Count(static message =>
                    message.Contains(
                        "RequiresCallSiteAnalysisUnknown",
                        StringComparison.Ordinal)),
                Is.EqualTo(3));
            Assert.That(
                factory.Outcomes["KnownBranch"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["BothBranches"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["MixedRefutedAndUnknown"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["UnselectedBranch"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["SelectedWithoutCalls"],
                Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
            Assert.That(
                factory.Outcomes["Unsupported"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
    }

    private sealed class RecordingSessionFactory : IAnalyzerSessionFactory
    {
        private readonly ConcurrentDictionary<
            string,
            AnalyzerSemanticOutcome> _outcomes =
            new(StringComparer.Ordinal);

        internal ConcurrentDictionary<string, AnalyzerSemanticOutcome>
            Outcomes => _outcomes;

        public AnalyzerSession Create(
            Compilation compilation,
            AnalyzerConfiguration configuration,
            CancellationToken cancellationToken)
        {
            return new AnalyzerSession(
                compilation,
                configuration,
                cancellationToken,
                (method, outcome) => _outcomes.AddOrUpdate(
                    method.Name,
                    outcome,
                    (_, current) =>
                        AnalyzerSemanticOutcomes.Combine(
                            current,
                            outcome)));
        }
    }
}
