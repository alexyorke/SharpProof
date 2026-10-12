using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerCaptureRequestWorkflowTests
{
    private const string RequestKey =
        "build_property._SharpProofCompilerCaptureRequestPath";

    [TestCase("not-a-guid")]
    [TestCase("00112233445566778899AABBCCDDEEFF")]
    [TestCase("00112233-4455-6677-8899-aabbccddeeff")]
    public async Task InvalidInvocationReportsFailureWithoutPublishing(string invocation)
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            var request = Path.Combine(directory, "request");
            await File.WriteAllTextAsync(request, invocation);
            var diagnostics = await AnalyzeAsync(path, request);

            using (Assert.EnterMultipleScope())
            {
                AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
                AnalyzerTestHost.AssertMessageContains(diagnostics.Single(),
                    "compiler capture invocation is invalid");
                Assert.That(File.Exists(path), Is.False);
                Assert.That(File.Exists(request + ".completed"), Is.False);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task CanonicalInvocationPublishesManifestAndMatchingCompletionReceipt()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            var request = Path.Combine(directory, "request");
            const string invocation = "00112233445566778899aabbccddeeff";
            await File.WriteAllTextAsync(request, " \n" + invocation + "\n ");

            var diagnostics = await AnalyzeAsync(path, request);
            Assert.That(diagnostics, Is.Empty);
            var artifact = CompilerManifestArtifactJson.Deserialize(
                await File.ReadAllTextAsync(path));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(artifact.Compilation.ProjectDirectory,
                    Is.EqualTo(Path.GetDirectoryName(path)));
                Assert.That(await File.ReadAllTextAsync(request + ".completed"),
                    Is.EqualTo(invocation));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task GenericLocalContractsPublishDistinctSemanticClaimIdentities()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            var request = Path.Combine(directory, "request");
            await File.WriteAllTextAsync(request, "00112233445566778899aabbccddeeff");
            var diagnostics = await AnalyzeAsync(path, request, source:
                """
                using SharpProof.Attributes;
                internal static class Subject {
                    internal static int Outer<T>(int value) {
                        int ParentType<U>(int input) {
                            Contract.Ensures(typeof(T) != null);
                            return input;
                        }
                        int LocalType<U>(int input) {
                            Contract.Ensures(typeof(U) != null);
                            return input;
                        }
                        return ParentType<int>(value) + LocalType<int>(value);
                    }
                }
                """);
            Assert.That(diagnostics, Is.Empty);
            var artifact = CompilerManifestArtifactJson.Deserialize(
                await File.ReadAllTextAsync(path));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(2));
                Assert.That(artifact.Manifest.Claims.Select(claim => claim.ClaimId).Distinct().Count(),
                    Is.EqualTo(2));
                Assert.That(await File.ReadAllTextAsync(request + ".completed"),
                    Is.EqualTo("00112233445566778899aabbccddeeff"));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task DuplicateSpecificationPacksReportFailureWithoutCompletionReceipt()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            var request = Path.Combine(directory, "request");
            await File.WriteAllTextAsync(request, "00112233445566778899aabbccddeeff");
            var diagnostics = await AnalyzeAsync(path, request, "dotnet.scalar; dotnet.scalar");
            using (Assert.EnterMultipleScope())
            {
                AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
                AnalyzerTestHost.AssertMessageContains(diagnostics.Single(),
                    "must not contain duplicate identifiers");
                Assert.That(File.Exists(path), Is.False);
                Assert.That(File.Exists(request + ".completed"), Is.False);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        string path, string request, string? packs = null,
        string source = "internal static class Subject { }")
    {
        var options = new Dictionary<string, string>
        {
            ["build_property.SharpProofProfile"] = "advisory",
            ["build_property.SharpProofFeatures"] = "all",
            ["build_property.SharpProofVerifyPolicy"] = "advisory",
            ["build_property.SharpProofAssumptionPolicy"] = "allow",
            ["build_property._SharpProofCompilerManifestPath"] = path,
            ["build_property._SharpProofCompilationTargetFramework"] = "net8.0",
            ["build_property._SharpProofProjectDirectory"] = Path.GetDirectoryName(path)!,
            ["build_property.SharpProofVerifyMaximumExpressionDepth"] = "64",
            [RequestKey] = request
        };
        if (packs != null)
        {
            options["build_property.SharpProofSpecificationPacks"] = packs;
        }
        return AnalyzerTestHost.AnalyzeAsync(
            AnalyzerTestHost.CreateCompilation(source, []),
            options,
            analyzer: new FinalCompilationCollectorAnalyzer());
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SharpProofCaptureWorkflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
