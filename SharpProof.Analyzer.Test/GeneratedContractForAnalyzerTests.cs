using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class GeneratedContractForAnalyzerTests
{
    private const string Target = """
        using SharpProof.Attributes;

        public interface IService
        {
            int Map(int value);
        }
        """;

    [Test]
    public async Task GeneratedCompanionIsValidatedFromFinalCompilation()
    {
        var valid = await AnalyzeGeneratedAsync("""
            using SharpProof.Attributes;

            [ContractFor(typeof(IService))]
            public static class ServiceContracts
            {
                public static int Map(IService receiver, int value) => value;
            }
            """);
        var malformed = await AnalyzeGeneratedAsync("""
            using SharpProof.Attributes;

            [ContractFor(typeof(IService))]
            public static class ServiceContracts
            {
            }
            """);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(valid, Is.Empty);
            Assert.That(
                malformed.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SPCF0004"]));
        }
    }

    [Test]
    public async Task PeerGeneratedRejectedContractForIsReported()
    {
        var diagnostics = await AnalyzeGeneratedAsync(
            """
            namespace SharpProof.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class ContractForAttribute : System.Attribute
                {
                    public ContractForAttribute(System.Type target) { }
                }
            }

            [SharpProof.Attributes.ContractFor(typeof(IService))]
            public static class RejectedContracts
            {
                public static int Map(IService receiver, int value) => value;
            }
            """);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SPCF0001"]));
    }

    [TestCase("advisory", "contracts", true)]
    [TestCase("advisory", "all", true)]
    [TestCase("advisory", "effects", true)]
    [TestCase("strict", "contracts", true)]
    [TestCase("strict", "all", true)]
    [TestCase("strict", "effects", true)]
    [TestCase("off", "contracts", false)]
    [TestCase("off", "all", false)]
    [TestCase("off", "effects", false)]
    public async Task RejectedGeneratedCompanionUsesProfileAndFeatureAuthority(
        string profile,
        string features,
        bool expected)
    {
        var diagnostics = await AnalyzeGeneratedAsync(
            RejectedContractForSource,
            profile: profile,
            features: features);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            expected ? Is.EqualTo(["SPCF0001"]) : Is.Empty);
    }

    [Test]
    public async Task PeerGeneratedRejectedContractForDoesNotDependOnHintName()
    {
        var diagnostics = await AnalyzeGeneratedAsync(
            RejectedContractForSource,
            hintName: "PeerContracts.cs");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SPCF0001"]));
    }

    [Test]
    public async Task MalformedPeerCompanionWithOrdinaryHintIsReconciled()
    {
        var diagnostics = await AnalyzeGeneratedAsync(
            """
            using SharpProof.Attributes;

            [ContractFor(typeof(IService))]
            public static class ServiceContracts
            {
            }
            """,
            hintName: "PeerContracts.cs");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SPCF0004"]));
    }

    [Test]
    public async Task PeerGeneratorOrderDoesNotChangeFinalReconciliation()
    {
        const string malformed = """
            using SharpProof.Attributes;

            [ContractFor(typeof(IService))]
            public static class ServiceContracts
            {
            }
            """;
        var forward = await AnalyzeGeneratedInOrderAsync(malformed, reverse: false);
        var reverse = await AnalyzeGeneratedInOrderAsync(malformed, reverse: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                forward.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SPCF0004"]));
            Assert.That(
                reverse.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SPCF0004"]));
        }
    }

    [Test]
    public async Task GeneratedAndHandwrittenCompanionsReportTheGeneratedOverlap()
    {
        const string handwritten = """
            using SharpProof.Attributes;

            public interface IService
            {
                int Map(int value);
            }

            [ContractFor(typeof(IService))]
            public static class HandwrittenContracts
            {
                public static int Map(IService receiver, int value) => value;
            }
            """;
        var diagnostics = await AnalyzeGeneratedAsync(
            """
            using SharpProof.Attributes;

            [ContractFor(typeof(IService))]
            public static class GeneratedContracts
            {
                public static int Map(IService receiver, int value) => value;
            }
            """,
            handwritten);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SPCF0002", "SPCF0002"]));
        Assert.That(
            diagnostics.Select(static diagnostic =>
                Path.GetFileName(diagnostic.Location.SourceTree?.FilePath)),
            Is.EqualTo(["GeneratedContracts.g.cs", "input.cs"]));
    }

    [Test]
    public async Task ProfileOffSuppressesGeneratedCompanionValidation()
    {
        var diagnostics = await AnalyzeGeneratedAsync(
            """
            using SharpProof.Attributes;

            [ContractFor(typeof(IService))]
            public static class ServiceContracts
            {
            }
            """,
            Target,
            profile: "off");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task GeneratedFinalValidationUsesAuthoritativeAliasOrder()
    {
        const string malformed = """
            using SharpProof.Attributes;

            [ContractFor(typeof(IService))]
            public static class ServiceContracts
            {
            }
            """;
        var conflicting = await AnalyzeGeneratedAsync(
            malformed,
            globalOptions: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sharpproof_profile"] = " advisory ",
                ["build_property.SharpProofProfile"] = "off"
            });
        var invalid = await AnalyzeGeneratedAsync(
            malformed,
            globalOptions: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sharpproof_profile"] = "invalid",
                ["build_property.SharpProofProfile"] = "advisory"
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                conflicting.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SPCF0004"]));
            Assert.That(invalid, Is.Empty);
        }
    }

    [Test]
    public async Task GeneratedCompanionBodyIsNotAnalyzedAsAnImplementation()
    {
        var diagnostics = await AnalyzeGeneratedAsync(
            """
            using System;
            using SharpProof.Attributes;

            [ContractFor(typeof(IService))]
            public static class ServiceContracts
            {
                public static int Map(IService receiver, int value)
                {
                    Contract.Ensures(true);
                    Func<int> unsupportedDummy = () => value;
                    return unsupportedDummy();
                }
            }
            """,
            """
            public sealed class IService
            {
                public int Map(int value) => value;
            }
            """,
            additionalDiagnosticIds: ["SP0047"]);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task MixedGeneratedCompanionBodyIsNotAnalyzedAsAnImplementation()
    {
        const string input = """
            using System;
            using SharpProof.Attributes;

            public sealed class IService
            {
                public int Map(int value) => value;
            }

            public static partial class ServiceContracts
            {
                public static int Map(IService receiver, int value)
                {
                    Contract.Ensures(true);
                    Func<int> unsupportedDummy = () => value;
                    return unsupportedDummy();
                }
            }
            """;
        var diagnostics = await AnalyzeGeneratedAsync(
            """
            using SharpProof.Attributes;

            [ContractFor(typeof(IService))]
            public static partial class ServiceContracts
            {
            }
            """,
            input,
            additionalDiagnosticIds: ["SP0047"]);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task InvalidGeneratedCompanionReportsOnlyItsContractDiagnostic()
    {
        var diagnostics = await AnalyzeGeneratedAsync(
            """
            using System;
            using SharpProof.Attributes;

            [ContractFor(typeof(IService))]
            public sealed class ServiceContracts
            {
                public static int Map(IService receiver, int value)
                {
                    Contract.Ensures(true);
                    Func<int> unsupportedDummy = () => value;
                    return unsupportedDummy();
                }
            }
            """,
            """
            public sealed class IService
            {
                public int Map(int value) => value;
            }
            """,
            additionalDiagnosticIds: ["SP0047"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SPCF0003"]));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeGeneratedAsync(
        string generatedSource,
        string inputSource = Target,
        string profile = "advisory",
        string features = "contracts",
        string hintName = "GeneratedContracts.g.cs",
        IEnumerable<string>? additionalDiagnosticIds = null,
        IReadOnlyDictionary<string, string>? globalOptions = null)
    {
        var diagnosticIds = Enumerable.Range(1, 8)
            .Select(static index => $"SPCF{index:D4}")
            .Concat(additionalDiagnosticIds ?? [])
            .ToArray();
        var compilation = AnalyzerTestHost.CreateCompilation(
            inputSource,
            diagnosticIds);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new GeneratedCompanionSourceGenerator(generatedSource, hintName)
                .AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees[0].Options);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var output,
            out var generatorDiagnostics);
        Assert.That(generatorDiagnostics, Is.Empty);

        if (globalOptions != null)
        {
            var options = globalOptions.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
            options["build_property.SharpProofFeatures"] = features;
            return await AnalyzerTestHost.AnalyzeAsync(
                (CSharpCompilation)output,
                options);
        }
        return await AnalyzerTestHost.AnalyzeAsync(
            (CSharpCompilation)output,
            mode: null,
            profile: profile,
            features: features);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeGeneratedInOrderAsync(
        string generatedSource,
        bool reverse)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            Target,
            Enumerable.Range(1, 8)
                .Select(static index => $"SPCF{index:D4}")
                .ToArray());
        ISourceGenerator companion = new GeneratedCompanionSourceGenerator(
            generatedSource,
            "PeerContracts.cs").AsSourceGenerator();
        ISourceGenerator decoy = new GeneratedCompanionSourceGenerator(
            "namespace Peer { internal sealed class Marker { } }",
            "PeerMarker.cs").AsSourceGenerator();
        var generators = reverse
            ? new[] { decoy, companion }
            : new[] { companion, decoy };
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators,
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees[0].Options);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var output,
            out var generatorDiagnostics);
        Assert.That(generatorDiagnostics, Is.Empty);
        return await AnalyzerTestHost.AnalyzeAsync(
            (CSharpCompilation)output,
            mode: null,
            profile: "advisory",
            features: "contracts");
    }

    private const string RejectedContractForSource = """
        // <auto-generated/>
        namespace SharpProof.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ContractForAttribute : System.Attribute
            {
                public ContractForAttribute(System.Type target) { }
            }
        }

        [SharpProof.Attributes.ContractFor(typeof(IService))]
        public static class RejectedContracts
        {
            public static int Map(IService receiver, int value) => value;
        }
        """;

    private sealed class GeneratedCompanionSourceGenerator(
        string source,
        string hintName) :
        IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(outputContext =>
                outputContext.AddSource(
                    hintName,
                    SourceText.From(source, System.Text.Encoding.UTF8)));
        }
    }
}
