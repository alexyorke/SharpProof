using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerSemanticIdentityWorkflowTests
{
    [Test]
    public void NestedPredicateTypeParametersUseEnclosingRolesAcrossRenames()
    {
        var first = Fingerprint("Left", "Right", "Left");
        var renamed = Fingerprint("RenamedLeft", "RenamedRight", "RenamedLeft");
        var second = Fingerprint("Left", "Right", "Right");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(renamed, Is.EqualTo(first));
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(first, Has.Length.EqualTo(64));
        }
    }

    private static string Fingerprint(string firstType, string secondType, string operand)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            $$"""
            using SharpProof.Attributes;
            internal static class Subject {
                internal static void Outer<{{firstType}}, {{secondType}}>() {
                    void Local() {
                        Contract.Ensures(typeof({{operand}}) != null);
                    }
                    Local();
                }
            }
            """, []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var local = tree.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single();
        var method = model.GetDeclaredSymbol(local)!;
        var syntax = local.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(syntax)!;
        Assert.That(invocation.TargetMethod.Name, Is.EqualTo("Ensures"));
        return SemanticClaimIdentity.CreateInvocationFingerprint(invocation, method, method,
            usesCompanion: false);
    }
}
