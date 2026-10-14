using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CompilerErrorUnaryPlusApiCoverageTests
{
    private static readonly string[] ExpectedDiagnosticIds = ["CS0023"];

    [Test]
    public void UnaryPlusOnBooleanPreservesRecoveryAndTypedApiAbstention()
    {
        const string source = "private static bool Target(bool value) => +value;";
        var tree = CSharpSyntaxTree.ParseText(FrontendTestHelpers.WrapSubjectMembers(source),
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("CompilerErrorUnaryPlusApi", [tree],
            TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.That(errors.Select(static diagnostic => diagnostic.Id), Is.EqualTo(ExpectedDiagnosticIds));
        using var output = new MemoryStream();
        Assert.That(compilation.Emit(output).Success, Is.False);
        // Failed compilation is inspected only by the expression API.
        var syntax = tree.GetRoot().DescendantNodes().OfType<PrefixUnaryExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(syntax);
        Assert.That(operation, Is.AssignableTo<IUnaryOperation>());
        var unary = (IUnaryOperation)operation!;
        Assert.That(unary.OperatorKind, Is.EqualTo(UnaryOperatorKind.Plus));
        Assert.That(unary.OperatorMethod, Is.Null);
        Assert.That(unary.IsLifted, Is.False);
        Assert.That(unary.Operand.Type!.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(unary.Type!.TypeKind, Is.EqualTo(TypeKind.Error));
        var lowered = new RoslynOperationLowerer(new IrFactory()).Lower(unary);
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        Assert.That(lowered.Term, Is.TypeOf<IrOpaqueTerm>());

        using var control = TypedProgramSubject.Create("int Target(int value) => +value;");
        Assert.That(control.Invoke([17]), Is.EqualTo(17));
        var program = control.Lower();
        Assert.That(program.IsExact, Is.True);
        Assert.That(program.ConstructionLimitExceeded, Is.False);
        var replay = control.Execute(program, [17]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(17)));
    }
}
