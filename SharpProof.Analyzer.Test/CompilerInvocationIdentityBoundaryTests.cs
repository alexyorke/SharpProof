using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

// These exercise the independent identity API, not admitted contract clauses.
[TestFixture]
public sealed class CompilerInvocationIdentityBoundaryTests
{
    [TestCase("")]
    [TestCase(" \t")]
    public void NestedIdentityRejectsBlankParentForAnEmittedLocalMethod(string parent)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            public static class Subject {
                public static int Root(int value) {
                    int Local(int input) => input;
                    return Local(value);
                }
            }
            """, [], filePath: "nested-identity.cs");
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var owner = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var localDeclaration = declaration.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single();
        var local = (IMethodSymbol)model.GetDeclaredSymbol(localDeclaration)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(local.ContainingSymbol, owner), Is.True);
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("NestedIdentity", isCollectible: true);
        try
        {
            using var image = new MemoryStream(AnalyzerTestHost.EmitImage(compilation), writable: false);
            var assembly = loadContext.LoadFromStream(image);
            Assert.That(assembly.GetType("Subject", throwOnError: true)!.GetMethod("Root")!
                .Invoke(null, [4]), Is.EqualTo(4));
        }
        finally
        {
            loadContext.Unload();
        }
        var validParent = SemanticClaimIdentity.CreateCallableId(owner);
        var valid = SemanticClaimIdentity.CreateNestedCallableId(validParent, local, 0);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(validParent, Is.Not.Empty);
            Assert.That(valid, Does.StartWith("spm1:"));
            Assert.That(SemanticClaimIdentity.CreateNestedCallableId(validParent, local, 0), Is.EqualTo(valid));
            Assert.That(SemanticClaimIdentity.CreateNestedCallableId(validParent, local, 1), Is.Not.EqualTo(valid));
        }
        var exception = Assert.Throws<ArgumentException>(() =>
            SemanticClaimIdentity.CreateNestedCallableId(parent, local, 0));
        Assert.That(exception!.ParamName, Is.EqualTo("parentId"));
    }

    [TestCase(0)]
    [TestCase(2)]
    public void EmittedInvocationArityRetainsArgumentAndMethodIdentity(int arity)
    {
        var arguments = arity == 0 ? "" : "value, 3";
        var expected = arity == 0 ? 7 : 43;
        var first = Fingerprint(arity, "value", "Target", arguments, expected);
        var renamed = Fingerprint(arity, "renamed", "Target",
            arity == 0 ? "" : "renamed, 3", expected);
        var differentMethod = Fingerprint(arity, "value", "Other", arguments, expected);
        var ordinary = Fingerprint(1, "value", "Target", "value", 4);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Length.EqualTo(64));
            Assert.That(renamed, Is.EqualTo(first));
            Assert.That(differentMethod, Is.Not.EqualTo(first));
            Assert.That(ordinary, Has.Length.EqualTo(64));
            Assert.That(ordinary, Is.Not.EqualTo(first));
        }
        if (arity == 2)
        {
            Assert.That(Fingerprint(2, "value", "Target", "3, value", 34), Is.Not.EqualTo(first));
            Assert.That(Fingerprint(2, "value", "Target", "value, 4", 44), Is.Not.EqualTo(first));
        }
    }

    private static string Fingerprint(int arity, string parameter, string target, string arguments, int expected)
    {
        var parameters = arity switch
        {
            0 => "",
            1 => "int left",
            2 => "int left, int right",
            _ => throw new ArgumentOutOfRangeException(nameof(arity))
        };
        var result = arity switch
        {
            0 => "7",
            1 => "left",
            2 => "left * 10 + right",
            _ => throw new ArgumentOutOfRangeException(nameof(arity))
        };
        var compilation = AnalyzerTestHost.CreateCompilation(
            $$"""
            public static class Subject {
                private static int {{target}}({{parameters}}) => {{result}};
                public static int Root(int {{parameter}}) => {{target}}({{arguments}});
            }
            """, [], filePath: "invocation-identity.cs");
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Root");
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var syntax = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var operation = (IInvocationOperation)model.GetOperation(syntax)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(operation.Arguments, Has.Length.EqualTo(arity));
            Assert.That(operation.TargetMethod.Name, Is.EqualTo(target));
            Assert.That(SymbolEqualityComparer.Default.Equals(
                model.GetEnclosingSymbol(syntax.SpanStart), method), Is.True);
        }
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("InvocationIdentity", isCollectible: true);
        try
        {
            using var image = new MemoryStream(AnalyzerTestHost.EmitImage(compilation), writable: false);
            var assembly = loadContext.LoadFromStream(image);
            Assert.That(assembly.GetType("Subject", throwOnError: true)!.GetMethod("Root")!
                .Invoke(null, [4]), Is.EqualTo(expected));
        }
        finally
        {
            loadContext.Unload();
        }
        return SemanticClaimIdentity.CreateInvocationFingerprint(operation, method, method, usesCompanion: false);
    }
}
