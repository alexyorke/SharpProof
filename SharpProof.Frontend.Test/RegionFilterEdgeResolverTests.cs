using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class RegionFilterEdgeResolverTests
{
    private const string Source = """
        public static class Subject
        {
            private static void Work() { }
            private static bool Left() => true;
            private static bool Right() => false;
            public static int Target(bool choose)
            {
                try
                {
                    try { Work(); }
                    finally { Work(); }
                    throw new System.Exception();
                }
                catch (System.Exception) when (choose ? Left() : Right())
                {
                    Work();
                }
                return 7;
            }
        }
        """;

    [Test]
    public void CompilerFilterDispatchResolvesToRejection()
    {
        var subject = CreateSubject();
        var edge = subject.Edges.Single(branch =>
            Inside(branch.Source.EnclosingRegion, subject.Filter) &&
            branch.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling);
        Assert.That(edge.Destination, Is.Null);
        Assert.That(subject.Resolver.Resolve(edge), Is.EqualTo(subject.Rejected));
    }

    [Test]
    public void CompilerCatchEntryResolvesToAcceptance()
    {
        var subject = CreateSubject();
        var edge = subject.Edges.First(branch =>
            Inside(branch.Source.EnclosingRegion, subject.Filter) &&
            branch.Destination is { } destination && Inside(destination.EnclosingRegion, subject.Catch));
        Assert.That(edge.Semantics, Is.EqualTo(ControlFlowBranchSemantics.Regular));
        Assert.That(edge.FinallyRegions, Is.Empty);
        Assert.That(subject.Resolver.Resolve(edge), Is.EqualTo(subject.Accepted));
    }

    [Test]
    public void CompilerFilterInteriorResolvesToItsBlock()
    {
        var subject = CreateSubject();
        var edge = subject.Edges.First(branch =>
            Inside(branch.Source.EnclosingRegion, subject.Filter) &&
            branch.Destination is { } destination && Inside(destination.EnclosingRegion, subject.Filter));
        Assert.That(edge.Semantics, Is.EqualTo(ControlFlowBranchSemantics.Regular));
        Assert.That(edge.FinallyRegions, Is.Empty);
        Assert.That(subject.Resolver.Resolve(edge), Is.EqualTo(subject.Blocks[edge.Destination!]));
    }

    [Test]
    public void NullEdgeRefusesControlFlow()
    {
        var subject = CreateSubject();
        AssertIncomplete(subject, null);
    }

    [Test]
    public void CompilerEntryOutsideTheFilterRefusesControlFlow()
    {
        var subject = CreateSubject();
        var edge = subject.Graph.Blocks[0].FallThroughSuccessor!;
        Assert.That(edge.Semantics, Is.EqualTo(ControlFlowBranchSemantics.Regular));
        Assert.That(edge.Destination, Is.Not.Null);
        Assert.That(edge.FinallyRegions, Is.Empty);
        Assert.That(Inside(edge.Destination!.EnclosingRegion, subject.Filter), Is.False);
        Assert.That(Inside(edge.Destination.EnclosingRegion, subject.Catch), Is.False);
        AssertIncomplete(subject, edge);
    }

    [Test]
    public void CompilerReturnEdgeRefusesControlFlow()
    {
        var subject = CreateSubject();
        var edge = subject.Edges.Single(branch => branch.Semantics == ControlFlowBranchSemantics.Return);
        Assert.That(edge.Destination, Is.Not.Null);
        AssertIncomplete(subject, edge);
    }

    [Test]
    public void CompilerThrowEdgeRefusesControlFlow()
    {
        var subject = CreateSubject();
        var edge = subject.Edges.Single(branch => branch.Semantics == ControlFlowBranchSemantics.Throw);
        Assert.That(edge.Destination, Is.Null);
        AssertIncomplete(subject, edge);
    }

    [Test]
    public void CompilerFinallyCrossingEdgeRefusesControlFlow()
    {
        var subject = CreateSubject();
        var edge = subject.Edges.First(branch =>
            branch.Semantics == ControlFlowBranchSemantics.Regular && !branch.FinallyRegions.IsEmpty);
        Assert.That(edge.Destination, Is.Not.Null);
        Assert.That(edge.FinallyRegions.All(region => region.Kind == ControlFlowRegionKind.Finally), Is.True);
        AssertIncomplete(subject, edge);
    }

    [Test]
    public void CancellationPrecedesNullEdgeValidation()
    {
        var token = new CancellationToken(canceled: true);
        var subject = CreateSubject(token);
        var exception = Assert.Throws<OperationCanceledException>(new Action(() => subject.Resolver.Resolve(null)))!;
        Assert.That(exception.CancellationToken, Is.EqualTo(token));
    }

    private static void AssertIncomplete(Subject subject, ControlFlowBranch? edge)
    {
        var exception = Assert.Throws<RoslynTotalProgramLowerer.RegionIncompleteException>(
            new Action(() => subject.Resolver.Resolve(edge)))!;
        Assert.That(exception.Reason, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
    }

    private static Subject CreateSubject(CancellationToken cancellationToken = default)
    {
        var tree = CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.CSharp12),
            path: "filter-edge-resolver.cs", cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("FilterEdgeResolver", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics(CancellationToken.None).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: CancellationToken.None);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var syntax = tree.GetRoot(CancellationToken.None).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Target");
        var model = compilation.GetSemanticModel(tree);
        var target = model.GetDeclaredSymbol(syntax, CancellationToken.None)!;
        var graph = ControlFlowGraph.Create(syntax, model, CancellationToken.None)!;
        var filter = Regions(graph.Root).Single(region => region.Kind == ControlFlowRegionKind.Filter);
        var caught = filter.EnclosingRegion!.NestedRegions.Single(region => region.Kind == ControlFlowRegionKind.Catch);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        var lowerer = new RoslynTotalProgramLowerer(context, cancellationToken);
        var builder = new IrProgramBuilder(factory);
        var blocks = graph.Blocks.ToDictionary(block => block, block => builder.CreateBlock("cfg:" + block.Ordinal));
        var accepted = builder.CreateBlock("accepted");
        var rejected = builder.CreateBlock("rejected");
        var resolver = new RoslynTotalProgramLowerer.RegionFilterEdgeResolver(lowerer, filter, caught, accepted, rejected, blocks);
        return new(graph, filter, caught, blocks, accepted, rejected, resolver);
    }

    private static IEnumerable<ControlFlowRegion> Regions(ControlFlowRegion root)
    {
        yield return root;
        foreach (var child in root.NestedRegions)
        {
            foreach (var region in Regions(child))
            { yield return region; }
        }
    }

    private static bool Inside(ControlFlowRegion candidate, ControlFlowRegion expected)
    {
        for (var region = candidate; region != null; region = region.EnclosingRegion)
        {
            if (region == expected)
            { return true; }
        }
        return false;
    }

    private sealed record Subject(ControlFlowGraph Graph, ControlFlowRegion Filter, ControlFlowRegion Catch,
        Dictionary<BasicBlock, IrBlockId> Blocks, IrBlockId Accepted, IrBlockId Rejected,
        RoslynTotalProgramLowerer.RegionFilterEdgeResolver Resolver)
    {
        internal IEnumerable<ControlFlowBranch> Edges => Graph.Blocks
            .SelectMany(block => new[] { block.FallThroughSuccessor, block.ConditionalSuccessor })
            .OfType<ControlFlowBranch>();
    }
}
