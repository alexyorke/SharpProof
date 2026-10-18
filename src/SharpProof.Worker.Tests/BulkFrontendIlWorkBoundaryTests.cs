using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class BulkFrontendIlWorkBoundaryTests
{
    [TestCase(8, true)]
    [TestCase(256, false)]
    public void RealEmittedSumRespectsTheDefaultConstructionWork(int termCount, bool exact)
    {
        var library = "public static class Library { public static int Target(int value) => " +
            string.Join(" + ", Enumerable.Repeat("value", termCount)) + "; }";
        const string source = "public static class Subject { public static int Target(int value) => Library.Target(value); }";
        using var subject = new MetadataTestSubject(library, source);
        Assert.That(subject.Method.Invoke(null, [1]), Is.EqualTo(termCount));
        Assert.That(subject.RootMethod.Invoke(null, [1]), Is.EqualTo(termCount));

        var references = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, TestContext.CurrentContext.CancellationToken);
        var method = subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target")
            .OfType<IMethodSymbol>().Single();
        var census = new CompilerTotalIlBodyProvider(subject.Compilation, references);
        var session = new TotalSourceCallSession(subject.Compilation, null, census.Resolve, TestContext.CurrentContext.CancellationToken);
        var body = session.PrepareIl(method);
        Assert.That(body, Is.Not.Null, "The actual SDK-emitted sum must decode.");
        var validationWork = 0;
        Assert.That(TotalIlStack.TryValidate(body!, amount =>
        {
            validationWork += amount;
            return session.Spend(amount);
        }, out var shapes), Is.True, "Stack validation must fit the unchanged default work limit.");
        Assert.That(session.ConstructionLimitExceeded, Is.False);
        TestContext.WriteLine($"terms={termCount}; emittedInstructions={body!.Instructions.Length}; " +
            $"validationWork={validationWork}; shapeAllocationWork={shapes.Sum(shape => shape.HasValue ? shape.Value.Length + 1 : throw new ArgumentException("A validated stack shape is required.", nameof(shape)))}");

        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var target = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var graph = ControlFlowGraph.Create(declaration, model)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        Assert.That(context.HasScalarSignature, Is.True);
        var provider = new CompilerTotalIlBodyProvider(subject.Compilation, references);
        var lowered = TotalBodyLowering.Lower(subject.Compilation, graph, context, TestContext.CurrentContext.CancellationToken,
            resolveIl: provider.Resolve);
        Assert.That(lowered.IsExact, Is.EqualTo(exact));
        Assert.That(lowered.ConstructionLimitExceeded, Is.EqualTo(!exact));
        if (!exact)
        {
            Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
            return;
        }

        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
        var entry = context.Parameters.Single().Entry;
        var replay = new IrProgramInterpreter(factory).Execute(lowered.Program,
            new Dictionary<IrVarId, IrValue> { [entry] = factory.CreateIntegerValue(factory.GetVariableInfo(entry).Type, 1) });
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(8)));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ApproximationVariables, Is.Empty);
    }
}
