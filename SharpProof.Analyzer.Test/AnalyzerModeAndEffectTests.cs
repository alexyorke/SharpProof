using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer.Test;

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
    public async Task ProfileOffCreatesNoAnalysisSession() {
        var factory = new ThrowingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            ModeFixture,
            mode: null,
            ["SP0045", "SP0027"],
            new SharpProofAnalyzer(factory),
            profile: "off");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.Zero);
    }

    [Test]
    public async Task DefaultAdvisoryAllKeepsUnannotatedCodeQuiet() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "public static class Fixture { public static int Add(int x) => x + 1; }",
            mode: null,
            []);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task EffectModeReusesAnalyzerResolvedApiSpecs() {
        var factory = new SpecReuseSessionFactory();
        _ = await AnalyzerTestHost.AnalyzeAsync(
            ModeFixture,
            "effects",
            ["SP0045"],
            new SharpProofAnalyzer(factory));

        Assert.That(factory.Session, Is.Not.Null);
        Assert.That(
            factory.Session!.EffectApiSpecs,
            Is.SameAs(factory.Session.ApiSpecs));
    }

    [TestCase(null, "everything", null, "advisory, strict, off")]
    [TestCase(null, null, "everything", "effects, contracts, all")]
    [TestCase("everything", null, null, "off, effects, contracts, all-experimental")]
    public async Task InvalidConfigurationReportsAllowedValuesAndFailsClosed(
        string? mode,
        string? profile,
        string? features,
        string allowedValues) {
        var factory = new ThrowingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            ModeFixture,
            mode,
            ["SP0045", "SP0025"],
            new SharpProofAnalyzer(factory),
            profile: profile,
            features: features);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0025"]));
        Assert.That(
            diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain(allowedValues));
        Assert.That(factory.CreateCount, Is.Zero);
    }

    [TestCase("off", "all", new string[0])]
    [TestCase("advisory", "effects", new[] { "SP0045" })]
    [TestCase("strict", "contracts", new[] { "SP0027" })]
    [TestCase("advisory", "all", new[] { "SP0045", "SP0027" })]
    public async Task ProfileAndFeaturesSelectOnlyTheirPipeline(
        string profile,
        string features,
        string[] expected) {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            ModeFixture,
            mode: null,
            ["SP0045", "SP0027"],
            profile: profile,
            features: features);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EquivalentTo(expected));
    }

    [Test]
    public async Task MayEffectSummaryReportsEachContractAsNotVerified() {
        var external = AnalyzerTestHost.EmitReference(
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
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
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
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
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
    public async Task VolatileFieldReadCannotProvePurityOrNoSynchronization() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public sealed class Fixture {
                private volatile int _value;

                [EnforcePure]
                [AllowedCapabilities(SharpProofCapability.None)]
                public int Read() => _value;
            }
            """,
            "effects",
            ["SP0002", "SP0016"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EquivalentTo(["SP0002", "SP0016"]));
    }

    [Test]
    public async Task ExactConstantAndPropertyIncrementEffectsSatisfyContracts() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public sealed class Fixture {
                private const int Answer = 42;
                private int _value;

                private int Value {
                    get => _value;
                    set => _value = value;
                }

                [EnforcePure]
                public static int ReadConstant() => Answer;

                [ZeroAllocations]
                [DoesNotThrow]
                [AllowedCapabilities(SharpProofCapability.None)]
                public void Increment() => Value++;
            }
            """,
            "effects",
            ["SP0002", "SP0016", "SP0045", "SP0046"]);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task SemanticOutcomeDoesNotTreatSilentCallSiteAsProven() {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
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
    public async Task ConstructorRequiresProduceAccountableCallSiteOutcomes() {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                public sealed class Positive {
                    public Positive(int value) {
                        Contract.Requires(value > 0);
                    }
                }

                private static int Unknown() => -1;

                public static Positive ProvenConstructor() =>
                    new Positive(1);

                public static Positive RefutedConstructor() =>
                    new Positive(-1);

                public static Positive UnknownConstructor() =>
                    new Positive(Unknown());

                public static Positive ConditionalConstructor(bool condition) {
                    if (condition) {
                        return new Positive(-1);
                    }
                    return new Positive(1);
                }

                public static Positive ThrowingPrefix(int denominator) {
                    _ = 1 / denominator;
                    return new Positive(-1);
                }

                private static void PositiveInvocation(int value) {
                    Contract.Requires(value > 0);
                }

                public static void InvocationNonRegression() =>
                    PositiveInvocation(-1);
            }
            """,
            "contracts",
            ["SP0027"],
            new SharpProofAnalyzer(factory));

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0027", "SP0027"]));
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                factory.Outcomes["ProvenConstructor"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["RefutedConstructor"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["UnknownConstructor"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["ConditionalConstructor"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["ThrowingPrefix"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["InvocationNonRegression"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
        }
    }

    [Test]
    public async Task RequiresChecksOnlyCompilerReachableInvocations() {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
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
        _ = await AnalyzerTestHost.AnalyzeAsync(
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
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
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
    public async Task UnsupportedSelectedMethodIsVisibleButUnannotatedPeerIsQuiet() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                [ZeroAllocations]
                public static int Selected() {
                    Func<int> value = () => 1;
                    return value();
                }

                public static int Unannotated() {
                    Func<int> value = () => 1;
                    return value();
                }
            }
            """,
            mode: null,
            []);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0047"]));
        Assert.That(
            diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("'Selected'"));
    }

    [Test]
    public async Task AbstractAndExternSelectionsCannotDisappearWithoutAnOutcome() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public interface IFixture {
                [ZeroAllocations]
                int SelectedEffect();

                [return: Positive]
                int SelectedContract();

                int Unannotated();
            }

            public abstract class Fixture {
                [DoesNotThrow]
                public abstract void SelectedException();

                [SharpProofSuppress("Reviewed unsupported boundary.")]
                [ZeroAllocations]
                public abstract void Suppressed();
            }

            public static class NativeFixture {
                [AllowedCapabilities(SharpProofCapability.None)]
                public static extern int SelectedExtern();
            }
            """,
            mode: null,
            ["SP0047"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("SP0047", 4)));
        Assert.That(
            diagnostics.Select(diagnostic =>
                diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Has.All.Contain("MissingOperationRoot"));
    }

    [Test]
    public async Task EffectContractSelectsUnsupportedMethod() {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                [EffectContract(SharpProofEffect.None)]
                public static int Selected() {
                    Func<int> value = () => 1;
                    return value();
                }
            }
            """,
            mode: null,
            []);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0047"]));
    }

    [Test]
    public void AdvisoryDescriptorsUseProductionDefaults() {
        var descriptors = new SharpProofAnalyzer().SupportedDiagnostics;
        var informational = descriptors.Where(static descriptor =>
            descriptor.Id is not ("SP0024" or "SP0025" or "SP0027" or "SP0049"));

        Assert.That(
            informational.Select(static descriptor => descriptor.DefaultSeverity),
            Is.All.EqualTo(DiagnosticSeverity.Info));
        Assert.That(
            descriptors.Select(static descriptor => descriptor.IsEnabledByDefault),
            Is.All.True);
        Assert.That(
            descriptors.Single(static descriptor => descriptor.Id == "SP0027")
                .DefaultSeverity,
            Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(
            descriptors.Single(static descriptor => descriptor.Id == "SP0049")
                .DefaultSeverity,
            Is.EqualTo(DiagnosticSeverity.Error));
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
                "The profile-off analyzer must not construct a session.");
        }
    }

    private sealed class RecordingSessionFactory : IAnalyzerSessionFactory {
        private readonly ConcurrentDictionary<
            string,
            AnalyzerSemanticOutcome> _outcomes =
            new(StringComparer.Ordinal);

        internal ConcurrentDictionary<string, AnalyzerSemanticOutcome> Outcomes =>
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
