using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;

namespace SharpProof.Analyzer.Test;

// Component source ownership boundary; public artifact capture rejects CompilationReference earlier.
[TestFixture]
public sealed class CompilerReferencedBodyOwnershipTests
{
    [Test]
    public void ActualBuilderReferencedTargetCannotUseRootOnlySourceOrdinals()
    {
        const string dependencySource = "public static class External { public static int Read(int value) => value; }";
        const string companionSource = """
            [ContractFor(typeof(External))]
            public static class ExternalContracts {
                public static int Read(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            public static class Caller { public static int Root() => External.Read(7); }
            """;
        var dependency = AnalyzerTestHost.CreateCompilation(dependencySource, [], filePath: "Dependency.cs")
            .WithAssemblyName("BodyOwnershipDependency");
        var reference = dependency.ToMetadataReference();
        var referenced = AnalyzerTestHost.CreateCompilation("using SharpProof.Attributes;\n" + companionSource, [],
            additionalReferences: [reference], filePath: "ReferencedCompanion.cs")
            .WithAssemblyName("BodyOwnershipReferencedCaller");
        var owned = AnalyzerTestHost.CreateCompilation("using SharpProof.Attributes;\n" + dependencySource + "\n" + companionSource,
            [], filePath: "OwnedCompanion.cs").WithAssemblyName("BodyOwnershipOwnedCaller");
        foreach (var compilation in ImmutableArray.Create(dependency, referenced, owned))
        {
            Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        }
        var dependencyImage = AnalyzerTestHost.EmitImage(dependency);
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("BodyOwnershipControl", isCollectible: true);
        try
        {
            using var dependencyStream = new MemoryStream((dependencyImage).ToArray(), writable: false);
            loadContext.LoadFromStream(dependencyStream);
            foreach (var compilation in ImmutableArray.Create(referenced, owned))
            {
                using var image = new MemoryStream((AnalyzerTestHost.EmitImage(compilation)).ToArray(), writable: false);
                var assembly = loadContext.LoadFromStream(image);
                Assert.That(assembly.GetType("Caller", throwOnError: true)!.GetMethod("Root")!
                    .Invoke(null, []), Is.EqualTo(7));
            }
        }
        finally
        {
            loadContext.Unload();
        }
        var authority = CompilerSpecificationPackProvider.ResolveConfiguration([]);
        foreach (var compilation in ImmutableArray.Create(referenced, owned))
        {
            var discovery = new ClaimManifestBuilder(compilation).Build();
            Assert.That(discovery.Targets, Has.Count.EqualTo(1));
            Assert.That(discovery.Manifest.Claims, Has.Length.EqualTo(1));
            var target = discovery.Targets.Values.Single();
            Assert.That(target.Method.ContainingType.Name, Is.EqualTo("External"));
            Assert.That(target.Method.Name, Is.EqualTo("Read"));
            var declaration = target.Method.DeclaringSyntaxReferences.Single().GetSyntax();
            var isOwned = ReferenceEquals(compilation, owned);
            Assert.That(compilation.SyntaxTrees.Contains(declaration.SyntaxTree), Is.EqualTo(isOwned));
            var trees = CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken);
            var collected = CompilerReachableSourceCollector.Collect(compilation,
                discovery.Targets.Values, trees.ToImmutableArray(), authority, TestContext.CurrentContext.CancellationToken);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(collected.CollectionComplete, Is.EqualTo(isOwned));
                Assert.That(collected.Roots, Has.Length.EqualTo(isOwned ? 1 : 0));
                Assert.That(collected.Bodies, Has.Length.EqualTo(isOwned ? 1 : 0));
                Assert.That(collected.Documents, Has.Length.EqualTo(isOwned ? 1 : 0));
            }
            if (isOwned)
            {
                Assert.That(collected.Roots.Single().CallableId, Is.EqualTo(target.Entry.CallableId));
                Assert.That(collected.Bodies.Single().SourceTreeOrdinal, Is.EqualTo(0));
            }
        }
    }
}
