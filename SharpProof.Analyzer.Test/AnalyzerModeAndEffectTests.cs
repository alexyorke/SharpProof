using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

    private static readonly CSharpCompilation ConfigurationFailureCompilation =
        AnalyzerTestHost.CreateCompilation(
            ModeFixture,
            ["SP0025", "SP0045"]);

    private static readonly CSharpCompilation RetiredModeCompilation =
        AnalyzerTestHost.CreateCompilation(
            ModeFixture,
            ["SP0025", "SP0045", "SP0027"]);

    private static readonly CSharpCompilation ProfileFeaturesCompilation =
        AnalyzerTestHost.CreateCompilation(
            ModeFixture,
            ["SP0045", "SP0027"]);

    private static readonly CSharpCompilation ContractCompanionCompilation =
        CreateContractCompanionCompilation();

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
        var factory = new RecordingSessionFactory();
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
        var factory = new RecordingSessionFactory();
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
        var factory = new RecordingSessionFactory();
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
        var factory = new RecordingSessionFactory();
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
    [TestCase(null, "   ", null, "advisory, strict, off")]
    [TestCase(null, null, "\t", "effects, contracts, all")]
    [TestCase("everything", null, null, "option was removed")]
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

        AnalyzerTestHost.AssertIds(diagnostics, "SP0025");
        Assert.That(
            diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain(allowedValues));
        Assert.That(factory.CreateCount, Is.Zero);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ConfigurationProviderFailureReportsAndSuppressesAnalysis(
        bool failGlobalOptions)
    {
        var factory = new ThrowingSessionFactory();
        var compilation = ConfigurationFailureCompilation;
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            new FailingOptionsProvider(failGlobalOptions),
            new SharpProofAnalyzer(factory));

        AnalyzerTestHost.AssertIds(diagnostics, "SP0025");
        Assert.That(
            diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("configuration provider failed"));
        Assert.That(factory.CreateCount, Is.Zero);
    }

    [TestCase("off")]
    [TestCase("effects")]
    [TestCase("contracts")]
    [TestCase("all-experimental")]
    public async Task RetiredModeOptionFailsClosed(string retiredMode)
    {
        var compilation = RetiredModeCompilation;
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sharpproof_mode"] = retiredMode
            });

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0025");
            Assert.That(
                diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("option was removed"));
        }
    }

    [Test]
    public async Task LowercaseRetiredBuildPropertyFailsClosed()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            ModeFixture,
            ["SP0025"]);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.sharpproof_mode"] = "everything"
            });

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0025");
            Assert.That(
                diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("option was removed"));
            Assert.That(
                diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("everything"));
        }
    }

    [Test]
    public async Task BlankRetiredEditorConfigAliasDoesNotHideMsBuildAlias()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            ModeFixture,
            ["SP0025"]);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sharpproof_mode"] = "  ",
                ["build_property.SharpProofMode"] = "strict"
            });

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0025");
            Assert.That(
                diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("option was removed"));
            Assert.That(
                diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("strict"));
        }
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
            ProfileFeaturesCompilation,
            mode: null,
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
                    PreconditionFree = true,
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
    public async Task LambdaOwnedEffectAttributesAreAnalyzed()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int state;

                public static void Configure() {
                    Action pure = [EnforcePure] () => state++;
                    Func<object> allocate = [ZeroAllocations] () => new object();
                    Func<int> divide = [DoesNotThrow] () => 1 / state;
                    Action declared =
                        [EffectContract(SharpProofEffect.None, Complete = true)]
                        () => state++;
                    _ = pure;
                    _ = allocate;
                    _ = divide;
                    _ = declared;
                }
            }
            """,
            "effects",
            ["SP0002", "SP0045", "SP0046", "SP0047"],
            new SharpProofAnalyzer(factory));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EquivalentTo(["SP0002", "SP0045", "SP0046", "SP0047"]));
            Assert.That(
                factory.Outcomes.Values,
                Has.Some.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
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
    public async Task IncompatibleCastCannotEstablishCalleePrecondition()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static void Need(object value) {
                    Contract.Requires((IDisposable)value != null);
                }

                [DoesNotThrow]
                public static void Call() => Need("text");
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
                    PreconditionFree = true,
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
    public void BodylessContractWithClosedPreconditionRemainsUnknown()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public abstract class Fixture {
                [EnforcePure]
                [SharpProofTrusted("reviewed bodyless implementation")]
                [EffectContract(
                    SharpProofEffect.None,
                    IsDeterministic = true,
                    PreconditionFree = true,
                    Complete = true)]
                public abstract void Restricted([Positive] int value);
            }
            """,
            ["SP0045"]);
        var session = new AnalyzerSession(
            compilation,
            AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None);
        var method = compilation.GetTypeByMetadataName("Fixture")!
            .GetMembers("Restricted")
            .OfType<IMethodSymbol>()
            .Single();
        Assert.That(session.HasPotentialCallPreconditions(method), Is.True);
        var analyzed = session.AnalyzeEffects(method, CancellationToken.None);
        Assert.That(
            analyzed.Summary.AnalysisIncompleteReason,
            Is.EqualTo(
                SharpProof.Effects.EffectAnalysisIncompleteReason
                    .CallPreconditionNotProven));
        var evaluation = EffectContractDiagnostics.Evaluate(
            method,
            Location.None,
            session,
            static _ => { },
            CancellationToken.None)
            .Single(static item =>
                item.Kind == EffectEvaluationContractKind.EnforcePure);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                evaluation.Outcome,
                Is.EqualTo(EffectEvaluationOutcome.Unknown));
            Assert.That(
                evaluation.Evidence,
                Does.Contain("CallPreconditionNotProven"));
            Assert.That(
                evaluation.Reason,
                Is.EqualTo(EffectEvaluationReason.EffectSummaryIncomplete));
        }
    }

    [Test]
    public void DirectExternalAnalyzerAnalysisAppliesClosedEntryPreconditions()
    {
        var external = AnalyzerTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public static class ExternalFixture {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.None,
                    IsDeterministic = true,
                    PreconditionFree = true,
                    Complete = true)]
                public static void Restricted([Positive] int value) {
                }
            }
            """,
            "DirectExternalAnalyzerPreconditionAssembly");
        var compilation = AnalyzerTestHost.CreateCompilation(
            "public static class Fixture { }",
            ["SP0045"],
            additionalReferences: [external]);
        var session = new AnalyzerSession(
            compilation,
            AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None);
        var method = compilation.GetTypeByMetadataName("ExternalFixture")!
            .GetMembers("Restricted")
            .OfType<IMethodSymbol>()
            .Single();

        var result = session.AnalyzeEffects(
            method,
            CancellationToken.None);

        Assert.That(
            result.Summary.AnalysisIncompleteReason,
            Is.EqualTo(
                SharpProof.Effects.EffectAnalysisIncompleteReason
                    .CallPreconditionNotProven));
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
            [],
            new SharpProofAnalyzer(factory));

        AnalyzerTestHost.AssertIds(diagnostics, "SP0046");
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

    [TestCase("definition")]
    [TestCase("implementation")]
    [TestCase("both")]
    public async Task PartialMethodHasOneExecutableEffectOwner(
        string attributePlacement)
    {
        var definitionAttribute = attributePlacement is "definition" or "both"
            ? "[EnforcePure]"
            : string.Empty;
        var implementationAttribute = attributePlacement is "implementation" or "both"
            ? "[EnforcePure]"
            : string.Empty;
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            $$"""
            using SharpProof.Attributes;

            public static partial class Fixture {
                private static int State;

                {{definitionAttribute}}
                public static partial void Write();

                {{implementationAttribute}}
                public static partial void Write() {
                    State = 1;
                }
            }
            """,
            "effects",
            [],
            new SharpProofAnalyzer(factory),
            allowCompilationErrors: true);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0002");
        Assert.That(
            factory.OutcomeCounts["Write"],
            Is.EqualTo(1));
    }

    [Test]
    public async Task GeneratedPartialDefinitionSelectsHandwrittenImplementation()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            // <auto-generated />
            using SharpProof.Attributes;
            public static partial class Fixture {
                [EnforcePure]
                public static partial void Write();
            }
            """,
            ["SP0002"],
            filePath: "Fixture.g.cs");
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            """
            public static partial class Fixture {
                private static int State;
                public static partial void Write() { State = 1; }
            }
            """,
            new CSharpParseOptions(LanguageVersion.Preview),
            "Fixture.cs"));
        var factory = new RecordingSessionFactory();

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            "effects",
            new SharpProofAnalyzer(factory));

        AnalyzerTestHost.AssertIds(diagnostics, "SP0002");
        Assert.That(
            diagnostics[0].Location.SourceTree!.FilePath,
            Is.EqualTo("Fixture.cs"));
        Assert.That(factory.OutcomeCounts["Write"], Is.EqualTo(1));
    }

    [Test]
    public async Task GeneratedImplicitEmptyConstructorHasAnExactEffectProjection()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [EffectContract(SharpProofEffect.Allocates, Complete = true)]
                public static GeneratedEmpty Create() => new();
            }
            """,
            ["SP0024"],
            filePath: "Fixture.cs");
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            """
            // <auto-generated />
            public sealed class GeneratedEmpty { }
            """,
            new CSharpParseOptions(LanguageVersion.Preview),
            "GeneratedEmpty.g.cs"));
        var factory = new RecordingSessionFactory();

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            "effects",
            new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            factory.Outcomes["Create"],
            Is.EqualTo(AnalyzerSemanticOutcome.Proven));
    }

    [Test]
    public async Task ValidPartialMethodRecordsOneProvenOutcome()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;
            public static partial class Fixture {
                [EnforcePure]
                public static partial void Read();
                public static partial void Read() { }
            }
            """,
            "effects",
            ["SP0002"],
            new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.OutcomeCounts["Read"], Is.EqualTo(1));
        Assert.That(
            factory.Outcomes["Read"],
            Is.EqualTo(AnalyzerSemanticOutcome.Proven));
    }

    [Test]
    public async Task PartialPropertyAccessorHasOneExecutableEffectOwner()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;
            public static partial class Fixture {
                private static int State;
                public static partial int Value { [EnforcePure] get; }
                public static partial int Value {
                    get { State = 1; return State; }
                }
            }
            """,
            "effects",
            [],
            new SharpProofAnalyzer(factory));

        AnalyzerTestHost.AssertIds(diagnostics, "SP0002");
        Assert.That(factory.OutcomeCounts["get_Value"], Is.EqualTo(1));
    }

    [Test]
    public async Task ConflictingPartialEffectContractsAreReportedOnce()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;
            public static partial class Fixture {
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static partial void Execute();
                [EffectContract(SharpProofEffect.Allocates, Complete = true)]
                public static partial void Execute() { }
            }
            """,
            "effects",
            []);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0024");
    }

    [Test]
    public async Task ConcurrentPartialMethodRunsEachReportExactlyOnce()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static partial class Fixture {
                private static int State;
                [EnforcePure]
                public static partial void Write();
                public static partial void Write() { State = 1; }
            }
            """;
        var factories = Enumerable.Range(0, 4)
            .Select(static _ => new RecordingSessionFactory())
            .ToArray();

        var runs = await Task.WhenAll(factories.Select(factory =>
            AnalyzerTestHost.AnalyzeAsync(
                source,
                "effects",
                ["SP0002"],
                new SharpProofAnalyzer(factory))));

        Assert.That(
            runs.Select(static diagnostics => diagnostics.Length),
            Is.EqualTo(Enumerable.Repeat(1, 4)));
        Assert.That(
            factories.Select(factory => factory.OutcomeCounts["Write"]),
            Is.EqualTo(Enumerable.Repeat(1, 4)));
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
    public async Task MutationBearingBranchCannotHideAReachablePurityViolation()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int State;

                [EnforcePure]
                public static void Run() {
                    var value = 1;
                    if (value + (value = 2) == 4) {
                    }
                    else {
                        State++;
                    }
                }
            }
            """,
            "effects",
            [],
            new SharpProofAnalyzer(factory));

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0002");
            Assert.That(
                factory.Outcomes["Run"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task PostCatchExecutionCannotHideAReachablePurityViolation()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int State;

                [EnforcePure]
                public static void Run() {
                    try {
                        throw new InvalidOperationException();
                    }
                    catch (InvalidOperationException) {
                    }

                    State++;
                }
            }
            """,
            "effects",
            [],
            new SharpProofAnalyzer(factory));

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0002");
            Assert.That(
                factory.Outcomes["Run"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
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
            [],
            new SharpProofAnalyzer(factory));

        AnalyzerTestHost.AssertIds(diagnostics, "SP0002");
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
            [],
            new SharpProofAnalyzer(factory));

        AnalyzerTestHost.AssertIds(diagnostics, "SP0027");
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
            [],
            new SharpProofAnalyzer(factory));

        AnalyzerTestHost.AssertIds(diagnostics, "SP0027", "SP0027");
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
            [],
            new SharpProofAnalyzer(factory));

        AnalyzerTestHost.AssertIds(diagnostics, "SP0027");
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
            [],
            new SharpProofAnalyzer(factory));

        AnalyzerTestHost.AssertIds(diagnostics, "SP0027");
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
            [],
            new SharpProofAnalyzer(factory));

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0045", "SP0045", "SP0045");
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
            [],
            new SharpProofAnalyzer(factory));

        AnalyzerTestHost.AssertIds(diagnostics, "SP0016", 3);
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
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["WrappedThrowingConstructor"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
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

        AnalyzerTestHost.AssertIds(diagnostics, "SP0047");
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

        AnalyzerTestHost.AssertIds(diagnostics, "SP0047", "SP0047", "SP0047");
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

    [TestCase("contracts")]
    [TestCase("all")]
    [TestCase("effects")]
    public async Task ContractCompanionBodyIsNotAnalyzedAsAnImplementation(
        string features)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            ContractCompanionCompilation,
            mode: null,
            features: features);

        Assert.That(diagnostics, Is.Empty);
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

        AnalyzerTestHost.AssertIds(diagnostics, "SP0047", 4);
        Assert.That(
            diagnostics.Select(diagnostic =>
                diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Has.All.Contain("MissingOperationRoot"));
    }

    [Test]
    public async Task ConcreteSelectedAutoAccessorsAbstainExactlyOnce()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public sealed class Fixture {
                public int Read { [EnforcePure] get; }
                public int Write { get; [EnforcePure] set; }
                public int Initialize { get; [EnforcePure] init; }

                public int Explicit {
                    [EnforcePure]
                    get { return 1; }
                }
            }
            """,
            "effects",
            [],
            new SharpProofAnalyzer(factory));

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0047", 3);
            Assert.That(
                diagnostics.Select(diagnostic => diagnostic.GetMessage(
                    CultureInfo.InvariantCulture)),
                Has.All.Contain("MissingOperationRoot"));
            Assert.That(factory.OutcomeCounts["get_Read"], Is.EqualTo(1));
            Assert.That(factory.OutcomeCounts["set_Write"], Is.EqualTo(1));
            Assert.That(factory.OutcomeCounts["set_Initialize"], Is.EqualTo(1));
            Assert.That(factory.OutcomeCounts["get_Explicit"], Is.EqualTo(1));
            Assert.That(factory.Outcomes["get_Read"],
                Is.EqualTo(AnalyzerSemanticOutcome.Abstained));
            Assert.That(factory.Outcomes["set_Write"],
                Is.EqualTo(AnalyzerSemanticOutcome.Abstained));
            Assert.That(factory.Outcomes["set_Initialize"],
                Is.EqualTo(AnalyzerSemanticOutcome.Abstained));
            Assert.That(factory.Outcomes["get_Explicit"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
        }
    }

    [Test]
    public async Task SuppressedAndGeneratedAutoAccessorsFollowExistingPolicy()
    {
        var suppressedFactory = new RecordingSessionFactory();
        var suppressed = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;
            public sealed class Fixture {
                public int Value {
                    [SharpProofSuppress("Reviewed generated storage boundary.")]
                    [EnforcePure]
                    get;
                }
            }
            """,
            "effects",
            ["SP0047"],
            new SharpProofAnalyzer(suppressedFactory));
        var generatedFactory = new RecordingSessionFactory();
        var generated = await AnalyzerTestHost.AnalyzeAsync(
            """
            // <auto-generated />
            using SharpProof.Attributes;
            public sealed class Fixture {
                public int Value { [EnforcePure] get; }
            }
            """,
            "effects",
            ["SP0047"],
            new SharpProofAnalyzer(generatedFactory),
            filePath: "Fixture.g.cs");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(suppressed, Is.Empty);
            Assert.That(suppressedFactory.OutcomeCounts["get_Value"],
                Is.EqualTo(1));
            Assert.That(suppressedFactory.Outcomes["get_Value"],
                Is.EqualTo(AnalyzerSemanticOutcome.Suppressed));
            Assert.That(generated, Is.Empty);
            Assert.That(generatedFactory.Outcomes, Is.Empty);
        }
    }

    [Test]
    public async Task BodylessInterfaceEventAccessorsAbstainExactlyOnce()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;
            public interface IFixture {
                event Action Changed {
                    [EnforcePure] add;
                    [EnforcePure] remove;
                }
            }
            """,
            "effects",
            [],
            new SharpProofAnalyzer(factory),
            allowCompilationErrors: true);

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0047", 2);
            Assert.That(factory.OutcomeCounts["add_Changed"], Is.EqualTo(1));
            Assert.That(factory.OutcomeCounts["remove_Changed"], Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ConcurrentAutoAccessorRunsReconcileExactlyOnce()
    {
        var runs = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            var factory = new RecordingSessionFactory();
            var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
                """
                using SharpProof.Attributes;
                public sealed class Fixture {
                    public int Value { [EnforcePure] get; }
                }
                """,
                "effects",
                [],
                new SharpProofAnalyzer(factory));
            return (diagnostics, factory);
        }));

        foreach (var (diagnostics, factory) in runs)
        {
            using (Assert.EnterMultipleScope())
            {
                AnalyzerTestHost.AssertIds(diagnostics, "SP0047");
                Assert.That(factory.OutcomeCounts["get_Value"], Is.EqualTo(1));
                Assert.That(factory.Outcomes["get_Value"],
                    Is.EqualTo(AnalyzerSemanticOutcome.Abstained));
            }
        }
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
            []);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0046", 8);
    }

    [Test]
    public async Task ProvenSafeConversionsSatisfyDoesNotThrow()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [DoesNotThrow]
                public static int? NullToNullableValue() =>
                    (int?)(object?)null;

                [DoesNotThrow]
                public static string? NullToReference() =>
                    (string?)(object?)null;

                [DoesNotThrow]
                public static int PresentNullableUnwrap() {
                    int? value = 1;
                    return (int)value;
                }

                [DoesNotThrow]
                public static int? CompatibleNullableUnbox() =>
                    (int?)(object)1;

                [DoesNotThrow]
                public static string CompatibleReferenceCast() =>
                    (string)(object)"text";
            }
            """,
            "effects",
            ["SP0046"],
            new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        foreach (var methodName in new[]
                 {
                     "NullToNullableValue",
                     "NullToReference",
                     "PresentNullableUnwrap",
                     "CompatibleNullableUnbox",
                     "CompatibleReferenceCast"
                 })
        {
            Assert.That(
                factory.Outcomes[methodName],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven),
                methodName);
        }
    }

    [Test]
    public async Task NullableBoxingProjectsZeroAllocationsFromPresence()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [ZeroAllocations]
                public static object? Empty() => (int?)null;

                [ZeroAllocations]
                public static object? Present() {
                    int? value = 1;
                    return value;
                }

                [ZeroAllocations]
                public static object? Unknown(int? value) => value;

                [ZeroAllocations]
                public static object? LiftedEmpty() {
                    int? source = null;
                    long? value = source;
                    return value;
                }

                [ZeroAllocations]
                public static object? LiftedPresent() {
                    int? source = 1;
                    long? value = source;
                    return value;
                }

                [ZeroAllocations]
                public static object OrdinaryValue(int value) => value;
            }
            """,
            "effects",
            [],
            new SharpProofAnalyzer(factory));

        AnalyzerTestHost.AssertIds(diagnostics, "SP0045", 4);
        Assert.That(
            diagnostics.Single(diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)
                        .Contains("'Unknown'", StringComparison.Ordinal))
                .GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("AllocationUnknown"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                factory.Outcomes["Empty"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["LiftedEmpty"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["Unknown"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            foreach (var methodName in new[]
                     {
                         "Present",
                         "LiftedPresent",
                         "OrdinaryValue"
                     })
            {
                Assert.That(
                    factory.Outcomes[methodName],
                    Is.EqualTo(AnalyzerSemanticOutcome.Unknown),
                    methodName);
                var message = diagnostics.Single(diagnostic =>
                        diagnostic.GetMessage(CultureInfo.InvariantCulture)
                            .Contains(
                                "'" + methodName + "'",
                                StringComparison.Ordinal))
                    .GetMessage(CultureInfo.InvariantCulture);
                Assert.That(message, Does.Contain("allocation: Managed"));
                Assert.That(message, Does.Not.Contain("AllocationUnknown"));
            }
        }
    }

    [Test]
    public async Task DefinitelyAbsentLiftedArithmeticSatisfiesDoesNotThrow()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [DoesNotThrow]
                public static int? DivideNullLeft() {
                    int? left = null;
                    int? right = 0;
                    return left / right;
                }

                [DoesNotThrow]
                public static int? DivideNullRight() {
                    int? left = int.MinValue;
                    int? right = null;
                    return left / right;
                }

                [DoesNotThrow]
                public static int? DivideBothNull() {
                    int? left = null;
                    int? right = null;
                    return left / right;
                }

                [DoesNotThrow]
                public static int? RemainderNullRight() {
                    int? left = int.MinValue;
                    int? right = null;
                    return left % right;
                }

                [DoesNotThrow]
                public static uint? UnsignedDivideNullLeft() {
                    uint? left = null;
                    uint? right = 0;
                    return left / right;
                }

                [DoesNotThrow]
                public static int? CheckedAddNullLeft() {
                    int? left = null;
                    int? right = int.MaxValue;
                    return checked(left + right);
                }

                [DoesNotThrow]
                public static int? CheckedNegateNull() {
                    int? value = null;
                    return checked(-value);
                }

                [DoesNotThrow]
                public static int? CheckedIncrementNull() {
                    int? value = null;
                    checked { value++; }
                    return value;
                }

                [DoesNotThrow]
                public static int? DivideAssignNullRight() {
                    int? left = 1;
                    int? right = null;
                    left /= right;
                    return left;
                }

                [DoesNotThrow]
                public static int? CheckedAddAssignNull() {
                    int? left = null;
                    int? right = int.MaxValue;
                    checked { left += right; }
                    return left;
                }

                [DoesNotThrow]
                public static int? UncheckedAddNull() {
                    int? left = null;
                    int? right = int.MaxValue;
                    return unchecked(left + right);
                }

                [DoesNotThrow]
                public static long? CheckedConversionNull() {
                    int? value = null;
                    return checked((long?)value);
                }

                [DoesNotThrow]
                public static int? PresentDivideZero() {
                    int? left = 1;
                    int? right = 0;
                    return left / right;
                }

                [DoesNotThrow]
                public static int? PresentCheckedAdd() {
                    int? left = int.MaxValue;
                    int? right = 1;
                    return checked(left + right);
                }

                [DoesNotThrow]
                public static int? UnknownDivide(int? left, int? right) =>
                    left / right;

                [DoesNotThrow]
                public static int? UnknownCheckedAdd(int? left, int? right) =>
                    checked(left + right);
            }
            """,
            "effects",
            [],
            new SharpProofAnalyzer(factory));

        var unsafeMethods = new[]
        {
            "PresentDivideZero",
            "PresentCheckedAdd",
            "UnknownDivide",
            "UnknownCheckedAdd"
        };
        AnalyzerTestHost.AssertIds(diagnostics, "SP0046", unsafeMethods.Length);
        foreach (var methodName in unsafeMethods)
        {
            Assert.That(
                diagnostics.Select(diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)),
                Has.Some.Contain("'" + methodName + "'"),
                methodName);
            Assert.That(
                factory.Outcomes[methodName],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown),
                methodName);
        }

        foreach (var methodName in new[]
                 {
                     "DivideNullLeft",
                     "DivideNullRight",
                     "DivideBothNull",
                     "RemainderNullRight",
                     "UnsignedDivideNullLeft",
                     "CheckedAddNullLeft",
                     "CheckedNegateNull",
                     "CheckedIncrementNull",
                     "DivideAssignNullRight",
                     "CheckedAddAssignNull",
                     "UncheckedAddNull",
                     "CheckedConversionNull"
                 })
        {
            Assert.That(
                factory.Outcomes[methodName],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven),
                methodName);
        }
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

        AnalyzerTestHost.AssertIds(diagnostics, "SP0047");
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
            AnalyzerTestHost.AssertIds(diagnostics, "SP0047", "SP0047");
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

        AnalyzerTestHost.AssertIds(diagnostics, "SP0047", "SP0045", "SP0047");
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

        AnalyzerTestHost.AssertIds(diagnostics, "SP0047");
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

        AnalyzerTestHost.AssertIds(diagnostics, "SP0046");
        Assert.That(
            factory.Outcomes["RethrowBeforeSiblingCatch"],
            Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }

    [Test]
    public async Task SelectedGeneratedNestedCatchUsesOnlyInnerRethrow()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            // <auto-generated />
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                [AllowedExceptions(
                    typeof(ApplicationException),
                    typeof(NullReferenceException))]
                public static void NestedCatch(
                    [NotNull] InvalidOperationException outer,
                    [NotNull] ApplicationException inner) {
                    Contract.Requires(outer != null);
                    Contract.Requires(inner != null);
                    try { throw outer; }
                    catch (InvalidOperationException) {
                        try { throw inner; }
                        catch (ApplicationException) { throw; }
                    }
                }
            }
            """,
            mode: null,
            ["SP0046"],
            new SharpProofAnalyzer(factory),
            features: "effects",
            filePath: "Fixture.g.cs");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            factory.Outcomes["NestedCatch"],
            Is.EqualTo(AnalyzerSemanticOutcome.Proven));
    }

    [Test]
    public async Task SelectedGeneratedCompileTimeFalseLoopHasNoBodyEffects()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            // <auto-generated />
            using SharpProof.Attributes;

            public static class Fixture {
                private static object? state;

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void FalseLoop() {
                    while (false) {
                        state = new object();
                        throw new System.Exception();
                    }
                }
            }
            """,
            mode: null,
            ["SP0047"],
            new SharpProofAnalyzer(factory),
            features: "effects",
            filePath: "Fixture.g.cs");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            factory.Outcomes["FalseLoop"],
            Is.EqualTo(AnalyzerSemanticOutcome.Proven));
    }

    [Test]
    public async Task SelectedGeneratedFreshObjectInitializerHasNoObservableWrite()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            // <auto-generated />
            using SharpProof.Attributes;

            public sealed class Value {
                public int Field;
            }

            public static class Fixture {
                [EffectContract(SharpProofEffect.Allocates, Complete = true)]
                public static Value Create() => new Value { Field = 1 };
            }
            """,
            mode: null,
            ["SP0047"],
            new SharpProofAnalyzer(factory),
            features: "effects",
            filePath: "Fixture.g.cs");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            factory.Outcomes["Create"],
            Is.EqualTo(AnalyzerSemanticOutcome.Proven));
    }

    [Test]
    public async Task SelectedGeneratedBasePropertyIsStaticallyDispatched()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            // <auto-generated />
            using SharpProof.Attributes;

            public class Base {
                private int _value;
                public virtual int Value => _value;
            }

            public sealed class Derived : Base {
                [EffectContract(
                    SharpProofEffect.ReadsReceiverState,
                    Complete = true)]
                public int Read() => base.Value;
            }
            """,
            mode: null,
            ["SP0047"],
            new SharpProofAnalyzer(factory),
            features: "effects",
            filePath: "Fixture.g.cs");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            factory.Outcomes["Read"],
            Is.EqualTo(AnalyzerSemanticOutcome.Proven));
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
            AnalyzerTestHost.AssertIds(diagnostics, "SP0046", "SP0046", "SP0046");
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
            AnalyzerTestHost.AssertIds(diagnostics, "SP0047");
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
            AnalyzerTestHost.AssertIds(diagnostics, "SP0046");
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
    public async Task AllowedExceptionDiagnosticsRetainNamespaceIdentity()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;
            namespace First { public sealed class SameException : System.Exception { } }
            namespace Second { public sealed class SameException : System.Exception { } }
            public static class Fixture {
                [DoesNotThrow]
                public static void Run(bool first) {
                    if (first) throw new First.SameException();
                    throw new Second.SameException();
                }
            }
            """,
            mode: null,
            ["SP0046"],
            new SharpProofAnalyzer(new RecordingSessionFactory()),
            features: "effects");

        var message = diagnostics.Single(static diagnostic => diagnostic.Id == "SP0046")
            .GetMessage(CultureInfo.InvariantCulture);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("First.SameException"));
            Assert.That(message, Does.Contain("Second.SameException"));
        }
    }

    [Test]
    public async Task AllowedExceptionsUsesRuntimeTypeForDefinitelyNullThrows()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            // <auto-generated />
            #nullable enable
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                [AllowedExceptions(typeof(NullReferenceException))]
                public static void NullAllowed() {
                    InvalidOperationException? exception = null;
                    throw exception!;
                }

                [AllowedExceptions(typeof(InvalidOperationException))]
                public static void NullDisallowed() {
                    InvalidOperationException? exception = null;
                    throw exception;
                }

                [AllowedExceptions(typeof(NullReferenceException))]
                public static void MaybeNull(
                    InvalidOperationException? exception) =>
                    throw exception;

                [AllowedExceptions(typeof(InvalidOperationException))]
                public static void NonNull() =>
                    throw new InvalidOperationException();
            }
            """,
            mode: null,
            ["SP0046"],
            new SharpProofAnalyzer(factory),
            features: "effects",
            filePath: "Fixture.g.cs");

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0046", "SP0046");
            Assert.That(
                diagnostics.Select(static diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)),
                Has.Exactly(1).Contains("NullReferenceException"));
            Assert.That(
                diagnostics.Select(static diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)),
                Has.Exactly(1).Contains("InvalidOperationException"));
            Assert.That(
                factory.Outcomes["NullAllowed"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.Outcomes["NullDisallowed"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["MaybeNull"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(
                factory.Outcomes["NonNull"],
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
            AnalyzerTestHost.AssertIds(diagnostics, "SP0046", "SP0046");
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
            AnalyzerTestHost.AssertIds(diagnostics, "SP0024", "SP0024");
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

        AnalyzerTestHost.AssertIds(diagnostics, "SP0024");
        Assert.That(
            diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("[EffectContract]"));
    }

    [Test]
    public void AdvisoryDescriptorsUseProductionDefaults()
    {
        var descriptors = GeneratedDiagnosticDescriptors.SupportedDiagnostics;
        // SP0050 joins SP0049 as an infrastructure error: both report that
        // SharpProof could not do its job, which is not an advisory finding
        // about the user's code.
        var informational = descriptors.Where(static descriptor =>
            descriptor.Id is not
                ("SP0024" or "SP0025" or "SP0027" or "SP0049" or "SP0050"));

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

    private static CSharpCompilation CreateContractCompanionCompilation()
    {
        return AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public sealed class Service {
                public int Map(int value) => value;
            }

            [ContractFor(typeof(Service))]
            public static class ServiceContracts {
                private static int state;

                public static int Map(Service receiver, int value) {
                    Contract.Requires(value > 0);
                    Action unsupportedDummy = () => state++;
                    if (value < 0) {
                        throw new InvalidOperationException();
                    }
                    unsupportedDummy();
                    return value;
                }
            }

            """,
            ["SP0027", "SP0047"]);
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

    private sealed class FailingOptionsProvider(bool failGlobalOptions)
        : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions Empty =
            new EmptyOptions();
        private static readonly AnalyzerConfigOptions Failing =
            new FailingOptions();

        public override AnalyzerConfigOptions GlobalOptions =>
            failGlobalOptions ? Failing : Empty;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return failGlobalOptions ? Empty : Failing;
        }

        public override AnalyzerConfigOptions GetOptions(
            AdditionalText textFile)
        {
            return Empty;
        }
    }

    private sealed class EmptyOptions : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = string.Empty;
            return false;
        }
    }

    private sealed class FailingOptions : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (key.Contains("sharpproof", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("options lookup failed");
            }

            value = string.Empty;
            return false;
        }
    }

}
