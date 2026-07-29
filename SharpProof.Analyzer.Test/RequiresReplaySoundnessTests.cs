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
