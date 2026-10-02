using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CSharpOperationSemanticsTests
{
    [TestCase(int.MinValue)]
    [TestCase(int.MinValue + 1)]
    [TestCase(-65536)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(65536)]
    [TestCase(int.MaxValue)]
    public void Int32MathAbsRuleAgreesWithCompiledRuntime(int value)
    {
        using var subject = TypedProgramSubject.Create("int Target(int value) => System.Math.Abs(value);");
        var parameter = subject.Context.Parameters.Single().Current;
        var rule = CSharpOperationSemantics.Int32MathAbs(subject.Factory, subject.Factory.Variable(parameter));
        var interpreter = new IrInterpreter(subject.Factory);
        var environment = new Dictionary<IrVarId, IrValue> { [parameter] = subject.Value(parameter, value) };
        var runtime = subject.Invoke([value]);
        var overflow = interpreter.Evaluate(rule.Throws.Single().Condition, environment).Value!.Boolean;
        Assert.That(overflow, Is.EqualTo(runtime is OverflowException));
        Assert.That(rule.Throws.Single().Kind, Is.EqualTo(IrExceptionKind.Overflow));
        if (!overflow)
        {
            Assert.That(interpreter.Evaluate(rule.Value, environment).Value!.Integer, Is.EqualTo((int)runtime!));
        }
    }

    [Test]
    public void FrozenDecisionsCoverEveryDistinctRoslynKind()
    {
        Assert.That(CSharpOperationSemantics.Operations.Keys, Is.EquivalentTo(Enum.GetValues<OperationKind>().Distinct()));
        Assert.That(CSharpOperationSemantics.Operations.Count, Is.EqualTo(126));
        Assert.That(CSharpOperationSemantics.Operations.ContainsKey((OperationKind)int.MaxValue), Is.False);
    }

    [TestCase("int Target(int a, int b) => checked(a + b);", int.MaxValue, 1)]
    [TestCase("int Target(int a, int b) => a / b;", 1, 0)]
    [TestCase("int Target(int a, int b) => a % b;", int.MinValue, -1)]
    [TestCase("byte Target(int a, int b) => checked((byte)a);", 256, 0)]
    public void SharedThrowClassificationCoversExecutedScalarFaults(string members, int a, int b)
    {
        using var subject = TypedProgramSubject.Create(members);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        ExpressionSyntax syntax = method.ExpressionBody!.Expression;
        while (syntax is CheckedExpressionSyntax checkedExpression)
        {
            syntax = checkedExpression.Expression;
        }
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        var expression = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(operation);
        var environment = subject.Context.Parameters.ToDictionary(binding => binding.Current,
            binding => subject.Value(binding.Current, binding.Parameter.Ordinal == 0 ? a : b));
        Assert.That(subject.Invoke([a, b]), Is.InstanceOf<ArithmeticException>());
        Assert.That(expression.Classification.IsExact, Is.True);
        Assert.That(new IrInterpreter(subject.Factory).Evaluate(expression.SafeCondition, environment).Value!.Boolean, Is.False);
        Assert.That(CSharpOperationSemantics.OperationMayThrow(operation), Is.True);
    }

    [TestCase("bool Target(int a, int b) => a / b == a / b;", 1, 0, false, true)]
    [TestCase("bool Target(int a, int b) => false && a / b > 0;", 1, 0, true, false)]
    [TestCase("bool Target(int a, int b) => true || a / b > 0;", 1, 0, true, true)]
    [TestCase("bool Target(int a, int b) => (a / b > 0) ? true : true;", 1, 0, false, true)]
    [TestCase("bool Target(int a, int b) => b == 0 ? true : a / b > 0;", 1, 0, true, true)]
    public void PureGuardPreservesTakenFaultsAfterValueFolding(string members, int a, int b, bool safe, bool value)
    {
        using var subject = TypedProgramSubject.Create(members);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(method.ExpressionBody!.Expression)!;
        var expression = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(operation);
        Assert.That(expression.Classification.IsExact, Is.True);
        var environment = subject.Context.Parameters.ToDictionary(binding => binding.Current,
            binding => subject.Value(binding.Current, binding.Parameter.Ordinal == 0 ? a : b));
        var interpreter = new IrInterpreter(subject.Factory);
        Assert.That(interpreter.Evaluate(expression.SafeCondition, environment).Value!.Boolean, Is.EqualTo(safe));
        Assert.That(interpreter.Evaluate(expression.Value, environment).Value!.Boolean, Is.EqualTo(value));
        Assert.That(subject.Invoke([a, b]) is DivideByZeroException, Is.EqualTo(!safe));
    }
}
