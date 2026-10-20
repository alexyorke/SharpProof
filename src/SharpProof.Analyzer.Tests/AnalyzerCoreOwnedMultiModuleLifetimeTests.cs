using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreOwnedMultiModuleLifetimeTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void RealAdditionalModuleLifetimeRetainsClosedPreconditionAdmission(bool closedPrecondition, bool removeModule)
    {
        const string moduleName = "OwnedAdditionalModule.netmodule";
        var parameter = closedPrecondition ? "[Positive] int value" : "int value";
        var moduleCompilation = AnalyzerTestHost.CreateCompilation(
            "using SharpProof.Attributes; public static class Api { public static int Value(" + parameter + ") => value; }", [])
            .WithAssemblyName("OwnedAdditionalModule");
        moduleCompilation = moduleCompilation.WithOptions(moduleCompilation.Options
            .WithOutputKind(OutputKind.NetModule).WithModuleName(moduleName));
        Assert.That(moduleCompilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var moduleImage = AnalyzerTestHost.EmitImage(moduleCompilation);
        Assert.That(moduleImage, Is.Not.Empty);
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SharpProofCore052-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var modulePath = Path.GetFullPath(Path.Combine(directory, moduleName));
        var assemblyPath = Path.GetFullPath(Path.Combine(directory, "OwnedMultiModuleAssembly.dll"));
        Assert.That(modulePath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal), Is.True);
        Assert.That(assemblyPath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal), Is.True);
        try
        {
            File.WriteAllBytes(modulePath, (moduleImage).ToArray());
            Assert.That(File.ReadAllBytes(modulePath), Is.EqualTo(moduleImage));
            var moduleReference = MetadataReference.CreateFromFile(modulePath, MetadataReferenceProperties.Module);
            Assert.That(moduleReference.Properties.Kind, Is.EqualTo(MetadataImageKind.Module));
            var assemblyCompilation = AnalyzerTestHost.CreateCompilation(
                "public static class Container { public static int Target() => Api.Value(1); }", [])
                .WithAssemblyName("OwnedMultiModuleAssembly").AddReferences(moduleReference);
            Assert.That(assemblyCompilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
            var assemblyImage = AnalyzerTestHost.EmitImage(assemblyCompilation);
            Assert.That(assemblyImage, Is.Not.Empty);
            File.WriteAllBytes(assemblyPath, (assemblyImage).ToArray());
            Assert.That(File.ReadAllBytes(assemblyPath), Is.EqualTo(assemblyImage));
            var reference = MetadataReference.CreateFromFile(assemblyPath);
            Assert.That(reference.FilePath, Is.EqualTo(assemblyPath));
            if (removeModule)
            {
                File.Delete(modulePath);
            }

            Assert.That(File.Exists(modulePath), Is.EqualTo(!removeModule));
            Assert.That(File.ReadAllBytes(assemblyPath), Is.EqualTo(assemblyImage));
            var result = SharpProofAnalyzerEngine.PortableReferenceContainsClosedPrecondition(reference);
            TestContext.Progress.WriteLine($"closed={closedPrecondition}; removed={removeModule}; helper={result}; moduleBytes={moduleImage.Length}; assemblyBytes={assemblyImage.Length}");
            Assert.That(result, Is.EqualTo(removeModule || closedPrecondition));
            if (removeModule)
            {
                Assert.Catch<IOException>((Action)(() =>
                {
                    using var metadata = (AssemblyMetadata)reference.GetMetadata();
                    _ = metadata.GetModules();
                }));
            }
            else
            {
                using var metadata = (AssemblyMetadata)reference.GetMetadata();
                Assert.That(metadata.GetModules(), Has.Length.EqualTo(2));
            }
        }
        finally
        {
            if (File.Exists(modulePath))
            {
                File.Delete(modulePath);
            }

            if (File.Exists(assemblyPath))
            {
                File.Delete(assemblyPath);
            }

            Directory.Delete(directory);
        }
    }
}
