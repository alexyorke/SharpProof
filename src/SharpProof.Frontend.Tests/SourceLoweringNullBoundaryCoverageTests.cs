using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceLoweringNullBoundaryCoverageTests
{
    [Test]
    public void PublicNullRejectionDoesNotContaminateTheFollowingCompiledExpression()
    {
        using var subject = TypedProgramSubject.Create("long Target(long value) => checked(value + 1L);");
        Assert.That(subject.Invoke([9L]), Is.EqualTo(10L));
        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);
        var error = Assert.Throws<ArgumentNullException>((Action)(() => _ = lowerer.Lower(null!)));
        Assert.That(error!.ParamName, Is.EqualTo("operation"));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var operation = FrontendTestHelpers.TryGetExpressionOperation(subject.Compilation.GetSemanticModel(tree), method.ExpressionBody!.Expression)!;
        var lowering = lowerer.Lower(operation);
        Assert.That(lowering.IsExact, Is.True);
        var inputs = lowering.Variables.ToDictionary(binding => binding.Variable, _ => factory.CreateIntegerValue(9L));
        var execution = new IrInterpreter(factory).Evaluate(lowering.Term, inputs);
        Assert.That(execution.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(execution.Value!.Integer, Is.EqualTo(10L));
    }
}
