using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.CompilerProbe.TestAsset;
using SharpProof.Testing;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class CompilerProbeInputConsistencyTests
{
    [Test]
    public async Task StatefulAdditionalTextCannotAuthenticateDifferentGeneratorInput()
    {
        using var temporary = new TempDirectory(
            "sharpproof-probe-input-consistency-");
        var root = temporary.FullName;
        var outputPath = Path.Combine(root, "probe.json");
        var input = new StatefulAdditionalText(
            Path.Combine(root, CompilerProbeContract.AdditionalFileName),
            "generator-value",
            "later-value");
        var options = new ProbeOptionsProvider(
            outputPath,
            input,
            metadataValue: "metadata");
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(
            LanguageVersion.CSharp12);
        var compilation = CreateCompilation(parseOptions);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new CompilerProbeGenerator().AsSourceGenerator()],
            additionalTexts: [input],
            parseOptions: parseOptions,
            optionsProvider: options);

        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var generatedCompilation,
            out var generatorDiagnostics);

        Assert.That(generatorDiagnostics, Is.Empty);
        var analyzerOptions = new AnalyzerOptions([input], options);
        var diagnostics = await generatedCompilation.WithAnalyzers(
                [new CompilerProbeAnalyzer()],
                new CompilationWithAnalyzersOptions(
                    analyzerOptions,
                    onAnalyzerException: null,
                    concurrentAnalysis: false,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false))
            .GetAnalyzerDiagnosticsAsync();

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)),
            Is.EquivalentTo([CompilerProbeContract.FailureDiagnosticId]));
        Assert.That(File.Exists(outputPath), Is.False);
    }

    private static CSharpCompilation CreateCompilation(
        CSharpParseOptions parseOptions)
    {
        System.ArgumentNullException.ThrowIfNull(parseOptions);
        return CSharpCompilation.Create(
            "ProbeInputConsistency",
            [CSharpSyntaxTree.ParseText(
                "internal static class Subject { }",
                parseOptions,
                "Subject.cs")],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                deterministic: true));
    }

    private sealed class StatefulAdditionalText(
        string path,
        string first,
        string later) : AdditionalText
    {
        private int _readCount;

        public override string Path { get; } = (later is null ? throw new System.ArgumentNullException(nameof(later)) : (first is null ? throw new System.ArgumentNullException(nameof(first)) : (path ?? throw new System.ArgumentNullException(nameof(path)))));

        public override SourceText GetText(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return SourceText.From(
                Interlocked.Increment(ref _readCount) == 1 ? first : later);
        }
    }

    private sealed class ProbeOptionsProvider(
        string outputPath,
        AdditionalText input,
        string metadataValue) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _global =
            (input is null ? throw new System.ArgumentNullException(nameof(input)) : new DictionaryAnalyzerConfigOptions(
                (CompilerProbeContract.OutputPathOptionKey, (outputPath ?? throw new System.ArgumentNullException(nameof(outputPath))))));
        private readonly AnalyzerConfigOptions _input =
            new DictionaryAnalyzerConfigOptions(
                (CompilerProbeContract.AdditionalFileMetadataOptionKey,
                    (metadataValue ?? throw new System.ArgumentNullException(nameof(metadataValue)))));

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            System.ArgumentNullException.ThrowIfNull(tree);
            return DictionaryAnalyzerConfigOptions.Empty;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            System.ArgumentNullException.ThrowIfNull(textFile);
            return ReferenceEquals(textFile, input)
                ? _input
                : DictionaryAnalyzerConfigOptions.Empty;
        }
    }
}
