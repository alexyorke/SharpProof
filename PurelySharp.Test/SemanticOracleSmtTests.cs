using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using PurelySharp.Analyzer;
using PurelySharp.Analyzer.Engine.Symbolic;
using PurelySharp.Test.Smt;
using SearchLib.Smt;


namespace PurelySharp.Test
{
    [TestFixture]
    public class SemanticOracleSmtTests
    {
        private const string SourcePredicateSource = @"
public static class SourcePredicates
{
    public static bool IsNullOrEmptyLike(string value)
    {
        return value == null || value.Length == 0;
    }

    public static bool HasText(string value) => value != null && value.Length > 0;

    public static bool InRange(int value)
    {
        return value >= 10 && value <= 20;
    }

    public static bool IsPositive(int value) => value > 0;
}
";

        [Test]
        public void Oracle_ContradictoryIntegerCondition_IsUnsatisfiable()
        {
            var context = AnalyzerTestHost.CreateConditionContext("int x", "x > 0 && x < 0");
            using var oracle = new SmtPathOracle();

            Assert.That(
                oracle.IsSatisfiable(context.Expression, context.SemanticModel, TimeSpan.FromMilliseconds(50)),
                Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void Oracle_ContradictoryUlongCondition_IsUnsatisfiable()
        {
            var context = AnalyzerTestHost.CreateConditionContext("ulong x", "x == 0UL && x != 0UL");
            using var oracle = new SmtPathOracle();

            Assert.That(
                oracle.IsSatisfiable(context.Expression, context.SemanticModel, TimeSpan.FromMilliseconds(50)),
                Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void Oracle_AffineContradictoryIntegerCondition_IsUnsatisfiable()
        {
            var context = AnalyzerTestHost.CreateConditionContext("int x", "x + 1 <= 0 && x >= 0");
            using var oracle = new SmtPathOracle();

            Assert.That(
                oracle.IsSatisfiable(context.Expression, context.SemanticModel, TimeSpan.FromMilliseconds(50)),
                Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void Oracle_ConstantMultiplicationContradiction_IsUnsatisfiable()
        {
            var context = AnalyzerTestHost.CreateConditionContext("int x", "x * 2 == 6 && x == 4");
            using var oracle = new SmtPathOracle();

            Assert.That(
                oracle.IsSatisfiable(context.Expression, context.SemanticModel, TimeSpan.FromMilliseconds(50)),
                Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void Oracle_ConditionalExpressionContradiction_IsUnsatisfiable()
        {
            var context = AnalyzerTestHost.CreateConditionContext("bool flag, int x, int y", "(flag ? x : y) == 10 && flag && x != 10");
            using var oracle = new SmtPathOracle();

            Assert.That(
                oracle.IsSatisfiable(context.Expression, context.SemanticModel, TimeSpan.FromMilliseconds(250)),
                Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void Oracle_CoalesceExpressionContradiction_IsUnsatisfiable()
        {
            var context = AnalyzerTestHost.CreateConditionContext("string value, string fallback", "(value ?? fallback) == null && value != null");
            using var oracle = new SmtPathOracle();

            Assert.That(
                oracle.IsSatisfiable(context.Expression, context.SemanticModel, TimeSpan.FromMilliseconds(250)),
                Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void Oracle_NullGuardImpliesNotNullComparison()
        {
            var context = AnalyzerTestHost.CreateConditionImplicationContext("string s", "s != null", "s != null");
            using var oracle = new SmtPathOracle();

            Assert.That(
                oracle.Implies(context.PathCondition, context.Conclusion, context.SemanticModel, TimeSpan.FromMilliseconds(50)),
                Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void Oracle_DisjunctiveNonZeroGuard_ImpliesNotZero()
        {
            var context = AnalyzerTestHost.CreateConditionImplicationContext("int divisor", "divisor < 0 || divisor > 0", "divisor != 0");
            using var oracle = new SmtPathOracle();

            Assert.That(
                oracle.Implies(context.PathCondition, context.Conclusion, context.SemanticModel, TimeSpan.FromMilliseconds(50)),
                Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void Oracle_AffineGuardImpliesNonZero()
        {
            var context = AnalyzerTestHost.CreateConditionImplicationContext(
                "int divisor",
                "divisor - 1 >= 0 || divisor + 1 <= 0",
                "divisor != 0");
            using var oracle = new SmtPathOracle();

            Assert.That(
                oracle.Implies(context.PathCondition, context.Conclusion, context.SemanticModel, TimeSpan.FromMilliseconds(50)),
                Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void ExecutionVisibility_TautologicalCondition_IsAlwaysTrue()
        {
            Assert.That(
                IsConditionAlwaysTrue("int x", "x >= 0 || x <= 0"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_AffineContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("int x", "x + 1 <= 0 && x >= 0"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_WhileFalseBody_IsStaticallyUnreachable()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod()
    {
        while (1 > 2)
        {
            return 1;
        }

        return 0;
    }
}",
                    "return 1;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_ForFalseBody_IsStaticallyUnreachable()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod()
    {
        for (var index = 0; index < 0; index++)
        {
            return 1;
        }

        return 0;
    }
}",
                    "return 1;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_ForInitializerReassignment_UsesLatestFact()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod()
    {
        int index;
        for (index = 0, index = 1; index == 1; index++)
        {
            return 1;
        }

        return 0;
    }
}",
                    "return 1;"),
                Is.False);
        }

        [Test]
        public void ExecutionVisibility_PriorLocalAssignment_PrunesUnreachableIfBranch()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod()
    {
        var value = 0;
        if (value != 0)
        {
            return 1;
        }

        return 0;
    }
}",
                    "return 1;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_PriorLocalAssignment_PrunesUnreachableElseBranch()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod()
    {
        var value = 0;
        if (value == 0)
        {
            return 1;
        }
        else
        {
            return 2;
        }
    }
}",
                    "return 2;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_PriorReassignment_UsesLatestFact()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod()
    {
        var value = 0;
        value = 1;
        if (value == 0)
        {
            return 1;
        }

        return 0;
    }
}",
                    "return 1;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_MutationBeforeBranch_InvalidatesPriorFact()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod()
    {
        var value = 0;
        value++;
        if (value != 0)
        {
            return 1;
        }

        return 0;
    }
}",
                    "return 1;"),
                Is.False);
        }

        [Test]
        public void ExecutionVisibility_LoopBodyMutation_InvalidatesPreLoopFact()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod(bool keepGoing)
    {
        var value = 0;
        while (keepGoing)
        {
            if (value != 0)
            {
                return 1;
            }

            value = 1;
        }

        return 0;
    }
}",
                    "return 1;"),
                Is.False);
        }

        [Test]
        public void ExecutionVisibility_LoopWithoutMutation_PreservesPreLoopFact()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod(bool keepGoing)
    {
        var value = 0;
        while (keepGoing)
        {
            if (value != 0)
            {
                return 1;
            }
        }

        return 0;
    }
}",
                    "return 1;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_ArrayCreationLength_PrunesUnreachableBranch()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[0];
        if (values.Length > 0)
        {
            return 1;
        }

        return 0;
    }
}",
                    "return 1;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_ImplicitArrayLength_PrunesUnreachableBranch()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new[] { 1, 2 };
        if (values.Length != 2)
        {
            return 1;
        }

        return 0;
    }
}",
                    "return 1;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_StringLiteralLength_PrunesUnreachableBranch()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod()
    {
        var text = ""abc"";
        if (text.Length < 3)
        {
            return 1;
        }

        return 0;
    }
}",
                    "return 1;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_LengthReassignment_UsesLatestFact()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[1];
        values = new int[2];
        if (values.Length == 1)
        {
            return 1;
        }

