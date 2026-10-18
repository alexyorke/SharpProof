using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreCallableShapeCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualDelegateAndOrdinaryDeclarationsRetainTheirCallableClassification(bool delegateDeclaration)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            "public delegate int Callback(int value); public static class Fixture { public static int Target(int value) => value; }", []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var root = await compilation.SyntaxTrees.Single().GetRootAsync().ConfigureAwait(false);
        IMethodSymbol method;
        SyntaxNode declaration;
        if (delegateDeclaration)
        {
            method = compilation.GetTypeByMetadataName("Callback")!.DelegateInvokeMethod!;
            declaration = root.DescendantNodes().OfType<DelegateDeclarationSyntax>().Single();
            Assert.That(method.MethodKind, Is.EqualTo(MethodKind.DelegateInvoke));
        }
        else
        {
            declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            method = (IMethodSymbol)compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration)!;
            Assert.That(method.MethodKind, Is.EqualTo(MethodKind.Ordinary));
        }
        Assert.That(CallableSubset.IsSupported(method, declaration), Is.EqualTo(!delegateDeclaration));
    }
}
