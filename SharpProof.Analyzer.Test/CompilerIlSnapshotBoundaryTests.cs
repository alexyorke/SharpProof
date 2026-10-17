using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerIlSnapshotBoundaryTests
{
    [Test]
    public void ActualEarlierReferenceSnapshotCannotAdmitAnAddedEmittedLibraryBody()
    {
        var first = AnalyzerTestHost.CreateCompilation(
            "using SharpProof.Attributes; public static class First { public static int Value([Positive] int value) => value; }", [])
            .WithAssemblyName("SnapshotFirst");
        var second = AnalyzerTestHost.CreateCompilation(
            "using SharpProof.Attributes; public static class Second { public static int Value([Positive] int value) => value; }", [])
            .WithAssemblyName("SnapshotSecond");
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofIlSnapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var firstImage = AnalyzerTestHost.EmitImage(first);
            var secondImage = AnalyzerTestHost.EmitImage(second);
            var firstPath = Path.Combine(directory, "SnapshotFirst.dll");
            var secondPath = Path.Combine(directory, "SnapshotSecond.dll");
            File.WriteAllBytes(firstPath, firstImage);
            File.WriteAllBytes(secondPath, secondImage);
            var firstReference = MetadataReference.CreateFromFile(firstPath);
            var secondReference = MetadataReference.CreateFromFile(secondPath);
            var earlier = AnalyzerTestHost.CreateCompilation(
                "public static class Caller { public static int Root() => First.Value(7); }", [],
                additionalReferences: [firstReference]).WithAssemblyName("SnapshotEarlierCaller");
            var current = AnalyzerTestHost.CreateCompilation(
                "public static class Caller { public static int Root() => Second.Value(9); }", [],
                additionalReferences: [firstReference, secondReference]).WithAssemblyName("SnapshotCurrentCaller");
            foreach (var compilation in new[] { first, second, earlier, current })
            {
                Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            }
            var earlierSnapshot = CompilerCompilationCapture.Capture(earlier, directory,
                "net8.0", [], TestContext.CurrentContext.CancellationToken);
            var currentSnapshot = CompilerCompilationCapture.Capture(current, directory,
                "net8.0", [], TestContext.CurrentContext.CancellationToken);
            var loadContext = new System.Runtime.Loader.AssemblyLoadContext("IlSnapshot", isCollectible: true);
            try
            {
                using var firstStream = new MemoryStream(firstImage, writable: false);
                using var secondStream = new MemoryStream(secondImage, writable: false);
                loadContext.LoadFromStream(firstStream);
                loadContext.LoadFromStream(secondStream);
                foreach (var (compilation, expected) in new[] { (earlier, 7), (current, 9) })
                {
                    using var image = new MemoryStream(AnalyzerTestHost.EmitImage(compilation), writable: false);
                    var assembly = loadContext.LoadFromStream(image);
                    Assert.That(assembly.GetType("Caller", throwOnError: true)!.GetMethod("Root")!
                        .Invoke(null, []), Is.EqualTo(expected));
                }
            }
            finally
            {
                loadContext.Unload();
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(earlierSnapshot.References.SelectMany(reference => reference.Modules)
                    .Any(module => Path.GetFullPath(module.Path) == secondPath), Is.False);
                Assert.That(currentSnapshot.References.SelectMany(reference => reference.Modules)
                    .Any(module => Path.GetFullPath(module.Path) == secondPath), Is.True);
                Assert.That(File.ReadAllBytes(firstPath), Is.EqualTo(firstImage));
                Assert.That(File.ReadAllBytes(secondPath), Is.EqualTo(secondImage));
            }
            var authority = CompilerSpecificationPackProvider.ResolveConfiguration([]);
            var stale = CompilerTotalCallableLowerer.PrepareShadowCallers(current,
                WorkerFeatureSet.All, currentSnapshot.SyntaxTrees, earlierSnapshot.References,
                authority, TestContext.CurrentContext.CancellationToken, enableMetadataRequires: true);
            var fresh = CompilerTotalCallableLowerer.PrepareShadowCallers(current,
                WorkerFeatureSet.All, currentSnapshot.SyntaxTrees, currentSnapshot.References,
                authority, TestContext.CurrentContext.CancellationToken, enableMetadataRequires: true);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(stale.Callers, Is.Empty);
                Assert.That(stale.Gaps, Has.Length.EqualTo(1));
                Assert.That(fresh.Callers, Has.Length.EqualTo(1));
                Assert.That(fresh.Gaps, Is.Empty);
            }
            Assert.That(stale.Gaps.Single().Reason, Is.EqualTo("UnsupportedBody"));
            var body = fresh.Callers.Single().Body;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(body.IsBodyAbstraction, Is.False);
                Assert.That(body.CallPreconditions, Has.Length.EqualTo(1));
                Assert.That(body.CallPreconditions.Single().MetadataClause, Is.Not.Null);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
