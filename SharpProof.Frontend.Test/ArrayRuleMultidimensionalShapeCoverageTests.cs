using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ArrayRuleMultidimensionalShapeCoverageTests
{
    [Test]
    public void WholeScalarRuleRejectsActualMultidimensionalReadChildren()
    {
        using var subject = TypedProgramSubject.Create("int Target(int[,] values) => values[0, 0];");
        var values = Array.CreateInstance(typeof(int), 1, 1);
        values.SetValue(17, 0, 0);
        Assert.That(subject.Invoke([values]), Is.EqualTo(17));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();
        var operation = (IArrayElementReferenceOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(operation.Indices, Has.Length.EqualTo(2));
        var lowerer = new RoslynTotalExpressionLowerer(subject.Context);
        var children = operation.ChildOperations.Select(child => lowerer.LowerClause(child)).ToArray();
        Assert.That(children, Has.Length.EqualTo(3));
        Assert.That(children.All(child => child.Classification.IsExact), Is.True);
        var rule = CSharpOperationSemantics.Apply(subject.Factory, operation, [.. children.Select(child => child.Value)]);
        Assert.That(rule.Classification.IsExact, Is.False);
        Assert.That(rule.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        Assert.That(rule.Throws, Is.Empty);
        Assert.That(new IrInterpreter(subject.Factory).Evaluate(rule.Value, new Dictionary<IrVarId, IrValue>()).Value!.Boolean, Is.False);
        var clause = lowerer.LowerClause(operation);
        Assert.That(clause.Classification.IsExact, Is.False);
        Assert.That(clause.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        var lowering = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        Assert.That(lowering.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrHavocInstruction>()
            .Any(havoc => havoc.Origin == IrHavocOrigin.Approximation), Is.True);
    }
}
