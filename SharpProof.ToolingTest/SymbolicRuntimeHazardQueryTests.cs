using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class SymbolicRuntimeHazardQueryTests
{
    public sealed record RuntimeHazardScenario(
        string Name,
        string Source,
        string Marker,
        bool IncludeUnprovenCandidates,
        SymbolicRuntimeHazardKind[] Kinds,
        SymbolicRuntimeHazardKind? Kind,
        SymbolicRuntimeHazardStatus? Status,
        string? ExceptionType,
        string? Category,
        string? Provenance,
        SymbolicProofStatus? ProofStatus,
        SymbolicProofBackend? Backend,
        SymbolicUnknownReason? UnknownReason,
        string? NodeKind,
        string? OperationText);

    private static readonly RuntimeHazardScenario[] RuntimeHazardScenariosPart1 =
    {


        new("QuerySourceRuntimeHazardsLine_ClassifiesLiteralThrowNullAsNullReferenceException", @"
public class TestClass
{
    public void TestMethod()
    {
        throw null;
    }
}", "throw null;", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.DirectThrow, SymbolicRuntimeHazardStatus.Proven, "System.NullReferenceException", "definite_throw_null", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ClassifiesPathProvenThrowMaybeNullAsNullReferenceException", @"
using System;

public class TestClass
{
    public void TestMethod(Exception? error)
    {
        if (error is null)
        {
            throw error;
        }
    }
}", "throw error;", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.DirectThrow, SymbolicRuntimeHazardStatus.Proven, "System.NullReferenceException", "definite_throw_null", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ReportsTypedCoalesceThrowExpression", @"
using System;

public class TestClass
{
    public object TestMethod(object? value)
    {
        return value ?? throw new InvalidOperationException();
    }
}", "return value ?? throw new InvalidOperationException();", false, new[] { SymbolicRuntimeHazardKind.DirectThrow }, SymbolicRuntimeHazardKind.DirectThrow, SymbolicRuntimeHazardStatus.Proven, "System.InvalidOperationException", "direct_throw", null, null, null, null, SyntaxKind.ThrowExpression.ToString(), null),

        new("QuerySourceRuntimeHazardsLine_RemainderDivisorUsesIrPrecondition", @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value % 2 == 0)
        {
            return 10 / (value % 2);
        }

        return 0;
    }
}", "return 10 / (value % 2);", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.DivideByZero, SymbolicRuntimeHazardStatus.Proven, "System.DivideByZeroException", null, "ir.runtime-hazard.divide-by-zero", null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_UnaryMinusDivisorUsesIrPrecondition", @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == 0)
        {
            return 10 / -value;
        }

        return 0;
    }
}", "return 10 / -value;", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.DivideByZero, SymbolicRuntimeHazardStatus.Proven, "System.DivideByZeroException", null, "ir.runtime-hazard.divide-by-zero", null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ConditionalDivisorUsesIrPrecondition", @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        if (flag)
        {
            return 10 / (flag ? 0 : 1);
        }

        return 0;
    }
}", "return 10 / (flag ? 0 : 1);", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.DivideByZero, SymbolicRuntimeHazardStatus.Proven, "System.DivideByZeroException", null, "ir.runtime-hazard.divide-by-zero", null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedCompoundDivideByZero", @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor == 0)
        {
            value /= divisor;
        }

        return value;
    }
}", "value /= divisor;", false, new[] { SymbolicRuntimeHazardKind.DivideByZero }, SymbolicRuntimeHazardKind.DivideByZero, SymbolicRuntimeHazardStatus.Proven, "System.DivideByZeroException", "definite_divide_by_zero", null, null, null, null, null, null),
    };

    private static IEnumerable<TestCaseData> RuntimeHazardScenarios()
    {
        var cases = RuntimeHazardScenariosPart1
            .Concat(RuntimeHazardScenariosPart2)
            .Concat(RuntimeHazardScenariosPart3)
            .Concat(RuntimeHazardScenariosPart4)
            .Concat(RuntimeHazardScenariosPart5)
            .Concat(RuntimeHazardScenariosPart6)
            .Concat(RuntimeHazardScenariosPart7)
            .Concat(RuntimeHazardScenariosPart8)
            .Concat(RuntimeHazardScenariosPart9)
            .Concat(RuntimeHazardScenariosPart10)
            .Concat(RuntimeHazardScenariosPart11)
            .Concat(RuntimeHazardScenariosPart12)
            .ToArray();

        if (cases.Length != 99 ||
            cases.Count(static testCase => testCase.Status == SymbolicRuntimeHazardStatus.Proven) != 71 ||
            cases.Count(static testCase => testCase.Status == SymbolicRuntimeHazardStatus.Unreachable) != 24 ||
            cases.Count(static testCase => testCase.Status == SymbolicRuntimeHazardStatus.Unknown) != 4 ||
            cases.Select(static testCase => testCase.Name).Distinct(StringComparer.Ordinal).Count() != 99)
        {
            throw new InvalidOperationException("Runtime hazard scenario invariants failed.");
        }

        return cases.Select(static testCase => new TestCaseData(testCase).SetName(testCase.Name));
    }

    [TestCaseSource(nameof(RuntimeHazardScenarios))]
    public void QuerySourceRuntimeHazardsLine_Scenarios(RuntimeHazardScenario testCase)
    {
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var options = testCase.IncludeUnprovenCandidates || testCase.Kinds.Length > 0
            ? new SymbolicRuntimeHazardQueryOptions(testCase.IncludeUnprovenCandidates, testCase.Kinds)
            : null;
        var result = QueryLine(testCase.Source, testCase.Marker, smtAnalysis, options);
        var hazard = AssertSingleHazard(result);
        if (testCase.Kind is { } kind) Assert.That(hazard.Kind, Is.EqualTo(kind));
        if (testCase.Status is { } status) Assert.That(hazard.Status, Is.EqualTo(status));
        if (testCase.ExceptionType is { } exceptionType) Assert.That(hazard.ExceptionType, Is.EqualTo(exceptionType));
        if (testCase.Category is { } category) Assert.That(hazard.Category, Is.EqualTo(category));
        if (testCase.Provenance is { } provenance) AssertIrExceptionPrecondition(hazard, provenance);
        if (testCase.ProofStatus is { } proofStatus) Assert.That(hazard.Proof.Status, Is.EqualTo(proofStatus));
        if (testCase.Backend is { } backend) Assert.That(hazard.Proof.Backend, Is.EqualTo(backend));
        if (testCase.UnknownReason is { } unknownReason) Assert.That(hazard.Proof.UnknownReason, Is.EqualTo(unknownReason));
        if (testCase.NodeKind is { } nodeKind) Assert.That(hazard.NodeKind, Is.EqualTo(nodeKind));
        if (testCase.OperationText is { } operationText) Assert.That(hazard.OperationText, Is.EqualTo(operationText));
    }

















    [TestCase("/")]
    [TestCase("%")]
    public void QuerySourceRuntimeHazardsLine_IntegralCastDivisorRetainsTypedConversion(string operation)
    {
        var statement = "return value " + operation + " (int)raw;";
        var source = @"
public class TestClass
{
    public int TestMethod(int value, double raw)
    {
        if ((int)raw == 0)
        {
            " + statement + @"
        }

        return 0;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            statement,
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                kinds: new[] { SymbolicRuntimeHazardKind.DivideByZero }));

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(hazard.TriggerPrecondition!.Text, Does.Contain("numeric-conversion"));
        Assert.That(hazard.TriggerPrecondition.Provenance,
            Is.EqualTo("ir.runtime-hazard.divide-by-zero"));
    }

    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownDivideByZeroCandidate()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        return 10 / divisor;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(source, "return 10 / divisor;", smtAnalysis);
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return 10 / divisor;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(true));
        Assert.That(candidateResult.Hazards, Has.Count.EqualTo(2));
        Assert.That(
            candidateResult.Hazards.Any(hazard =>
                hazard.Kind == SymbolicRuntimeHazardKind.DivideByZero &&
                hazard.Status == SymbolicRuntimeHazardStatus.Unknown),
            Is.True);
        Assert.That(
            candidateResult.Hazards.Any(hazard =>
                hazard.Kind == SymbolicRuntimeHazardKind.CheckedIntegralOverflow &&
                hazard.Status == SymbolicRuntimeHazardStatus.Unreachable &&
                string.Equals(hazard.ExceptionType, "System.OverflowException", StringComparison.Ordinal)),
            Is.True);
    }



    private static readonly RuntimeHazardScenario[] RuntimeHazardScenariosPart2 =
    {
        new("QuerySourceRuntimeHazardsLine_GuardedCompoundModuloByNonZeroIsPruned", @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor != 0)
        {
            value %= divisor;
        }

        return value;
    }
}", "value %= divisor;", true, new[] { SymbolicRuntimeHazardKind.DivideByZero }, SymbolicRuntimeHazardKind.DivideByZero, SymbolicRuntimeHazardStatus.Unreachable, "System.DivideByZeroException", "definite_modulo_by_zero", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedNullDereference", @"
public class TestClass
{
    public int TestMethod(string? value)
    {
        if (value is null)
        {
            return value.Length;
        }

        return 0;
    }
}", "return value.Length;", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Proven, "System.NullReferenceException", null, null, null, null, null, null, null),

        new("QuerySourceRuntimeHazardsLine_CoalescedReceiverNullDereferenceUsesIrPrecondition", @"
public class TestClass
{
    public int TestMethod(string? left, string? right)
    {
        if (left is null && right is null)
        {
            return (left ?? right).Length;
        }

        return 0;
    }
}", "return (left ?? right).Length;", false, new[] { SymbolicRuntimeHazardKind.NullDereference }, SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Proven, "System.NullReferenceException", null, "ir.runtime-hazard.null-dereference", null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_StaticStringEqualsNullGuardUsesIrPrecondition", @"
public class TestClass
{
    public int TestMethod(string? value)
    {
        string? other = null;
        if (string.Equals(value, other))
        {
            return value.Length;
        }

        return 0;
    }
}", "return value.Length;", false, new[] { SymbolicRuntimeHazardKind.NullDereference }, SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Proven, "System.NullReferenceException", null, "ir.runtime-hazard.null-dereference", null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ObjectReferenceEqualsNullGuardUsesIrPrecondition", @"
public class TestClass
{
    public int TestMethod(string? value)
    {
        if (object.ReferenceEquals(value, null))
        {
            return value.Length;
        }

        return 0;
    }
}", "return value.Length;", false, new[] { SymbolicRuntimeHazardKind.NullDereference }, SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Proven, "System.NullReferenceException", null, "ir.runtime-hazard.null-dereference", null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesWithExpressionNullReceiverDereference", @"
public record Person(string Name);

public class TestClass
{
    public Person TestMethod(Person? person)
    {
        if (person is null)
        {
            return person with { Name = ""fallback"" };
        }

        return person;
    }
}", "return person with { Name = \"fallback\" };", false, new[] { SymbolicRuntimeHazardKind.NullDereference }, SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Proven, "System.NullReferenceException", "definite_with_null", null, null, null, null, SyntaxKind.WithExpression.ToString(), null),
        new("QuerySourceRuntimeHazardsLine_PrunesWithExpressionNullReceiverAfterNonNullGuard", @"
public record Person(string Name);

public class TestClass
{
    public Person TestMethod(Person? person)
    {
        if (person is not null)
        {
            return person with { Name = ""safe"" };
        }

        return new Person(""fallback"");
    }
}", "return person with { Name = \"safe\" };", true, new[] { SymbolicRuntimeHazardKind.NullDereference }, SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Unreachable, "System.NullReferenceException", "definite_with_null", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesDeconstructionNullReceiverDereference", @"
public sealed class Pair
{
    public void Deconstruct(out int left, out int right)
    {
        left = 1;
        right = 2;
    }
}

public class TestClass
{
    public int TestMethod(Pair? pair)
    {
        if (pair is null)
        {
            int left;
            int right;
            (left, right) = pair;
            return left + right;
        }

        return 0;
    }
}", "(left, right) = pair;", false, new[] { SymbolicRuntimeHazardKind.NullDereference }, SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Proven, "System.NullReferenceException", "definite_deconstruction_null", null, null, null, null, SyntaxKind.SimpleAssignmentExpression.ToString(), null),
        new("QuerySourceRuntimeHazardsLine_PrunesDeconstructionNullReceiverAfterNonNullGuard", @"
public sealed class Pair
{
    public void Deconstruct(out int left, out int right)
    {
        left = 1;
        right = 2;
    }
}

public class TestClass
{
    public int TestMethod(Pair? pair)
    {
        if (pair is not null)
        {
            int left;
            int right;
            (left, right) = pair;
            return left + right;
        }

        return 0;
    }
}", "(left, right) = pair;", true, new[] { SymbolicRuntimeHazardKind.NullDereference }, SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Unreachable, "System.NullReferenceException", "definite_deconstruction_null", null, null, null, null, null, null),
    };

    [Test]
    public void QueryNodeRuntimeHazards_DefaultExcludesNestedCallableHazards()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        string? text = null;
        int Local() => text.Length;
        int divisor = 0;
        return value / divisor;
    }
}";

        var (method, semanticModel) = CreateMethodContext(source, "TestMethod");
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicRuntimeHazardQueryService().QueryNodeRuntimeHazards(
            method,
            semanticModel,
            smtAnalysis);

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DivideByZero));
        Assert.That(hazard.OperationText, Is.EqualTo("value / divisor"));
        Assert.That(result.ScopeStart, Is.EqualTo(method.SpanStart));
        Assert.That(result.ScopeEnd, Is.EqualTo(method.Span.End));
    }

    [Test]
    public void QueryNodeRuntimeHazards_CanIncludeNestedCallableHazards()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        string? text = null;
        int Local() => text.Length;
        int divisor = 0;
        return value / divisor;
    }
}";

        var (method, semanticModel) = CreateMethodContext(source, "TestMethod");
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicRuntimeHazardQueryService().QueryNodeRuntimeHazards(
            method,
            semanticModel,
            smtAnalysis,
            includeNestedCallables: true);

        Assert.That(result.Hazards.Select(hazard => hazard.Kind), Is.EquivalentTo(new[]
        {
            SymbolicRuntimeHazardKind.NullDereference,
            SymbolicRuntimeHazardKind.DivideByZero
        }));
    }



    [Test]
    public void QuerySourceRuntimeHazardsLine_PrunesNotNullReturnContractReceiver()
    {
        const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public class TestClass
{
    [return: NotNull]
    private static string? Read() => null;

    public int TestMethod()
    {
        return Read().Length;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return Read().Length;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                includeUnprovenCandidates: true,
                kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

        Assert.That(result.Hazards, Is.Empty);
    }

    [Test]
    public void QuerySourceRuntimeHazardsLine_RetainsMaybeNullReturnContractReceiver()
    {
        const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public class TestClass
{
    [return: MaybeNull]
    private static string Read() => null;

    public int TestMethod()
    {
        var value = Read();
        return value.Length;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return value.Length;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                includeUnprovenCandidates: true,
                kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

        Assert.That(result.Hazards, Has.Count.EqualTo(1));
        Assert.That(result.Hazards[0].Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullDereference));
    }

















    private static readonly RuntimeHazardScenario[] RuntimeHazardScenariosPart3 =
    {
        new("QuerySourceRuntimeHazardsLine_ProvesDeconstructionDeclarationNullReceiverDereference", @"
public sealed class Pair
{
    public void Deconstruct(out int left, out int right)
    {
        left = 1;
        right = 2;
    }
}

public class TestClass
{
    public int TestMethod(Pair? pair)
    {
        if (pair is null)
        {
            var (left, right) = pair;
            return left + right;
        }

        return 0;
    }
}", "var (left, right) = pair;", false, new[] { SymbolicRuntimeHazardKind.NullDereference }, SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Proven, "System.NullReferenceException", "definite_deconstruction_null", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesForeachNullSourceDereference", @"
public class TestClass
{
    public int TestMethod(string[] values)
    {
        if (values is null)
        {
            foreach (var value in values)
            {
                return value.Length;
            }
        }

        return 0;
    }
}", "foreach (var value in values)", false, new[] { SymbolicRuntimeHazardKind.NullDereference }, SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Proven, "System.NullReferenceException", null, null, null, null, null, SyntaxKind.ForEachStatement.ToString(), null),
        new("QuerySourceRuntimeHazardsLine_ClassifiesForeachNullSourceAfterNonNullGuardAsUnreachableCandidate", @"
public class TestClass
{
    public int TestMethod(string[] values)
    {
        if (values is not null)
        {
            foreach (var value in values)
            {
                return value.Length;
            }
        }

        return 0;
    }
}", "foreach (var value in values)", true, new[] { SymbolicRuntimeHazardKind.NullDereference }, SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Unreachable, null, null, null, null, null, null, SyntaxKind.ForEachStatement.ToString(), null),
        new("QuerySourceRuntimeHazardsLine_ProvesAwaitNullDereference", @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> TestMethod()
    {
        Task<int> task = null!;
        return await task;
    }
}", "return await task;", false, new[] { SymbolicRuntimeHazardKind.NullDereference }, SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Proven, "System.NullReferenceException", "definite_await_null", null, null, null, null, SyntaxKind.AwaitExpression.ToString(), null),
        new("QuerySourceRuntimeHazardsLine_ClassifiesAwaitNullDereferenceAfterNonNullGuardAsUnreachableCandidate", @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> TestMethod(Task<int> task)
    {
        if (task is not null)
        {
            return await task;
        }

        return 0;
    }
}", "return await task;", true, new[] { SymbolicRuntimeHazardKind.NullDereference }, SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Unreachable, null, "definite_await_null", null, null, null, null, SyntaxKind.AwaitExpression.ToString(), null),
        new("QuerySourceRuntimeHazardsLine_ProvesLockNullSourceArgumentNull", @"
public class TestClass
{
    public int TestMethod(object gate)
    {
        if (gate is null)
        {
            lock (gate)
            {
                return 1;
            }
        }

        return 0;
    }
}", "lock (gate)", false, new[] { SymbolicRuntimeHazardKind.ArgumentNull }, SymbolicRuntimeHazardKind.ArgumentNull, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentNullException", "definite_lock_null", null, null, null, null, SyntaxKind.LockStatement.ToString(), null),
        new("QuerySourceRuntimeHazardsLine_ClassifiesLockNullSourceAfterNonNullGuardAsUnreachableCandidate", @"
public class TestClass
{
    public int TestMethod(object gate)
    {
        if (gate is not null)
        {
            lock (gate)
            {
                return 1;
            }
        }

        return 0;
    }
}", "lock (gate)", true, new[] { SymbolicRuntimeHazardKind.ArgumentNull }, SymbolicRuntimeHazardKind.ArgumentNull, SymbolicRuntimeHazardStatus.Unreachable, "System.ArgumentNullException", null, null, null, null, null, SyntaxKind.LockStatement.ToString(), null),


        new("QuerySourceRuntimeHazardsLine_ProvesDynamicInvocationNullBinding", @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value.Missing();
    }
}", "return value.Missing();", false, new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }, SymbolicRuntimeHazardKind.DynamicNullBinding, SymbolicRuntimeHazardStatus.Proven, "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException", "definite_dynamic_invocation_null_binding", null, null, null, null, null, null),
    };

    [Test]
    public void QuerySourceRuntimeHazards_DoesNotTreatExtensionDeconstructionNullSourceAsImplicitDereference()
    {
        const string source = @"
public sealed class Pair
{
}

public static class PairExtensions
{
    public static void Deconstruct(this Pair pair, out int left, out int right)
    {
        left = 1;
        right = 2;
    }
}

public class TestClass
{
    public int TestMethod(Pair? pair)
    {
        if (pair is null)
        {
            int left;
            int right;
            (left, right) = pair;
            return left + right;
        }

        return 0;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "(left, right) = pair;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.NullDereference }));

        Assert.That(result.Hazards, Is.Empty);
    }



    [Test]
    public void QuerySourceRuntimeHazardsLine_PrunesForeachNullSourceAfterNonNullGuard()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(string[] values)
    {
        if (values is not null)
        {
            foreach (var value in values)
            {
                return value.Length;
            }
        }

        return 0;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "foreach (var value in values)",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

        Assert.That(result.Hazards, Is.Empty);
    }





    [Test]
    public void QuerySourceRuntimeHazardsLine_PrunesAwaitNullDereferenceAfterNonNullGuard()
    {
        const string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> TestMethod(Task<int> task)
    {
        if (task is not null)
        {
            return await task;
        }

        return 0;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return await task;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

        Assert.That(result.Hazards, Is.Empty);
    }





    [Test]
    public void QuerySourceRuntimeHazardsLine_PrunesLockNullSourceAfterNonNullGuard()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(object gate)
    {
        if (gate is not null)
        {
            lock (gate)
            {
                return 1;
            }
        }

        return 0;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "lock (gate)",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentNull }));

        Assert.That(result.Hazards, Is.Empty);
    }









    private static readonly RuntimeHazardScenario[] RuntimeHazardScenariosPart4 =
    {
        new("QuerySourceRuntimeHazardsLine_ProvesCastedDynamicInvocationNullBinding", @"
public class TestClass
{
    public object TestMethod()
    {
        return ((dynamic)null).Missing();
    }
}", "return ((dynamic)null).Missing();", false, new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }, SymbolicRuntimeHazardKind.DynamicNullBinding, SymbolicRuntimeHazardStatus.Proven, "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException", "definite_dynamic_invocation_null_binding", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesDynamicDirectInvocationNullBinding", @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value();
    }
}", "return value();", false, new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }, SymbolicRuntimeHazardKind.DynamicNullBinding, SymbolicRuntimeHazardStatus.Proven, "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException", "definite_dynamic_invocation_null_binding", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesCastedDynamicDirectInvocationNullBinding", @"
public class TestClass
{
    public object TestMethod()
    {
        return ((dynamic)null)();
    }
}", "return ((dynamic)null)();", false, new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }, SymbolicRuntimeHazardKind.DynamicNullBinding, SymbolicRuntimeHazardStatus.Proven, "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException", "definite_dynamic_invocation_null_binding", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesDynamicIndexerNullBinding", @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value[0];
    }
}", "return value[0];", false, new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }, SymbolicRuntimeHazardKind.DynamicNullBinding, SymbolicRuntimeHazardStatus.Proven, "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException", "definite_dynamic_index_null_binding", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesCastedDynamicIndexerNullBinding", @"
public class TestClass
{
    public object TestMethod()
    {
        return ((dynamic)null)[0];
    }
}", "return ((dynamic)null)[0];", false, new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }, SymbolicRuntimeHazardKind.DynamicNullBinding, SymbolicRuntimeHazardStatus.Proven, "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException", "definite_dynamic_index_null_binding", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazards_NonNullDynamicReceiverPrunesNullBindingCandidate", @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = new object();
        return value.ToString();
    }
}", "return value.ToString();", true, new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }, SymbolicRuntimeHazardKind.DynamicNullBinding, SymbolicRuntimeHazardStatus.Unreachable, null, null, null, null, null, null, null, null),



        new("QuerySourceRuntimeHazardsLine_ProvesNullableExplicitCastWithoutValue", @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (!value.HasValue)
        {
            return (int)value;
        }

        return 0;
    }
}", "return (int)value;", false, new[] { SymbolicRuntimeHazardKind.NullableValueWithoutValue }, SymbolicRuntimeHazardKind.NullableValueWithoutValue, SymbolicRuntimeHazardStatus.Proven, "System.InvalidOperationException", "definite_nullable_value_without_value", null, null, null, null, null, null),
    };




    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownDynamicNullBindingCandidate()
    {
        const string source = @"
public class TestClass
{
    public object TestMethod(dynamic value)
    {
        return value.Missing;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "return value.Missing;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return value.Missing;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));
        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
    }




    [Test]
    public void QuerySourceRuntimeHazards_SuppressesNullableValueAfterCoalesceFallbackAssignment()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int? left)
    {
        int? value = left ?? 5;
        return value.Value;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return value.Value;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[]
                { SymbolicRuntimeHazardKind.NullableValueWithoutValue }));

        Assert.That(result.Hazards, Is.Empty);
    }



    [Test]
    public void QuerySourceRuntimeHazards_GuardedNullableExplicitCastIsPruned()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (value.HasValue)
        {
            return (int)value;
        }

        return 0;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "return (int)value;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[]
                { SymbolicRuntimeHazardKind.NullableValueWithoutValue }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return (int)value;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.NullableValueWithoutValue }));

        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullableValueWithoutValue));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
        Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidOperationException"));
        Assert.That(hazard.Category, Is.EqualTo("definite_nullable_value_without_value"));
    }

    private static readonly RuntimeHazardScenario[] RuntimeHazardScenariosPart5 =
    {
        new("QuerySourceRuntimeHazards_ProvesNullableValueFromCompletedTaskAwait", @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> TestMethod()
    {
        int? value = await Task.FromResult<int?>(null);
        return value.Value;
    }
}", "return value.Value;", false, new[] { SymbolicRuntimeHazardKind.NullableValueWithoutValue }, SymbolicRuntimeHazardKind.NullableValueWithoutValue, SymbolicRuntimeHazardStatus.Proven, "System.InvalidOperationException", null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazards_ProvesNullableValueFromCompletedValueTaskResult", @"
using System.Threading.Tasks;

public class TestClass
{
    public int TestMethod()
    {
        int? value = new ValueTask<int?>((int?)null).Result;
        return value.Value;
    }
}", "return value.Value;", false, new[] { SymbolicRuntimeHazardKind.NullableValueWithoutValue }, SymbolicRuntimeHazardKind.NullableValueWithoutValue, SymbolicRuntimeHazardStatus.Proven, "System.InvalidOperationException", null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazards_ProvesNullDereferenceFromCompletedTaskGetResult", @"
using System.Threading.Tasks;

public class TestClass
{
    public int TestMethod()
    {
        string? value = Task.FromResult<string?>(null).GetAwaiter().GetResult();
        return value.Length;
    }
}", "return value.Length;", false, new[] { SymbolicRuntimeHazardKind.NullDereference }, SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Proven, "System.NullReferenceException", null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazards_ProvesNullDereferenceFromCompletedTaskResultProperty", @"
using System.Threading.Tasks;

public class TestClass
{
    public int TestMethod()
    {
        string? value = Task.FromResult<string?>(null).Result;
        return value.Length;
    }
}", "return value.Length;", false, new[] { SymbolicRuntimeHazardKind.NullDereference }, SymbolicRuntimeHazardKind.NullDereference, SymbolicRuntimeHazardStatus.Proven, null, null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazards_ProvesNullableValueFromCompletedValueTaskAwait", @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> TestMethod()
    {
        int? value = await ValueTask.FromResult<int?>(null);
        return value.Value;
    }
}", "return value.Value;", false, new[] { SymbolicRuntimeHazardKind.NullableValueWithoutValue }, SymbolicRuntimeHazardKind.NullableValueWithoutValue, SymbolicRuntimeHazardStatus.Proven, null, null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesUnboxNullCast", @"
public class TestClass
{
    public int TestMethod()
    {
        object value = null;
        return (int)value;
    }
}", "return (int)value;", false, new[] { SymbolicRuntimeHazardKind.UnboxNull }, SymbolicRuntimeHazardKind.UnboxNull, SymbolicRuntimeHazardStatus.Proven, "System.NullReferenceException", "definite_unbox_null", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesInvalidReferenceCast", @"
public class TestClass
{
    public string TestMethod()
    {
        object value = new object();
        return (string)value;
    }
}", "return (string)value;", false, new[] { SymbolicRuntimeHazardKind.InvalidCast }, SymbolicRuntimeHazardKind.InvalidCast, SymbolicRuntimeHazardStatus.Proven, "System.InvalidCastException", "definite_invalid_cast", null, null, null, null, null, null),

        new("QuerySourceRuntimeHazardsLine_ProvesInvalidCastAfterAsCastNullAndSourceNonNull", @"
public class TestClass
{
    public string TestMethod(object value)
    {
        var text = value as string;
        if (text == null && value != null)
        {
            return (string)value;
        }

        return string.Empty;
    }
}", "return (string)value;", false, new[] { SymbolicRuntimeHazardKind.InvalidCast }, SymbolicRuntimeHazardKind.InvalidCast, SymbolicRuntimeHazardStatus.Proven, "System.InvalidCastException", "definite_invalid_cast", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesInvalidCastAfterInlineAsAssignmentNullAndSourceNonNull", @"
public class TestClass
{
    public string TestMethod(object value)
    {
        string text;
        if ((text = value as string) == null && value != null)
        {
            return (string)value;
        }

        return string.Empty;
    }
}", "return (string)value;", false, new[] { SymbolicRuntimeHazardKind.InvalidCast }, SymbolicRuntimeHazardKind.InvalidCast, SymbolicRuntimeHazardStatus.Proven, "System.InvalidCastException", "definite_invalid_cast", null, null, null, null, null, null),
    };





    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownUnboxNullCastCandidate()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(object value)
    {
        return (int)value;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "return (int)value;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.UnboxNull }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return (int)value;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.UnboxNull }));
        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.UnboxNull));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
    }



    [Test]
    public void QuerySourceRuntimeHazards_SuppressesInvalidCastAfterAsCastNonNullGuard()
    {
        const string source = @"
public class TestClass
{
    public string TestMethod(object value)
    {
        var text = value as string;
        if (text != null)
        {
            return (string)value;
        }

        return string.Empty;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return (string)value;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));

        Assert.That(result.Hazards, Is.Empty);
    }


    [Test]
    public void QuerySourceRuntimeHazards_SuppressesInvalidCastAfterInlineAsAssignmentNonNullGuard()
    {
        const string source = @"
public class TestClass
{
    public string TestMethod(object value)
    {
        string text;
        if ((text = value as string) != null)
        {
            return (string)value;
        }

        return string.Empty;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return (string)value;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));

        Assert.That(result.Hazards, Is.Empty);
    }


    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownInvalidCastAfterNegativeTypeTest()
    {
        const string source = @"
public class TestClass
{
    public string TestMethod(object value)
    {
        if (value is not string)
        {
            return (string)value;
        }

        return string.Empty;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "return (string)value;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return (string)value;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.InvalidCast }));
        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.InvalidCast));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
    }

    private static readonly RuntimeHazardScenario[] RuntimeHazardScenariosPart6 =
    {
        new("QuerySourceRuntimeHazardsLine_ProvesInvalidUnboxCast", @"
public class TestClass
{
    public long TestMethod()
    {
        object value = 1;
        return (long)value;
    }
}", "return (long)value;", false, new[] { SymbolicRuntimeHazardKind.InvalidCast }, SymbolicRuntimeHazardKind.InvalidCast, SymbolicRuntimeHazardStatus.Proven, "System.InvalidCastException", "definite_invalid_cast", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesBuiltInIndexOutOfRange", @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values.Length == 0)
        {
            return values[0];
        }

        return 0;
    }
}", "return values[0];", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.IndexOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.IndexOutOfRangeException", null, "ir.runtime-hazard.index.out-of-range", null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_NegativeFromEndIndexReportsConstructionFailure", @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        return values[^-1];
    }
}", "return values[^-1];", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentOutOfRangeException", "definite_index_construction_argument_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesArrayGetValueIndexOutOfRange", @"
public class TestClass
{
    public object TestMethod(int[] values)
    {
        return values.GetValue(values.Length);
    }
}", "return values.GetValue(values.Length);", false, new[] { SymbolicRuntimeHazardKind.IndexOutOfRange }, SymbolicRuntimeHazardKind.IndexOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.IndexOutOfRangeException", "definite_array_get_value_index_out_of_range", "ir.runtime-hazard.array-get-value.index-out-of-range", null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_GuardedMultidimensionalArrayGetValueIndexOutOfRangeIsPruned", @"
public class TestClass
{
    public object TestMethod(int[,] values, int row, int column)
    {
        if (row >= 0 && row < values.GetLength(0) && column >= 0 && column < values.GetLength(1))
        {
            return values.GetValue(row, column);
        }

