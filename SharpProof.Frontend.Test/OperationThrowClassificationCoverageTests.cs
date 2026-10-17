using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class OperationThrowClassificationCoverageTests
{
    [TestCase("decimal x, decimal y", "x + y", true)]
    [TestCase("decimal x, decimal y", "x - y", true)]
    [TestCase("decimal x, decimal y", "x * y", true)]
    [TestCase("decimal x, decimal y", "x / y", true)]
    [TestCase("decimal x", "-x", true)]
    [TestCase("decimal x", "+x", false)]
    [TestCase("decimal x", "++x", true)]
    [TestCase("decimal x, decimal y", "x += y", true)]
    [TestCase("decimal? x, decimal? y", "x + y", true)]
    [TestCase("decimal x", "(int)x", true)]
    [TestCase("decimal x", "(double)x", false)]
    [TestCase("decimal x", "(float)x", false)]
    [TestCase("int x", "(decimal)x", false)]
    [TestCase("double x", "(decimal)x", true)]
    [TestCase("float x", "(decimal)x", true)]
    [TestCase("decimal? x", "(int?)x", true)]
    [TestCase("int? x", "(int)x", true)]
    [TestCase("int x", "(int?)x", false)]
    [TestCase("int? x", "(long?)x", false)]
    [TestCase("int x", "checked((byte)x)", true)]
    [TestCase("decimal x", "checked((double)x)", false)]
    [TestCase("int x", "x + 1", false)]
    [TestCase("int x", "checked(x + 1)", true)]
    [TestCase("int x", "x / 2", true)]
    [TestCase("object x", "x as string", false)]
    public void BuiltInClassificationPreservesArithmeticAndConversionFaults(
        string parameters, string expression, bool expected)
    {
        System.ArgumentNullException.ThrowIfNull(expression);
        System.ArgumentNullException.ThrowIfNull(parameters);
        var operation = GetExpression(parameters, expression);
        Assert.That(CSharpOperationSemantics.BuiltInOperationMayThrow(operation),
            Is.EqualTo(expected), operation.Kind.ToString());
        Assert.That(CSharpOperationSemantics.OperationMayThrow(operation),
            Is.EqualTo(expected), operation.Kind.ToString());
    }

    [TestCase("object x", "(string)x", true)]
    [TestCase("object x", "(int)x", true)]
    [TestCase("string x", "(object)x", true)]
    public void ReferenceAndUnboxingCastsPreserveRuntimeFaults(
        string parameters, string expression, bool expected)
    {
        System.ArgumentNullException.ThrowIfNull(expression);
        System.ArgumentNullException.ThrowIfNull(parameters);
        var operation = GetExpression(parameters, expression);
        Assert.That(CSharpOperationSemantics.OperationMayThrow(operation),
            Is.EqualTo(expected));
    }

    [Test]
    public void UserConversionIsPotentiallyThrowingEvenWhenItsBodyReturns()
    {
        var operation = GetExpression("Box x", "(int)x", """
            public sealed class Box {
                public static explicit operator int(Box value) => 1;
            }
            """);
        Assert.That(CSharpOperationSemantics.OperationMayThrow(operation), Is.True);
        Assert.That(CSharpOperationSemantics.BuiltInOperationMayThrow(operation), Is.False);
    }

    [Test]
    public void IncrementViaImplicitConversionsPreservesHiddenCallFaults()
    {
        var operation = GetExpression("Box x", "++x", """
            public struct Box {
                public static implicit operator int(Box value) => 1;
                public static implicit operator Box(int value) => default;
            }
            """);
        Assert.That(CSharpOperationSemantics.BuiltInOperationMayThrow(operation), Is.True);
        Assert.That(CSharpOperationSemantics.OperationMayThrow(operation), Is.True);
    }

    [TestCase("int x", "++x")]
    [TestCase("char x", "++x")]
    [TestCase("double x", "++x")]
    public void UncheckedPredefinedIncrementHasNoArithmeticFault(string parameters, string expression)
    {
        System.ArgumentNullException.ThrowIfNull(expression);
        System.ArgumentNullException.ThrowIfNull(parameters);
        var operation = GetExpression(parameters, expression);
        Assert.That(CSharpOperationSemantics.BuiltInOperationMayThrow(operation), Is.False);
        Assert.That(CSharpOperationSemantics.OperationMayThrow(operation), Is.False);
    }

    private static IOperation GetExpression(
        string parameters, string expression, string declarations = "")
    {
        System.ArgumentNullException.ThrowIfNull(declarations);
        System.ArgumentNullException.ThrowIfNull(expression);
        System.ArgumentNullException.ThrowIfNull(parameters);
        var tree = CSharpSyntaxTree.ParseText(
            declarations + " public static class Subject { public static object Target(" +
            parameters + ") => " + expression + "; }");
        var compilation = CSharpCompilation.Create(
            "ThrowClassification", [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)),
            Is.Empty);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target")).ExpressionBody!.Expression;
        return FrontendTestHelpers.TryGetExpressionOperation(
            compilation.GetSemanticModel(tree), syntax)!;
    }
}
