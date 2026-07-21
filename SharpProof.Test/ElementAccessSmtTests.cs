using NUnit.Framework;
using static SharpProof.Test.SymbolicProofTestAssertions;

namespace SharpProof.Test;

[TestFixture]
[Category("SmtHeavy")]
public sealed class ElementAccessSmtTests {
    public sealed record Expectation(string Marker, string Condition, bool Proven = true);

    public sealed record ElementCase(string Source, Expectation[] Expectations);

    private static IEnumerable<TestCaseData> Cases() {
        yield return Case("SymbolicSourceQueryService_ProvesArrayElementAccessThroughAssignedIndex",
            Method("int", "int[] values", "Index index = ^1;\nif (values != null && values.Length > 0)\n{\n    var result = values[index];\n    return result;\n}\n\nreturn 0;", "using System;"),
            Yes("return result;", "result == values[^1]"));
        yield return Case("SymbolicSourceQueryService_ProvesArrayElementAccessThroughImplicitIndexCreation",
            Method("int", "int[] values", "Index index = new(1, true);\nif (values != null && values.Length > 0)\n{\n    var result = values[index];\n    return result;\n}\n\nreturn 0;", "using System;"),
            Yes("return result;", "result == values[^1]"));
        yield return Case("SymbolicSourceQueryService_ProvesAssignedModuloIndexRange",
            Method("int", "int[] values, int hash", "if (values != null && values.Length > 0 && hash >= 0)\n{\n    var index = hash % values.Length;\n    return values[index];\n}\n\nreturn 0;"),
            Yes("return values[index];", "index >= 0 && index < values.Length"));
        yield return Case("SymbolicSourceQueryService_ProvesAssignedAbsModuloIndexRange",
            Method("int", "int[] values, int hash", "if (values != null && values.Length > 0)\n{\n    var index = Math.Abs(hash % values.Length);\n    return values[index];\n}\n\nreturn 0;", "using System;"),
            Yes("return values[index];", "index >= 0 && index < values.Length"));
        yield return Case("SymbolicSourceQueryService_ProvesMultidimensionalArrayElementAccess",
            Method("int", "int[,] values", "var result = values[0, 1];\nreturn result;"),
            Yes("return result;", "result == values[0, 1]"));
        yield return Case("SymbolicSourceQueryService_DifferentMultidimensionalArrayElementRemainsUnknown",
            Method("int", "int[,] values", "var result = values[0, 1];\nreturn result;"),
            No("return result;", "result == values[1, 0]"));
        yield return Case("SymbolicSourceQueryService_ReassignedIndexRemainsUnknown",
            Method("int", "int[] values", "Index index = ^1;\nindex = 0;\nif (values != null && values.Length > 0)\n{\n    var result = values[index];\n    return result;\n}\n\nreturn 0;", "using System;"),
            No("return result;", "result == values[^1]"));
        yield return Case("SymbolicSourceQueryService_UnassignedIndexParameterRemainsUnknown",
            Method("int", "int[] values, Index index", "if (values != null && values.Length > 0)\n{\n    var result = values[index];\n    return result;\n}\n\nreturn 0;", "using System;"),
            No("return result;", "result == values[^1]"));
        yield return Case("SymbolicSourceQueryService_ProvesArrayRangeLengthThroughAssignedRange",
            Method("int", "int[] values", "Range slice = 1..^1;\nif (values != null && values.Length >= 2)\n{\n    var result = values[slice].Length;\n    return result;\n}\n\nreturn 0;", "using System;"),
            Yes("return result;", "result == values.Length - 2"));
        yield return Case("SymbolicSourceQueryService_ProvesArrayRangeLengthThroughImplicitRangeCreation",
            Method("int", "int[] values", "Range slice = new(Index.FromStart(1), Index.FromEnd(1));\nif (values != null && values.Length >= 2)\n{\n    var result = values[slice].Length;\n    return result;\n}\n\nreturn 0;", "using System;"),
            Yes("return result;", "result == values.Length - 2"));
        yield return Case("SymbolicSourceQueryService_ProvesSpanRangeLength",
            Method("int", "Span<int> values", "if (values.Length >= 2)\n{\n    var result = values[1..^1].Length;\n    return result;\n}\n\nreturn 0;", "using System;"),
            Yes("return result;", "result == values.Length - 2"));
        yield return Case("SymbolicSourceQueryService_ProvesArrayAsSpanAndAsMemoryResultLengths",
            SemanticTestSource.Class("public int Tail(int[] values, int start)\n{\n    if (values != null && start >= 0 && start <= values.Length)\n    {\n        return values.AsSpan(start).Length;\n    }\n    return 0;\n}\n\npublic int Window(int[] values, int start, int length)\n{\n    if (values != null && start >= 0 && length >= 0 && start + length <= values.Length)\n    {\n        return values.AsMemory(start, length).Length;\n    }\n    return 0;\n}\n\npublic int RangeWindow(int[] values)\n{\n    if (values != null && values.Length >= 2)\n    {\n        return values.AsSpan(1..^1).Length;\n    }\n    return 0;\n}", "using System;"),
            Yes("return values.AsSpan(start).Length;", "values.AsSpan(start).Length == values.Length - start"),
            Yes("return values.AsMemory(start, length).Length;", "values.AsMemory(start, length).Length == length"),
            Yes("return values.AsSpan(1..^1).Length;", "values.AsSpan(1..^1).Length == values.Length - 2"));
        yield return Case("SymbolicSourceQueryService_ProvesCollectionExpressionSpreadArrayLength",
            Method("int", "int[] values", "if (values != null)\n{\n    int[] copy = [0, .. values, 1];\n    return copy.Length;\n}\n\nreturn 0;"),
            Yes("return copy.Length;", "copy.Length == values.Length + 2"));
        yield return Case("SymbolicSourceQueryService_ProvesCollectionExpressionSpreadCountLength",
            Method("int", "IReadOnlyCollection<int> values", "if (values != null)\n{\n    int[] copy = [0, .. values, 1];\n    return copy.Length;\n}\n\nreturn 0;", "using System.Collections.Generic;"),
            Yes("return copy.Length;", "copy.Length == values.Count + 2"));
        yield return Case("SymbolicSourceQueryService_EnumerableCollectionExpressionSpreadLengthRemainsUnknown",
            Method("int", "IEnumerable<int> values", "if (values != null)\n{\n    int[] copy = [.. values, 1];\n    return copy.Length;\n}\n\nreturn 0;", "using System.Collections.Generic;"),
            No("return copy.Length;", "copy.Length == 1"));
        yield return Case("SymbolicSourceQueryService_ProvesListIndexerRangeThroughCountGuard",
            Method("int", "List<int> values", "if (values.Count > 0)\n{\n    var result = values[0];\n    return result;\n}\n\nreturn 0;", "using System.Collections.Generic;"),
            Yes("return result;", "0 >= 0 && 0 < values.Count"));
        yield return Case("SymbolicSourceQueryService_ProvesIReadOnlyListIndexerRangeThroughAssignedIndex",
            Method("int", "IReadOnlyList<int> values, int hash", "if (values != null && values.Count > 0 && hash >= 0)\n{\n    var index = hash % values.Count;\n    return values[index];\n}\n\nreturn 0;", "using System.Collections.Generic;"),
            Yes("return values[index];", "index >= 0 && index < values.Count"));
        yield return Case("SymbolicSourceQueryService_ProvesIListElementAccessThroughAssignedIndex",
            Method("int", "IList<int> values", "if (values.Count > 0)\n{\n    var result = values[0];\n    return result;\n}\n\nreturn 0;", "using System.Collections.Generic;"),
            Yes("return result;", "result == values[0]"));
        yield return Case("SymbolicSourceQueryService_LinqCountRemainsUnknown",
            Method("int", "IEnumerable<int> values", "if (values.Count() > 0)\n{\n    return values.Count();\n}\n\nreturn 0;", "using System.Collections.Generic;\nusing System.Linq;"),
            No("return values.Count();", "values.Count() > 0"));
        yield return Case("SymbolicSourceQueryService_ProvesSpanElementAccessThroughAssignedIndex",
            Method("int", "Span<int> values", "Index index = ^1;\nif (values.Length > 0)\n{\n    var result = values[index];\n    return result;\n}\n\nreturn 0;", "using System;"),
            Yes("return result;", "result == values[^1]"));
        yield return Case("SymbolicSourceQueryService_ProvesStringListPatternElementBinding",
            Method("char", "string text", "if (text is [var first, ..])\n{\n    return first;\n}\n\nreturn '\\0';"),
            Yes("return first;", "first == text[0]"));
        yield return Case("SymbolicSourceQueryService_ProvesSpanListPatternElementBinding",
            Method("int", "ReadOnlySpan<int> values", "if (values is [var first, ..])\n{\n    return first;\n}\n\nreturn 0;", "using System;"),
            Yes("return first;", "first == values[0]"));
        yield return Case("SymbolicSourceQueryService_ProvesCountBackedListPatternLengthFact",
            Method("int", "IReadOnlyList<int> values", "if (values is [_, ..])\n{\n    return values.Count;\n}\n\nreturn 0;", "using System.Collections.Generic;"),
            Yes("return values.Count;", "values.Count >= 1"));
        yield return Case("SymbolicSourceQueryService_ReassignedRangeUsesLatestKnownAssignment",
            Method("int", "int[] values", "Range slice = 1..^1;\nslice = 0..^0;\nif (values != null && values.Length >= 2)\n{\n    var result = values[slice].Length;\n    return result;\n}\n\nreturn 0;", "using System;"),
            Yes("return result;", "result == values.Length"));
        yield return Case("SymbolicSourceQueryService_UnknownReassignedRangeRemainsUnknown",
            Method("int", "int[] values, Range other", "Range slice = 1..^1;\nslice = other;\nif (values != null && values.Length >= 2)\n{\n    var result = values[slice].Length;\n    return result;\n}\n\nreturn 0;", "using System;"),
            No("return result;", "result == values.Length - 2"));
        yield return Case("SymbolicSourceQueryService_RangeMutatedAfterLoopUseRemainsUnknown",
            Method("int", "int[] values, bool repeat", "Range slice = 1..^1;\nwhile (repeat)\n{\n    var result = values[slice].Length;\n    slice = 0..^0;\n}\n\nreturn 0;", "using System;"),
            No("var result = values[slice].Length;", "values[slice].Length == values.Length - 2"));
    }

    [TestCaseSource(nameof(Cases))]
    public void ElementAccessMatrix(ElementCase testCase) {
        foreach (var expectation in testCase.Expectations)
            if (expectation.Proven)
                AssertConditionProven(testCase.Source, expectation.Marker, expectation.Condition);
            else
                AssertConditionUnknown(testCase.Source, expectation.Marker, expectation.Condition);
    }

    private static string Method(string returnType, string parameters, string body, string? usings = null) =>
        SemanticTestSource.Method(returnType, parameters, body, usings);

    private static Expectation Yes(string marker, string condition) => new(marker, condition);

    private static Expectation No(string marker, string condition) => new(marker, condition, false);

    private static TestCaseData Case(string name, string source, params Expectation[] expectations) =>
        new TestCaseData(new ElementCase(source, expectations)).SetName(name);
}
