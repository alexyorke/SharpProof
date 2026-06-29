using System;
using NUnit.Framework;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
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
        public void SymbolicSourceQueryService_ListIndexerRemainsUnknown()
        {
            const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(List<int> values)
    {
        if (values.Count > 0)
        {
            return values[0];
        }

        return 0;
    }
}";

            AssertConditionUnknown(
                source,
                "return values[0];",
                "values[0] >= 0");
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
            return new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ElementAccessSmtTests.cs",
                FindLine(source, sourceLine),
                20,
                condition,
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());
        }

        private static int FindLine(string source, string text)
        {
            var lines = source.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains(text, StringComparison.Ordinal))
                {
                    return index + 1;
                }
            }

            throw new InvalidOperationException("Text was not found in source.");
        }
    }
}
