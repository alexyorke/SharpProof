using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkOwnedExpressionRouteCoverageTests
{
    [Test]
    public void RealIdentityConversionKeepsLegacyBoundIntegerAndChangingConversionCloses()
    {
        using var subject = TypedProgramSubject.Create("int Target(int value) => (int)value;");
        Assert.That(subject.Invoke([17]), Is.EqualTo(17));
        var factory = new IrFactory();
        var result = new RoslynOperationLowerer(factory).Lower(TargetExpression(subject));
        Assert.That(result.IsExact, Is.True);
        Assert.That(result.Term, Is.InstanceOf<IrVariableTerm>());
        Assert.That(result.Variables, Has.Length.EqualTo(1));
        var environment = result.Variables.ToDictionary(binding => binding.Variable, _ => factory.CreateIntegerValue(17));
        var evaluated = new IrInterpreter(factory).Evaluate(result.Term, environment);
        Assert.That(evaluated.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(evaluated.Value!.Integer, Is.EqualTo(17));
        using var changing = TypedProgramSubject.Create("int Target(long value) => checked((int)value);");
        Assert.That(changing.Invoke([17L]), Is.EqualTo(17));
        var rejected = new RoslynOperationLowerer(new IrFactory()).Lower(TargetExpression(changing));
        Assert.That(rejected.IsExact, Is.False);
        Assert.That(rejected.Classification.Abstention, Is.EqualTo(FrontendAbstention.ConversionMayChangeValue));
        ExactInteger(subject, [17], 17);
    }

    [Test]
    public void RealRefLocalReadClosesLegacyMutationWhilePlainLocalRetainsCompiledValue()
    {
        using var subject = TypedProgramSubject.Create("int Target(int value) { ref int alias = ref value; return alias; }");
        Assert.That(subject.Invoke([17]), Is.EqualTo(17));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>().Single(name => name.Identifier.ValueText == "alias");
        var operation = (ILocalReferenceOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(operation.Local.RefKind, Is.EqualTo(Microsoft.CodeAnalysis.RefKind.Ref));
        var result = new RoslynOperationLowerer(new IrFactory()).Lower(operation);
        Assert.That(result.IsExact, Is.False);
        Assert.That(result.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedMutation));
        using var supported = TypedProgramSubject.Create("int Target(int value) { int alias = value; return alias; }");
        Assert.That(supported.Invoke([17]), Is.EqualTo(17));
        ExactInteger(supported, [17], 17);
    }

    [Test]
    public void ActualCustomIndexerRetainsLegacyMemberClosureAndNaturalArgumentBindings()
    {
        using var subject = TypedProgramSubject.Create("int Target(int value) { var item = new Cell(); return item[value]; } public sealed class Cell { public int this[int value] => value + 1; }");
        Assert.That(subject.Invoke([16]), Is.EqualTo(17));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();
        var operation = (IPropertyReferenceOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(operation.Arguments, Has.Length.EqualTo(1));
        Assert.That(operation.Arguments[0].Value, Is.InstanceOf<IParameterReferenceOperation>());
        var result = new RoslynOperationLowerer(new IrFactory()).Lower(operation);
        Assert.That(result.IsExact, Is.False);
        Assert.That(result.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedMemberAccess));
        Assert.That(result.Variables.Any(binding => binding.Symbol.Name == "value"), Is.True);
        using var supported = TypedProgramSubject.Create("int Target(int value) => value + 1;");
        Assert.That(supported.Invoke([16]), Is.EqualTo(17));
        ExactInteger(supported, [16], 17);
    }

    [Test]
    public void RealOpaqueArgumentBoxingRetainsUnsupportedStructUnboxingChild()
    {
        using var subject = TypedProgramSubject.Create("object Target(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue((System.DateTime)value);");
        var input = new DateTime(2020, 1, 2);
        Assert.That(subject.Invoke([input]), Is.EqualTo(input));
        Assert.That(subject.Invoke(["word"]), Is.InstanceOf<InvalidCastException>());
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        var boxing = (IConversionOperation)invocation.Arguments.Single().Value;
        Assert.That(boxing.IsImplicit, Is.True);
        Assert.That(boxing.Parent, Is.InstanceOf<IArgumentOperation>());
        Assert.That(CSharpOperationSemantics.IsImplicitOpaqueBoxing(boxing, subject.Compilation), Is.True);
        var lowering = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        using var supported = TypedProgramSubject.Create("int Target(int value) => value;");
        Assert.That(supported.Invoke([17]), Is.EqualTo(17));
        ExactInteger(supported, [17], 17);
    }

    private static Microsoft.CodeAnalysis.IOperation TargetExpression(TypedProgramSubject subject)
    {
        var tree = subject.Compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Target");
        return subject.Compilation.GetSemanticModel(tree).GetOperation(method.ExpressionBody!.Expression)!;
    }

    private static void ExactInteger(TypedProgramSubject subject, object[] inputs, int expected)
    {
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(lowering, inputs);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }
}
