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
    private static readonly string[] ExpectedDiagnosticIds = ["BC31003"];

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
            .Select(d => d.Id), Is.EqualTo(ExpectedDiagnosticIds));
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

    [TestCase(255)]
    [TestCase(256)]
    public void ActualRegularPrintRecoveryRespectsTheDefaultExpressionDepthBoundary(int depth)
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
            .Select(d => d.Id), Is.EqualTo(ExpectedDiagnosticIds));
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
        // Require the same small ordinary carrier before interpreting the
        // lowerer's fixed expression bound; no enlarged construction budget.
        Assert.That(graph.Blocks.Length, Is.LessThanOrEqualTo(8));
        Assert.That(graph.Blocks.Sum(block => block.Operations.Length), Is.LessThanOrEqualTo(8));
        Assert.That(graph.Blocks.All(block => block.ConditionKind == ControlFlowConditionKind.None), Is.True);
        Assert.That(graph.Blocks.All(block => block.ConditionalSuccessor == null), Is.True);
        Assert.That(graph.Blocks.SelectMany(block => block.Operations).OfType<IFlowCaptureOperation>(), Is.Empty);
        Assert.That(graph.Root.NestedRegions.All(region => region.Kind == ControlFlowRegionKind.LocalLifetime &&
            region.NestedRegions.IsEmpty), Is.True);
        Assert.That(graph.Root.NestedRegions.Length, Is.LessThanOrEqualTo(1));
        Assert.That(graph.Root.Locals.Length + graph.Root.NestedRegions.Sum(region => region.Locals.Length),
            Is.LessThanOrEqualTo(2));
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        Assert.That(lowered.IsExact, Is.EqualTo(depth == 255));
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(depth == 255
            ? FrontendAbstention.None : FrontendAbstention.ExpressionDepthLimit));
        var instructions = lowered.Program.Blocks.SelectMany(block => block.Instructions).ToArray();
        Assert.That(instructions.Length, Is.LessThanOrEqualTo(64));
        Assert.That(instructions.OfType<IrWriteInstruction>(), Is.Empty);
        var havoc = instructions.OfType<IrHavocInstruction>().ToArray();
        Assert.That(havoc, Has.Length.EqualTo(depth == 255 ? 0 : 1));
        if (depth == 256)
        {
            Assert.That(havoc.Single().Origin, Is.EqualTo(IrHavocOrigin.Approximation));
            Assert.That(havoc.Single().HavocKind, Is.EqualTo(IrHavocKind.Variables));
        }
        // Neither diagnostic-bearing recovery candidate is executed, including
        // the Exact-at-255 case. Only the valid neighbor supplies CLR/IR replay.
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