        return 0;
    }
}", "return values.GetValue(row, column);", true, new[] { SymbolicRuntimeHazardKind.IndexOutOfRange }, SymbolicRuntimeHazardKind.IndexOutOfRange, SymbolicRuntimeHazardStatus.Unreachable, "System.IndexOutOfRangeException", "definite_array_get_value_index_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazards_AssignedModuloIndexUnderPositiveLengthGuardIsUnreachable", @"
public class TestClass
{
    public int TestMethod(int[] values, int hash)
    {
        if (values.Length > 0 && hash >= 0)
        {
            var index = hash % values.Length;
            return values[index];
        }

        return 0;
    }
}", "return values[index];", true, new[] { SymbolicRuntimeHazardKind.IndexOutOfRange }, SymbolicRuntimeHazardKind.IndexOutOfRange, SymbolicRuntimeHazardStatus.Unreachable, "System.IndexOutOfRangeException", null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazards_AssignedAbsModuloIndexUnderPositiveLengthGuardIsUnreachable", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int hash)
    {
        if (values.Length > 0)
        {
            var index = Math.Abs(hash % values.Length);
            return values[index];
        }

        return 0;
    }
}", "return values[index];", true, new[] { SymbolicRuntimeHazardKind.IndexOutOfRange }, SymbolicRuntimeHazardKind.IndexOutOfRange, SymbolicRuntimeHazardStatus.Unreachable, "System.IndexOutOfRangeException", null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazards_DirectAbsModuloIndexIsUnreachable", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int hash)
    {
        return values[Math.Abs(hash % values.Length)];
    }
}", "return values[Math.Abs(hash % values.Length)];", true, new[] { SymbolicRuntimeHazardKind.IndexOutOfRange }, SymbolicRuntimeHazardKind.IndexOutOfRange, SymbolicRuntimeHazardStatus.Unreachable, "System.IndexOutOfRangeException", null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesObjectErasedArrayCastAliasIndexOutOfRange", @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        object boxed = values;
        var alias = (int[])boxed;
        return alias[4];
    }
}", "return alias[4];", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.IndexOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.IndexOutOfRangeException", null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesAssignedSpanSliceIndexOutOfRange", @"
using System;

