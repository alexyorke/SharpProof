using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypeTestCompilationAvailabilityApiCoverageTests
{
    [Test]
    public void ActualGenericTypeTestUsesConservativePolicyWithoutCompilationAuthority()
    {
        using var subject = TypedProgramSubject.Create(
            "bool Target(int value) => Helper(value); static bool Helper<T>(T value) => value is object;");
        Assert.That(subject.Invoke([17]), Is.True);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : (syntax.Identifier.ValueText == "Helper"));
        var operation = subject.Compilation.GetSemanticModel(tree)
            .GetOperation(method.ExpressionBody!.Expression);
        Assert.That(operation, Is.AssignableTo<IIsTypeOperation>());
        var test = (IIsTypeOperation)operation!;
        Assert.That(test.ValueOperand.Type, Is.AssignableTo<ITypeParameterSymbol>());
        Assert.That(test.ValueOperand.Type!.IsReferenceType, Is.False);
        Assert.That(test.TypeOperand.SpecialType, Is.EqualTo(SpecialType.System_Object));
        Assert.That(((CSharpCompilation)subject.Compilation).Options.OptimizationLevel,
            Is.EqualTo(OptimizationLevel.Release));
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestOperand(test), Is.SameAs(test.ValueOperand));
        // The API's nullable compilation parameter reports a conservative
        // possibility. The actual owned compilation supplies the Release policy.
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestMayAllocate(test, null), Is.True);
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestMayAllocate(test, subject.Compilation), Is.False);
    }
}
