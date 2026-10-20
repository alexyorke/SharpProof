using System.Collections.Immutable;
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
    private static readonly ImmutableArray<string> ExpectedDiagnosticIds = ["CS0173"];

    [Test]
    public void IncompatibleConditionalArmsRetainCompilerRecoveryAndApiAbstention()
    {
        const string source = "private static void Target(bool condition) { var result = condition ? true : 1; }";
        var tree = CSharpSyntaxTree.ParseText(FrontendTestHelpers.WrapSubjectMembers(source),
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("CompilerErrorConditionalApi", [tree],
            TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.That(errors.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)), Is.EqualTo(ExpectedDiagnosticIds));
        using var output = new MemoryStream();
        Assert.That(compilation.Emit(output).Success, Is.False);
        // Recovery operations are inspected only through the expression API.
        var syntax = tree.GetRoot().DescendantNodes().OfType<ConditionalExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(syntax);
        Assert.That(operation, Is.AssignableTo<IConditionalOperation>());
        var conditional = (IConditionalOperation)operation!;
        var projectionLowerer = new RoslynOperationLowerer(new IrFactory());
        ProjectRecovery("conditional", conditional, projectionLowerer);
        ProjectRecovery("condition", conditional.Condition, projectionLowerer);
        ProjectRecovery("true-arm", conditional.WhenTrue, projectionLowerer);
        ProjectRecovery("false-arm", conditional.WhenFalse!, projectionLowerer);
        Assert.That(conditional.Condition.Type!.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(conditional.WhenTrue, Is.AssignableTo<IConversionOperation>());
        Assert.That(conditional.WhenFalse, Is.AssignableTo<IConversionOperation>());
        var trueArm = (IConversionOperation)conditional.WhenTrue;
        var falseArm = (IConversionOperation)conditional.WhenFalse!;
        foreach (var arm in ImmutableArray.Create(trueArm, falseArm))
        {
            Assert.That(arm.IsImplicit, Is.True);
            Assert.That(arm.Type!.TypeKind, Is.EqualTo(TypeKind.Error));
            Assert.That(arm.Type.SpecialType, Is.EqualTo(SpecialType.None));
            Assert.That(arm.ConstantValue.HasValue, Is.False);
            Assert.That(arm.OperatorMethod, Is.Null);
            Assert.That(arm.Conversion.Exists, Is.False);
            Assert.That(arm.Conversion.IsIdentity, Is.False);
            Assert.That(arm.Conversion.IsNumeric, Is.False);
            Assert.That(arm.Conversion.IsReference, Is.False);
            Assert.That(arm.Conversion.IsUserDefined, Is.False);
            Assert.That(arm.Operand, Is.AssignableTo<ILiteralOperation>());
            Assert.That(arm.Operand.IsImplicit, Is.False);
        }
        Assert.That(trueArm.Operand.Type!.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(trueArm.Operand.ConstantValue.HasValue, Is.True);
        Assert.That(trueArm.Operand.ConstantValue.Value, Is.EqualTo(true));
        Assert.That(falseArm.Operand.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(falseArm.Operand.ConstantValue.HasValue, Is.True);
        Assert.That(falseArm.Operand.ConstantValue.Value, Is.EqualTo(1));
        Assert.That(conditional.Type!.TypeKind, Is.EqualTo(TypeKind.Error));
        var lowerer = new RoslynOperationLowerer(new IrFactory());
        Assert.That(lowerer.Lower(conditional.Condition).Classification.IsExact, Is.True);
        Assert.That(lowerer.Lower(trueArm.Operand).Classification.IsExact, Is.True);
        Assert.That(lowerer.Lower(falseArm.Operand).Classification.IsExact, Is.True);
        foreach (var arm in ImmutableArray.Create(trueArm, falseArm))
        {
            var recovery = lowerer.Lower(arm);
            Assert.That(recovery.Classification.IsExact, Is.False);
            Assert.That(recovery.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
            Assert.That(recovery.Term, Is.TypeOf<IrOpaqueTerm>());
        }
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

    private static void ProjectRecovery(string label, IOperation operation, RoslynOperationLowerer lowerer)
    {
        System.ArgumentNullException.ThrowIfNull(label);
        System.ArgumentNullException.ThrowIfNull(lowerer);
        System.ArgumentNullException.ThrowIfNull(operation);
        var lowered = lowerer.Lower(operation);
        TestContext.WriteLine($"{label}: kind={operation.Kind}, implicit={operation.IsImplicit}, " +
            $"type={operation.Type?.TypeKind}/{operation.Type?.SpecialType}, " +
            $"constant={operation.ConstantValue.HasValue}/{(operation.ConstantValue.HasValue ? operation.ConstantValue.Value : null)}, " +
            $"exact={lowered.Classification.IsExact}, reason={lowered.Classification.Abstention}, term={lowered.Term.GetType().Name}");
        if (operation is IConversionOperation conversion)
        {
            TestContext.WriteLine($"{label}: conversion exists={conversion.Conversion.Exists}, " +
                $"identity={conversion.Conversion.IsIdentity}, numeric={conversion.Conversion.IsNumeric}, " +
                $"reference={conversion.Conversion.IsReference}, userDefined={conversion.Conversion.IsUserDefined}, " +
                $"operator={conversion.OperatorMethod?.Name}");
            ProjectRecovery(label + "-operand", conversion.Operand, lowerer);
        }
    }
}
