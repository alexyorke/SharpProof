using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Loader;
using System.Text.Json;

namespace SharpProof.Analyzer.Test;

// Actual compilation-reference census inputs; final PE capture is a separate boundary.
[TestFixture]
public sealed class CompilerReferenceBudgetShapeSampleTests
{
    [TestCase(8)]
    [TestCase(16)]
    public void ActualUnusedCompilationGraphRetainsBudgetHeadroom(int leafCount)
    {
        MeasureGraph("empty", leafCount, 0);
    }

    [TestCase(512)]
    [TestCase(1024)]
    public void ActualFlatMethodGraphRetainsSharedBudgetHeadroom(int statementsPerLeaf)
    {
        MeasureGraph("flat", 16, statementsPerLeaf);
    }

    private static void MeasureGraph(string shape, int leafCount, int statementsPerLeaf)
    {
        var totalClock = Stopwatch.StartNew();
        var phaseClock = Stopwatch.StartNew();
        var suffix = shape + "_" + leafCount.ToString(CultureInfo.InvariantCulture) + "_" +
            statementsPerLeaf.ToString(CultureInfo.InvariantCulture);
        var leaves = Enumerable.Range(0, leafCount).Select(index =>
        {
            var number = index.ToString(CultureInfo.InvariantCulture);
            var source = statementsPerLeaf == 0 ? string.Empty :
                "public static class BudgetLeaf" + number +
                " { public static void Touch() { " + new string(';', statementsPerLeaf) + " } }";
            return AnalyzerTestHost.CreateCompilation(source, [], filePath: "BudgetLeaf" + number + ".cs")
                .WithAssemblyName("BudgetShapeLeaf_" + suffix + "_" + number)
                .WithReferences(TestMetadataReferences.CoreLibraryOnly);
        }).ToArray();
        var references = leaves.Select(static leaf => leaf.ToMetadataReference()).ToArray();
        var callerSource = statementsPerLeaf == 0
            ? "public static class BudgetRoot { public static int Read() => 7; }"
            : "public static class BudgetRoot { public static int Read() { " +
                string.Join(" ", Enumerable.Range(0, leafCount).Select(index =>
                    "BudgetLeaf" + index.ToString(CultureInfo.InvariantCulture) + ".Touch();")) +
                " return 7; } }";
        var caller = AnalyzerTestHost.CreateCompilation(callerSource, [], filePath: "BudgetRoot.cs")
            .WithAssemblyName("BudgetShapeRoot_" + suffix)
            .WithReferences(TestMetadataReferences.CoreLibraryOnly.Concat(references));
        var compilations = leaves.Append(caller).ToArray();
        var creationMilliseconds = phaseClock.Elapsed.TotalMilliseconds;
        phaseClock.Restart();
        foreach (var compilation in compilations)
        {
            Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        }
        var diagnosticsMilliseconds = phaseClock.Elapsed.TotalMilliseconds;
        phaseClock.Restart();
        var leafNodeCounts = leaves.Select(static leaf =>
            leaf.SyntaxTrees.Single().GetRoot().DescendantNodesAndSelf().Count()).ToArray();
        var rootNodeCount = caller.SyntaxTrees.Single().GetRoot().DescendantNodesAndSelf().Count();
        var maximumDepth = compilations.SelectMany(static compilation => compilation.SyntaxTrees)
            .SelectMany(static tree => tree.GetRoot().DescendantNodesAndSelf())
            .Max(static node => node.Ancestors().Count());
        var traversedReferences = compilations.Sum(static compilation => compilation.References.Count());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(TestMetadataReferences.CoreLibraryOnly, Has.Length.EqualTo(1));
            Assert.That(leaves.Select(static leaf => leaf.References.Count()), Is.All.EqualTo(1));
            Assert.That(caller.References.OfType<CompilationReference>().Count(), Is.EqualTo(leafCount));
            Assert.That(caller.References.Count(), Is.EqualTo(leafCount + 1));
            Assert.That(compilations.SelectMany(static compilation => compilation.SyntaxTrees).Distinct().Count(),
                Is.EqualTo(leafCount + 1));
            Assert.That(references.Select(reference =>
                    ((IAssemblySymbol)caller.GetAssemblyOrModuleSymbol(reference)!).Name),
                Is.EqualTo(leaves.Select(static leaf => leaf.AssemblyName)));
            Assert.That(leaves.Select(static leaf => leaf.SyntaxTrees.Single().GetRoot()
                    .DescendantNodes().OfType<EmptyStatementSyntax>().Count()),
                Is.All.EqualTo(statementsPerLeaf));
            if (statementsPerLeaf == 0)
            {
                Assert.That(leaves.Select(static leaf =>
                    leaf.SyntaxTrees.Single().GetRoot().ChildNodes().Any()), Is.All.False);
            }
            Assert.That(maximumDepth, Is.LessThan(128));
            Assert.That(traversedReferences, Is.EqualTo(leafCount * 2 + 1));
        }
        var inspectionMilliseconds = phaseClock.Elapsed.TotalMilliseconds;
        phaseClock.Restart();
        var images = compilations.Select(AnalyzerTestHost.EmitImage).ToArray();
        var emitMilliseconds = phaseClock.Elapsed.TotalMilliseconds;
        phaseClock.Restart();
        var loadContext = new AssemblyLoadContext("BudgetShapeControl_" + suffix, isCollectible: true);
        try
        {
            for (var index = 0; index < leaves.Length; index++)
            {
                using var stream = new MemoryStream(images[index], writable: false);
                var loaded = loadContext.LoadFromStream(stream);
                Assert.That(loaded.GetName().Name, Is.EqualTo(leaves[index].AssemblyName));
            }
            using var rootStream = new MemoryStream(images[^1], writable: false);
            var runtimeRoot = loadContext.LoadFromStream(rootStream);
            Assert.That(runtimeRoot.GetType("BudgetRoot", throwOnError: true)!.GetMethod("Read")!
                .Invoke(null, []), Is.EqualTo(7));
        }
        finally
        {
            loadContext.Unload();
        }
        var clrMilliseconds = phaseClock.Elapsed.TotalMilliseconds;
        phaseClock.Restart();
        var inventory = new ClaimManifestBuilder(caller).BuildPotentialCallShadow();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(inventory.Gaps, Is.Empty);
            Assert.That(inventory.Owners, Has.Length.EqualTo(1));
            Assert.That(inventory.Owners.Single().Method.Name, Is.EqualTo("Read"));
            Assert.That(inventory.Owners.Single().DiscoveryComplete, Is.True);
        }
        TestContext.WriteLine(JsonSerializer.Serialize(new
        {
            shape,
            leafCount,
            statementsPerLeaf,
            rootNodeCount,
            leafNodeCounts,
            maximumDepth,
            traversedReferences,
            countedInputSyntaxNodesAndReferenceSlots = rootNodeCount + leafNodeCounts.Sum() + traversedReferences,
            creationMilliseconds,
            diagnosticsMilliseconds,
            inspectionMilliseconds,
            emitMilliseconds,
            clrMilliseconds,
            censusMilliseconds = phaseClock.Elapsed.TotalMilliseconds,
            totalMilliseconds = totalClock.Elapsed.TotalMilliseconds
        }));
    }
}
