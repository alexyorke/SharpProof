using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicCfgProgramPointStateCollectorTests
{
    private const string MemberNotNullCompletionSource = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

sealed class C
{
    private string? _value;

    [MemberNotNull(nameof(_value))]
    private void EnsureValue() => _value = string.Empty;

    private void M() => EnsureValue();
}";

    public enum SeedKind
    {
        Numeric,
        ReferenceNull,
        ReferenceNotNull,
        NullableValue
    }

    private static readonly (string Source, string Target)[] StraightLineCases =
    {
        ("static class C { static int M(int input) { int value = input; return value; } }", "return value"),
        ("static class C { static int M(int input) { int value = 0; value = input + 1; return value; } }", "return value"),
        ("static class C { static int M() { int value = 0; value++; return value; } }", "return value"),
        ("static class C { static int M() { int value = 4; value += 2; return value; } }", "return value"),
        ("static class C { static bool M(bool input) { bool value = input; return value; } }", "return value"),
        ("static class C { static string? M(string? input) { string? value = input; return value; } }", "return value")
    };

    private static readonly string[] ForInitialEntryCases =
    {
        "static class C { static void M() { for (int index = 0; index < 3; index++) { } } }",
        "static class C { static void M(int index) { for (index = 0; index < 3; index++) { } } }",
        "static class C { static void M() { for (int first = 1, second = first + 1; second < 3; second++) { } } }",
        "static class C { static void M(int[] values) { for (int value = values[0]; value < 3;) { } } }"
    };

    private static readonly (string Source, string Target, string Parameter, SeedKind Kind, bool SeedSurvives)[]
        SeededPathCases =
        {
            ("static class C { static int M(int input, bool condition) { if (condition) return input; return 0; } }",
                "return input;", "input", SeedKind.Numeric, true),
            ("static class C { static int M(string? value) { if (value == null) return 0; return value.Length; } }",
                "return 0;", "value", SeedKind.ReferenceNull, true),
            ("static class C { static int M(string? value) { if (value != null) return value.Length; return 0; } }",
                "return value.Length;", "value", SeedKind.ReferenceNotNull, true),
            ("static class C { static int M(int? value) { if (value.HasValue) return value.Value; return 0; } }",
                "return value.Value;", "value", SeedKind.NullableValue, true),
            ("static class C { static int M(int input, bool condition) { int copy; if (condition) copy = input; else copy = input; return copy; } }",
                "return copy;", "input", SeedKind.Numeric, true),
            ("static class C { static int M(int input) { input = 9; return input; } }",
                "return input;", "input", SeedKind.Numeric, false)
        };

    private static readonly string[] FinallyLocalFallbackCases =
    {
        "static class C { static int M(bool condition) { int value = 0; try { if (condition) return 1; value = 2; } finally { int marker = value; } return value; } }",
        "static class C { static int M(bool condition) { int value = 0; try { if (condition) throw new System.Exception(); value = 2; } finally { int marker = value; } return value; } }",
        "static class C { static int M() { int value = 0; try { return value; } finally { int marker = value; } } }",
        "static class C { static void M() { int value = 0; try { throw new System.Exception(); } finally { int marker = value; } } }",
        "static class C { static void M() { int value = 0; try { value = 1; } catch { value = 2; } finally { int marker = value; } } }",
        "static class C { static void M() { int value = 0; try { try { value = 1; } finally { value = 2; } } finally { int marker = value; } } }",
        "static class C { static void M(bool condition) { int value = 0; try { value = 1; } finally { if (condition) { int marker = value; } } } }",
        "static class C { static void M(bool condition) { int value = 0; try { value = 1; } finally { if (condition) value = 2; int marker = value; } } }",
        "static class C { static void M(bool condition) { int value = 0; while (condition) { try { value = 1; } finally { int marker = value; } break; } } }",
        "static class C { static void Touch() { } static void M() { int value = 0; try { Touch(); } finally { int marker = value; } } }",
        "static class C { static void M(int[] values) { int value = 0; try { value = values[0]; } finally { int marker = value; } } }",
        "static class C { static void M() { int value = 0; try { value = checked(value + 1); } finally { int marker = value; } } }",
        "static class C { static void Touch() { } static void M() { int value = 0; try { value = 1; } finally { Touch(); int marker = value; } } }",
        "sealed class D : System.IDisposable { public void Dispose() { } } static class C { static void M() { int value = 0; try { using (var resource = new D()) { value = 1; } } finally { int marker = value; } } }",
        "static class C { static readonly object Gate = new(); static void M() { int value = 0; try { lock (Gate) { value = 1; } } finally { int marker = value; } } }"
    };

    private static readonly (string Source, string[] Invalidated, string? Restored, long RestoredValue)[]
        FinallyLocalMultipleRegularPathCases =
        {
            ("static class C { static void M(bool condition) { int value = 1; int retained = 7; try { if (condition) value = 2; else value = 3; } finally { int marker = retained; } } }",
                new[] { "value" }, "retained", 7),
            ("static class C { static void M(bool condition) { int first = 1; int second = 2; try { if (condition) first = 3; else { second = 4; second = 5; } } finally { first = 9; int marker = first; } } }",
                new[] { "second" }, "first", 9)
        };

    private static IEnumerable<TestCaseData> CompletedTryCases()
    {
        yield return CompletedTryCase(
            "ExactCatch",
            "static class C { static void M() { int value = 0; try { throw new System.ArgumentException(); } catch (System.ArgumentException) { value = 2; } } }",
            expectedInteger: 2);
        yield return CompletedTryCase(
            "BaseCatch",
            "static class C { static void M() { int value = 0; try { throw new System.ArgumentException(); } catch (System.Exception) { value = 3; } } }",
            expectedInteger: 3);
        yield return CompletedTryCase(
            "IncompatibleCatch",
            "static class C { static void M() { int value = 0; try { throw new System.ArgumentException(); } catch (System.InvalidOperationException) { value = 4; } } }",
            expectedContradictory: true);
        yield return CompletedTryCase(
            "ConstantFalseFilterFallsThrough",
            "static class C { static void M() { int value = 0; try { throw new System.ArgumentException(); } catch (System.ArgumentException) when (false) { value = 2; } catch (System.Exception) { value = 3; } } }",
            expectedInteger: 3);
        yield return CompletedTryCase(
            "ConstantTrueFilter",
            "static class C { static void M() { int value = 0; try { throw new System.ArgumentException(); } catch (System.ArgumentException) when (true) { value = 4; } } }",
            expectedInteger: 4);
        yield return CompletedTryCase(
            "UnknownFilterAndLaterCatch",
            "static class C { static void M(bool filter) { int value = 0; try { throw new System.ArgumentException(); } catch (System.ArgumentException) when (filter) { value = 2; } catch (System.Exception) { value = 3; } } }",
            expectedInteger: null);
        yield return CompletedTryCase(
            "IncompatibleThenBaseCatch",
            "static class C { static void M() { int value = 0; try { throw new System.ArgumentException(); } catch (System.InvalidOperationException) { value = 2; } catch (System.Exception) { value = 3; } } }",
            expectedInteger: 3);
        yield return CompletedTryCase(
            "EarlierExactAndLaterBaseCatch",
            "static class C { static void M() { int value = 0; try { throw new System.ArgumentException(); } catch (System.ArgumentException) { value = 2; } catch (System.Exception) { value = 3; } } }",
            expectedInteger: null);
        yield return CompletedTryCase(
            "BaseTypedExactRuntimeCatch",
            "static class C { static void M() { int value = 0; System.Exception error = new System.ArgumentException(); try { throw error; } catch (System.ArgumentException) { value = 2; } } }",
            expectedInteger: 2);
        yield return CompletedTryCase(
            "UnknownRuntimeTypeCatch",
            "static class C { static void M(System.Exception error) { int value = 0; try { throw error; } catch (System.ArgumentException) { value = 2; } } }",
            expectedInteger: 2);
        yield return CompletedTryCase(
            "CatchThenFinally",
            "static class C { static void M() { int value = 0; try { throw new System.ArgumentException(); } catch (System.Exception) { value = 3; } finally { value = 4; } } }",
            expectedInteger: 4);
        yield return CompletedTryCase(
            "ThrowingFinally",
            "static class C { static void M() { int value = 0; try { throw new System.ArgumentException(); } catch (System.Exception) { value = 3; } finally { throw new System.InvalidOperationException(); } } }",
            expectedContradictory: true);
        yield return CompletedTryCase(
            "UnknownPotentialThrow",
            "static class C { static void Touch() { } static void M() { int value = 0; try { value = 1; Touch(); } catch { value = 2; } } }",
            expectedInteger: null);
    }

    private static TestCaseData CompletedTryCase(
        string name,
        string source,
        long? expectedInteger = null,
        bool expectedContradictory = false) =>
        new TestCaseData(source, expectedInteger, expectedContradictory)
            .SetName($"CompletedTry_{name}");

    private static IEnumerable<TestCaseData> CoalesceAssignmentCompletionCases()
    {
        yield return CoalesceCase(
            "ReferenceKnownNonNullNoOp",
            "static class C { static void M() { string? value = \"old\"; value ??= \"new\"; } }");
        yield return CoalesceCase(
            "ReferenceKnownNullAssignment",
            "static class C { static void M() { string? value = null; value ??= \"new\"; } }");
        yield return CoalesceCase(
            "ReferenceUnknownConditional",
            "static class C { static void M(string? value) { value ??= \"new\"; } }");
        yield return CoalesceCase(
            "NullableKnownHasValueNoOp",
            "static class C { static void M() { int? value = 1; value ??= 2; } }");
        yield return CoalesceCase(
            "NullableKnownNoValueAssignment",
            "static class C { static void M() { int? value = null; value ??= 2; } }");
        yield return CoalesceCase(
            "NullableUnknownConditional",
            "static class C { static void M(int? value) { value ??= 2; } }");
    }

    private static TestCaseData CoalesceCase(string name, string source) =>
        new TestCaseData(source).SetName($"CoalesceAssignmentCompletion_{name}");

    private static IEnumerable<TestCaseData> UnsupportedCoalesceAssignmentCompletionCases()
    {
        yield return CoalesceCase(
            "GuardMutationFallback",
            "static class C { static void M(string? value) { if (value == null) { value ??= \"new\"; } } }");
        yield return CoalesceCase(
            "LoopCurrentCompletionFallback",
            "static class C { static void M(string? value, bool repeat) { while (repeat) { value ??= \"new\"; } } }");
    }

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
    public void SwitchExitBeforeNestedBranch_RoutedFallbackIsUnreachable()
    {
        const string source = @"
using System;
static class C
{
    static void M(int value)
    {
        switch (value)
        {
            case 0:
                return;
        }
        if (value == 0)
            Console.WriteLine(value);
    }
}";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(SwitchExitBeforeNestedBranch_RoutedFallbackIsUnreachable));
        var site = fixture.Root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single();

        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var routed = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var completedSwitch = fixture.Root.DescendantNodes().OfType<SwitchStatementSyntax>().Single();
        var completion = SymbolicCfgProgramPointStateCollector.CollectCompletedStatementState(
            completedSwitch,
            new SymbolicState(),
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(actual.IsUnsupported, Is.True, actual.Provenance.Single().Detail);
        Assert.That(
            SymbolicReachabilityService.ClassifyStateFeasibility(routed, null).Info.Status,
            Is.EqualTo(SymbolicProofStatus.Unreachable),
            routed.NormalizedProofKey + Environment.NewLine +
            completion.Provenance.Single().Detail + Environment.NewLine +
            completion.Value?.NormalizedProofKey);
    }

    [TestCase(
        "static class C { static int M(bool condition) { int value = 1; if (condition) value = 2; else value = 3; return value; } }",
        typeof(IfStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 1; if (condition) value.ToString(); else value = 3; return value; } }",
        typeof(IfStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition, bool choose) { int value = 1; if (condition) value = choose ? 2 : 3; else value = 4; return value; } }",
        typeof(IfStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool first, bool second) { int value = 1; if (first) { if (second) value = 2; else value = 3; } else value = 4; return value; } }",
        typeof(IfStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool first, bool second) { int value = 1; if (first) { int inner = 2; if (second) value = inner; } else value = 4; return value; } }",
        typeof(IfStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 1; if (condition) return value; value = 2; return value; } }",
        typeof(IfStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 1; if (condition) throw new System.Exception(); value = 2; return value; } }",
        typeof(IfStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition) { if (condition) return 1; else throw new System.Exception(); } }",
        typeof(IfStatementSyntax))]
    [TestCase(
        "static class C { static int M(int input) { int value = 1; switch (input) { case 0: value = 2; break; default: value = 3; break; } return value; } }",
        typeof(SwitchStatementSyntax))]
    [TestCase(
        "static class C { static int M(int value) { switch (value) { case 0: return 0; } return 10 / value; } }",
        typeof(SwitchStatementSyntax))]
    [TestCase(
        "static class C { static int M(int value) { switch (value) { case 0: return 0; default: value = 0; break; } return value; } }",
        typeof(SwitchStatementSyntax))]
    [TestCase(
        "static class C { static int M(object input) { int value = 1; switch (input) { case int _: value = 2; break; default: value = 3; break; } return value; } }",
        typeof(SwitchStatementSyntax))]
    [TestCase(
        "static class C { static int M(int input) { switch (input) { case 0: return 1; default: throw new System.Exception(); } } }",
        typeof(SwitchStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 1; while (condition) value = 2; return value; } }",
        typeof(WhileStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition, bool stop) { int value = 1; while (condition) { if (stop) break; value = 2; } return value; } }",
        typeof(WhileStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition, bool skip, bool stop) { int value = 1; while (condition) { if (skip) continue; if (stop) break; value = 2; } return value; } }",
        typeof(WhileStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool stop) { int value = 1; for (;;) { if (stop) break; value = 2; } return value; } }",
        typeof(ForStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool ready, bool done) { int value = 1; for (;;) { if (!ready) continue; ready = false; if (done) break; } return value; } }",
        typeof(ForStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool ready, bool blocked, bool done) { int value = 1; for (;;) { if (!ready) continue; if (blocked) continue; if (done) break; } return value; } }",
        typeof(ForStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool ready, bool blocked) { int value = 1; for (;;) { if (ready) { if (blocked) continue; } break; } return value; } }",
        typeof(ForStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition, bool first, bool second) { int value = 1; while (condition) { if (first) { if (second) break; } value = 2; } return value; } }",
        typeof(WhileStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition, bool first, bool second) { int value = 1; while (condition) { if (first) break; if (second) break; value = 2; } return value; } }",
        typeof(WhileStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition, bool stop) { int value = 1; while (condition) { stop = false; if (stop) break; value = 2; } return value; } }",
        typeof(WhileStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition, bool stop) { int value = 1; while (condition) { if (stop) return value; value = 2; } return value; } }",
        typeof(WhileStatementSyntax))]
    [TestCase(
        "static class C { static int M(int[] values, int index) { while (index < values.Length) { if (index < 0) return -1; index++; } return index; } }",
        typeof(WhileStatementSyntax))]
    [TestCase(
        "static class C { static int M(int[] values, int index) { while (index < values.Length) { if (index < 0) throw new System.Exception(); index++; } return index; } }",
        typeof(WhileStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition, bool stop) { int value = 1; while (condition) { try { if (stop) break; value = 2; } finally { value = 3; } } return value; } }",
        typeof(WhileStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool outer, bool inner, bool stop) { int value = 1; while (outer) { while (inner) { break; } if (stop) break; value = 2; } return value; } }",
        typeof(WhileStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition, bool stop, int input) { int value = 1; while (condition) { switch (input) { case 0: break; } if (stop) break; value = 2; } return value; } }",
        typeof(WhileStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 1; do value = 2; while (condition); return value; } }",
        typeof(DoStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool repeat, bool stop) { int value = 1; do { int hidden = value; if (stop) break; if (repeat) continue; value = hidden + 1; } while (repeat); return value; } }",
        typeof(DoStatementSyntax))]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 1; for (; condition;) value = 2; return value; } }",
        typeof(ForStatementSyntax))]
    [TestCase(
        "static class C { static int M(int count) { int value = -1; for (value = 0; value < count; value++) { break; } return value; } }",
        typeof(ForStatementSyntax))]
    [TestCase(
        "static class C { static int M(int count) { int value = -1; for (value = 0; value < count; value++) { } return value; } }",
        typeof(ForStatementSyntax))]
    [TestCase(
        "static class C { static int M() { int value = 1; for (;;) { } return value; } }",
        typeof(ForStatementSyntax))]
    [TestCase(
        "static class C { static int M() { int value = 0; for (int index = 0; index < 3; index++) value = index; return value; } }",
        typeof(ForStatementSyntax))]
    [TestCase(
        "static class C { static int M(int[] values) { int value = 1; foreach (var item in values) value = item; return value; } }",
        typeof(ForEachStatementSyntax))]
    [TestCase(
        "static class C { static int M((int, int)[] values) { int value = 1; foreach (var (left, right) in values) value = left + right; return value; } }",
        typeof(ForEachVariableStatementSyntax))]
    [TestCase(
        "static class C { static int M(object gate) { int value = 1; lock (gate) value = 2; return value; } }",
        typeof(LockStatementSyntax))]
    public void CompletedStatementRegion_MatchesStructuralCompletion(
        string source,
        Type statementType)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(CompletedStatementRegion_MatchesStructuralCompletion));
        var statement = (StatementSyntax)fixture.Root.DescendantNodes()
            .First(node => node.GetType() == statementType);
        var entryState = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                statement,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                statement,
                fixture.SemanticModel,
                CancellationToken.None));
        var expected = entryState;
        SymbolicStatementStateTransfer.AddPriorStatementStateFacts(
            ref expected,
            statement,
            fixture.SemanticModel,
            CancellationToken.None);

        var actual = SymbolicCfgProgramPointStateCollector.CollectCompletedStatementState(
            statement,
            entryState,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        AssertStateParity(actual.Value!, expected);
    }

    [Test]
    public void SeededCompletedStatementTrace_PreservesSeedAndBypassesCache()
    {
        const string source =
            "static class C { static int M(bool condition, int input) { if (condition) { int hidden = input; } return input; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(SeededCompletedStatementTrace_PreservesSeedAndBypassesCache));
        var statement = fixture.Root.DescendantNodes().OfType<IfStatementSyntax>().Single();
        var seed = CreateSeed(fixture, statement, "input", SeedKind.Numeric, 17);
        var before = SymbolicReachabilityService.GetStructuralPathCacheInfo(statement, fixture.SemanticModel);

        var actual = SymbolicCfgExecutionTrace.CollectCompletedStatementState(
            statement,
            seed,
            fixture.SemanticModel,
            CancellationToken.None);
        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        AssertStateParity(actual.Value!, seed);
        Assert.That(ContainsSeedEvidence(actual.Value!), Is.True);
        Assert.That(actual.Provenance.Single().Stage, Is.EqualTo("cfg-program-point"));
        Assert.That(actual.Provenance.Single().SourceSpan, Is.EqualTo(statement.Span));
        Assert.That(actual.Provenance.Single().Detail, Is.EqualTo("exact"));
        AssertCacheInfo(
            SymbolicReachabilityService.GetStructuralPathCacheInfo(statement, fixture.SemanticModel),
            before);
    }

    [Test]
    public void SeededCompletedStatementTrace_TruncationIsAtomicAndBypassesCache()
    {
        const string source =
            "static class C { static int M(bool condition) { int value = 0; if (condition) { int left = 1; value = left; } else { int right = 2; value = right; } return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(SeededCompletedStatementTrace_TruncationIsAtomicAndBypassesCache));
        var statement = fixture.Root.DescendantNodes().OfType<IfStatementSyntax>().Single();
        var before = SymbolicReachabilityService.GetStructuralPathCacheInfo(statement, fixture.SemanticModel);
        using var scope = SymbolicAnalysisLimitContext.Push(
            SharpProofAnalysisBudget.Default with { MaxMergedIfElseFacts = 1 });

        var result = SymbolicCfgExecutionTrace.CollectCompletedStatementState(
            statement,
            new SymbolicState(),
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsUnsupported, Is.True);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
            var provenance = result.Provenance.Single();
            Assert.That(provenance.Stage, Is.EqualTo("cfg-program-point"));
            Assert.That(provenance.SourceSpan, Is.EqualTo(statement.Span));
            Assert.That(provenance.Detail, Is.EqualTo("trace.truncation"));
        });
        AssertCacheInfo(
            SymbolicReachabilityService.GetStructuralPathCacheInfo(statement, fixture.SemanticModel),
            before);
    }

    [TestCase(
        "static class C { static int M(int input) { int value = input; value++; return value; } }",
        "return value;")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 1; if (condition) value = 2; else value = 3; return value; } }",
        "return value;")]
    [TestCase(
        "static class C { static int M(bool condition, int value) { if (condition) return value + 1; return value; } }",
        "return value + 1;")]
    [TestCase(
        "sealed class C { int[] M(int length) { var values = new int[length]; if (length < 0) return new int[0]; return values; } }",
        "if (length < 0) return new int[0];")]
    public void ExecutionRootTrace_BeforeCurrentMatchesPerPointCollector(
        string source,
        string marker)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ExecutionRootTrace_BeforeCurrentMatchesPerPointCollector));
        var site = fixture.Root.DescendantNodes()
            .OfType<StatementSyntax>()
            .Single(statement => statement.ToString() == marker);
        var expected = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var actual = SymbolicCfgExecutionTrace.CollectStateFromExecutionTrace(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(expected.IsExact, Is.True, expected.Provenance.Single().Detail);
        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        AssertStateParity(actual.Value!, expected.Value!);
    }

    [Test]
    public void ExecutionRootTrace_CachedStatesRebaseMethodEntryEvidencePerSite()
    {
        const string source = "sealed class C { int M(string input) { int first = input.Length; int second = first + 1; return second; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ExecutionRootTrace_CachedStatesRebaseMethodEntryEvidencePerSite));
        var sites = fixture.Root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().ToArray();

        foreach (var site in sites)
        {
            var expected = SymbolicCfgProgramPointStateCollector.CollectState(
                site,
                fixture.SemanticModel,
                CancellationToken.None);
            var actual = SymbolicReachabilityService.CollectPathStateAt(
                site,
                fixture.SemanticModel,
                CancellationToken.None);

            Assert.That(expected.IsExact, Is.True, expected.Provenance.Single().Detail);
            AssertStateParity(actual, expected.Value!);
        }
    }

    [Test]
    public void ExecutionRootTrace_ConcurrentQueriesRemainIsolatedByExecutionRoot()
    {
        const string source = "sealed class C { int First(int value) { value++; return value; } int Second(int value) { value += 2; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ExecutionRootTrace_ConcurrentQueriesRemainIsolatedByExecutionRoot));
        var sites = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().ToArray();
        var expected = sites.Select(site => SymbolicReachabilityService.CollectPathStateAt(
                site,
                fixture.SemanticModel,
                CancellationToken.None))
            .Select(CreateTraceStateKey)
            .ToArray();
        var actual = new ConcurrentBag<string>();

        Parallel.For(0, 16, index => actual.Add(CreateTraceStateKey(
            SymbolicReachabilityService.CollectPathStateAt(
                sites[index % sites.Length],
                fixture.SemanticModel,
                CancellationToken.None))));

        Assert.That(actual.Distinct(StringComparer.Ordinal), Is.EquivalentTo(expected));
    }

    private static string CreateTraceStateKey(SymbolicState state) =>
        state.NormalizedProofKey + "\n" + CreateEvidenceKey(state) + "\n" + CreateVersionKey(state);

    [Test]
    public void ExecutionRootTrace_UnsupportedLoopFallsBackWithoutPartialState()
    {
        const string source = "static class C { static int M(int value) { while (value > 0) value--; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ExecutionRootTrace_UnsupportedLoopFallsBackWithoutPartialState));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();

        var trace = SymbolicCfgExecutionTrace.CollectStateFromExecutionTrace(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var routed = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = CollectStructuralState(fixture, site);

        Assert.That(trace.IsUnsupported, Is.True);
        Assert.That(trace.Value, Is.Null);
        Assert.That(trace.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
        Assert.That(trace.Provenance.Single().Detail, Is.EqualTo("trace.control-flow-shape"));
        AssertStateParity(routed, expected);
    }

    [Test]
    public void ExecutionRootTrace_CustomLimitsBypassWarmDefaultCache()
    {
        const string source = "static class C { static int M(bool condition) { int value = 1; if (condition) value = 2; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ExecutionRootTrace_CustomLimitsBypassWarmDefaultCache));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();
        _ = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var before = SymbolicReachabilityService.GetStructuralPathCacheInfo(site, fixture.SemanticModel);

        using var scope = SymbolicAnalysisLimitContext.Push(
            SharpProofAnalysisBudget.Default with { MaxMergedPathConditions = 1 });
        var actual = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = CollectStructuralState(fixture, site);

        AssertStateParity(actual, expected);
        AssertCacheInfo(SymbolicReachabilityService.GetStructuralPathCacheInfo(site, fixture.SemanticModel), before);
    }

    [Test]
    public void ExecutionRootTrace_CanceledRequestDoesNotPoisonCache()
    {
        const string source = "static class C { static int M(int input) { int value = input; value++; return value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ExecutionRootTrace_CanceledRequestDoesNotPoisonCache));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            cancellation.Token));
        var actual = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(expected.IsExact, Is.True, expected.Provenance.Single().Detail);
        AssertStateParity(actual, expected.Value!);
    }

    [TestCaseSource(nameof(CompletedTryCases))]
    public void CompletedTry_MatchesStructuralCompletion(
        string source,
        long? expectedInteger,
        bool expectedContradictory)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(CompletedTry_MatchesStructuralCompletion));
        var statement = fixture.Root.DescendantNodes().OfType<TryStatementSyntax>().Single();
        var entryState = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                statement,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                statement,
                fixture.SemanticModel,
                CancellationToken.None));
        var expected = entryState;
        SymbolicStatementStateTransfer.AddPriorStatementStateFacts(
            ref expected,
            statement,
            fixture.SemanticModel,
            CancellationToken.None);

        var actual = SymbolicCfgProgramPointStateCollector.CollectCompletedStatementState(
            statement,
            entryState,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        AssertStateParity(actual.Value!, expected);
        Assert.That(actual.Value!.IsContradictory, Is.EqualTo(expectedContradictory));
        if (expectedInteger.HasValue)
        {
            var value = GetLocal(fixture, "value");
            Assert.That(SymbolicStateValueFacts.TryGetCurrentValue(actual.Value, value, out var current), Is.True);
            Assert.That(current, Is.EqualTo(new SymbolicIntegerConstantTerm(expectedInteger.Value)));
        }
    }

    [TestCase(
        "static class C { static int M(int value) { switch (value) { case 0: switch (value) { case 1: break; } break; } return value; } }",
        typeof(SwitchStatementSyntax),
        "statement-region.switch-nested")]
    [TestCase(
        "static class C { static int M(int value) { switch (value) { case 0: case 1: break; } return value; } }",
        typeof(SwitchStatementSyntax),
        "statement-region.switch-multi-label")]
    [TestCase(
        "static class C { static int M(bool condition, bool other) { int value = 0; if (condition) { if (other) goto End; value = 1; } End: return value; } }",
        typeof(IfStatementSyntax),
        "statement-region.if-abrupt")]
    [TestCase(
        "static class C { static int M(int value) { try { switch (value) { case 0: return 1; } } finally { value = 2; } return value; } }",
        typeof(SwitchStatementSyntax),
        "statement-region.finally-ownership")]
    public void CompletedStatementRegion_UnsupportedShapePreservesTypedReason(
        string source,
        Type statementType,
        string expectedDetail)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(CompletedStatementRegion_UnsupportedShapePreservesTypedReason));
        var statement = (StatementSyntax)fixture.Root.DescendantNodes()
            .First(node => node.GetType() == statementType);

        var result = SymbolicCfgProgramPointStateCollector.CollectCompletedStatementState(
            statement,
            new SymbolicState(),
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsUnsupported, Is.True);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
            var provenance = result.Provenance.Single();
            Assert.That(provenance.Stage, Is.EqualTo("cfg-program-point"));
            Assert.That(provenance.SourceSpan, Is.EqualTo(statement.Span));
            Assert.That(provenance.Detail, Is.EqualTo(expectedDetail));
        });
    }

    [TestCaseSource(nameof(SeededPathCases))]
    public void SeededState_MatchesCfgStructuralAndRoutedCollectors(
        (string Source, string Target, string Parameter, SeedKind Kind, bool SeedSurvives) testCase)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            testCase.Source,
            nameof(SeededState_MatchesCfgStructuralAndRoutedCollectors));
        var site = fixture.Root.DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Single(statement => statement.ToString() == testCase.Target);
        var seed = CreateSeed(fixture, site, testCase.Parameter, testCase.Kind);

        var cfg = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            seed);
        var structural = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None,
                initialState: seed));
        var routed = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            seed);

        Assert.That(cfg.IsExact, Is.True, cfg.Provenance.Single().Detail);
        AssertStateParity(cfg.Value!, structural);
        AssertStateParity(routed, structural);
        Assert.That(ContainsSeedEvidence(cfg.Value!), Is.EqualTo(testCase.SeedSurvives));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SeededState_IsolatedFromSeedlessCacheAndOtherSeeds(bool warmUnseededCacheFirst)
    {
        const string source = "static class C { static int M(int input) { return input; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(SeededState_IsolatedFromSeedlessCacheAndOtherSeeds));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();
        var parameter = fixture.SemanticModel.GetDeclaredSymbol(
            fixture.Root.DescendantNodes().OfType<ParameterSyntax>().Single())!;
        SymbolicState? unseeded = null;
        if (warmUnseededCacheFirst)
        {
            unseeded = SymbolicReachabilityService.CollectPathStateAt(
                site,
                fixture.SemanticModel,
                CancellationToken.None);
        }

        var cacheBeforeSeeds = SymbolicReachabilityService.GetStructuralPathCacheInfo(site, fixture.SemanticModel);
        var seededStates = new List<SymbolicState>();
        foreach (var value in new long[] { 1, 2 })
        {
            var seed = CreateSeed(fixture, site, "input", SeedKind.Numeric, value);
            var seeded = SymbolicReachabilityService.CollectPathStateAt(
                site, fixture.SemanticModel, CancellationToken.None, seed);
            seededStates.Add(seeded);

            AssertStateParity(seeded, seed);
            Assert.That(SymbolicStateValueFacts.TryGetCurrentValue(seeded, parameter, out var current), Is.True);
            Assert.That(current, Is.EqualTo(new SymbolicIntegerConstantTerm(value)));
        }

        var afterSeeds = SymbolicReachabilityService.GetStructuralPathCacheInfo(site, fixture.SemanticModel);
        AssertCacheInfo(afterSeeds, cacheBeforeSeeds);
        Assert.That(seededStates[0].NormalizedProofKey, Is.Not.EqualTo(seededStates[1].NormalizedProofKey));

        unseeded ??= SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var warmCache = SymbolicReachabilityService.GetStructuralPathCacheInfo(site, fixture.SemanticModel);
        Assert.That(SymbolicStateValueFacts.TryGetCurrentValue(unseeded, parameter, out _), Is.False);

        var cachedUnseeded = SymbolicReachabilityService.CollectPathStateAt(
            site, fixture.SemanticModel, CancellationToken.None);
        AssertStateParity(cachedUnseeded, unseeded);
        AssertCacheInfo(
            SymbolicReachabilityService.GetStructuralPathCacheInfo(site, fixture.SemanticModel),
            new SymbolicCacheInfo(
                warmCache.Hits + 1,
                warmCache.Misses,
                warmCache.Entries,
                warmCache.Evictions));
    }

    [Test]
    public void SeededState_CustomLimitsPreserveSeedThroughStructuralFallback()
    {
        const string source = "static class C { static int M(int input) { return input; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(SeededState_CustomLimitsPreserveSeedThroughStructuralFallback));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();
        var parameter = fixture.SemanticModel.GetDeclaredSymbol(
            fixture.Root.DescendantNodes().OfType<ParameterSyntax>().Single())!;
        var seed = CreateSeed(fixture, site, "input", SeedKind.Numeric, 17);
        using var scope = SymbolicAnalysisLimitContext.Push(
            SharpProofAnalysisBudget.Default with { MaxMergedPathConditions = 1 });

        var cfg = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            seed);
        var routed = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            seed);
        var structural = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None,
                initialState: seed));

        Assert.That(cfg.IsUnsupported, Is.True, cfg.Provenance.Single().Detail);
        Assert.That(cfg.Value, Is.Null);
        AssertStateParity(routed, structural);
        Assert.That(SymbolicStateValueFacts.TryGetCurrentValue(routed, parameter, out var current), Is.True);
        Assert.That(current, Is.EqualTo(new SymbolicIntegerConstantTerm(17)));
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
        AssertStateParity(actual.Value!, expected);
    }

    [TestCase(
        "static class C { static int M(int input) { int value = input; value += 2; return value; } }",
        true,
        TestName = "CompoundCurrentCompletion_Unchecked")]
    [TestCase(
        "static class C { static int M(int input) { int value = input; checked { value += 2; } return value; } }",
        true,
        TestName = "CompoundCurrentCompletion_Checked")]
    [TestCase(
        "static class C { static int M(int input) { int value = input; if (value < 10) { value = 12; } return value; } }",
        false,
        TestName = "GuardInvalidatingCurrentCompletion_SimpleAssignment")]
    [TestCase(
        "static class C { static int M(int input) { int value = input; if (value < 10) { value += 2; } return value; } }",
        false,
        TestName = "GuardInvalidatingCurrentCompletion_UncheckedCompoundAssignment")]
    [TestCase(
        "static class C { static int M(int input) { int value = input; if (value < 10) { checked { value += 2; } } return value; } }",
        false,
        TestName = "GuardInvalidatingCurrentCompletion_CheckedCompoundAssignment")]
    public void AssignmentCurrentCompletion_PreservesSafeParityAndRejectsGuardMutation(
        string source,
        bool expectedExact)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(AssignmentCurrentCompletion_PreservesSafeParityAndRejectsGuardMutation));
        var site = fixture.Root.DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .Single(statement => statement.Expression is AssignmentExpressionSyntax);

        var direct = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);
        var structural = CollectStructuralState(
            fixture,
            site,
            includeCurrentStatementCompletionFacts: true);
        var routed = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);

        if (expectedExact)
        {
            Assert.That(direct.IsExact, Is.True, direct.Provenance.Single().Detail);
            AssertStateParity(direct.Value!, structural);
        }
        else
        {
            Assert.That(direct.IsUnsupported, Is.True, direct.Provenance.Single().Detail);
            Assert.That(direct.Value, Is.Null);
        }
        AssertStateParity(routed, structural);
    }

    [Test]
    public void MemberNotNullExpressionCompletion_CurrentRoutingCharacterization()
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            MemberNotNullCompletionSource,
            nameof(MemberNotNullExpressionCompletion_CurrentRoutingCharacterization));
        var site = fixture.Root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression.ToString() == "EnsureValue");

        var cfg = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);
        var structural = CollectStructuralState(
            fixture,
            site,
            includeCurrentStatementCompletionFacts: true);
        var routed = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);

        Assert.That(cfg.IsExact, Is.True, cfg.Provenance.Single().Detail);
        AssertStateParity(cfg.Value!, structural);
        AssertStateParity(routed, structural);
        Assert.That(
            CreateEvidenceKey(structural),
            Does.Contain("ir.path.normal-completion.member-not-null"));
    }

    [Test]
    public void MemberNotNullExpressionCompletion_CustomLimitsUseConservativeFallback()
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            MemberNotNullCompletionSource,
            nameof(MemberNotNullExpressionCompletion_CustomLimitsUseConservativeFallback));
        var site = fixture.Root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression.ToString() == "EnsureValue");
        using var scope = SymbolicAnalysisLimitContext.Push(
            SharpProofAnalysisBudget.Default with { MaxMergedPathConditions = 1 });

        var cfg = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);
        var structural = CollectStructuralState(
            fixture,
            site,
            includeCurrentStatementCompletionFacts: true);
        var routed = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);

        Assert.That(cfg.IsUnsupported, Is.True, cfg.Provenance.Single().Detail);
        Assert.That(cfg.Value, Is.Null);
        AssertStateParity(routed, structural);
    }

    [TestCaseSource(nameof(CoalesceAssignmentCompletionCases))]
    public void CoalesceAssignmentCompletion_CurrentRoutingCharacterization(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(CoalesceAssignmentCompletion_CurrentRoutingCharacterization));
        var site = GetCoalesceAssignmentStatement(fixture);

        var cfg = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);
        var structural = CollectStructuralState(
            fixture,
            site,
            includeCurrentStatementCompletionFacts: true);
        var routed = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);

        Assert.That(cfg.IsUnsupported, Is.True, cfg.Provenance.Single().Detail);
        Assert.That(cfg.Value, Is.Null);
        AssertStateParity(routed, structural);
    }

    [TestCaseSource(nameof(UnsupportedCoalesceAssignmentCompletionCases))]
    public void CoalesceAssignmentCompletion_UnsafeShapeUsesStructuralFallback(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(CoalesceAssignmentCompletion_UnsafeShapeUsesStructuralFallback));
        var site = GetCoalesceAssignmentStatement(fixture);

        var cfg = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);
        var structural = CollectStructuralState(
            fixture,
            site,
            includeCurrentStatementCompletionFacts: true);
        var routed = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);

        Assert.That(cfg.IsUnsupported, Is.True, cfg.Provenance.Single().Detail);
        Assert.That(cfg.Value, Is.Null);
        AssertStateParity(routed, structural);
    }

    [Test]
    public void CoalesceAssignmentCompletion_CustomLimitsUseStructuralFallback()
    {
        const string source =
            "static class C { static void M() { string? value = null; value ??= \"new\"; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(CoalesceAssignmentCompletion_CustomLimitsUseStructuralFallback));
        var site = GetCoalesceAssignmentStatement(fixture);
        using var scope = SymbolicAnalysisLimitContext.Push(
            SharpProofAnalysisBudget.Default with { MaxMergedPathConditions = 1 });

        var cfg = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);
        var structural = CollectStructuralState(
            fixture,
            site,
            includeCurrentStatementCompletionFacts: true);
        var routed = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);

        Assert.That(cfg.IsUnsupported, Is.True, cfg.Provenance.Single().Detail);
        Assert.That(cfg.Value, Is.Null);
        AssertStateParity(routed, structural);
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
        AssertStateParity(actual.Value!, expected);
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
        AssertStateParity(actual.Value!, expected);
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
        "static class C { static int M(bool condition) { int value = 0; try { if (condition) return 1; value = 2; } finally { value = 3; } return value; } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; try { if (condition) throw new System.Exception(); value = 2; } finally { value = 3; } return value; } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; try { if (condition) return 1; throw new System.Exception(); } finally { value = 2; } } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; try { value = 1; } finally { if (condition) throw new System.Exception(); value = 2; } return value; } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; try { try { if (condition) return 1; value = 2; } finally { value = 3; } } finally { value = 4; } return value; } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; try { if (condition) return 1; value = 2; } finally { if (condition) throw new System.Exception(); value = 3; } return value; } }")]
    [TestCase(
        "static class C { static int M(bool condition) { int value = 0; try { try { if (condition) return 1; value = 2; } finally { value = 3; } } finally { if (condition) throw new System.Exception(); value = 4; } return value; } }")]
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
    [TestCase(
        "static class C { static int M(bool condition, bool nested) { if (condition) { if (nested) return 1; int marker = 2; } return 0; } }")]
    [TestCase(
        "static class C { static int M(bool condition, bool nested) { if (condition) { if (nested) throw new System.Exception(); int marker = 2; } return 0; } }")]
    [TestCase(
        "static class C { static void M(bool condition, bool nested) { if (condition) { if (nested) nested = false; int marker = 2; } } }")]
    [TestCase(
        "static class C { static void M(bool condition, object value, object replacement) { if (condition) { if (value != null) value = replacement; int marker = 2; } } }")]
    [TestCase(
        "static class C { static int M(bool condition, bool nested) { if (condition) { if (nested) return 1; else return 2; } return 0; } }")]
    [TestCase(
        "static class C { static int M(bool condition, bool nested) { if (condition) { if (nested) return 1; else throw new System.Exception(); } return 0; } }")]
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
        "static class C { static void M(bool condition) { int value = 0; if (condition) { System.Console.WriteLine(); value = 2; } } }")]
    [TestCase(
        "static class C { static void M(bool condition, bool nested) { if (condition) { if (nested) condition = false; int marker = 2; } } }")]
    [TestCase(
        "static class C { static void M(object value, bool nested, object replacement) { if (value != null) { if (nested) value = replacement; int marker = 2; } } }")]
    [TestCase(
        "static class C { static int M(bool condition, bool nested) { if (condition) { if (nested) return 1; throw new System.Exception(); } return 0; } }")]
    [TestCase(
        "static class C { static int M(bool condition) { if (condition) { return 1; } return 0; } }")]
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

    [TestCase(false, "root-block-control-flow")]
    [TestCase(true, "nested-block-completion")]
    public void BlockCurrentCompletion_UnsupportedIdentityIsStable(
        bool nested,
        string expectedDetail)
    {
        var source = nested
            ? "static class C { static void M(bool condition) { if (condition) { System.Console.WriteLine(); } } }"
            : "static class C { static void M() { System.Console.WriteLine(); } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(BlockCurrentCompletion_UnsupportedIdentityIsStable));
        SyntaxNode site = nested
            ? fixture.Root.DescendantNodes().OfType<IfStatementSyntax>().Single().Statement
            : fixture.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Body!;

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsUnsupported, Is.True);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
            var provenance = result.Provenance.Single();
            Assert.That(provenance.Stage, Is.EqualTo("cfg-program-point"));
            Assert.That(provenance.SourceSpan, Is.EqualTo(site.Span));
            Assert.That(provenance.Detail, Is.EqualTo(expectedDetail));
        });
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

    [Test]
    public void ForInitialEntry_ReassignmentDiscardsPriorValue()
    {
        const string source =
            "static class C { static void M() { int index = 7; for (index = 0; index == 0; index++) { } } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ForInitialEntry_ReassignmentDiscardsPriorValue));
        var forStatement = fixture.Root.DescendantNodes().OfType<ForStatementSyntax>().Single();
        var index = fixture.SemanticModel.GetSymbolInfo(
            ((BinaryExpressionSyntax)forStatement.Condition!).Left).Symbol!;

        var analysis = new SymbolicInvariantService().AnalyzeForInitialEntry(
            forStatement,
            fixture.SemanticModel);

        Assert.That(analysis.PathState.IsContradictory, Is.False);
        Assert.That(
            SymbolicStateValueFacts.TryGetCurrentValue(analysis.PathState, index, out var value),
            Is.True);
        Assert.That(value, Is.EqualTo(new SymbolicIntegerConstantTerm(0)));
    }

    [TestCaseSource(nameof(ForInitialEntryCases))]
    public void ForInitialEntryState_MatchesStructuralCollector(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ForInitialEntryState_MatchesStructuralCollector));
        var forStatement = fixture.Root.DescendantNodes().OfType<ForStatementSyntax>().Single();

        var actual = SymbolicCfgProgramPointStateCollector.CollectForInitialEntryState(
            forStatement,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = SymbolicProgramPointFacts.CollectForInitialEntryState(
            forStatement,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expected)));
        Assert.That(CreateVersionKey(actual.Value), Is.EqualTo(CreateVersionKey(expected)));
    }

    [TestCase(
        "static class C { static void M() { int index = 7; for (index = 0, index = 1; index == 1; index++) { } } }",
        1)]
    [TestCase(
        "static class C { static void M(string? input) { string? value = null; for (value = input; value != null;) { } } }",
        0)]
    [TestCase(
        "static class C { static void M(int? input) { int? value = null; for (value = input; value.HasValue;) { } } }",
        0)]
    public void ForInitialEntryState_SequentialAssignmentsDiscardStaleFacts(
        string source,
        int initializerIndex)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ForInitialEntryState_SequentialAssignmentsDiscardStaleFacts));
        var forStatement = fixture.Root.DescendantNodes().OfType<ForStatementSyntax>().Single();
        var assignment = (AssignmentExpressionSyntax)forStatement.Initializers[initializerIndex];
        var target = fixture.SemanticModel.GetSymbolInfo(assignment.Left).Symbol!;
        var targetType = target switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            _ => null
        };

        var actual = SymbolicCfgProgramPointStateCollector.CollectForInitialEntryState(
            forStatement,
            fixture.SemanticModel,
            CancellationToken.None);
        var routed = SymbolicReachabilityService.CollectForInitialEntryState(
            forStatement,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.IsContradictory, Is.False);
        if (targetType?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            Assert.That(SymbolicStateValueFacts.IsKnownNullableNoValue(actual.Value, target), Is.False);
        }
        else if (targetType?.IsReferenceType == true)
        {
            Assert.That(SymbolicStateValueFacts.IsKnownNullReference(actual.Value, target), Is.False);
        }
        else
        {
            var expectedValue = SymbolicSemanticPipeline.LowerTerm(
                assignment.Right,
                new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None));
            Assert.That(expectedValue.IsExact, Is.True);
            Assert.That(
                SymbolicStateValueFacts.TryGetCurrentValue(actual.Value, target, out var currentValue),
                Is.True);
            Assert.That(currentValue, Is.EqualTo(expectedValue.Value));
        }
        Assert.That(routed.NormalizedProofKey, Is.EqualTo(actual.Value.NormalizedProofKey));
        Assert.That(CreateEvidenceKey(routed), Is.EqualTo(CreateEvidenceKey(actual.Value)));
    }

    [TestCase(
        "static class C { static int Get() => 1; static void M() { for (int index = Get(); index < 3; index++) { } } }")]
    [TestCase(
        "static class C { static int Get() => 1; static void M() { int index = 0; for (index = 1, index = Get(); index < 3; index++) { } } }")]
    [TestCase(
        "static class C { static void M(bool select) { for (int index = select ? 0 : 1; index < 3; index++) { } } }")]
    [TestCase(
        "static class C { static void M(string? input) { for (string value = input ?? throw new System.Exception(); value != null;) { } } }")]
    [TestCase(
        "static class C { static void Set(out int value) => value = 0; static void M() { int index = 0; for (Set(out index); index < 3; index++) { } } }")]
    [TestCase(
        "static class C { static void M() { int index = 0; for (index++; index < 3; index++) { } } }")]
    [TestCase(
        "static class C { static void M() { int index = 0; for (index += 1; index < 3; index++) { } } }")]
    [TestCase(
        "static class C { static void M() { for (int index = 0;; index++) { } } }")]
    [TestCase(
        "static class C { static void M(bool keepGoing) { while (keepGoing) { for (int index = 0; index < 3; index++) { } } } }")]
    [TestCase(
        "static class C { static void M(bool select) { int value = select ? 1 : 2; for (int index = 0; index < 3; index++) { } } }")]
    [TestCase(
        "sealed class C { int Value; void M() { for (Value = 0; Value < 3; Value++) { } } }")]
    public void UnsupportedForInitialEntry_RemainsConservativeFallback(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(UnsupportedForInitialEntry_RemainsConservativeFallback));
        var forStatement = fixture.Root.DescendantNodes().OfType<ForStatementSyntax>().Single();

        var result = SymbolicCfgProgramPointStateCollector.CollectForInitialEntryState(
            forStatement,
            fixture.SemanticModel,
            CancellationToken.None);
        var routed = SymbolicReachabilityService.CollectForInitialEntryState(
            forStatement,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = SymbolicProgramPointFacts.CollectForInitialEntryState(
            forStatement,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True, result.Provenance.Single().Detail);
        Assert.That(routed.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
        Assert.That(CreateEvidenceKey(routed), Is.EqualTo(CreateEvidenceKey(expected)));
    }

    [Test]
    public void CustomLimitsForInitialEntry_RemainsConservativeFallback()
    {
        const string source =
            "static class C { static void M() { for (int index = 0; index < 3; index++) { } } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(CustomLimitsForInitialEntry_RemainsConservativeFallback));
        var forStatement = fixture.Root.DescendantNodes().OfType<ForStatementSyntax>().Single();
        using var scope = SymbolicAnalysisLimitContext.Push(
            SharpProofAnalysisBudget.Default with { MaxMergedPathConditions = 1 });

        var result = SymbolicCfgProgramPointStateCollector.CollectForInitialEntryState(
            forStatement,
            fixture.SemanticModel,
            CancellationToken.None);
        var routed = SymbolicReachabilityService.CollectForInitialEntryState(
            forStatement,
            fixture.SemanticModel,
            CancellationToken.None);
        var expected = SymbolicProgramPointFacts.CollectForInitialEntryState(
            forStatement,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True, result.Provenance.Single().Detail);
        Assert.That(routed.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
        Assert.That(CreateEvidenceKey(routed), Is.EqualTo(CreateEvidenceKey(expected)));
    }

    private static SymbolicState CreateSeed(
        RoslynTestFixture.CompilationFixture fixture,
        SyntaxNode site,
        string parameterName,
        SeedKind kind,
        long numericValue = 7)
    {
        var parameterSyntax = fixture.Root.DescendantNodes()
            .OfType<ParameterSyntax>()
            .Single(parameter => parameter.Identifier.ValueText == parameterName);
        var parameter = fixture.SemanticModel.GetDeclaredSymbol(parameterSyntax)!;
        var variableName = SymbolicFactFactory.GetSmtVariableName(parameter);
        var atoms = kind switch
        {
            SeedKind.Numeric => new SymbolicAtom[]
            {
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    new SymbolicVariableTerm(variableName, SmtValueKind.Int),
                    new SymbolicIntegerConstantTerm(numericValue))
            },
            SeedKind.ReferenceNull => new SymbolicAtom[]
            {
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    new SymbolicVariableTerm(variableName, SmtValueKind.Reference),
                    new SymbolicNullTerm())
            },
            SeedKind.ReferenceNotNull => new SymbolicAtom[]
            {
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.NotEqual,
                    new SymbolicVariableTerm(variableName, SmtValueKind.Reference),
                    new SymbolicNullTerm())
            },
            SeedKind.NullableValue => new SymbolicAtom[]
            {
                new SymbolicTruthAtom(new SymbolicNullableHasValueTerm(variableName)),
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    new SymbolicNullableValueTerm(variableName, SmtValueKind.Int),
                    new SymbolicIntegerConstantTerm(numericValue))
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return new SymbolicState(atoms.Select(atom => SymbolicFact.Exact(
            atom,
            site,
            "test.seed." + kind,
            parameter,
            "seed." + kind)));
    }

    private static bool ContainsSeedEvidence(SymbolicState state) =>
        state.Facts.Concat(state.PathConditions.SelectMany(EnumerateFacts))
            .Any(static fact => fact.Provenance.StartsWith("test.seed.", StringComparison.Ordinal));

    private static LocalDeclarationStatementSyntax GetFinallyMarkerSite(
        RoslynTestFixture.CompilationFixture fixture) =>
        fixture.Root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Single(variable => variable.Identifier.ValueText == "marker")
            .FirstAncestorOrSelf<LocalDeclarationStatementSyntax>()!;

    private static ILocalSymbol GetLocal(
        RoslynTestFixture.CompilationFixture fixture,
        string name) =>
        (ILocalSymbol)fixture.SemanticModel.GetDeclaredSymbol(
            fixture.Root.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Single(variable => variable.Identifier.ValueText == name))!;

    private static ExpressionStatementSyntax GetCoalesceAssignmentStatement(
        RoslynTestFixture.CompilationFixture fixture) =>
        fixture.Root.DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .Single(statement => statement.Expression is AssignmentExpressionSyntax assignment &&
                assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CoalesceAssignmentExpression));

    private static SymbolicState CollectStructuralState(
        RoslynTestFixture.CompilationFixture fixture,
        SyntaxNode site,
        bool includeCurrentStatementCompletionFacts = false) =>
        SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None,
                includeCurrentStatementCompletionFacts));

    private static void AssertStateParity(SymbolicState actual, SymbolicState expected)
    {
        Assert.That(actual.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
        Assert.That(CreateEvidenceKey(actual), Is.EqualTo(CreateEvidenceKey(expected)));
        Assert.That(CreateVersionKey(actual), Is.EqualTo(CreateVersionKey(expected)));
    }

    private static void AssertCacheInfo(SymbolicCacheInfo actual, SymbolicCacheInfo expected)
    {
        Assert.That(actual.Hits, Is.EqualTo(expected.Hits));
        Assert.That(actual.Misses, Is.EqualTo(expected.Misses));
        Assert.That(actual.Entries, Is.EqualTo(expected.Entries));
        Assert.That(actual.Evictions, Is.EqualTo(expected.Evictions));
    }

    private static void AssertLoopTargetMatchesStructural(
        RoslynTestFixture.CompilationFixture fixture,
        SyntaxNode site,
        bool includeCurrentStatementCompletionFacts = false)
    {
        var actual = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: includeCurrentStatementCompletionFacts);
        var expected = SymbolicProgramPointFacts.MergeStates(
            SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                fixture.SemanticModel,
                CancellationToken.None),
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                fixture.SemanticModel,
                CancellationToken.None,
                includeCurrentStatementCompletionFacts));

        Assert.That(actual.IsExact, Is.True, actual.Provenance.Single().Detail);
        Assert.That(actual.Value!.NormalizedProofKey, Is.EqualTo(expected.NormalizedProofKey));
        Assert.That(CreateEvidenceKey(actual.Value), Is.EqualTo(CreateEvidenceKey(expected)));
        Assert.That(
            CreateVersionKey(actual.Value),
            Is.EqualTo(CreateVersionKey(expected)));
    }

    private static string CreateVersionKey(SymbolicState state) =>
        string.Join(
            "\n",
            state.SymbolVersions.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair =>
                pair.Key + ":" + pair.Value));

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

    [TestCase(
        "static class C { static int M(string? text) { if (text != null ? text.Length == 3 : false) { return 1; } return 0; } }")]
    [TestCase(
        "static class C { static int M(int value, int divisor) { if (divisor != 0 ? value / divisor == 3 : false) { return 1; } return 0; } }")]
    public void ConditionalBooleanBranchLocalTarget_MatchesStructuralCollector(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ConditionalBooleanBranchLocalTarget_MatchesStructuralCollector));
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
        var ifCondition = fixture.Root.DescendantNodes().OfType<IfStatementSyntax>().Single().Condition;
        var lowering = SymbolicSemanticPipeline.LowerBranchCondition(
            ifCondition,
            branchWhenTrue: true,
            new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None));
        Assert.That(lowering.IsExact, Is.True, lowering.Provenance.Single().Detail);

        using var smt = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var proofService = new SymbolicProofService(smt);
        var expectedProof = proofService.ClassifyConditionTruth(expected, lowering.Value!);
        var actualProof = proofService.ClassifyConditionTruth(actual.Value!, lowering.Value!);
        Assert.That(actualProof.Info.Status, Is.EqualTo(expectedProof.Info.Status));
        Assert.That(actualProof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue), actualProof.Info.Reason);
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

    [TestCase(
        "static class C { static int M(bool keepGoing) { while (keepGoing) { int value = 1; value++; } return 0; } }")]
    [TestCase(
        "static class C { static int M(bool keepGoing) { do { int value = 1; value++; } while (keepGoing); return 0; } }")]
    public void LoopLocalTarget_MatchesStructuralCollector(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(LoopLocalTarget_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<ExpressionStatementSyntax>().Single();

        AssertLoopTargetMatchesStructural(fixture, site);
    }

    [TestCase(
        "static class C { static int M(bool keepGoing) { int value = 0; while (keepGoing) { value = 1; value = 7; } return value; } }",
        0)]
    [TestCase(
        "static class C { static int M(bool keepGoing) { int value = 0; while (keepGoing) { value = 7; value++; } return value; } }",
        1)]
    [TestCase(
        "static class C { static int M(bool keepGoing) { int value = 0; do { value = 1; value = 7; } while (keepGoing); return value; } }",
        0)]
    [TestCase(
        "static class C { static int M(bool keepGoing) { int value = 0; do { value = 7; value++; } while (keepGoing); return value; } }",
        1)]
    public void LoopCarriedMutationAroundTarget_MatchesStructuralCollector(
        string source,
        int targetIndex)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(LoopCarriedMutationAroundTarget_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<ExpressionStatementSyntax>()
            .ElementAt(targetIndex);

        AssertLoopTargetMatchesStructural(fixture, site);
    }

    [TestCase(
        "static class C { static int M(bool keepGoing) { while (keepGoing) { int value = 1; value = 2; } return 0; } }")]
    [TestCase(
        "static class C { static int M(bool keepGoing) { do { int value = 1; value = 2; } while (keepGoing); return 0; } }")]
    public void LoopLocalExpressionCompletion_MatchesStructuralCollector(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(LoopLocalExpressionCompletion_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<ExpressionStatementSyntax>().Single();

        AssertLoopTargetMatchesStructural(
            fixture,
            site,
            includeCurrentStatementCompletionFacts: true);
    }

    [TestCase(
        "static class C { static int M(bool keepGoing) { while (keepGoing) { int value = 1; } return 0; } }")]
    [TestCase(
        "static class C { static int M(bool keepGoing) { do { int value = 1; } while (keepGoing); return 0; } }")]
    public void LoopLocalDeclarationCompletion_MatchesStructuralCollector(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(LoopLocalDeclarationCompletion_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();

        AssertLoopTargetMatchesStructural(
            fixture,
            site,
            includeCurrentStatementCompletionFacts: true);
    }

    [TestCase(
        "static class C { static int M(bool keepGoing) { while (keepGoing) { { int value = 1; value++; } } return 0; } }")]
    [TestCase(
        "static class C { static int M(bool keepGoing) { do { { int value = 1; value++; } } while (keepGoing); return 0; } }")]
    public void NestedBlockLoopLocalTarget_MatchesStructuralCollector(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(NestedBlockLoopLocalTarget_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<ExpressionStatementSyntax>().Single();

        AssertLoopTargetMatchesStructural(fixture, site);
    }

    [Test]
    public void SingleDoLoopTargetObservation_MatchesStructuralCollector()
    {
        const string source =
            "static class C { static int M() { do { int value = 1; value++; } while (false); return 0; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(SingleDoLoopTargetObservation_MatchesStructuralCollector));
        var site = fixture.Root.DescendantNodes().OfType<ExpressionStatementSyntax>().Single();

        AssertLoopTargetMatchesStructural(fixture, site);
    }

    [TestCase(
        "static class C { static int M(bool keepGoing) { int value = 0; while (keepGoing) { if (value != value) value++; } return value; } }")]
    [TestCase(
        "static class C { static int M(bool keepGoing) { int value = 0; do { if (value != value) value++; } while (keepGoing); return value; } }")]
    public void ContradictoryLoopTargetObservation_RemainsConservativeFallback(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ContradictoryLoopTargetObservation_RemainsConservativeFallback));
        var site = fixture.Root.DescendantNodes().OfType<ExpressionStatementSyntax>().Single();

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True, result.Provenance.Single().Detail);
    }

    [TestCase(
        "static class C { static int M(bool keepGoing) { int value = 0; if (value != value) { while (keepGoing) { int item = 1; item++; } } return value; } }")]
    [TestCase(
        "static class C { static int M(bool keepGoing) { int value = 0; if (value != value) { do { int item = 1; item++; } while (keepGoing); } return value; } }")]
    public void ContradictoryLoopTargetRevisits_RemainConservativeFallback(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ContradictoryLoopTargetRevisits_RemainConservativeFallback));
        var site = fixture.Root.DescendantNodes().OfType<ExpressionStatementSyntax>().Single();

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True, result.Provenance.Single().Detail);
    }

    [TestCase(
        "static class C { static int M(bool keepGoing) { int value = 0; while (keepGoing) { value++; break; } return value; } }",
        false)]
    [TestCase(
        "static class C { static int M(bool keepGoing) { int value = 0; do { value++; continue; } while (keepGoing); return value; } }",
        false)]
    [TestCase(
        "static class C { static int M(bool keepGoing) { int value = 0; while (keepGoing) { value++; } return value; } }",
        true)]
    [TestCase(
        "static class C { static int M() { while (false) { int value = 1; value++; } return 0; } }",
        false)]
    [TestCase(
        "static class C { static int M(bool outer, bool inner) { int value = 0; while (outer) { while (inner) value++; } return value; } }",
        false)]
    public void UnsupportedLoopLocalTargets_RemainConservativeFallback(
        string source,
        bool includeCurrentStatementCompletionFacts = false)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(UnsupportedLoopLocalTargets_RemainConservativeFallback));
        var site = fixture.Root.DescendantNodes().OfType<ExpressionStatementSyntax>().Single();

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            includeCurrentStatementCompletionFacts: includeCurrentStatementCompletionFacts);

        Assert.That(result.IsUnsupported, Is.True, result.Provenance.Single().Detail);
    }

    [TestCase(
        "static class C { static int M() { for (int index = 0; index < 3; index++) { int value = 1; value++; } return 0; } }")]
    [TestCase(
        "static class C { static int M() { foreach (var item in new[] { 1, 2 }) { int value = item; value++; } return 0; } }")]
    public void UnmigratedLoopLocalTargets_RemainConservativeFallback(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(UnmigratedLoopLocalTargets_RemainConservativeFallback));
        var site = fixture.Root.DescendantNodes().OfType<ExpressionStatementSyntax>().Single();

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True);
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

    [Test]
    public void NullableReassignment_MatchesStructuralCollector()
    {
        const string source = "static class C { static int M(int? input) { int? value = null; value = input; return value.Value; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(NullableReassignment_MatchesStructuralCollector));
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
    public void GuardedReferenceAssignmentAfterJoin_MatchesStructuralCollector()
    {
        const string source = "static class C { static int M(bool flag) { var values = new int[1]; if (flag) values = new int[2]; return values[1]; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(GuardedReferenceAssignmentAfterJoin_MatchesStructuralCollector));
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
    public void FinallyLocalFirstStatement_MatchesStructuralCollectorAndInvalidatesProtectedMutation()
    {
        const string source = "static class C { static void M() { int mutated = 1; int retained = 7; try { mutated = 2; } finally { int marker = retained; mutated = marker; } } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(FinallyLocalFirstStatement_MatchesStructuralCollectorAndInvalidatesProtectedMutation));
        var site = GetFinallyMarkerSite(fixture);
        var mutated = GetLocal(fixture, "mutated");
        var retained = GetLocal(fixture, "retained");

        var direct = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var structural = CollectStructuralState(fixture, site);
        var routed = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(direct.IsExact, Is.True, direct.Provenance.Single().Detail);
        AssertStateParity(direct.Value!, structural);
        AssertStateParity(routed, structural);
        Assert.That(SymbolicStateValueFacts.TryGetCurrentValue(direct.Value!, mutated, out _), Is.False);
        Assert.That(SymbolicStateValueFacts.TryGetCurrentValue(direct.Value!, retained, out var retainedValue), Is.True);
        Assert.That(retainedValue, Is.EqualTo(new SymbolicIntegerConstantTerm(7)));
    }

    [Test]
    public void FinallyLocalPriorAssignment_MatchesStructuralCollector()
    {
        const string source = "static class C { static void M() { int value = 1; try { value = 2; } finally { value = 3; int marker = value; } } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(FinallyLocalPriorAssignment_MatchesStructuralCollector));
        var site = GetFinallyMarkerSite(fixture);
        var value = GetLocal(fixture, "value");

        var direct = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var structural = CollectStructuralState(fixture, site);
        var routed = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(direct.IsExact, Is.True, direct.Provenance.Single().Detail);
        AssertStateParity(direct.Value!, structural);
        AssertStateParity(routed, structural);
        Assert.That(SymbolicStateValueFacts.TryGetCurrentValue(direct.Value!, value, out var current), Is.True);
        Assert.That(current, Is.EqualTo(new SymbolicIntegerConstantTerm(3)));
    }

    [TestCaseSource(nameof(FinallyLocalMultipleRegularPathCases))]
    public void FinallyLocalMultipleRegularPaths_RemainConservativeFallback(
        (string Source, string[] Invalidated, string? Restored, long RestoredValue) testCase)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            testCase.Source,
            nameof(FinallyLocalMultipleRegularPaths_RemainConservativeFallback));
        var site = GetFinallyMarkerSite(fixture);

        var direct = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);
        var structural = CollectStructuralState(fixture, site);
        var routed = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(direct.IsUnsupported, Is.True, direct.Provenance.Single().Detail);
        Assert.That(direct.Value, Is.Null);
        Assert.That(direct.Provenance.Single().Detail, Is.EqualTo("finally-local-target"));
        AssertStateParity(routed, structural);
        foreach (var name in testCase.Invalidated)
            Assert.That(
                SymbolicStateValueFacts.TryGetCurrentValue(
                    structural,
                    GetLocal(fixture, name),
                    out _),
                Is.False,
                name);
        if (testCase.Restored != null)
        {
            Assert.That(
                SymbolicStateValueFacts.TryGetCurrentValue(
                    structural,
                    GetLocal(fixture, testCase.Restored),
                    out var restored),
                Is.True);
            Assert.That(restored, Is.EqualTo(new SymbolicIntegerConstantTerm(testCase.RestoredValue)));
        }

        var condition = (IParameterSymbol)fixture.SemanticModel.GetDeclaredSymbol(
            fixture.Root.DescendantNodes()
                .OfType<ParameterSyntax>()
                .Single(parameter => parameter.Identifier.ValueText == "condition"))!;
        var conditionKey = SymbolicFactFactory.GetSmtVariableName(condition);
        Assert.That(structural.Facts.Any(fact =>
            SymbolicIrReferenceScanner.ContainsVariableOrMember(fact, conditionKey)), Is.False);
        Assert.That(structural.PathConditions.Any(pathCondition =>
            SymbolicIrReferenceScanner.ContainsVariableOrMember(pathCondition, conditionKey)), Is.False);
    }

    [TestCaseSource(nameof(FinallyLocalFallbackCases))]
    public void FinallyLocalUnsupportedShapes_PublishNoPartialState(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(FinallyLocalUnsupportedShapes_PublishNoPartialState));
        var site = GetFinallyMarkerSite(fixture);

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True, result.Provenance.Single().Detail);
        Assert.That(result.Value, Is.Null);
    }

    [Test]
    public void FinallyLocalCustomLimits_PublishNoPartialState()
    {
        const string source = "static class C { static void M() { int value = 1; try { value = 2; } finally { int marker = value; } } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(FinallyLocalCustomLimits_PublishNoPartialState));
        var site = GetFinallyMarkerSite(fixture);
        using var scope = SymbolicAnalysisLimitContext.Push(
            SharpProofAnalysisBudget.Default with { MaxMergedPathConditions = 1 });

        var result = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(result.IsUnsupported, Is.True, result.Provenance.Single().Detail);
        Assert.That(result.Value, Is.Null);
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
