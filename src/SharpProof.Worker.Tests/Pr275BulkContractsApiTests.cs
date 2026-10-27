using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275BulkContractsApiTests
{
    [Test]
    public void MaintainedBinderAndInventoryEntryPointsRejectMissingCallables()
    {
        var compilation = CreateCompilation(
            "Pr275BulkContractsApiRequiredInputs",
            "internal static class Subject { internal static void Target() { } }");
        var inventory = new ContractClauseInventoryBuilder(compilation);
        var binder = new ContractBinder(compilation, new IrFactory(), inventory);

        AssertMissingCallable("bind", "target", () => _ = binder.Bind(null!));
        AssertMissingCallable("requires", "target", () => _ = binder.BindRequires(null!));
        AssertMissingCallable("inventory query", "target", () => _ = binder.GetClauseInventory(null!));
        AssertMissingCallable("inventory create", "callable", () => _ = inventory.Create(null!));
    }

    [TestCase("property", "17")]
    [TestCase("indexer", "index")]
    public void InventoryCreateUsesTheOwnedExpressionBodyOfARealAccessor(
        string memberKind,
        string expectedBody)
    {
        System.ArgumentNullException.ThrowIfNull(expectedBody);
        System.ArgumentNullException.ThrowIfNull(memberKind);
        var compilation = CreateCompilation(
            "Pr275BulkContractsApiAccessor",
            """
            internal sealed class Subject {
                internal int Value => 17;
                internal int this[int index] => index;
            }
            """);
        var type = compilation.GetTypeByMetadataName("Subject")!;
        var property = memberKind == "property"
            ? type.GetMembers("Value").OfType<IPropertySymbol>().Single()
            : type.GetMembers().OfType<IPropertySymbol>().Single(static candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.IsIndexer));
        var accessor = property.GetMethod!;
        var declaration = property.DeclaringSyntaxReferences.Single().GetSyntax();
        var expectedExpression = memberKind == "property"
            ? ((PropertyDeclarationSyntax)declaration).ExpressionBody!.Expression
            : ((IndexerDeclarationSyntax)declaration).ExpressionBody!.Expression;
        var body = ContractClauseInventoryBuilder.GetBody(declaration);
        var inventory = new ContractClauseInventoryBuilder(compilation).Create(accessor);

        Assert.That(body, Is.SameAs(expectedExpression));
        Assert.That(body!.ToString(), Is.EqualTo(expectedBody));
        Assert.That(SymbolEqualityComparer.Default.Equals(inventory.Callable, accessor), Is.True);
        Assert.That(inventory.ContractApiAvailable, Is.True);
        Assert.That(inventory.ImplementationBody, Is.Not.Null);
        Assert.That(inventory.ImplementationBody!.Syntax.ToString(), Is.EqualTo(expectedBody));
        Assert.That(inventory.Clauses, Is.Empty);
        Assert.That(inventory.HasPlacementErrors, Is.False);
    }

    private static CSharpCompilation CreateCompilation(string assemblyPrefix, string source)
    {
        System.ArgumentNullException.ThrowIfNull(assemblyPrefix);
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create(assemblyPrefix, source);
        TestCompilation.AssertNoErrors(compilation);
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.That(emitted.Diagnostics.Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        Assert.That(emitted.Success, Is.True);
        return compilation;
    }

    private static void AssertMissingCallable(
        string api,
        string expectedParameter,
        Action action)
    {
        System.ArgumentNullException.ThrowIfNull(action);
        System.ArgumentNullException.ThrowIfNull(api);
        System.ArgumentNullException.ThrowIfNull(expectedParameter);
        var error = Assert.Throws<ArgumentNullException>((Action)(() => action()),
            $"{api} must reject a missing callable.");
        Assert.That(error!.ParamName, Is.EqualTo(expectedParameter));
    }
}
