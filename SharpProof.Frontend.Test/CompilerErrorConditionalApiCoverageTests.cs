using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CompilerErrorConditionalApiCoverageTests
{
    private static readonly string[] ExpectedDiagnosticIds = ["CS0173"];

    [Test]
    public void IncompatibleConditionalArmsRetainCompilerRecoveryAndApiAbstention()
    {
        const string source = "private static void Target(bool condition) { var result = condition ? true : 1; }";
        var tree = CSharpSyntaxTree.ParseText(FrontendTestHelpers.WrapSubjectMembers(source),
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("CompilerErrorConditionalApi", [tree],
            TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.That(errors.Select(static diagnostic => diagnostic.Id), Is.EqualTo(ExpectedDiagnosticIds));
        using var output = new MemoryStream();
        Assert.That(compilation.Emit(output).Success, Is.False);
        // Recovery operations are inspected only through the expression API.
        var syntax = tree.GetRoot().DescendantNodes().OfType<ConditionalExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(syntax);
        Assert.That(operation, Is.AssignableTo<IConditionalOperation>());
        var conditional = (IConditionalOperation)operation!;
        Assert.That(conditional.Condition.Type!.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(conditional.WhenTrue, Is.AssignableTo<ILiteralOperation>());
        Assert.That(conditional.WhenTrue.Type!.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(conditional.WhenFalse, Is.AssignableTo<ILiteralOperation>());
        Assert.That(conditional.WhenFalse!.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(conditional.Type!.TypeKind, Is.EqualTo(TypeKind.Error));
        var lowerer = new RoslynOperationLowerer(new IrFactory());
        Assert.That(lowerer.Lower(conditional.Condition).Classification.IsExact, Is.True);
        Assert.That(lowerer.Lower(conditional.WhenTrue).Classification.IsExact, Is.True);
        Assert.That(lowerer.Lower(conditional.WhenFalse!).Classification.IsExact, Is.True);
        var lowered = lowerer.Lower(conditional);
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        Assert.That(lowered.Term, Is.TypeOf<IrOpaqueTerm>());

        using var control = TypedProgramSubject.Create("int Target(bool condition) => condition ? 1 : 2;");
        Assert.That(control.Invoke([true]), Is.EqualTo(1));
        var program = control.Lower();
        Assert.That(program.IsExact, Is.True);
        Assert.That(program.ConstructionLimitExceeded, Is.False);
        var replay = control.Execute(program, [true]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(1)));
    }
}
