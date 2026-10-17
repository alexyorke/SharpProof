using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class AnalyzerCorePublicDriverLanguageBoundaryTests
{
    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public async Task ActualPublicDriverRetainsConservativeLanguageBoundary(
        bool visualBasic, bool strict, bool concurrent)
    {
        var external = CSharpCompilation.Create("PublicDriverClosedPrecondition",
            [CSharpSyntaxTree.ParseText("""
                using SharpProof.Attributes;
                public static class Guard
                {
                    public static int Accept([Positive] int value) => value;
                }
                """)], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(external.GetDiagnostics().Where(static d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var externalImage = new MemoryStream();
        var externalEmit = external.Emit(externalImage);
        Assert.That(externalEmit.Success, Is.True, string.Join(Environment.NewLine, externalEmit.Diagnostics));
        var reference = MetadataReference.CreateFromImage(externalImage.ToArray());
        var references = TestMetadataReferences.WithSharpProof.Add(reference);
        Compilation compilation = visualBasic
            ? VisualBasicCompilation.Create("PublicDriverVisualBasicBoundary",
                [VisualBasicSyntaxTree.ParseText("""
                    Public Class Fixture
                        Public Shared Function Target() As Integer
                            Return Guard.Accept(1)
                        End Function
                    End Class
                    """, path: "input.vb")], references,
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            : CSharpCompilation.Create("PublicDriverCSharpControl",
                [CSharpSyntaxTree.ParseText("""
                    public class Fixture
                    {
                        public static int Target() { return Guard.Accept(1); }
                    }
                    """, path: "input.cs")], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(static d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));

        var method = compilation.GetTypeByMetadataName("Fixture")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var syntaxReference = method.DeclaringSyntaxReferences.Single();
        var declaration = await syntaxReference.GetSyntaxAsync().ConfigureAwait(false);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        Assert.That(syntaxReference.SyntaxTree, Is.SameAs(tree));
        Assert.That(model.Compilation, Is.SameAs(compilation));
        Assert.That(SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(declaration), method), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly), Is.True);
        var executable = visualBasic ? declaration.Parent! : declaration;
        Assert.That(declaration, visualBasic ? Is.InstanceOf<MethodStatementSyntax>() : Is.InstanceOf<MethodDeclarationSyntax>());
        var operation = model.GetOperation(executable);
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.Syntax.SyntaxTree, Is.SameAs(tree));
        Assert.That(operation.Syntax, Is.SameAs(executable));
        Assert.That(syntaxReference.Span.Contains(operation.Syntax.Span), Is.EqualTo(!visualBasic));

        var analyzer = new SharpProofAnalyzer();
        var exceptions = new ConcurrentQueue<string>();
        var options = new AnalyzerOptions([], new OptionsProvider(strict));
        var driver = compilation.WithAnalyzers([analyzer], new CompilationWithAnalyzersOptions(
            options, (exception, _, _) => { System.ArgumentNullException.ThrowIfNull(exception); exceptions.Enqueue(exception.ToString()); },
            concurrentAnalysis: concurrent, logAnalyzerExecutionTime: true,
            reportSuppressedDiagnostics: false));
        Assert.That(driver.Analyzers.Single(), Is.SameAs(analyzer));
        var diagnostics = await driver.GetAnalyzerDiagnosticsAsync();
        Assert.That(exceptions, Is.Empty);
        Assert.That(diagnostics, Is.Empty);
        var telemetry = await driver.GetAnalyzerTelemetryInfoAsync(analyzer, TestContext.CurrentContext.CancellationToken);
        Assert.That(telemetry.CompilationStartActionsCount, Is.EqualTo(1));
        Assert.That(telemetry.OperationBlockActionsCount, Is.EqualTo(1));
    }

    private sealed class OptionsProvider(bool strict) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _options = new DictionaryOptions(strict);
        public override AnalyzerConfigOptions GlobalOptions => _options;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            System.ArgumentNullException.ThrowIfNull(tree);
            return _options;
        }
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            System.ArgumentNullException.ThrowIfNull(textFile);
            return _options;
        }
    }

    private sealed class DictionaryOptions(bool strict) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (key == "build_property.SharpProofProfile")
            {
                value = strict ? "strict" : "advisory";
                return true;
            }
            value = string.Empty;
            return false;
        }
    }
}
