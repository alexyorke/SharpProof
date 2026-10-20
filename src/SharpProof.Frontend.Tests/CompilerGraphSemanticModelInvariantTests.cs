using System.Collections.Immutable;
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
public sealed class CompilerGraphSemanticModelInvariantTests
{
    [TestCase("method")]
    [TestCase("local")]
    [TestCase("nested-local")]
    [TestCase("lambda")]
    public void CSharpOriginalBodiesRetainTheirCompilation(string shape)
    {
        System.ArgumentNullException.ThrowIfNull(shape);
        var members = shape switch
        {
            "local" => "int Target(int value) { static int Local(int item) => item + 7; return Local(value); }",
            "nested-local" => "int Target(int value) { static int Local(int item) { static int Nested(int input) => input + 7; return Nested(item); } return Local(value); }",
            "lambda" => "int Target(int value) { System.Func<int, int> apply = static item => item + 7; return apply(value); }",
            _ => "int Target(int value) => value + 7;"
        };
        using var subject = TypedProgramSubject.Create(members);
        var graph = subject.Graph;
        var target = subject.Context.Target;
        AssertOriginalCompilation(graph, subject.Compilation);
        if (shape is "local" or "nested-local")
        {
            target = graph.LocalFunctions.Single();
            graph = graph.GetLocalFunctionControlFlowGraph(target);
            AssertOriginalCompilation(graph, subject.Compilation);
            if (shape == "nested-local")
            {
                target = graph.LocalFunctions.Single();
                graph = graph.GetLocalFunctionControlFlowGraph(target);
                AssertOriginalCompilation(graph, subject.Compilation);
            }
        }
        else if (shape == "lambda")
        {
            var lambda = FlowLambda(graph);
            Assert.That(lambda.SemanticModel, Is.Null);
            graph = graph.GetAnonymousFunctionControlFlowGraph(lambda);
            AssertOriginalCompilation(graph, subject.Compilation);
            Assert.That(graph.OriginalOperation, Is.InstanceOf<IAnonymousFunctionOperation>());
        }
        // The public wrapper provides a normal CLR entry point for every body,
        // including local functions and anonymous functions.
        foreach (var input in ImmutableArray.Create(-17, 0, 23))
        {
            Assert.That(subject.Invoke([input]), Is.EqualTo(input + 7));
        }
        if (shape != "lambda")
        {
            AssertExactPreparedReplay(graph, target, 23, 30);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void VisualBasicOriginalBodiesRetainTheirCompilation(bool anonymous)
    {
        var body = anonymous
            ? "Dim apply As System.Func(Of Integer, Integer) = Function(item As Integer) item + 7\nReturn apply(value)"
            : "Return value + 7";
        var source = "Public Class Subject\nPublic Shared Function Target(value As Integer) As Integer\n" + body +
            "\nEnd Function\nEnd Class";
        var tree = VisualBasicSyntaxTree.ParseText(source, path: "graph-model.vb");
        var compilation = VisualBasicCompilation.Create("GraphModelVisualBasic", [tree], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>().Single();
        var graph = ControlFlowGraph.Create(syntax, model)!;
        AssertOriginalCompilation(graph, compilation);
        if (anonymous)
        {
            var lambda = FlowLambda(graph);
            Assert.That(lambda.SemanticModel, Is.Null);
            graph = graph.GetAnonymousFunctionControlFlowGraph(lambda);
            AssertOriginalCompilation(graph, compilation);
            Assert.That(graph.OriginalOperation, Is.InstanceOf<IAnonymousFunctionOperation>());
        }
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var runtime = new AssemblyLoadContext("GraphModelVisualBasic", isCollectible: true);
        try
        {
            image.Position = 0;
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            foreach (var input in ImmutableArray.Create(-17, 0, 23))
            {
                Assert.That(method.Invoke(null, [input]), Is.EqualTo(input + 7));
            }
        }
        finally
        {
            runtime.Unload();
        }
        if (!anonymous)
        {
            var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
            AssertExactPreparedReplay(graph, target, 23, 30);
        }
    }

    private static IFlowAnonymousFunctionOperation FlowLambda(ControlFlowGraph graph)
    {
        System.ArgumentNullException.ThrowIfNull(graph);
        return graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations))
            .SelectMany(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.DescendantsAndSelf())).OfType<IFlowAnonymousFunctionOperation>().Single();
    }

    private static void AssertOriginalCompilation(ControlFlowGraph graph, Compilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(graph);
        Assert.That(graph.OriginalOperation.SemanticModel, Is.Not.Null);
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
    }

    private static void AssertExactPreparedReplay(ControlFlowGraph graph, IMethodSymbol target, int input, int expected)
    {
        System.ArgumentNullException.ThrowIfNull(target);
        System.ArgumentNullException.ThrowIfNull(graph);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context, static _ => true, TestContext.CurrentContext.CancellationToken);
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var binding = context.Parameters.Single();
        var replay = new IrProgramInterpreter(factory).Execute(lowering.Program,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(binding.Entry, factory.CreateIntegerValue(factory.GetVariableInfo(binding.Entry).Type, input)));
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(expected)));
    }
}
