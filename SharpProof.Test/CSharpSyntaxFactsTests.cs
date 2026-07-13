using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public class CSharpSyntaxFactsTests
{
    private const ExecutionRootPolicy ExtendedExecutionRoots =
        ExecutionRootPolicy.Callable |
        ExecutionRootPolicy.ExpressionBodiedPropertyOrIndexer |
        ExecutionRootPolicy.Initializer |
        ExecutionRootPolicy.GlobalStatement;

    [Test]
    public void GetContainingExecutionRoot_ExtendedPolicy_SelectsNearestRequestedBoundary()
    {
        var root = CSharpSyntaxTree.ParseText("""
            System.Console.WriteLine();

            public class TestClass
            {
                private int _value = 1 + 2;
                public int Property => 3 + 4;
                public int Method() => 5 + 6;
            }
            """).GetRoot();
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var binaryExpressions = root.DescendantNodes().OfType<BinaryExpressionSyntax>().ToArray();

        Assert.That(
            CSharpSyntaxFacts.GetContainingExecutionRoot(invocation, ExtendedExecutionRoots),
            Is.TypeOf<GlobalStatementSyntax>());
        Assert.That(
            CSharpSyntaxFacts.GetContainingExecutionRoot(binaryExpressions[0], ExtendedExecutionRoots),
            Is.TypeOf<EqualsValueClauseSyntax>());
        Assert.That(
            CSharpSyntaxFacts.GetContainingExecutionRoot(binaryExpressions[1], ExtendedExecutionRoots),
            Is.TypeOf<PropertyDeclarationSyntax>());
        Assert.That(
            CSharpSyntaxFacts.GetContainingExecutionRoot(binaryExpressions[2], ExtendedExecutionRoots),
            Is.TypeOf<MethodDeclarationSyntax>());
    }

    [Test]
    public void GetContainingExecutionRoot_DefaultPolicyRetainsSyntaxTreeFallback()
    {
        var root = CSharpSyntaxTree.ParseText("public class TestClass { public int Value => 1 + 2; }").GetRoot();
        var expression = root.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();

        Assert.That(
            CSharpSyntaxFacts.GetContainingExecutionRoot(expression, ExecutionRootPolicy.Callable),
            Is.Null);
        Assert.That(CSharpSyntaxFacts.GetContainingExecutionRoot(expression), Is.SameAs(root));
    }
}
