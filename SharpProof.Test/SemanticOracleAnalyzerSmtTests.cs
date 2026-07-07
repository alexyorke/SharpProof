using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test
{
    [TestFixture]
    [Category("SmtHeavy")]
    public class SemanticOracleAnalyzerSmtTests : SemanticOracleSmtTestBase
    {
        [Test]
        public async Task Sp0002_ContradictoryGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsConditionAlwaysFalse("int x", "x > 0 && x < 0"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        if (x > 0 && x < 0)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ConditionalExpressionContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(bool flag, int x, int y)
    {
        if ((flag ? x : y) == 5 && flag && x != 5)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_PropertyPatternContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (text is { Length: > 3 } && text.Length <= 3)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_CoalesceThrowAssignedNonNullContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string value)
    {
        var safe = value ?? throw new InvalidOperationException();
        if (safe == null)
        {
            Console.WriteLine(safe);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_ConditionalThrowAssignedNonNullContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string value)
    {
        var safe = value != null ? value : throw new InvalidOperationException();
        if (safe == null)
        {
            Console.WriteLine(safe);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_FreshObjectAssignedNonNullContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(bool flag)
    {
        object value = flag ? new object() : new object();
        if (value == null)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_TypePatternContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(object value)
    {
        if (value is string && value == null)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_AffineContradictoryGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsConditionAlwaysFalse("int x", "x + 1 <= 0 && x >= 0"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        if (x + 1 <= 0 && x >= 0)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ReassignedLocalDoesNotReuseStalePathFact_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        if (x > 0)
        {
            x = -1;
            if (x < 0)
            {
                Console.WriteLine(x);
            }
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_LocalInitializerContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var x = 0;
        if (x != 0)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_LocalAssignmentContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int x;
        x = 5;
        if (x != 5)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_UlongLocalAssignmentContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        ulong value;
        value = 0UL;
        if (value != 0UL)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_EnumLocalAssignmentContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public enum Mode
{
    None = 0,
    Ready = 1
}

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Mode state;
        state = Mode.Ready;
        if (state != Mode.Ready)
        {
            Console.WriteLine(state);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ImplicitElseMergedNonZeroGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(bool flag)
    {
        var divisor = 1;
        if (flag)
        {
            divisor = 2;
        }

        if (divisor == 0)
        {
            Console.WriteLine(divisor);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_BooleanAssignmentContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var ready = true;
        if (!ready)
        {
            Console.WriteLine();
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ParameterAssignmentContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        x = 1;
        if (x != 1)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_UlongZeroContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(ulong value)
    {
        if (value == 0UL && value != 0UL)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_BooleanPredicateAliasContradictoryGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsStatementUnreachable(@"
using System;

public class TestClass
{
    public void TestMethod(int value)
    {
        var isZero = value == 0;
        if (isZero && value != 0)
        {
            Console.WriteLine(value);
        }
    }
}",
                    "Console.WriteLine(value);"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        var isZero = value == 0;
        if (isZero && value != 0)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_BitwiseBooleanAndContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        if ((value == 0) & (value != 0))
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_WideningIntegralCastContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        if ((long)value > 0L && value <= 0)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_EnumContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public enum Mode
{
    None = 0,
    Ready = 1
}

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Mode state)
    {
        if (state == Mode.Ready && state != Mode.Ready)
        {
            Console.WriteLine(state);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_WhileNormalExitConditionContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values, int index)
    {
        while (index < values.Length)
        {
            index++;
        }

        if (index < values.Length)
        {
            Console.WriteLine(index);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_LargeUlongConstantGuard_RemainsConservativeReports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(ulong value)
    {
        if (value == 18446744073709551615UL && value == 0UL)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_NullAssignmentContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        string value = null;
        if (value != null)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_LocalAssignmentReachableGuard_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var x = 0;
        x = 1;
        if (x == 1)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_ArrayCreationLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var values = new int[0];
        if (values.Length > 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ArrayCreationLengthReachableGuard_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var values = new int[1];
        if (values.Length > 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_SymbolicArrayLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int length)
    {
        var values = new int[length];
        if (values.Length != length)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ConditionalArrayLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(bool flag)
    {
        var values = flag ? new int[1] : new int[1];
        if (values.Length != 1)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_CoalescedArrayFallbackLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] input)
    {
        if (input != null)
        {
            return;
        }

        var values = input ?? new int[1];
        if (values.Length != 1)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullDominatedCoalesceAssignmentLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        if (values != null)
        {
            return;
        }

        values ??= new int[1];
        if (values.Length != 1)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_KnownNonNullCoalesceAssignmentLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var values = new int[2];
        values ??= new int[1];
        if (values.Length != 2)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullDominatedNullableCoalesceAssignmentContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int? maybe = null;
        maybe ??= 5;
        if (!maybe.HasValue || maybe.Value != 5)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_KnownHasValueNullableCoalesceAssignmentContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int? maybe = 7;
        maybe ??= 5;
        if (!maybe.HasValue || maybe.Value != 7)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableCoalesceAssignmentFallbackHasValueContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? maybe)
    {
        maybe ??= 5;
        if (!maybe.HasValue)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_ArrayInitializerLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var values = new[] { 1, 2 };
        if (values.Length != 2)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ArrayCollectionExpressionLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int[] values = [1, 2, 3];
        if (values.Length != 3)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ArrayCollectionExpressionSpreadFixedLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] input)
    {
        int[] values = [.. input, 1];
        if (values.Length == 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ArrayCollectionExpressionAllSpreadLength_RemainsConservativeReports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] input)
    {
        int[] values = [.. input];
        if (values.Length == 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_ReadOnlySpanCollectionExpressionSpreadFixedLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] input)
    {
        ReadOnlySpan<int> values = [.. input, 1];
        if (values.Length == 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ArrayEmptyLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var values = Array.Empty<int>();
        if (values.Length != 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ArrayAliasLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int length)
    {
        var values = new int[length];
        var alias = values;
        if (alias.Length != length)
        {
            Console.WriteLine(alias.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ObjectErasedArrayCastAliasLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int length)
    {
        var values = new int[length];
        object boxed = values;
        var alias = (int[])boxed;
        if (alias.Length != length)
        {
            Console.WriteLine(alias.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ObjectErasedStringCastAliasLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        object boxed = ""abcd"";
        var alias = (string)boxed;
        if (alias.Length != 4)
        {
            Console.WriteLine(alias.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_StringLiteralLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var text = ""abc"";
        if (text.Length != 3)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_DirectStringLiteralLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        if (""abc"".Length != 3)
        {
            Console.WriteLine();
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_StringEmptyLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var text = string.Empty;
        if (text.Length > 0)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_StringEmptyLengthReachableGuard_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var text = string.Empty;
        if (text.Length == 0)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_StringAliasLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string input)
    {
        var text = input;
        var alias = text;
        if (alias.Length != input.Length)
        {
            Console.WriteLine(alias);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_StringLiteralLengthReachableGuard_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var text = ""abc"";
        if (text.Length == 3)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_ArrayLengthFactInvalidatedAfterReassignment_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] input)
    {
        var values = new int[0];
        values = input;
        if (values.Length > 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_DisjunctiveContradictoryGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsConditionAlwaysFalse("int x", "(x == 0 || x == 1) && x != 0 && x != 1"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        if ((x == 0 || x == 1) && x != 0 && x != 1)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_EarlyExitGuardContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values, int index)
    {
        if (index < 0 || index >= values.Length)
        {
            return;
        }

        if (index < 0 || index >= values.Length)
        {
            Console.WriteLine(index);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_EarlyExitGuardPrunesSwitchSectionImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        if (value < 0)
        {
            return;
        }

        switch (value)
        {
            case < 0:
                Console.WriteLine(value);
                break;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_EarlyExitGuardMutationBeforeSwitch_RemainsConservativeReports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value, int replacement)
    {
        if (value < 0)
        {
            return;
        }

        value = replacement;
        switch (value)
        {
            case < 0:
                Console.WriteLine(value);
                break;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_ContradictoryNullPatternGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsConditionAlwaysFalse("string value", "(value is null) && (value is not null)"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string value)
    {
        if ((value is null) && (value is not null))
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ContradictoryRelationalPatternGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsConditionAlwaysFalse("int x", "x is > 0 and < 0"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        if (x is > 0 and < 0)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ArrayListPatternContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        if (values is [] && values.Length > 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ArrayListPatternReachableGuard_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        if (values is [_, ..] && values.Length > 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_ArrayLengthNegativeGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        if (values.Length < 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_StringLengthNegativeGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (text.Length < 0)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_CollectionCountNegativeGuard_EvaluatesUnknownGetterReports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(IReadOnlyCollection<int> values)
    {
        if (values.Count < 0)
        {
            Console.WriteLine(values.Count);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_SourceCollectionCountNegativeGuard_RemainsConservativeReports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using System.Collections;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class SourceCollection : IReadOnlyCollection<int>
{
    public int Count => -1;

    public IEnumerator<int> GetEnumerator()
    {
        yield break;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public class TestClass
{
    [EnforcePure]
    public void TestMethod(SourceCollection values)
    {
        if (values.Count < 0)
        {
            Console.WriteLine(values.Count);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_SourceNullOrEmptyPredicateTrueBranchContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (SourcePredicates.IsNullOrEmptyLike(text) && text != null && text.Length > 0)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SourceNullOrEmptyPredicateFalseBranchContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (!SourcePredicates.IsNullOrEmptyLike(text) && text.Length <= 0)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SourceNullOrEmptyPredicateReachableImpureCall_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (SourcePredicates.IsNullOrEmptyLike(text))
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_SourceHasTextPredicateLengthContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (SourcePredicates.HasText(text) && text.Length <= 0)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SourceHasTextPredicateNullContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (SourcePredicates.HasText(text) && text == null)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SourceHasTextGuardPredicateContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (SourcePredicates.HasTextWithGuard(text) && (text == null || text.Length <= 0))
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SourceHasTextIfElsePredicateContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (SourcePredicates.HasTextWithIfElse(text) && (text == null || text.Length <= 0))
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SourceHasTextLocalAliasPredicateContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (SourcePredicates.HasTextViaLocal(text) && (text == null || text.Length <= 0))
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SourceHasTextLocalAssignmentPredicateContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (SourcePredicates.HasTextViaAssignment(text) && (text == null || text.Length <= 0))
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SourceLocalAssignmentIntegerPredicateContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        if (SourcePredicates.IsPositiveAfterLocalAssignment(value) && value < -1)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SourceMultiGuardIndexPredicateContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values, int index)
    {
        if (SourcePredicates.IsValidIndex(values, index) &&
            (values == null || index < 0 || index >= values.Length))
        {
            Console.WriteLine(index);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SourceSwitchStatementPredicateContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        if (SourcePredicates.IsZeroWithSwitch(value) && value != 0)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SourceSwitchStatementPatternPredicateContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        if (SourcePredicates.IsSmallPositiveWithSwitch(value) &&
            (value <= 0 || value >= 10))
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SourceBooleanPropertyContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(SourcePredicateBox box)
    {
        if (box.HasText && (box.Value == null || box.Value.Length <= 0))
        {
            Console.WriteLine(box.Value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_InstanceSourceBooleanMethodContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(SourcePredicateBox box)
    {
        if (box.HasTextMethod() && (box.Value == null || box.Value.Length <= 0))
        {
            Console.WriteLine(box.Value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_MetadataStringPredicateContradictoryBranch_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (string.IsNullOrEmpty(text) && text != null && text.Length > 0)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_StringConcatContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string left, string right)
    {
        var value = left + right;
        if (left == ""A"" && right == ""B"" && value != ""AB"")
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_StringPrefixSubstringContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (text.Substring(0, 3) == ""PRE"" && text == ""ALT"")
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_StringIndexOfOrdinalIgnoreCaseContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (text.IndexOf(""a"", StringComparison.OrdinalIgnoreCase) < 0 && text == ""A"")
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_StringEqualsOrdinalIgnoreCaseContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (string.Equals(text, ""a"", StringComparison.OrdinalIgnoreCase) && text == ""B"")
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_StringPredicatesOrdinalIgnoreCaseContradictoryImpureCalls_DoNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (text.Contains(""a"", StringComparison.OrdinalIgnoreCase) && text == ""BBB"")
        {
            Console.WriteLine(text);
        }

        if (text.StartsWith(""ab"", StringComparison.OrdinalIgnoreCase) && text == ""zzAB"")
        {
            Console.WriteLine(text);
        }

        if (text.EndsWith(""xy"", StringComparison.OrdinalIgnoreCase) && text == ""XYzz"")
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_CustomLengthNegativeGuard_RemainsConservativeReports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public sealed class HasLength
{
    public int Length => -1;
}

public class TestClass
{
    [EnforcePure]
    public void TestMethod(HasLength value)
    {
        if (value.Length < 0)
        {
            Console.WriteLine(value.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_CustomCountNegativeGuard_RemainsConservativeReports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public sealed class HasCount
{
    public int Count => -1;
}

public class TestClass
{
    [EnforcePure]
    public void TestMethod(HasCount value)
    {
        if (value.Count < 0)
        {
            Console.WriteLine(value.Count);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_SwitchExpressionArrayLengthNegativeArm_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int[] values)
    {
        return values.Length switch
        {
            < 0 => Console.ReadLine(),
            _ => string.Empty
        };
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SwitchExpressionAssignedNonZeroGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int mode)
    {
        var divisor = mode switch
        {
            0 => 1,
            1 => 2,
            _ => 3
        };

        if (divisor == 0)
        {
            Console.WriteLine(divisor);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_ElementConstrainedListPatternFalseBranchRemainsReachable_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        if (values is not [1] && values.Length == 1)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_SwitchStatementContradictoryPatternGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        switch (x)
        {
            case > 0 when x < 0:
                Console.WriteLine(x);
                break;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SwitchStatementReachablePatternGuardedImpureCall_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        switch (x)
        {
            case > 0 when x > 0:
                Console.WriteLine(x);
                break;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_SwitchStatementContradictoryConstantCaseGuard_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        switch (x)
        {
            case 0 when x != 0:
                Console.WriteLine(x);
                break;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SwitchStatementExitingCasePostCondition_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        switch (x)
        {
            case 0:
                return;
        }

        if (x == 0)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SwitchStatementContinuingMutationDoesNotUseStalePostCondition_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        switch (x)
        {
            case 0:
                return;
            default:
                x = 0;
                break;
        }

        if (x == 0)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_SwitchExpressionContradictoryPatternGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int x)
    {
        return x switch
        {
            > 0 when x < 0 => Console.ReadLine(),
            _ => string.Empty
        };
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_SwitchExpressionReachablePatternGuardedImpureCall_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int x)
    {
        return x switch
        {
            > 0 when x > 0 => Console.ReadLine(),
            _ => string.Empty
        };
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_PartialConjunctiveGuardFeedsNestedContradiction_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x, string text)
    {
        if (x > 0 && text.Length >= 0)
        {
            if (x < 0)
            {
                Console.WriteLine(x);
            }
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_NullableHasValueContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int? value = 5;
        if (!value.HasValue)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableEqualsConstantContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value == 5)
        {
            if (!value.HasValue || value.Value != 5)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableGreaterThanContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value > 0)
        {
            if (!value.HasValue || value.Value <= 0)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_RecursivePatternAliasMemberContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string value)
    {
        if (value is { Length: > 0 } text)
        {
            if (text == null || text.Length <= 0)
            {
                Console.WriteLine(text);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_ExtendedPropertyPatternContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

" + ExtendedPropertyPatternSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(ExtendedPatternBox box)
    {
        if (box is { Child.Value: > 0 } && box.Child.Value <= 0)
        {
            Console.WriteLine(box.Child.Value);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableNotNullGuardContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value != null)
        {
            if (!value.HasValue)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableNullGuardContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value == null)
        {
            if (value.HasValue)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableIsNotNullPatternContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value is not null)
        {
            if (!value.HasValue)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableIsNullPatternContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value is null)
        {
            if (value.HasValue)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableRecursivePatternContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value is { })
        {
            if (!value.HasValue)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableNotRecursivePatternContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value is not { })
        {
            if (value.HasValue)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableDefaultReassignmentReachableGuard_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int? value = 5;
        value = default;
        if (!value.HasValue)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_AsExpressionNullSourceContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        object value = null;
        var text = value as string;
        if (text != null)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_AsExpressionNonNullSourceNullResultGuard_RemainsConservativeReports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        object value = new object();
        var text = value as string;
        if (text == null)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.True);
        }

        [Test]
        public async Task Sp0002_InlineAsAssignmentContradictoryRuntimeTypeGuard_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(object value)
    {
        string text;
        if ((text = value as string) == null && value is string)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_ConditionalAccessNullSourceHasValueGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        string text = null;
        int? length = text?.Length;
        if (length.HasValue)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_ConditionalAccessNonNullSourceHasValueGuard_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        string text = ""value"";
        int? length = text?.Length;
        if (length.HasValue)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Sp0002_ConditionalAccessNullableValueContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        string text = ""abc"";
        int? length = text?.Length;
        if (length.HasValue && length.Value != 3)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableCoalesceConditionalAccessNonNullReceiverContradictoryGuard_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        string text = ""abc"";
        int length = text?.Length ?? 0;
        if (length != 3)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableCoalesceConditionalAccessNullReceiverContradictoryGuard_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        string text = null;
        int length = text?.Length ?? 0;
        if (length != 0)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableDeclarationPatternNullInputGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int? maybe = null;
        if (maybe is int value)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableDeclarationPatternBindingContradictoryGuard_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int? maybe = 5;
        if (maybe is int value && value != 5)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_NullableRelationalPatternContradictoryGuard_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int? maybe = 5;
        if (maybe is < 0)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Sp0002_SatisfiableGuardedImpureCall_ReportsStructuredEvidence()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        if (x >= 0 && x <= 0)
        {
            Console.WriteLine(x);
        }
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);

            Assert.That(
                diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty],
                Is.AnyOf("catalog_hit", "impure_callee", "unknown_external_call"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(
                diagnostic.Properties[SharpProofDiagnostics.ImpurityOperationKindProperty],
                Is.AnyOf("Invocation", "InvocationExpression"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.InvalidOperationException=direct_throw:throw"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_array_get_value_index_out_of_range"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.NullReferenceException=definite_null_dereference:null_receiver"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
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

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

    }
}
