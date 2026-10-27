using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerStructuralIdentityWorkflowTests
{
    [Test]
    public void NamedContainerUsesItsDocumentationIdentityAcrossSourcePaths()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ContainerId("internal", "first.cs"), Is.EqualTo("T:Subject"));
            Assert.That(ContainerId("internal", "second.cs"), Is.EqualTo("T:Subject"));
        }
    }

    [Test]
    public void FileLocalContainerRetainsItsSourceScopeInStructuralIdentity()
    {
        var first = ContainerId("file", "first.cs");
        var repeated = ContainerId("file", "first.cs");
        var second = ContainerId("file", "second.cs");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Does.StartWith("sps1:"));
            Assert.That(first, Has.Length.EqualTo(69));
            Assert.That(repeated, Is.EqualTo(first));
            Assert.That(second, Is.Not.EqualTo(first));
        }
    }

    [Test]
    public void FunctionPointerConventionIdentityPreservesParameterRolesAcrossRenames()
    {
        var first = PointerFingerprint("Cdecl, SuppressGCTransition", "pointer");
        var renamed = PointerFingerprint("Cdecl, SuppressGCTransition", "renamed");
        var different = PointerFingerprint("Stdcall, SuppressGCTransition", "pointer");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Length.EqualTo(64));
            Assert.That(renamed, Is.EqualTo(first));
            Assert.That(different, Is.Not.EqualTo(first));
        }
    }

    private static string ContainerId(string modifier, string path)
    {
        System.ArgumentNullException.ThrowIfNull(modifier);
        System.ArgumentNullException.ThrowIfNull(path);
        var compilation = AnalyzerTestHost.CreateCompilation($"{modifier} class Subject {{ }}", [], filePath: path);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var type = compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
        return SemanticClaimIdentity.CreateContainerId(type);
    }

    private static string PointerFingerprint(string conventions, string parameter)
    {
        System.ArgumentNullException.ThrowIfNull(conventions);
        System.ArgumentNullException.ThrowIfNull(parameter);
        var compilation = AnalyzerTestHost.CreateCompilation(
            $$"""
            using SharpProof.Attributes;
            internal static unsafe class Subject {
                internal static void Identity(delegate* unmanaged[{{conventions}}]<int, int> {{parameter}}) {
                    Contract.Ensures({{parameter}} != null);
                }
            }
            """, []);
        compilation = compilation.WithOptions(compilation.Options.WithAllowUnsafe(true));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var syntax = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(syntax)!;
        return SemanticClaimIdentity.CreateInvocationFingerprint(invocation, method, method, usesCompanion: false);
    }
}
