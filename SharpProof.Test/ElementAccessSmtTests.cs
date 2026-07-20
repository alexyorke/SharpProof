using NUnit.Framework;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using static SharpProof.Test.SymbolicProofTestAssertions;

namespace SharpProof.Test;

[TestFixture]
[Category("SmtHeavy")]
public sealed class ElementAccessSmtTests
{
    [Test]
    public void SymbolicSourceQueryService_ProvesArrayElementAccessThroughAssignedIndex()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int[] values)
    {
        Index index = ^1;
        if (values != null && values.Length > 0)
        {
            var result = values[index];
            return result;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return result;",
            "result == values[^1]");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesArrayElementAccessThroughImplicitIndexCreation()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int[] values)
    {
        Index index = new(1, true);
        if (values != null && values.Length > 0)
        {
            var result = values[index];
            return result;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return result;",
            "result == values[^1]");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesAssignedModuloIndexRange()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int[] values, int hash)
    {
        if (values != null && values.Length > 0 && hash >= 0)
        {
            var index = hash % values.Length;
            return values[index];
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return values[index];",
            "index >= 0 && index < values.Length");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesAssignedAbsModuloIndexRange()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int hash)
    {
        if (values != null && values.Length > 0)
        {
            var index = Math.Abs(hash % values.Length);
            return values[index];
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return values[index];",
            "index >= 0 && index < values.Length");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesMultidimensionalArrayElementAccess()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int[,] values)
    {
        var result = values[0, 1];
        return result;
    }
}";

        AssertConditionProven(
            source,
            "return result;",
            "result == values[0, 1]");
    }

    [Test]
    public void SymbolicSourceQueryService_DifferentMultidimensionalArrayElementRemainsUnknown()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int[,] values)
    {
        var result = values[0, 1];
        return result;
    }
}";

        AssertConditionUnknown(
            source,
            "return result;",
            "result == values[1, 0]");
    }

    [Test]
    public void SymbolicSourceQueryService_ReassignedIndexRemainsUnknown()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int[] values)
    {
        Index index = ^1;
        index = 0;
        if (values != null && values.Length > 0)
        {
            var result = values[index];
            return result;
        }

        return 0;
    }
}";

        AssertConditionUnknown(
            source,
            "return result;",
            "result == values[^1]");
    }

    [Test]
    public void SymbolicSourceQueryService_UnassignedIndexParameterRemainsUnknown()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, Index index)
    {
        if (values != null && values.Length > 0)
        {
            var result = values[index];
            return result;
        }

        return 0;
    }
}";

        AssertConditionUnknown(
            source,
            "return result;",
            "result == values[^1]");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesArrayRangeLengthThroughAssignedRange()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int[] values)
    {
        Range slice = 1..^1;
        if (values != null && values.Length >= 2)
        {
            var result = values[slice].Length;
            return result;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return result;",
            "result == values.Length - 2");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesArrayRangeLengthThroughImplicitRangeCreation()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int[] values)
    {
        Range slice = new(Index.FromStart(1), Index.FromEnd(1));
        if (values != null && values.Length >= 2)
        {
            var result = values[slice].Length;
            return result;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return result;",
            "result == values.Length - 2");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesSpanRangeLength()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(Span<int> values)
    {
        if (values.Length >= 2)
        {
            var result = values[1..^1].Length;
            return result;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return result;",
            "result == values.Length - 2");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesArrayAsSpanAndAsMemoryResultLengths()
    {
        const string source = @"
using System;

public class TestClass
{
    public int Tail(int[] values, int start)
    {
        if (values != null && start >= 0 && start <= values.Length)
        {
            return values.AsSpan(start).Length;
        }

        return 0;
    }

    public int Window(int[] values, int start, int length)
    {
        if (values != null && start >= 0 && length >= 0 && start + length <= values.Length)
        {
            return values.AsMemory(start, length).Length;
        }

        return 0;
    }

    public int RangeWindow(int[] values)
    {
        if (values != null && values.Length >= 2)
        {
            return values.AsSpan(1..^1).Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return values.AsSpan(start).Length;",
            "values.AsSpan(start).Length == values.Length - start");
        AssertConditionProven(
            source,
            "return values.AsMemory(start, length).Length;",
            "values.AsMemory(start, length).Length == length");
        AssertConditionProven(
            source,
            "return values.AsSpan(1..^1).Length;",
            "values.AsSpan(1..^1).Length == values.Length - 2");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesCollectionExpressionSpreadArrayLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values != null)
        {
            int[] copy = [0, .. values, 1];
            return copy.Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return copy.Length;",
            "copy.Length == values.Length + 2");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesCollectionExpressionSpreadCountLength()
    {
        const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(IReadOnlyCollection<int> values)
    {
        if (values != null)
        {
            int[] copy = [0, .. values, 1];
            return copy.Length;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return copy.Length;",
            "copy.Length == values.Count + 2");
    }

    [Test]
    public void SymbolicSourceQueryService_EnumerableCollectionExpressionSpreadLengthRemainsUnknown()
    {
        const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(IEnumerable<int> values)
    {
        if (values != null)
        {
            int[] copy = [.. values, 1];
            return copy.Length;
        }

        return 0;
    }
}";

        AssertConditionUnknown(
            source,
            "return copy.Length;",
            "copy.Length == 1");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesListIndexerRangeThroughCountGuard()
    {
        const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(List<int> values)
    {
        if (values.Count > 0)
        {
            var result = values[0];
            return result;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return result;",
            "0 >= 0 && 0 < values.Count");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesIReadOnlyListIndexerRangeThroughAssignedIndex()
    {
        const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(IReadOnlyList<int> values, int hash)
    {
        if (values != null && values.Count > 0 && hash >= 0)
        {
            var index = hash % values.Count;
            return values[index];
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return values[index];",
            "index >= 0 && index < values.Count");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesIListElementAccessThroughAssignedIndex()
    {
        const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(IList<int> values)
    {
        if (values.Count > 0)
        {
            var result = values[0];
            return result;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return result;",
            "result == values[0]");
    }

    [Test]
    public void SymbolicSourceQueryService_LinqCountRemainsUnknown()
    {
        const string source = @"
using System.Collections.Generic;
using System.Linq;

public class TestClass
{
    public int TestMethod(IEnumerable<int> values)
    {
        if (values.Count() > 0)
        {
            return values.Count();
        }

        return 0;
    }
}";

        AssertConditionUnknown(
            source,
            "return values.Count();",
            "values.Count() > 0");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesSpanElementAccessThroughAssignedIndex()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(Span<int> values)
    {
        Index index = ^1;
        if (values.Length > 0)
        {
            var result = values[index];
            return result;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return result;",
            "result == values[^1]");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesStringListPatternElementBinding()
    {
        const string source = @"
public class TestClass
{
    public char TestMethod(string text)
    {
        if (text is [var first, ..])
        {
            return first;
        }

        return '\0';
    }
}";

        AssertConditionProven(
            source,
            "return first;",
            "first == text[0]");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesSpanListPatternElementBinding()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(ReadOnlySpan<int> values)
    {
        if (values is [var first, ..])
        {
            return first;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return first;",
            "first == values[0]");
    }

    [Test]
    public void SymbolicSourceQueryService_ProvesCountBackedListPatternLengthFact()
    {
        const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(IReadOnlyList<int> values)
    {
        if (values is [_, ..])
        {
            return values.Count;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return values.Count;",
            "values.Count >= 1");
    }

    [Test]
    public void SymbolicSourceQueryService_ReassignedRangeUsesLatestKnownAssignment()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int[] values)
    {
        Range slice = 1..^1;
        slice = 0..^0;
        if (values != null && values.Length >= 2)
        {
            var result = values[slice].Length;
            return result;
        }

        return 0;
    }
}";

        AssertConditionProven(
            source,
            "return result;",
            "result == values.Length");
    }

    [Test]
    public void SymbolicSourceQueryService_UnknownReassignedRangeRemainsUnknown()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, Range other)
    {
        Range slice = 1..^1;
        slice = other;
        if (values != null && values.Length >= 2)
        {
            var result = values[slice].Length;
            return result;
        }

        return 0;
    }
}";

        AssertConditionUnknown(
            source,
            "return result;",
            "result == values.Length - 2");
    }

    [Test]
    public void SymbolicSourceQueryService_RangeMutatedAfterLoopUseRemainsUnknown()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, bool repeat)
    {
        Range slice = 1..^1;
        while (repeat)
        {
            var result = values[slice].Length;
            slice = 0..^0;
        }

        return 0;
    }
}";

        AssertConditionUnknown(
            source,
            "var result = values[slice].Length;",
            "values[slice].Length == values.Length - 2");
    }

}
