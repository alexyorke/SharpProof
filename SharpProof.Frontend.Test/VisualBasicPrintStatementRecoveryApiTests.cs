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
public sealed class VisualBasicPrintStatementRecoveryApiTests
{
    [TestCase(1)]
    [TestCase(2)]
    public void ActualRegularPrintRecoveryPreservesOwnedParenthesesWithoutFieldWrites(int depth)
    {
        var expression = new string('(', depth) + "x" + new string(')', depth);
        var source = "Public Class Subject\nPublic Shared Function Target(x As Integer) As Integer\n? " +
            expression + "\nReturn x\nEnd Function\nEnd Class";
        var tree = VisualBasicSyntaxTree.ParseText(source,
            new VisualBasicParseOptions(kind: SourceCodeKind.Regular), path: "print-recovery.vb");
        var compilation = VisualBasicCompilation.Create("PrintRecovery", [tree], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(tree.GetRoot().DescendantNodes().OfType<PrintStatementSyntax>().Count(), Is.EqualTo(1));
        Assert.That(tree.GetRoot().DescendantNodes().OfType<ParenthesizedExpressionSyntax>().Count(), Is.EqualTo(depth));
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Id), Is.EqualTo(new[] { "BC31003" }));
        using var rejectedImage = new MemoryStream();
        Assert.That(compilation.Emit(rejectedImage).Success, Is.False);

        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>().Single();
        var graph = ControlFlowGraph.Create(declaration, model)!;
        var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(target.ContainingAssembly, Is.SameAs(compilation.Assembly));
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
        var statement = graph.Blocks.SelectMany(block => block.Operations).OfType<IExpressionStatementOperation>().Single();
        IOperation operand = statement.Operation;
        var wrappers = 0;
        while (operand is IParenthesizedOperation parenthesized)
        {
            wrappers++;
            operand = parenthesized.Operand;
        }
        Assert.That(wrappers, Is.EqualTo(depth));
        Assert.That(operand, Is.InstanceOf<IParameterReferenceOperation>());
        Assert.That(SymbolEqualityComparer.Default.Equals(((IParameterReferenceOperation)operand).Parameter,
            target.Parameters.Single()), Is.True);
        Assert.That(operand.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(operand.Syntax.SyntaxTree, Is.SameAs(tree));
        Assert.That(graph.OriginalOperation.Descendants().OfType<IFieldReferenceOperation>(), Is.Empty);
        Assert.That(graph.OriginalOperation.Descendants().OfType<IIncrementOrDecrementOperation>(), Is.Empty);

        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        Assert.That(lowered.IsExact, Is.True);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
        Assert.That(lowered.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrWriteInstruction>(), Is.Empty);
        // Exact describes this lowering API's supported operation subset. The
        // diagnostic-bearing recovery source failed Emit and is never executed.
        AssertEmittedReturnControl();
    }

    private static void AssertEmittedReturnControl()
    {
        const string source = "Public Class Subject\nPublic Shared Function Target(x As Integer) As Integer\nReturn x\nEnd Function\nEnd Class";
        var tree = VisualBasicSyntaxTree.ParseText(source,
            new VisualBasicParseOptions(kind: SourceCodeKind.Regular), path: "return-control.vb");
        var compilation = VisualBasicCompilation.Create("ReturnControl", [tree], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>().Single();
        var graph = ControlFlowGraph.Create(declaration, model)!;
        var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowered.IsExact, Is.True);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        var runtime = new AssemblyLoadContext("print-recovery-return-control", isCollectible: true);
        try
        {
            image.Position = 0;
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            foreach (var input in new[] { -3, 0, 7 })
            {
                Assert.That(method.Invoke(null, [input]), Is.EqualTo(input));
                var entry = context.Parameters.Single().Entry;
                var replay = new IrProgramInterpreter(factory).Execute(lowered.Program,
                    new Dictionary<IrVarId, IrValue> { [entry] = factory.CreateIntegerValue(factory.GetVariableInfo(entry).Type, input) });
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(input)));
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.ApproximationVariables, Is.Empty);
            }
        }
        finally
        {
            runtime.Unload();
        }
    }
}
