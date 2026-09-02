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
        var json = CompilerManifestArtifactJson.Serialize(artifact);
        var path = TemporaryManifestPath();
        try
        {
            var request = await WriteRequestAsync(path, json);

            var snapshot = await WorkerInputSnapshot.LoadAsync(
                request,
                WorkerCacheIdentity.Current,
                CancellationToken.None);

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
                Assert.That(
                    snapshot.CompilerManifest.Compilation.SyntaxTrees
                        .Single().EffectivePreprocessorSymbols,
                    Does.Not.Contain(Contract.ConditionalSymbol));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ResealedEffectiveRuntimeSymbolIsRejectedByWorkerInput()
    {
        var artifact = CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(CreateArtifact()));
        artifact.Compilation.SyntaxTrees.Single()
            .EffectivePreprocessorSymbols = [Contract.ConditionalSymbol];
        artifact.CompilationSha256 =
            CompilationFingerprint.ComputeSha256(artifact.Compilation, []);
        var json = JsonSerializer.Serialize(
            artifact,
            WorkerProtocolJson.Options) + "\n";
        var path = TemporaryManifestPath();
        try
        {
            Assert.That(
                (Action)(() =>
                    CompilerManifestArtifactJson.Deserialize(json)),
                Throws.TypeOf<JsonException>());
            var request = await WriteRequestAsync(path, json);

            var exception = Assert.ThrowsAsync<IOException>(
                (Func<Task>)(async () =>
                    await WorkerInputSnapshot.LoadAsync(
                        request,
                        WorkerCacheIdentity.Current,
                        CancellationToken.None)));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exception, Is.Not.Null);
                Assert.That(
                    exception!.Message,
                    Is.EqualTo(WorkerInputSnapshot.ManifestInvalid));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static CompilerManifestArtifact CreateArtifact()
    {
        const string source =
            """
            #undef SHARPPROOF_CONTRACTS
            internal static class Subject { }
            """;
        var compilation = WorkerTestCompilation.Create(
            "CompilerRuntimeSymbolArtifactTests",
            ("Subject.cs", source));
        var discovery = new ClaimManifestBuilder(compilation).Build();
        return CompilerManifestArtifactProducer.Create(
            compilation,
            TestContext.CurrentContext.WorkDirectory,
            "net9.0",
            WorkerFeatureSet.All,
            discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth,
            CancellationToken.None);
    }

    private static async Task<WorkerVerifyRequest> WriteRequestAsync(
        string path,
        string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await File.WriteAllBytesAsync(path, bytes);
        return new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference
            {
                Path = path,
                Sha256 = WorkerProtocolJson.ComputeSha256(bytes)
            },
            Cache = new WorkerCacheOptions
            {
                Enabled = false
            }
        };
    }

    private static string TemporaryManifestPath()
    {
        return Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "runtime-symbol-artifact-" +
            Guid.NewGuid().ToString("N") +
            ".json");
    }
}