public class TestClass
{
    public int TestMethod(ReadOnlySpan<int> values)
    {
        var tail = values.Slice(values.Length);
        return tail[0];
    }
}", "return tail[0];", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.IndexOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.IndexOutOfRangeException", null, null, null, null, null, null, null),
    };
    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesCompatibleCast()
    {
        const string source = @"
public class TestClass
{
    public string TestMethod()
    {
        object value = ""text"";
        return (string)value;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return (string)value;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));

        Assert.That(result.Hazards, Is.Empty);
    }










    private static readonly RuntimeHazardScenario[] RuntimeHazardScenariosPart7 =
    {
        new("QuerySourceRuntimeHazardsLine_ProvesBuiltInRangeOutOfRange", @"
public class TestClass
{
    public string TestMethod(string value)
    {
        if (value.Length == 0)
        {
            return value[1..];
        }

        return value;
    }
}", "return value[1..];", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentOutOfRangeException", null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesStringSubstringArgumentOutOfRange", @"
public class TestClass
{
    public string TestMethod(string value)
    {
        return value.Substring(value.Length + 1);
    }
}", "return value.Substring(value.Length + 1);", false, new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }, SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentOutOfRangeException", "definite_string_substring_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_StringRemoveZeroCountAtLengthIsPruned", @"
public class TestClass
{
    public string TestMethod(string value)
    {
        if (value.Length >= 0)
        {
            return value.Remove(value.Length, 0);
        }

        return value;
    }
}", "return value.Remove(value.Length, 0);", true, new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }, SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Unreachable, null, "definite_string_remove_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesSpanSliceArgumentOutOfRange", @"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values)
    {
        return values.Slice(values.Length + 1);
    }
}", "return values.Slice(values.Length + 1);", false, new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }, SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentOutOfRangeException", "definite_slice_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_OverflowProneSpanSliceGuardRemainsUnknown", @"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values, int start, int length)
    {
        if (start >= 0 && length >= 0 && start + length <= values.Length)
        {
            return values.Slice(start, length);
        }

        return values;
    }
}", "return values.Slice(start, length);", true, new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }, SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Unknown, null, "definite_slice_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesSpanSliceUncheckedAddOverflowArgumentOutOfRange", @"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values)
    {
        var start = int.MaxValue;
        var length = 1;
        if (start >= 0 && length >= 0 && start + length <= values.Length)
        {
            return values.Slice(start, length);
        }

        return values;
    }
}", "return values.Slice(start, length);", false, new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }, SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentOutOfRangeException", "definite_slice_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesArrayAsSpanArgumentOutOfRange", @"
using System;

