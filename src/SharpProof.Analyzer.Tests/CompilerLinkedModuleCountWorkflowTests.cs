using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerLinkedModuleCountWorkflowTests
{
    private static readonly ImmutableArray<string> ExpectedModuleNames = ["CaptureLinked.dll", "First.netmodule", "Second.netmodule"];

    [Test]
    public void SameGenuineThreeModuleReferenceRejectsCountTwoAndAcceptsCountThree()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofLinkedModuleCount-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var firstPath = Path.Combine(directory, "First.netmodule");
            var secondPath = Path.Combine(directory, "Second.netmodule");
            var manifestPath = Path.Combine(directory, "CaptureLinked.dll");
            var firstImage = EmitModule("First");
            var secondImage = EmitModule("Second");
            File.WriteAllBytes(firstPath, (firstImage).ToArray());
            File.WriteAllBytes(secondPath, (secondImage).ToArray());
            var moduleProperties = new MetadataReferenceProperties(MetadataImageKind.Module);
            var firstReference = MetadataReference.CreateFromImage(firstImage, moduleProperties, filePath: firstPath);
            var secondReference = MetadataReference.CreateFromImage(secondImage, moduleProperties, filePath: secondPath);
            var manifest = AnalyzerTestHost.CreateCompilation(
                "public static class CaptureLinked { public static int Value => First.Value + Second.Value; }", [])
                .WithAssemblyName("CaptureLinked").AddReferences(firstReference, secondReference);
            var manifestImage = AnalyzerTestHost.EmitImage(manifest);
            File.WriteAllBytes(manifestPath, (manifestImage).ToArray());
            using var metadata = AssemblyMetadata.Create(ModuleMetadata.CreateFromImage(manifestImage),
                ModuleMetadata.CreateFromImage(firstImage), ModuleMetadata.CreateFromImage(secondImage));
            var reference = metadata.GetReference(filePath: manifestPath);
            var subject = AnalyzerTestHost.CreateCompilation("internal class Subject { }", []).AddReferences(reference);
            Assert.That(subject.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
            Assert.That(subject.GetAssemblyOrModuleSymbol(reference), Is.InstanceOf<IAssemblySymbol>());
            var maximumModule = Math.Max(((long)manifestImage.Length), Math.Max(((long)firstImage.Length), ((long)secondImage.Length)));
            var closureBytes = ((long)manifestImage.Length) + ((long)firstImage.Length) + ((long)secondImage.Length);
            Assert.That((Action)(() => CompilerCompilationCapture.CaptureReferences([reference],
                new CompilerCompilationCapture.ReferenceCaptureLimits(maximumModule, closureBytes, 2), TestContext.CurrentContext.CancellationToken)),
                Throws.TypeOf<InvalidDataException>().With.Message.EqualTo("A compiler reference exceeds the module count limit."));
            var captured = CompilerCompilationCapture.CaptureReferences([reference],
                new CompilerCompilationCapture.ReferenceCaptureLimits(maximumModule, closureBytes, 3), TestContext.CurrentContext.CancellationToken).Single();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(captured.Kind, Is.EqualTo("Assembly"));
                Assert.That(captured.Modules.Select(module => module is null ? throw new System.ArgumentNullException(nameof(module)) : (module.Name)),
                    Is.EqualTo(ExpectedModuleNames));
                Assert.That(captured.Modules.Select(module => module is null ? throw new System.ArgumentNullException(nameof(module)) : (module.SizeBytes)),
                    Is.EqualTo(ImmutableArray.Create(((long)manifestImage.Length), ((long)firstImage.Length), ((long)secondImage.Length))));
                Assert.That(captured.Modules.Select(module => module is null ? throw new System.ArgumentNullException(nameof(module)) : (module.Path)),
                    Is.EqualTo(ImmutableArray.Create(manifestPath, firstPath, secondPath).Select(Path.GetFullPath)));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ImmutableArray<byte> EmitModule(string name)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        var compilation = AnalyzerTestHost.CreateCompilation(
            $"public static class {name} {{ public static int Value => 1; }}", []).WithAssemblyName(name);
        return AnalyzerTestHost.EmitImage(compilation.WithOptions(compilation.Options.WithOutputKind(OutputKind.NetModule)));
    }
}
