using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypeTestAllocationDomainCoverageTests
{
    [TestCase("bool Target(int value) => value is int;", 17)]
    [TestCase("bool Target(object value) => value is string;", "word")]
    [TestCase("bool Target(int value) => value is int number;", 17)]
    public void ActualNonGenericTypeTestsHaveNoGenericBoxingOperand(string members, object input)
    {
        System.ArgumentNullException.ThrowIfNull(input);
        System.ArgumentNullException.ThrowIfNull(members);
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([input]), Is.True);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(syntax.ExpressionBody!.Expression)!;
        Assert.That(operation, Is.InstanceOf<IIsTypeOperation>().Or.InstanceOf<IIsPatternOperation>());
        Assert.That(operation.Type!.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestOperand(operation), Is.Null);
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestMayAllocate(operation, subject.Compilation), Is.False);
    }

    [Test]
    public void ActualReferenceConstrainedGenericTypeTestRetainsItsOperandWithoutBoxing()
    {
        using var subject = TypedProgramSubject.Create("bool Target(string value) => Helper<string>(value); static bool Helper<T>(T value) where T : class => value is string;");
        Assert.That(subject.Invoke(["word"]), Is.True);
        Assert.That(subject.Invoke([null!]), Is.False);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Helper"));
        var operation = (IIsTypeOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax.ExpressionBody!.Expression)!;
        Assert.That(operation.ValueOperand.Type, Is.InstanceOf<ITypeParameterSymbol>());
        Assert.That(operation.ValueOperand.Type!.IsReferenceType, Is.True);
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestOperand(operation), Is.SameAs(operation.ValueOperand));
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestMayAllocate(operation, subject.Compilation), Is.False);
        using var supported = TypedProgramSubject.Create("int Target(int value) => value + 1;");
        Assert.That(supported.Invoke([17]), Is.EqualTo(18));
        var lowering = supported.Lower();
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        var execution = supported.Execute(lowering, [17]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(18)));
    }
}
