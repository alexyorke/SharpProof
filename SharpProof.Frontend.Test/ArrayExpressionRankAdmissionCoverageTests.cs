using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ArrayExpressionRankAdmissionCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ActualArrayExpressionRankPreservesSupportedReadAndClosedAdmission(bool multidimensional)
    {
        using var subject = TypedProgramSubject.Create(multidimensional
            ? "long Target(long[,] values, int index) => values[index, index];"
            : "long Target(long[] values, int index) => values[index];");
        object values = multidimensional ? new long[,] { { 17L } } : new long[] { 17L };
        Assert.That(subject.Invoke([values, 0]), Is.EqualTo(17L));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var access = tree.GetRoot().DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();
        var operation = (IArrayElementReferenceOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(access)!;
        Assert.That(operation.Indices, Has.Length.EqualTo(multidimensional ? 2 : 1));
        var lowering = new RoslynOperationLowerer(new IrFactory()).Lower(operation);
        Assert.That(lowering.IsExact, Is.EqualTo(!multidimensional));
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(multidimensional
            ? FrontendAbstention.UnsupportedMemberAccess : FrontendAbstention.None));
        Assert.That(lowering.Term, Is.Not.Null);
    }
}
