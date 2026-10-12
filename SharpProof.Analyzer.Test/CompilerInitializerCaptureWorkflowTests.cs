using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerInitializerCaptureWorkflowTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task MemberInitializerLambdaPublishesAStableContainerScopedClaim(bool property)
    {
        var first = await CaptureAsync(InitializerCompilation(property, "Work"));
        var repeated = await CaptureAsync(InitializerCompilation(property, "Work"));
        var renamed = await CaptureAsync(InitializerCompilation(property, "Renamed"));
        var callable = first.Manifest.Callables.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(callable.ClaimIds, Is.EqualTo(first.Manifest.Claims.Select(claim => claim.ClaimId)));
            Assert.That(repeated.Manifest.Callables.Single().CallableId, Is.EqualTo(callable.CallableId));
            Assert.That(renamed.Manifest.Callables.Single().CallableId, Is.Not.EqualTo(callable.CallableId));
            Assert.That(first.Callables.Single().FailureReason, Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
            Assert.That(first.CompilerDiagnostics, Is.Empty);
        }
    }

    [Test]
    public async Task TopLevelContractPublishesTheCompilerEntryPointAndItsGlobalStatementSpan()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            "using SharpProof.Attributes;\nContract.Ensures(true);", [], filePath: "program.cs");
        compilation = compilation.WithOptions(compilation.Options.WithOutputKind(OutputKind.ConsoleApplication));
        var artifact = await CaptureAsync(compilation);
        var root = compilation.SyntaxTrees.Single().GetRoot();
        var statement = root.DescendantNodes().OfType<GlobalStatementSyntax>().Single();
        var callable = artifact.Manifest.Callables.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(compilation.GetEntryPoint(CancellationToken.None), Is.Not.Null);
            Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(callable.Location.Path, Is.EqualTo("program.cs"));
            Assert.That(callable.Location.Start, Is.EqualTo(root.FullSpan.Start));
            Assert.That(callable.Location.Length, Is.EqualTo(statement.Span.End - root.FullSpan.Start));
            Assert.That(artifact.Callables.Single().FailureReason, Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
            Assert.That(artifact.CompilerDiagnostics, Is.Empty);
        }
    }

    private static CSharpCompilation InitializerCompilation(bool property, string member)
    {
        var declaration = property
            ? $"internal static System.Func<int> {member} {{ get; }} ="
            : $"internal static readonly System.Func<int> {member} =";
        return AnalyzerTestHost.CreateCompilation(
            $$"""
            using SharpProof.Attributes;
            internal static class Subject {
                {{declaration}} static () => {
                    Contract.Ensures(Contract.Result<int>() == 1);
                    return 1;
                };
            }
            """, [], filePath: "initializer.cs");
    }

    private static async Task<CompilerManifestArtifact> CaptureAsync(CSharpCompilation compilation)
    {
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofInitializerCapture-" + Guid.NewGuid().ToString("N"));
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
            return CompilerManifestArtifactJson.Deserialize(await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
