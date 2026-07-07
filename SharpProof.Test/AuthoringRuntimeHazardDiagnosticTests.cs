using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class AuthoringRuntimeHazardDiagnosticTests
    {
        [TestCaseSource(nameof(ProvableRuntimeHazardCases))]
        public async Task Sp0011_AuthoringRuntimeHazards_ReportWithoutEnforcePure(
            string source,
            string operationText,
            string exceptionType,
            string category)
        {
            var diagnostics = await GetAuthoringHazardDiagnosticsAsync(source);

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
            var diagnostic = SingleRuntimeHazardDiagnostic(diagnostics);

            Assert.That(diagnostic.Id, Is.EqualTo(SharpProofDiagnostics.UncaughtExceptionSiteId));
            Assert.That(diagnostic.GetMessage(), Does.Contain(operationText));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo(exceptionType));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo(category));
        }

        [TestCaseSource(nameof(GuardedSafeOrUnknownRuntimeHazardCases))]
        public async Task Sp0011_AuthoringRuntimeHazards_DoNotReportGuardedSafeOrUnknownCases(string source)
        {
            var diagnostics = await GetAuthoringHazardDiagnosticsAsync(source);

            Assert.That(
                diagnostics.Any(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId),
                Is.False,
                FormatDiagnostics(diagnostics));
        }

        [Test]
        public async Task Sp0011_RuntimeHazardModeNo_SuppressesAuthoringRuntimeHazards()
        {
            var diagnostics = await GetAuthoringHazardDiagnosticsAsync(@"
public class TestClass
{
    public int NullDereference()
    {
        string value = null!;
        return value.Length;
    }
}", "no");

            Assert.That(
                diagnostics.Any(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId),
                Is.False,
                FormatDiagnostics(diagnostics));
        }

        private static IEnumerable<TestCaseData> ProvableRuntimeHazardCases()
        {
            yield return new TestCaseData(
                @"
public class TestClass
{
    public int NullDereference()
    {
        string value = null!;
        return value.Length;
    }
}",
                "value.Length",
                "System.NullReferenceException",
                "definite_null_dereference")
                .SetName("Sp0011_AuthoringRuntimeHazards_ReportNullDereferenceWithoutEnforcePure");

            yield return new TestCaseData(
                @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> AwaitNullDereference()
    {
        Task<int> task = null!;
        return await task;
    }
}",
                "await task",
                "System.NullReferenceException",
                "definite_await_null")
                .SetName("Sp0011_AuthoringRuntimeHazards_ReportAwaitNullWithoutEnforcePure");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public void LockNullReceiver(object gate)
    {
        if (gate is null)
        {
            lock (gate)
            {
            }
        }
    }
}",
                "lock (gate)",
                "System.ArgumentNullException",
                "definite_lock_null")
                .SetName("Sp0011_AuthoringRuntimeHazards_ReportLockNullWithoutEnforcePure");

            yield return new TestCaseData(
                @"
using System;

public class TestClass
{
    public void ThrowMaybeNull(Exception? error)
    {
        if (error is null)
        {
            throw error;
        }
    }
}",
                "throw error;",
                "System.NullReferenceException",
                "definite_throw_null")
                .SetName("Sp0011_AuthoringRuntimeHazards_ReportBranchProvenThrowNullWithoutEnforcePure");

            yield return new TestCaseData(
                @"
using System;

public class TestClass
{
    public void ThrowNull()
    {
        Exception? error = null;
        throw error;
    }
}",
                "throw error;",
                "System.NullReferenceException",
                "definite_throw_null")
                .SetName("Sp0011_AuthoringRuntimeHazards_ReportThrowNullWithoutEnforcePure");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int DivideByZero(int value)
    {
        int divisor = 0;
        return value / divisor;
    }
}",
                "value / divisor",
                "System.DivideByZeroException",
                "definite_divide_by_zero")
                .SetName("Sp0011_AuthoringRuntimeHazards_ReportDivideByZeroWithoutEnforcePure");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int IndexOutOfRange()
    {
        var values = new int[0];
        return values[^1];
    }
}",
                "values[^1]",
                "System.IndexOutOfRangeException",
                "definite_index_out_of_range")
                .SetName("Sp0011_AuthoringRuntimeHazards_ReportIndexOutOfRangeWithoutEnforcePure");

            yield return new TestCaseData(
                @"
using System;

public class TestClass
{
    public int NegativeStackAllocLength(int length)
    {
        if (length < 0)
        {
            Span<int> span = stackalloc int[length];
            return span.Length;
        }

        return 0;
    }
}",
                "stackalloc int[length]",
                "System.OverflowException",
                "definite_negative_stackalloc_length")
                .SetName("Sp0011_AuthoringRuntimeHazards_ReportNegativeStackAllocLengthWithoutEnforcePure");

            yield return new TestCaseData(
                @"
using System.Collections.Generic;

public class TestClass
{
    public int CountIndexOutOfRange(List<int> values)
    {
        if (values.Count == 0)
        {
            return values[0];
        }

        return 0;
    }
}",
                "values[0]",
                "System.ArgumentOutOfRangeException",
                "definite_count_index_out_of_range")
                .SetName("Sp0011_AuthoringRuntimeHazards_ReportCountIndexOutOfRangeWithoutEnforcePure");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int SwitchExpressionNoMatch(int value)
    {
        if (value > 0)
        {
            return value switch
            {
                < 0 => -1,
                0 => 0
            };
        }

        return 0;
    }
}",
                "value switch",
                "System.Runtime.CompilerServices.SwitchExpressionException",
                "definite_switch_expression_no_match")
                .SetName("Sp0011_AuthoringRuntimeHazards_ReportSwitchExpressionNoMatchWithoutEnforcePure");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int ArrayGetValueOutOfRange()
    {
        int[] values = new int[0];
        return (int)values.GetValue(0)!;
    }
}",
                "values.GetValue(0)",
                "System.IndexOutOfRangeException",
                "definite_array_get_value_index_out_of_range")
                .SetName("Sp0011_AuthoringRuntimeHazards_ReportArrayGetValueOutOfRangeWithoutEnforcePure");

            yield return new TestCaseData(
                @"
public record Person(int Value);

public class TestClass
{
    public int WithNullReceiver()
    {
        Person person = null!;
        return (person with { Value = 1 }).Value;
    }
}",
                "person with",
                "System.NullReferenceException",
                "definite_with_null")
                .SetName("Sp0011_AuthoringRuntimeHazards_ReportWithNullReceiverWithoutEnforcePure");

            yield return new TestCaseData(
                @"
public sealed class Pair
{
    public void Deconstruct(out int left, out int right)
    {
        left = 0;
        right = 0;
    }
}

public class TestClass
{
    public int DeconstructionNullReceiver()
    {
        Pair pair = null!;
        int left;
        int right;
        (left, right) = pair;
        return left + right;
    }
}",
                "(left, right) = pair",
                "System.NullReferenceException",
                "definite_deconstruction_null")
                .SetName("Sp0011_AuthoringRuntimeHazards_ReportDeconstructionNullReceiverWithoutEnforcePure");
        }

        private static IEnumerable<TestCaseData> GuardedSafeOrUnknownRuntimeHazardCases()
        {
            yield return new TestCaseData(
                @"
using System;

public class TestClass
{
    public void CaughtThrowNull()
    {
        try
        {
            Exception? error = null;
            throw error;
        }
        catch (NullReferenceException)
        {
        }
    }
}")
                .SetName("Sp0011_AuthoringRuntimeHazards_SuppressCaughtThrowNull");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int GuardedNullDereference(string value)
    {
        if (value != null)
        {
            return value.Length;
        }

        return 0;
    }
}")
                .SetName("Sp0011_AuthoringRuntimeHazards_SuppressGuardedNullDereference");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public void GuardedLockNullReceiver(object gate)
    {
        if (gate is not null)
        {
            lock (gate)
            {
            }
        }
    }
}")
                .SetName("Sp0011_AuthoringRuntimeHazards_SuppressGuardedLockNullReceiver");

            yield return new TestCaseData(
                @"
using System;

public class TestClass
{
    public void CaughtLockNullReceiver(object gate)
    {
        try
        {
            if (gate is null)
            {
                lock (gate)
                {
                }
            }
        }
        catch (ArgumentNullException)
        {
        }
    }
}")
                .SetName("Sp0011_AuthoringRuntimeHazards_SuppressCaughtLockNullReceiver");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int UnknownNullDereference(string value)
    {
        return value.Length;
    }
}")
                .SetName("Sp0011_AuthoringRuntimeHazards_SuppressUnknownNullDereference");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int GuardedDivideByZero(int value, int divisor)
    {
        if (divisor != 0)
        {
            return value / divisor;
        }

        return 0;
    }
}")
                .SetName("Sp0011_AuthoringRuntimeHazards_SuppressGuardedDivideByZero");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int UnknownDivideByZero(int value, int divisor)
    {
        return value / divisor;
    }
}")
                .SetName("Sp0011_AuthoringRuntimeHazards_SuppressUnknownDivideByZero");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int GuardedIndexOutOfRange(int[] values, int index)
    {
        if (index >= 0 && index < values.Length)
        {
            return values[index];
        }

        return 0;
    }
}")
                .SetName("Sp0011_AuthoringRuntimeHazards_SuppressGuardedIndexOutOfRange");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int UnknownIndexOutOfRange(int[] values, int index)
    {
        return values[index];
    }
}")
                .SetName("Sp0011_AuthoringRuntimeHazards_SuppressUnknownIndexOutOfRange");

            yield return new TestCaseData(
                @"
using System;

public class TestClass
{
    public int GuardedNegativeStackAllocLength(int length)
    {
        if (length >= 0)
        {
            Span<int> span = stackalloc int[length];
            return span.Length;
        }

        return 0;
    }
}")
                .SetName("Sp0011_AuthoringRuntimeHazards_SuppressGuardedNegativeStackAllocLength");

            yield return new TestCaseData(
                @"
using System.Collections.Generic;

public class TestClass
{
    public int GuardedCountIndexOutOfRange(IReadOnlyList<int> values, int index)
    {
        if (index >= 0 && index < values.Count)
        {
            return values[index];
        }

        return 0;
    }
}")
                .SetName("Sp0011_AuthoringRuntimeHazards_SuppressGuardedCountIndexOutOfRange");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int GuardedSwitchExpressionNoMatch(int value)
    {
        if (value <= 0)
        {
            return value switch
            {
                < 0 => -1,
                0 => 0
            };
        }

        return 0;
    }
}")
                .SetName("Sp0011_AuthoringRuntimeHazards_SuppressGuardedSwitchExpressionNoMatch");
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAuthoringHazardDiagnosticsAsync(
            string source,
            string runtimeHazardMode = "sites")
        {
            return await AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_runtime_hazard_mode", runtimeHazardMode),
                allowUnsafe: false,
                additionalFiles: ImmutableArray<AdditionalText>.Empty,
                concurrentAnalysis: true);
        }

        private static Diagnostic SingleRuntimeHazardDiagnostic(ImmutableArray<Diagnostic> diagnostics)
        {
            var siteDiagnostics = diagnostics
                .Where(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId)
                .ToImmutableArray();

            Assert.That(siteDiagnostics, Has.Length.EqualTo(1), FormatDiagnostics(diagnostics));
            return siteDiagnostics[0];
        }

        private static string FormatDiagnostics(ImmutableArray<Diagnostic> diagnostics)
        {
            return string.Join(
                Environment.NewLine,
                diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.GetMessage()));
        }
    }
}