public class TestClass
{
    public Span<int> TestMethod(int[] values)
    {
        return values.AsSpan(values.Length + 1);
    }
}", "return values.AsSpan(values.Length + 1);", false, new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }, SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentOutOfRangeException", "definite_memory_extensions_as_span_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_OverflowProneStringAsSpanGuardRemainsUnknown", @"
using System;

public class TestClass
{
    public ReadOnlySpan<char> TestMethod(string value, int start, int length)
    {
        if (start >= 0 && length >= 0 && start + length <= value.Length)
        {
            return value.AsSpan(start, length);
        }

        return value.AsSpan();
    }
}", "return value.AsSpan(start, length);", true, new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }, SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Unknown, null, "definite_memory_extensions_as_span_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesStringAsMemoryArgumentOutOfRange", @"
using System;

public class TestClass
{
    public ReadOnlyMemory<char> TestMethod(string value)
    {
        return value.AsMemory(value.Length + 1);
    }
}", "return value.AsMemory(value.Length + 1);", false, new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }, SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentOutOfRangeException", "definite_memory_extensions_as_memory_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_OverflowProneArrayAsMemoryGuardRemainsUnknown", @"
using System;

public class TestClass
{
    public Memory<int> TestMethod(int[] values, int start, int length)
    {
        if (start >= 0 && length >= 0 && start + length <= values.Length)
        {
            return values.AsMemory(start, length);
        }

        return values.AsMemory();
    }
}", "return values.AsMemory(start, length);", true, new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }, SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Unknown, null, "definite_memory_extensions_as_memory_out_of_range", null, null, null, null, null, null),
    };

    [Test]
    public void QuerySourceRuntimeHazardsLine_GuardedStringSubstringArgumentOutOfRangeIsPruned()
    {
        const string source = @"
public class TestClass
{
    public string TestMethod(string value, int start)
    {
        if (start >= 0 && start <= value.Length)
        {
            return value.Substring(start);
        }

        return value;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "return value.Substring(start);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return value.Substring(start);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
        Assert.That(hazard.Category, Is.EqualTo("definite_string_substring_out_of_range"));
    }

    [Test]
    public void QuerySourceRuntimeHazardsLine_StringRemoveStartAtLengthIsValid()
    {
        const string source = @"
public class TestClass
{
    public string TestMethod(string value)
    {
        return value.Remove(value.Length);
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return value.Remove(value.Length);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

        Assert.That(result.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return value.Remove(value.Length);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
        Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
        Assert.That(hazard.Category, Is.EqualTo("definite_string_remove_out_of_range"));
    }









    private static readonly RuntimeHazardScenario[] RuntimeHazardScenariosPart8 =
    {
        new("QuerySourceRuntimeHazardsLine_ProvesArrayAsMemoryUncheckedAddOverflowArgumentOutOfRange", @"
using System;

public class TestClass
{
    public Memory<int> TestMethod(int[] values)
    {
        var start = int.MaxValue;
        var length = 1;
        if (start >= 0 && length >= 0 && start + length <= values.Length)
        {
            return values.AsMemory(start, length);
        }

        return values.AsMemory();
    }
}", "return values.AsMemory(start, length);", false, new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }, SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentOutOfRangeException", "definite_memory_extensions_as_memory_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesMemorySliceNegativeLengthArgumentOutOfRange", @"
using System;

public class TestClass
{
    public Memory<int> TestMethod(Memory<int> values)
    {
        return values.Slice(0, -1);
    }
}", "return values.Slice(0, -1);", false, new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }, SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentOutOfRangeException", "definite_slice_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_GuardedRangeAccessArgumentOutOfRangeStillPruned", @"
public class TestClass
{
    public string TestMethod(string value, int start, int end)
    {
        if (start >= 0 && start <= end && end <= value.Length)
        {
            return value[start..end];
        }

        return value;
    }
}", "return value[start..end];", true, new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }, SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Unreachable, null, "definite_range_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesListIndexerArgumentOutOfRange", @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(List<int> values)
    {
        if (values.Count == 0)
        {
            return values[0];
        }

        return 0;
    }
}", "return values[0];", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentOutOfRangeException", "definite_count_index_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesReadOnlyListIndexerArgumentOutOfRange", @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
        {
            return values[0];
        }

        return 0;
    }
}", "return values[0];", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentOutOfRangeException", "definite_count_index_out_of_range", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesListIndexFromEndArgumentOutOfRange", @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(List<int> values)
    {
        if (values.Count == 0)
        {
            return values[^1];
        }

        return 0;
    }
}", "return values[^1];", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentOutOfRangeException", "definite_count_index_out_of_range", null, null, null, null, null, null),

        new("QuerySourceRuntimeHazardsLine_PrunesQueueDequeueAfterCountGuard", @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(Queue<int> values)
    {
        if (values.Count > 0)
        {
            return values.Dequeue();
        }

        return 0;
    }
}", "return values.Dequeue();", true, new[] { SymbolicRuntimeHazardKind.InvalidCollectionCardinality }, SymbolicRuntimeHazardKind.InvalidCollectionCardinality, SymbolicRuntimeHazardStatus.Unreachable, null, null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesStackPopOnEmptyCollection", @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(Stack<int> values)
    {
        if (values.Count == 0)
        {
            return values.Pop();
        }

        return 0;
    }
}", "return values.Pop();", false, new[] { SymbolicRuntimeHazardKind.InvalidCollectionCardinality }, SymbolicRuntimeHazardKind.InvalidCollectionCardinality, SymbolicRuntimeHazardStatus.Proven, null, null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_PrunesPriorityQueuePeekAfterCountGuard", @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(PriorityQueue<int, int> values)
    {
        if (values.Count > 0)
        {
            return values.Peek();
        }

        return 0;
    }
}", "return values.Peek();", true, new[] { SymbolicRuntimeHazardKind.InvalidCollectionCardinality }, SymbolicRuntimeHazardKind.InvalidCollectionCardinality, SymbolicRuntimeHazardStatus.Unreachable, null, null, null, null, null, null, null, null),
    };

    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownStringSubstringArgumentOutOfRangeCandidate()
    {
        const string source = @"
public class TestClass
{
    public string TestMethod(string value, int start)
    {
        return value.Substring(start);
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "return value.Substring(start);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return value.Substring(start);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        Assert.That(hazard.Category, Is.EqualTo("definite_string_substring_out_of_range"));
    }




    [Test]
    public void QuerySourceRuntimeHazardsLine_GuardedReadOnlyListIndexerArgumentOutOfRangeIsPruned()
    {
        const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(IReadOnlyList<int> values, int index)
    {
        if (index >= 0 && index < values.Count)
        {
            return values[index];
        }

        return 0;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(source, "return values[index];", smtAnalysis);

        Assert.That(result.Hazards, Is.Empty);

        var allCandidates = QueryLine(
            source,
            "return values[index];",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

        var hazard = AssertSingleHazard(allCandidates);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
        Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
        Assert.That(hazard.Category, Is.EqualTo("definite_count_index_out_of_range"));
    }


    [Test]
    public void QuerySourceRuntimeHazardsLine_GuardedListIndexFromEndArgumentOutOfRangeIsPruned()
    {
        const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(List<int> values)
    {
        if (values.Count > 0)
        {
            return values[^1];
        }

        return 0;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(source, "return values[^1];", smtAnalysis);

        Assert.That(result.Hazards, Is.Empty);

        var allCandidates = QueryLine(
            source,
            "return values[^1];",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

        Assert.That(allCandidates.Hazards, Has.Count.EqualTo(2));
        var indexConstructionHazard = allCandidates.Hazards.Single(candidate =>
            candidate.Category == "definite_index_construction_argument_out_of_range");
        var listAccessHazard = allCandidates.Hazards.Single(candidate =>
            candidate.Category == "definite_count_index_out_of_range");
        Assert.That(indexConstructionHazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
        Assert.That(listAccessHazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
        Assert.That(indexConstructionHazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
        Assert.That(listAccessHazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
    }





    private static readonly RuntimeHazardScenario[] RuntimeHazardScenariosPart9 =
    {


        new("QuerySourceRuntimeHazardsLine_ProvesInvalidMathClampBounds", @"
using System;

public class TestClass
{
    public int TestMethod(int value)
    {
        return Math.Clamp(value, 10, 0);
    }
}", "return Math.Clamp(value, 10, 0);", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.ArgumentOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentException", "definite_invalid_clamp_bounds", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesRegexNullInput", @"
using System.Text.RegularExpressions;

public class TestClass
{
    public bool TestMethod()
    {
        string input = null;
        return Regex.IsMatch(input, ""a"");
    }
}", "return Regex.IsMatch(input, \"a\");", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.ArgumentNull, SymbolicRuntimeHazardStatus.Proven, "System.ArgumentNullException", "definite_regex_null_input", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_GuardedMathAbsOverflowIsPruned", @"
using System;

public class TestClass
{
    public int TestMethod(int value)
    {
        if (value != int.MinValue)
        {
            return Math.Abs(value);
        }

        return 0;
    }
}", "return Math.Abs(value);", true, new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }, SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Unreachable, "System.OverflowException", null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesShortMathAbsMinimumOverflow", @"
using System;

public class TestClass
{
    public short TestMethod()
    {
        return Math.Abs(short.MinValue);
    }
}", "return Math.Abs(short.MinValue);", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", null, null, null, null, null, null, null),

        new("QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedDivisionOverflow", @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MinValue)
        {
            return checked(value / -1);
        }

        return 0;
    }
}", "return checked(value / -1);", false, new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }, SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", "definite_checked_integral_overflow", "ir.runtime-hazard.checked-integral.signed-division-overflow", null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_GuardedCheckedDivisionOverflowIsPruned", @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (value > int.MinValue && divisor == -1)
        {
            return checked(value / divisor);
        }

        return 0;
    }
}", "return checked(value / divisor);", true, new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }, SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Unreachable, "System.OverflowException", "definite_checked_integral_overflow", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedUncheckedDivisionOverflow", @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (value == int.MinValue && divisor == -1)
        {
            return unchecked(value / divisor);
        }

        return 0;
    }
}", "return unchecked(value / divisor);", false, new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }, SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", "definite_checked_integral_overflow", "ir.runtime-hazard.checked-integral.signed-division-overflow", null, null, null, null, null),
    };






    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownCheckedIntegralOverflowCandidate()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        return checked(value + 1);
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "return checked(value + 1);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return checked(value + 1);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
    }




    private static readonly RuntimeHazardScenario[] RuntimeHazardScenariosPart10 =
    {
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedRemainderOverflow", @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MinValue)
        {
            return checked(value % -1);
        }

        return 0;
    }
}", "return checked(value % -1);", false, new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }, SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", "definite_checked_integral_overflow", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedUncheckedRemainderOverflow", @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (value == int.MinValue && divisor == -1)
        {
            return unchecked(value % divisor);
        }

        return 0;
    }
}", "return unchecked(value % divisor);", false, new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }, SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", "definite_checked_integral_overflow", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedLongRemainderOverflow", @"
public class TestClass
{
    public long TestMethod(long value)
    {
        if (value == long.MinValue)
        {
            return checked(value % -1L);
        }

        return 0;
    }
}", "return checked(value % -1L);", false, new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }, SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", "definite_checked_integral_overflow", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_GuardedCheckedRemainderOverflowIsPruned", @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (value > int.MinValue && divisor == -1)
        {
            return checked(value % divisor);
        }

        return 0;
    }
}", "return checked(value % divisor);", true, new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }, SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Unreachable, "System.OverflowException", "definite_checked_integral_overflow", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedCompoundAssignmentOverflow", @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MaxValue)
        {
            checked
            {
                value += 1;
            }
        }

        return value;
    }
}", "value += 1;", false, new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }, SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", "definite_checked_integral_overflow", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedCompoundDivisionOverflow", @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (value == int.MinValue && divisor == -1)
        {
            checked
            {
                value /= divisor;
            }
        }

        return value;
    }
}", "value /= divisor;", false, new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }, SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", "definite_checked_integral_overflow", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedCompoundRemainderOverflow", @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (value == int.MinValue && divisor == -1)
        {
            checked
            {
                value %= divisor;
            }
        }

        return value;
    }
}", "value %= divisor;", false, new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }, SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", "definite_checked_integral_overflow", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_GuardedCheckedCompoundAssignmentOverflowIsPruned", @"
public class TestClass
{
    public int TestMethod(int value, int delta)
    {
        if (value >= int.MinValue && delta >= 0 && value <= int.MaxValue - delta)
        {
            checked
            {
                value += delta;
            }
        }

        return value;
    }
}", "value += delta;", true, new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }, SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Unreachable, null, "definite_checked_integral_overflow", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedNegativeArrayLength", @"
public class TestClass
{
    public int[] TestMethod(int length)
    {
        if (length < 0)
        {
            return new int[length];
        }

        return new int[0];
    }
}", "return new int[length];", false, new[] { SymbolicRuntimeHazardKind.NegativeArrayLength }, SymbolicRuntimeHazardKind.NegativeArrayLength, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", "definite_negative_array_length", "ir.runtime-hazard.array.negative-length.aggregate", null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedNegativeStackAllocLength", @"
using System;