        return 0;
    }
}",
                    "return 1;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_LoopArrayMutation_InvalidatesPreLoopLengthFact()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod(bool keepGoing)
    {
        var values = new int[0];
        while (keepGoing)
        {
            if (values.Length > 0)
            {
                return 1;
            }

            values = new int[1];
        }

        return 0;
    }
}",
                    "return 1;"),
                Is.False);
        }

        [Test]
        public void ExecutionVisibility_LoopWithoutArrayMutation_PreservesPreLoopLengthFact()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod(bool keepGoing)
    {
        var values = new int[0];
        while (keepGoing)
        {
            if (values.Length > 0)
            {
                return 1;
            }
        }

        return 0;
    }
}",
                    "return 1;"),
                Is.True);
        }

        [Test]
        public void SymbolicProgramPointFacts_CollectPriorAssignmentFacts_ReturnsReusableFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[2];
        if (values.Length != 2)
        {
            return 1;
        }

        return 0;
    }
}",
                "if (values.Length != 2)");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Length", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsAncestorReachabilityFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values.Length == 2)
        {
            return values[0];
        }

        return 0;
    }
}",
                "return values[0];");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Length", StringComparison.Ordinal) &&
                                           fact.Contains("2", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsPriorEarlyExitGuardFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (index < 0 || index >= values.Length)
        {
            return 0;
        }

        return values[index];
    }
}",
                "return values[index];");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("LessThan", StringComparison.Ordinal)), Is.True);
            Assert.That(facts.Any(fact => fact.Contains("GreaterThanOrEqual", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsSwitchStatementSectionFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 2:
                return value;
            default:
                return 0;
        }
    }
}",
                "return value;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("2", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsSwitchExpressionArmFacts()
        {
            var facts = CollectExpressionProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value switch
        {
            > 10 when value < 20 => value + 1,
            _ => 0
        };
    }
}",
                "value + 1");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("GreaterThan", StringComparison.Ordinal) &&
                                           fact.Contains("10", StringComparison.Ordinal)), Is.True);
            Assert.That(facts.Any(fact => fact.Contains("LessThan", StringComparison.Ordinal) &&
                                           fact.Contains("20", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsCoalesceRightNullFact()
        {
            var facts = CollectExpressionProgramPointFacts(
                @"
public class TestClass
{
    public string TestMethod(string first, string second)
    {
        return first ?? second;
    }
}",
                "second");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("first", StringComparison.Ordinal) &&
                                           fact.Contains("Null", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsConditionalAccessNonNullFact()
        {
            var facts = CollectExpressionProgramPointFacts(
                @"
public class TestClass
{
    public int? TestMethod(string value)
    {
        return value?.Length;
    }
}",
                "Length");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("NotEqual", StringComparison.Ordinal) &&
                                           fact.Contains("value", StringComparison.Ordinal) &&
                                           fact.Contains("Null", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void ExecutionVisibility_UlongZeroContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("ulong x", "x == 0UL && x != 0UL"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_ConditionalExpressionContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("bool flag, int x, int y", "(flag ? x : y) == 5 && flag && x != 5"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_PropertyPatternContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "text is { Length: > 3 } && text.Length <= 3"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_ArrayEmptyListPatternContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("int[] values", "values is [] && values.Length > 0"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_ArrayNonEmptyListPatternContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("int[] values", "values is [_, ..] && values.Length == 0"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_ArrayConstrainedNonEmptyListPatternContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("int[] values", "values is [0, ..] && values.Length == 0"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_StringListPatternExactLengthContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "text is [_, _] && text.Length != 2"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_ArrayLengthNegative_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("int[] values", "values.Length < 0"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_UnsignedCastBoundsCheckImpliesNonNegativeIndex()
        {
            Assert.That(
                IsConditionAlwaysFalse("int[] values, int index", "(uint)index < (uint)values.Length && index < 0"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_UnsignedCastBoundsCheckImpliesUpperBound()
        {
            Assert.That(
                IsConditionAlwaysFalse("int[] values, int index", "(uint)index < (uint)values.Length && index >= values.Length"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_UnsignedCastBoundsCheckFalseBranchImpliesOutOfRange()
        {
            Assert.That(
                IsConditionAlwaysFalse("int[] values, int index", "!((uint)index < (uint)values.Length) && index >= 0 && index < values.Length"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_UnsignedCastUpperBoundGuardImpliesOutOfRange()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text, int index", "(uint)index >= (uint)text.Length && index >= 0 && index < text.Length"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_StringLengthNegative_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "text.Length < 0"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_CustomLengthNegative_RemainsUnknown()
        {
            Assert.That(
                IsConditionAlwaysFalse("HasLength value", "value.Length < 0", "public sealed class HasLength { public int Length => -1; }"),
                Is.False);
        }

        [Test]
        public void ExecutionVisibility_SourceNullOrEmptyPredicateTrueBranchLengthContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "SourcePredicates.IsNullOrEmptyLike(text) && text != null && text.Length > 0", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourceNullOrEmptyPredicateFalseBranchLengthContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "!SourcePredicates.IsNullOrEmptyLike(text) && text.Length <= 0", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourceRangePredicateContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("int value", "SourcePredicates.InRange(value) && (value < 10 || value > 20)", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourcePositivePredicateArgumentExpression_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("int value", "SourcePredicates.IsPositive(value + 1) && value < -1", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourcePositivePredicateReachable_RemainsUnknown()
        {
            Assert.That(
                IsConditionAlwaysFalse("int value", "SourcePredicates.IsPositive(value) && value > 10", SourcePredicateSource),
                Is.False);
        }

        [Test]
        public void ExecutionVisibility_SourceRangePredicateConstant_IsAlwaysTrue()
        {
            Assert.That(
                IsConditionAlwaysTrue("", "SourcePredicates.InRange(15)", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourceHasTextPredicateNullContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "SourcePredicates.HasText(text) && text == null", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourceHasTextPredicateLengthContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "SourcePredicates.HasText(text) && text.Length <= 0", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_StringLiteralLengthContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("", "\"abc\".Length != 3"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_StringEmptyLengthContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("", "string.Empty.Length > 0"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourceNullOrEmptyPredicateNestedInNegation_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "!(SourcePredicates.IsNullOrEmptyLike(text)) && text.Length <= 0", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourceHasTextPredicateInOrFalseBranch_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "!(SourcePredicates.HasText(text) || false) && text.Length > 0 && text != null", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourceNullOrEmptyPredicateReachable_RemainsUnknown()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "SourcePredicates.IsNullOrEmptyLike(text) && text != null && text.Length == 0", SourcePredicateSource),
                Is.False);
        }

        [Test]
        public void ExecutionVisibility_DeclarationPatternImpliesNonNull_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("object value", "value is string && value == null"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_BooleanVariableContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("bool ready", "ready && !ready"),
                Is.True);
        }

        [Test]
        public void SmtConfiguration_BoundedDefaults_UseExpandedBudgets()
        {
            var options = ReadSmtOptions(ImmutableDictionary<string, string>.Empty);

            Assert.That(options.Mode, Is.EqualTo("Bounded"));
            Assert.That(options.TimeoutMs, Is.EqualTo(750));
            Assert.That(options.MethodBudgetMs, Is.EqualTo(5000));
            Assert.That(options.MaxPathConditions, Is.EqualTo(192));
            Assert.That(options.MaxExpressionNodes, Is.EqualTo(2048));
        }

        [Test]
        public void SmtConfiguration_DeepMode_UsesDeepFallbackBudgets()
        {
            var options = ReadSmtOptions(
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_smt_mode", "deep"));

            Assert.That(options.Mode, Is.EqualTo("Deep"));
            Assert.That(options.TimeoutMs, Is.EqualTo(2000));
            Assert.That(options.MethodBudgetMs, Is.EqualTo(15000));
            Assert.That(options.MaxPathConditions, Is.EqualTo(512));
            Assert.That(options.MaxExpressionNodes, Is.EqualTo(8192));
        }

        [Test]
        public void SmtConfiguration_ExplicitOverrides_WinOverDeepFallbacks()
        {
            var options = ReadSmtOptions(
                ImmutableDictionary<string, string>.Empty
                    .Add("purelysharp_smt_mode", "deep")
                    .Add("purelysharp_smt_timeout_ms", "321")
                    .Add("purelysharp_smt_method_budget_ms", "4321")
                    .Add("purelysharp_smt_max_path_conditions", "123")
                    .Add("purelysharp_smt_max_expression_nodes", "4567"));

            Assert.That(options.Mode, Is.EqualTo("Deep"));
            Assert.That(options.TimeoutMs, Is.EqualTo(321));
            Assert.That(options.MethodBudgetMs, Is.EqualTo(4321));
            Assert.That(options.MaxPathConditions, Is.EqualTo(123));
            Assert.That(options.MaxExpressionNodes, Is.EqualTo(4567));
        }

        [Test]
        public void SmtConfiguration_OffMode_DisablesService()
        {
            var options = ReadSmtOptions(
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_smt_mode", "off"));

            Assert.That(options.Mode, Is.EqualTo("Off"));
            Assert.That(options.IsEnabled, Is.False);
        }

        [Test]
        public async Task Ps0002_ContradictoryGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsConditionAlwaysFalse("int x", "x > 0 && x < 0"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        if (x > 0 && x < 0)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ConditionalExpressionContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(bool flag, int x, int y)
    {
        if ((flag ? x : y) == 5 && flag && x != 5)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_PropertyPatternContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (text is { Length: > 3 } && text.Length <= 3)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_TypePatternContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(object value)
    {
        if (value is string && value == null)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_AffineContradictoryGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsConditionAlwaysFalse("int x", "x + 1 <= 0 && x >= 0"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        if (x + 1 <= 0 && x >= 0)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ReassignedLocalDoesNotReuseStalePathFact_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        if (x > 0)
        {
            x = -1;
            if (x < 0)
            {
                Console.WriteLine(x);
            }
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_LocalInitializerContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var x = 0;
        if (x != 0)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_LocalAssignmentContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int x;
        x = 5;
        if (x != 5)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_BooleanAssignmentContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var ready = true;
        if (!ready)
        {
            Console.WriteLine();
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ParameterAssignmentContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        x = 1;
        if (x != 1)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_UlongZeroContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(ulong value)
    {
        if (value == 0UL && value != 0UL)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_LargeUlongConstantGuard_RemainsConservativeReports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(ulong value)
    {
        if (value == 18446744073709551615UL && value == 0UL)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_NullAssignmentContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        string value = null;
        if (value != null)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_LocalAssignmentReachableGuard_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var x = 0;
        x = 1;
        if (x == 1)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_ArrayCreationLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var values = new int[0];
        if (values.Length > 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ArrayCreationLengthReachableGuard_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var values = new int[1];
        if (values.Length > 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_SymbolicArrayLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int length)
    {
        var values = new int[length];
        if (values.Length != length)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ArrayInitializerLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var values = new[] { 1, 2 };
        if (values.Length != 2)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ArrayCollectionExpressionLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int[] values = [1, 2, 3];
        if (values.Length != 3)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ArrayCollectionExpressionSpreadLength_RemainsConservativeReports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] input)
    {
        int[] values = [.. input, 1];
        if (values.Length == 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_ArrayEmptyLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var values = Array.Empty<int>();
        if (values.Length != 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ArrayAliasLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int length)
    {
        var values = new int[length];
        var alias = values;
        if (alias.Length != length)
        {
            Console.WriteLine(alias.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_StringLiteralLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var text = ""abc"";
        if (text.Length != 3)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_DirectStringLiteralLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        if (""abc"".Length != 3)
        {
            Console.WriteLine();
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_StringEmptyLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var text = string.Empty;
        if (text.Length > 0)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_StringEmptyLengthReachableGuard_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var text = string.Empty;
        if (text.Length == 0)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_StringAliasLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string input)
    {
        var text = input;
        var alias = text;
        if (alias.Length != input.Length)
        {
            Console.WriteLine(alias);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_StringLiteralLengthReachableGuard_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var text = ""abc"";
        if (text.Length == 3)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_ArrayLengthFactInvalidatedAfterReassignment_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] input)
    {
        var values = new int[0];
        values = input;
        if (values.Length > 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_DisjunctiveContradictoryGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsConditionAlwaysFalse("int x", "(x == 0 || x == 1) && x != 0 && x != 1"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        if ((x == 0 || x == 1) && x != 0 && x != 1)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_EarlyExitGuardContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values, int index)
    {
        if (index < 0 || index >= values.Length)
        {
            return;
        }

        if (index < 0 || index >= values.Length)
        {
            Console.WriteLine(index);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ContradictoryNullPatternGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsConditionAlwaysFalse("string value", "(value is null) && (value is not null)"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string value)
    {
        if ((value is null) && (value is not null))
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ContradictoryRelationalPatternGuardedImpureCall_DoesNotReport()
        {
            Assert.That(
                IsConditionAlwaysFalse("int x", "x is > 0 and < 0"),
                Is.True);

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        if (x is > 0 and < 0)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ArrayListPatternContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        if (values is [] && values.Length > 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ArrayListPatternReachableGuard_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        if (values is [_, ..] && values.Length > 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_ArrayLengthNegativeGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        if (values.Length < 0)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_StringLengthNegativeGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (text.Length < 0)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SourceNullOrEmptyPredicateTrueBranchContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (SourcePredicates.IsNullOrEmptyLike(text) && text != null && text.Length > 0)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SourceNullOrEmptyPredicateFalseBranchContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (!SourcePredicates.IsNullOrEmptyLike(text) && text.Length <= 0)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SourceNullOrEmptyPredicateReachableImpureCall_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (SourcePredicates.IsNullOrEmptyLike(text))
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_SourceHasTextPredicateLengthContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (SourcePredicates.HasText(text) && text.Length <= 0)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SourceHasTextPredicateNullContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (SourcePredicates.HasText(text) && text == null)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_MetadataStringPredicateContradictoryBranch_RemainsConservativeReports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text)
    {
        if (string.IsNullOrEmpty(text) && text != null && text.Length > 0)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_CustomLengthNegativeGuard_RemainsConservativeReports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public sealed class HasLength
{
    public int Length => -1;
}

public class TestClass
{
    [EnforcePure]
    public void TestMethod(HasLength value)
    {
        if (value.Length < 0)
        {
            Console.WriteLine(value.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_SwitchExpressionArrayLengthNegativeArm_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int[] values)
    {
        return values.Length switch
        {
            < 0 => Console.ReadLine(),
            _ => string.Empty
        };
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ElementConstrainedListPatternFalseBranchRemainsReachable_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        if (values is not [1] && values.Length == 1)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_SwitchStatementContradictoryPatternGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        switch (x)
        {
            case > 0 when x < 0:
                Console.WriteLine(x);
                break;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SwitchStatementReachablePatternGuardedImpureCall_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        switch (x)
        {
            case > 0 when x > 0:
                Console.WriteLine(x);
                break;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_SwitchStatementContradictoryConstantCaseGuard_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        switch (x)
        {
            case 0 when x != 0:
                Console.WriteLine(x);
                break;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SwitchExpressionContradictoryPatternGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int x)
    {
        return x switch
        {
            > 0 when x < 0 => Console.ReadLine(),
            _ => string.Empty
        };
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SwitchExpressionReachablePatternGuardedImpureCall_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int x)
    {
        return x switch
        {
            > 0 when x > 0 => Console.ReadLine(),
            _ => string.Empty
        };
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_PartialConjunctiveGuardFeedsNestedContradiction_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x, string text)
    {
        if (x > 0 && text.Length >= 0)
        {
            if (x < 0)
            {
                Console.WriteLine(x);
            }
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SatisfiableGuardedImpureCall_ReportsStructuredEvidence()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int x)
    {
        if (x >= 0 && x <= 0)
        {
            Console.WriteLine(x);
        }
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(
                diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty],
                Is.AnyOf("catalog_hit", "impure_callee", "unknown_external_call"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(
                diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty],
                Is.AnyOf("Invocation", "InvocationExpression"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0010_ContradictoryGuardedThrow_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(int x)
    {
        if (x > 0 && x < 0)
        {
            throw new InvalidOperationException();
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SatisfiableGuardedThrow_ReportsDirectThrow()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(int x)
    {
        if (x >= 0 && x <= 0)
        {
            throw new InvalidOperationException();
        }
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.InvalidOperationException=direct_throw:throw"));
        }

        [Test]
        public async Task Ps0010_GuardImpliesZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor == 0)
        {
            return value / divisor;
        }

        return 0;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_UlongGuardImpliesZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public ulong TestMethod(ulong value, ulong divisor)
    {
        if (divisor == 0UL)
        {
            return value / divisor;
        }

        return 0UL;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_UlongGuardImpliesZeroModulo_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public ulong TestMethod(ulong value, ulong divisor)
    {
        if (divisor == 0UL)
        {
            return value % divisor;
        }

        return 0UL;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_AffineGuardImpliesZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor + 1 == 1)
        {
            return value / divisor;
        }

        return 0;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_RelationalPatternExactZero_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor is <= 0 and >= 0)
        {
            return value / divisor;
        }

        return 0;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_AssignedZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        var divisor = 0;
        return value / divisor;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_GuardExcludesZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor != 0)
        {
            return value / divisor;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_AssignedNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        var divisor = 1;
        return value / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_PartialConjunctiveGuardExcludesZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor != 0 && IsReady())
        {
            return value / divisor;
        }

        return 0;
    }

    private static bool IsReady()
    {
        return DateTime.UtcNow.Ticks >= 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_AffineGuardExcludesZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor - 1 >= 0 || divisor + 1 <= 0)
        {
            return value / divisor;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_DisjunctiveGuardExcludesZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor < 0 || divisor > 0)
        {
            return value / divisor;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_RelationalPatternNonZero_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor is < 0 or > 0)
        {
            return value / divisor;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_EmptyListPatternIndex_ReportsIndexOutOfRange()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [])
        {
            return values[0];
        }

        return 0;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_NonEmptyListPatternIndex_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [_, ..])
        {
            return values[0];
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ConstrainedNonEmptyListPatternIndex_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [0, ..])
        {
            return values[0];
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_GuardImpliesNullReceiver_ReportsNullReference()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (value == null)
        {
            return value.Length;
        }

        return 0;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.NullReferenceException=definite_null_dereference:null_receiver"));
        }

        [Test]
        public async Task Ps0010_CoalesceRightImpliesNullReceiver_ReportsNullReference()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public string TestMethod(string value)
    {
        return value ?? value.ToString();
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
        }

        [Test]
        public async Task Ps0010_CoalesceRightAssignmentBeforeUse_RemainsConservativeDoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public string TestMethod(string value)
    {
        return value ?? (value = ""safe"").ToString();
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ConditionalExpressionTrueBranchImpliesNullReceiver_ReportsNullReference()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        return value == null ? value.Length : 0;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
        }

        [Test]
        public async Task Ps0010_TypePatternExcludesNullReceiver_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(object value)
    {
        if (value is string text)
        {
            return text.Length;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_PartialConjunctiveGuardImpliesNullReceiver_ReportsNullReference()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod(string value)
    {
        if (value == null && IsReady())
        {
            return value.Length;
        }

        return 0;
    }

    private static bool IsReady()
    {
        return DateTime.UtcNow.Ticks >= 0;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
        }

        [Test]
        public async Task Ps0010_GuardExcludesNullReceiver_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (value != null)
        {
            return value.Length;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NegatedGuardExcludesNullReceiver_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (!(value == null))
        {
            return value.Length;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CatchFilterTautology_SuppressesException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(int x)
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (x == x)
        {
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_CatchFilterContradiction_DoesNotSuppressException()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(int x)
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (x != x)
        {
        }
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_CatchFilterUnknown_RemainsConservative()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (ShouldCatch())
        {
        }
    }

    private static bool ShouldCatch()
    {
        return DateTime.UtcNow.Ticks >= 0;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        private static Task<ImmutableArray<Diagnostic>> GetExceptionDiagnosticsAsync(string source)
        {
            return AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));
        }

        private static bool IsConditionAlwaysFalse(string parameterList, string conditionExpression, string extraSource = "")
        {
            var context = AnalyzerTestHost.CreateConditionContext(parameterList, conditionExpression, extraSource);
            var method = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer).Assembly
                .GetType("PurelySharp.Analyzer.Engine.ExecutionVisibility", throwOnError: true)!
                .GetMethod("IsConditionAlwaysFalse", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

            return (bool)method.Invoke(null, new object?[] { context.Expression, context.SemanticModel, CancellationToken.None })!;
        }

        private static bool IsConditionAlwaysTrue(string parameterList, string conditionExpression, string extraSource = "")
        {
            var context = AnalyzerTestHost.CreateConditionContext(parameterList, conditionExpression, extraSource);
            var method = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer).Assembly
                .GetType("PurelySharp.Analyzer.Engine.ExecutionVisibility", throwOnError: true)!
                .GetMethod("IsConditionAlwaysTrue", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

            return (bool)method.Invoke(null, new object?[] { context.Expression, context.SemanticModel, CancellationToken.None })!;
        }

        private static bool IsStatementUnreachable(string source, string statementText)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "StatementReachabilityHost",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var statement = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<StatementSyntax>()
                .Single(node => string.Equals(node.ToString(), statementText, StringComparison.Ordinal));
            var method = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer).Assembly
                .GetType("PurelySharp.Analyzer.Engine.ExecutionVisibility", throwOnError: true)!
                .GetMethod("IsInStaticallyUnreachableBranch", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

            return (bool)method.Invoke(null, new object?[] { statement, semanticModel, CancellationToken.None })!;
        }

        private static string[] CollectProgramPointFacts(string source, string statementPrefix)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ProgramPointFactHost",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var statement = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<StatementSyntax>()
                .Single(node => node.ToString().StartsWith(statementPrefix, StringComparison.Ordinal));
            var snapshot = new SymbolicInvariantService().GetInvariantsAt(statement, semanticModel, CancellationToken.None);

            return snapshot.Facts.ToArray();
        }

        private static string[] CollectExpressionProgramPointFacts(string source, string expressionPrefix)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "SymbolicFactsTest",
                new[] { syntaxTree },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var expression = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ExpressionSyntax>()
                .Single(node => node.ToString().StartsWith(expressionPrefix, StringComparison.Ordinal));
            var snapshot = new SymbolicInvariantService().GetInvariantsAt(expression, semanticModel, CancellationToken.None);

            return snapshot.Facts.ToArray();
        }

        private static SmtOptionsSnapshot ReadSmtOptions(ImmutableDictionary<string, string> globalOptions)
        {
            var analyzerOptions = AnalyzerTestHost.CreateAnalyzerOptions(globalOptions);
            var configurationType = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer).Assembly
                .GetType("PurelySharp.Analyzer.Configuration.AnalyzerConfiguration", throwOnError: true)!;
            var fromOptions = configurationType.GetMethod(
                "FromOptions",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var configuration = fromOptions.Invoke(null, new object?[] { analyzerOptions })!;
            var smtOptions = configurationType.GetProperty("SmtOptions")!.GetValue(configuration)!;
            var smtOptionsType = smtOptions.GetType();
            var queryTimeout = (TimeSpan)smtOptionsType.GetProperty("QueryTimeout")!.GetValue(smtOptions)!;
            var methodBudget = (TimeSpan)smtOptionsType.GetProperty("MethodBudget")!.GetValue(smtOptions)!;

            return new SmtOptionsSnapshot(
                smtOptionsType.GetProperty("Mode")!.GetValue(smtOptions)!.ToString()!,
                (int)queryTimeout.TotalMilliseconds,
                (int)methodBudget.TotalMilliseconds,
                (int)smtOptionsType.GetProperty("MaxPathConditions")!.GetValue(smtOptions)!,
                (int)smtOptionsType.GetProperty("MaxExpressionNodes")!.GetValue(smtOptions)!,
                (bool)smtOptionsType.GetProperty("IsEnabled")!.GetValue(smtOptions)!);
        }

        private readonly record struct SmtOptionsSnapshot(
            string Mode,
            int TimeoutMs,
            int MethodBudgetMs,
            int MaxPathConditions,
            int MaxExpressionNodes,
            bool IsEnabled);
    }
}
