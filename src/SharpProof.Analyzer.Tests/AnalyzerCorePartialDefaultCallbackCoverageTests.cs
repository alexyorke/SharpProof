using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCorePartialDefaultCallbackCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task UnimplementedPartialRetainsOrdinaryDefaultInitializerAdmission(bool hasDefault)
    {
        var parameter = hasDefault ? "int value = 1" : "int value";
        var source = "using SharpProof.Attributes; public static partial class Fixture { " +
            "static partial void Missing(" + parameter + "); " +
            "[EnforcePure] public static int Selected() { Missing(1); return 1; } }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0002"]);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "effects", analyzer: new SharpProofAnalyzer(factory));
        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.Session, Is.Not.Null);
        var actual = factory.Session!.Compilation;
        var root = await actual.SyntaxTrees.Single().GetRootAsync().ConfigureAwait(false);
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Missing");
        var model = actual.GetSemanticModel(declaration.SyntaxTree);
        var missing = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(missing.ContainingAssembly, actual.Assembly), Is.True);
        Assert.That(missing.IsPartialDefinition, Is.True);
        Assert.That(missing.PartialImplementationPart, Is.Null);
        Assert.That(missing.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        Assert.That(missing.Parameters.Single().HasExplicitDefaultValue, Is.EqualTo(hasDefault));
        var syntax = declaration.ParameterList.Parameters.Single();
        if (hasDefault)
        {
            Assert.That(syntax.Default, Is.Not.Null);
            Assert.That(missing.Parameters.Single().ExplicitDefaultValue, Is.EqualTo(1));
            var initializer = model.GetOperation(syntax.Default!);
            Assert.That(initializer, Is.Not.Null);
            Assert.That(initializer!.Kind, Is.EqualTo(OperationKind.ParameterInitializer));
        }
        else
        {
            Assert.That(syntax.Default, Is.Null);
        }
        Assert.That(factory.Outcomes.ContainsKey(missing.Name), Is.False);
        Assert.That(factory.GetNamedOutcome("Selected"), Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }
}
