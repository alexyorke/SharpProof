using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerDiagnosticSourceIdentityRegressionTests
{
    [TestCase("", "src/Subject.cs")]
    [TestCase("#line 100\n", "src/Subject.cs")]
    [TestCase("#line 100 \"mapped.cs\"\n", "src/mapped.cs")]
    [TestCase("#line 100 \"src/Subject.cs\"\n", "src/src/Subject.cs")]
    [TestCase("#line 100 \"mapped.cs\"\n#line default\n", "src/Subject.cs")]
    public void NestedRelativeSourcePathsResolvePhysicalAndMappedPathsOnce(
        string directive, string expectedPath)
    {
        ArgumentNullException.ThrowIfNull(directive);
        ArgumentNullException.ThrowIfNull(expectedPath);
        var compilation = CSharpCompilation.Create("RelativeDiagnosticPaths",
            [CSharpSyntaxTree.ParseText(directive + "class Subject { int Value() => Missing; }",
                path: "src/Subject.cs")],
            TestMetadataReferences.WithSharpProof,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));

        var artifact = CreateArtifact(compilation);
        var diagnostic = artifact.CompilerDiagnostics.Single();
        var tree = artifact.Compilation.SyntaxTrees.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostic.Code, Is.EqualTo("compiler.CS0103"));
            Assert.That(diagnostic.Location.Path, Is.EqualTo(Path.GetFullPath(expectedPath)));
            Assert.That(tree.LineMap.Last().MappedPath, Is.EqualTo(Path.GetFullPath(expectedPath)));
            Assert.That(diagnostic.SourceTreePath, Is.EqualTo(Path.GetFullPath("src/Subject.cs")));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DiagnosticsWithSharedCoordinatesRetainTheCompilerTree(bool mapped)
    {
        var directive = mapped ? "#line 100 \"shared.cs\"\n" : string.Empty;
        var compilation = CSharpCompilation.Create("SharedDiagnosticCoordinates",
            [CSharpSyntaxTree.ParseText(directive + "class A { int Value() => MissingA; }",
                path: mapped ? "first.cs" : "Subject.cs"),
             CSharpSyntaxTree.ParseText(directive + "class B { int Value() => MissingB; }",
                path: mapped ? "second.cs" : "Subject.cs")],
            TestMetadataReferences.WithSharpProof,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));

        var artifact = CreateArtifact(compilation);
        var diagnostics = artifact.CompilerDiagnostics.OrderBy(diagnostic =>
            diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.SourceTreeOrdinal).ToArray();
        Assert.That(diagnostics, Has.Length.EqualTo(2));
        Assert.That(diagnostics[0].Location, Is.EqualTo(diagnostics[1].Location));
        for (var ordinal = 0; ordinal < diagnostics.Length; ordinal++)
        {
            var diagnostic = diagnostics[ordinal];
            var tree = artifact.Compilation.SyntaxTrees[ordinal];
            using (Assert.EnterMultipleScope())
            {
                Assert.That(diagnostic.Code, Is.EqualTo("compiler.CS0103"));
                Assert.That(diagnostic.Message, Does.Contain(ordinal == 0 ? "MissingA" : "MissingB"));
                Assert.That(diagnostic.SourceTreeOrdinal, Is.EqualTo(ordinal));
                Assert.That(diagnostic.SourceTreePath, Is.EqualTo(tree.Path));
                Assert.That(diagnostic.SourceTreeSha256, Is.EqualTo(tree.Sha256));
                Assert.That(diagnostic.SourceLineMapSha256, Is.EqualTo(tree.LineMapSha256));
            }
        }
        var roundTrip = CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(artifact, TestContext.CurrentContext.CancellationToken),
            TestContext.CurrentContext.CancellationToken);
        Assert.That(roundTrip.CompilerDiagnostics.Select(diagnostic =>
                diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Location),
            Is.EqualTo(artifact.CompilerDiagnostics.Select(diagnostic =>
                diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Location)));
    }

    private static CompilerManifestArtifact CreateArtifact(CSharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        return CompilerManifestArtifactProducer.Create(compilation,
            TestContext.CurrentContext.WorkDirectory, "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth,
            TestContext.CurrentContext.CancellationToken);
    }
}