public class TestClass
{
    public int TestMethod(int length)
    {
        if (length < 0)
        {
            Span<int> span = stackalloc int[length];
            return span.Length;
        }

        return 0;
    }
}", "Span<int> span = stackalloc int[length];", false, new[] { SymbolicRuntimeHazardKind.NegativeStackAllocLength }, SymbolicRuntimeHazardKind.NegativeStackAllocLength, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", "definite_negative_stackalloc_length", "ir.runtime-hazard.stackalloc.negative-length.aggregate", null, null, null, null, null),
    };



    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownCheckedDivisionOverflowCandidate()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        return checked(value / divisor);
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "return checked(value / divisor);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return checked(value / divisor);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
    }

    [Test]
    public void QuerySourceRuntimeHazards_UIntDivisionDoesNotCreateImpossibleCheckedOverflowCandidate()
    {
        const string source = @"
public class TestClass
{
    public uint TestMethod(uint value, uint divisor)
    {
        return checked(value / divisor);
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return checked(value / divisor);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

        Assert.That(result.Hazards, Is.Empty);
    }





    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownCheckedCompoundAssignmentOverflowCandidate()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value, int delta)
    {
        checked
        {
            value += delta;
        }

        return value;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "value += delta;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "value += delta;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
    }


    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownNegativeArrayLengthCandidate()
    {
        const string source = @"
public class TestClass
{
    public int[] TestMethod(int length)
    {
        return new int[length];
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "return new int[length];",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NegativeArrayLength }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return new int[length];",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.NegativeArrayLength }));
        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NegativeArrayLength));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
    }


    private static readonly RuntimeHazardScenario[] RuntimeHazardScenariosPart11 =
    {
        new("QuerySourceRuntimeHazardsLine_GuardedStackAllocLengthIsPruned", @"
using System;

public class TestClass
{
    public int TestMethod(int length)
    {
        if (length >= 0)
        {
            Span<int> span = stackalloc int[length];
            return span.Length;
        }

        return 0;
    }
}", "Span<int> span = stackalloc int[length];", true, new[] { SymbolicRuntimeHazardKind.NegativeStackAllocLength }, SymbolicRuntimeHazardKind.NegativeStackAllocLength, SymbolicRuntimeHazardStatus.Unreachable, null, "definite_negative_stackalloc_length", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazards_ArrayCreationNormalCompletionPrunesNegativeLengthBranch", @"
public class TestClass
{
    public int[] TestMethod(int length)
    {
        var values = new int[length];
        if (length < 0)
        {
            return new int[length + 0];
        }

        return values;
    }
}", "return new int[length + 0];", true, new[] { SymbolicRuntimeHazardKind.NegativeArrayLength }, SymbolicRuntimeHazardKind.NegativeArrayLength, SymbolicRuntimeHazardStatus.Unreachable, null, null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazards_MultidimensionalArrayNegativeLength_ProvesOverflow", @"
public class TestClass
{
    public int[,] TestMethod()
    {
        var length = -1;
        return new int[1, length];
    }
}", "return new int[1, length];", false, new[] { SymbolicRuntimeHazardKind.NegativeArrayLength }, SymbolicRuntimeHazardKind.NegativeArrayLength, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazards_AssignedMultidimensionalArrayDimensionLengthProvesIndexOutOfRange", @"
public class TestClass
{
    public int TestMethod(int rows, int columns)
    {
        var values = new int[rows, columns];
        return values[rows, 0];
    }
}", "return values[rows, 0];", false, new[] { SymbolicRuntimeHazardKind.IndexOutOfRange }, SymbolicRuntimeHazardKind.IndexOutOfRange, SymbolicRuntimeHazardStatus.Proven, "System.IndexOutOfRangeException", null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedBytePreIncrementOverflow", @"
public class TestClass
{
    public byte TestMethod(byte value)
    {
        if (value == byte.MaxValue)
        {
            return checked(++value);
        }

        return 0;
    }
}", "return checked(++value);", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", "definite_checked_integral_overflow", "ir.runtime-hazard.checked-integral.increment-overflow", null, null, null, null, "++value"),
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedLongPostDecrementOverflow", @"
public class TestClass
{
    public long TestMethod(long value)
    {
        if (value == long.MinValue)
        {
            return checked(value--);
        }

        return 0L;
    }
}", "return checked(value--);", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", "definite_checked_integral_overflow", "ir.runtime-hazard.checked-integral.decrement-overflow", null, null, null, null, "value--"),
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedExplicitNumericConversionOverflow", @"
public class TestClass
{
    public int TestMethod(long value)
    {
        if (value > int.MaxValue)
        {
            return checked((int)value);
        }

        return 0;
    }
}", "return checked((int)value);", false, Array.Empty<SymbolicRuntimeHazardKind>(), SymbolicRuntimeHazardKind.CheckedIntegralOverflow, SymbolicRuntimeHazardStatus.Proven, "System.OverflowException", "definite_checked_numeric_conversion_overflow", "ir.runtime-hazard.checked-conversion.overflow", null, null, null, null, "(int)value"),
        new("QuerySourceRuntimeHazardsLine_ProvesArrayCovarianceStoreMismatch", @"
public class TestClass
{
    public void TestMethod()
    {
        object[] values = new string[1];
        values[0] = 42;
    }
}", "values[0] = 42;", false, new[] { SymbolicRuntimeHazardKind.ArrayTypeMismatch }, SymbolicRuntimeHazardKind.ArrayTypeMismatch, SymbolicRuntimeHazardStatus.Proven, "System.ArrayTypeMismatchException", "definite_array_type_mismatch", null, null, null, null, null, null),
        new("KnownLimitation_ArrayCovarianceStoreAcrossMergedIdentities_RemainsUnknown", @"
using System;

public class TestClass
{
    public void TestMethod(bool useStrings)
    {
        object[] values;
        if (useStrings)
            values = new string[1];
        else
            values = new Uri[1];

        values[0] = new object();
    }
}", "values[0] = new object();", true, new[] { SymbolicRuntimeHazardKind.ArrayTypeMismatch }, SymbolicRuntimeHazardKind.ArrayTypeMismatch, SymbolicRuntimeHazardStatus.Unknown, "System.ArrayTypeMismatchException", null, null, SymbolicProofStatus.Unknown, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ProvesGuardedSwitchExpressionNoMatch", @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            return value switch
            {
                < 0 => -1,
                0 => 0,
            };
        }

        return 0;
    }
}", "return value switch", false, new[] { SymbolicRuntimeHazardKind.SwitchExpressionNoMatch }, SymbolicRuntimeHazardKind.SwitchExpressionNoMatch, SymbolicRuntimeHazardStatus.Proven, "System.Runtime.CompilerServices.SwitchExpressionException", "definite_switch_expression_no_match", null, null, null, null, null, null),
    };
    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownNegativeStackAllocLengthCandidate()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int length)
    {
        Span<int> span = stackalloc int[length];
        return span.Length;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "Span<int> span = stackalloc int[length];",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NegativeStackAllocLength }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "Span<int> span = stackalloc int[length];",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.NegativeStackAllocLength }));
        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NegativeStackAllocLength));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        Assert.That(hazard.Category, Is.EqualTo("definite_negative_stackalloc_length"));
    }






    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownCheckedPostIncrementOverflowCandidate()
    {
        const string source = @"
public class TestClass
{
    public short TestMethod(short value)
    {
        return checked(value++);
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "return checked(value++);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return checked(value++);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        Assert.That(hazard.OperationText, Is.EqualTo("value++"));
    }

    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnreachableCheckedPreIncrementOverflowCandidate()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MaxValue)
        {
            return 0;
        }

        if (value == int.MaxValue)
        {
            return checked(++value);
        }

        return 1;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "return checked(++value);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return checked(++value);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
        Assert.That(hazard.OperationText, Is.EqualTo("++value"));
    }


    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownCheckedExplicitNumericConversionOverflowCandidate()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(long value)
    {
        return checked((int)value);
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "return checked((int)value);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return checked((int)value);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        Assert.That(hazard.OperationText, Is.EqualTo("(int)value"));
    }


    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesCompatibleArrayCovarianceStore()
    {
        const string source = @"
public class TestClass
{
    public void TestMethod()
    {
        object[] values = new string[1];
        values[0] = ""text"";
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "values[0] = \"text\";",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArrayTypeMismatch }));

        Assert.That(result.Hazards, Is.Empty);
    }



    private static readonly RuntimeHazardScenario[] RuntimeHazardScenariosPart12 =
    {
        new("QuerySourceRuntimeHazardsLine_GuardedSwitchExpressionNoMatchIsPruned", @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value <= 0)
        {
            return value switch
            {
                < 0 => -1,
                0 => 0,
            };
        }

        return 1;
    }
}", "return value switch", true, new[] { SymbolicRuntimeHazardKind.SwitchExpressionNoMatch }, SymbolicRuntimeHazardKind.SwitchExpressionNoMatch, SymbolicRuntimeHazardStatus.Unreachable, null, "definite_switch_expression_no_match", null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_GuardConditionMakesArgumentOutOfRangeGuardUnreachable", @"
using System;

