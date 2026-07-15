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
        ("static class C { static int M() { int value = 0; value++; return value; } }", "return value"),
        ("static class C { static int M() { int value = 4; value += 2; return value; } }", "return value"),
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

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
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
    public void ConditionalControlFlow_MatchesStructuralCollector()
    {
        const string source = "static class C { static int M(bool condition) { int value = 0; if (condition) value = 1; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ConditionalControlFlow_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
    }

    [Test]
    public void MutatedBranchGuard_MatchesConservativeStructuralMerge()
    {
        const string source = "static class C { static int M(int value) { if (value > 0) value = 0; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(MutatedBranchGuard_MatchesConservativeStructuralMerge));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
    }

    [Test]
    public void BranchLocalTarget_RemainsConservativeFallback()
    {
        const string source = "static class C { static string? M(string? value) { if (value is null) { var copy = value; value = \"fallback\"; return copy; } return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(BranchLocalTarget_RemainsConservativeFallback));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().First();

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True);
    }

    [Test]
    public void BranchLocalTargetWithoutGuardMutation_MatchesStructuralCollector()
    {
        const string source = "static class C { static string? M(string? value) { if (value is null) { var copy = value; return copy; } return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(BranchLocalTargetWithoutGuardMutation_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().First();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
    }

    [Test]
    public void SingleSurvivingBranch_MatchesStructuralCompletionState()
    {
        const string source = "static class C { static int M(bool stop) { if (stop) return 0; int value = 2; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(SingleSurvivingBranch_MatchesStructuralCompletionState));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Last();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
    }

    [Test]
    public void AllPathsComplete_MatchesStructuralUnreachableState()
    {
        const string source = "static class C { static int M(bool stop) { int value = 0; if (stop) return 1; else return 2; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(AllPathsComplete_MatchesStructuralUnreachableState));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Last();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
        Assert.That(actual.Value.IsContradictory, Is.True);
    }

    [Test]
    public void LoopConditionMutation_RemainsConservativeFallback()
    {
        const string source = "static class C { static int M(int count) { int value = 0; while (count-- > 0) value = 1; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(LoopConditionMutation_RemainsConservativeFallback));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True);
    }

    [Test]
    public void WhileLoopAfterState_MatchesStructuralCollector()
    {
        const string source = "static class C { static int M(bool keepGoing) { int value = 0; while (keepGoing) value = 1; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(WhileLoopAfterState_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
    }

    [Test]
    public void ForLoopAfterState_MatchesStructuralCollector()
    {
        const string source = "static class C { static int M() { int value = 0; for (int index = 0; index < 3; index++) value = index; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(ForLoopAfterState_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
    }

    [Test]
    public void DoLoopAfterState_MatchesStructuralCollector()
    {
        const string source = "static class C { static int M(bool keepGoing) { int value = 0; do value = 1; while (keepGoing); return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(DoLoopAfterState_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
    }

    [TestCase(
        "static class C { static int M(int? input) { int? value = null; value = input; return value.Value; } }")]
    [TestCase(
        "static class C { static int M(bool flag) { var values = new int[1]; if (flag) values = new int[2]; return values[1]; } }")]
    public void AssignmentShapesWithoutStateParity_RemainConservativeFallback(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(AssignmentShapesWithoutStateParity_RemainConservativeFallback));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True);
    }

    [Test]
    public void FinallyContinuationState_MatchesStructuralCollector()
    {
        const string source = "static class C { static int M() { int value = 0; try { value = 1; } finally { value = 2; } return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(FinallyContinuationState_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
    }
}
