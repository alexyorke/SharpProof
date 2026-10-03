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
            ProfileFeaturesCompilation,
            mode: null,
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
    public async Task UnrelatedAttributesDoNotCreateAnAnalysisSession(
        string source)
    {
        var factory = new ThrowingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            source,
            mode: null,
            [],
            new SharpProofAnalyzer(factory));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(factory.CreateCount, Is.Zero);
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
            ConfigurationFailureCompilation,
            mode,
            new SharpProofAnalyzer(factory),
            profile: profile,
            features: features);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0025");
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], allowedValues);
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
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], "configuration provider failed");
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
            AnalyzerTestHost.AssertMessageContains(diagnostics[0], "option was removed");
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
            AnalyzerTestHost.AssertMessageContains(diagnostics[0], "option was removed");
            AnalyzerTestHost.AssertMessageContains(diagnostics[0], "everything");
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
            AnalyzerTestHost.AssertMessageContains(diagnostics[0], "option was removed");
            AnalyzerTestHost.AssertMessageContains(diagnostics[0], "strict");
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
    public async Task NondeterministicBoundaryDoesNotImplyRandomnessCapability()
    {
        var external = AnalyzerTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public static class ExternalFixture {
                [SharpProofTrusted("Reviewed native boundary.")]
                [EffectContract(
                    SharpProofEffect.ReadsAmbientState |
                        SharpProofEffect.UsesNativeCode,
                    Capabilities = SharpProofCapability.NativeInterop,
                    Complete = true,
                    IsDeterministic = false)]
                public static extern int NativeValue();

                [SharpProofTrusted("Reviewed clock boundary.")]
                [EffectContract(
                    SharpProofEffect.ReadsAmbientState,
                    Capabilities = SharpProofCapability.Clock,
                    Complete = true,
                    IsDeterministic = false)]
                public static extern int ClockValue();
            }
            """,
            "NondeterministicExternalFixture");

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [AllowedCapabilities(SharpProofCapability.NativeInterop)]
                public static int ReadNative() => ExternalFixture.NativeValue();

                [AllowedCapabilities(SharpProofCapability.Clock)]
                public static int ReadClock() => ExternalFixture.ClockValue();
            }
            """,
            "effects",
            ["SP0016"],
            additionalReferences: [external]);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task ExplicitNondeterminismStillRequiresDeclaredRandomnessCapability()
    {
        var external = AnalyzerTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public static class ExternalFixture {
                [SharpProofTrusted("Reviewed nondeterministic boundary.")]
                [EffectContract(
                    SharpProofEffect.UsesNondeterminism,
                    Capabilities = SharpProofCapability.Randomness,
                    Complete = true)]
                public static extern int RandomValue();
            }
            """,
            "ExplicitNondeterministicExternalFixture");

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [AllowedCapabilities(SharpProofCapability.Randomness)]
                public static int ReadRandom() => ExternalFixture.RandomValue();
            }
            """,
            "effects",
            ["SP0016"],
            additionalReferences: [external]);

        Assert.That(diagnostics, Is.Empty);
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
    public async Task ValidPartialMethodRecordsOneOutcome()
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
            Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
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
    public async Task ElidedCalleeRequiresStillReportsAnInvalidCallSite()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture
            {
                private static int RequiresPositive(int value)
                {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int InvalidCall() => RequiresPositive(0);
                public static int ValidCall() => RequiresPositive(1);
            }
            """,
            "contracts",
            [],
            new SharpProofAnalyzer());

        AnalyzerTestHost.AssertIds(diagnostics, "SP0027");
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
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], "'Selected'");
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
        var messages = diagnostics.Select(diagnostic =>
            diagnostic.GetMessage(CultureInfo.InvariantCulture)).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                messages.Count(static message =>
                    message.Contains(
                        "BodylessEffectContractNotEnforced",
                        StringComparison.Ordinal)),
                Is.EqualTo(3));
            Assert.That(
                messages.Count(static message =>
                    message.Contains(
                        "MissingOperationRoot",
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
        }
    }

    [Test]
    public async Task BodylessEffectAnnotationsAreReportedAsUnenforced()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public interface IFixture {
                [EnforcePure]
                void Run();
            }

            public sealed class ImpureFixture : IFixture {
                private static int state;

                public void Run() {
                    state++;
                }
            }
            """,
            "effects",
            ["SP0002", "SP0045", "SP0046", "SP0047"]);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0047");
        Assert.That(
            diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("BodylessEffectContractNotEnforced"));
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
    public async Task CustomEventAccessorsAreRejectedWithAnAnalyzedControl()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;
            public sealed class Fixture {
                private Action _handlers = static () => { };

                public event Action Changed {
                    [EnforcePure]
                    add { _handlers += value; }
                    [EnforcePure]
                    remove { }
                }

                [EnforcePure]
                public static void EmptyControl() { }
            }
            """,
            "effects",
            ["SP0047"],
            new SharpProofAnalyzer(factory));

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0047", 2);
            Assert.That(
                diagnostics.Select(static diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)),
                Has.All.Contains("Advisory:"));
            Assert.That(factory.OutcomeCounts["add_Changed"], Is.EqualTo(1));
            Assert.That(factory.OutcomeCounts["remove_Changed"], Is.EqualTo(1));
            Assert.That(
                factory.Outcomes["add_Changed"],
                Is.EqualTo(AnalyzerSemanticOutcome.Abstained));
            Assert.That(
                factory.Outcomes["remove_Changed"],
                Is.EqualTo(AnalyzerSemanticOutcome.Abstained));
            Assert.That(
                factory.Outcomes["EmptyControl"],
                Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
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
                AnalyzerTestHost.AssertIds(diagnostics);
                Assert.That(factory.OutcomeCounts["get_Value"], Is.EqualTo(1));
                Assert.That(factory.Outcomes["get_Value"],
                    Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
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
        AnalyzerTestHost.AssertMessageContains(
            diagnostics[0],
            "could not completely analyze selected method");
    }

    [Test]
    public async Task CompleteSourceEffectContractReportsAnUnprovenContract()
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
            [],
            new SharpProofAnalyzer(factory),
            features: "effects");

        AnalyzerTestHost.AssertIds(diagnostics, "SP0052");
        Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(
            diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("EffectContract")
                .And.Contain("does not cover its complete body summary"));
        Assert.That(
            factory.Outcomes["Write"],
            Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
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
            [],
            new SharpProofAnalyzer(factory),
            features: "effects");

        AnalyzerTestHost.AssertIds(diagnostics, "SP0046");
        Assert.That(
            factory.Outcomes["RethrowBeforeSiblingCatch"],
            Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
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
            [],
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
            [],
            features: "contracts");

        AnalyzerTestHost.AssertIds(diagnostics, "SP0024");
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], "[EffectContract]");
    }

    [Test]
    public void AdvisoryDescriptorsUseProductionDefaults()
    {
        var descriptors = GeneratedDiagnosticDescriptors.SupportedDiagnostics;
        // SP0050 joins SP0049 as an infrastructure error. SP0052 is a warning
        // because a complete body summary does not satisfy its declared
        // effect contract.
        var informational = descriptors.Where(static descriptor =>
            descriptor.Id is not
                ("SP0024" or "SP0025" or "SP0027" or "SP0049" or "SP0050" or "SP0052"));

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
            descriptors.Single(static descriptor => descriptor.Id == "SP0052")
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
