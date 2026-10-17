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
        var subject = CreateSubject(TestContext.CurrentContext.CancellationToken);
        var edge = subject.Edges.Single(branch =>
            branch is null ? throw new System.ArgumentNullException(nameof(branch)) : (Inside(branch.Source.EnclosingRegion, subject.Filter) &&
            branch.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling));
        Assert.That(edge.Destination, Is.Null);
        Assert.That(subject.Resolver.Resolve(edge), Is.EqualTo(subject.Rejected));
    }

    [Test]
    public void CompilerCatchEntryResolvesToAcceptance()
    {
        var subject = CreateSubject(TestContext.CurrentContext.CancellationToken);
        var edge = subject.Edges.First(branch =>
            branch is null ? throw new System.ArgumentNullException(nameof(branch)) : (Inside(branch.Source.EnclosingRegion, subject.Filter) &&
            branch.Destination is { } destination && Inside(destination.EnclosingRegion, subject.Catch)));
        Assert.That(edge.Semantics, Is.EqualTo(ControlFlowBranchSemantics.Regular));
        Assert.That(edge.FinallyRegions, Is.Empty);
        Assert.That(subject.Resolver.Resolve(edge), Is.EqualTo(subject.Accepted));
    }

    [Test]
    public void CompilerFilterInteriorResolvesToItsBlock()
    {
        var subject = CreateSubject(TestContext.CurrentContext.CancellationToken);
        var edge = subject.Edges.First(branch =>
            branch is null ? throw new System.ArgumentNullException(nameof(branch)) : (Inside(branch.Source.EnclosingRegion, subject.Filter) &&
            branch.Destination is { } destination && Inside(destination.EnclosingRegion, subject.Filter)));
        Assert.That(edge.Semantics, Is.EqualTo(ControlFlowBranchSemantics.Regular));
        Assert.That(edge.FinallyRegions, Is.Empty);
        Assert.That(subject.Resolver.Resolve(edge), Is.EqualTo(subject.Blocks[edge.Destination!]));
    }

    [Test]
    public void NullEdgeRefusesControlFlow()
    {
        var subject = CreateSubject(TestContext.CurrentContext.CancellationToken);
        AssertIncomplete(subject, null);
    }

    [Test]
    public void CompilerEntryOutsideTheFilterRefusesControlFlow()
    {
        var subject = CreateSubject(TestContext.CurrentContext.CancellationToken);
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
        var subject = CreateSubject(TestContext.CurrentContext.CancellationToken);
        var edge = subject.Edges.Single(branch => branch is null ? throw new System.ArgumentNullException(nameof(branch)) : (branch.Semantics == ControlFlowBranchSemantics.Return));
        Assert.That(edge.Destination, Is.Not.Null);
        AssertIncomplete(subject, edge);
    }

    [Test]
    public void CompilerThrowEdgeRefusesControlFlow()
    {
        var subject = CreateSubject(TestContext.CurrentContext.CancellationToken);
        var edge = subject.Edges.Single(branch => branch is null ? throw new System.ArgumentNullException(nameof(branch)) : (branch.Semantics == ControlFlowBranchSemantics.Throw));
        Assert.That(edge.Destination, Is.Null);
        AssertIncomplete(subject, edge);
    }

    [Test]
    public void CompilerFinallyCrossingEdgeRefusesControlFlow()
    {
        var subject = CreateSubject(TestContext.CurrentContext.CancellationToken);
        var edge = subject.Edges.First(branch =>
            branch is null ? throw new System.ArgumentNullException(nameof(branch)) : (branch.Semantics == ControlFlowBranchSemantics.Regular && !branch.FinallyRegions.IsEmpty));
        Assert.That(edge.Destination, Is.Not.Null);
        Assert.That(edge.FinallyRegions.All(region => region is null ? throw new System.ArgumentNullException(nameof(region)) : (region.Kind == ControlFlowRegionKind.Finally)), Is.True);
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
        System.ArgumentNullException.ThrowIfNull(subject);
        var exception = Assert.Throws<RoslynTotalProgramLowerer.RegionIncompleteException>(
            new Action(() => subject.Resolver.Resolve(edge)))!;
        Assert.That(exception.Reason, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
    }

    private static Subject CreateSubject(CancellationToken cancellationToken)
    {
        var tree = CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.CSharp12),
            path: "filter-edge-resolver.cs", cancellationToken: TestContext.CurrentContext.CancellationToken);
        var compilation = CSharpCompilation.Create("FilterEdgeResolver", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics(TestContext.CurrentContext.CancellationToken).Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var syntax = tree.GetRoot(TestContext.CurrentContext.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var model = compilation.GetSemanticModel(tree);
        var target = model.GetDeclaredSymbol(syntax, TestContext.CurrentContext.CancellationToken)!;
        var graph = ControlFlowGraph.Create(syntax, model, TestContext.CurrentContext.CancellationToken)!;
        var filter = Regions(graph.Root).Single(region => region is null ? throw new System.ArgumentNullException(nameof(region)) : (region.Kind == ControlFlowRegionKind.Filter));
        var caught = filter.EnclosingRegion!.NestedRegions.Single(region => region is null ? throw new System.ArgumentNullException(nameof(region)) : (region.Kind == ControlFlowRegionKind.Catch));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        var lowerer = new RoslynTotalProgramLowerer(context, cancellationToken);
        var builder = new IrProgramBuilder(factory);
        var blocks = graph.Blocks.ToDictionary(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block), block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (builder.CreateBlock("cfg:" + block.Ordinal)));
        var accepted = builder.CreateBlock("accepted");
        var rejected = builder.CreateBlock("rejected");
        var resolver = new RoslynTotalProgramLowerer.RegionFilterEdgeResolver(lowerer, filter, caught, accepted, rejected, blocks);
        return new(graph, filter, caught, blocks, accepted, rejected, resolver);
    }

    private static IEnumerable<ControlFlowRegion> Regions(ControlFlowRegion root)
    {
        System.ArgumentNullException.ThrowIfNull(root);
        yield return root;
        foreach (var child in root.NestedRegions)
        {
            foreach (var region in Regions(child))
            { yield return region; }
        }
    }

    private static bool Inside(ControlFlowRegion candidate, ControlFlowRegion expected)
    {
        System.ArgumentNullException.ThrowIfNull(candidate);
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
        public ControlFlowGraph Graph { get; init; } = Graph ?? throw new System.ArgumentNullException(nameof(Graph));
        public ControlFlowRegion Filter { get; init; } = Filter ?? throw new System.ArgumentNullException(nameof(Filter));
        public ControlFlowRegion Catch { get; init; } = Catch ?? throw new System.ArgumentNullException(nameof(Catch));
        public Dictionary<BasicBlock, IrBlockId> Blocks { get; init; } = Blocks ?? throw new System.ArgumentNullException(nameof(Blocks));
        public IrBlockId Accepted { get; init; } = Accepted.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(Accepted)) : Accepted;
        public IrBlockId Rejected { get; init; } = Rejected.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(Rejected)) : Rejected;
        public RoslynTotalProgramLowerer.RegionFilterEdgeResolver Resolver { get; init; } = Resolver.Equals(default(RoslynTotalProgramLowerer.RegionFilterEdgeResolver)) ? throw new System.ArgumentException("Value must be initialized.", nameof(Resolver)) : Resolver;

        internal IEnumerable<ControlFlowBranch> Edges => Graph.Blocks
            .SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (new[] { block.FallThroughSuccessor, block.ConditionalSuccessor }))
            .OfType<ControlFlowBranch>();
    }
}
