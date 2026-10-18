using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.VisualBasic;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class AnalyzerCoreAdvisoryLanguageBoundaryTests
{
    private static readonly ImmutableArray<string> ExpectedIds = ["SP0027"];

    [TestCase(false)]
    [TestCase(true)]
    public void RealCompilationLanguageControlsTheGenericAdvisoryBoundary(bool visualBasic)
    {
        var supported = CreateSupportedCompilation(
            "using SharpProof.Attributes; public static class Fixture { " +
            "private static int Need(int value) { Contract.Requires(value > 0); return value; } " +
            "public static int Target() { return Need(0); } }");
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
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));

        var method = compilation.GetTypeByMetadataName("Fixture")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly), Is.True);
        var declaration = method.DeclaringSyntaxReferences.Single().GetSyntax();
        Assert.That(compilation.ContainsSyntaxTree(declaration.SyntaxTree), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration), method), Is.True);
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, TestContext.CurrentContext.CancellationToken);
        Assert.That(session.Compilation, Is.SameAs(compilation));
        var diagnostics = new List<Diagnostic>();
        var outcome = AdvisoryRequiresDiagnostics.Analyze(method, declaration, session, diagnostics.Add, TestContext.CurrentContext.CancellationToken);

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
            Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)), Is.EqualTo(ExpectedIds));
            var call = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            Assert.That(diagnostics.Single().Location.SourceTree, Is.SameAs(declaration.SyntaxTree));
            Assert.That(diagnostics.Single().Location.SourceSpan, Is.EqualTo(call.Span));
            Assert.That(diagnostics.Single().GetMessage(CultureInfo.InvariantCulture), Does.Contain("Need"));
            Assert.That(diagnostics.Single().GetMessage(CultureInfo.InvariantCulture), Does.Contain("value > 0"));
        }
    }

    private static CSharpCompilation CreateSupportedCompilation(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithSpecificDiagnosticOptions(new SharpProofAnalyzer().SupportedDiagnostics.ToImmutableDictionary(
                static descriptor => descriptor is null ? throw new System.ArgumentNullException(nameof(descriptor)) : (descriptor.Id),
                static descriptor => descriptor is null ? throw new System.ArgumentNullException(nameof(descriptor)) : (descriptor.Id == "SP0027" ? ReportDiagnostic.Warn : ReportDiagnostic.Suppress),
                StringComparer.Ordinal));
        return CSharpCompilation.Create("AnalyzerFixture",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview), "input.cs")],
            TestMetadataReferences.WithSharpProof, options);
    }
}
