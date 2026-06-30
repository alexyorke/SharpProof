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

        private static Task<ImmutableArray<Diagnostic>> GetExceptionDiagnosticsAsync(string source)
        {
            return AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));
        }

        private static Diagnostic SingleExceptionDiagnostic(ImmutableArray<Diagnostic> diagnostics)
        {
            return AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);
        }
    }
}
