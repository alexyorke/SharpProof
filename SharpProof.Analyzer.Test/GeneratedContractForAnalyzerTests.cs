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
            Is.EqualTo(["SPCF0002"]));
        Assert.That(
            diagnostics[0].Location.SourceTree?.FilePath,
            Does.EndWith("GeneratedContracts.g.cs"));
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
            [new GeneratedCompanionSourceGenerator(generatedSource)
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
            options["build_property.SharpProofFeatures"] = "contracts";
            return await AnalyzerTestHost.AnalyzeAsync(
                (CSharpCompilation)output,
                options);
        }
        return await AnalyzerTestHost.AnalyzeAsync(
            (CSharpCompilation)output,
            mode: "CONTRACTS",
            profile: profile);
    }

    private sealed class GeneratedCompanionSourceGenerator(string source) :
        IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(outputContext =>
                outputContext.AddSource(
                    "GeneratedContracts.g.cs",
                    SourceText.From(source, System.Text.Encoding.UTF8)));
        }
    }
}
