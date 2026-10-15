using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CompilerErrorReferenceNullComparisonApiCoverageTests
{
    private static readonly string[] ExpectedDiagnosticIds = ["CS0019"];

    [TestCase(false)]
    [TestCase(true)]
    public void ActualScalarToStringNullComparisonRetainsApiRejection(bool unequal)
    {
        var source = "private static void Target(int value) { var result = value " +
            (unequal ? "!=" : "==") + " (string)null; }";
        var tree = CSharpSyntaxTree.ParseText(FrontendTestHelpers.WrapSubjectMembers(source),
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("CompilerErrorNullComparisonApi", [tree],
            TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.That(errors.Select(static diagnostic => diagnostic.Id), Is.EqualTo(ExpectedDiagnosticIds));
        using var output = new MemoryStream();
        Assert.That(compilation.Emit(output).Success, Is.False);
        // Recovery operations are used only as expression API inputs.
        var syntax = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(syntax);
        Assert.That(operation, Is.AssignableTo<IBinaryOperation>());
        var binary = (IBinaryOperation)operation!;
        Assert.That(binary.OperatorKind, Is.EqualTo(unequal ? BinaryOperatorKind.NotEquals : BinaryOperatorKind.Equals));
        Assert.That(binary.OperatorMethod, Is.Null);
        Assert.That(binary.IsLifted, Is.False);
        Assert.That(binary.LeftOperand, Is.AssignableTo<IParameterReferenceOperation>());
        Assert.That(binary.LeftOperand.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(binary.RightOperand, Is.AssignableTo<IConversionOperation>());
        var conversion = (IConversionOperation)binary.RightOperand;
        Assert.That(conversion.IsImplicit, Is.False);
        Assert.That(conversion.Type!.SpecialType, Is.EqualTo(SpecialType.System_String));
        Assert.That(conversion.ConstantValue.HasValue, Is.True);
        Assert.That(conversion.ConstantValue.Value, Is.Null);
        Assert.That(conversion.OperatorMethod, Is.Null);
        Assert.That(conversion.Conversion.Exists, Is.True);
        Assert.That(conversion.Conversion.IsReference, Is.True);
        Assert.That(conversion.Operand, Is.AssignableTo<ILiteralOperation>());
        Assert.That(conversion.Operand.ConstantValue.HasValue, Is.True);
        Assert.That(conversion.Operand.ConstantValue.Value, Is.Null);
        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);
        var compared = lowerer.Lower(binary.LeftOperand);
        Assert.That(compared.Classification.IsExact, Is.True);
        Assert.That(compared.Term.Type, Is.EqualTo(factory.IntegerType));
        var rejected = lowerer.Lower(binary);
        Assert.That(rejected.Classification.IsExact, Is.False);
        Assert.That(rejected.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        Assert.That(rejected.Term, Is.TypeOf<IrOpaqueTerm>());

        using var control = TypedProgramSubject.Create("bool Target(object value) => value " +
            (unequal ? "!=" : "==") + " null;");
        object[] inputs = [null!];
        Assert.That(control.Invoke(inputs), Is.EqualTo(!unequal));
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
        Assert.That(replay.ReturnValue!.Boolean, Is.EqualTo(!unequal));
    }
}
