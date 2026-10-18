using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using System.Globalization;

namespace SharpProof.Analyzer.Test;

// Independent compilation-reference graph census; final PE capture is a separate boundary.
[TestFixture]
public sealed class CompilerCoreLibraryGraphSampleTests
{
    [TestCase(8)]
    [TestCase(16)]
    public void ActualEmittedCoreLibraryGraphRetainsDistinctTreeOwnership(int leafCount)
    {
        var leaves = Enumerable.Range(0, leafCount).Select(index =>
            AnalyzerTestHost.CreateCompilation(
                "public static class Leaf" + index.ToString(CultureInfo.InvariantCulture) +
                " { public static int Value(int value) => value + " +
                index.ToString(CultureInfo.InvariantCulture) + "; }", [],
                filePath: "Leaf" + index.ToString(CultureInfo.InvariantCulture) + ".cs")
                .WithAssemblyName("CoreGraphLeaf_" + leafCount.ToString(CultureInfo.InvariantCulture) +
                    "_" + index.ToString(CultureInfo.InvariantCulture))
                .WithReferences(TestMetadataReferences.CoreLibraryOnly)).ToArray();
        var references = leaves.Select(static leaf => leaf is null ? throw new System.ArgumentNullException(nameof(leaf)) : (leaf.ToMetadataReference())).ToArray();
        var expression = string.Join(" + ", Enumerable.Range(0, leafCount).Select(index =>
            "Leaf" + index.ToString(CultureInfo.InvariantCulture) + ".Value(value)"));
        var caller = AnalyzerTestHost.CreateCompilation(
            "public static class Caller { public static int Root(int value) => " + expression + "; }",
            [], additionalReferences: references, filePath: "Caller.cs")
            .WithAssemblyName("CoreGraphCaller_" + leafCount.ToString(CultureInfo.InvariantCulture))
            .WithReferences(TestMetadataReferences.CoreLibraryOnly.Concat(references));
        var compilations = leaves.Append(caller).ToArray();
        foreach (var compilation in compilations)
        {
            Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(caller.References.OfType<CompilationReference>().Count(), Is.EqualTo(leafCount));
            Assert.That(leaves.Select(static leaf => leaf is null ? throw new System.ArgumentNullException(nameof(leaf)) : (leaf.References.Count())), Is.All.EqualTo(1));
            Assert.That(caller.References.Count(), Is.EqualTo(leafCount + 1));
            Assert.That(compilations.SelectMany(static compilation => compilation is null ? throw new System.ArgumentNullException(nameof(compilation)) : (compilation.SyntaxTrees))
                .Distinct().Count(), Is.EqualTo(leafCount + 1));
            Assert.That(references.Select(reference =>
                reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (((IAssemblySymbol)caller.GetAssemblyOrModuleSymbol(reference)!).Name)),
                Is.EqualTo(leaves.Select(static leaf => leaf is null ? throw new System.ArgumentNullException(nameof(leaf)) : (leaf.AssemblyName))));
        }
        var images = compilations.Select(AnalyzerTestHost.EmitImage).ToArray();
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("GraphControl", isCollectible: true);
        try
        {
            for (var index = 0; index < leaves.Length; index++)
            {
                using var stream = new MemoryStream(images[index], writable: false);
                loadContext.LoadFromStream(stream);
            }
            using var callerStream = new MemoryStream(images[^1], writable: false);
            var runtimeCaller = loadContext.LoadFromStream(callerStream);
            Assert.That(runtimeCaller.GetType("Caller", throwOnError: true)!.GetMethod("Root")!
                .Invoke(null, [17]), Is.EqualTo(17 * leafCount + leafCount * (leafCount - 1) / 2));
        }
        finally
        {
            loadContext.Unload();
        }
        var inventory = new ClaimManifestBuilder(caller).BuildPotentialCallShadow();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(inventory.Gaps, Is.Empty);
            Assert.That(inventory.Owners, Has.Length.EqualTo(1));
            Assert.That(inventory.Owners.Single().Method.Name, Is.EqualTo("Root"));
            Assert.That(inventory.Owners.Single().DiscoveryComplete, Is.True);
        }
    }
}
