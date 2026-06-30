using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class SymbolicRuntimeHazardQueryTests
    {
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
            Assert.That(hazard.PathConditions.Any(condition => condition.Contains("divisor", StringComparison.Ordinal) &&
                                                               condition.Contains("Value = 0", StringComparison.Ordinal)), Is.True);
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
                new SymbolicRuntimeHazardQueryOptions(includeUnprovenCandidates: true));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DivideByZero));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedNullDereference()
        {
            const string source = @"
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
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return value.Length;", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullDereference));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
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
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesDynamicInvocationNullBinding()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value.Missing();
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value.Missing();",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_dynamic_invocation_null_binding"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesDynamicDirectInvocationNullBinding()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value();
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value();",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_dynamic_invocation_null_binding"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesDynamicIndexerNullBinding()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value[0];
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value[0];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_dynamic_index_null_binding"));
        }

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
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        }

        [Test]
        public void QuerySourceRuntimeHazards_NonNullDynamicReceiverPrunesNullBindingCandidate()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = new object();
        return value.ToString();
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value.ToString();",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
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
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesUnboxNullCast()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        object value = null;
        return (int)value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return (int)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.UnboxNull }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.UnboxNull));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_unbox_null"));
        }

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
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.UnboxNull }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.UnboxNull));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesInvalidReferenceCast()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod()
    {
        object value = new object();
        return (string)value;
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
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesInvalidUnboxCast()
        {
            const string source = @"
public class TestClass
{
    public long TestMethod()
    {
        object value = 1;
        return (long)value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return (long)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.InvalidCast));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidCastException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_invalid_cast"));
        }

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

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesBuiltInIndexOutOfRange()
        {
            const string source = @"
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
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return values[0];", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.IndexOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesAssignedSpanSliceIndexOutOfRange()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod(ReadOnlySpan<int> values)
    {
        var tail = values.Slice(values.Length);
        return tail[0];
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return tail[0];", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.IndexOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesBuiltInRangeOutOfRange()
        {
            const string source = @"
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
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return value[1..];", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
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
        }

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
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedNegativeArrayLength()
        {
            const string source = @"
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
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return new int[length];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NegativeArrayLength }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NegativeArrayLength));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_negative_array_length"));
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
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.NegativeArrayLength }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NegativeArrayLength));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        }

        [Test]
        public void QuerySourceRuntimeHazards_MultidimensionalArrayNegativeLength_ProvesOverflow()
        {
            const string source = @"
public class TestClass
{
    public int[,] TestMethod()
    {
        var length = -1;
        return new int[1, length];
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return new int[1, length];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NegativeArrayLength }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NegativeArrayLength));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedBytePreIncrementOverflow()
        {
            const string source = @"
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
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return checked(++value);", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
            Assert.That(hazard.OperationText, Is.EqualTo("++value"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedLongPostDecrementOverflow()
        {
            const string source = @"
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
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return checked(value--);", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
            Assert.That(hazard.OperationText, Is.EqualTo("value--"));
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
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
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
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.OperationText, Is.EqualTo("++value"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedExplicitNumericConversionOverflow()
        {
            const string source = @"
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
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return checked((int)value);", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_numeric_conversion_overflow"));
            Assert.That(hazard.OperationText, Is.EqualTo("(int)value"));
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
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
            Assert.That(hazard.OperationText, Is.EqualTo("(int)value"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesArrayCovarianceStoreMismatch()
        {
            const string source = @"
public class TestClass
{
    public void TestMethod()
    {
        object[] values = new string[1];
        values[0] = 42;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "values[0] = 42;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArrayTypeMismatch }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArrayTypeMismatch));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArrayTypeMismatchException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_array_type_mismatch"));
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
                var result = await RunSymbolicCliAsync(
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
                Assert.That(hazard.GetProperty("Kind").GetString(), Is.EqualTo(SymbolicRuntimeHazardKind.DivideByZero.ToString()));
                Assert.That(hazard.GetProperty("Status").GetString(), Is.EqualTo(SymbolicRuntimeHazardStatus.Proven.ToString()));
                Assert.That(hazard.GetProperty("ExceptionType").GetString(), Is.EqualTo("System.DivideByZeroException"));
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
                var result = await RunSymbolicCliAsync(
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
                Assert.That(hazard.GetProperty("Kind").GetString(), Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding.ToString()));
                Assert.That(hazard.GetProperty("Status").GetString(), Is.EqualTo(SymbolicRuntimeHazardStatus.Proven.ToString()));
                Assert.That(hazard.GetProperty("ExceptionType").GetString(), Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
                Assert.That(hazard.GetProperty("Category").GetString(), Is.EqualTo("definite_dynamic_member_null_binding"));
            }
            finally
            {
                File.Delete(sourcePath);
            }
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
                references: AnalyzerTestHost.GetTrustedPlatformReferences(),
                options: options);
        }

        private static SymbolicRuntimeHazard AssertSingleHazard(SymbolicRuntimeHazardQueryResult result)
        {
            Assert.That(result.Hazards, Has.Count.EqualTo(1));
            return result.Hazards.Single();
        }

        private static int FindLine(string source, string text)
        {
            var position = FindPosition(source, text);
            var line = 1;
            for (var index = 0; index < position; index++)
            {
                if (source[index] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        private static int FindPosition(string source, string text)
        {
            var position = source.IndexOf(text, StringComparison.Ordinal);
            if (position < 0)
            {
                throw new InvalidOperationException("Text not found: " + text);
            }

            return position;
        }

        private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunSymbolicCliAsync(params string[] arguments)
        {
            var repositoryRoot = FindRepositoryRoot();
            var cliAssemblyPath = FindSymbolicCliAssemblyPath(repositoryRoot);
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(cliAssemblyPath);
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start symbolic CLI.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            return (process.ExitCode, await outputTask, await errorTask);
        }

        private static string FindSymbolicCliAssemblyPath(string repositoryRoot)
        {
            var targetFramework = Path.GetFileName(TestContext.CurrentContext.TestDirectory);
            var configurations = new[]
            {
                FindBuildConfiguration(),
                "Release",
                "Debug",
            }
            .Where(static configuration => !string.IsNullOrWhiteSpace(configuration))
            .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var configuration in configurations)
            {
                var candidate = Path.Combine(
                    repositoryRoot,
                    "Tools",
                    "PurelySharp.SymbolicCli",
                    "bin",
                    configuration,
                    targetFramework,
                    "PurelySharp.SymbolicCli.dll");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException(
                "Could not find built PurelySharp.SymbolicCli.dll. Build PurelySharp.Test first so its test dependency builds the CLI once.",
                Path.Combine(repositoryRoot, "Tools", "PurelySharp.SymbolicCli"));
        }

        private static string FindBuildConfiguration()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (string.Equals(directory.Name, "Release", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(directory.Name, "Debug", StringComparison.OrdinalIgnoreCase))
                {
                    return directory.Name;
                }

                directory = directory.Parent;
            }

            return "Debug";
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PurelySharp.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not find repository root.");
        }
    }
}
