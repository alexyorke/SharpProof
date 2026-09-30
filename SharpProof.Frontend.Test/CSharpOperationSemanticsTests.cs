using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CSharpOperationSemanticsTests
{
    [Test]
    public void FrozenDecisionsCoverEveryDistinctRoslynKind()
    {
        Assert.That(CSharpOperationSemantics.Operations.Keys, Is.EquivalentTo(Enum.GetValues<OperationKind>().Distinct()));
        Assert.That(CSharpOperationSemantics.Operations.Count, Is.EqualTo(126));
        Assert.That(CSharpOperationSemantics.Operations.ContainsKey((OperationKind)int.MaxValue), Is.False);
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
