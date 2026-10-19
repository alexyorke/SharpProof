using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreRecursiveCallableDomainTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void RecursiveManagedSignaturesIncludeDelegateReferences(bool nestedDelegate)
    {
        var type = nestedDelegate ? "List<Func<int>[]>" : "Dictionary<int, List<int[][]>>";
        var source = "using System; using System.Collections.Generic; public static class Fixture { public static " +
            type + " Target(" + type + " value) => value; }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        Assert.That(AnalyzerTestHost.EmitImage(compilation), Is.Not.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
        Assert.That(method, Is.Not.Null);
        Assert.That(method.ReturnType, Is.AssignableTo<INamedTypeSymbol>());
        Assert.That(method.Parameters.Single().Type, Is.EqualTo(method.ReturnType));
        Assert.That(CallableSubset.IsSupported(method, declaration), Is.True);
    }
}
