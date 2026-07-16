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
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expectedState)));
    }

    [Test]
    public void CurrentStatementCompletion_MatchesStructuralCollector()
    {
        const string source =
            "static class C { static int M(int value) { value = 7; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(CurrentStatementCompletion_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<ExpressionStatementSyntax>().Single();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None,
                includeCurrentStatementCompletionFacts: true));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expected)));
    }

    [Test]
    public void LocalDeclarationCompletion_MatchesStructuralCollector()
    {
        const string source =
            "static class C { static int M(int[] values) { int value = values[0]; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(LocalDeclarationCompletion_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None,
                includeCurrentStatementCompletionFacts: true));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expected)));
    }

    [Test]
    public void MultiDeclaratorCompletion_MatchesStructuralCollector()
    {
        const string source =
            "static class C { static int M(int[] values) { int first = values[0], second = first + 2; return second; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(MultiDeclaratorCompletion_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None,
                includeCurrentStatementCompletionFacts: true));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expected)));
    }

    [TestCase(
        "static class C { static string M(string? input) { string value = input ?? throw new System.Exception(); return value; } }")]
    [TestCase(
        "static class C { static string M(string? input) { int first = 1; string value = input ?? throw new System.Exception(), copy = value; return copy; } }")]
    public void ThrowGuardedDeclarationCompletion_MatchesStructuralCollector(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ThrowGuardedDeclarationCompletion_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Last();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None,
                includeCurrentStatementCompletionFacts: true));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expected)));
    }

    [TestCase(
        "static class C { static string M(bool condition, string input) { string value = condition ? input : throw new System.Exception(); return value; } }")]
    [TestCase(
        "static class C { static string M(bool condition, string input) { string value = condition ? throw new System.Exception() : input; return value; } }")]
    public void ConditionalThrowGuardedDeclarationCompletion_MatchesStructuralCollector(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ConditionalThrowGuardedDeclarationCompletion_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None,
                includeCurrentStatementCompletionFacts: true));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expected)));
    }

    [Test]
    public void UnsupportedDeclarationCompletion_RemainsConservativeFallback()
    {
        const string source =
            "static class C { static int Get() => 1; static int M() { int value = Get(); return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(UnsupportedDeclarationCompletion_RemainsConservativeFallback));
        var site = fixture.Root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);

        Assert.That(result.IsUnsupported, Is.True, result.Provenance.Single().Detail);
    }

    [TestCase("static class C { static void M() { int value = 7; value = 9; } }")]
    [TestCase("static class C { static void M() { int value = 7; value = 9; return; } }")]
    [TestCase(
        "static class C { static void M(bool condition) { int value = 0; if (condition) value = 1; } }")]
    [TestCase(
        "static class C { static void M(bool condition) { int value = 0; if (condition) value = 1; else value = 2; } }")]
    [TestCase(
        "static class C { static void M(bool condition) { int value = 0; if (condition) value = 1; int marker = 2; } }")]
    [TestCase(
        "static class C { static void M(bool condition) { int value = 0; if (condition) value = 1; else value = 2; int marker = 3; } }")]
    [TestCase(
        "static class C { static void M(bool condition) { int value = 0; if (condition) value = 1; int marker = 2; marker = 3; } }")]
    [TestCase(
        "static class C { static void M(bool condition, bool nested) { int value = 0; if (condition) { if (nested) value = 1; } int marker = 2; } }")]
    [TestCase(
        "static class C { static void M(bool condition, bool nested) { int value = 0; if (condition) { if (nested) value = 1; else value = 2; } int marker = 3; } }")]
    [TestCase(
        "static class C { static void M(bool condition, bool nested, bool third) { int value = 0; if (condition) { if (nested) value = 1; else if (third) value = 2; } int marker = 3; } }")]
    [TestCase(
        "static class C { static void M(bool condition, bool nested) { int value = 0; if (condition) { if (nested) value = 1; int inner = 2; } int marker = 3; } }")]
    [TestCase(
        "static class C { static void M(bool first, bool second) { int value = 0; if (first) value = 1; int marker = 2; if (second) value = 3; int final = 4; } }")]
    [TestCase(
        "static class C { static void M(bool condition, bool nested) { int value = 0; if (condition) { if (nested) value = 1; else value = 2; int inner = 3; } int marker = 4; } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; if (condition) return 1; value = 2; return value; } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; if (condition) { value = 1; return value; } value = 2; return value; } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; if (condition) { value = 1; return value; } else { value = 2; return value; } } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; if (condition) throw new System.Exception(); value = 2; return value; } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; if (condition) { value = 1; throw new System.Exception(); } value = 2; return value; } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; if (condition) throw new System.Exception(); else return value; } }")]
    [TestCase(
        "static class C { static void M() { int value = 0; try { value = 1; } finally { value = 2; } } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; try { value = 1; } finally { if (condition) throw new System.Exception(); value = 2; } return value; } }")]
    public void RootBlockCompletion_MatchesStructuralCollector(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(RootBlockCompletion_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Body!;

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None,
                includeCurrentStatementCompletionFacts: true));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expected)));
    }

    [TestCase(
        "static class C { static void M(bool condition) { if (condition) { int value = 7; } } }")]
    [TestCase(
        "static class C { static void M(bool condition) { int value = 0; if (condition) { value = 7; } } }")]
    [TestCase(
        "static class C { static void M(bool condition, int[] values) { if (condition) { int value = values[0]; } } }")]
    [TestCase(
        "static class C { static void M(bool condition) { int value = 0; if (condition) { value = 1; value = 2; } } }")]
    [TestCase(
        "static class C { static void M(bool condition, int[] values) { if (condition) { int first = values[0]; int second = first + 1; } } }")]
    public void LinearNestedBlockCompletion_MatchesStructuralCollector(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(LinearNestedBlockCompletion_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<IfStatementSyntax>().Single().Statement;

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None,
                includeCurrentStatementCompletionFacts: true));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expected)));
    }

    [TestCase(
        "static class C { static void M(bool condition, bool nested) { int value = 0; if (condition) { if (nested) value = 1; int marker = 2; } } }")]
    [TestCase(
        "static class C { static void M(bool condition, bool nested) { int value = 0; if (condition) { if (nested) value = 1; else value = 2; int marker = 3; } } }")]
    [TestCase(
        "static class C { static void M(bool condition, bool nested) { int value = 0; if (condition) { if (nested) value = 1; } } }")]
    [TestCase(
        "static class C { static void M(bool condition, bool nested) { int value = 0; if (condition) { if (nested) value = 1; else value = 2; } } }")]
    [TestCase(
        "static class C { static void M(bool condition, bool nested) { int value = 0; if (condition) { if (nested) { value = 1; value = 2; } } } }")]
    public void InternallyBranchingNestedBlockCompletion_MatchesStructuralCollector(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(InternallyBranchingNestedBlockCompletion_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<IfStatementSyntax>().First().Statement;

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None,
                includeCurrentStatementCompletionFacts: true));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expected)));
    }

    [TestCase(
        "static class C { static int M(bool condition, bool nested) { if (condition) { if (nested) return 1; int marker = 2; } return 0; } }")]
    [TestCase(
        "static class C { static void M(bool condition, bool nested) { if (condition) { if (nested) nested = false; int marker = 2; } } }")]
    [TestCase(
        "static class C { static void M(bool condition) { int value = 0; if (condition) { System.Console.WriteLine(); value = 2; } } }")]
    public void ComplexNestedBlockCompletion_RemainsConservativeFallback(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ComplexNestedBlockCompletion_RemainsConservativeFallback));
        var site = fixture.Root.DescendantNodes().OfType<IfStatementSyntax>().First().Statement;

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);

        Assert.That(result.IsUnsupported, Is.True, result.Provenance.Single().Detail);
    }

    [TestCase(
        "static class C { static void M() { System.Console.WriteLine(); } }")]
    [TestCase(
        "static class C { static void M(bool condition) { int value = 0; while (condition) value = 1; } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; try { if (condition) throw new System.Exception(); value = 1; } catch { value = 2; } return value; } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; try { if (condition) return 1; value = 2; } finally { value = 3; } return value; } }")]
    public void UnsupportedRootBlockCompletion_RemainsConservativeFallback(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(UnsupportedRootBlockCompletion_RemainsConservativeFallback));
        var site = fixture.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Body!;

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);

        Assert.That(result.IsUnsupported, Is.True, result.Provenance.Single().Detail);
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
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expected)));
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

    private static string CreateEvidenceKey(SymbolicState state) =>
        string.Join(
            "\n",
            state.Facts.Concat(state.PathConditions.SelectMany(EnumerateFacts)).Select(static fact =>
                string.Join(
                    "|",
                    SymbolicState.CreateProofConditionKey(new SymbolicFactCondition(fact)),
                    fact.Provenance,
                    fact.SourceSpan.Start,
                    fact.SourceSpan.Length,
                    fact.Symbol?.ToDisplayString() ?? string.Empty,
                    fact.EvidenceKey ?? string.Empty)));

    private static IEnumerable<SymbolicFact> EnumerateFacts(SymbolicCondition condition)
    {
        switch (condition)
        {
            case SymbolicFactCondition fact:
                yield return fact.Fact;
                break;
            case SymbolicNotCondition not:
                foreach (var nested in EnumerateFacts(not.Operand))
                    yield return nested;
                break;
            case SymbolicBinaryCondition binary:
                foreach (var nested in EnumerateFacts(binary.Left))
                    yield return nested;
                foreach (var nested in EnumerateFacts(binary.Right))
                    yield return nested;
                break;
        }
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
    public void BranchLocalTargetWithScalarGuardMutation_RemainsConservativeFallback()
    {
        const string source = "static class C { static int M(int value) { if (value < 0) { value = 0; return value; } return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(BranchLocalTargetWithScalarGuardMutation_RemainsConservativeFallback));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().First();

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True, result.Provenance.Single().Detail);
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
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expected)));
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

    [Test]
    public void ConstructorPropertyAssignmentBeforeVoidReturn_RemainsConservativeFallback()
    {
        const string source = "sealed class C { string? Value { get; } C() { Value = null; return; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ConstructorPropertyAssignmentBeforeVoidReturn_RemainsConservativeFallback));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True, result.Provenance.Single().Detail);
    }

    [Test]
    public void CurrentInstanceMemberAssignmentBeforeReturn_MatchesStructuralCollector()
    {
        const string source =
            "sealed class C { int Value; int M() { Value = 7; return Value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(CurrentInstanceMemberAssignmentBeforeReturn_MatchesStructuralCollector));
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
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expected)));
    }

    [Test]
    public void ElementAssignmentBeforeReturn_MatchesStructuralCollector()
    {
        const string source =
            "static class C { static int M(int[] values) { values[0] = 7; return values[0]; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ElementAssignmentBeforeReturn_MatchesStructuralCollector));
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
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expected)));
    }

    [Test]
    public void ExternalMemberAssignment_RemainsConservativeFallback()
    {
        const string source =
            "sealed class C { public int Value; static int M(C instance) { instance.Value = 7; return 0; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ExternalMemberAssignment_RemainsConservativeFallback));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True, result.Provenance.Single().Detail);
    }
}
