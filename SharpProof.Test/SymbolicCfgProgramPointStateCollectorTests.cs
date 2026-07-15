using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicCfgProgramPointStateCollectorTests
{
    private static readonly (string Source, string Target)[] StraightLineCases =
    {
        ("static class C { static int M(int input) { int value = input; return value; } }", "return value"),
        ("static class C { static int M(int input) { int value = 0; value = input + 1; return value; } }", "return value"),
        ("static class C { static bool M(bool input) { bool value = input; return value; } }", "return value"),
        ("static class C { static string? M(string? input) { string? value = input; return value; } }", "return value")
    };

    [TestCaseSource(nameof(StraightLineCases))]
    public void StraightLineState_MatchesStructuralCollector((string Source, string Target) testCase)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            testCase.Source,
            nameof(StraightLineState_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Single(statement => statement.ToString().StartsWith(testCase.Target, StringComparison.Ordinal));

        var actual = SymbolicCfgProgramPointStateCollector.CollectStraightLineState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var expectedState = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expectedState.NormalizedProofKey));
    }

    [Test]
    public void ConditionalControlFlow_RemainsConservativeFallback()
    {
        const string source = "static class C { static int M(bool condition) { int value = 0; if (condition) value = 1; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ConditionalControlFlow_RemainsConservativeFallback));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();

        var result = SymbolicCfgProgramPointStateCollector.CollectStraightLineState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True);
    }
}
