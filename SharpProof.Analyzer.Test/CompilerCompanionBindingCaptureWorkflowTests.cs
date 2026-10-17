using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Contracts;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerCompanionBindingCaptureWorkflowTests
{
    [TestCase("missing", ContractBindingFailure.MissingCompanion)]
    [TestCase("ambiguous", ContractBindingFailure.AmbiguousCompanion)]
    [TestCase("signature", ContractBindingFailure.CompanionSignatureMismatch)]
    public async Task OrdinaryCompanionShapesPublishTheActualBindingFailure(
        string shape, ContractBindingFailure expectedFailure)
    {
        var companions = shape switch
        {
            "missing" => "[ContractFor(typeof(Target))] public static class TargetContracts { public static long Other(long value) => value; }",
            "ambiguous" => "[ContractFor(typeof(Target))] public static class FirstContracts { public static long Read(long value) => value; } " +
                "[ContractFor(typeof(Target))] public static class SecondContracts { public static long Read(long value) => value; }",
            "signature" => "[ContractFor(typeof(Target))] public static class TargetContracts { public static long Read(string value) => 0L; }",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var compilation = AnalyzerTestHost.CreateCompilation(
            "using SharpProof.Attributes; public static class Target { [ZeroAllocations] public static long Read(long value) => value; } " + companions,
            [], filePath: "companion.cs");
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var method = compilation.GetTypeByMetadataName("Target")!
            .GetMembers("Read").OfType<IMethodSymbol>().Single();
        var binding = new ContractBinder(compilation, new IrFactory()).Bind(method);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(binding.IsSuccess, Is.False);
            Assert.That(binding.Failure, Is.EqualTo(expectedFailure));
        }

        var artifact = await CaptureAsync(compilation);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(artifact.Manifest.Callables, Has.Length.EqualTo(1));
            Assert.That(artifact.Callables, Has.Length.EqualTo(1));
            Assert.That(artifact.Callables.Single().CallableId, Is.EqualTo(artifact.Manifest.Callables.Single().CallableId));
            Assert.That(artifact.Callables.Single().FailureReason, Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
            Assert.That(artifact.CompilerDiagnostics, Is.Empty);
        }
    }

    private static async Task<CompilerManifestArtifact> CaptureAsync(CSharpCompilation compilation)
    {
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofCompanionCapture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            var request = Path.Combine(directory, "request");
            const string invocation = "00112233445566778899aabbccddeeff";
            await File.WriteAllTextAsync(request, invocation);
            var options = new Dictionary<string, string>
            {
                ["build_property.SharpProofProfile"] = "advisory",
                ["build_property.SharpProofFeatures"] = "all",
                ["build_property.SharpProofVerifyPolicy"] = "advisory",
                ["build_property.SharpProofAssumptionPolicy"] = "allow",
                ["build_property._SharpProofCompilerManifestPath"] = path,
                ["build_property._SharpProofCompilationTargetFramework"] = "net8.0",
                ["build_property._SharpProofProjectDirectory"] = directory,
                ["build_property.SharpProofVerifyMaximumExpressionDepth"] = "64",
                ["build_property._SharpProofCompilerCaptureRequestPath"] = request
            };
            var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, options,
                analyzer: new FinalCompilationCollectorAnalyzer());
            Assert.That(diagnostics, Is.Empty);
            Assert.That(await File.ReadAllTextAsync(request + ".completed"), Is.EqualTo(invocation));
            return CompilerManifestArtifactJson.Deserialize(await File.ReadAllTextAsync(path), cancellationToken: TestContext.CurrentContext.CancellationToken);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
