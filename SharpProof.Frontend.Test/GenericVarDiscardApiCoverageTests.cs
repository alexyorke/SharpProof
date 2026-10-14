using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class GenericVarDiscardApiCoverageTests
{
    [TestCase("bool Target(int value) => Helper(value); static bool Helper<T>(T value) => value is var _;", 17)]
    [TestCase("bool Target(object value) => Helper(value); static bool Helper<T>(T value) => value is var _;", null)]
    public void ActualGenericVarDiscardHasNoRuntimeTestedTypeOrBoxingRequirement(string source, object? input)
    {
        using var subject = TypedProgramSubject.Create(source);
        Assert.That(subject.Invoke([input!]), Is.True);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(syntax => syntax.Identifier.ValueText == "Helper");
        var operation = subject.Compilation.GetSemanticModel(tree)
            .GetOperation(method.ExpressionBody!.Expression);
        Assert.That(operation, Is.AssignableTo<IIsPatternOperation>());
        var test = (IIsPatternOperation)operation!;
        Assert.That(test.Value.Type, Is.AssignableTo<ITypeParameterSymbol>());
        Assert.That(test.Value.Type!.IsReferenceType, Is.False);
        Assert.That(test.Pattern, Is.AssignableTo<IDeclarationPatternOperation>());
        var pattern = (IDeclarationPatternOperation)test.Pattern;
        Assert.That(pattern.DeclaredSymbol, Is.Null);
        Assert.That(pattern.MatchedType, Is.Null);
        Assert.That(pattern.MatchesNull, Is.True);
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestOperand(test), Is.SameAs(test.Value));
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestMayAllocate(test, subject.Compilation), Is.False);
    }
}
