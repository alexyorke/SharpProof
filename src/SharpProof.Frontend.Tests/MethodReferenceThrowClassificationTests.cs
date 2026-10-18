using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class MethodReferenceThrowClassificationTests
{
    [TestCase("public sealed class Cell { public int Read() => 1; }", "Cell cell", "cell.Read", true)]
    [TestCase("public sealed class Cell { public static int Read() => 1; }", "", "Cell.Read", false)]
    [TestCase("public struct Cell { public int Read() => 1; }", "Cell cell", "cell.Read", false)]
    public void MethodGroupNullCheckingFollowsTheActualReceiverDomain(
        string declaration, string parameters, string expression, bool expected)
    {
        System.ArgumentNullException.ThrowIfNull(declaration);
        System.ArgumentNullException.ThrowIfNull(expression);
        System.ArgumentNullException.ThrowIfNull(parameters);
        var tree = CSharpSyntaxTree.ParseText(declaration +
            " public static class Subject { public static System.Func<int> Target(" +
            parameters + ") => " + expression + "; }");
        var compilation = CSharpCompilation.Create("MethodReferenceClassification", [tree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var operation = compilation.GetSemanticModel(tree).GetOperation(syntax.ExpressionBody!.Expression)!;
        var reference = operation.DescendantsAndSelf().OfType<IMethodReferenceOperation>().Single();
        Assert.That(CSharpOperationSemantics.OperationMayThrow(reference), Is.EqualTo(expected));
        Assert.That(CSharpOperationSemantics.BuiltInOperationMayThrow(reference), Is.False);
    }

    [TestCase("int x", "-x")]
    [TestCase("int x, int y", "x += y")]
    public void NonDecimalUnaryAndCompoundArithmeticHaveNoDecimalFault(
        string parameters, string expression)
    {
        System.ArgumentNullException.ThrowIfNull(expression);
        System.ArgumentNullException.ThrowIfNull(parameters);
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target(" +
            parameters + ") => " + expression + "; }");
        var compilation = CSharpCompilation.Create("NonDecimalClassification", [tree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(syntax.ExpressionBody!.Expression)!;
        Assert.That(CSharpOperationSemantics.OperationMayThrow(operation), Is.False);
        Assert.That(CSharpOperationSemantics.BuiltInOperationMayThrow(operation), Is.False);
    }
}
