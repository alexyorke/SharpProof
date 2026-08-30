using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.CompilerProbe.TestAsset;

namespace SharpProof.Package.Test;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class CompilerProbeSnapshotTests
{
    [Test]
    public async Task CompilationReferenceChangesProbeSnapshot()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-compilation-reference-");
        try
        {
            var outputPath = Path.Combine(directory.FullName, "probe.json");

            var first = await CaptureSnapshotAsync(
                outputPath,
                "Referenced.First");
            var second = await CaptureSnapshotAsync(
                outputPath,
                "Referenced.Second");

            Assert.That(second, Is.Not.EqualTo(first));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<string> CaptureSnapshotAsync(
        string outputPath,
        string referencedAssemblyName)
    {
        var referencedCompilation = CSharpCompilation.Create(
            referencedAssemblyName);
        var compilation = CSharpCompilation.Create(
            "ProbeConsumer",
            references: [referencedCompilation.ToMetadataReference()]);
        var analyzerOptions = new AnalyzerOptions(
            [],
            new OutputPathOptionsProvider(outputPath));
        var withAnalyzers = compilation.WithAnalyzers(
            [new CompilerProbeAnalyzer()],
            new CompilationWithAnalyzersOptions(
                analyzerOptions,
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false));

        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        Assert.That(diagnostics, Is.Empty);
        return await File.ReadAllTextAsync(outputPath);
    }

    private sealed class OutputPathOptionsProvider(string outputPath)
        : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _options =
            new OutputPathOptions(outputPath);

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return _options;
        }

        public override AnalyzerConfigOptions GetOptions(
            AdditionalText textFile)
        {
            return _options;
        }
    }

    private sealed class OutputPathOptions(string outputPath)
        : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (string.Equals(
                    key,
                    CompilerProbeContract.OutputPathOptionKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = outputPath;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
