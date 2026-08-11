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

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeGeneratedAsync(
        string generatedSource,
        string inputSource = Target,
        string profile = "advisory")
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            inputSource,
            Enumerable.Range(1, 8).Select(static index =>
                $"SPCF{index:D4}"));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new GeneratedCompanionSourceGenerator(generatedSource)
                .AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees[0].Options);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var output,
            out var generatorDiagnostics);
        Assert.That(generatorDiagnostics, Is.Empty);

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
