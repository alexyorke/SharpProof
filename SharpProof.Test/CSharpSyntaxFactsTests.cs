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

    [TestCase(SyntaxKind.AddAssignmentExpression, SyntaxKind.AddExpression)]
    [TestCase(SyntaxKind.SubtractAssignmentExpression, SyntaxKind.SubtractExpression)]
    [TestCase(SyntaxKind.MultiplyAssignmentExpression, SyntaxKind.MultiplyExpression)]
    [TestCase(SyntaxKind.DivideAssignmentExpression, SyntaxKind.DivideExpression)]
    [TestCase(SyntaxKind.ModuloAssignmentExpression, SyntaxKind.ModuloExpression)]
    [TestCase(SyntaxKind.AndAssignmentExpression, SyntaxKind.BitwiseAndExpression)]
    [TestCase(SyntaxKind.ExclusiveOrAssignmentExpression, SyntaxKind.ExclusiveOrExpression)]
    [TestCase(SyntaxKind.OrAssignmentExpression, SyntaxKind.BitwiseOrExpression)]
    [TestCase(SyntaxKind.LeftShiftAssignmentExpression, SyntaxKind.LeftShiftExpression)]
    [TestCase(SyntaxKind.RightShiftAssignmentExpression, SyntaxKind.RightShiftExpression)]
    [TestCase(SyntaxKind.UnsignedRightShiftAssignmentExpression, SyntaxKind.UnsignedRightShiftExpression)]
    [TestCase(SyntaxKind.CoalesceAssignmentExpression, SyntaxKind.CoalesceExpression)]
    public void TryGetCompoundAssignmentBinaryKind_MapsSupportedOperators(
        SyntaxKind assignmentKind,
        SyntaxKind expectedBinaryKind)
    {
        Assert.That(CSharpSyntaxFacts.TryGetCompoundAssignmentBinaryKind(assignmentKind, out var binaryKind), Is.True);
        Assert.That(binaryKind, Is.EqualTo(expectedBinaryKind));
    }

    [Test]
    public void TryGetCompoundAssignmentBinaryKind_RejectsSimpleAssignment()
    {
        Assert.That(CSharpSyntaxFacts.TryGetCompoundAssignmentBinaryKind(
            SyntaxKind.SimpleAssignmentExpression, out var binaryKind), Is.False);
        Assert.That(binaryKind, Is.EqualTo(SyntaxKind.None));
    }

    [TestCase("++value", 1)]
    [TestCase("value++", 1)]
    [TestCase("--value", -1)]
    [TestCase("value--", -1)]
    [TestCase("((value++))", 1)]
    public void TryGetIncrementOrDecrementOperand_ReturnsOperandAndDelta(string text, int expectedDelta)
    {
        var expression = SyntaxFactory.ParseExpression(text);

        Assert.That(CSharpSyntaxFacts.TryGetIncrementOrDecrementOperand(
            expression, out var operand, out var delta), Is.True);
        Assert.That(operand.ToString(), Is.EqualTo("value"));
        Assert.That(delta, Is.EqualTo(expectedDelta));
    }

    [Test]
    public void TryGetIncrementOrDecrementOperand_RejectsNonMutation()
    {
        Assert.That(CSharpSyntaxFacts.TryGetIncrementOrDecrementOperand(
            SyntaxFactory.ParseExpression("value + 1"), out _, out _), Is.False);
    }

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
