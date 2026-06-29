using System;
using System.Threading;
using NUnit.Framework;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class StringLengthSmtTests
    {
        [Test]
        public void SymbolicSourceQueryService_ProvesStringRemoveStartResultLength()
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
        public void SymbolicSourceQueryService_ProvesStringRemoveRangeResultLength()
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
        public void SymbolicSourceQueryService_ProvesStringInsertResultLength()
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
        public void SymbolicSourceQueryService_ProvesStringPadResultLengths()
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
        public void SymbolicSourceQueryService_UnsupportedStringTransformLengthsRemainUnknown()
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
            return new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "StringLengthSmtTests.cs",
                FindLine(source, sourceLine),
                20,
                condition,
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());
        }

        private static bool IsConditionAlwaysFalse(string parameterList, string conditionExpression)
        {
            var context = AnalyzerTestHost.CreateConditionContext(parameterList, conditionExpression);
            var method = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer).Assembly
                .GetType("PurelySharp.Analyzer.Engine.ExecutionVisibility", throwOnError: true)!
                .GetMethod("IsConditionAlwaysFalse", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

            return (bool)method.Invoke(null, new object?[] { context.Expression, context.SemanticModel, CancellationToken.None })!;
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
