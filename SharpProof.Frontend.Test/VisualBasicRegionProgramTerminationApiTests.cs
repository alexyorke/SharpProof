using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class VisualBasicRegionProgramTerminationApiTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void TerminationInsideFinallyRegionIsRejectedAndFiniteReturnIsExact(bool terminate)
    {
        var statement = terminate ? "End" : "Return x + 1";
        var source = "Public Class Subject\nPublic Shared Function Target(x As Integer) As Integer\nTry\n" +
            statement + "\nFinally\nx += 1\nEnd Try\nEnd Function\nPublic Shared Sub Main()\nEnd Sub\nEnd Class";
        var tree = VisualBasicSyntaxTree.ParseText(source, path: "region-termination.vb");
        var compilation = VisualBasicCompilation.Create("RegionTermination", [tree], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.ConsoleApplication, optionStrict: OptionStrict.On,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>()
            .Single(block => block.SubOrFunctionStatement.Identifier.ValueText == "Target");
        var graph = ControlFlowGraph.Create(declaration, model)!;
        var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(target.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(graph.OriginalOperation.Descendants().OfType<IEndOperation>().Count(), Is.EqualTo(terminate ? 1 : 0));
        var regions = EnumerateRegions(graph.Root).ToArray();
        Assert.That(regions.Count(region => region.Kind == ControlFlowRegionKind.TryAndFinally), Is.EqualTo(1));
        Assert.That(regions.Count(region => region.Kind == ControlFlowRegionKind.Try), Is.EqualTo(1));
        Assert.That(regions.Count(region => region.Kind == ControlFlowRegionKind.Finally), Is.EqualTo(1));
        var edges = graph.Blocks.Select(block => block.FallThroughSuccessor)
            .Where(edge => edge?.Semantics == (terminate
                ? ControlFlowBranchSemantics.ProgramTermination : ControlFlowBranchSemantics.Return)).ToArray();
        Assert.That(edges, Has.Length.EqualTo(1));
        var edge = edges.Single()!;
        Assert.That(edge.Source.IsReachable, Is.True);
        Assert.That(graph.Blocks[edge.Source.Ordinal], Is.SameAs(edge.Source));
        Assert.That(edge.Source.EnclosingRegion.Kind, Is.EqualTo(ControlFlowRegionKind.Try));
        if (terminate)
        {
            Assert.That(edge.Destination, Is.Null);
            Assert.That(edge.FinallyRegions, Is.Empty);
        }
        else
        {
            Assert.That(edge.Destination!.Kind, Is.EqualTo(BasicBlockKind.Exit));
            Assert.That(graph.Blocks[edge.Destination.Ordinal], Is.SameAs(edge.Destination));
            Assert.That(edge.FinallyRegions, Has.Length.EqualTo(1));
            Assert.That(edge.FinallyRegions.Single().Kind, Is.EqualTo(ControlFlowRegionKind.Finally));
        }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        Assert.That(lowered.IsExact, Is.EqualTo(!terminate));
        if (terminate)
        {
            // End stops the process. This case is compiler/API-only: neither
            // invoke the emitted method nor execute its rejected IR candidate.
            Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
            return;
        }
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
        var runtime = new AssemblyLoadContext("finite-region-return", isCollectible: true);
        try
        {
            image.Position = 0;
            Assert.That(runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!.Invoke(null, [3]), Is.EqualTo(4));
            var entry = context.Parameters.Single().Entry;
            var replay = new IrProgramInterpreter(factory).Execute(lowered.Program,
                new Dictionary<IrVarId, IrValue> { [entry] = factory.CreateIntegerValue(factory.GetVariableInfo(entry).Type, 3) });
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(4)));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ApproximationVariables, Is.Empty);
        }
        finally
        {
            runtime.Unload();
        }
    }

    private static IEnumerable<ControlFlowRegion> EnumerateRegions(ControlFlowRegion root)
    {
        yield return root;
        foreach (var child in root.NestedRegions)
        {
            foreach (var region in EnumerateRegions(child))
            {
                yield return region;
            }
        }
    }
}
