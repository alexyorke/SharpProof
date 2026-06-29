using System;
using NUnit.Framework;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class ExpressionSmtTranslationTests
    {
        [Test]
        public void SymbolicSourceQueryService_ProvesTupleLiteralEqualityElementFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int leftNumber, int rightNumber, bool flag, object leftObject, object rightObject)
    {
        if ((leftNumber, flag, leftObject) == (rightNumber, true, rightObject))
        {
            return leftNumber;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return leftNumber;",
                "leftNumber == rightNumber");
            AssertConditionProven(
                source,
                "return leftNumber;",
                "flag");
            AssertConditionProven(
                source,
                "return leftNumber;",
                "leftObject == rightObject");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesTupleLocalEqualityElementFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int leftNumber, int rightNumber, object leftObject, object rightObject)
    {
        var left = (number: leftNumber, item: leftObject);
        var right = (number: rightNumber, item: rightObject);
        if (left == right)
        {
            return rightNumber;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return rightNumber;",
                "leftNumber == rightNumber");
            AssertConditionProven(
                source,
                "return rightNumber;",
                "leftObject == rightObject");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesTupleInequalityRemainderFact()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int first, int second, int otherFirst, int otherSecond)
    {
        if ((first, second) != (otherFirst, otherSecond) && first == otherFirst)
        {
            return second;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return second;",
                "second != otherSecond");
        }

        [Test]
        public void SymbolicSourceQueryService_OverloadedReferenceTupleElementRemainsUnknown()
        {
            const string source = @"
public sealed class Box
{
    public static bool operator ==(Box? left, Box? right) => true;
    public static bool operator !=(Box? left, Box? right) => false;
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);
    public override int GetHashCode() => 0;
}

public class TestClass
{
    public int TestMethod(Box left, Box right, int x, int y)
    {
        if ((left, x) == (right, y))
        {
            return x;
        }

        return 0;
    }
}";

            AssertConditionUnknown(
                source,
                "return x;",
                "x == y");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesNullableCoalesceValueFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int? left, int? right)
    {
        if ((left ?? right) == 5)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "(left ?? right).HasValue && (left ?? right).Value == 5");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesLiftedNullableBooleanCoalesceFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool? left, bool? right)
    {
        if ((left ?? right) == true)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "(left ?? right).HasValue && (left ?? right).Value");
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
                "ExpressionSmtTranslationTests.cs",
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
