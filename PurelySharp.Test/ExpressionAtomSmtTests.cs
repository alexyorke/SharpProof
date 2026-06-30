using System;
using NUnit.Framework;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class ExpressionAtomSmtTests
    {
        [Test]
        public void SymbolicSourceQueryService_ProvesConditionalNullableMemberFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag, int? left, int? right)
    {
        if (flag && left.HasValue && left.Value == 5)
        {
            return left.Value;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return left.Value;",
                "(flag ? left : right).HasValue && (flag ? left : right).Value == 5");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesConditionalAccessReferenceNullCheck()
        {
            const string source = @"
public sealed class Holder
{
    public string Text;
}

public class TestClass
{
    public string TestMethod(Holder holder)
    {
        if (holder != null && holder.Text != null)
        {
            return holder?.Text;
        }

        return null;
    }
}";

            AssertConditionProven(
                source,
                "return holder?.Text;",
                "holder?.Text != null");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesConditionalAccessStringEqualityFacts()
        {
            const string source = @"
public sealed class Holder
{
    public string Text;
}

public class TestClass
{
    public int TestMethod(Holder holder)
    {
        if (holder?.Text == ""ABC"")
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "holder != null && holder.Text == \"ABC\"");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesConditionalAccessStringCoalesceLengthFacts()
        {
            const string source = @"
public sealed class Holder
{
    public string Text;
}

public class TestClass
{
    public int TestMethod(Holder holder, string fallback)
    {
        if ((holder?.Text ?? fallback) == ""OK"")
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "(holder?.Text ?? fallback).Length == 2");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesTupleEqualityElementRelation()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod((int A, int B) left, (int A, int B) right)
    {
        if (left == right)
        {
            return left.A;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return left.A;",
                "left.B == right.B");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesIdentityBooleanCastFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        if ((bool)flag)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "flag == true");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesIdentityStringCastLengthFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string text)
    {
        if (text != null && ((string)text).Length == 3)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "text.Length != 4");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesTupleLiteralElementArithmeticFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value, bool flag)
    {
        if ((value + 1, flag).Item1 == 5)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "value == 4");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesCheckedArithmeticAtomFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (checked(value + 1) == 5)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "value == 4");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesUncheckedEnumCastAtomFacts()
        {
            const string source = @"
public enum Mode
{
    None = 0,
    Ready = 1
}

public class TestClass
{
    public int TestMethod(Mode mode)
    {
        if (unchecked((int)mode) == 1)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "mode == Mode.Ready");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesCheckedIndexAtomFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (values != null &&
            index >= 0 &&
            index < values.Length &&
            values[checked(index)] == 7)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "values[index] == 7");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesConditionalTupleElementFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag, (int A, int B) left, (int A, int B) right)
    {
        if ((flag ? left : right).A > 0)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "(flag ? left : right).A != 0");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesEnumConstantComparison()
        {
            const string source = @"
public enum Mode
{
    None = 0,
    Ready = 1
}

public class TestClass
{
    public int TestMethod(Mode mode)
    {
        if (mode == Mode.Ready)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "mode != Mode.None");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesNullableEnumCoalesceComparisonFacts()
        {
            const string source = @"
public enum Mode
{
    None = 0,
    Ready = 1
}

public class TestClass
{
    public int TestMethod(Mode? left, Mode? right)
    {
        if ((left ?? right) == Mode.Ready)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "(left ?? right).HasValue && (left ?? right).Value != Mode.None");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesStringIndexCharAtom()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string text)
    {
        if (text != null && text.Length > 0 && text[0] == 'A')
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "text[0] != 'B'");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesDefaultStaticStringEqualsFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string left, string right)
    {
        if (string.Equals(left, right))
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "left == right");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesDefaultInstanceStringEqualsFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string text)
    {
        if (text != null && text.Equals(""ABC""))
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "text == \"ABC\"");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesDefaultStringContainsFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string text)
    {
        if (text != null && text.Contains(""Z""))
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "text != \"ABC\"");
        }

        [Test]
        public void SymbolicSourceQueryService_DefaultStringStartsWithRemainsConservative()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string text, string prefix)
    {
        if (text != null && prefix != null && text.StartsWith(prefix))
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionUnknown(
                source,
                "return 1;",
                "text.StartsWith(prefix, System.StringComparison.Ordinal)");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesAsExpressionNonNullImpliesSourceNonNull()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(object value)
    {
        if ((value as string) != null)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "value != null");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesIdentityReferenceCastNullRelation()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string text)
    {
        if ((object)text != null)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "text != null");
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesAsExpressionPreservesNullEquality()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string text)
    {
        if ((text as object) == null)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionProven(
                source,
                "return 1;",
                "text == null");
        }

        [Test]
        public void SymbolicSourceQueryService_NonNullObjectDoesNotProveTypeTest()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(object value)
    {
        if (value != null)
        {
            return 1;
        }

        return 0;
    }
}";

            AssertConditionUnknown(
                source,
                "return 1;",
                "value is string");
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
                "ExpressionAtomSmtTests.cs",
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
