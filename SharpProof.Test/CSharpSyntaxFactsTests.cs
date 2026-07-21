using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public class CSharpSyntaxFactsTests {
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
        SyntaxKind expectedBinaryKind) {
        Assert.That(CSharpSyntaxFacts.TryGetCompoundAssignmentBinaryKind(assignmentKind, out var binaryKind), Is.True);
        Assert.That(binaryKind, Is.EqualTo(expectedBinaryKind));
    }

    [Test]
    public void TryGetCompoundAssignmentBinaryKind_RejectsSimpleAssignment() {
        Assert.That(CSharpSyntaxFacts.TryGetCompoundAssignmentBinaryKind(
            SyntaxKind.SimpleAssignmentExpression, out var binaryKind), Is.False);
        Assert.That(binaryKind, Is.EqualTo(SyntaxKind.None));
    }

    [TestCase("++value", 1)]
    [TestCase("value++", 1)]
    [TestCase("--value", -1)]
    [TestCase("value--", -1)]
    [TestCase("((value++))", 1)]
    public void TryGetIncrementOrDecrementOperand_ReturnsOperandAndDelta(string text, int expectedDelta) {
        var expression = SyntaxFactory.ParseExpression(text);

        Assert.That(CSharpSyntaxFacts.TryGetIncrementOrDecrementOperand(
            expression, out var operand, out var delta), Is.True);
        Assert.That(operand.ToString(), Is.EqualTo("value"));
        Assert.That(delta, Is.EqualTo(expectedDelta));
    }

    [Test]
    public void TryGetIncrementOrDecrementOperand_RejectsNonMutation() {
        Assert.That(CSharpSyntaxFacts.TryGetIncrementOrDecrementOperand(
            SyntaxFactory.ParseExpression("value + 1"), out _, out _), Is.False);
    }

    [Test]
    public void GetContainingExecutionRoot_ExtendedPolicy_SelectsNearestRequestedBoundary() {
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
    public void GetContainingExecutionRoot_DefaultPolicyRetainsSyntaxTreeFallback() {
        var root = CSharpSyntaxTree.ParseText("public class TestClass { public int Value => 1 + 2; }").GetRoot();
        var expression = root.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();

        Assert.That(
            CSharpSyntaxFacts.GetContainingExecutionRoot(expression, ExecutionRootPolicy.Callable),
            Is.Null);
        Assert.That(CSharpSyntaxFacts.GetContainingExecutionRoot(expression), Is.SameAs(root));
    }

    [TestCase("class C { void M() { } }", SyntaxKind.MethodDeclaration, SyntaxKind.Block)]
    [TestCase("class C { int M() => 1; }", SyntaxKind.MethodDeclaration, SyntaxKind.NumericLiteralExpression)]
    [TestCase("class C { C() { } }", SyntaxKind.ConstructorDeclaration, SyntaxKind.Block)]
    [TestCase("class C { int value; C() => value = 1; }", SyntaxKind.ConstructorDeclaration,
        SyntaxKind.SimpleAssignmentExpression)]
    [TestCase("class C { public static C operator +(C left, C right) { return left; } }",
        SyntaxKind.OperatorDeclaration, SyntaxKind.Block)]
    [TestCase("class C { public static C operator +(C left, C right) => left; }",
        SyntaxKind.OperatorDeclaration, SyntaxKind.IdentifierName)]
    [TestCase("class C { public static explicit operator int(C value) { return 1; } }",
        SyntaxKind.ConversionOperatorDeclaration, SyntaxKind.Block)]
    [TestCase("class C { public static explicit operator int(C value) => 1; }",
        SyntaxKind.ConversionOperatorDeclaration, SyntaxKind.NumericLiteralExpression)]
    [TestCase("class C { int P { get { return 1; } } }", SyntaxKind.GetAccessorDeclaration, SyntaxKind.Block)]
    [TestCase("class C { int P { get => 1; } }", SyntaxKind.GetAccessorDeclaration,
        SyntaxKind.NumericLiteralExpression)]
    [TestCase("class C { void M() { int Local() { return 1; } } }", SyntaxKind.LocalFunctionStatement,
        SyntaxKind.Block)]
    [TestCase("class C { void M() { int Local() => 1; } }", SyntaxKind.LocalFunctionStatement,
        SyntaxKind.NumericLiteralExpression)]
    [TestCase("class C { int P => 1; }", SyntaxKind.PropertyDeclaration, SyntaxKind.NumericLiteralExpression)]
    [TestCase("class C { int this[int index] => index; }", SyntaxKind.IndexerDeclaration,
        SyntaxKind.IdentifierName)]
    public void MethodBodyOperationResolver_SelectsSharedBodyOrExpressionTaxonomy(
        string source,
        SyntaxKind declarationKind,
        SyntaxKind expectedOperationSyntaxKind) {
        var semanticModel = CreateSemanticModel(source, out var root);
        var declaration = root.DescendantNodes().Single(node => node.IsKind(declarationKind));

        var operation = MethodBodyOperationResolver.GetMethodBodyRootOperation(
            declaration,
            semanticModel,
            CancellationToken.None);

        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.Syntax.Kind(), Is.EqualTo(expectedOperationSyntaxKind));
    }

    [TestCase("class C { public static explicit operator int(C value) => 1; }",
        SyntaxKind.ConversionOperatorDeclaration, false)]
    [TestCase("class C { ~C() { } }", SyntaxKind.DestructorDeclaration, true)]
    public void MethodBodyOperationResolver_RetainsDeclarationFallback(
        string source,
        SyntaxKind declarationKind,
        bool includeConversionOperators) {
        var semanticModel = CreateSemanticModel(source, out var root);
        var declaration = root.DescendantNodes().Single(node => node.IsKind(declarationKind));
        var expected = semanticModel.GetOperation(declaration);

        var actual = MethodBodyOperationResolver.GetMethodBodyRootOperation(
            declaration,
            semanticModel,
            CancellationToken.None,
            includeConversionOperators);

        Assert.That(actual?.Kind, Is.EqualTo(expected?.Kind));
        Assert.That(actual?.Syntax, Is.SameAs(expected?.Syntax));
    }

    private static SemanticModel CreateSemanticModel(string source, out SyntaxNode root) {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            nameof(CSharpSyntaxFactsTests),
            new[] { tree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty);
        root = tree.GetRoot();
        return compilation.GetSemanticModel(tree);
    }
}
