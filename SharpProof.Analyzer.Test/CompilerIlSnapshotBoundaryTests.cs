using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerIlSnapshotBoundaryTests
{
    [Test]
    public void ActualEarlierReferenceSnapshotCannotAdmitAnAddedEmittedLibraryBody()
    {
        var first = AnalyzerTestHost.CreateCompilation(
            "public static class First { public static int Value() => 7; }", [])
            .WithAssemblyName("SnapshotFirst");
        var second = AnalyzerTestHost.CreateCompilation(
            "public static class Second { public static int Value() => 9; }", [])
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
                "public static class Caller { public static int Root() => First.Value(); }", [],
                additionalReferences: [firstReference]).WithAssemblyName("SnapshotEarlierCaller");
            var current = AnalyzerTestHost.CreateCompilation(
                "public static class Caller { public static int Root() => Second.Value(); }", [],
                additionalReferences: [firstReference, secondReference]).WithAssemblyName("SnapshotCurrentCaller");
            foreach (var compilation in new[] { first, second, earlier, current })
            {
                Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            }
            var earlierSnapshots = CompilerCompilationCapture.CaptureReferences(earlier.References,
                CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None);
            var currentSnapshots = CompilerCompilationCapture.CaptureReferences(current.References,
                CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None);
            var method = (IMethodSymbol)current.GetTypeByMetadataName("Second")!.GetMembers("Value").Single();
            var staleProvider = new CompilerTotalIlBodyProvider(current, earlierSnapshots);
            var freshProvider = new CompilerTotalIlBodyProvider(current, currentSnapshots);
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
                Assert.That(staleProvider.Resolve(method, CancellationToken.None), Is.Null);
                Assert.That(staleProvider.Resolve(method, CancellationToken.None), Is.Null);
                Assert.That(freshProvider.Resolve(method, CancellationToken.None), Is.Not.Null);
                Assert.That(File.ReadAllBytes(firstPath), Is.EqualTo(firstImage));
                Assert.That(File.ReadAllBytes(secondPath), Is.EqualTo(secondImage));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
