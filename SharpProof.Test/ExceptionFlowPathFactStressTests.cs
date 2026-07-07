using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test
{
    [TestFixture]
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

        [Test]
        public async Task Sp0010_AndConditionZeroDivisor_ReportsDivideByZeroException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Sp0010_AndConditionNullReceiver_ReportsNullReferenceException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Sp0010_GenericClassConstrainedNullReceiver_ReportsNullReferenceException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
        }

        [Test]
        public async Task Sp0010_NotNullParameterGuardNormalCompletionSuppressesNullDereference()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_NotNullParameterGuardNormalCompletionDoesNotSurviveReassignment()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
        }

        [Test]
        public async Task Sp0010_NullableValueNullLocal_ReportsInvalidOperationException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        int? value = null;
        return value.Value;
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_nullable_value_without_value"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.InvalidOperationException=definite_nullable_value_without_value:nullable_value"));
        }

        [Test]
        public async Task Sp0010_NullableValueDefaultLocal_ReportsInvalidOperationException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        int? value = default;
        return value.Value;
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_nullable_value_without_value"));
        }

        [Test]
        public async Task Sp0010_NullableValueReassignedPresentLocal_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        int? value = null;
        value = 42;
        return value.Value;
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_NullableValueReassignedUnknownLocal_DoesNotReportDefiniteException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int? input)
    {
        int? value = null;
        value = input;
        return value.Value;
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_NullableValueFalseHasValueBranch_ReportsInvalidOperationException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_nullable_value_without_value"));
        }

        [Test]
        public async Task Sp0010_NullableValueTrueHasValueBranch_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_NullableValueCaughtInvalidOperationException_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_OrFalseBranchZeroDivisor_ReportsDivideByZeroException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Sp0010_IsNotNullElseBranch_ReportsNullReferenceException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Sp0010_IsNotZeroElseBranch_ReportsDivideByZeroException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Sp0010_AndConditionZeroDivisor_ReassignedBeforeUse_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(
                diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId),
                Is.False,
                string.Join(
                    "; ",
                    diagnostics
                        .Where(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId)
                        .Select(d => d.Properties[SharpProofDiagnostics.ExceptionTypesProperty])));
        }

        [Test]
        public async Task Sp0010_OrTrueBranchZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_OrTrueBranchNullReceiver_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_NegatedNotEqualZero_ReportsDivideByZeroException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Sp0010_NegatedEqualsZero_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_NegatedIsNotNull_ReportsNullReferenceException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Sp0010_AndConditionNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_ContradictoryShortCircuitDivideByZero_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_ContradictoryShortCircuitNullDereference_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_GuardFalsePathReassignedBeforeUse_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_BranchFactDivideByZeroCaught_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_WhileConditionZeroDivisor_ReportsDivideByZeroException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Sp0010_WhileConditionNullReceiver_ReportsNullReferenceException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Sp0010_NegativeArrayIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        return values[-1];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_NegativeIndexGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_UpperBoundIndexGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_MultidimensionalArrayRowUpperBoundGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_MultidimensionalArrayGuardedInRange_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_DirectMultidimensionalArrayCreationOutOfRange_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        return (new int[1, 2])[0, 2];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_AssignedMultidimensionalArrayCreationOutOfRange_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int rows, int columns)
    {
        var values = new int[rows, columns];
        return values[rows, 0];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_ReadOnlySpanUpperBoundIndexGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_ReadOnlySpanGuardedInRange_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_SpanUpperBoundIndexGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_SystemIndexVariableFromEndZeroGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_SystemIndexVariableFromEndGuardedInRange_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_SystemIndexFactoryFromEndZeroGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_SystemIndexFactoriesGuardedInRange_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_SystemIndexFactoryNegativeInput_DoesNotReportIndexOutOfRangeException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(
                diagnostics
                    .Where(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId)
                    .Any(d =>
                        d.Properties.TryGetValue(SharpProofDiagnostics.ExceptionTypesProperty, out var exceptionTypes) &&
                        exceptionTypes != null &&
                        exceptionTypes.Contains("System.IndexOutOfRangeException")),
                Is.False);
        }

        [Test]
        public async Task Sp0010_DirectArrayRangeStartAfterEnd_ReportsArgumentOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int[] TestMethod(int[] values)
    {
        return values[2..1];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArgumentOutOfRangeException=definite_range_out_of_range:range_slice"));
        }

        [Test]
        public async Task Sp0010_DirectReadOnlySpanRangeStartAfterEnd_ReportsArgumentOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values)
    {
        return values[2..1];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArgumentOutOfRangeException=definite_range_out_of_range:range_slice"));
        }

        [Test]
        public async Task Sp0010_ReadOnlySpanSliceStartGreaterThanLength_ReportsArgumentOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values)
    {
        return values.Slice(values.Length + 1);
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArgumentOutOfRangeException=definite_range_out_of_range:span_slice"));
        }

        [Test]
        public async Task Sp0010_SpanSliceNegativeLength_ReportsArgumentOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public class TestClass
{
    public Span<int> TestMethod(Span<int> values)
    {
        return values.Slice(0, -1);
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArgumentOutOfRangeException=definite_range_out_of_range:span_slice"));
        }

        [Test]
        public async Task Sp0010_ReadOnlySpanSliceStartPlusLengthPastEnd_ReportsArgumentOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArgumentOutOfRangeException=definite_range_out_of_range:span_slice"));
        }

        [Test]
        public async Task Sp0010_AssignedReadOnlySpanSliceIndexOutOfRange_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public class TestClass
{
    public int TestMethod(ReadOnlySpan<int> values)
    {
        var tail = values.Slice(values.Length);
        return tail[0];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_MemorySliceAliasLengthOutOfRange_ReportsArgumentOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
        }

        [Test]
        public async Task Sp0010_ReadOnlySpanSliceGuardedInRange_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_ReadOnlySpanSliceCaught_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_ArrayRangeGuardedStartAfterEnd_ReportsArgumentOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
        }

        [Test]
        public async Task Sp0010_ArrayRangeGuardedInRange_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_SystemRangeVariableStartAfterEnd_ReportsArgumentOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
        }

        [Test]
        public async Task Sp0010_SystemRangeVariableReassignedFromUnknown_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_SystemRangeFactoryStartAfterEnd_ReportsArgumentOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public class TestClass
{
    public int[] TestMethod(int[] values)
    {
        return values[new Range(Index.FromStart(2), Index.FromStart(1))];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
        }

        [Test]
        public async Task Sp0010_SystemRangeFactoriesGuardedInRange_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_SystemRangeFactoryNegativeEndpoint_DoesNotReportRangeOutOfRangeException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(
                diagnostics
                    .Where(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId)
                    .Any(d =>
                        d.Properties.TryGetValue(SharpProofDiagnostics.ExceptionCategoriesProperty, out var exceptionCategories) &&
                        exceptionCategories != null &&
                        exceptionCategories.Contains("definite_range_out_of_range")),
                Is.False);
        }

        [Test]
        public async Task Sp0010_InRangeArrayIndexGuard_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_ContradictoryShortCircuitIndexOutOfRange_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_UnsignedCastArrayIndexGuard_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_UnsignedCastArrayIndexFalseBranch_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_UnsignedCastArrayUpperBoundGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_WhileConditionUpperBoundIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_ForConditionUpperBoundIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_ForLoopMonotonicIndexBounds_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_ForLoopReverseIndexBounds_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_WhileLoopMonotonicIndexBounds_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_WhileLoopReverseIndexBounds_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_LoopConditionIndexReassignedBeforeUse_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_WhileFalseBodyIndexOutOfRange_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_ArrayLengthNegativeGuardedIndex_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_SwitchExpressionArrayLengthNegativeArm_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_OutOfRangeGuardReassignedBeforeUse_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_BranchFactIndexOutOfRangeCaught_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_LocalArrayCreationConstantUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        return values[4];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_DirectArrayCreationUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        return (new int[4])[4];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_LocalArrayCreationSymbolicUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var length = 4;
        var values = new int[length];
        return values[length];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_ArrayEmptyIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public class TestClass
{
    public int TestMethod()
    {
        var values = Array.Empty<int>();
        return values[0];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_ArrayCollectionExpressionIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        int[] values = [1, 2, 3];
        return values[3];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_ArrayAliasUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        var alias = values;
        return alias[4];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_ObjectErasedArrayCastAliasUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        object boxed = values;
        var alias = (int[])boxed;
        return alias[4];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_NegativeStringIndexGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_StringUpperBoundIndexGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_UnsignedCastStringIndexGuard_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_UnsignedCastStringUpperBoundGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_StringLiteralUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public char TestMethod()
    {
        var text = ""abc"";
        return text[3];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_DirectStringLiteralUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public char TestMethod()
    {
        return ""abc""[3];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_StringEmptyIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public char TestMethod()
    {
        var text = string.Empty;
        return text[0];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_DirectStringLiteralInRangeIndex_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public char TestMethod()
    {
        return ""abc""[2];
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_StringAliasUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public char TestMethod(string input)
    {
        var text = input;
        var alias = text;
        return alias[input.Length];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_StringLiteralInRangeIndex_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public char TestMethod()
    {
        var text = ""abc"";
        return text[2];
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_SourceNullOrEmptyPredicateNonNullIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(SourcePredicateSource + @"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_SourceNullOrEmptyPredicateFalseBranchIndex_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(SourcePredicateSource + @"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_SourceHasTextPredicateContradictoryIndex_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(SourcePredicateSource + @"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_ArrayCollectionExpressionSpreadIndex_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        int[] values = [.. input];
        return values[0];
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_ArrayCollectionExpressionSpreadFixedElementsPruneZeroLengthBranch_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_IndexAssignedFromArrayLength_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        var index = values.Length;
        return values[index];
    }
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_LocalArrayCreationInRangeIndex_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        return values[3];
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_ArrayLengthFactRemovedAfterReassignment_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        var values = new int[1];
        values = input;
        return values[1];
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_ConditionalArrayReassignmentInvalidatesLength_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_SelfReferentialIndexAssignment_DoesNotReportFromUnsatisfiableFacts()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        index = index + 1;
        return values[index];
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_SwitchStatementConstantZeroDivisor_ReportsDivideByZeroException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Sp0010_SwitchStatementNonZeroCase_DoesNotReportDivideByZeroException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_SwitchStatementReassignmentInvalidatesCaseFact_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_SwitchStatementNullCase_ReportsNullReferenceException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Sp0010_SwitchStatementRelationalPatternIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_SwitchExpressionRelationalPatternIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_SwitchExpressionWhenGuardIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Sp0010_SwitchExpressionWhenGuardInRange_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_ForeachNewEmptyArrayConstantDivide_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_NullGuardedForeachBodyConstantDivide_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_SingleElementForeachZeroDivisor_ReportsDivideByZeroException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Sp0010_SingleElementForeachNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_FiniteForeachNonZeroContradictoryDivide_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_CoalesceAssignmentNonNullContradictoryNullDereference_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_PriorAssignedFiniteForeachContradictoryDivide_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_CatchFilterNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_CatchFilterZeroDivisor_ReportsDivideByZeroException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Sp0010_CatchFilterNullReceiver_ReportsNullReferenceException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
        }

        [Test]
        public async Task Sp0010_CatchFilterUpperBoundIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_CatchFilterFactReassignedBeforeUse_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_ContinueGuardZeroDivisor_ReportsDivideByZeroException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Sp0010_ContinueGuardUpperBoundIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0010_BreakGuardNullReceiver_ReportsNullReferenceException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
        }

        [Test]
        public async Task Sp0010_PathSensitiveFinallyThrow_ShadowsGuardedDirectThrow()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ApplicationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Sp0010_PathSensitiveFinallyThrow_ShadowsGuardedDivideByZero()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ApplicationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Sp0010_PathSensitiveFinallyNestedBlockThrow_ShadowsGuardedDivideByZero()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ApplicationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Sp0010_PathSensitiveFinallyNestedAliasThrow_ShadowsGuardedDivideByZero()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ApplicationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Sp0010_FinallyUnknownThrowCondition_DoesNotShadowTryThrow()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Does.Contain("System.ApplicationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Does.Contain("System.InvalidOperationException"));
        }

        [Test]
        public async Task Sp0011_PathSensitiveFinallyThrow_ShadowsCheckedDirectThrowSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ApplicationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Sp0010_CatchFilterTrueFromThrowSiteBranch_SuppressesException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_CatchFilterBooleanAliasFromThrowSiteBranch_SuppressesException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_CatchFilterAliasPrunesContradictoryIndexUse_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_CatchFilterUnknownAtThrowSite_DoesNotSuppressException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Sp0010_NestedCatchFilterTrueFromOuterCatchFilter_SuppressesException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_NestedCatchFilterFactReassignedBeforeThrow_RemainsConservativeReports()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
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
}");

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Sp0010_FilteredCatchContradictoryGuardedThrow_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
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
}");

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0011_CatchFilterTrueFromThrowSiteBranch_SuppressesExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Sp0011_NestedCatchFilterTrueFromOuterCatchFilter_SuppressesExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Sp0011_FilteredCatchContradictoryGuardedThrow_DoesNotReportExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Sp0011_CatchFilterUnknownAtThrowSite_ReportsExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Sp0011_NullableValueFalseHasValueBranch_ReportsExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_nullable_value_without_value"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.InvalidOperationException=definite_nullable_value_without_value:nullable_value"));
        }

        [Test]
        public async Task Sp0011_ConditionalAccessNullReceiverNullableValue_ReportsExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_nullable_value_without_value"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.InvalidOperationException=definite_nullable_value_without_value:nullable_value"));
        }

        [Test]
        public async Task Sp0011_NullableCoalesceMissingFallback_ReportsExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_nullable_value_without_value"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.InvalidOperationException=definite_nullable_value_without_value:nullable_value"));
        }

        [Test]
        public async Task Sp0011_ConditionalNullableMissingBranch_ReportsExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_nullable_value_without_value"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.InvalidOperationException=definite_nullable_value_without_value:nullable_value"));
        }

        [Test]
        public async Task Sp0011_NullableValueTrueHasValueBranch_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Sp0011_UnboxNullCast_ReportsExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
