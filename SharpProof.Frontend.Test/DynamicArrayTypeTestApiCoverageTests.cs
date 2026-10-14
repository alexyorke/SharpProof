using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class DynamicArrayTypeTestApiCoverageTests
{
    [Test]
    public void ActualDynamicArrayTestHasAClosedRuntimeElementDomain()
    {
        using var subject = TypedProgramSubject.Create(
            "bool Target(int value) => Helper(value); static bool Helper<T>(T value) => value is dynamic[];");
        Assert.That(subject.Invoke([17]), Is.False);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(syntax => syntax.Identifier.ValueText == "Helper");
        var operation = subject.Compilation.GetSemanticModel(tree)
            .GetOperation(method.ExpressionBody!.Expression);
        Assert.That(operation, Is.AssignableTo<IIsTypeOperation>());
        var test = (IIsTypeOperation)operation!;
        Assert.That(test.ValueOperand.Type, Is.AssignableTo<ITypeParameterSymbol>());
        Assert.That(test.TypeOperand, Is.AssignableTo<IArrayTypeSymbol>());
        var array = (IArrayTypeSymbol)test.TypeOperand;
        Assert.That(array.IsSZArray, Is.True);
        Assert.That(array.ElementType.TypeKind, Is.EqualTo(TypeKind.Dynamic));
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestOperand(test), Is.SameAs(test.ValueOperand));
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestMayAllocate(test, subject.Compilation), Is.False);
    }
}
