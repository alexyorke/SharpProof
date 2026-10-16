using System.Diagnostics;
using System.Globalization;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerSourceInitializationCacheSampleTests
{
    [Test]
    public void ActualSourceGetterPreconditionsAdmitMultipleDistinctTypesAcrossOwners()
    {
        const int ownerCount = 4;
        const int gettersPerOwner = 4;
        var clock = Stopwatch.StartNew();
        var getters = string.Join("\n", Enumerable.Range(0, ownerCount * gettersPerOwner).Select(index =>
            "public static class SourceEntry" + index.ToString(CultureInfo.InvariantCulture) +
            " { public static int Value { get { Contract.Requires(true); return 1; } } }"));
        var owners = string.Join("\n", Enumerable.Range(0, ownerCount).Select(owner =>
            "public static int Read" + owner.ToString(CultureInfo.InvariantCulture) + "() => " +
            string.Join(" + ", Enumerable.Range(owner * gettersPerOwner, gettersPerOwner).Select(index =>
                "SourceEntry" + index.ToString(CultureInfo.InvariantCulture) + ".Value")) + ";"));
        var source = "using SharpProof.Attributes;\n" + getters + "\npublic static class SourceCacheRoot { " + owners + " }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, [], filePath: "source-getter-cache-sample.cs");
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var root = tree.GetRoot();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.DescendantNodes().OfType<ClassDeclarationSyntax>().Count(),
                Is.EqualTo(ownerCount * gettersPerOwner + 1));
            Assert.That(root.DescendantNodes().OfType<MethodDeclarationSyntax>().Count(), Is.EqualTo(ownerCount));
            Assert.That(root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Count(),
                Is.EqualTo(ownerCount * gettersPerOwner));
        }
        var image = AnalyzerTestHost.EmitImage(compilation);
        var runtime = new AssemblyLoadContext("SourceCacheSample", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image, writable: false);
            var type = runtime.LoadFromStream(stream).GetType("SourceCacheRoot", throwOnError: true)!;
            for (var owner = 0; owner < ownerCount; owner++)
            {
                Assert.That(type.GetMethod("Read" + owner.ToString(CultureInfo.InvariantCulture))!
                    .Invoke(null, []), Is.EqualTo(gettersPerOwner));
            }
        }
        finally
        {
            runtime.Unload();
        }
        var sdkAndClrMilliseconds = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        var inventory = new ClaimManifestBuilder(compilation).BuildPotentialCallShadow();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(inventory.Gaps, Is.Empty);
            Assert.That(inventory.Owners, Has.Length.EqualTo(ownerCount));
            Assert.That(inventory.Owners.Select(static owner => owner.DiscoveryComplete), Is.All.True);
        }
        var prepared = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation,
            WorkerFeatureSet.All, CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None),
            null, CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Gaps, Is.Empty);
            Assert.That(prepared.Callers, Has.Length.EqualTo(ownerCount));
            Assert.That(prepared.Callers.Select(static caller => caller.Body.IsBodyAbstraction), Is.All.False);
            Assert.That(prepared.Callers.Select(static caller => caller.Body.CallPreconditions.Length),
                Is.All.EqualTo(gettersPerOwner));
            Assert.That(prepared.Callers.SelectMany(static caller => caller.Body.CallPreconditions)
                .Select(static clause => clause.MetadataClause), Is.All.Null);
        }
        TestContext.WriteLine("Source getter cache sample: owners=" + ownerCount.ToString(CultureInfo.InvariantCulture) +
            "; getters=" + (ownerCount * gettersPerOwner).ToString(CultureInfo.InvariantCulture) +
            "; actualSyntaxNodes=" + root.DescendantNodesAndSelf().Count().ToString(CultureInfo.InvariantCulture) +
            "; sdkAndClrMilliseconds=" + sdkAndClrMilliseconds.ToString(CultureInfo.InvariantCulture) +
            "; censusAndPreparationMilliseconds=" + clock.Elapsed.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
    }
}