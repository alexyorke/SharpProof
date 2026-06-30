using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class ExceptionReachabilitySmtTests
    {
        [Test]
        public async Task Ps0010_NonNullConditionalAccessCoalesceDivideByZeroFallback_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string text)
    {
        var zero = 0;
        if (text != null)
        {
            return text?.Length ?? (10 / zero);
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_UnknownConditionalAccessCoalesceDivideByZeroFallback_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string text)
    {
        var zero = 0;
        return text?.Length ?? (10 / zero);
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_NonNullStringCoalesceDivideByZeroFallback_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public string TestMethod()
    {
        var zero = 0;
        return ""value"" ?? (10 / zero).ToString();
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_UnknownStringCoalesceDivideByZeroFallback_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public string TestMethod(string text)
    {
        var zero = 0;
        return text ?? (10 / zero).ToString();
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_NonNullConditionalAccessCoalesceOutOfRangeFallback_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string text)
    {
        var values = new int[1];
        if (text != null)
        {
            return text?.Length ?? values[1];
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NonNullObjectConditionalAccessCoalesceOutOfRangeFallback_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public sealed class Box
{
    public int Value { get; set; }
}

public class TestClass
{
    public int TestMethod()
    {
        var values = new int[1];
        return new Box()?.Value ?? values[1];
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NonNullStringCoalesceRangeFallback_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public string TestMethod()
    {
        var values = new int[1];
        return ""value"" ?? values[0..2].Length.ToString();
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_UnknownConditionalAccessCoalesceOutOfRangeFallback_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string text)
    {
        var values = new int[1];
        return text?.Length ?? values[1];
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_NonNullConditionalAccessNullableValueCoalesceDivideByZeroFallback_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public sealed class Box
{
    public int? Maybe { get; set; }
}

public class TestClass
{
    public int TestMethod(Box box)
    {
        var zero = 0;
        if (box != null)
        {
            return box?.Maybe ?? (10 / zero);
        }

        return 0;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_NonNullConditionalAccessNullableValueCoalesceOutOfRangeFallback_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public sealed class Box
{
    public int? Maybe { get; set; }
}

public class TestClass
{
    public int TestMethod(Box box)
    {
        var values = new int[1];
        if (box != null)
        {
            return box?.Maybe ?? values[1];
        }

        return 0;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_CoalesceAssignmentThrowProvesNonNullContinuation_DoesNotReportNullDereference()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod(string value)
    {
        value ??= throw new InvalidOperationException();
        if (value == null)
        {
            return value.Length;
        }

        return value.Length;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_SelfCoalesceAssignmentThrowProvesNonNullContinuation_DoesNotReportNullDereference()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod(string value)
    {
        value = value ?? throw new InvalidOperationException();
        if (value == null)
        {
            return value.Length;
        }

        return value.Length;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_SelfConditionalThrowNullGuardProvesNonNullContinuation_DoesNotReportNullDereference()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod(string value)
    {
        value = value is not null ? value : throw new InvalidOperationException();
        if (value == null)
        {
            return value.Length;
        }

        return value.Length;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_SelfConditionalThrowDivideGuardPrunesImpossibleDivideByZeroBranch()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod(int divisor)
    {
        divisor = divisor != 0 ? divisor : throw new InvalidOperationException();
        if (divisor == 0)
        {
            return 10 / divisor;
        }

        return 10 / divisor;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_SelfConditionalThrowIndexGuardPrunesImpossibleIndexOutOfRangeBranch()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        index = index >= 0 && index < values.Length ? index : throw new InvalidOperationException();
        if (index >= values.Length)
        {
            return values[index];
        }

        return values[index];
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_SelfConditionalThrowPatternGuardPrunesImpossibleLengthBranch()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod(string value)
    {
        value = value is { Length: > 0 } ? value : throw new InvalidOperationException();
        if (value.Length == 0)
        {
            return 1 / 0;
        }

        return value.Length;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_EarlyReturnGuardContradictsDivideByZeroBranch_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        var zero = 0;
        if (divisor == 0)
        {
            return 0;
        }

        if (divisor == 0)
        {
            return 10 / zero;
        }

        return 1;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_EarlyReturnGuardReferencedValueMutatedBeforeDivideByZero_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
            return 0;
        }

        divisor = 0;
        if (divisor == 0)
        {
            return 10 / divisor;
        }

        return 1;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_InlineAssignmentZeroDivisorBranch_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int input)
    {
        var divisor = input;
        if ((divisor = 0) == 0)
        {
            return 10 / divisor;
        }

        return 1;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_InlineAssignmentNonZeroDivisorBranch_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int input)
    {
        var divisor = input;
        if ((divisor = 1) > 0)
        {
            return 10 / divisor;
        }

        return 1;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_InlineAssignmentShortCircuitTrueBranch_RemainsConservative()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var divisor = 1;
        if (flag || (divisor = 0) == 0)
        {
            return 10 / divisor;
        }

        return 1;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CheckedIntAdditionOverflow_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
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
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.OverflowException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public async Task Ps0010_CheckedIntSubtractionOverflow_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MinValue)
        {
            return checked(value - 1);
        }

        return 0;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.OverflowException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public async Task Ps0010_CheckedLongMultiplicationOverflow_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public long TestMethod(long value)
    {
        if (value == long.MaxValue)
        {
            return checked(value * 2L);
        }

        return 0L;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.OverflowException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public async Task Ps0010_CheckedUIntAdditionOverflow_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public uint TestMethod(uint value)
    {
        if (value == uint.MaxValue)
        {
            return checked(value + 1u);
        }

        return 0u;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.OverflowException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_checked_integral_overflow"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.OverflowException=definite_checked_integral_overflow:checked_operator"));
        }

        [Test]
        public async Task Ps0010_CheckedUIntSubtractionOverflow_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public uint TestMethod(uint value)
    {
        if (value == uint.MinValue)
        {
            return checked(value - 1u);
        }

        return 0u;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.OverflowException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public async Task Ps0010_CheckedUIntMultiplicationOverflow_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public uint TestMethod(uint value)
    {
        if (value == uint.MaxValue)
        {
            return checked(value * 2u);
        }

        return 0u;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.OverflowException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public async Task Ps0010_CheckedIntUnaryMinusOverflow_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MinValue)
        {
            return checked(-value);
        }

        return 0;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.OverflowException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public async Task Ps0010_CheckedIntPreIncrementOverflow_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MaxValue)
        {
            return checked(++value);
        }

        return 0;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.OverflowException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_checked_integral_overflow"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.OverflowException=definite_checked_integral_overflow:checked_operator"));
        }

        [Test]
        public async Task Ps0010_CheckedLongPostDecrementOverflow_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
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
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.OverflowException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public async Task Ps0010_CheckedIntPostIncrementGuardedUnreachableOverflow_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
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
            return checked(value++);
        }

        return 1;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_UncheckedIntPreIncrementOverflow_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MaxValue)
        {
            return unchecked(++value);
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CheckedIntPreIncrementOverflowCaught_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        try
        {
            if (value == int.MaxValue)
            {
                return checked(++value);
            }

            return value;
        }
        catch (System.OverflowException)
        {
            return 0;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CheckedExplicitNumericConversionOverflow_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(long value)
    {
        if (value == 2147483648L)
        {
            return checked((int)value);
        }

        return 0;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.OverflowException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_checked_integral_overflow"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.OverflowException=definite_checked_integral_overflow:checked_conversion"));
        }

        [Test]
        public async Task Ps0010_CheckedExplicitNumericConversionGuardedUnreachableOverflow_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(long value)
    {
        if (value == 2147483648L)
        {
            return 0;
        }

        if (value == 2147483648L)
        {
            return checked((int)value);
        }

        return 1;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_UncheckedExplicitNumericConversionOverflow_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(long value)
    {
        if (value == 2147483648L)
        {
            return unchecked((int)value);
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CheckedExplicitNumericConversionOverflowCaught_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(long value)
    {
        try
        {
            if (value == 2147483648L)
            {
                return checked((int)value);
            }

            return value == 0L ? 0 : 1;
        }
        catch (System.OverflowException)
        {
            return 0;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0011_CheckedExplicitNumericConversionOverflow_ReportsSite()
        {
            var diagnostics = await GetCheckedExceptionSiteDiagnosticsAsync(@"
public class TestClass
{
    public byte TestMethod(int value)
    {
        if (value > byte.MaxValue)
        {
            return checked((byte)value);
        }

        return 0;
    }
}");

            var diagnostic = SingleUncaughtExceptionSiteDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.OverflowException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_checked_integral_overflow"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.OverflowException=definite_checked_integral_overflow:checked_conversion"));
        }

        [Test]
        public async Task Ps0011_CheckedExplicitNumericConversionGuardedUnreachableOverflow_DoesNotReport()
        {
            var diagnostics = await GetCheckedExceptionSiteDiagnosticsAsync(@"
public class TestClass
{
    public byte TestMethod(int value)
    {
        if (value > byte.MaxValue)
        {
            return 0;
        }

        if (value > byte.MaxValue)
        {
            return checked((byte)value);
        }

        return 1;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0011_CheckedExplicitNumericConversionOverflowCaught_DoesNotReport()
        {
            var diagnostics = await GetCheckedExceptionSiteDiagnosticsAsync(@"
public class TestClass
{
    public byte TestMethod(int value)
    {
        try
        {
            if (value > byte.MaxValue)
            {
                return checked((byte)value);
            }

            return 1;
        }
        catch (System.OverflowException)
        {
            return 0;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0010_CheckedIntAdditionGuardedUnreachableOverflow_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
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
            return checked(value + 1);
        }

        return 1;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CheckedUIntAdditionGuardedUnreachableOverflow_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public uint TestMethod(uint value)
    {
        if (value == uint.MaxValue)
        {
            return 0u;
        }

        if (value == uint.MaxValue)
        {
            return checked(value + 1u);
        }

        return 1u;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_UncheckedIntAdditionOverflow_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MaxValue)
        {
            return unchecked(value + 1);
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0011_CheckedUIntAdditionOverflow_ReportsSite()
        {
            var diagnostics = await GetCheckedExceptionSiteDiagnosticsAsync(@"
public class TestClass
{
    public uint TestMethod(uint value)
    {
        if (value == uint.MaxValue)
        {
            return checked(value + 1u);
        }

        return 0u;
    }
}");

            var diagnostic = SingleUncaughtExceptionSiteDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.OverflowException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_checked_integral_overflow"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.OverflowException=definite_checked_integral_overflow:checked_operator"));
        }

        [Test]
        public async Task Ps0010_CheckedIntAdditionOverflowCaught_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        try
        {
            if (value == int.MaxValue)
            {
                return checked(value + 1);
            }

            return value;
        }
        catch (System.OverflowException)
        {
            return 0;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_GuardedNegativeArrayLength_ReportsOverflowException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
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
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.OverflowException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_negative_array_length"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.OverflowException=definite_negative_array_length:array_length"));
        }

        [Test]
        public async Task Ps0011_MultidimensionalNegativeArrayLength_ReportsOverflowExceptionAtSite()
        {
            var diagnostics = await GetCheckedExceptionSiteDiagnosticsAsync(@"
public class TestClass
{
    public int[,] TestMethod()
    {
        var length = -1;
        return new int[1, length];
    }
}");

            var diagnostic = SingleUncaughtExceptionSiteDiagnostic(diagnostics);

            Assert.That(diagnostic.GetMessage(), Does.Contain("new int[1, length]"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.OverflowException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_negative_array_length"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.OverflowException=definite_negative_array_length:array_length"));
        }

        [Test]
        public async Task Ps0010_NegativeArrayLengthCaught_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int[] TestMethod()
    {
        try
        {
            var length = -1;
            return new int[length];
        }
        catch (System.OverflowException)
        {
            return new int[0];
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_UnknownArrayLength_DoesNotReportOverflowException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int[] TestMethod(int length)
    {
        return new int[length];
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId &&
                    diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty] == "definite_negative_array_length"),
                Is.False);
        }

        [Test]
        public async Task Ps0010_NegativeArrayLengthAfterSuccessfulCreation_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int[] TestMethod(int length)
    {
        var values = new int[length];
        if (length < 0)
        {
            return new int[length];
        }

        return values;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ConditionalExpressionUnreachableNullDerefArm_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string text)
    {
        if (text != null)
        {
            return text == null ? ((string)null).Length : 0;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ConditionalExpressionReachableNullDerefArm_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string text)
    {
        return text == null ? ((string)null).Length : 0;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
        }

        [Test]
        public async Task Ps0010_NullGuardedLockReceiver_ReportsArgumentNullException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public void TestMethod(object gate)
    {
        if (gate is null)
        {
            lock (gate)
            {
            }
        }
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentNullException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_lock_null"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArgumentNullException=definite_lock_null:lock_receiver"));
        }

        [Test]
        public async Task Ps0010_NonNullGuardedLockReceiver_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public void TestMethod(object gate)
    {
        if (gate is not null)
        {
            lock (gate)
            {
            }
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NullGuardedLockReceiverCaught_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(object gate)
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
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_LockReceiverReassignedToNull_ReportsArgumentNullException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public void TestMethod(object gate)
    {
        gate = null;
        lock (gate)
        {
        }
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentNullException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_lock_null"));
        }

        [Test]
        public async Task Ps0010_DynamicMemberNullBinding_ReportsRuntimeBinderException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value.Missing;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_dynamic_member_null_binding"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException=definite_dynamic_member_null_binding:dynamic_member"));
        }

        [Test]
        public async Task Ps0010_CastedDynamicNullBinding_ReportsRuntimeBinderException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public object TestMethod()
    {
        return ((dynamic)null).Missing;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_dynamic_member_null_binding"));
        }

        [Test]
        public async Task Ps0011_DynamicInvocationNullBinding_ReportsRuntimeBinderExceptionAtSite()
        {
            var diagnostics = await GetCheckedExceptionSiteDiagnosticsAsync(@"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value.Missing();
    }
}");

            var diagnostic = SingleUncaughtExceptionSiteDiagnostic(diagnostics);

            Assert.That(diagnostic.GetMessage(), Does.Contain("value.Missing()"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_dynamic_invocation_null_binding"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException=definite_dynamic_invocation_null_binding:dynamic_invocation"));
        }

        [Test]
        public async Task Ps0011_DynamicDirectInvocationNullBinding_ReportsRuntimeBinderExceptionOnly()
        {
            var diagnostics = await GetCheckedExceptionSiteDiagnosticsAsync(@"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value();
    }
}");

            var diagnostic = SingleUncaughtExceptionSiteDiagnostic(diagnostics);

            Assert.That(diagnostic.GetMessage(), Does.Contain("value()"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_dynamic_invocation_null_binding"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException=definite_dynamic_invocation_null_binding:dynamic_invocation"));
        }

        [Test]
        public async Task Ps0010_DynamicIndexerNullBindingCaught_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public object TestMethod()
    {
        try
        {
            dynamic value = null;
            return value[0];
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return null;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_UnknownDynamicReceiver_DoesNotReportRuntimeBinderException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public object TestMethod(dynamic value)
    {
        return value.Missing;
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId &&
                    diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty] == "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"),
                Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchArmContradictedByOuterGuard_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        var zero = 0;
        if (value != 0)
        {
            return value switch
            {
                0 => value / zero,
                _ => 0
            };
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchArmReachableDivideByZero_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        var zero = 0;
        return value switch
        {
            0 => value / zero,
            _ => 0
        };
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_EarlyReturnGuardContradictsIndexOutOfRangeBranch_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int index)
    {
        var values = new int[1];
        if (index == 1)
        {
            return 0;
        }

        if (index == 1)
        {
            return values[index];
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_IndexOutOfRangeBranchReachable_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int index)
    {
        var values = new int[1];
        if (index == 1)
        {
            return values[index];
        }

        return 0;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_EarlyReturnGuardContradictsRangeOutOfRangeBranch_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int end)
    {
        var values = new int[1];
        if (end != 2)
        {
            return 0;
        }

        if (end != 2)
        {
            return values[0..2].Length;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_RangeOutOfRangeBranchReachable_Reports()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int end)
    {
        var values = new int[1];
        if (end == 2)
        {
            return values[0..end].Length;
        }

        return 0;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_range_out_of_range"));
        }

        [Test]
        public async Task Ps0010_UnboxNullCast_ReportsNullReferenceException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        object value = null;
        return (int)value;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_unbox_null"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.NullReferenceException=definite_unbox_null:cast"));
        }

        [Test]
        public async Task Ps0010_UnboxNullCastCaught_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod()
    {
        try
        {
            object value = null;
            return (int)value;
        }
        catch (NullReferenceException)
        {
            return 0;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_InvalidReferenceCast_ReportsInvalidCastException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        object value = 42;
        return ((string)value).Length;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidCastException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_invalid_cast"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.InvalidCastException=definite_invalid_cast:cast"));
        }

        [Test]
        public async Task Ps0010_InvalidUnboxCast_ReportsInvalidCastException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        object value = ""text"";
        return (int)value;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidCastException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_invalid_cast"));
        }

        [Test]
        public async Task Ps0010_UnknownObjectReferenceCast_DoesNotReportDefiniteInvalidCast()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(object value)
    {
        return ((string)value).Length;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NullableUnboxNull_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        object value = null;
        int? result = (int?)value;
        return result.GetValueOrDefault();
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_InvalidReferenceCastInUnreachableBranch_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int flag)
    {
        object value = 42;
        if (flag == 0)
        {
            return 0;
        }

        if (flag == 0)
        {
            return ((string)value).Length;
        }

        return 1;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_InvalidCastCaught_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod()
    {
        try
        {
            object value = 42;
            return ((string)value).Length;
        }
        catch (InvalidCastException)
        {
            return 0;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ArrayCovarianceStoreMismatch_ReportsArrayTypeMismatchException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public void TestMethod()
    {
        object[] values = new string[1];
        values[0] = 42;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArrayTypeMismatchException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_array_type_mismatch"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArrayTypeMismatchException=definite_array_type_mismatch:array_store"));
        }

        [Test]
        public async Task Ps0010_ArrayCovarianceStoreMismatchThroughAlias_ReportsArrayTypeMismatchException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public void TestMethod()
    {
        string[] strings = new string[1];
        object[] values = strings;
        values[0] = 42;
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArrayTypeMismatchException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_array_type_mismatch"));
        }

        [Test]
        public async Task Ps0010_ArrayCovarianceStoreMismatchBehindIndexGuard_ReportsArrayTypeMismatchException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public void TestMethod(int index)
    {
        object[] values = new string[1];
        if (index == 0)
        {
            values[index] = 42;
        }
    }
}");

            var diagnostic = SingleExceptionDiagnostic(diagnostics);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArrayTypeMismatchException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_array_type_mismatch"));
        }

        [Test]
        public async Task Ps0010_ArrayCovarianceCompatibleStore_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public void TestMethod()
    {
        object[] values = new string[1];
        values[0] = ""text"";
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ArrayCovarianceNullStore_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public void TestMethod()
    {
        object[] values = new string[1];
        values[0] = null;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ArrayCovarianceUnknownArray_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public void TestMethod(object[] values)
    {
        values[0] = 42;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ArrayCovarianceKnownObjectArray_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public void TestMethod()
    {
        object[] values = new object[1];
        values[0] = 42;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ArrayCovarianceUnknownStoreValue_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public void TestMethod(object value)
    {
        object[] values = new string[1];
        values[0] = value;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ArrayCovarianceStoreMismatchCaught_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod()
    {
        try
        {
            object[] values = new string[1];
            values[0] = 42;
            return 1;
        }
        catch (ArrayTypeMismatchException)
        {
            return 0;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        private static Task<ImmutableArray<Diagnostic>> GetExceptionDiagnosticsAsync(string source)
        {
            return AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));
        }

        private static Task<ImmutableArray<Diagnostic>> GetCheckedExceptionSiteDiagnosticsAsync(string source)
        {
            return AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_checked_exceptions", "true"));
        }

        private static Diagnostic SingleExceptionDiagnostic(ImmutableArray<Diagnostic> diagnostics)
        {
            return AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);
        }

        private static Diagnostic SingleUncaughtExceptionSiteDiagnostic(ImmutableArray<Diagnostic> diagnostics)
        {
            return AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId).ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);
        }
    }
}
