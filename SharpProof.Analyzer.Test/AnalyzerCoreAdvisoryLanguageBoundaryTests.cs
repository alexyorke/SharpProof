using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.VisualBasic;
using NUnit.Framework;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreAdvisoryLanguageBoundaryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void RealCompilationLanguageControlsTheGenericAdvisoryBoundary(bool visualBasic)
    {
        var supported = AnalyzerTestHost.CreateCompilation(
            "using SharpProof.Attributes; public static class Fixture { " +
            "private static int Need(int value) { Contract.Requires(value > 0); return value; } " +
            "public static int Target() { return Need(0); } }", ["SP0027"]);
        Compilation compilation = visualBasic
            ? VisualBasicCompilation.Create("VisualBasicAdvisoryBoundary",
                [VisualBasicSyntaxTree.ParseText("""
                    Imports SharpProof.Attributes
                    Public Class Fixture
                        Private Shared Function Need(value As Integer) As Integer
                            Contract.Requires(value > 0)
                            Return value
                        End Function
                        Public Shared Function Target() As Integer
                            Return Need(0)
                        End Function
                    End Class
                    """, path: "input.vb")],
                supported.References, new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            : supported;
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));

        var method = compilation.GetTypeByMetadataName("Fixture")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly), Is.True);
        var declaration = method.DeclaringSyntaxReferences.Single().GetSyntax();
        Assert.That(compilation.ContainsSyntaxTree(declaration.SyntaxTree), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration), method), Is.True);
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, CancellationToken.None);
        Assert.That(session.Compilation, Is.SameAs(compilation));
        var diagnostics = new List<Diagnostic>();
        var outcome = AdvisoryRequiresDiagnostics.Analyze(method, declaration, session, diagnostics.Add, CancellationToken.None);

        if (visualBasic)
        {
            Assert.That(compilation.Language, Is.EqualTo(LanguageNames.VisualBasic));
            Assert.That(outcome, Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
            Assert.That(diagnostics, Is.Empty);
        }
        else
        {
            Assert.That(compilation.Language, Is.EqualTo(LanguageNames.CSharp));
            Assert.That(outcome, Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            AnalyzerTestHost.AssertIds(diagnostics, "SP0027");
            var call = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            Assert.That(diagnostics.Single().Location.SourceTree, Is.SameAs(declaration.SyntaxTree));
            Assert.That(diagnostics.Single().Location.SourceSpan, Is.EqualTo(call.Span));
            AnalyzerTestHost.AssertMessageContains(diagnostics.Single(), "Need");
            AnalyzerTestHost.AssertMessageContains(diagnostics.Single(), "value > 0");
        }
    }
}
