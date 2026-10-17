using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class EntryPointBodyOwnershipRecoveryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void NamedMainCannotOwnTheCompilerProducedTopLevelBody(bool selectNamedMain)
    {
        var compilation = TestCompilation.Create("EntryPointBodyOwnershipRecovery", """
            using SharpProof.Attributes;
            Contract.Ensures(true);
            public static class Other {
                public static void Main() { }
            }
            """, outputKind: OutputKind.ConsoleApplication);
        if (selectNamedMain)
        {
            compilation = compilation.WithOptions(compilation.Options.WithMainTypeName("Other"));
        }
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (selectNamedMain)
        {
            Assert.That(errors, Has.Length.EqualTo(1));
            Assert.That(errors.Single().Id, Is.EqualTo("CS8804"));
        }
        else
        {
            Assert.That(errors, Is.Empty);
            using var image = new MemoryStream();
            Assert.That(compilation.Emit(image).Success, Is.True);
        }

        var tree = compilation.SyntaxTrees.Single();
        var root = (CompilationUnitSyntax)tree.GetRoot();
        var model = compilation.GetSemanticModel(tree);
        var topLevel = (IMethodSymbol)model.GetEnclosingSymbol(
            root.Members.OfType<GlobalStatementSyntax>().Single().SpanStart)!;
        var namedMain = compilation.GetTypeByMetadataName("Other")!
            .GetMembers("Main").OfType<IMethodSymbol>().Single();
        var body = model.GetOperation(root);
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(body.Syntax.SyntaxTree, Is.SameAs(tree));
        Assert.That(body.Syntax.Span, Is.EqualTo(root.Span));
        Assert.That(topLevel.ContainingAssembly, Is.SameAs(compilation.Assembly));
        Assert.That(namedMain.ContainingAssembly, Is.SameAs(compilation.Assembly));
        Assert.That(SymbolEqualityComparer.Default.Equals(topLevel, namedMain), Is.False);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            compilation.GetEntryPoint(TestContext.CurrentContext.CancellationToken), selectNamedMain ? namedMain : topLevel), Is.True);
        Assert.That(topLevel.DeclaringSyntaxReferences.Single().GetSyntax(), Is.TypeOf<CompilationUnitSyntax>());
        Assert.That(namedMain.DeclaringSyntaxReferences.Single().GetSyntax(), Is.TypeOf<MethodDeclarationSyntax>());

        var builder = new ContractClauseInventoryBuilder(compilation);
        var ownTopLevel = builder.Create(topLevel, body);
        Assert.That(ownTopLevel.HasRejectedContractApiUsage, Is.False);
        Assert.That(ownTopLevel.ImplementationBody, Is.SameAs(body));
        Assert.That(ownTopLevel.Clauses, Has.Length.EqualTo(1));
        Assert.That(ownTopLevel.Clauses.Single().Placement, Is.EqualTo(ContractClausePlacement.ValidPrologue));
        var ownMain = builder.Create(namedMain);
        Assert.That(ownMain.HasRejectedContractApiUsage, Is.False);
        Assert.That(ownMain.ImplementationBody, Is.Not.Null);
        Assert.That(ownMain.Clauses, Is.Empty);

        var wrongMain = builder.Create(namedMain, body);
        Assert.That(wrongMain.HasRejectedContractApiUsage, Is.True);
        Assert.That(wrongMain.ImplementationBody, Is.Null);
        Assert.That(wrongMain.Clauses, Is.Empty);
        var bound = new ContractBinder(compilation, new IrFactory()).Bind(namedMain, body);
        Assert.That(bound.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedTarget));
    }
}
