using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreNestedSyntaxDomainTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualCallableDeclarationsRetainWholeSyntaxPolicyDomain(bool localDeclaration)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;
            public static class Fixture {
                [EnforcePure] public static int Target() => 1;
                [SharpProofSuppress("reviewed")] public static int Ordinary() => 1;
                public static int Host() {
                    [SharpProofSuppress("reviewed")] static int Local() => 1;
                    return Local();
                }
            }
            """, ["SP0002", "SP0024", "SP0047"]);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "effects", analyzer: new SharpProofAnalyzer(factory));
        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.EqualTo(1));
        Assert.That(factory.Session, Is.Not.Null);
        Assert.That(factory.GetNamedOutcome("Target"), Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        var session = factory.Session!;
        var actual = session.Compilation;
        var tree = actual.SyntaxTrees.Single();
        var root = await tree.GetRootAsync().ConfigureAwait(false);
        SyntaxNode declaration = localDeclaration
            ? root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single()
            : root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == "Ordinary");
        var model = actual.GetSemanticModel(tree);
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        Assert.That(method.MethodKind, Is.EqualTo(localDeclaration ? MethodKind.LocalFunction : MethodKind.Ordinary));
        Assert.That(method.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, actual.Assembly), Is.True);
        var attribute = declaration.DescendantNodes().OfType<AttributeSyntax>().Single();
        var constructor = model.GetSymbolInfo(attribute).Symbol as IMethodSymbol;
        Assert.That(constructor, Is.Not.Null);
        Assert.That(constructor!.ContainingType.Name, Is.EqualTo("SharpProofSuppressAttribute"));
        var reason = model.GetConstantValue(attribute.ArgumentList!.Arguments.Single().Expression);
        Assert.That(reason.HasValue, Is.True);
        Assert.That(reason.Value, Is.EqualTo("reviewed"));

        var reported = new List<Diagnostic>();
        SharpProofControlAttributePolicy.ValidateNestedCallableDeclaration(
            declaration, model, session, reported.Add, TestContext.CurrentContext.CancellationToken);
        Assert.That(reported, Is.Empty);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Action validateCancelled = () => SharpProofControlAttributePolicy.ValidateNestedCallableDeclaration(
            declaration, model, session, reported.Add, cancellation.Token);
        if (localDeclaration)
        {
            var cancelled = Assert.Throws<OperationCanceledException>(validateCancelled);
            Assert.That(cancelled!.CancellationToken, Is.EqualTo(cancellation.Token));
        }
        else
        {
            Assert.DoesNotThrow(validateCancelled);
        }
        Assert.That(reported, Is.Empty);
        Assert.That(factory.GetNamedOutcome("Target"), Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }
}
