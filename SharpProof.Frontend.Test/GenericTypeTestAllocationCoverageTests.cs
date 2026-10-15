using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class GenericTypeTestAllocationCoverageTests
{
    [TestCase("U[]", true)]
    [TestCase("System.Collections.Generic.Dictionary<int, int>", false)]
    [TestCase("System.Collections.Generic.List<dynamic>", false)]
    public void CompiledGenericTypeTestsDistinguishOpenAndClosedTestedTypes(string testedType, bool expected)
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static bool Target<T, U>(T value) => value is " + testedType + "; }");
        var compilation = CSharpCompilation.Create("GenericTypeTestAllocationControl", [tree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(method.ExpressionBody!.Expression)!;
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestOperand(operation), Is.Not.Null);
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestMayAllocate(operation, compilation), Is.EqualTo(expected));
    }
}
