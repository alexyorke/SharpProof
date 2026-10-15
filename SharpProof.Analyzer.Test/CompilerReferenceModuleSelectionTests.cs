using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerReferenceModuleSelectionTests
{
    private byte[] _firstImage = [];
    private byte[] _secondImage = [];

    [OneTimeSetUp]
    public void CompileOrdinaryAssemblyImages()
    {
        _firstImage = EmitImage(1);
        _secondImage = EmitImage(2);
    }

    [Test]
    public void NoMatchingModulesRemainAbsent()
    {
        Assert.That(CompilerMetadataResolution.SelectReferenceModules([], CancellationToken.None), Is.Empty);
    }

    [Test]
    public void OneActualModuleKeepsItsReferenceAndMetadata()
    {
        using var files = new ModuleFiles();
        var module = files.Add("only.dll", _firstImage);
        var selected = CompilerMetadataResolution.SelectReferenceModules([module], CancellationToken.None);
        Assert.That(selected, Has.Length.EqualTo(1));
        Assert.That(selected.Single(), Is.SameAs(module));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void IdenticalImagesChooseTheSameOrdinalPathRepresentative(bool reverse)
    {
        using var files = new ModuleFiles();
        var first = files.Add("A.dll", _firstImage);
        var second = files.Add("B.dll", _firstImage);
        CompilerMetadataResolution.ReferenceModule[] matches = reverse ? [second, first] : [first, second];
        var selected = CompilerMetadataResolution.SelectReferenceModules(matches, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(selected, Has.Length.EqualTo(1));
            Assert.That(selected.Single(), Is.SameAs(first));
            Assert.That(selected.Single().Reference.FilePath, Is.EqualTo(first.Path));
        }
    }

    [Test]
    public void DifferentEmittedImagesKeepTwoMatchesForAmbiguityRejection()
    {
        using var files = new ModuleFiles();
        var first = files.Add("A.dll", _firstImage);
        var second = files.Add("B.dll", _secondImage);
        var third = files.Add("C.dll", _firstImage);
        var firstReader = first.Module.GetMetadataReader();
        var secondReader = second.Module.GetMetadataReader();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstReader.GetString(firstReader.GetAssemblyDefinition().Name),
                Is.EqualTo(secondReader.GetString(secondReader.GetAssemblyDefinition().Name)));
            Assert.That(CompilerCompilationCapture.ReadModuleName(firstReader),
                Is.EqualTo(CompilerCompilationCapture.ReadModuleName(secondReader)));
            Assert.That(_secondImage, Is.Not.EqualTo(_firstImage));
        }
        var selected = CompilerMetadataResolution.SelectReferenceModules([first, second, third], CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(selected, Has.Length.EqualTo(2));
            Assert.That(selected[0], Is.SameAs(first));
            Assert.That(selected[1], Is.SameAs(second));
        }
    }

    [Test]
    public void DisappearingOrdinaryFileKeepsTheResultAmbiguous()
    {
        using var files = new ModuleFiles();
        var first = files.Add("A.dll", _firstImage);
        var second = files.Add("B.dll", _firstImage);
        File.Delete(second.Path);
        var selected = CompilerMetadataResolution.SelectReferenceModules([first, second], CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(selected, Has.Length.EqualTo(2));
            Assert.That(selected[0], Is.SameAs(first));
            Assert.That(selected[1], Is.SameAs(second));
        }
    }

    [Test]
    public void CancellationPropagatesBeforeSelectingDuplicateImages()
    {
        using var files = new ModuleFiles();
        var first = files.Add("A.dll", _firstImage);
        var second = files.Add("B.dll", _firstImage);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That((Action)(() => CompilerMetadataResolution.SelectReferenceModules(
            [first, second], cancellation.Token)), Throws.TypeOf<OperationCanceledException>());
    }

    private static byte[] EmitImage(int value)
    {
        return AnalyzerTestHost.EmitImage(AnalyzerTestHost.CreateCompilation(
            $"public static class Dependency {{ public static int Value() => {value}; }}", [])
            .WithAssemblyName("MetadataSelectionDependency"));
    }

    private sealed class ModuleFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(),
            "SharpProofModuleSelection-" + Guid.NewGuid().ToString("N"));
        private readonly List<AssemblyMetadata> _metadata = [];

        internal ModuleFiles()
        {
            Directory.CreateDirectory(_directory);
        }

        internal CompilerMetadataResolution.ReferenceModule Add(string name, byte[] image)
        {
            var path = Path.Combine(_directory, name);
            File.WriteAllBytes(path, image);
            var reference = MetadataReference.CreateFromFile(path);
            var metadata = (AssemblyMetadata)reference.GetMetadata();
            _metadata.Add(metadata);
            var modules = metadata.GetModules();
            Assert.That(modules, Has.Length.EqualTo(1));
            return new(reference, modules.Single(), Path.GetFullPath(path));
        }

        public void Dispose()
        {
            foreach (var metadata in _metadata)
            {
                metadata.Dispose();
            }
            Directory.Delete(_directory, recursive: true);
        }
    }
}
