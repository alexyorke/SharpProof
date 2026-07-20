using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicFiniteDomainLowererTests
{
    private static readonly (string Source, int ConditionCount)[] Cases =
    {
        ("static class C { static int M() { int total = 0; foreach (var value in new[] { 1, 2 }) total += value; return total; } }", 1),
        ("static class C { static int M() { int total = 0; foreach (var value in new[] { \"a\", \"b\" }) total += value.Length; return total; } }", 1),
        ("static class C { static int M() { var values = new[] { 1, 2 }; int total = 0; foreach (var value in values) total += value; return total; } }", 1)
    };

    [TestCaseSource(nameof(Cases))]
    public void FiniteForeach_LowersTypedDomain((string Source, int ConditionCount) testCase)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            testCase.Source,
            nameof(FiniteForeach_LowersTypedDomain));
        var loop = fixture.Root.DescendantNodes().OfType<ForEachStatementSyntax>().Single();

        var result = SymbolicFiniteDomainLowerer.LowerForeachDomain(
            loop.Expression,
            loop,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsExact, Is.True, result.Provenance.Single().Detail);
        Assert.That(result.Value, Has.Count.EqualTo(testCase.ConditionCount));
    }
}
