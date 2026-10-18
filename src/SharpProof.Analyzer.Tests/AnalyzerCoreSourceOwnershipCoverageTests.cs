using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreSourceOwnershipCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void LoweringRequiresTheActualDeclarationTreeOwner(bool foreign)
    {
        var owner = AnalyzerTestHost.CreateCompilation(
            "public static class Fixture { public static int Target() => 1; }", []);
        Assert.That(owner.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var tree = owner.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)owner.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
        var suppliedCompilation = foreign
            ? AnalyzerTestHost.CreateCompilation("public static class Other { public static int Value() => 2; }", [])
            : owner;
        Assert.That(suppliedCompilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        Assert.That(suppliedCompilation.ContainsSyntaxTree(tree), Is.EqualTo(!foreign));
        var (body, gap) = AdvisoryLowering.Lower(suppliedCompilation, method, declaration, TestContext.CurrentContext.CancellationToken);
        if (foreign)
        {
            Assert.That(body, Is.Null);
            Assert.That(gap, Is.EqualTo("SourceOwnership"));
        }
        else
        {
            Assert.That(gap, Is.Null);
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Lowering.IsExact, Is.True);
            Assert.That(body.Trees.Values.Single(), Is.SameAs(tree));
            Assert.That(body.Program.Blocks, Is.Not.Empty);
        }
    }
}