public class TestClass
{
    public int TestMethod()
    {
        object value = null;
        return (int)value;
    }
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_unbox_null"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.NullReferenceException=definite_unbox_null:cast"));
        }

        [Test]
        public async Task Sp0011_InvalidReferenceCast_ReportsExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
public class TestClass
{
    public int TestMethod()
    {
        object value = 42;
        return ((string)value).Length;
    }
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidCastException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_invalid_cast"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.InvalidCastException=definite_invalid_cast:cast"));
        }

        [Test]
        public async Task Sp0011_SystemIndexVariableFromEndZeroGuard_ReportsExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0011_SystemIndexVariableFromEndGuardedInRange_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Sp0011_SystemIndexFactoryFromEndZeroGuard_ReportsExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0011_ReadOnlySpanUpperBoundIndex_ReportsExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0011_ReadOnlySpanGuardedInRange_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Sp0011_MultidimensionalArrayRowUpperBoundGuard_ReportsExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Sp0011_MultidimensionalArrayGuardedInRange_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Sp0011_DirectReadOnlySpanRangeStartAfterEnd_ReportsExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values)
    {
        return values[2..1];
    }
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArgumentOutOfRangeException=definite_range_out_of_range:range_slice"));
        }

        [Test]
        public async Task Sp0011_DirectReadOnlySpanRangeStartAfterEndCaught_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Sp0011_DirectArrayRangeStartAfterEnd_ReportsExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
