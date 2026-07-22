using NUnit.Framework;
using static SharpProof.Test.SymbolicProofTestAssertions;

namespace SharpProof.Test;

[TestFixture]
[Category("SmtHeavy")]
public sealed class StringLengthSmtTests : SemanticOracleSmtTestBase {
    public sealed record Expectation(string Marker, string Condition, bool Proven = true);

    public sealed record StringCase(string Source, Expectation[] Expectations);

    private static IEnumerable<TestCaseData> Cases() {
        yield return Case("SymbolicQueryExecutor_ProvesStringRemoveStartResultLength",
            Method("int", "string text, int start",
                "if (text != null && start >= 0 && start <= text.Length)\n{\n    return text.Remove(start).Length;\n}\n\nreturn 0;"),
            Yes("return text.Remove(start).Length;", "text.Remove(start).Length == text.Length - start"));
        yield return Case("SymbolicQueryExecutor_ProvesStringRemoveRangeResultLength",
            Method("int", "string text, int start, int count", "if (text != null && start >= 0 && count >= 0 && start + count <= text.Length)\n{\n    return text.Remove(start, count).Length;\n}\n\nreturn 0;"),
            Yes("return text.Remove(start, count).Length;", "text.Remove(start, count).Length == text.Length - count"));
        yield return Case("SymbolicQueryExecutor_ProvesStringInsertResultLength",
            Method("int", "string text, string value, int index", "if (text != null && value != null && index >= 0 && index <= text.Length)\n{\n    return text.Insert(index, value).Length;\n}\n\nreturn 0;"),
            Yes("return text.Insert(index, value).Length;", "text.Insert(index, value).Length == text.Length + value.Length"));
        yield return Case("SymbolicQueryExecutor_ProvesStringPadResultLengths",
            Class("public int PadLeft(string text, int width)\n{\n    if (text != null && width >= text.Length)\n    {\n        return text.PadLeft(width).Length;\n    }\n    return 0;\n}\n\npublic int PadRight(string text, int width)\n{\n    if (text != null && width >= 0 && width <= text.Length)\n    {\n        return text.PadRight(width, '.').Length;\n    }\n    return 0;\n}"),
            Yes("return text.PadLeft(width).Length;", "text.PadLeft(width).Length == width"),
            Yes("return text.PadRight(width, '.').Length;", "text.PadRight(width, '.').Length == text.Length"));
        yield return Case("SymbolicQueryExecutor_ProvesSpanSliceStartResultLength",
            Method("int", "System.Span<int> span, int start",
                "if (start >= 0 && start <= span.Length)\n{\n    return span.Slice(start).Length;\n}\n\nreturn 0;"),
            Yes("return span.Slice(start).Length;", "span.Slice(start).Length == span.Length - start"));
        yield return Case("SymbolicQueryExecutor_ProvesReadOnlySpanSliceRangeResultLength",
            Method("int", "System.ReadOnlySpan<int> span, int start, int length", "if (start >= 0 && length >= 0 && start + length <= span.Length)\n{\n    return span.Slice(start, length).Length;\n}\n\nreturn 0;"),
            Yes("return span.Slice(start, length).Length;", "span.Slice(start, length).Length == length"));
        yield return Case("SymbolicQueryExecutor_ProvesMemorySliceStartResultLength",
            Method("int", "System.Memory<int> memory, int start",
                "if (start >= 0 && start <= memory.Length)\n{\n    return memory.Slice(start).Length;\n}\n\nreturn 0;"),
            Yes("return memory.Slice(start).Length;", "memory.Slice(start).Length == memory.Length - start"));
        yield return Case("SymbolicQueryExecutor_ProvesReadOnlyMemorySliceRangeResultLength",
            Method("int", "System.ReadOnlyMemory<int> memory, int start, int length", "if (start >= 0 && length >= 0 && start + length <= memory.Length)\n{\n    return memory.Slice(start, length).Length;\n}\n\nreturn 0;"),
            Yes("return memory.Slice(start, length).Length;", "memory.Slice(start, length).Length == length"));
        yield return Case("SymbolicQueryExecutor_ProvesStringAsSpanAndAsMemoryResultLengths",
            Class("public int Tail(string text, int start)\n{\n    if (text != null && start >= 0 && start <= text.Length)\n    {\n        return text.AsSpan(start).Length;\n    }\n    return 0;\n}\n\npublic int Window(string text, int start, int length)\n{\n    if (text != null && start >= 0 && length >= 0 && start + length <= text.Length)\n    {\n        return text.AsMemory(start, length).Length;\n    }\n    return 0;\n}\n\npublic int RangeWindow(string text)\n{\n    if (text != null && text.Length >= 2)\n    {\n        return text.AsSpan(1..^1).Length;\n    }\n    return 0;\n}", "using System;"),
            Yes("return text.AsSpan(start).Length;", "text.AsSpan(start).Length == text.Length - start"),
            Yes("return text.AsMemory(start, length).Length;", "text.AsMemory(start, length).Length == length"),
            Yes("return text.AsSpan(1..^1).Length;", "text.AsSpan(1..^1).Length == text.Length - 2"));
        yield return Case("SymbolicQueryExecutor_ProvesAssignedSpanLengthSnapshot",
            Method("int", "System.Span<int> span", "System.Span<int> copy = span;\nreturn copy.Length;"),
            Yes("return copy.Length;", "copy.Length == span.Length"));
        yield return Case("SymbolicQueryExecutor_ProvesAssignedReadOnlySpanSliceLengthSnapshot",
            Method("int", "System.ReadOnlySpan<int> span, int start, int length", "if (start >= 0 && length >= 0 && start + length <= span.Length)\n{\n    System.ReadOnlySpan<int> window = span.Slice(start, length);\n    return window.Length;\n}\n\nreturn 0;"),
            Yes("return window.Length;", "window.Length == length"));
        yield return Case("SymbolicQueryExecutor_ProvesAssignedMemorySliceLengthSnapshots",
            Class("public int Tail(System.Memory<int> memory, int start)\n{\n    if (start >= 0 && start <= memory.Length)\n    {\n        System.Memory<int> tail = memory.Slice(start);\n        return tail.Length;\n    }\n    return 0;\n}\n\npublic int Window(System.ReadOnlyMemory<int> memory, int start, int length)\n{\n    if (start >= 0 && length >= 0 && start + length <= memory.Length)\n    {\n        System.ReadOnlyMemory<int> window = memory.Slice(start, length);\n        return window.Length;\n    }\n    return 0;\n}"),
            Yes("return tail.Length;", "tail.Length == memory.Length - start"),
            Yes("return window.Length;", "window.Length == length"));
        yield return Case("SymbolicQueryExecutor_UnsupportedSliceStartRemainsUnknown",
            Class("public int TestMethod(System.Span<int> span)\n{\n    if (span.Length > 0)\n    {\n        return span.Slice(GetStart()).Length;\n    }\n    return 0;\n}\n\nprivate int GetStart()\n{\n    return 1;\n}"),
            No("return span.Slice(GetStart()).Length;", "span.Slice(GetStart()).Length == span.Length - GetStart()"));
        yield return Case("SymbolicQueryExecutor_ProvesStringLiteralLengthConstant",
            Method("int", "string text", "if (text != null && text.Length == \"abc\".Length)\n{\n    return text.Length;\n}\n\nreturn 0;"),
            Yes("return text.Length;", "text.Length == 3"));
        yield return Case("SymbolicQueryExecutor_ProvesStringRepeatCreationResultLength",
            Method("int", "int count", "if (count >= 0)\n{\n    return new string('x', count).Length;\n}\n\nreturn 0;"),
            Yes("return new string('x', count).Length;", "new string('x', count).Length == count"));
        yield return Case("SymbolicQueryExecutor_ProvesStringCharArrayCreationResultLengths",
            Class("public int WholeArray(char[] chars)\n{\n    if (chars != null)\n    {\n        return new string(chars).Length;\n    }\n    return 0;\n}\n\npublic int ArrayRange(char[] chars, int start, int length)\n{\n    if (chars != null && start >= 0 && length >= 0 && start + length <= chars.Length)\n    {\n        return new string(chars, start, length).Length;\n    }\n    return 0;\n}\n\npublic int SpanRange(char[] chars, int start, int length)\n{\n    if (chars != null && start >= 0 && length >= 0 && start + length <= chars.Length)\n    {\n        return new string(chars.AsSpan(start, length)).Length;\n    }\n    return 0;\n}", "using System;"),
            Yes("return new string(chars).Length;", "new string(chars).Length == chars.Length"),
            Yes("return new string(chars, start, length).Length;", "new string(chars, start, length).Length == length"),
            Yes("return new string(chars.AsSpan(start, length)).Length;", "new string(chars.AsSpan(start, length)).Length == length"));
        yield return Case("SymbolicQueryExecutor_ProvesStringConcatResultLengths",
            Class("public int FixedConcat(string first, string second)\n{\n    if (first != null && second != null)\n    {\n        return string.Concat(first, \"-\", second).Length;\n    }\n    return 0;\n}\n\npublic int ParamsConcat(string first, string second, string third)\n{\n    if (first != null && second != null && third != null)\n    {\n        return string.Concat(first, \"-\", second, \"-\", third).Length;\n    }\n    return 0;\n}"),
            Yes("return string.Concat(first, \"-\", second).Length;",
                "string.Concat(first, \"-\", second).Length == first.Length + 1 + second.Length"),
            Yes("return string.Concat(first, \"-\", second, \"-\", third).Length;",
                "string.Concat(first, \"-\", second, \"-\", third).Length == first.Length + 1 + second.Length + 1 + third.Length"));
        yield return Case("SymbolicQueryExecutor_ProvesStringInterpolationResultLengthWhenPartsAreStrings",
            Method("int", "string first, string second",
                "if (first != null && second != null)\n{\n    return $\"{first}-{second}\".Length;\n}\n\nreturn 0;"),
            Yes("return $\"{first}-{second}\".Length;", "$\"{first}-{second}\".Length == first.Length + 1 + second.Length"));
        yield return Case("SymbolicQueryExecutor_ProvesStringInterpolationLengthWithNameofConstant",
            Class("public int WithNameof(string text)\n{\n    if (text != null)\n    {\n        return $\"{text}:{nameof(WithNameof)}\".Length;\n    }\n    return 0;\n}"),
            Yes("return $\"{text}:{nameof(WithNameof)}\".Length;",
                "$\"{text}:{nameof(WithNameof)}\".Length == text.Length + 1 + nameof(WithNameof).Length"));
        yield return Case("SymbolicQueryExecutor_UnsupportedFormattedStringConstructionLengthsRemainUnknown",
            Class("public int InterpolationWithNonStringHole(string text, int value)\n{\n    if (text != null)\n    {\n        return $\"{text}:{value}\".Length;\n    }\n    return 0;\n}\n\npublic int ObjectConcat(string text, object value)\n{\n    if (text != null)\n    {\n        return string.Concat(text, value).Length;\n    }\n    return 0;\n}"),
            No("return $\"{text}:{value}\".Length;", "$\"{text}:{value}\".Length == text.Length + 1"),
            No("return string.Concat(text, value).Length;", "string.Concat(text, value).Length == text.Length"));
        yield return Case("SymbolicQueryExecutor_UnsupportedStringTransformLengthsRemainUnknown",
            Class("public int ReplaceLength(string text, string oldValue, string newValue)\n{\n    if (text != null && oldValue != null && oldValue.Length > 0 && newValue != null)\n    {\n        return text.Replace(oldValue, newValue).Length;\n    }\n    return 0;\n}\n\npublic int TrimLength(string text)\n{\n    if (text != null)\n    {\n        return text.Trim().Length;\n    }\n    return 0;\n}"),
            No("return text.Replace(oldValue, newValue).Length;", "text.Replace(oldValue, newValue).Length == text.Length"),
            No("return text.Trim().Length;", "text.Trim().Length == text.Length"));
        yield return Case("SymbolicQueryExecutor_ProvesStringSubstringStartResultLength",
            Method("int", "string text, int start",
                "if (text != null && start >= 0 && start <= text.Length)\n{\n    return text.Substring(start).Length;\n}\n\nreturn 0;"),
            Yes("return text.Substring(start).Length;", "text.Substring(start).Length == text.Length - start"));
        yield return Case("SymbolicQueryExecutor_ProvesStringSubstringRangeResultLength",
            Method("int", "string text, int start, int count", "if (text != null && start >= 0 && count >= 0 && start + count <= text.Length)\n{\n    return text.Substring(start, count).Length;\n}\n\nreturn 0;"),
            Yes("return text.Substring(start, count).Length;", "text.Substring(start, count).Length == count"));
        yield return Case("SymbolicQueryExecutor_ProvesStringSubstringContentIdentity",
            Method("bool", "string text", "if (text != null)\n{\n    return text.Substring(0, text.Length) == text;\n}\n\nreturn false;"),
            Yes("return text.Substring(0, text.Length) == text;", "text.Substring(0, text.Length) == text"));
    }
    [TestCaseSource(nameof(Cases))]
    public void StringLengthMatrix(StringCase testCase) {
        foreach (var expectation in testCase.Expectations)
            if (expectation.Proven)
                AssertConditionProven(testCase.Source, expectation.Marker, expectation.Condition);
            else
                AssertConditionUnknown(testCase.Source, expectation.Marker, expectation.Condition);
    }
    [Test]
    public void ExecutionVisibility_StringInsertLengthContradiction_IsAlwaysFalse() => Assert.That(
        IsConditionAlwaysFalse(
            "string text, string value, int index",
            "text != null && value != null && index >= 0 && index <= text.Length && text.Insert(index, value).Length != text.Length + value.Length"),
        Is.True);

    private static string Method(string returnType, string parameters, string body) =>
        SemanticTestSource.Method(returnType, parameters, body);

    private static string Class(string members, string? usings = null) =>
        SemanticTestSource.Class(members, usings);

    private static Expectation Yes(string marker, string condition) => new(marker, condition);

    private static Expectation No(string marker, string condition) => new(marker, condition, false);

    private static TestCaseData Case(string name, string source, params Expectation[] expectations) =>
        new TestCaseData(new StringCase(source, expectations)).SetName(name);
}
