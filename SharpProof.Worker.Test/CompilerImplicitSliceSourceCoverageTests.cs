using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerImplicitSliceSourceCoverageTests
{
    [TestCase(false, true)]
    [TestCase(true, false)]
    public void GenuineInheritedLengthDistinguishesMetadataAndSourceSlice(bool sourceSlice, bool expectedComplete)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofImplicitSlice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("ImplicitSliceSource", isCollectible: true);
        try
        {
            var dependency = TestCompilation.Create("SliceDependency",
                "public class ExternalBase { public int Length => 3; public ExternalBase Slice(int start,int length) => this; }",
                includeSharpProofReference: false);
            TestCompilation.AssertNoErrors(dependency);
            using var dependencyImage = new MemoryStream();
            var emittedDependency = dependency.Emit(dependencyImage);
            Assert.That(emittedDependency.Success, Is.True, string.Join(Environment.NewLine, emittedDependency.Diagnostics));
            Assert.That(dependencyImage.Length, Is.GreaterThan(0));
            var dependencyPath = Path.Combine(directory, "dependency.dll");
            File.WriteAllBytes(dependencyPath, dependencyImage.ToArray());
            var reference = MetadataReference.CreateFromFile(dependencyPath);
            var source = "#undef SHARPPROOF_CONTRACTS\nusing SharpProof.Attributes; public sealed class Value:ExternalBase { " +
                (sourceSlice ? "public new Value Slice(int start,int length) => this;" : string.Empty) +
                "} public static class Subject { public static int Root(Value value) { Contract.Ensures(Contract.Result<int>()==1); _=value[1..^1]; return 1; } }";
            var compilation = CSharpCompilation.Create("ImplicitSliceConsumer_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(source, (CSharpParseOptions)dependency.SyntaxTrees.Single().Options, "slice.cs")],
                TestMetadataReferences.WithSharpProof.Add(reference),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            TestCompilation.AssertNoErrors(compilation);
            using var image = new MemoryStream();
            var emitted = compilation.Emit(image);
            Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
            Assert.That(image.Length, Is.GreaterThan(0));
            var tree = compilation.SyntaxTrees.Single();
            var model = compilation.GetSemanticModel(tree);
            var indexerSyntax = tree.GetRoot().DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();
            var operation = model.GetOperation(indexerSyntax);
            Assert.That(operation, Is.InstanceOf<IImplicitIndexerReferenceOperation>());
            var indexer = (IImplicitIndexerReferenceOperation)operation!;
            Assert.That(indexer.LengthSymbol, Is.InstanceOf<IPropertySymbol>());
            var length = (IPropertySymbol)indexer.LengthSymbol!;
            Assert.That(SymbolEqualityComparer.Default.Equals(length.ContainingAssembly, compilation.Assembly), Is.False);
            Assert.That(length.GetMethod!.IsVirtual, Is.False);
            Assert.That(length.SetMethod, Is.Null);
            Assert.That(indexer.IndexerSymbol, Is.InstanceOf<IMethodSymbol>());
            var slice = (IMethodSymbol)indexer.IndexerSymbol!;
            Assert.That(slice.IsVirtual, Is.False);
            Assert.That(SymbolEqualityComparer.Default.Equals(slice.ContainingAssembly, compilation.Assembly), Is.EqualTo(sourceSlice));
            if (sourceSlice)
            { Assert.That(slice.DeclaringSyntaxReferences, Has.Length.EqualTo(1)); }
            dependencyImage.Position = 0;
            runtime.LoadFromStream(dependencyImage);
            image.Position = 0;
            var assembly = runtime.LoadFromStream(image);
            var value = Activator.CreateInstance(assembly.GetType("Value")!);
            Assert.That(assembly.GetType("Subject")!.GetMethod("Root")!.Invoke(null, [value]), Is.EqualTo(1));
            var artifact = CompilerManifestArtifactProducer.Create(compilation, directory, "net9.0", WorkerFeatureSet.All,
                new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
            var claim = artifact.Manifest.Claims.Single();
            var reachable = artifact.ReachableSource!;
            var root = reachable.Roots.Single();
            var body = reachable.Bodies.Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.BodyId == root.BodyId));
            var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (node.Identifier.ValueText == "Root"));
            Assert.That(root.CallableId, Is.EqualTo(claim.CallableId));
            Assert.That(body.MethodIdentity, Is.EqualTo(claim.CallableId));
            Assert.That(body.Start, Is.EqualTo(declaration.SpanStart));
            Assert.That(body.Length, Is.EqualTo(declaration.Span.Length));
            Assert.That(body.CallsComplete, Is.EqualTo(expectedComplete));
            Assert.That(reachable.CollectionComplete, Is.EqualTo(expectedComplete));
            Assert.That(artifact.CompilerDiagnostics, Is.Empty);
        }
        finally
        {
            runtime.Unload();
            Directory.Delete(directory, recursive: true);
        }
    }
}
