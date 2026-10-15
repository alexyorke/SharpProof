using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceStatementBoundaryCoverageTests
{
    [Test]
    public void ExpressionLoweringRejectsARealIfStatementWithoutAnElse()
    {
        using var subject = TypedProgramSubject.Create("int Target(bool condition) { if (condition) return 7; return 9; }");
        Assert.That(subject.Invoke([true]), Is.EqualTo(7));
        Assert.That(subject.Invoke([false]), Is.EqualTo(9));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Single();
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(operation, Is.InstanceOf<IConditionalOperation>());
        var result = new RoslynOperationLowerer(new IrFactory()).Lower(operation);
        Assert.That(result.IsExact, Is.False);
        Assert.That(result.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        var program = subject.Lower();
        Assert.That(program.IsExact, Is.True, program.Classification.Abstention.ToString());
        var whenTrue = subject.Execute(program, [true]);
        var whenFalse = subject.Execute(program, [false]);
        Assert.That(whenTrue.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(whenFalse.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(whenTrue.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(7)));
        Assert.That(whenFalse.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(9)));
    }
}
