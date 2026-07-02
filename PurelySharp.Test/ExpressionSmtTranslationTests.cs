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

            using var session = CreateSession(source);
            AssertConditionProven(session, "return leftNumber;", "leftNumber == rightNumber");
            AssertConditionProven(session, "return leftNumber;", "flag");
            AssertConditionProven(session, "return leftNumber;", "leftObject == rightObject");
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

            using var session = CreateSession(source);
            AssertConditionProven(session, "return rightNumber;", "leftNumber == rightNumber");
            AssertConditionProven(session, "return rightNumber;", "leftObject == rightObject");
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

            using var session = CreateSession(source);
            AssertConditionProven(session, "return second;", "second != otherSecond");
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

            using var session = CreateSession(source);
            AssertConditionUnknown(session, "return x;", "x == y");
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

            using var session = CreateSession(source);
            AssertConditionProven(session, "return 1;", "(left ?? right).HasValue && (left ?? right).Value == 5");
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

            using var session = CreateSession(source);
            AssertConditionProven(session, "return 1;", "(left ?? right).HasValue && (left ?? right).Value");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesConditionalPropertyPatternArmFacts()
        {
            const string source = @"
public sealed class Box
{
    public int Count { get; set; }
}

public class TestClass
{
    public int TestMethod(Box left, Box right, bool flag)
    {
        if ((flag ? left : right) is { Count: > 0 })
        {
            if (flag)
            {
                return left.Count;
            }

            return right.Count;
        }

        return 0;
    }
}";

            using var session = CreateSession(source);
            AssertConditionProven(session, "return left.Count;", "left != null && left.Count > 0");
            AssertConditionProven(session, "return right.Count;", "right != null && right.Count > 0");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesConditionalReceiverMemberArmFacts()
        {
            const string source = @"
public sealed class Box
{
    public int Count { get; set; }
}

public class TestClass
{
    public int TestMethod(Box left, Box right, bool flag)
    {
        if ((flag ? left : right).Count > 0)
        {
            if (flag)
            {
                return left.Count;
            }

            return right.Count;
        }

        return 0;
    }
}";

            using var session = CreateSession(source);
            AssertConditionProven(session, "return left.Count;", "left.Count > 0");
            AssertConditionProven(session, "return right.Count;", "right.Count > 0");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesCoalesceReceiverMemberArmFacts()
        {
            const string source = @"
public sealed class Box
{
    public int Count { get; set; }
}

public class TestClass
{
    public int TestMethod(Box left, Box right)
    {
        if ((left ?? right).Count == 3)
        {
            if (left != null)
            {
                return left.Count;
            }

            return right.Count;
        }

        return 0;
    }
}";

            using var session = CreateSession(source);
            AssertConditionProven(session, "return left.Count;", "left.Count == 3");
            AssertConditionProven(session, "return right.Count;", "right.Count == 3");
        }

        private static void AssertConditionProven(SymbolicSourceQueryTestSession session, string sourceLine, string condition)
        {
            var proof = ProveCondition(session, sourceLine, condition);

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        private static void AssertConditionUnknown(SymbolicSourceQueryTestSession session, string sourceLine, string condition)
        {
            var proof = ProveCondition(session, sourceLine, condition);

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
        }

        private static SymbolicSourceQueryTestSession CreateSession(string source)
        {
            return new SymbolicSourceQueryTestSession(source, "ExpressionSmtTranslationTests.cs");
        }

        private static SymbolicConditionProofResult ProveCondition(
            SymbolicSourceQueryTestSession session,
            string sourceLine,
            string condition)
        {
            return session.ProveAtMarker(
                (session.FindLine(sourceLine), 20, 0),
                condition);
        }
    }
}
