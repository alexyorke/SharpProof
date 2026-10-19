using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerMetadataApiBoundaryTests
{
    [Test]
    public void ActualPlainMetadataBodyRequiresAvailableContractApiWhenBindingIsEnabled()
    {
        var library = AnalyzerTestHost.CreateCompilation(
            "public static class PlainLibrary { public static int Value(int value) => value; }", [])
            .WithAssemblyName("PlainMetadataApiLibrary")
            .WithReferences(TestMetadataReferences.CoreLibraryOnly);
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofMetadataApi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.That(library.GetDiagnostics().Where(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
            var libraryImage = AnalyzerTestHost.EmitImage(library);
            var path = Path.Combine(directory, "PlainMetadataApiLibrary.dll");
            File.WriteAllBytes(path, libraryImage);
            var reference = MetadataReference.CreateFromFile(path);
            const string source = "public static class Caller { public static int Root() => PlainLibrary.Value(7); }";
            var unavailable = AnalyzerTestHost.CreateCompilation(source, [])
                .WithAssemblyName("PlainMetadataApiUnavailableCaller")
                .WithReferences(TestMetadataReferences.WithoutSharpProof.Append(reference));
            var available = AnalyzerTestHost.CreateCompilation(source, [])
                .WithAssemblyName("PlainMetadataApiAvailableCaller")
                .WithReferences(TestMetadataReferences.WithSharpProof.Append(reference));
            var callers = ImmutableArray.Create(unavailable, available);
            foreach (var caller in callers)
            {
                Assert.That(caller.GetDiagnostics().Where(static diagnostic =>
                    diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(unavailable.GetTypeByMetadataName("SharpProof.Attributes.PositiveAttribute"), Is.Null);
                Assert.That(available.GetTypeByMetadataName("SharpProof.Attributes.PositiveAttribute"), Is.Not.Null);
                Assert.That(unavailable.GetTypeByMetadataName("PlainLibrary")!.GetMembers("Value")
                    .OfType<IMethodSymbol>().Single().GetAttributes(), Is.Empty);
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(libraryImage));
            }
            var loadContext = new System.Runtime.Loader.AssemblyLoadContext("MetadataApiControl", isCollectible: true);
            try
            {
                using var libraryStream = new MemoryStream(libraryImage, writable: false);
                loadContext.LoadFromStream(libraryStream);
                foreach (var caller in callers)
                {
                    using var image = new MemoryStream(AnalyzerTestHost.EmitImage(caller), writable: false);
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
            using (Assert.EnterMultipleScope())
            {
                foreach (var caller in callers)
                {
                    var snapshot = CompilerCompilationCapture.Capture(caller, directory,
                        "net8.0", [], TestContext.CurrentContext.CancellationToken);
                    var preparation = CompilerTotalCallableLowerer.PrepareShadowCallers(caller,
                        WorkerFeatureSet.All, snapshot.SyntaxTrees, snapshot.References,
                        authority, TestContext.CurrentContext.CancellationToken, enableMetadataRequires: true);
                    TestContext.WriteLine("Actual metadata API workflow: " +
                        (ReferenceEquals(caller, unavailable) ? "unavailable" : "available") +
                        "; callers=" + preparation.Callers.Length + "; gaps=" + preparation.Gaps.Length +
                        "; reasons=" + string.Join(",", preparation.Gaps.Select(static gap => gap is null ? throw new System.ArgumentNullException(nameof(gap)) : (gap.Reason))));
                    Assert.That(preparation.Callers, Is.Empty);
                    if (ReferenceEquals(caller, unavailable))
                    {
                        Assert.That(preparation.Gaps, Has.Length.EqualTo(1));
                        Assert.That(preparation.Gaps.Single().Reason, Is.EqualTo("UnsupportedOwnContracts"));
                    }
                    else
                    {
                        Assert.That(preparation.Gaps, Is.Empty);
                    }
                }
            }
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(libraryImage));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
