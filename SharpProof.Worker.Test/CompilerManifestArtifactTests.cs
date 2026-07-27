using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerManifestArtifactTests {
    [Test]
    public void ArtifactAttestsBothRoslynBuildsAndParseFeatures() {
        var parse = new CSharpParseOptions(LanguageVersion.CSharp12)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "sharp-proof-test-feature",
                    "enabled")
            ]);
        var artifact = CreateArtifact(parse);

        var reconstructed =
            CompilerManifestArtifactJson.CreateCompilation(
                artifact,
                CancellationToken.None);
        var reconstructedParse = (CSharpParseOptions)
            reconstructed.SyntaxTrees.Single().Options;

        using (Assert.EnterMultipleScope()) {
            Assert.That(
                artifact.Compilation.CompilerVersion,
                Is.EqualTo(
                    CompilationFingerprint.CurrentCompilerVersion));
            Assert.That(
                artifact.Compilation.CSharpCompilerVersion,
                Is.EqualTo(
                    CompilationFingerprint.CurrentCSharpCompilerVersion));
            Assert.That(
                Guid.TryParseExact(
                    artifact.Compilation.CompilerMvid,
                    "D",
                    out _),
                Is.True);
            Assert.That(
                Guid.TryParseExact(
                    artifact.Compilation.CSharpCompilerMvid,
                    "D",
                    out _),
                Is.True);
            Assert.That(
                reconstructedParse.Features,
                Is.EqualTo(parse.Features));
            Assert.That(
                artifact.Compilation.Options.ResolverPolicy,
                Is.EqualTo("Materialized"));
        }
    }

    [Test]
    public void EitherCompilerModuleMismatchIsIncompatible() {
        var artifact = CreateArtifact();
        artifact.Compilation.CompilerMvid =
            Guid.NewGuid().ToString("D");

        Assert.That(
            CompilerManifestArtifactJson.IsCompilerCompatible(
                artifact,
                out _),
            Is.False);

        artifact = CreateArtifact();
        artifact.Compilation.CSharpCompilerMvid =
            Guid.NewGuid().ToString("D");

        Assert.That(
            CompilerManifestArtifactJson.IsCompilerCompatible(
                artifact,
                out _),
            Is.False);
    }

    [Test]
    public void RecomputedOuterHashCannotHideMalformedNestedState() {
        Action<CompilerCompilationSnapshot>[] corruptions = [
            snapshot => snapshot.Options.OutputKind = "invalid",
            snapshot => snapshot.Options.ResolverPolicy = "ignored",
            snapshot => snapshot.SyntaxTrees[0].Features = null!,
            snapshot => snapshot.SyntaxTrees[0].Text = null!,
            snapshot => snapshot.References[0].Aliases = null!,
            snapshot => snapshot.References[0].Kind = "invalid"
        ];

        foreach (var corrupt in corruptions) {
            var artifact = CreateArtifact();
            corrupt(artifact.Compilation);
            artifact.CompilationSha256 =
                CompilationFingerprint.ComputeSha256(
                    artifact.Compilation);
            var json = JsonSerializer.Serialize(
                    artifact,
                    WorkerProtocolJson.Options) +
                "\n";

            Assert.Throws<JsonException>(
                (Action)(() =>
                    CompilerManifestArtifactJson.Deserialize(json)));
        }
    }

    [Test]
    public void ResolverDependentDirectivesFailClosed() {
        var parse = new CSharpParseOptions(
            LanguageVersion.CSharp12,
            kind: SourceCodeKind.Script);
        var compilation = CreateCompilation(
            parse,
            "#r \"dependency.dll\"\nclass Subject {}\n");

        Assert.Throws<InvalidOperationException>(
            (Action)(() => CompilerManifestArtifactJson.Create(
                compilation,
                TestContext.CurrentContext.WorkDirectory,
                "net8.0",
                WorkerFeatureSet.All,
                EmptyManifest(),
                CancellationToken.None)));
    }

    private static CompilerManifestArtifact CreateArtifact(
        CSharpParseOptions? parse = null) =>
        CompilerManifestArtifactJson.Create(
            CreateCompilation(
                parse ?? new CSharpParseOptions(
                    LanguageVersion.CSharp12),
                "internal sealed class Subject {}\n"),
            TestContext.CurrentContext.WorkDirectory,
            "net8.0",
            WorkerFeatureSet.All,
            EmptyManifest(),
            CancellationToken.None);

    private static CSharpCompilation CreateCompilation(
        CSharpParseOptions parse,
        string source) =>
        CSharpCompilation.Create(
            "CompilerArtifactTest",
            [CSharpSyntaxTree.ParseText(
                source,
                parse,
                Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "Subject.cs"))],
            [MetadataReference.CreateFromFile(
                typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));

    private static WorkerClaimManifest EmptyManifest() {
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        return manifest;
    }
}
