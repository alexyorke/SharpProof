using System.Reflection;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
[Category("SmtHeavy")]
public sealed class StringLengthSmtTests
{
    [Test]
    public void SymbolicQueryExecutor_ProvesStringRemoveStartResultLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(string text, int start)
    {
        if (text != null && start >= 0 && start <= text.Length)
        {
            return text.Remove(start).Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return text.Remove(start).Length;",
            "text.Remove(start).Length == text.Length - start");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesStringRemoveRangeResultLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(string text, int start, int count)
    {
        if (text != null && start >= 0 && count >= 0 && start + count <= text.Length)
        {
            return text.Remove(start, count).Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return text.Remove(start, count).Length;",
            "text.Remove(start, count).Length == text.Length - count");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesStringInsertResultLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(string text, string value, int index)
    {
        if (text != null && value != null && index >= 0 && index <= text.Length)
        {
            return text.Insert(index, value).Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return text.Insert(index, value).Length;",
            "text.Insert(index, value).Length == text.Length + value.Length");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesStringPadResultLengths()
    {
        const string source = @"
public class TestClass
{
    public int PadLeft(string text, int width)
    {
        if (text != null && width >= text.Length)
        {
            return text.PadLeft(width).Length;
        }

        return 0;
    }

    public int PadRight(string text, int width)
    {
        if (text != null && width >= 0 && width <= text.Length)
        {
            return text.PadRight(width, '.').Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return text.PadLeft(width).Length;",
            "text.PadLeft(width).Length == width");
        AssertConditionProven(
            source,
            "return text.PadRight(width, '.').Length;",
            "text.PadRight(width, '.').Length == text.Length");
    }

    [Test]
    public void ExecutionVisibility_StringInsertLengthContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text, string value, int index",
                "text != null && value != null && index >= 0 && index <= text.Length && text.Insert(index, value).Length != text.Length + value.Length"),
            Is.True);
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesSpanSliceStartResultLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(System.Span<int> span, int start)
    {
        if (start >= 0 && start <= span.Length)
        {
            return span.Slice(start).Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return span.Slice(start).Length;",
            "span.Slice(start).Length == span.Length - start");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesReadOnlySpanSliceRangeResultLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(System.ReadOnlySpan<int> span, int start, int length)
    {
        if (start >= 0 && length >= 0 && start + length <= span.Length)
        {
            return span.Slice(start, length).Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return span.Slice(start, length).Length;",
            "span.Slice(start, length).Length == length");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesMemorySliceStartResultLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(System.Memory<int> memory, int start)
    {
        if (start >= 0 && start <= memory.Length)
        {
            return memory.Slice(start).Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return memory.Slice(start).Length;",
            "memory.Slice(start).Length == memory.Length - start");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesReadOnlyMemorySliceRangeResultLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(System.ReadOnlyMemory<int> memory, int start, int length)
    {
        if (start >= 0 && length >= 0 && start + length <= memory.Length)
        {
            return memory.Slice(start, length).Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return memory.Slice(start, length).Length;",
            "memory.Slice(start, length).Length == length");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesStringAsSpanAndAsMemoryResultLengths()
    {
        const string source = @"
using System;

public class TestClass
{
    public int Tail(string text, int start)
    {
        if (text != null && start >= 0 && start <= text.Length)
        {
            return text.AsSpan(start).Length;
        }

        return 0;
    }

    public int Window(string text, int start, int length)
    {
        if (text != null && start >= 0 && length >= 0 && start + length <= text.Length)
        {
            return text.AsMemory(start, length).Length;
        }

        return 0;
    }

    public int RangeWindow(string text)
    {
        if (text != null && text.Length >= 2)
        {
            return text.AsSpan(1..^1).Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return text.AsSpan(start).Length;",
            "text.AsSpan(start).Length == text.Length - start");
        AssertConditionProven(
            source,
            "return text.AsMemory(start, length).Length;",
            "text.AsMemory(start, length).Length == length");
        AssertConditionProven(
            source,
            "return text.AsSpan(1..^1).Length;",
            "text.AsSpan(1..^1).Length == text.Length - 2");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesAssignedSpanLengthSnapshot()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(System.Span<int> span)
    {
        System.Span<int> copy = span;
        return copy.Length;
    }
}";

        AssertConditionProven(
            source,
            "return copy.Length;",
            "copy.Length == span.Length");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesAssignedReadOnlySpanSliceLengthSnapshot()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(System.ReadOnlySpan<int> span, int start, int length)
    {
        if (start >= 0 && length >= 0 && start + length <= span.Length)
        {
            System.ReadOnlySpan<int> window = span.Slice(start, length);
            return window.Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return window.Length;",
            "window.Length == length");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesAssignedMemorySliceLengthSnapshots()
    {
        const string source = @"
public class TestClass
{
    public int Tail(System.Memory<int> memory, int start)
    {
        if (start >= 0 && start <= memory.Length)
        {
            System.Memory<int> tail = memory.Slice(start);
            return tail.Length;
        }

        return 0;
    }

    public int Window(System.ReadOnlyMemory<int> memory, int start, int length)
    {
        if (start >= 0 && length >= 0 && start + length <= memory.Length)
        {
            System.ReadOnlyMemory<int> window = memory.Slice(start, length);
            return window.Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return tail.Length;",
            "tail.Length == memory.Length - start");
        AssertConditionProven(
            source,
            "return window.Length;",
            "window.Length == length");
    }

    [Test]
    public void SymbolicQueryExecutor_UnsupportedSliceStartRemainsUnknown()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(System.Span<int> span)
    {
        if (span.Length > 0)
        {
            return span.Slice(GetStart()).Length;
        }

        return 0;
    }

    private int GetStart()
    {
        return 1;
    }
}";

        AssertConditionUnknown(
            source,
            "return span.Slice(GetStart()).Length;",
            "span.Slice(GetStart()).Length == span.Length - GetStart()");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesStringLiteralLengthConstant()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(string text)
    {
        if (text != null && text.Length == ""abc"".Length)
        {
            return text.Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return text.Length;",
            "text.Length == 3");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesStringRepeatCreationResultLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int count)
    {
        if (count >= 0)
        {
            return new string('x', count).Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return new string('x', count).Length;",
            "new string('x', count).Length == count");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesStringCharArrayCreationResultLengths()
    {
        const string source = @"
using System;

public class TestClass
{
    public int WholeArray(char[] chars)
    {
        if (chars != null)
        {
            return new string(chars).Length;
        }

        return 0;
    }

    public int ArrayRange(char[] chars, int start, int length)
    {
        if (chars != null && start >= 0 && length >= 0 && start + length <= chars.Length)
        {
            return new string(chars, start, length).Length;
        }

        return 0;
    }

    public int SpanRange(char[] chars, int start, int length)
    {
        if (chars != null && start >= 0 && length >= 0 && start + length <= chars.Length)
        {
            return new string(chars.AsSpan(start, length)).Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return new string(chars).Length;",
            "new string(chars).Length == chars.Length");
        AssertConditionProven(
            source,
            "return new string(chars, start, length).Length;",
            "new string(chars, start, length).Length == length");
        AssertConditionProven(
            source,
            "return new string(chars.AsSpan(start, length)).Length;",
            "new string(chars.AsSpan(start, length)).Length == length");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesStringConcatResultLengths()
    {
        const string source = @"
public class TestClass
{
    public int FixedConcat(string first, string second)
    {
        if (first != null && second != null)
        {
            return string.Concat(first, ""-"", second).Length;
        }

        return 0;
    }

    public int ParamsConcat(string first, string second, string third)
    {
        if (first != null && second != null && third != null)
        {
            return string.Concat(first, ""-"", second, ""-"", third).Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return string.Concat(first, \"-\", second).Length;",
            "string.Concat(first, \"-\", second).Length == first.Length + 1 + second.Length");
        AssertConditionProven(
            source,
            "return string.Concat(first, \"-\", second, \"-\", third).Length;",
            "string.Concat(first, \"-\", second, \"-\", third).Length == first.Length + 1 + second.Length + 1 + third.Length");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesStringInterpolationResultLengthWhenPartsAreStrings()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(string first, string second)
    {
        if (first != null && second != null)
        {
            return $""{first}-{second}"".Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return $\"{first}-{second}\".Length;",
            "$\"{first}-{second}\".Length == first.Length + 1 + second.Length");
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesStringInterpolationLengthWithNameofConstant()
    {
        const string source = @"
public class TestClass
{
    public int WithNameof(string text)
    {
        if (text != null)
        {
            return $""{text}:{nameof(WithNameof)}"".Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return $\"{text}:{nameof(WithNameof)}\".Length;",
            "$\"{text}:{nameof(WithNameof)}\".Length == text.Length + 1 + nameof(WithNameof).Length");
    }

    [Test]
    public void SymbolicQueryExecutor_UnsupportedFormattedStringConstructionLengthsRemainUnknown()
    {
        const string source = @"
public class TestClass
{
    public int InterpolationWithNonStringHole(string text, int value)
    {
        if (text != null)
        {
            return $""{text}:{value}"".Length;
        }

        return 0;
    }

    public int ObjectConcat(string text, object value)
    {
        if (text != null)
        {
            return string.Concat(text, value).Length;
        }

        return 0;
    }
}";

        AssertConditionUnknown(
            source,
            "return $\"{text}:{value}\".Length;",
            "$\"{text}:{value}\".Length == text.Length + 1");
        AssertConditionUnknown(
            source,
            "return string.Concat(text, value).Length;",
            "string.Concat(text, value).Length == text.Length");
    }

    [Test]
    public void SymbolicQueryExecutor_UnsupportedStringTransformLengthsRemainUnknown()
    {
        const string source = @"
public class TestClass
{
    public int ReplaceLength(string text, string oldValue, string newValue)
    {
        if (text != null && oldValue != null && oldValue.Length > 0 && newValue != null)
        {
            return text.Replace(oldValue, newValue).Length;
        }

        return 0;
    }

    public int TrimLength(string text)
    {
        if (text != null)
        {
            return text.Trim().Length;
        }

        return 0;
    }
}";

        AssertConditionUnknown(
            source,
            "return text.Replace(oldValue, newValue).Length;",
            "text.Replace(oldValue, newValue).Length == text.Length");
        AssertConditionUnknown(
            source,
            "return text.Trim().Length;",
            "text.Trim().Length == text.Length");
    }

    private static void AssertConditionProven(string source, string sourceLine, string condition)
    {
        var proof = ProveCondition(source, sourceLine, condition);

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
    }

    private static void AssertConditionUnknown(string source, string sourceLine, string condition)
    {
        var proof = ProveCondition(source, sourceLine, condition);

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
    }

    private static SymbolicConditionProofResult ProveCondition(string source, string sourceLine, string condition)
    {
        return new SymbolicQueryExecutor().ProveConditionAtSource(
            source,
            "StringLengthSmtTests.cs",
            SemanticOracleSmtTests.FindLine(source, sourceLine),
            20,
            condition,
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());
    }

    private static bool IsConditionAlwaysFalse(string parameterList, string conditionExpression)
    {
        var context = AnalyzerTestHost.CreateConditionContext(parameterList, conditionExpression);
        var method = typeof(SharpProofAnalyzer).Assembly
            .GetType("SharpProof.Analyzer.Engine.ExecutionVisibility", true)!
            .GetMethod("IsConditionAlwaysFalseUsingSmt",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

        return (bool)method.Invoke(null,
            new object?[] { context.Expression, context.SemanticModel, CancellationToken.None, null })!;
    }
}
