using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
[Category("SmtHeavy")]
public class SemanticOracleRuntimeHazardAnalyzerSmtTests : SemanticOracleSmtTestBase
{
    [Test]
    public async Task Sp0010_ContradictoryGuardedThrow_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(int x)
    {
        if (x > 0 && x < 0)
        {
            throw new InvalidOperationException();
        }
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_SatisfiableGuardedThrow_ReportsDirectThrow()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(int x)
    {
        if (x >= 0 && x <= 0)
        {
            throw new InvalidOperationException();
        }
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("direct_throw"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty],
            Is.EqualTo("System.InvalidOperationException=direct_throw:throw"));
    }

    [Test]
    public async Task Sp0010_GuardImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor == 0)
        {
            return value / divisor;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_DecimalGuardImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public decimal TestMethod(decimal value, decimal divisor)
    {
        if (divisor == 0m)
        {
            return value / divisor;
        }

        return 0m;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_DecimalNonZeroGuardSuppressesDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public decimal TestMethod(decimal value, decimal divisor)
    {
        if (divisor != 0m)
        {
            return value / divisor;
        }

        return 0m;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_DecimalPositiveGuardSuppressesDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public decimal TestMethod(decimal value, decimal divisor)
    {
        if (divisor > 0m)
        {
            return value / divisor;
        }

        return 0m;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_BigIntegerGuardImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System.Numerics;

public class TestClass
{
    public BigInteger TestMethod(BigInteger value, BigInteger divisor)
    {
        if (divisor == 0)
        {
            return value / divisor;
        }

        return BigInteger.Zero;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_BigIntegerNonZeroGuardSuppressesDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System.Numerics;

public class TestClass
{
    public BigInteger TestMethod(BigInteger value, BigInteger divisor)
    {
        if (divisor != 0)
        {
            return value / divisor;
        }

        return BigInteger.Zero;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_ExtendedPropertyPatternImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(ExtendedPropertyPatternSource + @"
public class TestClass
{
    public int TestMethod(ExtendedPatternBox box)
    {
        if (box is { Child.Value: 0 })
        {
            return 1 / box.Child.Value;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_ExtendedPropertyPatternContradictoryZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(ExtendedPropertyPatternSource + @"
public class TestClass
{
    public int TestMethod(ExtendedPatternBox box)
    {
        if (box is { Child.Value: > 0 } && box.Child.Value == 0)
        {
            return 1 / box.Child.Value;
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_BitwiseBooleanAndGuardImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor, bool ready)
    {
        if ((divisor == 0) & ready)
        {
            return value / divisor;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_WideningIntegralCastGuardImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if ((long)divisor == 0L)
        {
            return value / divisor;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_EnumGuardImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public enum Mode
{
    None = 0,
    Ready = 1
}

public class TestClass
{
    public int TestMethod(int value, Mode state)
    {
        if (state == Mode.None)
        {
            return value / (int)state;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_UlongGuardImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public ulong TestMethod(ulong value, ulong divisor)
    {
        if (divisor == 0UL)
        {
            return value / divisor;
        }

        return 0UL;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_UlongGuardImpliesZeroModulo_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public ulong TestMethod(ulong value, ulong divisor)
    {
        if (divisor == 0UL)
        {
            return value % divisor;
        }

        return 0UL;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_AffineGuardImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor + 1 == 1)
        {
            return value / divisor;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_RelationalPatternExactZero_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor is <= 0 and >= 0)
        {
            return value / divisor;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_IfElseElseExitImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
        }
        else
        {
            return 0;
        }

        return 10 / divisor;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_DefaultLiteralDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        int divisor = default;
        return 10 / divisor;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_BooleanPredicateAliasImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        var isZero = divisor == 0;
        if (isZero)
        {
            return 10 / divisor;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_SourceGuardPredicateImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
" + SourcePredicateSource + @"

public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (SourcePredicates.IsZeroWithGuard(divisor))
        {
            return 10 / divisor;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_SourceLocalAliasPredicateImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
" + SourcePredicateSource + @"

public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (SourcePredicates.IsZeroViaLocal(divisor))
        {
            return 10 / divisor;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_SourceLocalAssignmentPredicateImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
" + SourcePredicateSource + @"

public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (SourcePredicates.IsZeroViaAssignment(divisor))
        {
            return 10 / divisor;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_SourceSwitchStatementPredicateImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
" + SourcePredicateSource + @"

public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (SourcePredicates.IsZeroWithSwitch(divisor))
        {
            return 10 / divisor;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_SourceMultiGuardIndexPredicateInRange_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
" + SourcePredicateSource + @"

public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (SourcePredicates.IsValidIndex(values, index))
        {
            return values[index];
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_SourceBooleanPropertyImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
" + SourcePredicateSource + @"

public class TestClass
{
    public int TestMethod(SourcePredicateBox box)
    {
        if (box.IsZeroDivisor)
        {
            return 10 / box.Divisor;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_InstanceSourceBooleanMethodImpliesZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
" + SourcePredicateSource + @"

public class TestClass
{
    public int TestMethod(SourcePredicateBox box)
    {
        if (box.IsZeroDivisorMethod())
        {
            return 10 / box.Divisor;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_AssignedZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        var divisor = 0;
        return value / divisor;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_GuardExcludesZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor != 0)
        {
            return value / divisor;
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_RelationalPatternVariableBindingExcludesZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value is > 0 and var divisor)
        {
            return 10 / divisor;
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_PropertyPatternVariableBindingExcludesZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string text)
    {
        if (text is { Length: > 0 and var length })
        {
            return 10 / length;
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_SwitchStatementPatternVariableBindingExcludesZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case > 0 and var divisor:
                return 10 / divisor;
            default:
                return 0;
        }
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_SwitchStatementPriorSectionExcludesZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 0:
                return 0;
            case var divisor:
                return 10 / divisor;
        }
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_SwitchExpressionPatternVariableBindingExcludesZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value switch
        {
            > 0 and var divisor => 10 / divisor,
            _ => 0
        };
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_SwitchExpressionFallbackExcludesZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value switch
        {
            0 => 0,
            _ => 10 / value
        };
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_SwitchExpressionAssignedNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int mode)
    {
        var divisor = mode switch
        {
            0 => 1,
            1 => 2,
            _ => 3
        };

        return value / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_SwitchStatementAssignedNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int mode)
    {
        var divisor = 0;
        switch (mode)
        {
            case 0:
                divisor = 1;
                break;
            case 1:
                divisor = 2;
                break;
            default:
                divisor = 3;
                break;
        }

        return value / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_SwitchStatementDefaultExcludesZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        switch (divisor)
        {
            case 0:
                return 0;
            default:
                return 10 / divisor;
        }
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_SwitchStatementExitingCaseExcludesZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        switch (divisor)
        {
            case 0:
                return 0;
        }

        return 10 / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_SwitchStatementContinuingMutationReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        switch (divisor)
        {
            case 0:
                return 0;
            default:
                divisor = 0;
                break;
        }

        return 10 / divisor;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_AssignedNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        var divisor = 1;
        return value / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_CompoundAssignedNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 0;
        divisor += 1;
        return 10 / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_IncrementedNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 0;
        divisor++;
        return 10 / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_TupleAssignedNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 0;
        var other = 0;
        (divisor, other) = (1, 2);
        return 10 / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_TupleDeconstructionDeclaredNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var (divisor, other) = (1, 2);
        return 10 / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_InlineFiniteArrayElementAssignedNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = (new[] { 1, 2 })[0];
        return 10 / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_PriorFiniteArrayElementAssignedNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new[] { 1, 2 };
        var divisor = values[0];
        return 10 / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_InlineFiniteArrayFromEndElementAssignedNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = (new[] { 1, 2 })[^1];
        return 10 / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_PriorFiniteArrayFromEndElementAssignedNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new[] { 1, 2 };
        var divisor = values[^1];
        return 10 / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_ConditionalFiniteArrayElementAssignedNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var values = new[] { 1, 2 };
        var divisor = flag ? values[0] : values[1];
        return 10 / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_TupleElementAssignedNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (1, 2);
        var divisor = pair.Item1;
        return 10 / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_NamedTupleElementAssignedNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (divisor: 1, other: 2);
        var divisor = pair.divisor;
        return 10 / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_TupleLocalDeconstructionAssignedNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (1, 2);
        var divisor = 0;
        var other = 0;
        (divisor, other) = pair;
        return 10 / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_TupleLocalDeconstructionDeclaredNonZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (1, 2);
        var (divisor, other) = pair;
        return 10 / divisor;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_TupleArrayElementLengthIndex_ReportsIndexOutOfRange()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (values: new int[1], other: 0);
        return pair.values[1];
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.IndexOutOfRangeException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_index_out_of_range"));
    }

    [Test]
    public async Task Sp0010_TupleMultidimensionalArrayElementGetLengthIndex_ReportsIndexOutOfRange()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (values: new int[2, 3], other: 0);
        return pair.values[1, 3];
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.IndexOutOfRangeException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_index_out_of_range"));
    }

    [Test]
    public async Task Sp0010_MultidimensionalArrayGetValueIndex_ReportsIndexOutOfRange()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[2, 3];
        return (int)values.GetValue(1, 3)!;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.IndexOutOfRangeException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_array_get_value_index_out_of_range"));
    }

    [Test]
    public async Task Sp0010_PartialConjunctiveGuardExcludesZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor != 0 && IsReady())
        {
            return value / divisor;
        }

        return 0;
    }

    private static bool IsReady()
    {
        return DateTime.UtcNow.Ticks >= 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_AffineGuardExcludesZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor - 1 >= 0 || divisor + 1 <= 0)
        {
            return value / divisor;
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_DisjunctiveGuardExcludesZeroDivisor_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor < 0 || divisor > 0)
        {
            return value / divisor;
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_RelationalPatternNonZero_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor is < 0 or > 0)
        {
            return value / divisor;
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_ListPatternElementBindingNonZero_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [> 0 and var divisor, ..])
        {
            return 10 / divisor;
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_TrailingListPatternElementBindingNonZero_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [.., > 0 and var divisor])
        {
            return 10 / divisor;
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_ArrayElementReadFromListPatternFacts_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [> 0, ..])
        {
            var divisor = values[0];
            return 10 / divisor;
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_ArrayElementWriteThenReadZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        values[0] = 0;
        var divisor = values[0];
        return 10 / divisor;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
    }

    [Test]
    public async Task Sp0010_MultidimensionalArrayElementWriteThenReadZeroDivisor_ReportsDivideByZero()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[,] values)
    {
        values[0, 1] = 0;
        var divisor = values[0, 1];
        return 10 / divisor;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_divide_by_zero"));
    }

    [Test]
    public async Task Sp0010_EmptyListPatternIndex_ReportsIndexOutOfRange()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [])
        {
            return values[0];
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.IndexOutOfRangeException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_index_out_of_range"));
    }

    [Test]
    public async Task Sp0010_ConditionalArrayLengthIndex_ReportsIndexOutOfRange()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var values = flag ? new int[1] : new int[1];
        return values[1];
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.IndexOutOfRangeException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_index_out_of_range"));
    }

    [Test]
    public async Task Sp0010_CoalescedArrayFallbackLengthIndex_ReportsIndexOutOfRange()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        if (input != null)
        {
            return 0;
        }

        var values = input ?? new int[1];
        return values[1];
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.IndexOutOfRangeException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_index_out_of_range"));
    }

    [Test]
    public async Task Sp0010_NullDominatedCoalesceAssignmentLengthIndex_ReportsIndexOutOfRange()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values != null)
        {
            return 0;
        }

        values ??= new int[1];
        return values[1];
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.IndexOutOfRangeException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_index_out_of_range"));
    }

    [Test]
    public async Task Sp0010_KnownNonNullCoalesceAssignmentLengthIndex_ReportsIndexOutOfRange()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[2];
        values ??= new int[1];
        return values[2];
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.IndexOutOfRangeException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_index_out_of_range"));
    }

    [Test]
    public async Task Sp0010_WhileNormalExitIndex_ReportsIndexOutOfRange()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        while (index < values.Length)
        {
            index++;
        }

        return values[index];
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.IndexOutOfRangeException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_index_out_of_range"));
    }

    [Test]
    public async Task Sp0010_CompletedLoopExitPrunesSwitchSectionThrow_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(int index)
    {
        var limit = 10;
        while (index < limit)
        {
            index++;
        }

        switch (index)
        {
            case < 10:
                throw new InvalidOperationException();
        }
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_LoopBreakBeforeSwitchThrow_RemainsConservativeReports()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(int index, bool stop)
    {
        var limit = 10;
        while (index < limit)
        {
            if (stop)
            {
                break;
            }

            index++;
        }

        switch (index)
        {
            case < 10:
                throw new InvalidOperationException();
        }
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("direct_throw"));
    }

    [Test]
    public async Task Sp0010_WhileBreakExitIndex_RemainsConservativeDoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values, int index, bool stop)
    {
        while (index < values.Length)
        {
            if (stop)
            {
                break;
            }

            index++;
        }

        return values[index];
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_EmptyArrayFromEndIndex_ReportsIndexOutOfRange()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[0];
        return values[^1];
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.IndexOutOfRangeException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_index_out_of_range"));
    }

    [Test]
    public async Task Sp0010_EmptyStringFromEndIndex_ReportsIndexOutOfRange()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public char TestMethod()
    {
        var text = string.Empty;
        return text[^1];
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.IndexOutOfRangeException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_index_out_of_range"));
    }

    [Test]
    public async Task Sp0010_FromEndZeroIndex_ReportsIndexOutOfRange()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        return values[^0];
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.IndexOutOfRangeException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_index_out_of_range"));
    }

    [Test]
    public async Task Sp0010_NonEmptyListPatternFromEndIndex_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [_, ..])
        {
            return values[^1];
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_NonEmptyListPatternIndex_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [_, ..])
        {
            return values[0];
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_ConstrainedNonEmptyListPatternIndex_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [0, ..])
        {
            return values[0];
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_NestedSliceListPatternIndex_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [.. [_, _]])
        {
            return values[1];
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_GuardImpliesNullReceiver_ReportsNullReference()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (value == null)
        {
            return value.Length;
        }

        return 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.NullReferenceException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_null_dereference"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty],
            Is.EqualTo("System.NullReferenceException=definite_null_dereference:null_receiver"));
    }

    [Test]
    public async Task Sp0010_DefaultLiteralReference_ReportsNullReference()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        string value = default;
        return value.Length;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.NullReferenceException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_null_dereference"));
    }

    [Test]
    public async Task Sp0010_CoalesceRightImpliesNullReceiver_ReportsNullReference()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public string TestMethod(string value)
    {
        return value ?? value.ToString();
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.NullReferenceException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_null_dereference"));
    }

    [Test]
    public async Task Sp0010_CoalesceRightAssignmentBeforeUse_RemainsConservativeDoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public string TestMethod(string value)
    {
        return value ?? (value = ""safe"").ToString();
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_ConditionalExpressionTrueBranchImpliesNullReceiver_ReportsNullReference()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        return value == null ? value.Length : 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.NullReferenceException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_null_dereference"));
    }

    [Test]
    public async Task Sp0010_TypePatternExcludesNullReceiver_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(object value)
    {
        if (value is string text)
        {
            return text.Length;
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_PartialConjunctiveGuardImpliesNullReceiver_ReportsNullReference()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod(string value)
    {
        if (value == null && IsReady())
        {
            return value.Length;
        }

        return 0;
    }

    private static bool IsReady()
    {
        return DateTime.UtcNow.Ticks >= 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.NullReferenceException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("definite_null_dereference"));
    }

    [Test]
    public async Task Sp0010_GuardExcludesNullReceiver_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (value != null)
        {
            return value.Length;
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_NegatedGuardExcludesNullReceiver_DoesNotReport()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (!(value == null))
        {
            return value.Length;
        }

        return 0;
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_CatchFilterTautology_SuppressesException()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(int x)
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (x == x)
        {
        }
    }
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_CatchFilterContradiction_DoesNotSuppressException()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(int x)
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (x != x)
        {
        }
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("direct_throw"));
    }

    [Test]
    public async Task Sp0010_CatchFilterUnknown_RemainsConservative()
    {
        var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (ShouldCatch())
        {
        }
    }

    private static bool ShouldCatch()
    {
        return DateTime.UtcNow.Ticks >= 0;
    }
}");

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("direct_throw"));
    }
}