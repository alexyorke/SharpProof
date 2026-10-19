using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerMetadataAliasWorkflowTests
{
    private static readonly ImmutableArray<string> AlternateAlias = ["alternate"];

    [TestCase(false)]
    [TestCase(true)]
    public async Task MetadataBodyAdmissionUsesTheActualCapturedBackingFileSize(bool replaceBackingFile)
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "SharpProofMetadataFileSize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var dependency = AnalyzerTestHost.CreateCompilation(
                """
                using SharpProof.Attributes;
                public static class Dependency {
                    public static int Identity([Positive] int value) => value;
                }
                """, []).WithAssemblyName("MetadataSizeDependency");
            var originalImage = AnalyzerTestHost.EmitImage(dependency);
            var replacementImage = await File.ReadAllBytesAsync(
                typeof(SharpProof.Attributes.PositiveAttribute).Assembly.Location);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(originalImage, Is.Not.Empty);
                Assert.That(replacementImage, Is.Not.Empty);
                Assert.That(replacementImage.Length, Is.Not.EqualTo(originalImage.Length));
            }
            await TestContext.Out.WriteLineAsync($"actual dependency assembly bytes={originalImage.Length}; actual replacement SDK attributes assembly bytes={replacementImage.Length}");
            var dependencyPath = Path.Combine(directory, "dependency.dll");
            await File.WriteAllBytesAsync(dependencyPath, (originalImage).ToArray());
            var reference = MetadataReference.CreateFromImage(originalImage, filePath: dependencyPath);
            var compilation = AnalyzerTestHost.CreateCompilation(
                "internal static class Subject { internal static int Identity(int value) => Dependency.Identity(value); }",
                [], [reference]);
            Assert.That(AnalyzerTestHost.EmitImage(compilation), Is.Not.Empty);
            var captured = CompilerCompilationCapture.Capture(compilation, directory,
                "net8.0", [], TestContext.CurrentContext.CancellationToken);
            var authority = captured.References.SelectMany(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Modules)).Single(module =>
                module is null ? throw new System.ArgumentNullException(nameof(module)) : (Path.GetFullPath(module.Path) == Path.GetFullPath(dependencyPath)));
            Assert.That(authority.SizeBytes, Is.EqualTo(originalImage.Length));
            await File.WriteAllBytesAsync(dependencyPath, replaceBackingFile ? replacementImage : (originalImage).ToArray());
            var prepared = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation,
                WorkerFeatureSet.All, captured.SyntaxTrees, captured.References,
                CompilerSpecificationPackProvider.ResolveConfiguration([]),
                TestContext.CurrentContext.CancellationToken, enableMetadataRequires: true);
            if (replaceBackingFile)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(prepared.Callers, Is.Empty);
                    Assert.That(prepared.Gaps, Has.Length.EqualTo(1));
                }
                Assert.That(prepared.Gaps.Single().Reason, Is.EqualTo("UnsupportedBody"));
            }
            else
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(prepared.Gaps, Is.Empty);
                    Assert.That(prepared.Callers, Has.Length.EqualTo(1));
                }
                var body = prepared.Callers.Single().Body;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(body.IsBodyAbstraction, Is.False);
                    Assert.That(body.CallPreconditions, Has.Length.EqualTo(1));
                    Assert.That(body.CallPreconditions.Single().MetadataClause, Is.Not.Null);
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestCase("int", "value", "local", true)]
    [TestCase("string", "\"text\"", "local.Length", false)]
    [TestCase("float", "value", "(int)local", false)]
    public async Task OrdinaryDebugMetadataLocalBodiesRespectAdmission(string localType,
        string initializer, string returned, bool expectedPrepared)
    {
        System.ArgumentNullException.ThrowIfNull(initializer);
        System.ArgumentNullException.ThrowIfNull(localType);
        System.ArgumentNullException.ThrowIfNull(returned);
        var directory = Path.Combine(Path.GetTempPath(),
            "SharpProofMetadataLocals-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var dependency = AnalyzerTestHost.CreateCompilation(
                $$"""
                using SharpProof.Attributes;
                public static class Dependency {
                    public static int Identity([Positive] int value) {
                        {{localType}} local = {{initializer}};
                        return {{returned}};
                    }
                }
                """, []).WithAssemblyName("MetadataLocalDependency");
            dependency = dependency.WithOptions(dependency.Options
                .WithOptimizationLevel(OptimizationLevel.Debug));
            var dependencyPath = Path.Combine(directory, "dependency.dll");
            await File.WriteAllBytesAsync(dependencyPath, (AnalyzerTestHost.EmitImage(dependency)).ToArray());
            var compilation = AnalyzerTestHost.CreateCompilation(
                "internal static class Subject { internal static int Identity(int value) => Dependency.Identity(value); }",
                [], [MetadataReference.CreateFromFile(dependencyPath)]);
            var captured = CompilerCompilationCapture.Capture(compilation, directory,
                "net8.0", [], TestContext.CurrentContext.CancellationToken);
            var prepared = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation,
                WorkerFeatureSet.All, captured.SyntaxTrees, captured.References,
                CompilerSpecificationPackProvider.ResolveConfiguration([]),
                TestContext.CurrentContext.CancellationToken, enableMetadataRequires: true);
            if (expectedPrepared)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(prepared.Gaps, Is.Empty);
                    Assert.That(prepared.Callers, Has.Length.EqualTo(1));
                }
                var body = prepared.Callers.Single().Body;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(body.CallPreconditions, Has.Length.EqualTo(1));
                    Assert.That(body.CallPreconditions.Single().MetadataClause, Is.Not.Null);
                    Assert.That(body.IsBodyAbstraction, Is.False);
                }
            }
            else
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(prepared.Callers, Is.Empty);
                    Assert.That(prepared.Gaps, Has.Length.EqualTo(1));
                }
                Assert.That(prepared.Gaps.Single().Reason, Is.EqualTo("UnsupportedBody"));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OrdinaryMetadataPreconditionIsPreparedWithCapturedReferenceEvidence(bool includeAlias)
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "SharpProofMetadataPrecondition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var dependency = AnalyzerTestHost.CreateCompilation(
                """
                using SharpProof.Attributes;
                public static class Dependency {
                    public static int Identity([Positive] int value) => value;
                }
                """, []).WithAssemblyName("MetadataPreconditionDependency");
            var image = AnalyzerTestHost.EmitImage(dependency);
            var firstPath = Path.Combine(directory, "A.dll");
            var secondPath = Path.Combine(directory, "B.dll");
            await File.WriteAllBytesAsync(firstPath, (image).ToArray());
            await File.WriteAllBytesAsync(secondPath, (image).ToArray());
            var first = MetadataReference.CreateFromFile(firstPath);
            var second = MetadataReference.CreateFromFile(secondPath).WithAliases(AlternateAlias);
            ImmutableArray<MetadataReference> references = includeAlias ? [first, second] : [first];
            var compilation = AnalyzerTestHost.CreateCompilation(
                "internal static class Subject { internal static int Identity(int value) => Dependency.Identity(value); }",
                [], references);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
            var captured = CompilerCompilationCapture.Capture(compilation, directory,
                "net8.0", [], TestContext.CurrentContext.CancellationToken);
            var prepared = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation,
                WorkerFeatureSet.All, captured.SyntaxTrees, captured.References,
                CompilerSpecificationPackProvider.ResolveConfiguration([]),
                TestContext.CurrentContext.CancellationToken, enableMetadataRequires: true);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(prepared.Gaps, Is.Empty);
                Assert.That(prepared.Callers, Has.Length.EqualTo(1));
            }
            var body = prepared.Callers.Single().Body;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(body.CallPreconditions, Has.Length.EqualTo(1));
                Assert.That(body.CallPreconditions.Single().MetadataClause, Is.Not.Null);
                Assert.That(body.Clauses, Is.Empty);
                Assert.That(body.IsBodyAbstraction, Is.False);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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
            await File.WriteAllBytesAsync(firstPath, (image).ToArray());
            await File.WriteAllBytesAsync(secondPath, (image).ToArray());
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
            var artifact = CompilerManifestArtifactJson.Deserialize(aliased, cancellationToken: TestContext.CurrentContext.CancellationToken);
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
        string name, ImmutableArray<MetadataReference> references)
    {
        if (references.IsDefault)
        { throw new ArgumentNullException(nameof(references)); }
        System.ArgumentNullException.ThrowIfNull(directory);
        System.ArgumentNullException.ThrowIfNull(name);
        System.ArgumentNullException.ThrowIfNull(source);
        var path = Path.Combine(directory, name + ".json");
        var options = ImmutableDictionary.CreateBuilder<string, string>();
        options["build_property.SharpProofProfile"] = "advisory";
        options["build_property.SharpProofFeatures"] = "all";
        options["build_property.SharpProofVerifyPolicy"] = "advisory";
        options["build_property.SharpProofAssumptionPolicy"] = "allow";
        options["build_property._SharpProofCompilerManifestPath"] = path;
        options["build_property._SharpProofCompilationTargetFramework"] = "net8.0";
        options["build_property._SharpProofProjectDirectory"] = directory;
        options["build_property.SharpProofVerifyMaximumExpressionDepth"] = "64";
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            AnalyzerTestHost.CreateCompilation(source, [], references), options.ToImmutable(),
            analyzer: new FinalCompilationCollectorAnalyzer());
        Assert.That(diagnostics, Is.Empty);
        return await File.ReadAllTextAsync(path);
    }
}