public class TestClass
{
    public void TestMethod(int value)
    {
        if (value >= 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
        }
    }
}", "ArgumentOutOfRangeException.ThrowIfNegative(value);", true, new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }, null, SymbolicRuntimeHazardStatus.Unreachable, null, null, null, null, null, null, null, null),
        new("QuerySourceRuntimeHazardsLine_ArgumentOutOfRangeGuardsProveArrayIndexInRange", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, values.Length);
        return values[index];
    }
}", "return values[index];", true, new[] { SymbolicRuntimeHazardKind.IndexOutOfRange }, null, SymbolicRuntimeHazardStatus.Unreachable, null, null, null, null, null, null, null, null),
    };
    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownSwitchExpressionNoMatchCandidate()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value switch
        {
            < 0 => -1,
            0 => 0,
        };
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "return value switch",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.SwitchExpressionNoMatch }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "return value switch",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.SwitchExpressionNoMatch }));

        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.SwitchExpressionNoMatch));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        Assert.That(hazard.Category, Is.EqualTo("definite_switch_expression_no_match"));
    }

    [Test]
    public void QuerySourceRuntimeHazardsSpan_FiltersToRequestedSpan()
    {
        const string source = @"
using System;

public class TestClass
{
    public void TestMethod(bool flag)
    {
        if (flag)
        {
            throw new InvalidOperationException();
        }

        throw new ArgumentException();
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var spanStart = FindPosition(source, "throw new ArgumentException();");
        var spanEnd = spanStart + "throw new ArgumentException();".Length;
        var result = new SymbolicRuntimeHazardQueryService().QuerySourceRuntimeHazardsSpan(
            source,
            "Hazards.cs",
            spanStart,
            spanEnd,
            smtAnalysis);

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.OperationText, Is.EqualTo("throw new ArgumentException();"));
        Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentException"));
    }

    [Test]
    public async Task SymbolicCli_RuntimeHazardsJson_EmitsProvenHazard()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
            return 10 / divisor;
        }

        return 0;
    }
}";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicRuntimeHazards-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "return 10 / divisor;").ToString(),
                "--runtime-hazards",
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("HazardCount").GetInt32(), Is.EqualTo(1));
            var hazard = root.GetProperty("Hazards")[0];
            Assert.That(hazard.GetProperty("Kind").GetString(),
                Is.EqualTo(SymbolicRuntimeHazardKind.DivideByZero.ToString()));
            Assert.That(hazard.GetProperty("Status").GetString(),
                Is.EqualTo(SymbolicRuntimeHazardStatus.Proven.ToString()));
            Assert.That(hazard.GetProperty("ExceptionType").GetString(), Is.EqualTo("System.DivideByZeroException"));
            var triggerPrecondition = hazard.GetProperty("TriggerPrecondition");
            Assert.That(triggerPrecondition.GetProperty("Kind").GetString(),
                Is.EqualTo("SymbolicExceptionPreconditionAtom"));
            Assert.That(triggerPrecondition.GetProperty("Provenance").GetString(),
                Is.EqualTo("ir.runtime-hazard.divide-by-zero"));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_RuntimeHazardsCompactJson_EmitsTriggerPrecondition()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
            return 10 / divisor;
        }

        return 0;
    }
}";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicRuntimeHazardsCompact-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "return 10 / divisor;").ToString(),
                "--runtime-hazards",
                "--compact-json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
            var hazard = root.GetProperty("hazards")[0];
            var triggerPrecondition = hazard.GetProperty("triggerPrecondition");
            Assert.That(triggerPrecondition.GetProperty("kind").GetString(),
                Is.EqualTo("SymbolicExceptionPreconditionAtom"));
            Assert.That(triggerPrecondition.GetProperty("provenance").GetString(),
                Is.EqualTo("ir.runtime-hazard.divide-by-zero"));
            Assert.That(triggerPrecondition.GetProperty("confidence").GetString(), Is.EqualTo("Exact"));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_RuntimeHazardsJson_EmitsDynamicNullBindingHazard()
    {
        const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value.Missing;
    }
}";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicRuntimeDynamicHazards-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "return value.Missing;").ToString(),
                "--runtime-hazards",
                "--hazard-kind",
                "DynamicNullBinding",
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("HazardCount").GetInt32(), Is.EqualTo(1));
            var hazard = root.GetProperty("Hazards")[0];
            Assert.That(hazard.GetProperty("Kind").GetString(),
                Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding.ToString()));
            Assert.That(hazard.GetProperty("Status").GetString(),
                Is.EqualTo(SymbolicRuntimeHazardStatus.Proven.ToString()));
            Assert.That(hazard.GetProperty("ExceptionType").GetString(),
                Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(hazard.GetProperty("Category").GetString(), Is.EqualTo("definite_dynamic_member_null_binding"));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [TestCase("ArgumentOutOfRangeException.ThrowIfNegative(-1);")]
    [TestCase("ArgumentOutOfRangeException.ThrowIfZero(0);")]
    [TestCase("ArgumentOutOfRangeException.ThrowIfNegativeOrZero(0);")]
    [TestCase("ArgumentOutOfRangeException.ThrowIfLessThan(0, 1);")]
    [TestCase("ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(1, 1);")]
    [TestCase("ArgumentOutOfRangeException.ThrowIfGreaterThan(2, 1);")]
    [TestCase("ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(1, 1);")]
    public void QuerySourceRuntimeHazardsLine_ProvesArgumentOutOfRangeGuardFailure(string guardInvocation)
    {
        var source = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        " + guardInvocation + @"
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            guardInvocation,
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
        Assert.That(hazard.Category, Is.EqualTo("definite_argument_out_of_range_guard"));
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(
            hazard.TriggerPrecondition!.Provenance,
            Does.StartWith("ir.runtime-hazard.argument-out-of-range.guard."));
    }


    [Test]
    public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownArgumentOutOfRangeGuardCandidate()
    {
        const string source = @"
using System;

public class TestClass
{
    public void TestMethod(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var defaultResult = QueryLine(
            source,
            "ArgumentOutOfRangeException.ThrowIfNegative(value);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));
        Assert.That(defaultResult.Hazards, Is.Empty);

        var candidateResult = QueryLine(
            source,
            "ArgumentOutOfRangeException.ThrowIfNegative(value);",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));
        var hazard = AssertSingleHazard(candidateResult);
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
    }


    [Test]
    public void ClassifyTriggerCore_UsesTypedFactInsteadOfProvenanceAsControlFlow()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("class C { void M(int value) { } }");
        var node = syntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var subject = new SymbolicVariableTerm("value", SmtValueKind.Int);
        var zeroFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                subject,
                new SymbolicIntegerConstantTerm(0)),
            node,
            "path.zero");
        var fallbackTrigger = SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.DivideByZero,
                subject,
                new SymbolicFactCondition(zeroFact)),
            node,
            "ir.runtime-hazard.divide-by-zero.formula-fallback");
        var analysis = new SymbolicProgramPointAnalysis(
            node.SpanStart,
            Array.Empty<SmtFormula>(),
            new SymbolicState(new[] { zeroFact }, new SymbolicCondition[] { new SymbolicFactCondition(zeroFact) }),
            SymbolicReachability.Reachable,
            "reachable",
            SymbolicSmtDiagnostics.NotConfigured,
            node);
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var (status, reason, proof, _) = SymbolicRuntimeHazardQueryService.ClassifyTriggerCore(
            analysis,
            new SymbolicFactCondition(zeroFact),
            fallbackTrigger,
            smtAnalysis);

        Assert.That(status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(reason, Is.Not.Empty);
        Assert.That(proof, Is.Not.Null);
        Assert.That(proof!.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void ClassifyTriggerCore_ReportsUnsupportedTypedProjectionWithSourceLikeEvidence()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("class C { void M(int value) { } }");
        var node = syntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var subject = new SymbolicVariableTerm("value", SmtValueKind.Int);
        var unsupportedTrigger = new SymbolicFact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.DivideByZero,
                subject,
                new SymbolicFactCondition(new SymbolicFact(
                    new SymbolicTruthAtom(new SymbolicVariableTerm("unsupported#1", SmtValueKind.Bool)),
                    true,
                    SymbolicFactConfidence.Unsupported,
                    "ir.runtime-hazard.divide-by-zero.translated.trigger",
                    node.Span,
                    null,
                    "unsupported-trigger"))),
            true,
            SymbolicFactConfidence.Unsupported,
            "ir.runtime-hazard.divide-by-zero.translated",
            node.Span,
            null,
            "unsupported-precondition");
        var analysis = new SymbolicProgramPointAnalysis(
            node.SpanStart,
            Array.Empty<SmtFormula>(),
            new SymbolicState(Array.Empty<SymbolicFact>()),
            SymbolicReachability.Reachable,
            "reachable",
            SymbolicSmtDiagnostics.NotConfigured,
            node);
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var (status, reason, proof, _) = SymbolicRuntimeHazardQueryService.ClassifyTriggerCore(
            analysis,
            ((SymbolicExceptionPreconditionAtom)unsupportedTrigger.Atom).Trigger,
            unsupportedTrigger,
            smtAnalysis);
        var publicFact = SymbolicFactInfo.FromFact(unsupportedTrigger);

        Assert.That(status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        Assert.That(reason, Is.EqualTo("unsupported_typed_projection"));
        Assert.That(proof, Is.Null);
        Assert.That(publicFact.Confidence, Is.EqualTo("Unsupported"));
        Assert.That(publicFact.Text, Is.EqualTo("unknown(DivideByZero trigger for value)"));
    }

    [Test]
    public void ClassifyTriggerCore_PreservesIrBackedHazardProofs()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("class C { void M(int value) { } }");
        var node = syntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var subject = new SymbolicVariableTerm("value", SmtValueKind.Int);
        var zeroFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                subject,
                new SymbolicIntegerConstantTerm(0)),
            node,
            "path.zero");
        var irTrigger = SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.DivideByZero,
                subject,
                new SymbolicFactCondition(zeroFact)),
            node,
            "ir.runtime-hazard.divide-by-zero");
        var analysis = new SymbolicProgramPointAnalysis(
            node.SpanStart,
            Array.Empty<SmtFormula>(),
            new SymbolicState(new[] { zeroFact }, new SymbolicCondition[] { new SymbolicFactCondition(zeroFact) }),
            SymbolicReachability.Reachable,
            "reachable",
            SymbolicSmtDiagnostics.NotConfigured,
            node);
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var (status, reason, proof, _) = SymbolicRuntimeHazardQueryService.ClassifyTriggerCore(
            analysis,
            new SymbolicFactCondition(zeroFact),
            irTrigger,
            smtAnalysis);

        Assert.That(status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(reason, Is.Not.Empty);
        Assert.That(proof, Is.Not.Null);
        Assert.That(proof!.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void UnsupportedTypedProjectionRuntimeHazard_MapsToConservativePublicProofMetadata()
    {
        var operation = SyntaxFactory.ParseExpression("10 / divisor");
        var subject = new SymbolicVariableTerm("divisor", SmtValueKind.Int);
        var trigger = new SymbolicFactCondition(new SymbolicFact(
            new SymbolicTruthAtom(new SymbolicVariableTerm("unsupported#10_22", SmtValueKind.Bool)),
            true,
            SymbolicFactConfidence.Unsupported,
            "unsupported_typed_projection",
            operation.Span,
            null,
            "unsupported_typed_projection"));
        var descriptor = new SymbolicHazardOperation(
            SymbolicRuntimeHazardKind.DivideByZero,
            SymbolicExceptionPreconditionKind.DivideByZero,
            subject,
            trigger,
            SymbolicFactConfidence.Unsupported,
            "System.DivideByZeroException",
            "definite_divide_by_zero",
            new SymbolicOperationOrigin(operation.Span, 0, "unsupported_typed_projection"));
        var hazard = new SymbolicRuntimeHazard(
            "Hazards.cs",
            descriptor,
            SymbolicRuntimeHazardStatus.Unknown,
            "unsupported_typed_projection",
            "DivideExpression",
            "10 / divisor",
            10,
            22,
            5,
            16,
            5,
            16,
            5,
            28,
            "trigger#10_22",
            null,
            "divisor == 0",
            Array.Empty<string>(),
            Array.Empty<SymbolicFactInfo>(),
            SymbolicReachability.Reachable,
            "reachable",
            null,
            SymbolicSmtDiagnostics.NotConfigured);

        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        Assert.That(hazard.Proof.Status, Is.EqualTo(SymbolicProofStatus.Unknown));
        Assert.That(hazard.Proof.Backend, Is.EqualTo(SymbolicProofBackend.None));
        Assert.That(hazard.Proof.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
        Assert.That(hazard.Proof.Reason, Is.EqualTo("unsupported_typed_projection"));
        Assert.That(
            hazard.GetDisplayStatusReason(),
            Is.EqualTo("runtime-hazard trigger could not be projected to typed symbolic IR"));
    }

    [Test]
    public async Task SymbolicCli_RuntimeHazardCompactGates_UseFinalCountsAndTruncation()
    {
        const string source = """
                              using System;

                              public static class HazardGateSample
                              {
                                  public static void Throw() => throw new InvalidOperationException();
                              }
                              """;
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicHazardGates-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var threshold = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "throw new").ToString(),
                "--runtime-hazards",
                "--compact-json",
                "--fail-on-compact-threshold",
                "hazards=0");
            Assert.That(threshold.ExitCode, Is.EqualTo(1));
            Assert.That(threshold.StandardError,
                Does.Contain("CI gate failed [compact-threshold.hazards]"));
            using (JsonDocument.Parse(threshold.StandardOutput))
            {
            }

            var truncation = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "throw new").ToString(),
                "--runtime-hazards",
                "--compact-json",
                "--max-hazards",
                "0",
                "--fail-on-compact-truncation");
            Assert.That(truncation.ExitCode, Is.EqualTo(1));
            Assert.That(truncation.StandardError, Does.Contain("CI gate failed [compact-truncation]"));
            using var document = JsonDocument.Parse(truncation.StandardOutput);
            Assert.That(document.RootElement.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
            Assert.That(document.RootElement.GetProperty("hazards").GetArrayLength(), Is.Zero);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }





























    [Test]
    public void QuerySourceRuntimeHazardsLine_ReturnsProvenDirectThrow()
    {
        const string source = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        throw new InvalidOperationException();
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(source, "throw new InvalidOperationException();", smtAnalysis);

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DirectThrow));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidOperationException"));
        Assert.That(hazard.Category, Is.EqualTo("direct_throw"));
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
        Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.direct-throw"));
        Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance),
            Does.Contain("ir.runtime-hazard.direct-throw"));
        Assert.That(hazard.Proof.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(hazard.Proof.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
        Assert.That(hazard.Proof.UnknownReason, Is.EqualTo(SymbolicUnknownReason.None));
        Assert.That(hazard.InvariantInfo.MergedText, Is.EqualTo(hazard.MergedInvariantText));
        Assert.That(hazard.InvariantInfo.Facts, Is.EquivalentTo(hazard.SymbolicFacts));
        Assert.That(hazard.InvariantInfo.Proofs.Single().Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void QuerySourceRuntimeHazardsLine_ClassifiesThrowNullAsNullReferenceException()
    {
        const string source = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Exception? error = null;
        throw error;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(source, "throw error;", smtAnalysis);

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DirectThrow));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
        Assert.That(hazard.Category, Is.EqualTo("definite_throw_null"));
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
        Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.throw-null"));
        Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance),
            Does.Contain("ir.runtime-hazard.throw-null"));
    }

    [Test]
    public void QuerySourceRuntimeHazardsLine_ProvesGuardedDivideByZero()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
            return 10 / divisor;
        }

        return 0;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(source, "return 10 / divisor;", smtAnalysis);

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DivideByZero));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(hazard.ExceptionType, Is.EqualTo("System.DivideByZeroException"));
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
        Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.divide-by-zero"));
        Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance),
            Does.Contain("ir.runtime-hazard.divide-by-zero"));
        Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance), Has.Some.StartsWith("ir."));
        Assert.That(hazard.Proof.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(hazard.Proof.Backend, Is.AnyOf(SymbolicProofBackend.Smt, SymbolicProofBackend.Syntactic));
        if (hazard.Proof.Backend == SymbolicProofBackend.Smt)
        {
            Assert.That(hazard.Proof.Budget, Is.Not.Null);
            Assert.That(hazard.Proof.Budget!.MaxPathConditions,
                Is.EqualTo(SmtAnalysisOptions.Default.MaxPathConditions));
        }
        else
        {
            Assert.That(hazard.Proof.Budget, Is.Null);
        }

        Assert.That(hazard.InvariantInfo.Facts, Is.EquivalentTo(hazard.SymbolicFacts));
        Assert.That(hazard.PathConditions, Does.Contain("divisor == 0"));
    }

    [Test]
    public void QuerySourceRuntimeHazardsLine_MemberReceiverNullDereferenceUsesIrPrecondition()
    {
        const string source = @"
public sealed class Holder
{
    public string? Value { get; set; }
}

public class TestClass
{
    public int TestMethod(Holder holder)
    {
        if (holder.Value is null)
        {
            return holder.Value.Length;
        }

        return 0;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return holder.Value.Length;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullDereference));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
        Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.null-dereference"));
        Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance),
            Does.Contain("ir.runtime-hazard.null-dereference"));
    }

    [Test]
    public void QuerySourceRuntimeHazardsLine_ProvesDynamicMemberNullBinding()
    {
        const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value.Missing;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return value.Missing;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(hazard.ExceptionType, Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
        Assert.That(hazard.Category, Is.EqualTo("definite_dynamic_member_null_binding"));
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
        Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.dynamic-null-binding"));
        Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance),
            Does.Contain("ir.runtime-hazard.dynamic-null-binding"));
    }

    [Test]
    public void QuerySourceRuntimeHazardsLine_ProvesCastedDynamicMemberNullBinding()
    {
        const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        return ((dynamic)null).Missing;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return ((dynamic)null).Missing;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(hazard.ExceptionType, Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
        Assert.That(hazard.Category, Is.EqualTo("definite_dynamic_member_null_binding"));
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
        Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.dynamic-null-binding"));
    }

    [Test]
    public void KnownLimitation_DynamicBinderMissingMemberOnNonNullReceiver_HasNoBinderHazard()
    {
        const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = new object();
        return value.Missing;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return value.Missing;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

        var nullBindingCandidate = AssertSingleHazard(result);
        Assert.That(nullBindingCandidate.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
        Assert.That(nullBindingCandidate.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
        Assert.That(result.Hazards, Has.None.Matches<SymbolicRuntimeHazard>(hazard =>
            hazard.Status == SymbolicRuntimeHazardStatus.Proven));
    }

    [Test]
    public void QuerySourceRuntimeHazardsLine_ProvesNullableValueWithoutValue()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        int? value = default;
        return value.Value;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(source, "return value.Value;", smtAnalysis);

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullableValueWithoutValue));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidOperationException"));
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
        Assert.That(hazard.TriggerPrecondition.Provenance,
            Is.EqualTo("ir.runtime-hazard.nullable-value.without-value"));
    }

    [Test]
    public void QuerySourceRuntimeHazards_ProvesNullableValueAfterConditionalAccessNullReceiverAssignment()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(string text)
    {
        int? value = text?.Length;
        if (text is null)
        {
            return value.Value;
        }

        return 0;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return value.Value;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[]
                { SymbolicRuntimeHazardKind.NullableValueWithoutValue }));

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullableValueWithoutValue));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidOperationException"));
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
        Assert.That(hazard.TriggerPrecondition.Provenance,
            Is.EqualTo("ir.runtime-hazard.nullable-value.without-value"));
        Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance),
            Does.Contain("ir.runtime-hazard.nullable-value.without-value"));
    }

    [Test]
    public void QuerySourceRuntimeHazardsLine_ProvesInvalidCastAfterNegativeTypeAndNullTests()
    {
        const string source = @"
public class TestClass
{
    public string TestMethod(object value)
    {
        if (value is not string && value is not null)
        {
            return (string)value;
        }

        return string.Empty;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return (string)value;",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.InvalidCast));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidCastException"));
        Assert.That(hazard.Category, Is.EqualTo("definite_invalid_cast"));
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
        Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.invalid-cast.mismatch"));
        Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance),
            Does.Contain("ir.runtime-hazard.invalid-cast.mismatch"));
    }

    [Test]
    public void QuerySourceRuntimeHazardsLine_ProvesQueuePeekOnEmptyCollection()
    {
        const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(Queue<int> values)
    {
        if (values.Count == 0)
        {
            return values.Peek();
        }

        return 0;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return values.Peek();",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(kinds: new[]
                { SymbolicRuntimeHazardKind.InvalidCollectionCardinality }));

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.InvalidCollectionCardinality));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidOperationException"));
        Assert.That(hazard.Category, Is.EqualTo("definite_invalid_collection_cardinality"));
        Assert.That(hazard.TriggerPrecondition?.Provenance,
            Is.EqualTo("ir.runtime-hazard.collection-cardinality"));
    }

    [Test]
    public void QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedIntegralOverflow()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MaxValue)
        {
            return checked(value + 1);
        }

        return 0;
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(source, "return checked(value + 1);", smtAnalysis);

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
        Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
        Assert.That(hazard.TriggerPrecondition.Provenance,
            Is.EqualTo("ir.runtime-hazard.checked-integral.binary-overflow"));
        Assert.That(hazard.Proof.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
        Assert.That(hazard.Proof.Budget, Is.Not.Null);
    }

    [Test]
    public void QuerySourceRuntimeHazardsLine_ProvesMathAbsMinimumOverflow()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod()
    {
        return Math.Abs(int.MinValue);
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(source, "return Math.Abs(int.MinValue);", smtAnalysis);

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
        Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
        Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(hazard.TriggerPrecondition!.Provenance,
            Is.EqualTo("ir.runtime-hazard.math.abs-overflow"));
    }

    [Test]
    public void QuerySourceRuntimeHazardsLine_ProvesConstantBoundMathClampIndexIsInRangeThroughIr()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int index)
    {
        var values = new int[11];
        return values[Math.Clamp(index, 0, 10)];
    }
}";

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = QueryLine(
            source,
            "return values[Math.Clamp(index, 0, 10)];",
            smtAnalysis,
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.IndexOutOfRange }));

        var hazard = AssertSingleHazard(result);
        Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.IndexOutOfRange));
        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(hazard.TriggerPrecondition!.Provenance,
            Is.EqualTo("ir.runtime-hazard.index.out-of-range"));
    }

    private static SymbolicRuntimeHazardQueryResult QueryLine(
        string source,
        string marker,
        SmtAnalysisService smtAnalysis,
        SymbolicRuntimeHazardQueryOptions? options = null)
    {
        return new SymbolicRuntimeHazardQueryService().QuerySourceRuntimeHazardsLine(
            source,
            "Hazards.cs",
            FindLine(source, marker),
            smtAnalysis,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            options: options);
    }

    private static SymbolicRuntimeHazard AssertSingleHazard(SymbolicRuntimeHazardQueryResult result)
    {
        Assert.That(result.Hazards, Has.Count.EqualTo(1));
        return result.Hazards.Single();
    }

    private static void AssertIrExceptionPrecondition(
        SymbolicRuntimeHazard hazard,
        string provenance)
    {
        Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
        Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
        Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo(provenance));
        Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance), Does.Contain(provenance));
    }

    private static (MethodDeclarationSyntax Method, SemanticModel SemanticModel) CreateMethodContext(
        string source,
        string methodName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "NodeHazards.cs");
        var compilation = CSharpCompilation.Create(
            "NodeHazards",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var method = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == methodName);
        return (method, compilation.GetSemanticModel(syntaxTree));
    }

    private static int FindLine(string source, string text)
    {
        var position = FindPosition(source, text);
        var line = 1;
        for (var index = 0; index < position; index++)
            if (source[index] == '\n')
                line++;

        return line;
    }

    private static int FindPosition(string source, string text)
    {
        var position = source.IndexOf(text, StringComparison.Ordinal);
        if (position < 0) throw new InvalidOperationException("Text not found: " + text);

        return position;
    }
}
