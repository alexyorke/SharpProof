using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    [TestFixture]
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
        public async Task Ps0010_AndConditionZeroDivisor_ReportsDivideByZeroException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Ps0010_AndConditionNullReceiver_ReportsNullReferenceException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Ps0010_GenericClassConstrainedNullReceiver_ReportsNullReferenceException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
        }

        [Test]
        public async Task Ps0010_OrFalseBranchZeroDivisor_ReportsDivideByZeroException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Ps0010_IsNotNullElseBranch_ReportsNullReferenceException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Ps0010_IsNotZeroElseBranch_ReportsDivideByZeroException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Ps0010_AndConditionZeroDivisor_ReassignedBeforeUse_DoesNotReport()
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
                diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId),
                Is.False,
                string.Join(
                    "; ",
                    diagnostics
                        .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                        .Select(d => d.Properties[PurelySharpDiagnostics.ExceptionTypesProperty])));
        }

        [Test]
        public async Task Ps0010_OrTrueBranchZeroDivisor_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_OrTrueBranchNullReceiver_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NegatedNotEqualZero_ReportsDivideByZeroException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Ps0010_NegatedEqualsZero_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NegatedIsNotNull_ReportsNullReferenceException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Ps0010_AndConditionNonZeroDivisor_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ContradictoryShortCircuitDivideByZero_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ContradictoryShortCircuitNullDereference_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_GuardFalsePathReassignedBeforeUse_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_BranchFactDivideByZeroCaught_IsSuppressed()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_WhileConditionZeroDivisor_ReportsDivideByZeroException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Ps0010_WhileConditionNullReceiver_ReportsNullReferenceException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Ps0010_NegativeArrayIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        return values[-1];
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_NegativeIndexGuard_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_UpperBoundIndexGuard_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_MultidimensionalArrayRowUpperBoundGuard_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_MultidimensionalArrayGuardedInRange_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_DirectMultidimensionalArrayCreationOutOfRange_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        return (new int[1, 2])[0, 2];
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_ReadOnlySpanUpperBoundIndexGuard_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_ReadOnlySpanGuardedInRange_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SpanUpperBoundIndexGuard_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_SystemIndexVariableFromEndZeroGuard_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_SystemIndexVariableFromEndGuardedInRange_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_DirectArrayRangeStartAfterEnd_ReportsArgumentOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int[] TestMethod(int[] values)
    {
        return values[2..1];
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArgumentOutOfRangeException=definite_range_out_of_range:range_slice"));
        }

        [Test]
        public async Task Ps0010_DirectReadOnlySpanRangeStartAfterEnd_ReportsArgumentOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArgumentOutOfRangeException=definite_range_out_of_range:range_slice"));
        }

        [Test]
        public async Task Ps0010_ArrayRangeGuardedStartAfterEnd_ReportsArgumentOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
        }

        [Test]
        public async Task Ps0010_ArrayRangeGuardedInRange_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SystemRangeVariableStartAfterEnd_ReportsArgumentOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
        }

        [Test]
        public async Task Ps0010_SystemRangeVariableReassignedFromUnknown_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_InRangeArrayIndexGuard_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ContradictoryShortCircuitIndexOutOfRange_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_UnsignedCastArrayIndexGuard_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_UnsignedCastArrayIndexFalseBranch_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_UnsignedCastArrayUpperBoundGuard_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_WhileConditionUpperBoundIndex_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_ForConditionUpperBoundIndex_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_ForLoopMonotonicIndexBounds_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ForLoopReverseIndexBounds_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_LoopConditionIndexReassignedBeforeUse_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_WhileFalseBodyIndexOutOfRange_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ArrayLengthNegativeGuardedIndex_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchExpressionArrayLengthNegativeArm_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_OutOfRangeGuardReassignedBeforeUse_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_BranchFactIndexOutOfRangeCaught_IsSuppressed()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_LocalArrayCreationConstantUpperBound_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_DirectArrayCreationUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        return (new int[4])[4];
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_LocalArrayCreationSymbolicUpperBound_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_ArrayEmptyIndex_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_ArrayCollectionExpressionIndex_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_ArrayAliasUpperBound_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_NegativeStringIndexGuard_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_StringUpperBoundIndexGuard_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_UnsignedCastStringIndexGuard_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_UnsignedCastStringUpperBoundGuard_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_StringLiteralUpperBound_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_DirectStringLiteralUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public char TestMethod()
    {
        return ""abc""[3];
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_StringEmptyIndex_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_DirectStringLiteralInRangeIndex_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public char TestMethod()
    {
        return ""abc""[2];
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_StringAliasUpperBound_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_StringLiteralInRangeIndex_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SourceNullOrEmptyPredicateNonNullIndex_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_SourceNullOrEmptyPredicateFalseBranchIndex_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SourceHasTextPredicateContradictoryIndex_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ArrayCollectionExpressionSpreadIndex_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_IndexAssignedFromArrayLength_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_LocalArrayCreationInRangeIndex_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ArrayLengthFactRemovedAfterReassignment_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ConditionalArrayReassignmentInvalidatesLength_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SelfReferentialIndexAssignment_DoesNotReportFromUnsatisfiableFacts()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchStatementConstantZeroDivisor_ReportsDivideByZeroException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Ps0010_SwitchStatementNonZeroCase_DoesNotReportDivideByZeroException()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchStatementReassignmentInvalidatesCaseFact_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchStatementNullCase_ReportsNullReferenceException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Ps0010_SwitchStatementRelationalPatternIndex_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_SwitchExpressionRelationalPatternIndex_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_SwitchExpressionWhenGuardIndex_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_SwitchExpressionWhenGuardInRange_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ForeachNewEmptyArrayConstantDivide_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NullGuardedForeachBodyConstantDivide_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SingleElementForeachZeroDivisor_ReportsDivideByZeroException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_SingleElementForeachNonZeroDivisor_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_FiniteForeachNonZeroContradictoryDivide_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CoalesceAssignmentNonNullContradictoryNullDereference_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_PriorAssignedFiniteForeachContradictoryDivide_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CatchFilterNonZeroDivisor_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CatchFilterZeroDivisor_ReportsDivideByZeroException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_CatchFilterNullReceiver_ReportsNullReferenceException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
        }

        [Test]
        public async Task Ps0010_CatchFilterUpperBoundIndex_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_CatchFilterFactReassignedBeforeUse_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ContinueGuardZeroDivisor_ReportsDivideByZeroException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_ContinueGuardUpperBoundIndex_ReportsIndexOutOfRangeException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_BreakGuardNullReceiver_ReportsNullReferenceException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
        }

        [Test]
        public async Task Ps0010_PathSensitiveFinallyThrow_ShadowsGuardedDirectThrow()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ApplicationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_PathSensitiveFinallyThrow_ShadowsGuardedDivideByZero()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ApplicationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_PathSensitiveFinallyNestedBlockThrow_ShadowsGuardedDivideByZero()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ApplicationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_PathSensitiveFinallyNestedAliasThrow_ShadowsGuardedDivideByZero()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ApplicationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_FinallyUnknownThrowCondition_DoesNotShadowTryThrow()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Does.Contain("System.ApplicationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Does.Contain("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0011_PathSensitiveFinallyThrow_ShadowsCheckedDirectThrowSite()
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

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ApplicationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_CatchFilterTrueFromThrowSiteBranch_SuppressesException()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CatchFilterBooleanAliasFromThrowSiteBranch_SuppressesException()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CatchFilterAliasPrunesContradictoryIndexUse_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CatchFilterUnknownAtThrowSite_DoesNotSuppressException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_NestedCatchFilterTrueFromOuterCatchFilter_SuppressesException()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NestedCatchFilterFactReassignedBeforeThrow_RemainsConservativeReports()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_FilteredCatchContradictoryGuardedThrow_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0011_CatchFilterTrueFromThrowSiteBranch_SuppressesExceptionSite()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0011_NestedCatchFilterTrueFromOuterCatchFilter_SuppressesExceptionSite()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0011_FilteredCatchContradictoryGuardedThrow_DoesNotReportExceptionSite()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0011_CatchFilterUnknownAtThrowSite_ReportsExceptionSite()
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

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0011_SystemIndexVariableFromEndZeroGuard_ReportsExceptionSite()
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

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0011_SystemIndexVariableFromEndGuardedInRange_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0011_ReadOnlySpanUpperBoundIndex_ReportsExceptionSite()
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

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0011_ReadOnlySpanGuardedInRange_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0011_MultidimensionalArrayRowUpperBoundGuard_ReportsExceptionSite()
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

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0011_MultidimensionalArrayGuardedInRange_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0011_DirectReadOnlySpanRangeStartAfterEnd_ReportsExceptionSite()
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

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArgumentOutOfRangeException=definite_range_out_of_range:range_slice"));
        }

        [Test]
        public async Task Ps0011_DirectReadOnlySpanRangeStartAfterEndCaught_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0011_DirectArrayRangeStartAfterEnd_ReportsExceptionSite()
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

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArgumentOutOfRangeException=definite_range_out_of_range:range_slice"));
        }

        [Test]
        public async Task Ps0011_DirectArrayRangeStartAfterEndCaught_DoesNotReport()
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

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        private static async Task<Diagnostic> SingleExceptionDiagnosticAsync(string source)
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            return diagnostics.Single(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId);
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            string source,
            bool reportExceptions = true,
            bool checkedExceptions = false)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var references = GetTrustedPlatformReferences()
                .Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location));

            var compilation = CSharpCompilation.Create(
                "ExceptionFlowPathFactStressTests",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzerOptions = new AnalyzerOptions(
                ImmutableArray<AdditionalText>.Empty,
                new TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string>.Empty
                    .Add("purelysharp_report_exceptions", reportExceptions ? "true" : "false")
                    .Add("purelysharp_checked_exceptions", checkedExceptions ? "true" : "false")));

            var compilationWithAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new PurelySharpAnalyzer()),
                new CompilationWithAnalyzersOptions(
                    analyzerOptions,
                    onAnalyzerException: null,
                    concurrentAnalysis: false,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false));

            return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        }

        private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
        {
            var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                return ImmutableArray.Create<MetadataReference>(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .ToImmutableArray();
        }

        private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
        {
            private readonly AnalyzerConfigOptions _globalOptions;
            private readonly AnalyzerConfigOptions _emptyOptions = new TestAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);

            public TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> globalOptions)
            {
                _globalOptions = new TestAnalyzerConfigOptions(globalOptions);
            }

            public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _emptyOptions;

            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _emptyOptions;
        }

        private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
        {
            private readonly ImmutableDictionary<string, string> _values;

            public TestAnalyzerConfigOptions(ImmutableDictionary<string, string> values)
            {
                _values = values;
            }

            public override bool TryGetValue(string key, out string value)
            {
                if (_values.TryGetValue(key, out var found))
                {
                    value = found;
                    return true;
                }

                value = string.Empty;
                return false;
            }
        }
    }
}
