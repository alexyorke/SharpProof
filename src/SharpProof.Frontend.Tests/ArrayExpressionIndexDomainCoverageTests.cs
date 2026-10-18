using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ArrayExpressionIndexDomainCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ActualTotalArrayIndexPreservesSignedAdmissionAndUnsignedClosure(bool usesNonNegativeIndexDomain)
    {
        using var subject = TypedProgramSubject.Create(usesNonNegativeIndexDomain
            ? "int Target(int[] values, uint index) => values[index];"
            : "int Target(int[] values, int index) => values[index];");
        int[] values = [17];
        Assert.That(subject.Invoke(usesNonNegativeIndexDomain ? [values, 0U] : [values, 0]), Is.EqualTo(17));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();
        var operation = (IArrayElementReferenceOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(operation.Indices.Single().Type!.SpecialType,
            Is.EqualTo(usesNonNegativeIndexDomain ? SpecialType.System_UInt32 : SpecialType.System_Int32));
        var result = new RoslynOperationLowerer(new IrFactory(IrExecutionSemantics.Total)).Lower(operation);
        Assert.That(result.IsExact, Is.EqualTo(!usesNonNegativeIndexDomain));
        Assert.That(result.Classification.Abstention,
            Is.EqualTo(usesNonNegativeIndexDomain ? FrontendAbstention.UnsupportedType : FrontendAbstention.None));
        Assert.That(result.Term, Is.Not.Null);
    }
}
