using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class VisualBasicProgramTerminationApiTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void ProgramTerminationIsRejectedAndFiniteReturnRemainsExact(bool terminate)
    {
        var statement = terminate ? "End" : "Return";
        var source = "Public Class Subject\nPublic Shared Sub Target()\n" + statement + "\nEnd Sub\nPublic Shared Sub Main()\nEnd Sub\nEnd Class";
        var tree = VisualBasicSyntaxTree.ParseText(source, path: "termination-" + statement + ".vb");
        var compilation = VisualBasicCompilation.Create("Termination_" + statement, [tree], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.ConsoleApplication, optionStrict: OptionStrict.On,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>()
            .Single(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.SubOrFunctionStatement.Identifier.ValueText == "Target"));
        var graph = ControlFlowGraph.Create(declaration, model)!;
        var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(target.ReturnsVoid, Is.True);
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(graph.OriginalOperation.Descendants().OfType<IEndOperation>().Count(), Is.EqualTo(terminate ? 1 : 0));
        var terminationEdges = graph.Blocks.Select(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.FallThroughSuccessor))
            .Where(edge => edge?.Semantics == ControlFlowBranchSemantics.ProgramTermination).ToImmutableArray();
        Assert.That(terminationEdges.Length, Is.EqualTo(terminate ? 1 : 0));
        foreach (var edge in terminationEdges)
        {
            Assert.That(edge!.Destination, Is.Null);
            Assert.That(graph.Blocks[edge.Source.Ordinal], Is.SameAs(edge.Source));
            Assert.That(edge.Source.IsReachable, Is.True);
            Assert.That(edge.FinallyRegions, Is.Empty);
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
            // End terminates the process. This genuine emitted method is API-only:
            // never invoke its CLR body or execute the rejected IR candidate.
            Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
            return;
        }
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("finite-return", isCollectible: true);
        try
        {
            image.Position = 0;
            Assert.That(runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!.Invoke(null, null), Is.Null);
            var replay = new IrProgramInterpreter(factory).Execute(lowered.Program);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue, Is.Null);
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ApproximationVariables, Is.Empty);
        }
        finally
        {
            runtime.Unload();
        }
    }
}
