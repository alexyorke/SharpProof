using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer.V2.Test;

[TestFixture]
public sealed class AnalyzerModeAndEffectTests {
    private const string ModeFixture = """
        using SharpProof.Attributes;

        public static class Fixture {
            [ZeroAllocations]
            public static object Allocate() => new object();

            public static void Positive(int value) {
                Contract.Requires(value > 0);
            }

            public static void Call() {
                Positive(-1);
            }
        }
        """;

    [Test]
    public async Task DefaultOffCreatesNoAnalysisSession() {
        var factory = new ThrowingSessionFactory();
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            ModeFixture,
            mode: null,
            ["SP0045", "SP0027"],
            new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.Zero);
    }

    [Test]
    public async Task EffectModeReusesAnalyzerResolvedApiSpecs() {
        var factory = new SpecReuseSessionFactory();
        _ = await AnalyzerV2TestHost.AnalyzeAsync(
            ModeFixture,
            "effects",
            ["SP0045"],
            new SharpProofAnalyzer(factory));

        Assert.That(factory.Session, Is.Not.Null);
        Assert.That(
            factory.Session!.EffectApiSpecs,
            Is.SameAs(factory.Session.ApiSpecs));
    }

    [Test]
    public async Task InvalidModeReportsConfigurationAndFailsClosed() {
        var factory = new ThrowingSessionFactory();
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            ModeFixture,
            "everything",
            ["SP0045", "SP0025"],
            new SharpProofAnalyzer(factory));

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0025"]));
        Assert.That(factory.CreateCount, Is.Zero);
    }

    [TestCase("off", new string[0])]
    [TestCase("effects", new[] { "SP0045" })]
    [TestCase("contracts", new[] { "SP0027" })]
    [TestCase("all-experimental", new[] { "SP0045", "SP0027" })]
    public async Task ModesSelectOnlyTheirFeaturePipeline(
        string mode,
        string[] expected) {
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            ModeFixture,
            mode,
            ["SP0045", "SP0027"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EquivalentTo(expected));
    }

    [Test]
    public async Task MayEffectSummaryReportsEachContractAsNotVerified() {
        var external = AnalyzerV2TestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public static class ExternalFixture {
                [SharpProofTrusted("Reviewed external effect contract.")]
                [EffectContract(
                    SharpProofEffect.ReadsAmbientState,
                    Capabilities = SharpProofCapability.Synchronization,
                    Complete = true)]
                public static void Synchronize() {
                }
            }
            """,
            "ExternalEffectFixture");
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int state;

                [EnforcePure]
                public static void Write() {
                    state = 1;
                }

                [ZeroAllocations]
                public static object Allocate() => new object();

                [AllowedCapabilities(SharpProofCapability.None)]
                public static void Synchronize() {
                    ExternalFixture.Synchronize();
                }

                [DoesNotThrow]
                public static int Divide(int left, int right) => left / right;

                [AllowedExceptions(typeof(InvalidOperationException))]
                public static int WrongException(int left, int right) => left / right;
            }
            """,
            "effects",
            ["SP0002", "SP0016", "SP0045", "SP0046"],
            additionalReferences: [external]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EquivalentTo(
                ["SP0002", "SP0016", "SP0045", "SP0046", "SP0046"]));
    }

    [Test]
    public async Task UnknownEffectFacetsNeverCountAsSuccess() {
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                [ZeroAllocations]
                [DoesNotThrow]
                [AllowedCapabilities(SharpProofCapability.None)]
                public static Guid Unknown() => Guid.NewGuid();
            }
            """,
            "effects",
            ["SP0016", "SP0045", "SP0046"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EquivalentTo(["SP0016", "SP0045", "SP0046"]));
    }

    [Test]
    public async Task SemanticOutcomeDoesNotTreatSilentCallSiteAsProven() {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static void Positive(int value) {
                    Contract.Requires(value > 0);
                }

                private static int Unknown() => -1;

                public static void Proven() => Positive(1);
                public static void Refuted() => Positive(-1);
                public static void SilentUnknown() => Positive(Unknown());
            }
            """,
            "contracts",
            ["SP0027"],
            new SharpProofAnalyzer(factory));

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0027"]));
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                factory.Outcomes["Proven"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["Refuted"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["SilentUnknown"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task RequiresChecksOnlyCompilerReachableInvocations() {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            """
            #nullable enable
            using SharpProof.Attributes;

            public static class Fixture {
                private sealed class Receiver {
                    public bool Positive(int value) {
                        Contract.Requires(value > 0);
                        return true;
                    }
                }

                private static bool Positive(int value) {
                    Contract.Requires(value > 0);
                    return true;
                }

                public static void IfFalse() {
                    if (false) {
                        Positive(-1);
                    }
                }

                public static bool FalseAnd() =>
                    false && Positive(-1);

                public static bool TrueOr() =>
                    true || Positive(-1);

                public static bool ConditionalOperator() =>
                    true ? true : Positive(-1);

                public static bool ConditionalAccess() =>
                    ((Receiver?)null)?.Positive(-1) ?? true;

                public static bool ConditionalUnknown(bool condition) =>
                    condition && Positive(-1);

                public static void Contradictory(int value) {
                    if (value > 0 && value < 0) {
                        Positive(-1);
                    }
                }

                public static bool ReachableRefutation() =>
                    Positive(-1);
            }
            """,
            "contracts",
            ["SP0027"],
            new SharpProofAnalyzer(factory));

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0027"]));
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                factory.Outcomes["IfFalse"],
                Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
            Assert.That(
                factory.Outcomes["FalseAnd"],
                Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
            Assert.That(
                factory.Outcomes["TrueOr"],
                Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
            Assert.That(
                factory.Outcomes["ConditionalOperator"],
                Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
            Assert.That(
                factory.Outcomes["ConditionalAccess"],
                Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
            Assert.That(
                factory.Outcomes["ConditionalUnknown"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["Contradictory"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["ReachableRefutation"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
        }
    }

    [Test]
    public async Task EffectSemanticOutcomeNeverRefutesFromAMaySummary() {
        var factory = new RecordingSessionFactory();
        _ = await AnalyzerV2TestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int State;

                [EnforcePure]
                public static int Proven(int value) => value + 1;

                [EnforcePure]
                public static int Refuted(int value) => State = value;

                [ZeroAllocations]
                public static Guid Unknown() => Guid.NewGuid();
            }
            """,
            "effects",
            ["SP0002", "SP0045"],
            new SharpProofAnalyzer(factory));

        using (Assert.EnterMultipleScope()) {
            Assert.That(
                factory.Outcomes["Proven"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["Refuted"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["Unknown"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task CompilerBoundGhostContractsHaveNoRuntimeEffects() {
        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [EnforcePure]
                [ZeroAllocations]
                [DoesNotThrow]
                public static int Identity(int value) {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(
                        Contract.Result<int>() == Contract.Old(value));
                    return value;
                }
            }
            """,
            "effects",
            ["SP0002", "SP0045", "SP0046", "SP0016"]);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void FeatureDescriptorsRemainInfoAndDisabled() {
        var descriptors = new SharpProofAnalyzer().SupportedDiagnostics;
        var features = descriptors.Where(static descriptor =>
            descriptor.Id is not ("SP0024" or "SP0025"));

        Assert.That(
            features.Select(static descriptor => descriptor.DefaultSeverity),
            Is.All.EqualTo(DiagnosticSeverity.Info));
        Assert.That(
            features.Select(static descriptor => descriptor.IsEnabledByDefault),
            Is.All.False);
        Assert.That(
            descriptors.Single(static descriptor => descriptor.Id == "SP0024")
                .IsEnabledByDefault,
            Is.True);
        Assert.That(
            descriptors.Single(static descriptor => descriptor.Id == "SP0025")
                .IsEnabledByDefault,
            Is.True);
    }

    private sealed class ThrowingSessionFactory : IAnalyzerSessionFactory {
        private int _createCount;

        internal int CreateCount => Volatile.Read(ref _createCount);

        public AnalyzerSession Create(
            Compilation compilation,
            AnalyzerConfiguration configuration,
            CancellationToken cancellationToken) {
            Interlocked.Increment(ref _createCount);
            throw new InvalidOperationException(
                "The default-off analyzer must not construct a session.");
        }
    }

    private sealed class RecordingSessionFactory : IAnalyzerSessionFactory {
        private readonly ConcurrentDictionary<
            string,
            AnalyzerSemanticOutcome> _outcomes =
            new(StringComparer.Ordinal);

        internal IReadOnlyDictionary<string, AnalyzerSemanticOutcome> Outcomes =>
            _outcomes;

        public AnalyzerSession Create(
            Compilation compilation,
            AnalyzerConfiguration configuration,
            CancellationToken cancellationToken) =>
            new(
                compilation,
                configuration,
                cancellationToken,
                (method, outcome) => _outcomes.AddOrUpdate(
                    method.Name,
                    outcome,
                    (_, current) =>
                        AnalyzerSemanticOutcomes.Combine(current, outcome)));
    }

    private sealed class SpecReuseSessionFactory : IAnalyzerSessionFactory {
        internal AnalyzerSession? Session { get; private set; }

        public AnalyzerSession Create(
            Compilation compilation,
            AnalyzerConfiguration configuration,
            CancellationToken cancellationToken) {
            Session = new AnalyzerSession(
                compilation,
                configuration,
                cancellationToken);
            return Session;
        }
    }
}
