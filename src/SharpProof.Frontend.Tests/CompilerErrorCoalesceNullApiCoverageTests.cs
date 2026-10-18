using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CompilerErrorCoalesceNullApiCoverageTests
{
    private static readonly string[] ExpectedDiagnosticIds = ["CS0019"];

    [Test]
    public void ActualInvalidCoalesceNullTestRejectsItsScalarCapture()
    {
        const string source = "private static void Target(int value) { var result = value ?? 1; }";
        var tree = CSharpSyntaxTree.ParseText(FrontendTestHelpers.WrapSubjectMembers(source),
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("CompilerErrorCoalesceNullApi", [tree],
            TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.That(errors.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)), Is.EqualTo(ExpectedDiagnosticIds));
        using var output = new MemoryStream();
        Assert.That(compilation.Emit(output).Success, Is.False);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        Assert.That(model.GetOperation(syntax), Is.AssignableTo<ICoalesceOperation>());
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        // The genuine recovery CFG supplies expression API inputs only, never an executable proof context.
        var graph = ControlFlowGraph.Create(method, model)!;
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
        var operations = graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations
            .Concat(block.BranchValue == null ? Array.Empty<IOperation>() : [block.BranchValue]))).ToArray();
        var nullTest = operations.OfType<IIsNullOperation>().Single();
        Assert.That(nullTest.Type!.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(nullTest.Operand, Is.AssignableTo<IFlowCaptureReferenceOperation>());
        var reference = (IFlowCaptureReferenceOperation)nullTest.Operand;
        Assert.That(reference.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        var capture = operations.OfType<IFlowCaptureOperation>().Single(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Id.Equals(reference.Id)));
        Assert.That(capture.Value, Is.AssignableTo<IParameterReferenceOperation>());
        Assert.That(capture.Value.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);
        Assert.That(lowerer.Lower(capture).Classification.IsExact, Is.True);
        var operand = lowerer.Lower(reference);
        Assert.That(operand.Classification.IsExact, Is.True);
        Assert.That(operand.Term.Type, Is.EqualTo(factory.IntegerType));
        var rejected = lowerer.Lower(nullTest);
        Assert.That(rejected.Classification.IsExact, Is.False);
        Assert.That(rejected.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        Assert.That(rejected.Term, Is.TypeOf<IrOpaqueTerm>());

        using var control = TypedProgramSubject.Create("string Target(string text) => text ?? \"fallback\";");
        object[] inputs = [null!];
        Assert.That(control.Invoke(inputs), Is.EqualTo("fallback"));
        var program = control.Lower();
        Assert.That(program.IsExact, Is.True);
        Assert.That(program.ConstructionLimitExceeded, Is.False);
        var parameter = control.Context.Parameters.Single().Entry;
        var inputType = control.Factory.GetVariableInfo(parameter).Type;
        var environment = new Dictionary<IrVarId, IrValue>
        {
            [parameter] = control.Factory.CreateNullValue(inputType)
        };
        var replay = new IrProgramInterpreter(control.Factory).Execute(program.Program, environment);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.String, Is.EqualTo("fallback"));
    }
}