public class TestClass
{
    public int[] TestMethod(int[] values)
    {
        return values[2..1];
    }
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArgumentOutOfRangeException=definite_range_out_of_range:range_slice"));
        }

        [Test]
        public async Task Sp0011_SystemRangeFactoryStartAfterEnd_ReportsExceptionSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
using System;

public class TestClass
{
    public int[] TestMethod(int[] values)
    {
        return values[new Range(Index.FromStart(2), Index.FromStart(1))];
    }
}",
                reportExceptions: false,
                checkedExceptions: true);

            var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArgumentOutOfRangeException=definite_range_out_of_range:range_slice"));
        }

        [Test]
        public async Task Sp0011_DirectArrayRangeStartAfterEndCaught_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
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
}",
                reportExceptions: false,
                checkedExceptions: true);

            Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        private static async Task<Diagnostic> SingleExceptionDiagnosticAsync(string source)
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            return diagnostics.Single(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId);
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            string source,
            bool reportExceptions = true,
            bool checkedExceptions = false)
        {
            return await AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                globalOptions: ImmutableDictionary<string, string>.Empty
                    .Add("sharpproof_report_exceptions", reportExceptions ? "true" : "false")
                    .Add("sharpproof_checked_exceptions", checkedExceptions ? "true" : "false"),
                allowUnsafe: false,
                frameworkReferences: AnalyzerTestHost.GetMinimalFrameworkReferences(),
                compilationName: "ExceptionFlowPathFactStressTests");
        }
    }
}
