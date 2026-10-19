using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CompilerErrorBinaryApiCoverageTests
{
    private static readonly ImmutableArray<string> ExpectedDiagnosticIds = ["CS0019"];

    [TestCase("&", BinaryOperatorKind.And)]
    [TestCase("==", BinaryOperatorKind.Equals)]
    public void ActualIncompatibleScalarOperandsRetainCompilerErrorAndApiAbstention(
        string token, BinaryOperatorKind kind)
    {
        System.ArgumentNullException.ThrowIfNull(token);
        var source = "private static void Target(bool left, int right) { var result = left " + token + " right; }";
        var tree = CSharpSyntaxTree.ParseText(FrontendTestHelpers.WrapSubjectMembers(source),
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("CompilerErrorBinaryApi", [tree],
            TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.That(errors.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)), Is.EqualTo(ExpectedDiagnosticIds));
        using var output = new MemoryStream();
        Assert.That(compilation.Emit(output).Success, Is.False);
        // Compiler recovery is inspected only by the expression API.
        var syntax = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(syntax);
        Assert.That(operation, Is.AssignableTo<IBinaryOperation>());
        var binary = (IBinaryOperation)operation!;
        Assert.That(binary.OperatorKind, Is.EqualTo(kind));
        Assert.That(binary.OperatorMethod, Is.Null);
        Assert.That(binary.IsLifted, Is.False);
        Assert.That(binary.LeftOperand, Is.AssignableTo<IParameterReferenceOperation>());
        Assert.That(binary.RightOperand, Is.AssignableTo<IParameterReferenceOperation>());
        Assert.That(binary.LeftOperand.Type!.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(binary.RightOperand.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        TestContext.WriteLine($"Actual recovery binary {kind}: result kind={binary.Type?.TypeKind}; special={binary.Type?.SpecialType}");
        var lowerer = new RoslynOperationLowerer(new IrFactory(IrExecutionSemantics.Total));
        Assert.That(lowerer.Lower(binary.LeftOperand).Classification.IsExact, Is.True);
        Assert.That(lowerer.Lower(binary.RightOperand).Classification.IsExact, Is.True);
        var lowered = lowerer.Lower(binary);
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        Assert.That(lowered.Term, Is.TypeOf<IrOpaqueTerm>());

        using var control = TypedProgramSubject.Create("bool Target(bool left, bool right) => left " + token + " right;");
        ImmutableArray<object> inputs = [true, false];
        Assert.That(control.Invoke(inputs), Is.False);
        var program = control.Lower();
        Assert.That(program.IsExact, Is.True);
        Assert.That(program.ConstructionLimitExceeded, Is.False);
        var replay = control.Execute(program, inputs);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Boolean, Is.False);
    }
}
