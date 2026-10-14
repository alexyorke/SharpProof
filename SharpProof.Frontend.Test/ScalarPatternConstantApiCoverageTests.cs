using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ScalarPatternConstantApiCoverageTests
{
    private static readonly string[] ExpectedDiagnosticIds = ["CS0031"];

    [TestCase(false)]
    [TestCase(true)]
    public void ActualOutOfRangeBytePatternConstantIsRejectedWithoutAProgramContext(bool relational)
    {
        var source = "private static void Target(byte value) { var result = value is " +
            (relational ? "> 256" : "256") + "; }";
        var tree = CSharpSyntaxTree.ParseText(FrontendTestHelpers.WrapSubjectMembers(source),
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("ScalarPatternConstantApi", [tree],
            TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.That(errors.Select(static diagnostic => diagnostic.Id), Is.EqualTo(ExpectedDiagnosticIds));
        using var output = new MemoryStream();
        Assert.That(compilation.Emit(output).Success, Is.False);
        var syntax = tree.GetRoot().DescendantNodes().OfType<IsPatternExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(syntax);
        Assert.That(operation, Is.AssignableTo<IIsPatternOperation>());
        var pattern = (IIsPatternOperation)operation!;
        Assert.That(pattern.Value, Is.AssignableTo<IParameterReferenceOperation>());
        Assert.That(pattern.Value.Type!.SpecialType, Is.EqualTo(SpecialType.System_Byte));
        Assert.That(SymbolEqualityComparer.Default.Equals(pattern.Pattern.InputType, pattern.Value.Type), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(pattern.Pattern.NarrowedType, pattern.Value.Type), Is.True);
        var operand = PatternOperand(pattern.Pattern, relational);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var expectedType = new RoslynTypeMapper(factory).GetTypeId(pattern.Value.Type);
        TestContext.WriteLine($"recovery operand: kind={operand.Kind}, type={operand.Type?.TypeKind}/{operand.Type?.SpecialType}, " +
            $"constant={operand.ConstantValue.HasValue}/{(operand.ConstantValue.HasValue ? operand.ConstantValue.Value : null)}");
        // Only the actual operand/input symbols and their owned mapped type enter this scalar API.
        Assert.That(CSharpOperationSemantics.ScalarPatternConstant(factory, operand,
            pattern.Value.Type!, expectedType), Is.Null);

        using var control = TypedProgramSubject.Create("bool Target(byte value) => value is " +
            (relational ? "> 0" : "0") + ";");
        object[] inputs = [relational ? (byte)1 : (byte)0];
        Assert.That(control.Invoke(inputs), Is.EqualTo(true));
        var controlTree = control.Compilation.SyntaxTrees.Single();
        var controlSyntax = controlTree.GetRoot().DescendantNodes().OfType<IsPatternExpressionSyntax>().Single();
        var controlPattern = (IIsPatternOperation)control.Compilation.GetSemanticModel(controlTree).GetOperation(controlSyntax)!;
        var controlOperand = PatternOperand(controlPattern.Pattern, relational);
        var controlType = new RoslynTypeMapper(control.Factory).GetTypeId(controlPattern.Value.Type);
        var literal = CSharpOperationSemantics.ScalarPatternConstant(control.Factory, controlOperand,
            controlPattern.Value.Type!, controlType);
        Assert.That(literal, Is.Not.Null);
        Assert.That(literal!.Type, Is.EqualTo(controlType));
        Assert.That(new IrInterpreter(control.Factory).Evaluate(literal).Value!.IntegerNumericValue, Is.EqualTo(BigInteger.Zero));
        var program = control.Lower();
        Assert.That(program.IsExact, Is.True);
        Assert.That(program.ConstructionLimitExceeded, Is.False);
        var replay = control.Execute(program, inputs);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Boolean, Is.EqualTo(true));
    }

    private static IOperation PatternOperand(IPatternOperation pattern, bool relational)
    {
        if (relational)
        {
            Assert.That(pattern, Is.AssignableTo<IRelationalPatternOperation>());
            var bound = (IRelationalPatternOperation)pattern;
            Assert.That(bound.OperatorKind, Is.EqualTo(BinaryOperatorKind.GreaterThan));
            return bound.Value;
        }
        Assert.That(pattern, Is.AssignableTo<IConstantPatternOperation>());
        return ((IConstantPatternOperation)pattern).Value;
    }
}
