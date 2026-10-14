using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerReferenceKindBoundaryTests
{
    [Test]
    public void PublicSdkPropertyClonePreservesBackingMetadataAndRejectsMismatchedKind()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            "public static class Subject { public static int Root(int value) => value; }", []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var image = AnalyzerTestHost.EmitImage(compilation);
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofReferenceKind-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Subject.dll");
            File.WriteAllBytes(path, image);
            var reference = MetadataReference.CreateFromImage(image, filePath: path);
            var captured = CompilerCompilationCapture.CaptureReferences([reference],
                CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None).Single();
            var loadContext = new System.Runtime.Loader.AssemblyLoadContext("ReferenceKind", isCollectible: true);
            try
            {
                using var stream = new MemoryStream(image, writable: false);
                var assembly = loadContext.LoadFromStream(stream);
                Assert.That(assembly.GetType("Subject", throwOnError: true)!.GetMethod("Root")!
                    .Invoke(null, [7]), Is.EqualTo(7));
            }
            finally
            {
                loadContext.Unload();
            }
            var mismatched = reference.WithProperties(MetadataReferenceProperties.Module);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(captured.Kind, Is.EqualTo("Assembly"));
                Assert.That(captured.Modules.Single().SizeBytes, Is.EqualTo(image.Length));
                Assert.That(mismatched.Properties.Kind, Is.EqualTo(MetadataImageKind.Module));
                Assert.That(mismatched.FilePath, Is.EqualTo(path));
                Assert.That(reference.GetMetadata(), Is.InstanceOf<AssemblyMetadata>());
                Assert.That(mismatched.GetMetadata(), Is.InstanceOf<AssemblyMetadata>());
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(image));
            }
            var exception = Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerCompilationCapture.CaptureReferences([mismatched],
                    CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None)));
            Assert.That(exception!.Message, Is.EqualTo("A compiler reference kind does not match its metadata."));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
