using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ResourceBoundsDiagnosticBoundaryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PublicSdkContextAdmissionRequiresADeclaredMethod(bool speculative)
    {
        var token = TestContext.CurrentContext.CancellationToken;
        var compilation = AnalyzerTestHost.CreateCompilation(
            "public static class C { public static void Target(int n) { var value=new int[n]; } }", ["SP0060"]);
        var tree = compilation.SyntaxTrees.Single();
        var original = tree.GetRoot(token).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var originalModel = compilation.GetSemanticModel(tree);
        var symbol = originalModel.GetDeclaredSymbol(original, token);
        Assert.That(symbol, Is.Not.Null);
        var declaration = original;
        SemanticModel model = originalModel;
        if (speculative)
        {
            declaration = original.WithBody((BlockSyntax)SyntaxFactory.ParseStatement("{ var replacement=new int[n]; }"));
            Assert.That(originalModel.TryGetSpeculativeSemanticModelForMethodBody(
                original.Body!.Statements[0].SpanStart, declaration, out var replacementModel), Is.True);
            model = replacementModel!;
        }
        Assert.That(model.IsSpeculativeSemanticModel, Is.EqualTo(speculative));
        Assert.That(model.Compilation, Is.SameAs(compilation));
        Assert.That(compilation.ContainsSyntaxTree(model.SyntaxTree), Is.EqualTo(!speculative));
        Assert.That(model.GetDeclaredSymbol(declaration, token), speculative ? Is.Null : Is.SameAs(symbol));
        Assert.That(model.GetOperation(declaration.Body!, token), Is.Not.Null);

        // This exercises public SDK context admission. CompilationWithAnalyzers
        // rejects the speculative tree, so this is not an editor driver callback.
        var options = new AnalyzerOptions([]);
        var registration = new ResourceRegistration(compilation, options, token);
        ResourceBoundsDiagnostics.Register(registration);
        var reports = new List<Diagnostic>();
        // Roslyn 4.14 has only obsolete public context constructors. Its supported
        // driver API cannot admit the genuine speculative model tested here.
#pragma warning disable CS0618
        var context = new SyntaxNodeAnalysisContext(declaration, symbol, model, options,
            reports.Add, static diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) :
                diagnostic.Id == "SP0060", token);
#pragma warning restore CS0618
        Assert.That(registration.MethodAction, Is.Not.Null);
        registration.MethodAction!(context);
        if (speculative)
        { Assert.That(reports, Is.Empty); }
        else
        {
            Assert.That(reports, Has.Count.EqualTo(1));
            Assert.That(reports[0].Id, Is.EqualTo("SP0060"));
            Assert.That(reports[0].Properties["ResourceStatus"], Is.EqualTo("Proven"));
            Assert.That(reports[0].Properties["ArrayAllocationsUpperBound"], Is.EqualTo("1"));
            Assert.That(reports[0].Properties["AllocatedElementsUpperBound"], Is.EqualTo("max(0, n)"));
        }
    }

    private sealed class ResourceRegistration(Compilation compilation, AnalyzerOptions options, CancellationToken token)
        : CompilationStartAnalysisContext(compilation ?? throw new ArgumentNullException(nameof(compilation)),
            options ?? throw new ArgumentNullException(nameof(options)), token)
    {
        internal Action<SyntaxNodeAnalysisContext>? MethodAction { get; private set; }

        public override void RegisterSyntaxNodeAction<TLanguageKindEnum>(Action<SyntaxNodeAnalysisContext> action,
            ImmutableArray<TLanguageKindEnum> syntaxKinds)
        {
            ArgumentNullException.ThrowIfNull(action);
            Assert.That(syntaxKinds.IsDefault, Is.False);
            Assert.That(syntaxKinds.Cast<object>(), Is.EqualTo(new object[] { SyntaxKind.MethodDeclaration }));
            MethodAction = action;
        }

        public override void RegisterCompilationEndAction(Action<CompilationAnalysisContext> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            throw new InvalidOperationException("Only the resource syntax-node action is expected.");
        }

        public override void RegisterSemanticModelAction(Action<SemanticModelAnalysisContext> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            throw new InvalidOperationException("Only the resource syntax-node action is expected.");
        }

        public override void RegisterSymbolAction(Action<SymbolAnalysisContext> action, ImmutableArray<SymbolKind> symbolKinds)
        {
            ArgumentNullException.ThrowIfNull(action);
            throw new InvalidOperationException("Only the resource syntax-node action is expected.");
        }

        public override void RegisterCodeBlockStartAction<TLanguageKindEnum>(Action<CodeBlockStartAnalysisContext<TLanguageKindEnum>> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            throw new InvalidOperationException("Only the resource syntax-node action is expected.");
        }

        public override void RegisterCodeBlockAction(Action<CodeBlockAnalysisContext> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            throw new InvalidOperationException("Only the resource syntax-node action is expected.");
        }

        public override void RegisterSyntaxTreeAction(Action<SyntaxTreeAnalysisContext> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            throw new InvalidOperationException("Only the resource syntax-node action is expected.");
        }
    }
}
