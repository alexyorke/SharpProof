using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
[Category("SmtHeavy")]
public class ExceptionFlowPathFactStressTests
{
    private const string SourcePredicateSource = @"
public static class SourcePredicates
{
    public static bool IsNullOrEmptyLike(string value)
    {
        return value == null || value.Length == 0;
    }

    public static bool HasText(string value)
    {
        return value != null && value.Length > 0;
    }
}
";

    public enum DiagnosticSurface
    {
        SummarySp0010,
        SiteSp0011
    }

    public enum AssertionMode
    {
        ExactSingle,
        Absent,
        AnyContainsType,
        ForbidType,
        ForbidCategory
    }

    public sealed record ExceptionPathCase(
        string Name,
        string Source,
        DiagnosticSurface Surface,
        AssertionMode Mode,
        string? ExceptionType,
        string? ExceptionCategory,
        string? ExceptionSource);

    private static readonly ExceptionPathCase[] ExceptionPathCasesPart1 =
    {
        new("Sp0010_AndConditionZeroDivisor_ReportsDivideByZeroException", @"
public class TestClass
{
    public int TestMethod(int value, int divisor, bool enabled)
    {
        if (enabled && divisor == 0)
        {
            return value / divisor;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.DivideByZeroException", null, null),
        new("Sp0010_AndConditionNullReceiver_ReportsNullReferenceException", @"
public class TestClass
{
    public int TestMethod(string value, bool enabled)
    {
        if (enabled && value == null)
        {
            return value.Length;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.NullReferenceException", null, null),
        new("Sp0010_GenericClassConstrainedNullReceiver_ReportsNullReferenceException", @"
public class TestClass
{
    public int TestMethod<T>(T value)
        where T : class
    {
        if (value == null)
        {
            return value.GetHashCode();
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.NullReferenceException", "definite_null_dereference", null),
        new("Sp0010_NotNullParameterGuardNormalCompletionSuppressesNullDereference", @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static void Require([NotNull] object? value)
    {
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        if (value == null)
        {
            Guard.Require(value);
            return value.Length;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_NotNullParameterGuardNormalCompletionDoesNotSurviveReassignment", @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static void Require([NotNull] object? value)
    {
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        if (value == null)
        {
            Guard.Require(value);
            value = null;
            return value.Length;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.NullReferenceException", "definite_null_dereference", null),
        new("Sp0010_NullableValueNullLocal_ReportsInvalidOperationException", @"
public class TestClass
{
    public int TestMethod()
    {
        int? value = null;
        return value.Value;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.InvalidOperationException", "definite_nullable_value_without_value", "System.InvalidOperationException=definite_nullable_value_without_value:nullable_value"),
        new("Sp0010_NullableValueDefaultLocal_ReportsInvalidOperationException", @"
public class TestClass
{
    public int TestMethod()
    {
        int? value = default;
        return value.Value;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.InvalidOperationException", "definite_nullable_value_without_value", null),
        new("Sp0010_NullableValueMutatedAfterLoopUse_RemainsConservative", @"
public class TestClass
{
    public int TestMethod(bool repeat)
    {
        int? value = 1;
        var result = 0;
        while (repeat)
        {
            result = value.Value;
            value = null;
        }

        return result;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.AnyContainsType, "System.InvalidOperationException", null, null),
        new("Sp0010_NullableValueReassignedPresentLocal_DoesNotReport", @"
public class TestClass
{
    public int TestMethod()
    {
        int? value = null;
        value = 42;
        return value.Value;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_NullableValueReassignedUnknownLocal_DoesNotReportDefiniteException", @"
public class TestClass
{
    public int TestMethod(int? input)
    {
        int? value = null;
        value = input;
        return value.Value;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_NullableValueFalseHasValueBranch_ReportsInvalidOperationException", @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (!value.HasValue)
        {
            return value.Value;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.InvalidOperationException", "definite_nullable_value_without_value", null),
        new("Sp0010_NullableValueTrueHasValueBranch_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (value.HasValue)
        {
            return value.Value;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_NullableValueCaughtInvalidOperationException_DoesNotReport", @"
using System;

public class TestClass
{
    public int TestMethod(int? value)
    {
        try
        {
            if (!value.HasValue)
            {
                return value.Value;
            }

            return value.Value;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_OrFalseBranchZeroDivisor_ReportsDivideByZeroException", @"
public class TestClass
{
    public int TestMethod(int value, int divisor, bool enabled)
    {
        if (divisor != 0 || enabled)
        {
            return 0;
        }

        return value % divisor;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.DivideByZeroException", null, null),
        new("Sp0010_IsNotNullElseBranch_ReportsNullReferenceException", @"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (value is not null)
        {
            return 0;
        }
        else
        {
            return value.Length;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.NullReferenceException", null, null),
        new("Sp0010_IsNotZeroElseBranch_ReportsDivideByZeroException", @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor is not 0)
        {
            return 0;
        }
        else
        {
            return value / divisor;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.DivideByZeroException", null, null),
        new("Sp0010_AndConditionZeroDivisor_ReassignedBeforeUse_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int value, int divisor, bool enabled)
    {
        if (enabled && divisor == 0)
        {
            divisor = 1;
            return value / divisor;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_OrTrueBranchZeroDivisor_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int value, int divisor, bool enabled)
    {
        if (divisor == 0 || enabled)
        {
            return value / divisor;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_OrTrueBranchNullReceiver_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(string value, bool enabled)
    {
        if (value == null || enabled)
        {
            return value.Length;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
    };

    private static IEnumerable<TestCaseData> ExceptionPathCases()
    {
        var cases = ExceptionPathCasesPart1
            .Concat(ExceptionPathCasesPart2)
            .Concat(ExceptionPathCasesPart3)
            .Concat(ExceptionPathCasesPart4)
            .Concat(ExceptionPathCasesPart5)
            .Concat(ExceptionPathCasesPart6)
            .Concat(ExceptionPathCasesPart7)
            .Concat(ExceptionPathCasesPart8)
            .Concat(ExceptionPathCasesPart9)
            .ToArray();

        if (cases.Length != 163 ||
            cases.Count(static testCase => testCase.Surface == DiagnosticSurface.SummarySp0010) != 139 ||
            cases.Count(static testCase => testCase.Surface == DiagnosticSurface.SiteSp0011) != 24 ||
            cases.Count(static testCase => testCase.Mode == AssertionMode.ExactSingle) != 88 ||
            cases.Count(static testCase => testCase.Mode == AssertionMode.Absent) != 72 ||
            cases.Count(static testCase => testCase.Mode == AssertionMode.AnyContainsType) != 1 ||
            cases.Count(static testCase => testCase.Mode == AssertionMode.ForbidType) != 1 ||
            cases.Count(static testCase => testCase.Mode == AssertionMode.ForbidCategory) != 1 ||
            cases.Select(static testCase => testCase.Name).Distinct(StringComparer.Ordinal).Count() != 163)
        {
            throw new InvalidOperationException("Exception path case invariants failed.");
        }

        return cases.Select(static testCase => new TestCaseData(testCase).SetName(testCase.Name));
    }

    [TestCaseSource(nameof(ExceptionPathCases))]
    public async Task ExceptionFlowPathFactCases(ExceptionPathCase testCase)
    {
        var isSummary = testCase.Surface == DiagnosticSurface.SummarySp0010;
        var diagnostics = await GetAnalyzerDiagnosticsAsync(testCase.Source, isSummary, !isSummary);
        var diagnosticId = isSummary ? SharpProofDiagnostics.ExceptionSummaryId : SharpProofDiagnostics.UncaughtExceptionSiteId;
        var matching = diagnostics.Where(diagnostic => diagnostic.Id == diagnosticId).ToArray();

        switch (testCase.Mode)
        {
            case AssertionMode.ExactSingle:
            {
                var diagnostic = matching.Single();
                if (testCase.ExceptionType is { } exceptionType)
                    Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo(exceptionType));
                if (testCase.ExceptionCategory is { } exceptionCategory)
                    Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo(exceptionCategory));
                if (testCase.ExceptionSource is { } exceptionSource)
                    Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo(exceptionSource));
                break;
            }
            case AssertionMode.Absent:
                Assert.That(matching, Is.Empty);
                break;
            case AssertionMode.AnyContainsType:
                Assert.That(matching.Any(diagnostic => diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty]!
                    .Contains(testCase.ExceptionType!, StringComparison.Ordinal)), Is.True);
                break;
            case AssertionMode.ForbidType:
                Assert.That(matching.Any(diagnostic => diagnostic.Properties.TryGetValue(
                    SharpProofDiagnostics.ExceptionTypesProperty, out var types) && types != null &&
                    types.Contains(testCase.ExceptionType!, StringComparison.Ordinal)), Is.False);
                break;
            case AssertionMode.ForbidCategory:
                Assert.That(matching.Any(diagnostic => diagnostic.Properties.TryGetValue(
                    SharpProofDiagnostics.ExceptionCategoriesProperty, out var categories) && categories != null &&
                    categories.Contains(testCase.ExceptionCategory!, StringComparison.Ordinal)), Is.False);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }





































    private static readonly ExceptionPathCase[] ExceptionPathCasesPart2 =
    {
        new("Sp0010_NegatedNotEqualZero_ReportsDivideByZeroException", @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (!(divisor != 0))
        {
            return value / divisor;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.DivideByZeroException", null, null),
        new("Sp0010_NegatedEqualsZero_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (!(divisor == 0))
        {
            return value / divisor;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_NegatedIsNotNull_ReportsNullReferenceException", @"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (!(value is not null))
        {
            return value.Length;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.NullReferenceException", null, null),
        new("Sp0010_AndConditionNonZeroDivisor_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int value, int divisor, bool enabled)
    {
        if (enabled && divisor != 0)
        {
            return value / divisor;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_ContradictoryShortCircuitDivideByZero_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor != 0 && divisor == 0)
        {
            return value / divisor;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_ContradictoryShortCircuitNullDereference_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (value != null && value == null)
        {
            return value.Length;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_GuardFalsePathReassignedBeforeUse_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor != 0)
        {
            return 0;
        }

        divisor = 1;
        return value / divisor;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_BranchFactDivideByZeroCaught_IsSuppressed", @"
using System;

public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        try
        {
            if (divisor == 0)
            {
                return value / divisor;
            }

            return 0;
        }
        catch (DivideByZeroException)
        {
            return 0;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_WhileConditionZeroDivisor_ReportsDivideByZeroException", @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        while (divisor == 0)
        {
            return value / divisor;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.DivideByZeroException", null, null),
        new("Sp0010_WhileConditionNullReceiver_ReportsNullReferenceException", @"
public class TestClass
{
    public int TestMethod(string value)
    {
        while (value == null)
        {
            return value.Length;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.NullReferenceException", null, null),
        new("Sp0010_NegativeArrayIndex_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        return values[-1];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_NegativeIndexGuard_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (index < 0)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_UpperBoundIndexGuard_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (index >= values.Length)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_MultidimensionalArrayRowUpperBoundGuard_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod(int[,] values, int row)
    {
        if (row >= values.GetLength(0))
        {
            return values[row, 0];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_MultidimensionalArrayGuardedInRange_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[,] values, int row, int column)
    {
        if (row >= 0 &&
            row < values.GetLength(0) &&
            column >= 0 &&
            column < values.GetLength(1))
        {
            return values[row, column];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_DirectMultidimensionalArrayCreationOutOfRange_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod()
    {
        return (new int[1, 2])[0, 2];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_AssignedMultidimensionalArrayCreationOutOfRange_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod(int rows, int columns)
    {
        var values = new int[rows, columns];
        return values[rows, 0];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_ReadOnlySpanUpperBoundIndexGuard_ReportsIndexOutOfRangeException", @"
using System;

public class TestClass
{
    public int TestMethod(ReadOnlySpan<int> values, int index)
    {
        if (index >= values.Length)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_ReadOnlySpanGuardedInRange_DoesNotReport", @"
using System;

public class TestClass
{
    public int TestMethod(ReadOnlySpan<int> values, int index)
    {
        if (index >= 0 && index < values.Length)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
    };





































    private static readonly ExceptionPathCase[] ExceptionPathCasesPart3 =
    {
        new("Sp0010_SpanUpperBoundIndexGuard_ReportsIndexOutOfRangeException", @"
using System;

public class TestClass
{
    public int TestMethod(Span<int> values, int index)
    {
        if (index >= values.Length)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_SystemIndexVariableFromEndZeroGuard_ReportsIndexOutOfRangeException", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int offset)
    {
        Index index = 0;
        index = ^offset;
        if (offset == 0)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_SystemIndexVariableFromEndGuardedInRange_DoesNotReport", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int offset)
    {
        Index index = ^offset;
        if (offset > 0 && offset <= values.Length)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_SystemIndexFactoryFromEndZeroGuard_ReportsIndexOutOfRangeException", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int offset)
    {
        if (offset == 0)
        {
            return values[Index.FromEnd(offset)];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_SystemIndexFactoriesGuardedInRange_DoesNotReport", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int start, int offset)
    {
        if (start >= 0 && start < values.Length && offset > 0 && offset <= values.Length)
        {
            return values[Index.FromStart(start)] +
                values[Index.FromEnd(offset)] +
                values[new Index(start)] +
                values[new Index(offset, true)];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_SystemIndexFactoryNegativeInput_DoesNotReportIndexOutOfRangeException", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (index < 0)
        {
            return values[Index.FromStart(index)];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ForbidType, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_DirectArrayRangeStartAfterEnd_ReportsArgumentOutOfRangeException", @"
public class TestClass
{
    public int[] TestMethod(int[] values)
    {
        return values[2..1];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.ArgumentOutOfRangeException", "definite_range_out_of_range", "System.ArgumentOutOfRangeException=definite_range_out_of_range:range_slice"),
        new("Sp0010_DirectReadOnlySpanRangeStartAfterEnd_ReportsArgumentOutOfRangeException", @"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values)
    {
        return values[2..1];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.ArgumentOutOfRangeException", "definite_range_out_of_range", "System.ArgumentOutOfRangeException=definite_range_out_of_range:range_slice"),
        new("Sp0010_ReadOnlySpanSliceStartGreaterThanLength_ReportsArgumentOutOfRangeException", @"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values)
    {
        return values.Slice(values.Length + 1);
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.ArgumentOutOfRangeException", "definite_range_out_of_range", "System.ArgumentOutOfRangeException=definite_range_out_of_range:span_slice"),
        new("Sp0010_SpanSliceNegativeLength_ReportsArgumentOutOfRangeException", @"
using System;

public class TestClass
{
    public Span<int> TestMethod(Span<int> values)
    {
        return values.Slice(0, -1);
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.ArgumentOutOfRangeException", "definite_range_out_of_range", "System.ArgumentOutOfRangeException=definite_range_out_of_range:span_slice"),
        new("Sp0010_ReadOnlySpanSliceStartPlusLengthPastEnd_ReportsArgumentOutOfRangeException", @"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values, int start, int length)
    {
        if (start >= 0 && length >= 0 && start + length > values.Length)
        {
            return values.Slice(start, length);
        }

        return values.Slice(0, 0);
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.ArgumentOutOfRangeException", "definite_range_out_of_range", "System.ArgumentOutOfRangeException=definite_range_out_of_range:span_slice"),
        new("Sp0010_AssignedReadOnlySpanSliceIndexOutOfRange_ReportsIndexOutOfRangeException", @"
using System;

public class TestClass
{
    public int TestMethod(ReadOnlySpan<int> values)
    {
        var tail = values.Slice(values.Length);
        return tail[0];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_MemorySliceAliasLengthOutOfRange_ReportsArgumentOutOfRangeException", @"
using System;

public class TestClass
{
    public Memory<int> TestMethod(Memory<int> values, int start)
    {
        var copy = values;
        if (start > copy.Length)
        {
            return values.Slice(start);
        }

        return values.Slice(0, 0);
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.ArgumentOutOfRangeException", "definite_range_out_of_range", null),
        new("Sp0010_ReadOnlySpanSliceGuardedInRange_DoesNotReport", @"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values, int start, int length)
    {
        if (start >= 0 && length >= 0 && start + length <= values.Length)
        {
            return values.Slice(start, length);
        }

        return values.Slice(0, 0);
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_ReadOnlySpanSliceCaught_DoesNotReport", @"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values)
    {
        try
        {
            return values.Slice(values.Length + 1);
        }
        catch (ArgumentOutOfRangeException)
        {
            return values.Slice(0, 0);
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_ArrayRangeGuardedStartAfterEnd_ReportsArgumentOutOfRangeException", @"
public class TestClass
{
    public int[] TestMethod(int[] values, int start, int end)
    {
        if (start > end)
        {
            return values[start..end];
        }

        return values[0..0];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.ArgumentOutOfRangeException", "definite_range_out_of_range", null),
        new("Sp0010_ArrayRangeGuardedInRange_DoesNotReport", @"
public class TestClass
{
    public int[] TestMethod(int[] values, int start, int end)
    {
        if (start >= 0 && start <= end && end <= values.Length)
        {
            return values[start..end];
        }

        return values[0..0];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_SystemRangeVariableStartAfterEnd_ReportsArgumentOutOfRangeException", @"
using System;

public class TestClass
{
    public int[] TestMethod(int[] values, int start, int end)
    {
        Range range = start..end;
        if (start > end)
        {
            return values[range];
        }

        return values[0..0];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.ArgumentOutOfRangeException", "definite_range_out_of_range", null),
        new("Sp0010_SystemRangeVariableReassignedFromUnknown_DoesNotReport", @"
using System;

public class TestClass
{
    public int[] TestMethod(int[] values, int start, int end, Range other)
    {
        Range range = start..end;
        range = other;
        if (start > end)
        {
            return values[range];
        }

        return values[0..0];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
    };





































    private static readonly ExceptionPathCase[] ExceptionPathCasesPart4 =
    {
        new("Sp0010_SystemRangeFactoryStartAfterEnd_ReportsArgumentOutOfRangeException", @"
using System;

public class TestClass
{
    public int[] TestMethod(int[] values)
    {
        return values[new Range(Index.FromStart(2), Index.FromStart(1))];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.ArgumentOutOfRangeException", "definite_range_out_of_range", null),
        new("Sp0010_SystemRangeFactoriesGuardedInRange_DoesNotReport", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values.Length >= 2)
        {
            var startAt = values[Range.StartAt(Index.FromStart(1))];
            var endAt = values[Range.EndAt(Index.FromEnd(1))];
            var constructed = values[new Range(Index.FromStart(1), Index.FromEnd(1))];
            var all = values[Range.All];
            return startAt.Length + endAt.Length + constructed.Length + all.Length;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_SystemRangeFactoryNegativeEndpoint_DoesNotReportRangeOutOfRangeException", @"
using System;

public class TestClass
{
    public int[] TestMethod(int[] values, int start)
    {
        if (start < 0)
        {
            return values[Range.StartAt(Index.FromStart(start))];
        }

        return values[Range.All];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ForbidCategory, null, "definite_range_out_of_range", null),
        new("Sp0010_InRangeArrayIndexGuard_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (index >= 0 && index < values.Length)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_ContradictoryShortCircuitIndexOutOfRange_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (index >= 0 && index < values.Length && index >= values.Length)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_UnsignedCastArrayIndexGuard_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if ((uint)index < (uint)values.Length)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_UnsignedCastArrayIndexFalseBranch_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if ((uint)index < (uint)values.Length)
        {
            return 0;
        }

        return values[index];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_UnsignedCastArrayUpperBoundGuard_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if ((uint)index >= (uint)values.Length)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_WhileConditionUpperBoundIndex_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        while (index >= values.Length)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_ForConditionUpperBoundIndex_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        for (; index >= values.Length; index++)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_ForLoopMonotonicIndexBounds_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        var sum = 0;
        for (var index = 0; index < values.Length; index++)
        {
            sum += values[index];
        }

        return sum;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_ForLoopReverseIndexBounds_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        var sum = 0;
        for (var index = values.Length - 1; index >= 0; index--)
        {
            sum += values[index];
        }

        return sum;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_WhileLoopMonotonicIndexBounds_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        var sum = 0;
        var index = 0;
        while (index < values.Length)
        {
            sum += values[index];
            index++;
        }

        return sum;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_WhileLoopReverseIndexBounds_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        var sum = 0;
        var index = values.Length - 1;
        while (index >= 0)
        {
            sum += values[index];
            index--;
        }

        return sum;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_LoopConditionIndexReassignedBeforeUse_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        while (index >= values.Length)
        {
            index = 0;
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_WhileFalseBodyIndexOutOfRange_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        while (values.Length < 0)
        {
            return values[-1];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_ArrayLengthNegativeGuardedIndex_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values.Length < 0)
        {
            return values[-1];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_SwitchExpressionArrayLengthNegativeArm_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        return values.Length switch
        {
            < 0 => values[-1],
            _ => 0
        };
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_OutOfRangeGuardReassignedBeforeUse_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (index < 0)
        {
            index = 0;
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
    };





































    private static readonly ExceptionPathCase[] ExceptionPathCasesPart5 =
    {
        new("Sp0010_BranchFactIndexOutOfRangeCaught_IsSuppressed", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        try
        {
            if (index >= values.Length)
            {
                return values[index];
            }

            return 0;
        }
        catch (IndexOutOfRangeException)
        {
            return 0;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_LocalArrayCreationConstantUpperBound_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        return values[4];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_DirectArrayCreationUpperBound_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod()
    {
        return (new int[4])[4];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_LocalArrayCreationSymbolicUpperBound_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod()
    {
        var length = 4;
        var values = new int[length];
        return values[length];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_ArrayEmptyIndex_ReportsIndexOutOfRangeException", @"
using System;

public class TestClass
{
    public int TestMethod()
    {
        var values = Array.Empty<int>();
        return values[0];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_ArrayCollectionExpressionIndex_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod()
    {
        int[] values = [1, 2, 3];
        return values[3];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_ArrayAliasUpperBound_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        var alias = values;
        return alias[4];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_ObjectErasedArrayCastAliasUpperBound_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        object boxed = values;
        var alias = (int[])boxed;
        return alias[4];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_NegativeStringIndexGuard_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public char TestMethod(string text, int index)
    {
        if (index < 0)
        {
            return text[index];
        }

        return '\0';
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_StringUpperBoundIndexGuard_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public char TestMethod(string text, int index)
    {
        if (index >= text.Length)
        {
            return text[index];
        }

        return '\0';
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_UnsignedCastStringIndexGuard_DoesNotReport", @"
public class TestClass
{
    public char TestMethod(string text, int index)
    {
        if ((uint)index < (uint)text.Length)
        {
            return text[index];
        }

        return '\0';
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_UnsignedCastStringUpperBoundGuard_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public char TestMethod(string text, int index)
    {
        if ((uint)index >= (uint)text.Length)
        {
            return text[index];
        }

        return '\0';
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_StringLiteralUpperBound_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public char TestMethod()
    {
        var text = ""abc"";
        return text[3];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_DirectStringLiteralUpperBound_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public char TestMethod()
    {
        return ""abc""[3];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_StringEmptyIndex_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public char TestMethod()
    {
        var text = string.Empty;
        return text[0];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_DirectStringLiteralInRangeIndex_DoesNotReport", @"
public class TestClass
{
    public char TestMethod()
    {
        return ""abc""[2];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_StringAliasUpperBound_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public char TestMethod(string input)
    {
        var text = input;
        var alias = text;
        return alias[input.Length];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_StringLiteralInRangeIndex_DoesNotReport", @"
public class TestClass
{
    public char TestMethod()
    {
        var text = ""abc"";
        return text[2];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_SourceNullOrEmptyPredicateNonNullIndex_ReportsIndexOutOfRangeException", SourcePredicateSource + @"
public class TestClass
{
    public char TestMethod(string text)
    {
        if (SourcePredicates.IsNullOrEmptyLike(text) && text != null)
        {
            return text[0];
        }

        return '\0';
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
    };





































    private static readonly ExceptionPathCase[] ExceptionPathCasesPart6 =
    {
        new("Sp0010_SourceNullOrEmptyPredicateFalseBranchIndex_DoesNotReport", SourcePredicateSource + @"
public class TestClass
{
    public char TestMethod(string text)
    {
        if (!SourcePredicates.IsNullOrEmptyLike(text))
        {
            return text[0];
        }

        return '\0';
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_SourceHasTextPredicateContradictoryIndex_DoesNotReport", SourcePredicateSource + @"
public class TestClass
{
    public char TestMethod(string text)
    {
        if (SourcePredicates.HasText(text) && text.Length <= 0)
        {
            return text[0];
        }

        return '\0';
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_ArrayCollectionExpressionSpreadIndex_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        int[] values = [.. input];
        return values[0];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_ArrayCollectionExpressionSpreadFixedElementsPruneZeroLengthBranch_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        int[] values = [.. input, 1];
        if (values.Length == 0)
        {
            return values[0];
        }

        return values[0];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_IndexAssignedFromArrayLength_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        var index = values.Length;
        return values[index];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_LocalArrayCreationInRangeIndex_DoesNotReport", @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        return values[3];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_ArrayLengthFactRemovedAfterReassignment_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        var values = new int[1];
        values = input;
        return values[1];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_ConditionalArrayReassignmentInvalidatesLength_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var values = new int[1];
        if (flag)
        {
            values = new int[2];
        }

        return values[1];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_SelfReferentialIndexAssignment_DoesNotReportFromUnsatisfiableFacts", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        index = index + 1;
        return values[index];
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_SwitchStatementConstantZeroDivisor_ReportsDivideByZeroException", @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        switch (divisor)
        {
            case 0:
                return 1 / divisor;
            default:
                return 0;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.DivideByZeroException", null, null),
        new("Sp0010_SwitchStatementNonZeroCase_DoesNotReportDivideByZeroException", @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        switch (divisor)
        {
            case 1:
                return 1 / divisor;
            default:
                return 0;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_SwitchStatementReassignmentInvalidatesCaseFact_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        switch (divisor)
        {
            case 0:
                divisor = 1;
                return 1 / divisor;
            default:
                return 0;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_SwitchStatementNullCase_ReportsNullReferenceException", @"
public class TestClass
{
    public int TestMethod(string value)
    {
        switch (value)
        {
            case null:
                return value.Length;
            default:
                return 0;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.NullReferenceException", null, null),
        new("Sp0010_SwitchStatementRelationalPatternIndex_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        switch (index)
        {
            case < 0:
                return values[index];
            default:
                return 0;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_SwitchExpressionRelationalPatternIndex_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        return index switch
        {
            < 0 => values[index],
            _ => 0
        };
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_SwitchExpressionWhenGuardIndex_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        return index switch
        {
            _ when index >= values.Length => values[index],
            _ => 0
        };
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", null, null),
        new("Sp0010_SwitchExpressionWhenGuardInRange_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        return index switch
        {
            _ when index >= 0 && index < values.Length => values[index],
            _ => 0
        };
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_ForeachNewEmptyArrayConstantDivide_DoesNotReport", @"
public class TestClass
{
    public int TestMethod()
    {
        foreach (var value in new int[0])
        {
            return 1 / 0;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_NullGuardedForeachBodyConstantDivide_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values == null)
        {
            foreach (var value in values)
            {
                return 1 / 0;
            }
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
    };





































    private static readonly ExceptionPathCase[] ExceptionPathCasesPart7 =
    {
        new("Sp0010_SingleElementForeachZeroDivisor_ReportsDivideByZeroException", @"
public class TestClass
{
    public int TestMethod()
    {
        foreach (var divisor in new[] { 0 })
        {
            return 10 / divisor;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.DivideByZeroException", "definite_divide_by_zero", null),
        new("Sp0010_SingleElementForeachNonZeroDivisor_DoesNotReport", @"
public class TestClass
{
    public int TestMethod()
    {
        foreach (var divisor in new[] { 5 })
        {
            return 10 / divisor;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_FiniteForeachNonZeroContradictoryDivide_DoesNotReport", @"
public class TestClass
{
    public int TestMethod()
    {
        foreach (var divisor in new[] { 5, 10 })
        {
            if (divisor == 0)
            {
                return 10 / 0;
            }
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_CoalesceAssignmentNonNullContradictoryNullDereference_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(string value)
    {
        value ??= ""safe"";
        if (value == null)
        {
            return value.Length;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_PriorAssignedFiniteForeachContradictoryDivide_DoesNotReport", @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new[] { 5, 10 };
        foreach (var divisor in values)
        {
            if (divisor == 0)
            {
                return 10 / 0;
            }
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_CatchFilterNonZeroDivisor_DoesNotReport", @"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        try
        {
            return 0;
        }
        catch (InvalidOperationException) when (divisor != 0)
        {
            return 10 / divisor;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_CatchFilterZeroDivisor_ReportsDivideByZeroException", @"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        try
        {
            return 0;
        }
        catch (InvalidOperationException) when (divisor == 0)
        {
            return 10 / divisor;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.DivideByZeroException", "definite_divide_by_zero", null),
        new("Sp0010_CatchFilterNullReceiver_ReportsNullReferenceException", @"
using System;

public class TestClass
{
    public int TestMethod(string value)
    {
        try
        {
            return 0;
        }
        catch (InvalidOperationException) when (value == null)
        {
            return value.Length;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.NullReferenceException", "definite_null_dereference", null),
        new("Sp0010_CatchFilterUpperBoundIndex_ReportsIndexOutOfRangeException", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        try
        {
            return 0;
        }
        catch (InvalidOperationException) when (index >= values.Length)
        {
            return values[index];
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_CatchFilterFactReassignedBeforeUse_DoesNotReport", @"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        try
        {
            return 0;
        }
        catch (InvalidOperationException) when (divisor == 0)
        {
            divisor = 1;
            return 10 / divisor;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_ContinueGuardZeroDivisor_ReportsDivideByZeroException", @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        for (var i = 0; i < 1; i++)
        {
            if (divisor != 0)
            {
                continue;
            }

            return 10 / divisor;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.DivideByZeroException", "definite_divide_by_zero", null),
        new("Sp0010_ContinueGuardUpperBoundIndex_ReportsIndexOutOfRangeException", @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        for (var i = 0; i < 1; i++)
        {
            if (index < values.Length)
            {
                continue;
            }

            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0010_BreakGuardNullReceiver_ReportsNullReferenceException", @"
public class TestClass
{
    public int TestMethod(string value)
    {
        while (true)
        {
            if (value != null)
            {
                break;
            }

            return value.Length;
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.NullReferenceException", "definite_null_dereference", null),
        new("Sp0010_PathSensitiveFinallyThrow_ShadowsGuardedDirectThrow", @"
using System;

public class TestClass
{
    public void TestMethod(int divisor)
    {
        if (divisor == 0)
        {
            try
            {
                throw new InvalidOperationException();
            }
            finally
            {
                if (divisor == 0)
                {
                    throw new ApplicationException();
                }
            }
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.ApplicationException", "direct_throw", null),
        new("Sp0010_PathSensitiveFinallyThrow_ShadowsGuardedDivideByZero", @"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
            try
            {
                return 10 / divisor;
            }
            finally
            {
                if (divisor == 0)
                {
                    throw new ApplicationException();
                }
            }
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.ApplicationException", "direct_throw", null),
        new("Sp0010_PathSensitiveFinallyNestedBlockThrow_ShadowsGuardedDivideByZero", @"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
            try
            {
                return 10 / divisor;
            }
            finally
            {
                {
                    if (divisor == 0)
                    {
                        throw new ApplicationException();
                    }
                }
            }
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.ApplicationException", "direct_throw", null),
        new("Sp0010_PathSensitiveFinallyNestedAliasThrow_ShadowsGuardedDivideByZero", @"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
            try
            {
                return 10 / divisor;
            }
            finally
            {
                {
                    var mustThrow = divisor == 0;
                    if (mustThrow)
                    {
                        throw new ApplicationException();
                    }
                }
            }
        }

        return 0;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.ApplicationException", "direct_throw", null),
        new("Sp0010_FinallyUnknownThrowCondition_DoesNotShadowTryThrow", @"
using System;

public class TestClass
{
    public void TestMethod(bool enabled)
    {
        try
        {
            throw new InvalidOperationException();
        }
        finally
        {
            if (enabled)
            {
                throw new ApplicationException();
            }
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, null, null, null),
        new("Sp0011_PathSensitiveFinallyThrow_ShadowsCheckedDirectThrowSite", @"
using System;

public class TestClass
{
    public void TestMethod(int divisor)
    {
        if (divisor == 0)
        {
            try
            {
                throw new InvalidOperationException();
            }
            finally
            {
                if (divisor == 0)
                {
                    throw new ApplicationException();
                }
            }
        }
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.ApplicationException", "direct_throw", null),
    };





































    private static readonly ExceptionPathCase[] ExceptionPathCasesPart8 =
    {
        new("Sp0010_CatchFilterTrueFromThrowSiteBranch_SuppressesException", @"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        try
        {
            if (divisor == 0)
            {
                throw new InvalidOperationException();
            }

            return 1;
        }
        catch (InvalidOperationException) when (divisor == 0)
        {
            return 0;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_CatchFilterBooleanAliasFromThrowSiteBranch_SuppressesException", @"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        var shouldCatch = divisor == 0;
        try
        {
            if (divisor == 0)
            {
                throw new InvalidOperationException();
            }

            return 1;
        }
        catch (InvalidOperationException) when (shouldCatch)
        {
            return 0;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_CatchFilterAliasPrunesContradictoryIndexUse_DoesNotReport", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        var inRange = index >= 0 && index < values.Length;
        try
        {
            return 0;
        }
        catch (InvalidOperationException) when (inRange)
        {
            if (index < 0 || index >= values.Length)
            {
                return values[index];
            }

            return values[index];
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_CatchFilterUnknownAtThrowSite_DoesNotSuppressException", @"
using System;

public class TestClass
{
    public int TestMethod(bool enabled)
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (enabled)
        {
            return 0;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.InvalidOperationException", "direct_throw", null),
        new("Sp0010_NestedCatchFilterTrueFromOuterCatchFilter_SuppressesException", @"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        try
        {
            if (divisor == 0)
            {
                throw new ApplicationException();
            }

            return 1;
        }
        catch (ApplicationException) when (divisor == 0)
        {
            try
            {
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException) when (divisor == 0)
            {
                return 0;
            }
        }

        return 2;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0010_NestedCatchFilterFactReassignedBeforeThrow_RemainsConservativeReports", @"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        try
        {
            if (divisor == 0)
            {
                throw new ApplicationException();
            }

            return 1;
        }
        catch (ApplicationException) when (divisor == 0)
        {
            divisor = 1;
            try
            {
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException) when (divisor == 0)
            {
                return 0;
            }
        }

        return 2;
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.ExactSingle, "System.InvalidOperationException", "direct_throw", null),
        new("Sp0010_FilteredCatchContradictoryGuardedThrow_DoesNotReport", @"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        try
        {
            if (divisor == 0)
            {
                throw new ApplicationException();
            }

            return 1;
        }
        catch (ApplicationException) when (divisor == 0)
        {
            if (divisor != 0)
            {
                throw new InvalidOperationException();
            }

            return 0;
        }
    }
}", DiagnosticSurface.SummarySp0010, AssertionMode.Absent, null, null, null),
        new("Sp0011_CatchFilterTrueFromThrowSiteBranch_SuppressesExceptionSite", @"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        try
        {
            if (divisor == 0)
            {
                throw new InvalidOperationException();
            }

            return 1;
        }
        catch (InvalidOperationException) when (divisor == 0)
        {
            return 0;
        }
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.Absent, null, null, null),
        new("Sp0011_NestedCatchFilterTrueFromOuterCatchFilter_SuppressesExceptionSite", @"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        try
        {
            if (divisor == 0)
            {
                throw new ApplicationException();
            }

            return 1;
        }
        catch (ApplicationException) when (divisor == 0)
        {
            try
            {
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException) when (divisor == 0)
            {
                return 0;
            }
        }

        return 2;
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.Absent, null, null, null),
        new("Sp0011_FilteredCatchContradictoryGuardedThrow_DoesNotReportExceptionSite", @"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        try
        {
            if (divisor == 0)
            {
                throw new ApplicationException();
            }

            return 1;
        }
        catch (ApplicationException) when (divisor == 0)
        {
            if (divisor != 0)
            {
                throw new InvalidOperationException();
            }

            return 0;
        }
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.Absent, null, null, null),
        new("Sp0011_CatchFilterUnknownAtThrowSite_ReportsExceptionSite", @"
using System;

public class TestClass
{
    public int TestMethod(bool enabled)
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (enabled)
        {
            return 0;
        }
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.InvalidOperationException", "direct_throw", null),
        new("Sp0011_NullableValueFalseHasValueBranch_ReportsExceptionSite", @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (!value.HasValue)
        {
            return value.Value;
        }

        return 0;
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.InvalidOperationException", "definite_nullable_value_without_value", "System.InvalidOperationException=definite_nullable_value_without_value:nullable_value"),
        new("Sp0011_ConditionalAccessNullReceiverNullableValue_ReportsExceptionSite", @"
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
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.InvalidOperationException", "definite_nullable_value_without_value", "System.InvalidOperationException=definite_nullable_value_without_value:nullable_value"),
        new("Sp0011_NullableCoalesceMissingFallback_ReportsExceptionSite", @"
public class TestClass
{
    public int TestMethod(int? left)
    {
        int? value = left ?? (int?)null;
        if (!left.HasValue)
        {
            return value.Value;
        }

        return 0;
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.InvalidOperationException", "definite_nullable_value_without_value", "System.InvalidOperationException=definite_nullable_value_without_value:nullable_value"),
        new("Sp0011_ConditionalNullableMissingBranch_ReportsExceptionSite", @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        int? value = flag ? default(int?) : 5;
        if (flag)
        {
            return value.Value;
        }

        return 0;
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.InvalidOperationException", "definite_nullable_value_without_value", "System.InvalidOperationException=definite_nullable_value_without_value:nullable_value"),
        new("Sp0011_NullableValueTrueHasValueBranch_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (value.HasValue)
        {
            return value.Value;
        }

        return 0;
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.Absent, null, null, null),
        new("Sp0011_UnboxNullCast_ReportsExceptionSite", @"
public class TestClass
{
    public int TestMethod()
    {
        object value = null;
        return (int)value;
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.NullReferenceException", "definite_unbox_null", "System.NullReferenceException=definite_unbox_null:cast"),
        new("Sp0011_InvalidReferenceCast_ReportsExceptionSite", @"
public class TestClass
{
    public int TestMethod()
    {
        object value = 42;
        return ((string)value).Length;
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.InvalidCastException", "definite_invalid_cast", "System.InvalidCastException=definite_invalid_cast:cast"),
        new("Sp0011_SystemIndexVariableFromEndZeroGuard_ReportsExceptionSite", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int offset)
    {
        Index index = ^offset;
        if (offset == 0)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
    };





































    private static readonly ExceptionPathCase[] ExceptionPathCasesPart9 =
    {
        new("Sp0011_SystemIndexVariableFromEndGuardedInRange_DoesNotReport", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int offset)
    {
        Index index = ^offset;
        if (offset > 0 && offset <= values.Length)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.Absent, null, null, null),
        new("Sp0011_SystemIndexFactoryFromEndZeroGuard_ReportsExceptionSite", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int offset)
    {
        if (offset == 0)
        {
            return values[Index.FromEnd(offset)];
        }

        return 0;
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0011_ReadOnlySpanUpperBoundIndex_ReportsExceptionSite", @"
using System;

public class TestClass
{
    public int TestMethod(ReadOnlySpan<int> values, int index)
    {
        if (index >= values.Length)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0011_ReadOnlySpanGuardedInRange_DoesNotReport", @"
using System;

public class TestClass
{
    public int TestMethod(ReadOnlySpan<int> values, int index)
    {
        if (index >= 0 && index < values.Length)
        {
            return values[index];
        }

        return 0;
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.Absent, null, null, null),
        new("Sp0011_MultidimensionalArrayRowUpperBoundGuard_ReportsExceptionSite", @"
public class TestClass
{
    public int TestMethod(int[,] values, int row)
    {
        if (row >= values.GetLength(0))
        {
            return values[row, 0];
        }

        return 0;
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.IndexOutOfRangeException", "definite_index_out_of_range", null),
        new("Sp0011_MultidimensionalArrayGuardedInRange_DoesNotReport", @"
public class TestClass
{
    public int TestMethod(int[,] values, int row, int column)
    {
        if (row >= 0 &&
            row < values.GetLength(0) &&
            column >= 0 &&
            column < values.GetLength(1))
        {
            return values[row, column];
        }

        return 0;
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.Absent, null, null, null),
        new("Sp0011_DirectReadOnlySpanRangeStartAfterEnd_ReportsExceptionSite", @"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values)
    {
        return values[2..1];
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.ArgumentOutOfRangeException", "definite_range_out_of_range", "System.ArgumentOutOfRangeException=definite_range_out_of_range:range_slice"),
        new("Sp0011_DirectReadOnlySpanRangeStartAfterEndCaught_DoesNotReport", @"
using System;

public class TestClass
{
    public int TestMethod(ReadOnlySpan<int> values)
    {
        try
        {
            _ = values[2..1];
            return 1;
        }
        catch (ArgumentOutOfRangeException)
        {
            return 0;
        }
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.Absent, null, null, null),
        new("Sp0011_DirectArrayRangeStartAfterEnd_ReportsExceptionSite", @"
public class TestClass
{
    public int[] TestMethod(int[] values)
    {
        return values[2..1];
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.ArgumentOutOfRangeException", "definite_range_out_of_range", "System.ArgumentOutOfRangeException=definite_range_out_of_range:range_slice"),
        new("Sp0011_SystemRangeFactoryStartAfterEnd_ReportsExceptionSite", @"
using System;

public class TestClass
{
    public int[] TestMethod(int[] values)
    {
        return values[new Range(Index.FromStart(2), Index.FromStart(1))];
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.ExactSingle, "System.ArgumentOutOfRangeException", "definite_range_out_of_range", "System.ArgumentOutOfRangeException=definite_range_out_of_range:range_slice"),
        new("Sp0011_DirectArrayRangeStartAfterEndCaught_DoesNotReport", @"
using System;

public class TestClass
{
    public int TestMethod(int[] values)
    {
        try
        {
            _ = values[2..1];
            return 1;
        }
        catch (ArgumentOutOfRangeException)
        {
            return 0;
        }
    }
}", DiagnosticSurface.SiteSp0011, AssertionMode.Absent, null, null, null),
    };





















    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
        string source,
        bool reportExceptions = true,
        bool checkedExceptions = false)
    {
        return await AnalyzerTestHost.GetExceptionFlowDiagnosticsAsync(
            source,
            "ExceptionFlowPathFactStressTests",
            reportExceptions,
            checkedExceptions,
            frameworkReferences: AnalyzerTestHost.GetMinimalFrameworkReferences(),
            concurrentAnalysis: true);
    }
}
