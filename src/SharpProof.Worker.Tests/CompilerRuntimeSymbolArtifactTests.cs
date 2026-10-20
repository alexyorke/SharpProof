using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerRuntimeSymbolArtifactTests
{
    [Test]
    public async Task ProjectSymbolNeutralizedByUndefRemainsValidEvidence()
    {
        var artifact = CreateArtifact();
        var tree = artifact.Compilation.SyntaxTrees.Single();
        var json = CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        using var temporary = new TempDirectory(
            "runtime-symbol-artifact-",
            TestContext.CurrentContext.WorkDirectory);
        var path = Path.Combine(temporary.FullName, "manifest.json");
        var request = await WriteRequestAsync(path, json);

        var snapshot = WorkerInputSnapshot.Load(
            request,
            WorkerCacheIdentity.Current,
            TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                artifact.SchemaVersion,
                Is.EqualTo(CompilerManifestArtifactVersions.Current));
            Assert.That(
                tree.PreprocessorSymbols,
                Does.Contain(Contract.ConditionalSymbol));
            Assert.That(
                tree.EffectivePreprocessorSymbols,
                Does.Not.Contain(Contract.ConditionalSymbol));
            Assert.That(snapshot.Callables, Is.Empty);
        }
    }

    private static CompilerManifestArtifact CreateArtifact()
    {
        const string source =
            """
            #undef SHARPPROOF_CONTRACTS
            internal static class Subject { }
            """;
        var compilation = TestCompilation.CreateExact(
            "CompilerRuntimeSymbolArtifactTests",
            [("Subject.cs", source)]);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        return CompilerManifestArtifactProducer.Create(
            compilation,
            TestContext.CurrentContext.WorkDirectory,
            "net9.0",
            WorkerFeatureSet.All,
            discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth,
            TestContext.CurrentContext.CancellationToken);
    }

    private static async Task<WorkerVerifyRequest> WriteRequestAsync(
        string path,
        string json)
    {
        System.ArgumentNullException.ThrowIfNull(json);
        System.ArgumentNullException.ThrowIfNull(path);
        var bytes = Encoding.UTF8.GetBytes(json);
        await File.WriteAllBytesAsync(path, bytes);
        return new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference
            {
                Path = path,
                Sha256 = WorkerProtocolJson.ComputeSha256((bytes).ToImmutableArray())
            },
            Cache = new WorkerCacheOptions
            {
                Enabled = false
            }
        };
    }

}
