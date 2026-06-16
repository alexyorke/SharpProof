using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using PurelySharp.Analyzer;
using PurelySharp.Test.Smt;
using SearchLib.Smt;


namespace PurelySharp.Test
{
    [TestFixture]
    public class SemanticOracleSmtTests
    {
        [Test]
        public void Oracle_ContradictoryIntegerCondition_IsUnsatisfiable()
        {
            var context = AnalyzerTestHost.CreateConditionContext("int x", "x > 0 && x < 0");
            using var oracle = new SmtPathOracle();

            Assert.That(
                oracle.IsSatisfiable(context.Expression, context.SemanticModel, TimeSpan.FromMilliseconds(50)),
                Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void Oracle_NullGuardImpliesNotNullComparison()
        {
            var context = AnalyzerTestHost.CreateConditionImplicationContext("string s", "s != null", "s != null");
            using var oracle = new SmtPathOracle();

            Assert.That(
                oracle.Implies(context.PathCondition, context.Conclusion, context.SemanticModel, TimeSpan.FromMilliseconds(50)),
                Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void Oracle_DisjunctiveNonZeroGuard_ImpliesNotZero()
        {
            var context = AnalyzerTestHost.CreateConditionImplicationContext("int divisor", "divisor < 0 || divisor > 0", "divisor != 0");
            using var oracle = new SmtPathOracle();

            Assert.That(
                oracle.Implies(context.PathCondition, context.Conclusion, context.SemanticModel, TimeSpan.FromMilliseconds(50)),
                Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public async Task Ps0002_ContradictoryGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsConditionAlwaysFalse("int x", "x > 0 && x < 0"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_DisjunctiveContradictoryGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsConditionAlwaysFalse("int x", "(x == 0 || x == 1) && x != 0 && x != 1"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ContradictoryNullPatternGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsConditionAlwaysFalse("string value", "(value is null) && (value is not null)"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ContradictoryRelationalPatternGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsConditionAlwaysFalse("int x", "x is > 0 and < 0"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SatisfiableGuardedImpureCall_ReportsStructuredEvidence()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

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

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("Invocation"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0010_ContradictoryGuardedThrow_DoesNotReport()
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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SatisfiableGuardedThrow_ReportsDirectThrow()
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
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.InvalidOperationException=direct_throw:throw"));
        }

        [Test]
        public async Task Ps0010_GuardImpliesZeroDivisor_ReportsDivideByZero()
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
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_RelationalPatternExactZero_ReportsDivideByZero()
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
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_GuardExcludesZeroDivisor_DoesNotReport()
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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_DisjunctiveGuardExcludesZeroDivisor_DoesNotReport()
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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_RelationalPatternNonZero_DoesNotReport()
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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_GuardImpliesNullReceiver_ReportsNullReference()
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
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.NullReferenceException=definite_null_dereference:null_receiver"));
        }

        [Test]
        public async Task Ps0010_GuardExcludesNullReceiver_DoesNotReport()
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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NegatedGuardExcludesNullReceiver_DoesNotReport()
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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CatchFilterTautology_SuppressesException()
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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CatchFilterContradiction_DoesNotSuppressException()
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
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_CatchFilterUnknown_RemainsConservative()
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
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        private static Task<ImmutableArray<Diagnostic>> GetExceptionDiagnosticsAsync(string source)
        {
            return AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));
        }

        private static bool IsConditionAlwaysFalse(string parameterList, string conditionExpression)
        {
            var context = AnalyzerTestHost.CreateConditionContext(parameterList, conditionExpression);
            var method = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer).Assembly
                .GetType("PurelySharp.Analyzer.Engine.ExecutionVisibility", throwOnError: true)!
                .GetMethod("IsConditionAlwaysFalse", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

            return (bool)method.Invoke(null, new object?[] { context.Expression, context.SemanticModel, CancellationToken.None })!;
        }
    }
}
