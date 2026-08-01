using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;
using SharpProof.Worker.Protocol;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerModeAndEffectTests
{
    private const int ManagedFlowOperationBudget = 4096;
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
    public async Task ProfileOffCreatesNoAnalysisSession()
    {
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
    public async Task DefaultAdvisoryAllKeepsUnannotatedCodeQuiet()
    {
        var factory = new ThrowingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "public static class Fixture { public static int Add(int x) => x + 1; }",
            mode: null,
            [],
            new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.Zero);
    }

    [Test]
    public async Task StrictProfileDoesNotUseTheAdvisoryFastPath()
    {
        var factory = new SpecReuseSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "public static class Fixture { public static int Add(int x) => x + 1; }",
            mode: null,
            [],
            new SharpProofAnalyzer(factory),
            profile: "strict");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.Session, Is.Not.Null);
    }

    [TestCase(
        "using System; public static class Fixture { " +
        "[Obsolete] public static int Read() => 1; }")]
    public async Task AdvisoryPotentialWorkCreatesOnlyALightweightSession(
        string source)
    {
        var factory = new SpecReuseSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            source,
            mode: null,
            [],
            new SharpProofAnalyzer(factory));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(factory.Session, Is.Not.Null);
            Assert.That(factory.Session!.HasCreatedApiSpecs, Is.False);
            Assert.That(factory.Session.HasCreatedEffectAnalysis, Is.False);
        }
    }

    [TestCase(
        "public static class Fixture { " +
        "public static int Read(int value) => System.Math.Abs(value); }")]
    [TestCase(
        "public static class Fixture { " +
        "public static object Create() => new object(); }")]
    [TestCase(
        "public class Base { } public sealed class Derived : Base { }")]
    [TestCase(
        "public static class Fixture { " +
        "public static int Read() { return 1; } }")]
    public async Task AdvisoryWorkWithoutSharpProofContractsCreatesNoSession(
        string source)
    {
        var factory = new ThrowingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            source,
            mode: null,
            [],
            new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.Zero);
    }

    [Test]
    public async Task OrdinaryAssemblyMetadataDoesNotDefeatTheFastPath()
    {
        var factory = new ThrowingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;

            [assembly: CLSCompliant(true)]

            public static class Fixture {
                public static int Read(int value) => value + 1;
            }
            """,
            mode: null,
            [],
            new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.Zero);
    }

    [Test]
    public async Task SharpProofAssemblyMetadataDefeatsTheFastPath()
    {
        var factory = new SpecReuseSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            [assembly: SharpProofTrusted("reviewed")]

            public static class Fixture {
                public static int Read(int value) => value + 1;
            }
            """,
            mode: null,
            [],
            new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.Session, Is.Not.Null);
    }

    [Test]
    public async Task EffectModeReusesAnalyzerResolvedApiSpecs()
    {
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
        string allowedValues)
    {
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

    [TestCase("off", null)]
    [TestCase(null, "contracts")]
    [TestCase("strict", "all")]
    public async Task ConflictingLegacyAndReplacementOptionsFailClosed(
        string? profile,
        string? features)
    {
        var factory = new ThrowingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            ModeFixture,
            "effects",
            ["SP0025", "SP0045", "SP0027"],
            new SharpProofAnalyzer(factory),
            profile: profile,
            features: features);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0025"]));
        Assert.That(
            diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("conflicts"));
        Assert.That(factory.CreateCount, Is.Zero);
    }

    [TestCase("off", "off", "all")]
    [TestCase("effects", "advisory", "effects")]
    [TestCase("contracts", "strict", "contracts")]
    [TestCase("all-experimental", "advisory", "all")]
    public async Task EquivalentLegacyAndReplacementOptionsRemainCompatible(
        string mode,
        string profile,
        string features)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            ModeFixture,
            mode,
            ["SP0025"],
            profile: profile,
            features: features);

        Assert.That(diagnostics, Is.Empty);
    }

    [TestCase("off", null, "all", new string[0])]
    [TestCase("off", null, "effects", new string[0])]
    [TestCase("effects", "advisory", null, new[] { "SP0045" })]
    [TestCase("effects", "strict", null, new[] { "SP0045" })]
    [TestCase("effects", null, "effects", new[] { "SP0045" })]
    [TestCase("contracts", "advisory", null, new[] { "SP0027" })]
    [TestCase("contracts", "strict", null, new[] { "SP0027" })]
    [TestCase("all-experimental", "strict", null, new[] { "SP0045", "SP0027" })]
    public async Task PartialReplacementOptionsInheritTheMissingLegacyDimension(
        string mode,
        string? profile,
        string? features,
        string[] expected)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            ModeFixture,
            mode,
            ["SP0025", "SP0045", "SP0027"],
            profile: profile,
            features: features);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EquivalentTo(expected));
    }

    [TestCase("off", "all", new string[0])]
    [TestCase("advisory", "effects", new[] { "SP0045" })]
    [TestCase("strict", "contracts", new[] { "SP0027" })]
    [TestCase("advisory", "all", new[] { "SP0045", "SP0027" })]
    public async Task ProfileAndFeaturesSelectOnlyTheirPipeline(
        string profile,
        string features,
        string[] expected)
    {
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
    public async Task MayEffectSummaryReportsEachContractAsNotVerified()
    {
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
    public async Task UnknownEffectFacetsNeverCountAsSuccess()
    {
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
        Assert.That(
            diagnostics.Single(static diagnostic => diagnostic.Id == "SP0045")
                .GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("AllocationUnknown"));
        Assert.That(
            diagnostics.Single(static diagnostic => diagnostic.Id == "SP0016")
                .GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("CapabilitySetUnknown"));
        Assert.That(
            diagnostics.Single(static diagnostic => diagnostic.Id == "SP0046")
                .GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("ExceptionSetUnknown"));
    }

    [Test]
    public async Task IncompleteUnrelatedFacetsDoNotBlockIndependentContracts()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System.Collections.Generic;
            using SharpProof.Attributes;

            public static class Fixture {
                [DoesNotThrow]
                public static int[] Empty() => System.Array.Empty<int>();

                [AllowedCapabilities(SharpProofCapability.None)]
                public static void Add(List<int> values) => values.Add(1);
            }
            """,
            "effects",
            ["SP0016", "SP0046"]);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task EffectProofRequiresEstablishedCalleePreconditions()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static void Restricted(int value) {
                    Contract.Requires(value > 0);
                }

                [DoesNotThrow]
                public static void Proven(int value) {
                    Contract.Requires(value > 0);
                    Restricted(value);
                }

                [DoesNotThrow]
                public static void Unknown(int value) =>
                    Restricted(value);
            }
            """,
            "effects",
            ["SP0047"],
            new SharpProofAnalyzer(factory));

        Assert.That(
            diagnostics.Select(
                static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0047"]));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics[0].GetMessage(
                    CultureInfo.InvariantCulture),
                Does.Contain(
                    "CallPreconditionNotProven"));
            Assert.That(
                factory.Outcomes["Proven"],
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["Unknown"],
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task LaterArgumentMutationCannotProveEarlierCalleeArgument()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Divide(
                    int denominator,
                    int ignored) {
                    Contract.Requires(denominator > 0);
                    return 1 / denominator;
                }

                [DoesNotThrow]
                public static int Call() {
                    var value = 0;
                    return Divide(value, value = 1);
                }
            }
            """,
            "effects",
            ["SP0047"],
            new SharpProofAnalyzer(factory));

        Assert.That(
            diagnostics.Select(
                static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0047"]));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics[0].GetMessage(
                    CultureInfo.InvariantCulture),
                Does.Contain(
                    "CallPreconditionNotProven"));
            Assert.That(
                factory.Outcomes["Call"],
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task MutationInsideAnArgumentCannotProveCalleePreconditions()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static void RequireZero(int value) {
                    Contract.Requires(value == 0);
                    if (value != 0) {
                        throw new InvalidOperationException();
                    }
                }

                [DoesNotThrow]
                public static void Call() {
                    var value = 1;
                    RequireZero(value + (value = 0));
                }
            }
            """,
            "effects",
            ["SP0047"],
            new SharpProofAnalyzer(factory));

        Assert.That(
            diagnostics.Select(
                static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0047"]));
        Assert.That(
            diagnostics[0].GetMessage(
                CultureInfo.InvariantCulture),
            Does.Contain(
                "CallPreconditionNotProven"));
        Assert.That(
            factory.Outcomes["Call"],
            Is.EqualTo(
                AnalyzerSemanticOutcome.Unknown));
    }

    [Test]
    public async Task ExpandedParamsCallsRemainIncompleteAndAccountForTheArray()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static void RequireNull(
                    params string?[] values) {
                    Contract.Requires(values == null);
                    if (values != null) {
                        throw new InvalidOperationException();
                    }
                }

                private static void Free(
                    params string?[] values) {
                }

                [DoesNotThrow]
                public static void ExceptionClaim() =>
                    RequireNull((string?)null);

                [ZeroAllocations]
                public static void AllocationClaim() =>
                    Free((string?)null);
            }
            """,
            "effects",
            ["SP0047"],
            new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Not.Empty);
        Assert.That(
            diagnostics.Select(
                static diagnostic => diagnostic.Id),
            Has.All.EqualTo("SP0047"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                factory.Outcomes["ExceptionClaim"],
                Is.Not.EqualTo(
                    AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["AllocationClaim"],
                Is.Not.EqualTo(
                    AnalyzerSemanticOutcome.Proven));
        }
    }

    [Test]
    public async Task ComputedPropertySetterPreconditionsDoNotUseAnOperandAsTheStoredValue()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public sealed class Fixture {
                private int _value;

                private int Restricted {
                    [return: Positive]
                    get => _value;
                    set {
                        Contract.Requires(value > 0);
                        _value = value;
                    }
                }

                [DoesNotThrow]
                public void Add(int rhs) {
                    Contract.Requires(rhs > 0);
                    Restricted += rhs;
                }

                [DoesNotThrow]
                public void Increment() =>
                    Restricted++;
            }
            """,
            "effects",
            ["SP0047"],
            new SharpProofAnalyzer(factory));

        Assert.That(
            diagnostics.Select(
                static diagnostic => diagnostic.Id),
            Is.EqualTo([
                "SP0047",
                "SP0047"
            ]));
        Assert.That(
            diagnostics.Select(diagnostic =>
                diagnostic.GetMessage(
                    CultureInfo.InvariantCulture)),
            Has.All.Contain(
                "CallPreconditionNotProven"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                factory.Outcomes["Add"],
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["Increment"],
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task InArgumentEffectsFailClosedAtTheCurrentSubsetBoundary()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Divide(
                    in int denominator,
                    int ignored) {
                    Contract.Requires(denominator > 0);
                    return 1 / denominator;
                }

                [DoesNotThrow]
                public static int RvalueSnapshot() {
                    var value = 0;
                    return Divide(value + 0, value = 1);
                }

                [DoesNotThrow]
                public static int ExplicitAlias() {
                    var value = 1;
                    return Divide(in value, value = 0);
                }
            }
            """,
            "effects",
            ["SP0047"],
            new SharpProofAnalyzer(factory));

        Assert.That(
            diagnostics.Select(
                static diagnostic => diagnostic.Id),
            Is.EqualTo([
                "SP0047",
                "SP0047"
            ]));
        Assert.That(
            diagnostics.Select(diagnostic =>
                diagnostic.GetMessage(
                    CultureInfo.InvariantCulture)),
            Has.All.Contain(
                "UnsupportedOperationShape"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                factory.Outcomes["RvalueSnapshot"],
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Abstained));
            Assert.That(
                factory.Outcomes["ExplicitAlias"],
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Abstained));
        }
    }

    [Test]
    public async Task ExternalClosedPreconditionMustAlsoBeEstablished()
    {
        var external = AnalyzerTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public static class ExternalFixture {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.None,
                    IsDeterministic = true,
                    Complete = true)]
                public static void Restricted(
                    [Positive] int value) {
                }
            }
            """,
            "ExternalPreconditionFixture");
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [DoesNotThrow]
                public static void Proven() =>
                    ExternalFixture.Restricted(1);

                [DoesNotThrow]
                public static void Unknown(int value) =>
                    ExternalFixture.Restricted(value);
            }
            """,
            "effects",
            ["SP0047"],
            new SharpProofAnalyzer(factory),
            additionalReferences: [external]);

        Assert.That(
            diagnostics.Select(
                static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0047"]));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics[0].GetMessage(
                    CultureInfo.InvariantCulture),
                Does.Contain(
                    "CallPreconditionNotProven"));
            Assert.That(
                factory.Outcomes["Proven"],
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["Unknown"],
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task InvalidlyPlacedCallerRequiresCannotCleanEffectSummary()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static void Restricted(int value) {
                    Contract.Requires(value > 0);
                }

                [DoesNotThrow]
                public static void CallAfterLateRequires(
                    int value) {
                    _ = value;
                    Contract.Requires(value > 0);
                    Restricted(value);
                }

                [DoesNotThrow]
                public static void CallAfterConditionalRequires(
                    int value) {
                    if (value > 0) {
                        Contract.Requires(value > 0);
                    }
                    Restricted(value);
                }

                [DoesNotThrow]
                public static void CallAfterMixedRequires(
                    int value) {
                    Contract.Requires(value != 0);
                    _ = value;
                    Contract.Requires(value > 0);
                    Restricted(value);
                }
            }
            """,
            "effects",
            ["SP0047"],
            new SharpProofAnalyzer(factory));

        Assert.That(
            diagnostics.Select(
                static diagnostic => diagnostic.Id),
            Is.EqualTo([
                "SP0047",
                "SP0047",
                "SP0047"
            ]));
        Assert.That(
            diagnostics.Select(diagnostic =>
                diagnostic.GetMessage(
                    CultureInfo.InvariantCulture)),
            Has.All.Contain(
                "CallPreconditionNotProven"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                factory.Outcomes[
                    "CallAfterLateRequires"],
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes[
                    "CallAfterConditionalRequires"],
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes[
                    "CallAfterMixedRequires"],
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task CompanionPreconditionDoesNotContaminateOtherMember()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            #nullable enable
            using SharpProof.Attributes;

            public sealed class Service {
                public void Restricted(int value) {
                }

                public void Free() {
                }
            }

            [ContractFor(typeof(Service))]
            public static class ServiceContracts {
                public static void Restricted(
                    Service receiver,
                    int value) {
                    Contract.Requires(value > 0);
                }

                public static void Free(
                    Service receiver) {
                }
            }

            public static class Fixture {
                [DoesNotThrow]
                public static void CallFree(
                    [NotNull] Service service) =>
                    service.Free();
            }
            """,
            "effects",
            ["SP0047"],
            new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            factory.Outcomes["CallFree"],
            Is.EqualTo(
                AnalyzerSemanticOutcome.Proven));
    }

    [Test]
    public async Task LateEnsuresDoesNotContaminateEntryPreconditionEvidence()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static void Free() {
                }

                [DoesNotThrow]
                public static void CallFree() {
                    _ = 0;
                    Contract.Ensures(true);
                    Free();
                }
            }
            """,
            "effects",
            ["SP0047"],
            new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            factory.Outcomes["CallFree"],
            Is.EqualTo(
                AnalyzerSemanticOutcome.Proven));
    }

    [Test]
    public async Task SealedArrayStoreProvesDoesNotThrowButCovariantStoreRemainsUnknown()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [DoesNotThrow]
                public static void StoreSealed(string value) {
                    var values = new string[1];
                    values[0] = value;
                }

                [DoesNotThrow]
                public static void StoreCovariant(object value) {
                    object[] values = new string[1];
                    values[0] = value;
                }
            }
            """,
            "effects",
            ["SP0046"],
            new SharpProofAnalyzer(factory));

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0046"]));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("'StoreCovariant'"));
            Assert.That(
                factory.Outcomes["StoreSealed"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["StoreCovariant"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task VolatileFieldReadCannotProvePurityOrNoSynchronization()
    {
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
    public async Task FreshAggregateBorrowedContentCannotProvePurity()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public sealed class Box {
                public int Value;
            }

            public static class Fixture {
                [EnforcePure]
                public static void Mutate(Box box) {
                    var holder = new[] { box };
                    var alias = holder[0];
                    alias.Value = 1;
                }
            }
            """,
            "effects",
            ["SP0002"],
            new SharpProofAnalyzer(factory));

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0002"]));
        Assert.That(
            factory.Outcomes["Mutate"],
            Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }

    [Test]
    public async Task ExactConstantAndPropertyIncrementEffectsSatisfyContracts()
    {
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
    public async Task SemanticOutcomeDoesNotTreatSilentCallSiteAsProven()
    {
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
        using (Assert.EnterMultipleScope())
        {
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
    public async Task ConstructorRequiresProduceAccountableCallSiteOutcomes()
    {
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
        using (Assert.EnterMultipleScope())
        {
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
    public async Task ExhaustedManagedFlowCannotProduceAPositiveCallSiteOutcome()
    {
        var padding = string.Concat(
            Enumerable.Repeat(
                "value++;",
                ManagedFlowOperationBudget + 1));
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static void Positive(int value) {
                    Contract.Requires(value > 0);
                }

                public static void IncompleteProven() {
                    var value = 0;
            """ +
            padding +
            """
                    Positive(1);
                }

                public static void IncompleteRefuted() {
                    var value = 0;
            """ +
            padding +
            """
                    Positive(-1);
                }
            }
            """,
            "contracts",
            ["SP0027"],
            new SharpProofAnalyzer(factory));

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0027"]));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                factory.Outcomes["IncompleteProven"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["IncompleteRefuted"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
        }
    }

    [Test]
    public async Task RequiresChecksOnlyCompilerReachableInvocations()
    {
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
        using (Assert.EnterMultipleScope())
        {
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
                Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
            Assert.That(
                factory.Outcomes["ReachableRefutation"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
        }
    }

    [Test]
    public async Task EffectSemanticOutcomeNeverRefutesFromAMaySummary()
    {
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

        using (Assert.EnterMultipleScope())
        {
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
    public async Task TypeInitializationCannotFabricateADefiniteAllocationViolation()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public sealed class PlainAllocation {
                public PlainAllocation() {
                }
            }

            public sealed class ThrowingInitialization {
                static ThrowingInitialization() {
                    throw new InvalidOperationException();
                }

                public ThrowingInitialization() {
                }
            }

            public static class Fixture {
                [ZeroAllocations]
                public static object FrameworkObject() =>
                    new object();

                [ZeroAllocations]
                public static PlainAllocation PlainSourceType() =>
                    new PlainAllocation();

                [ZeroAllocations]
                public static ThrowingInitialization BlockedByTypeInitializer() =>
                    new ThrowingInitialization();
            }
            """,
            "effects",
            ["SP0045"],
            new SharpProofAnalyzer(factory));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0045", "SP0045", "SP0045"]));
            Assert.That(
                factory.Outcomes["FrameworkObject"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["PlainSourceType"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["BlockedByTypeInitializer"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task DirectLockReceiverCompletionControlsRefutation()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public sealed class ThrowingGate {
                public ThrowingGate() {
                    throw new InvalidOperationException();
                }
            }

            public static class Fixture {
                [AllowedCapabilities(SharpProofCapability.None)]
                public static void SafeObject() {
                    lock ((object)new object()) {
                    }
                }

                [AllowedCapabilities(SharpProofCapability.None)]
                public static void SafeArray() {
                    lock (new object[1]) {
                    }
                }

                [AllowedCapabilities(SharpProofCapability.None)]
                public static void ThrowingConstructor() {
                    lock (new ThrowingGate()) {
                    }
                }

                [AllowedCapabilities(SharpProofCapability.None)]
                public static void WrappedThrowingConstructor() {
                    lock ((object)(new ThrowingGate())) {
                    }
                }

                [AllowedCapabilities(SharpProofCapability.None)]
                public static void DynamicArrayLength(int length) {
                    lock (new object[length]) {
                    }
                }
            }
            """,
            "effects",
            ["SP0016"],
            new SharpProofAnalyzer(factory));

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("SP0016", 5)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                factory.Outcomes["SafeObject"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["SafeArray"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["ThrowingConstructor"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["WrappedThrowingConstructor"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["DynamicArrayLength"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task CompilerBoundGhostContractsHaveNoRuntimeEffects()
    {
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
    public async Task UnsupportedSelectedMethodIsVisibleButUnannotatedPeerIsQuiet()
    {
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
    public async Task UnsupportedContractSelectedBodiesAreVisibleButUnannotatedPeersAreQuiet()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using System.Threading.Tasks;
            using SharpProof.Attributes;

            public static class Fixture {
                public static int SelectedByClause() {
                    Contract.Ensures(true);
                    Func<int> value = () => 1;
                    return value();
                }

                [return: Positive]
                public static T SelectedByAttribute<T>(T value) =>
                    value;

                public static async Task<int> SelectedAsyncByClause() {
                    Contract.Ensures(true);
                    await Task.Yield();
                    return 1;
                }

                public static int Unannotated() {
                    Func<int> value = () => 1;
                    return value();
                }
            }
            """,
            mode: null,
            ["SP0047"],
            features: "contracts");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0047", "SP0047", "SP0047"]));
        var messages = diagnostics.Select(diagnostic =>
            diagnostic.GetMessage(CultureInfo.InvariantCulture)).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                messages.Count(static message =>
                    message.Contains(
                        "'SelectedByClause'",
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(
                messages.Count(static message =>
                    message.Contains(
                        "'SelectedByAttribute'",
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(
                messages.Count(static message =>
                    message.Contains(
                        "'SelectedAsyncByClause'",
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(
                messages.Any(static message =>
                    message.Contains(
                        "'Unannotated'",
                        StringComparison.Ordinal)),
                Is.False);
        }
    }

    [Test]
    public async Task AbstractAndExternSelectionsCannotDisappearWithoutAnOutcome()
    {
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
    public async Task OnlyValidTrustedCompleteBodylessEffectContractsAreAccepted()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class NativeFixture {
                [SharpProofTrusted("Reviewed native implementation.")]
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static extern int Accepted();

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static extern int Untrusted();

                [SharpProofTrusted("Reviewed native implementation.")]
                [EffectContract(SharpProofEffect.None, Complete = false)]
                public static extern int Incomplete();

                [SharpProofTrusted("Reviewed native implementation.")]
                [EffectContract((SharpProofEffect)(1L << 40), Complete = true)]
                public static extern int Invalid();

                [SharpProofTrusted("Reviewed native implementation.")]
                [EffectContract(SharpProofEffect.None)]
                [EffectContract(SharpProofEffect.Allocates)]
                public static extern int Conflicting();

                [DoesNotThrow]
                [SharpProofTrusted("Reviewed native implementation.")]
                [EffectContract(
                    SharpProofEffect.Throws,
                    ThrownExceptions = new[] { typeof(InvalidOperationException) },
                    Complete = true)]
                public static extern int Contradictory();
            }
            """,
            mode: null,
            ["SP0024", "SP0046", "SP0047"]);

        Assert.That(
            diagnostics.Count(static diagnostic => diagnostic.Id == "SP0024"),
            Is.EqualTo(2));
        Assert.That(
            diagnostics.Where(static diagnostic => diagnostic.Id == "SP0024")
                .Select(diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Has.All.Contain("[EffectContract]"));
        var incomplete = diagnostics
            .Where(static diagnostic => diagnostic.Id == "SP0047")
            .Select(diagnostic =>
                diagnostic.GetMessage(CultureInfo.InvariantCulture))
            .ToArray();
        Assert.That(
            incomplete,
            Has.Length.EqualTo(4),
            string.Join(Environment.NewLine, incomplete));
        Assert.That(incomplete, Has.None.Contain("'Accepted'"));
        Assert.That(incomplete, Has.Some.Contain("'Untrusted'"));
        Assert.That(incomplete, Has.Some.Contain("'Incomplete'"));
        Assert.That(incomplete, Has.Some.Contain("'Invalid'"));
        Assert.That(incomplete, Has.Some.Contain("'Conflicting'"));
        Assert.That(
            diagnostics.Where(static diagnostic => diagnostic.Id == "SP0046")
                .Select(diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Has.Some.Contain("'Contradictory'"));
    }

    [Test]
    public async Task NullableAndNativeDivisionCannotSatisfyDoesNotThrow()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [DoesNotThrow]
                public static int? NullableDivide(int? left, int? right) =>
                    left / right;

                [DoesNotThrow]
                public static int? NullableRemainder(int? left, int? right) =>
                    left % right;

                [DoesNotThrow]
                public static uint? NullableUnsignedDivide(
                    uint? left,
                    uint? right) => left / right;

                [DoesNotThrow]
                public static uint? NullableUnsignedRemainder(
                    uint? left,
                    uint? right) => left % right;

                [DoesNotThrow]
                public static nint NativeDivide(nint left, nint right) =>
                    left / right;

                [DoesNotThrow]
                public static nint NativeRemainder(nint left, nint right) =>
                    left % right;

                [DoesNotThrow]
                public static nuint NativeUnsignedDivide(
                    nuint left,
                    nuint right) => left / right;

                [DoesNotThrow]
                public static nuint NativeUnsignedRemainder(
                    nuint left,
                    nuint right) => left % right;
            }
            """,
            "effects",
            ["SP0046"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("SP0046", 8)));
    }

    [Test]
    public async Task EffectContractSelectsUnsupportedMethod()
    {
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
    public async Task CompleteSourceEffectContractCanBeProvenFromAnEmptyBody()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void Empty() {
                }
            }
            """,
            mode: null,
            ["SP0047"],
            new SharpProofAnalyzer(factory),
            features: "effects");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            factory.Outcomes["Empty"],
            Is.EqualTo(AnalyzerSemanticOutcome.Proven));
    }

    [Test]
    public async Task BottomEntryCannotDirectlyProveAnEffectContract()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [EffectContract(
                    SharpProofEffect.None,
                    Complete = true)]
                public static int Impossible(
                    [Positive, InRange(-2, -1)] int value) =>
                    value;
            }
            """,
            mode: null,
            ["SP0047"],
            new SharpProofAnalyzer(factory),
            features: "effects");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics.Select(
                    static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0047"]));
            Assert.That(
                factory.Outcomes["Impossible"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task IntrinsicLengthReadsArgumentState()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static int UndeclaredStringRead(string value) {
                    Contract.Requires(value != null);
                    return value.Length;
                }

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static int UndeclaredArrayRead(int[] value) {
                    Contract.Requires(value != null);
                    return value.Length;
                }

                [EffectContract(
                    SharpProofEffect.ReadsArgumentState,
                    Complete = true)]
                public static int DeclaredStringRead(string value) {
                    Contract.Requires(value != null);
                    return value.Length;
                }

                [EffectContract(
                    SharpProofEffect.ReadsArgumentState,
                    Complete = true)]
                public static int DeclaredArrayRead(int[] value) {
                    Contract.Requires(value != null);
                    return value.Length;
                }
            }
            """,
            mode: null,
            ["SP0047"],
            new SharpProofAnalyzer(factory),
            features: "effects");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0047", "SP0047"]));
            Assert.That(
                diagnostics.Select(diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)),
                Is.EqualTo((string[])[
                    "SharpProof could not completely analyze selected method " +
                    "'UndeclaredStringRead': EffectContractDoesNotCoverBodySummary",
                    "SharpProof could not completely analyze selected method " +
                    "'UndeclaredArrayRead': EffectContractDoesNotCoverBodySummary"
                ]));
            Assert.That(
                factory.Outcomes["UndeclaredStringRead"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["UndeclaredArrayRead"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["DeclaredStringRead"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["DeclaredArrayRead"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
        }
    }

    [Test]
    public async Task BudgetIncompleteManagedFlowCannotProveSelectedEffectContracts()
    {
        var padding = string.Concat(
            Enumerable.Repeat(
                "value++;",
                ManagedFlowOperationBudget + 1));
        var arrayValues = string.Join(
            ",",
            Enumerable.Repeat("1", ManagedFlowOperationBudget + 1));
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [ZeroAllocations]
                public static void BudgetExceeded() {
                    var value = 0;
            """ +
            padding +
            """
                }

                [ZeroAllocations]
                public static int[] BudgetRefuted() =>
                    new[] {
            """ +
            arrayValues +
            """
                    };

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void Cyclic(bool condition) {
                    while (condition) {
                    }
                }
            }
            """,
            mode: null,
            ["SP0045", "SP0047"],
            new SharpProofAnalyzer(factory),
            features: "effects");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0047", "SP0045", "SP0047"]));
        Assert.That(
            diagnostics.Where(static diagnostic => diagnostic.Id == "SP0047")
                .Select(diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo((string[])[
                "SharpProof could not completely analyze selected method " +
                "'BudgetExceeded': ManagedAbstractFlow:OperationBudgetExceeded",
                "SharpProof could not completely analyze selected method " +
                "'BudgetRefuted': ManagedAbstractFlow:OperationBudgetExceeded"
            ]));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                factory.Outcomes["BudgetExceeded"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["BudgetRefuted"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["Cyclic"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
        }

        var session = factory.Session!;
        var fixture = session.Compilation.GetTypeByMetadataName("Fixture")!;
        var budgetEvaluation = EffectContractDiagnostics.Evaluate(
            fixture.GetMembers("BudgetExceeded").OfType<IMethodSymbol>().Single(),
            Location.None, session, static _ => { }, default).Single();
        var refutedEvaluation = EffectContractDiagnostics.Evaluate(
            fixture.GetMembers("BudgetRefuted").OfType<IMethodSymbol>().Single(),
            Location.None, session, static _ => { }, default).Single();
        var cyclicEvaluation = EffectContractDiagnostics.Evaluate(
            fixture.GetMembers("Cyclic").OfType<IMethodSymbol>().Single(),
            Location.None, session, static _ => { }, default).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                budgetEvaluation.Reason,
                Is.EqualTo(EffectEvaluationReason.ResourceLimit));
            Assert.That(budgetEvaluation.Evidence, Does.Contain("OperationBudgetExceeded"));
            Assert.That(
                refutedEvaluation.Outcome,
                Is.EqualTo(EffectEvaluationOutcome.Refuted));
            Assert.That(refutedEvaluation.Evidence, Does.Contain("OperationBudgetExceeded"));
            Assert.That(
                cyclicEvaluation.Outcome,
                Is.EqualTo(EffectEvaluationOutcome.Proven));
            Assert.That(
                cyclicEvaluation.Reason,
                Is.EqualTo(EffectEvaluationReason.None));
            Assert.That(cyclicEvaluation.Evidence, Does.Contain("actual.analysisIncompleteReason=None"));
        }
    }

    [Test]
    public async Task CompleteSourceEffectContractReportsAnUncoveredStateWrite()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int state;

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void Write() => state = 1;
            }
            """,
            mode: null,
            ["SP0047"],
            new SharpProofAnalyzer(factory),
            features: "effects");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0047"]));
        Assert.That(
            diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("EffectContractDoesNotCoverBodySummary"));
        Assert.That(
            factory.Outcomes["Write"],
            Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }

    [Test]
    public async Task BaseExceptionEffectContractCoversDerivedBodyException()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                [EffectContract(
                    SharpProofEffect.Throws | SharpProofEffect.Allocates,
                    ThrownExceptions = new[] { typeof(Exception) },
                    Complete = true)]
                public static void ThrowDerived() =>
                    throw new InvalidOperationException();
            }
            """,
            mode: null,
            ["SP0047"],
            new SharpProofAnalyzer(factory),
            features: "effects");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            factory.Outcomes["ThrowDerived"],
            Is.EqualTo(AnalyzerSemanticOutcome.Proven));
    }

    [Test]
    public async Task LaterSiblingCatchDoesNotConsumeRethrow()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                [DoesNotThrow]
                public static void RethrowBeforeSiblingCatch() {
                    try {
                        throw new InvalidOperationException();
                    }
                    catch (InvalidOperationException) {
                        throw;
                    }
                    catch (Exception) {
                    }
                }
            }
            """,
            mode: null,
            ["SP0046"],
            new SharpProofAnalyzer(factory),
            features: "effects");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0046"]));
        Assert.That(
            factory.Outcomes["RethrowBeforeSiblingCatch"],
            Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }

    [Test]
    public async Task ExceptionConstructorContractsUseOnlyExactApprovedSpecs()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using System.Collections.Generic;
            using SharpProof.Attributes;

            public static class Fixture {
                [DoesNotThrow]
                public static InvalidOperationException SafeConstruction() =>
                    new InvalidOperationException("message");

                [DoesNotThrow]
                public static AggregateException UnmodeledConstruction() =>
                    new AggregateException(
                        (IEnumerable<Exception>)null!);

                [AllowedExceptions(typeof(ArgumentException))]
                public static void DefiniteWrongThrow() =>
                    throw new InvalidOperationException();

                [AllowedExceptions(typeof(ArgumentException))]
                public static void UnmodeledThrow() =>
                    throw new AggregateException(
                        (IEnumerable<Exception>)null!);
            }
            """,
            mode: null,
            ["SP0046"],
            new SharpProofAnalyzer(factory),
            features: "effects");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0046", "SP0046", "SP0046"]));
            Assert.That(
                factory.Outcomes["SafeConstruction"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["UnmodeledConstruction"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["DefiniteWrongThrow"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["UnmodeledThrow"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                diagnostics.Select(diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)),
                Has.Some.Contain("UnmodeledCall"));
        }
    }

    [Test]
    public async Task ThrowsOnlyDoesNotCoverAllocationButStillCoversThrowingExistingException()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                [EffectContract(
                    SharpProofEffect.Throws,
                    ThrownExceptions = new[] { typeof(Exception) },
                    Complete = true)]
                public static object AllocateOnly() => new object();

                [EffectContract(
                    SharpProofEffect.Throws,
                    ThrownExceptions = new[] { typeof(Exception) },
                    Complete = true)]
                public static void ThrowExisting(Exception exception) {
                    Contract.Requires(exception != null);
                    throw exception;
                }
            }
            """,
            mode: null,
            ["SP0047"],
            new SharpProofAnalyzer(factory),
            features: "effects");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0047"]));
            Assert.That(
                diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("EffectContractDoesNotCoverBodySummary"));
            Assert.That(
                factory.Outcomes["AllocateOnly"],
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.Outcomes["ThrowExisting"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
        }
    }

    [Test]
    public async Task AllowedExceptionsAccountsForPossiblyNullThrownExpressions()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            #nullable enable
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                [AllowedExceptions(typeof(InvalidOperationException))]
                public static void MaybeNull(
                    InvalidOperationException? exception) =>
                    throw exception;

                [AllowedExceptions(typeof(InvalidOperationException))]
                public static void RequiredNonNull(
                    InvalidOperationException? exception) {
                    Contract.Requires(exception != null);
                    throw exception;
                }
            }
            """,
            mode: null,
            ["SP0046"],
            new SharpProofAnalyzer(factory),
            features: "effects");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0046"]));
            Assert.That(
                diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("NullReferenceException"));
            Assert.That(
                factory.Outcomes["MaybeNull"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["RequiredNonNull"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
        }
    }

    [Test]
    public async Task ConstructedGenericExceptionContractsAndCatchesRemainExact()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public class GenericException<T> : Exception {
            }

            public sealed class DerivedStringException
                : GenericException<string> {
            }

            public static class Fixture {
                [AllowedExceptions(typeof(GenericException<int>))]
                public static void WrongAllowed(
                    [NotNull] GenericException<string> exception) =>
                    throw exception;

                [AllowedExceptions(typeof(GenericException<string>))]
                public static void ExactAllowed(
                    [NotNull] GenericException<string> exception) =>
                    throw exception;

                [AllowedExceptions(typeof(Exception))]
                public static void BaseAllowed(
                    [NotNull] GenericException<string> exception) =>
                    throw exception;

                [AllowedExceptions(typeof(GenericException<string>))]
                public static void DerivedAllowed(
                    [NotNull] DerivedStringException exception) =>
                    throw exception;

                [DoesNotThrow]
                public static void WrongCatch(
                    [NotNull] GenericException<string> exception) {
                    try {
                        throw exception;
                    }
                    catch (GenericException<int>) {
                    }
                }

                [DoesNotThrow]
                public static void ExactCatch(
                    [NotNull] GenericException<string> exception) {
                    try {
                        throw exception;
                    }
                    catch (GenericException<string>) {
                    }
                }
            }
            """,
            mode: null,
            ["SP0046", "SP0047"],
            new SharpProofAnalyzer(factory),
            features: "effects");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0046", "SP0046"]));
            Assert.That(
                factory.Outcomes["WrongAllowed"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["ExactAllowed"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["BaseAllowed"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["DerivedAllowed"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["WrongCatch"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["ExactCatch"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
        }
    }

    [Test]
    public async Task UnboundGenericExceptionContractsAreInvalid()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public sealed class GenericException<T> : Exception {
            }

            public static class Fixture {
                [AllowedExceptions(typeof(GenericException<>))]
                public static void InvalidAllowedExceptions() {
                }

                [EffectContract(
                    SharpProofEffect.Throws,
                    ThrownExceptions = new[] {
                        typeof(GenericException<>)
                    },
                    Complete = true)]
                public static void InvalidEffectContract() {
                }
            }
            """,
            mode: null,
            ["SP0024"],
            features: "effects");
        var messages = diagnostics.Select(diagnostic =>
            diagnostic.GetMessage(CultureInfo.InvariantCulture));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0024", "SP0024"]));
            Assert.That(
                messages,
                Has.Some.Contain("closed System.Exception-derived types"));
            Assert.That(
                messages,
                Has.Some.Contain("[EffectContract]"));
        }
    }

    [Test]
    public async Task ContractsOnlyStillRejectsInvalidEffectContractBits()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [EffectContract(
                    (SharpProofEffect)(1L << 40),
                    Complete = true)]
                public static void Invalid() {
                }
            }
            """,
            mode: null,
            ["SP0024"],
            features: "contracts");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0024"]));
        Assert.That(
            diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("[EffectContract]"));
    }

    [Test]
    public void AdvisoryDescriptorsUseProductionDefaults()
    {
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

    private sealed class ThrowingSessionFactory : IAnalyzerSessionFactory
    {
        private int _createCount;

        internal int CreateCount => Volatile.Read(ref _createCount);

        public AnalyzerSession Create(
            Compilation compilation,
            AnalyzerConfiguration configuration,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCount);
            throw new InvalidOperationException(
                "The profile-off analyzer must not construct a session.");
        }
    }

    private sealed class RecordingSessionFactory : IAnalyzerSessionFactory
    {
        private readonly ConcurrentDictionary<
            string,
            AnalyzerSemanticOutcome> _outcomes =
            new(StringComparer.Ordinal);

        internal ConcurrentDictionary<string, AnalyzerSemanticOutcome> Outcomes =>
            _outcomes;
        internal AnalyzerSession? Session
        {
            get; private set;
        }

        public AnalyzerSession Create(
            Compilation compilation,
            AnalyzerConfiguration configuration,
            CancellationToken cancellationToken)
        {
            Session = new AnalyzerSession(
                compilation,
                configuration,
                cancellationToken,
                (method, outcome) => _outcomes.AddOrUpdate(
                    method.Name,
                    outcome,
                    (_, current) =>
                        AnalyzerSemanticOutcomes.Combine(current, outcome)));
            return Session;
        }
    }

    private sealed class SpecReuseSessionFactory : IAnalyzerSessionFactory
    {
        internal AnalyzerSession? Session
        {
            get; private set;
        }

        public AnalyzerSession Create(
            Compilation compilation,
            AnalyzerConfiguration configuration,
            CancellationToken cancellationToken)
        {
            Session = new AnalyzerSession(
                compilation,
                configuration,
                cancellationToken);
            return Session;
        }
    }
}
