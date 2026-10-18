using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TotalExpressionAdmissionCoverageTests
{
    [TestCase("bool Target(int x) => x is int and >= 0;")]
    [TestCase("bool Target(int x) => x is >= 0 and int;")]
    [TestCase("bool Target(int x) => x is int or < 0;")]
    [TestCase("bool Target(int x) => x is < 0 or int;")]
    public void UnsupportedPatternHalvesKeepTheCombinedPatternClosed(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        using var subject = TypedProgramSubject.Create(source);
        Assert.That(subject.Invoke([0]), Is.EqualTo(true));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<IsPatternExpressionSyntax>().Single();
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        var expression = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(operation);
        Assert.That(expression.Classification.IsExact, Is.False);
        Assert.That(subject.Lower().IsExact, Is.False);
    }

    [Test]
    public void DeepValidExpressionPreservesTheProductionDepthBoundary()
    {
        var expressionText = string.Concat(Enumerable.Repeat("x + (", 270)) + "x" + new string(')', 270);
        using var subject = TypedProgramSubject.Create("int Target(int x) => " + expressionText + ";");
        Assert.That(subject.Invoke([1]), Is.EqualTo(271));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single().ExpressionBody!.Expression;
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        var result = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(operation);
        Assert.That(result.Classification.Abstention, Is.EqualTo(FrontendAbstention.ExpressionDepthLimit));
    }

    [TestCase("string Target(string text) => (string)null + (string)null;", "")]
    [TestCase("string Target(string text) => text + \"\";", "proof")]
    public void DegenerateConcatenationRetainsItsCompiledValue(string members, string expected)
    {
        System.ArgumentNullException.ThrowIfNull(expected);
        System.ArgumentNullException.ThrowIfNull(members);
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke(["proof"]), Is.EqualTo(expected));
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
    }
}
