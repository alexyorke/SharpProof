using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicLoopTransferLowererTests
{
    [Test]
    public void WhileLoop_LowersConditionsInvalidationAndInvariant()
    {
        const string source = "static class C { static int M(bool keepGoing) { int value = 0; while (keepGoing) { value++; } return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(WhileLoop_LowersConditionsInvalidationAndInvariant));
        var loop = fixture.Root.DescendantNodes().OfType<WhileStatementSyntax>().Single();
        var value = fixture.Root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var valueSymbol = fixture.SemanticModel.GetDeclaredSymbol(value)!;

        var result = SymbolicLoopTransferLowerer.Lower(loop, fixture.SemanticModel, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsExact, Is.True, result.Provenance.Single().Detail);
            Assert.That(result.Value!.EntryCondition, Is.Not.Null);
            Assert.That(result.Value.ExitCondition, Is.Not.Null);
            Assert.That(result.Value.BackEdgeInvalidations.Select(static target => target.Key),
                Does.Contain(SymbolicFactFactory.GetSmtVariableName(valueSymbol)));
            Assert.That(result.Value.Invariants, Is.Not.Empty);
        });
    }

    [Test]
    public void ForLoop_ExcludesOneTimeInitializerFromBackEdgeInvalidation()
    {
        const string source = "static class C { static int M() { int value = 0; for (int index = 0; index < 3; index++) value += index; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(ForLoop_ExcludesOneTimeInitializerFromBackEdgeInvalidation));
        var loop = fixture.Root.DescendantNodes().OfType<ForStatementSyntax>().Single();
        var symbols = fixture.Root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .ToDictionary(node => node.Identifier.ValueText, node => fixture.SemanticModel.GetDeclaredSymbol(node)!);

        var result = SymbolicLoopTransferLowerer.Lower(loop, fixture.SemanticModel, CancellationToken.None);

        Assert.That(result.IsExact, Is.True, result.Provenance.Single().Detail);
        Assert.That(result.Value!.BackEdgeInvalidations.Select(static target => target.Key), Is.EquivalentTo(new[]
        {
            SymbolicFactFactory.GetSmtVariableName(symbols["index"]),
            SymbolicFactFactory.GetSmtVariableName(symbols["value"])
        }));
    }

    [Test]
    public void ForeachLoop_RemainsUnsupportedUntilFiniteDomainLoweringMigrates()
    {
        const string source = "static class C { static int M(int[] values) { int total = 0; foreach (var value in values) total += value; return total; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(ForeachLoop_RemainsUnsupportedUntilFiniteDomainLoweringMigrates));
        var loop = fixture.Root.DescendantNodes().OfType<ForEachStatementSyntax>().Single();

        var result = SymbolicLoopTransferLowerer.Lower(loop, fixture.SemanticModel, CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True);
    }

    [Test]
    public void AbruptLoopCompletion_RemainsUnsupportedUntilExitSummariesMigrate()
    {
        const string source = "static class C { static int M(bool stop) { int value = 0; while (true) { if (stop) break; value++; } return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(AbruptLoopCompletion_RemainsUnsupportedUntilExitSummariesMigrate));
        var loop = fixture.Root.DescendantNodes().OfType<WhileStatementSyntax>().Single();

        var result = SymbolicLoopTransferLowerer.Lower(loop, fixture.SemanticModel, CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True);
    }
}
