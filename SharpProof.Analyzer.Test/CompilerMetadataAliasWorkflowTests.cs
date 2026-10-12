using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerMetadataAliasWorkflowTests
{
    private static readonly string[] AlternateAlias = ["alternate"];

    [Test]
    public async Task IdenticalReferencesWithDifferentAliasesPreserveThePublishedCallable()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "SharpProofMetadataAlias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var dependency = AnalyzerTestHost.CreateCompilation(
                "public static class Dependency { public static int Identity(int value) => value; }", [])
                .WithAssemblyName("MetadataAliasDependency");
            var image = AnalyzerTestHost.EmitImage(dependency);
            var firstPath = Path.Combine(directory, "A.dll");
            var secondPath = Path.Combine(directory, "B.dll");
            await File.WriteAllBytesAsync(firstPath, image);
            await File.WriteAllBytesAsync(secondPath, image);
            var first = MetadataReference.CreateFromFile(firstPath);
            var second = MetadataReference.CreateFromFile(secondPath).WithAliases(AlternateAlias);

            const string source = """
                using SharpProof.Attributes;
                internal static class Subject {
                    internal static int Identity(int value) {
                        Contract.Ensures(Contract.Result<int>() == value);
                        return Dependency.Identity(value);
                    }
                }
                """;
            var single = await PublishAsync(source, directory, "single", [first]);
            var aliased = await PublishAsync(source, directory, "aliased", [first, second]);
            var artifact = CompilerManifestArtifactJson.Deserialize(aliased);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
                Assert.That(artifact.Callables, Has.Length.EqualTo(1));
                Assert.That(artifact.Callables.Single().Total, Is.Not.Null);
                Assert.That(aliased, Is.EqualTo(single));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string> PublishAsync(string source, string directory,
        string name, MetadataReference[] references)
    {
        var path = Path.Combine(directory, name + ".json");
        var options = new Dictionary<string, string>
        {
            ["build_property.SharpProofProfile"] = "advisory",
            ["build_property.SharpProofFeatures"] = "all",
            ["build_property.SharpProofVerifyPolicy"] = "advisory",
            ["build_property.SharpProofAssumptionPolicy"] = "allow",
            ["build_property._SharpProofCompilerManifestPath"] = path,
            ["build_property._SharpProofCompilationTargetFramework"] = "net8.0",
            ["build_property._SharpProofProjectDirectory"] = directory,
            ["build_property.SharpProofVerifyMaximumExpressionDepth"] = "64"
        };
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            AnalyzerTestHost.CreateCompilation(source, [], references), options,
            analyzer: new FinalCompilationCollectorAnalyzer());
        Assert.That(diagnostics, Is.Empty);
        return await File.ReadAllTextAsync(path);
    }
}
