using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using System.Reflection.PortableExecutable;

namespace SharpProof.Analyzer.Test;

// Actual SDK metadata/file pairing boundary; neither PE image is altered.
[TestFixture]
public sealed class CompilerNativeReferenceBoundaryTests
{
    [Test]
    public void ActualNativeDependencyCannotBackAnEmittedManagedReference()
    {
        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Assert.That(packageRoot, Is.Not.Null.And.Not.Empty);
        var nativePath = Path.Combine(packageRoot!, "microsoft.z3", "4.12.2",
            "runtimes", "win-x64", "native", "libz3.dll");
        Assert.That(File.Exists(nativePath), Is.True, "The pinned package's actual native PE asset must exist.");
        var limits = CompilerCompilationCapture.ReferenceCaptureLimits.Default;
        var nativeLength = new FileInfo(nativePath).Length;
        Assert.That(nativeLength, Is.GreaterThan(0).And.LessThanOrEqualTo(limits.MaximumModuleBytes));
        using (var nativeFile = File.OpenRead(nativePath))
        using (var nativeImage = new PEReader(nativeFile))
        {
            Assert.That(nativeImage.HasMetadata, Is.False);
        }
        var compilation = AnalyzerTestHost.CreateCompilation(
            "public static class Subject { public static int Root(int value) => value; }", []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var image = AnalyzerTestHost.EmitImage(compilation);
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofNativeReference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Subject.dll");
            File.WriteAllBytes(path, image);
            var reference = MetadataReference.CreateFromImage(image, filePath: path);
            var captured = CompilerCompilationCapture.CaptureReferences([reference],
                limits, CancellationToken.None).Single();
            var mismatched = MetadataReference.CreateFromImage(image, filePath: nativePath);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(captured.Kind, Is.EqualTo("Assembly"));
                Assert.That(captured.Modules.Single().SizeBytes, Is.EqualTo(image.Length));
                Assert.That(mismatched.Properties.Kind, Is.EqualTo(MetadataImageKind.Assembly));
                Assert.That(mismatched.FilePath, Is.EqualTo(nativePath));
                Assert.That(reference.GetMetadata(), Is.InstanceOf<AssemblyMetadata>());
                Assert.That(mismatched.GetMetadata(), Is.InstanceOf<AssemblyMetadata>());
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(image));
            }
            var loadContext = new System.Runtime.Loader.AssemblyLoadContext("NativeReferenceControl", isCollectible: true);
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
            var exception = Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerCompilationCapture.CaptureReferences([mismatched], limits, CancellationToken.None)));
            Assert.That(exception!.Message, Is.EqualTo("A compiler reference does not contain metadata."));
            Assert.That(new FileInfo(nativePath).Length, Is.EqualTo(nativeLength));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
