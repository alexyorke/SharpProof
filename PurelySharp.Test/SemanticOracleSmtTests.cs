using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using PurelySharp.Analyzer;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;
using PurelySharp.Test.Smt;
using SearchLib.Purity;
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

    public static bool HasTextWithGuard(string value)
    {
        if (value == null)
        {
            return false;
        }

        return value.Length > 0;
    }

    public static bool HasTextWithIfElse(string value)
    {
        if (value == null)
        {
            return false;
        }
        else
        {
            return value.Length > 0;
        }
    }

    public static bool HasTextViaLocal(string value)
    {
        var present = value != null;
        var positiveLength = value.Length > 0;
        return present && positiveLength;
    }

    public static bool HasTextViaAssignment(string value)
    {
        bool present;
        bool positiveLength;
        present = value != null;
        positiveLength = value.Length > 0;
        return present && positiveLength;
    }

    public static bool InRange(int value)
    {
        return value >= 10 && value <= 20;
    }

    public static bool IsValidIndex(int[] values, int index)
    {
        if (values == null)
        {
            return false;
        }

        if (index < 0)
        {
            return false;
        }

        return index < values.Length;
    }

    public static bool IsPositive(int value) => value > 0;

    public static bool IsZeroWithGuard(int value)
    {
        if (value != 0)
        {
            return false;
        }

        return true;
    }

    public static bool IsZeroViaLocal(int value)
    {
        var isZero = value == 0;
        return isZero;
    }

    public static bool IsZeroViaAssignment(int value)
    {
        bool isZero;
        isZero = value == 0;
        return isZero;
    }

    public static bool IsPositiveAfterLocalAssignment(int value)
    {
        var adjusted = value;
        adjusted = adjusted + 1;
        return adjusted > 0;
    }

    public static bool IsZeroWithSwitch(int value)
    {
        switch (value)
        {
            case 0:
                return true;
            default:
                return false;
        }
    }

    public static bool IsZeroWithSwitchFallback(int value)
    {
        switch (value)
        {
            case 0:
                return true;
        }

        return false;
    }

    public static bool IsSmallPositiveWithSwitch(int value)
    {
        switch (value)
        {
            case > 0 and < 10:
                return true;
            default:
                return false;
        }
    }
}

public sealed class SourcePredicateBox
{
    public SourcePredicateBox(string value, int divisor)
    {
        Value = value;
        Divisor = divisor;
    }

    public string Value { get; }

    public int Divisor { get; }

    public bool HasText => Value != null && Value.Length > 0;

    public bool HasTextMethod() => Value != null && Value.Length > 0;

    public bool IsZeroDivisor
    {
        get
        {
            var isZero = Divisor == 0;
            return isZero;
        }
    }

    public bool IsZeroDivisorMethod()
    {
        var isZero = Divisor == 0;
        return isZero;
    }
}
";

        [Test]
        public void SymbolicSourceQueryService_QueryFile_RequestApiProvesImplication()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value <= 0)
        {
            return 1;
        }

        return value;
    }
}";
            var filePath = Path.Combine(
                Path.GetTempPath(),
                "PurelySharp.SymbolicFileQuery." + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(filePath, source);

            try
            {
                var query = new SymbolicFileQuery(
                    filePath,
                    FindLine(source, "return value;"),
                    16,
                    AnalyzerTestHost.GetTrustedPlatformReferences(),
                    new[] { "value > 0" });

                var result = new SymbolicSourceQueryService().QueryFile(
                    query,
                    smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

                Assert.That(result.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
            }
            finally
            {
                File.Delete(filePath);
            }
        }

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
        public void CSharpConditionToFormula_ElementAccessInRange_TranslatesFromEndIndex()
        {
            var source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values.Length > 0)
        {
            return values[^1];
        }

        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ElementAccessInRangeHost",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();
            var guard = root.DescendantNodes().OfType<IfStatementSyntax>().Single().Condition;
            var elementAccess = root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();

            Assert.That(
                PurelySharp.Symbolic.Smt.CSharpConditionToFormula.TryTranslateBuiltInElementAccessInRange(
                    elementAccess,
                    semanticModel,
                    CancellationToken.None,
                    out var inRangeFormula),
                Is.True);
            Assert.That(
                PurelySharp.Symbolic.Smt.CSharpConditionToFormula.TryTranslate(
                    guard,
                    semanticModel,
                    CancellationToken.None,
                    out var guardFormula),
                Is.True);
            Assert.That(guardFormula, Is.Not.Null);

            var proof = new SmtAnalysisService(SmtAnalysisOptions.Default)
                .ClassifyImplication(new[] { guardFormula! }, inRangeFormula);

            Assert.That(proof.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
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
        public void ExecutionVisibility_WhileNormalExitCondition_PrunesUnreachableBranch()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        while (index < values.Length)
        {
            index++;
        }

        if (index < values.Length)
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
        public void ExecutionVisibility_WhileBreakExit_RemainsConservative()
        {
            Assert.That(
                IsStatementUnreachable(
                    @"
public class TestClass
{
    public int TestMethod(int[] values, int index, bool stop)
    {
        while (index < values.Length)
        {
            if (stop)
            {
                break;
            }

            index++;
        }

        if (index < values.Length)
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
        public void SymbolicProgramPointFacts_CollectPriorAssignmentFacts_UsesSharedArrayEmptyLengthFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod()
    {
        var values = System.Array.Empty<int>();
        if (values.Length != 0)
        {
            return 1;
        }

        return 0;
    }
}",
                "if (values.Length != 0)");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Length", StringComparison.Ordinal) &&
                                           fact.Contains("0", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsWhileNormalExitConditionFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        while (index < values.Length)
        {
            index++;
        }

        return index;
    }
}",
                "return index;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Not", StringComparison.Ordinal) &&
                                           fact.Contains("LessThan", StringComparison.Ordinal) &&
                                           fact.Contains("Length", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicProgramPointFacts_CollectCompletedLoopExitInvariantFacts_ReturnsForLoopExitFacts()
        {
            var facts = CollectCompletedLoopExitFacts(
                @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        for (; index < values.Length; index++)
        {
        }

        return index;
    }
}",
                "for (; index < values.Length; index++)");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Not", StringComparison.Ordinal) &&
                                           fact.Contains("LessThan", StringComparison.Ordinal) &&
                                           fact.Contains("Length", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicProgramPointFacts_CollectCompletedLoopExitInvariantFacts_SuppressesLoopExitFactsWhenBreakCanExitLoop()
        {
            var facts = CollectCompletedLoopExitFacts(
                @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        while (index < values.Length)
        {
            break;
        }

        return index;
    }
}",
                "while (index < values.Length)");

            Assert.That(facts, Is.Empty);
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
        public void SymbolicInvariantService_CollectsRegexReachabilityFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
using System.Text.RegularExpressions;

public class TestClass
{
    public int TestMethod(string input)
    {
        if (Regex.IsMatch(input, ""^[A-Z][0-9]$""))
        {
            return input.Length;
        }

        return 0;
    }
}",
                "return input.Length;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("SmtRegexMatchFormula", StringComparison.Ordinal) &&
                                           fact.Contains("^[A-Z][0-9]$", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsStringPredicateReachabilityFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(string input)
    {
        if (input.Contains(""SKU""))
        {
            return input.Length;
        }

        return 0;
    }
}",
                "return input.Length;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("SmtStringContainsFormula", StringComparison.Ordinal) &&
                                           fact.Contains("SKU", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsStringConcatAssignmentFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(string prefix)
    {
        var code = prefix + ""-01"";
        return code.Length;
    }
}",
                "return code.Length;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("SmtStringConcatTerm", StringComparison.Ordinal) &&
                                           fact.Contains("-01", StringComparison.Ordinal)), Is.True);
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
        public void SymbolicInvariantService_CollectsBooleanPredicateAliasFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        var inRange = index >= 0 && index < values.Length;
        if (inRange)
        {
            return values[index];
        }

        return 0;
    }
}",
                "return values[index];");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("inRange", StringComparison.Ordinal) &&
                                           fact.Contains("And", StringComparison.Ordinal) &&
                                           fact.Contains("LessThan", StringComparison.Ordinal)), Is.True);
            Assert.That(facts.Any(fact => fact.Contains("inRange", StringComparison.Ordinal)), Is.True);
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
        public void SymbolicInvariantService_CollectsSwitchStatementDefaultExclusionFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 0:
                return 0;
            default:
                return 10 / value;
        }
    }
}",
                "return 10 / value;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Not", StringComparison.Ordinal) &&
                                           fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("0", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsSwitchStatementPatternBindingFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case > 0 and var divisor:
                return 10 / divisor;
            default:
                return 0;
        }
    }
}",
                "return 10 / divisor;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("GreaterThan", StringComparison.Ordinal) &&
                                           fact.Contains("value", StringComparison.Ordinal) &&
                                           fact.Contains("0", StringComparison.Ordinal)), Is.True);
            Assert.That(facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("divisor", StringComparison.Ordinal) &&
                                           fact.Contains("value", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsSwitchStatementPriorSectionExclusionFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 0:
                return 0;
            case var divisor:
                return 10 / divisor;
        }
    }
}",
                "return 10 / divisor;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Not", StringComparison.Ordinal) &&
                                           fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("value", StringComparison.Ordinal) &&
                                           fact.Contains("0", StringComparison.Ordinal)), Is.True);
            Assert.That(facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("divisor", StringComparison.Ordinal) &&
                                           fact.Contains("value", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsSwitchStatementExitingSectionExclusionFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 0:
                return 0;
        }

        return 10 / value;
    }
}",
                "return 10 / value;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Not", StringComparison.Ordinal) &&
                                           fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("value", StringComparison.Ordinal) &&
                                           fact.Contains("0", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_SwitchContinuingMutationSuppressesStaleSectionCondition()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 0:
                return 0;
            default:
                value = 0;
                break;
        }

        return value;
    }
}",
                "return value;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("value", StringComparison.Ordinal) &&
                                           fact.Contains("0", StringComparison.Ordinal)), Is.True);
            Assert.That(facts.Any(fact => fact.Contains("Not", StringComparison.Ordinal) &&
                                           fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("value", StringComparison.Ordinal) &&
                                           fact.Contains("0", StringComparison.Ordinal)), Is.False);
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
        public void SymbolicInvariantService_CollectsSwitchExpressionFallbackExclusionFacts()
        {
            var facts = CollectExpressionProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value switch
        {
            0 => 0,
            _ => 10 / value
        };
    }
}",
                "10 / value");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Not", StringComparison.Ordinal) &&
                                           fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("value", StringComparison.Ordinal) &&
                                           fact.Contains("0", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsSwitchExpressionPatternBindingFacts()
        {
            var facts = CollectExpressionProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value switch
        {
            > 0 and var divisor => 10 / divisor,
            _ => 0
        };
    }
}",
                "10 / divisor");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("GreaterThan", StringComparison.Ordinal) &&
                                           fact.Contains("value", StringComparison.Ordinal) &&
                                           fact.Contains("0", StringComparison.Ordinal)), Is.True);
            Assert.That(facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("divisor", StringComparison.Ordinal) &&
                                           fact.Contains("value", StringComparison.Ordinal)), Is.True);
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
        public void SymbolicSourceQueryService_QuerySource_CollectsStatementGuardFacts()
        {
            const string source = @"
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
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "QuerySourceStatementFacts.cs",
                FindLine(source, "return values[index];"),
                9,
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(result.NodeKind, Is.EqualTo("ReturnStatement"));
            Assert.That(result.Facts, Is.Not.Empty);
            Assert.That(result.MergedInvariantText, Does.Contain("And"));
            Assert.That(result.Facts.Any(fact => fact.Contains("LessThan", StringComparison.Ordinal)), Is.True);
            Assert.That(result.Facts.Any(fact => fact.Contains("GreaterThanOrEqual", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_AnalyzeAt_ExposesTypedPathConditions()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "SymbolicProgramPointAnalysisHost",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var statement = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Single(node => node.Expression?.ToString() == "value");

            var analysis = new SymbolicInvariantService().AnalyzeAt(statement, semanticModel, cancellationToken: CancellationToken.None);

            Assert.That(analysis.PathConditions, Is.Not.Empty);
            Assert.That(analysis.PathConditions.Any(condition => condition is SmtBinaryFormula), Is.True);
            Assert.That(analysis.MergedInvariant, Is.InstanceOf<SmtBinaryFormula>());
            Assert.That(analysis.MergedInvariantText, Does.Contain("GreaterThan"));
            Assert.That(analysis.Facts.Any(fact => fact.Contains("GreaterThan", StringComparison.Ordinal)), Is.True);
            Assert.That(analysis.Reachability, Is.EqualTo(SymbolicReachability.NotChecked));
        }

        [Test]
        public void SymbolicSourceQueryService_AnalyzeSource_ExposesProgramPointAnalysis()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().AnalyzeSource(
                source,
                "AnalyzeSourceProgramPoint.cs",
                FindLine(source, "return value;"),
                13,
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(result.FilePath, Is.EqualTo("AnalyzeSourceProgramPoint.cs"));
            Assert.That(result.NodeKind, Is.EqualTo("ReturnStatement"));
            Assert.That(result.Analysis, Is.Not.Null);
            Assert.That(result.PathConditions, Is.Not.Empty);
            Assert.That(result.PathConditions.Any(condition => condition is SmtBinaryFormula), Is.True);
            Assert.That(result.MergedInvariant, Is.SameAs(result.Analysis.MergedInvariant));
            Assert.That(result.MergedInvariantText, Is.EqualTo(result.Analysis.MergedInvariantText));
            Assert.That(result.Facts.Any(fact => fact.Contains("GreaterThan", StringComparison.Ordinal)), Is.True);
            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.NotChecked));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySourceAtPosition_ProvesImplication()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 0;
    }
}";
            var position = source.IndexOf("return value;", StringComparison.Ordinal);
            var result = new SymbolicSourceQueryService().QuerySourceAtPosition(
                source,
                "QuerySourceAtPosition.cs",
                position,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
                impliedConditions: new[] { "value > 0" });

            Assert.That(result.Position, Is.EqualTo(position));
            Assert.That(result.Line, Is.EqualTo(8));
            Assert.That(result.NodeKind, Is.EqualTo("ReturnStatement"));
            Assert.That(result.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesProgramPointReachability()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            if (value <= 0)
            {
                return value;
            }
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().AnalyzeSource(
                source,
                "AnalyzeSourceReachability.cs",
                FindLine(source, "return value;"),
                17,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

            Assert.That(result.PathConditions, Is.Not.Empty);
            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
            Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_DoesNotCheckReachabilityByDefault()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "QuerySourceReachabilityDefault.cs",
                FindLine(source, "return value;"),
                13,
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(result.Facts, Is.Not.Empty);
            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.NotChecked));
            Assert.That(result.ReachabilityReason, Is.EqualTo("reachability_not_checked"));
            Assert.That(result.SmtDiagnostics.IsConfigured, Is.False);
            Assert.That(result.SmtDiagnostics.ExecutedQueryCount, Is.EqualTo(0));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_WithSmt_ClassifiesContradictoryProgramPointUnreachable()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            if (value <= 0)
            {
                return value;
            }
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "QuerySourceReachabilitySmt.cs",
                FindLine(source, "return value;"),
                17,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

            Assert.That(result.Facts, Is.Not.Empty);
            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
            Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_ProvesMultipleConditionsInSingleProgramPointQuery()
        {
            const string source = @"
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
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "QuerySourceMultipleProofs.cs",
                FindLine(source, "return values[index];"),
                13,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
                impliedConditions: new[] { "index >= 0", "index < values.Length" });

            Assert.That(result.Facts, Is.Not.Empty);
            Assert.That(result.ConditionProofs, Has.Count.EqualTo(2));
            Assert.That(result.ConditionProofs.Select(static proof => proof.TruthValue), Is.All.EqualTo(SymbolicTruthValue.ProvenTrue));
            Assert.That(result.SmtDiagnostics.IsConfigured, Is.True);
            Assert.That(result.SmtDiagnostics.Mode, Is.EqualTo(SmtAnalysisMode.Bounded));
            Assert.That(result.SmtDiagnostics.QueryTimeoutMs, Is.EqualTo(750));
            Assert.That(result.SmtDiagnostics.MethodBudgetMs, Is.EqualTo(5000));
            Assert.That(result.SmtDiagnostics.MaxPathConditions, Is.EqualTo(192));
            Assert.That(result.SmtDiagnostics.MaxExpressionNodes, Is.EqualTo(2048));
            Assert.That(result.SmtDiagnostics.ExecutedQueryCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(result.SmtDiagnostics.CacheEntryCount, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesGuardStyleSourcePredicateImplications()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (SourcePredicates.HasTextWithGuard(value))
        {
            return value.Length;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "GuardStyleSourcePredicateImplications.cs",
                FindLine(source, "return value.Length;"),
                13,
                "value != null && value.Length > 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesIfElseSourcePredicateImplications()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (SourcePredicates.HasTextWithIfElse(value))
        {
            return value.Length;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "IfElseSourcePredicateImplications.cs",
                FindLine(source, "return value.Length;"),
                13,
                "value != null && value.Length > 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesLocalAliasSourcePredicateImplications()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (SourcePredicates.HasTextViaLocal(value))
        {
            return value.Length;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "LocalAliasSourcePredicateImplications.cs",
                FindLine(source, "return value.Length;"),
                13,
                "value != null && value.Length > 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesLocalAssignmentSourcePredicateImplications()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (SourcePredicates.HasTextViaAssignment(value))
        {
            return value.Length;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "LocalAssignmentSourcePredicateImplications.cs",
                FindLine(source, "return value.Length;"),
                13,
                "value != null && value.Length > 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesMultiGuardSourcePredicateIndexFacts()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (SourcePredicates.IsValidIndex(values, index))
        {
            return values[index];
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "MultiGuardSourcePredicateIndexFacts.cs",
                FindLine(source, "return values[index];"),
                13,
                "values != null && index >= 0 && index < values.Length",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesGuardStyleSourcePredicateExactValue()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (SourcePredicates.IsZeroWithGuard(divisor))
        {
            return 10 / divisor;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "GuardStyleSourcePredicateExactValue.cs",
                FindLine(source, "return 10 / divisor;"),
                13,
                "divisor == 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesLocalAliasSourcePredicateExactValue()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (SourcePredicates.IsZeroViaLocal(divisor))
        {
            return 10 / divisor;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "LocalAliasSourcePredicateExactValue.cs",
                FindLine(source, "return 10 / divisor;"),
                13,
                "divisor == 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesLocalAssignmentSourcePredicateExactValue()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (SourcePredicates.IsZeroViaAssignment(divisor))
        {
            return 10 / divisor;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "LocalAssignmentSourcePredicateExactValue.cs",
                FindLine(source, "return 10 / divisor;"),
                13,
                "divisor == 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesReassignedIntegerLocalSourcePredicate()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (SourcePredicates.IsPositiveAfterLocalAssignment(value))
        {
            return value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ReassignedIntegerLocalSourcePredicate.cs",
                FindLine(source, "return value;"),
                13,
                "value > -1",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesSwitchStatementSourcePredicateExactValue()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (SourcePredicates.IsZeroWithSwitch(divisor))
        {
            return 10 / divisor;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "SwitchStatementSourcePredicateExactValue.cs",
                FindLine(source, "return 10 / divisor;"),
                13,
                "divisor == 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesSwitchStatementFallbackSourcePredicateExactValue()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (SourcePredicates.IsZeroWithSwitchFallback(divisor))
        {
            return 10 / divisor;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "SwitchStatementFallbackSourcePredicateExactValue.cs",
                FindLine(source, "return 10 / divisor;"),
                13,
                "divisor == 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesSwitchStatementPatternSourcePredicateRange()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (SourcePredicates.IsSmallPositiveWithSwitch(value))
        {
            return value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "SwitchStatementPatternSourcePredicateRange.cs",
                FindLine(source, "return value;"),
                13,
                "value > 0 && value < 10",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesSourceBooleanPropertyImplications()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(SourcePredicateBox box)
    {
        if (box.HasText)
        {
            return box.Value.Length;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "SourceBooleanPropertyImplications.cs",
                FindLine(source, "return box.Value.Length;"),
                13,
                "box.Value != null && box.Value.Length > 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesSourceBooleanGetterLocalAliasExactValue()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(SourcePredicateBox box)
    {
        if (box.IsZeroDivisor)
        {
            return 10 / box.Divisor;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "SourceBooleanGetterLocalAliasExactValue.cs",
                FindLine(source, "return 10 / box.Divisor;"),
                13,
                "box.Divisor == 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesInstanceSourceBooleanMethodImplications()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(SourcePredicateBox box)
    {
        if (box.HasTextMethod())
        {
            return box.Value.Length;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "InstanceSourceBooleanMethodImplications.cs",
                FindLine(source, "return box.Value.Length;"),
                13,
                "box.Value != null && box.Value.Length > 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesInstanceSourceBooleanMethodLocalAliasExactValue()
        {
            var source = SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(SourcePredicateBox box)
    {
        if (box.IsZeroDivisorMethod())
        {
            return 10 / box.Divisor;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "InstanceSourceBooleanMethodLocalAliasExactValue.cs",
                FindLine(source, "return 10 / box.Divisor;"),
                13,
                "box.Divisor == 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_ConditionProofsWithoutSmtRemainConservative()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "QuerySourceProofsWithoutSmt.cs",
                FindLine(source, "return value;"),
                13,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                impliedConditions: new[] { "value > 0" });

            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.NotChecked));
            Assert.That(result.ConditionProofs, Has.Count.EqualTo(1));
            Assert.That(result.ConditionProofs[0].TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
            Assert.That(result.ConditionProofs[0].Reason, Is.EqualTo("smt_required"));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesRangeGuardImplications()
        {
            const string source = @"
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
}";
            var service = new SymbolicSourceQueryService();
            var nonNegative = service.ProveConditionAtSource(
                source,
                "ProveConditionRangeGuard.cs",
                FindLine(source, "return values[index];"),
                13,
                "index >= 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());
            var belowLength = service.ProveConditionAtSource(
                source,
                "ProveConditionRangeGuard.cs",
                FindLine(source, "return values[index];"),
                13,
                "index < values.Length",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(nonNegative.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
            Assert.That(belowLength.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesForeachReceiverNonNull()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        foreach (var value in values)
        {
            return values.Length + value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ForeachReceiverNonNull.cs",
                FindLine(source, "return values.Length + value;"),
                13,
                "values != null",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesForeachArrayLengthPositive()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        foreach (var value in values)
        {
            return values.Length + value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ForeachArrayLengthPositive.cs",
                FindLine(source, "return values.Length + value;"),
                13,
                "values.Length > 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesForeachNewEmptyArrayBodyUnreachable()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        foreach (var value in new int[0])
        {
            return value;
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().AnalyzeSource(
                source,
                "ForeachNewEmptyArrayUnreachable.cs",
                FindLine(source, "return value;"),
                13,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

            Assert.That(result.PathConditions, Is.Not.Empty);
            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
            Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesForeachBodyAfterNullGuardUnreachable()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values == null)
        {
            foreach (var value in values)
            {
                return values.Length + value;
            }
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().AnalyzeSource(
                source,
                "ForeachReceiverContradiction.cs",
                FindLine(source, "return values.Length + value;"),
                17,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

            Assert.That(result.PathConditions, Is.Not.Empty);
            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
            Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesSingleElementForeachValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        foreach (var value in new[] { 5 })
        {
            return value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "SingleElementForeachValue.cs",
                FindLine(source, "return value;"),
                20,
                "value == 5",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_DoesNotAssumeMultiElementForeachValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        foreach (var value in new[] { 0, 1 })
        {
            return value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "MultiElementForeachValue.cs",
                FindLine(source, "return value;"),
                20,
                "value == 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesFiniteForeachNonZeroValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        foreach (var value in new[] { 1, 2 })
        {
            return value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "FiniteForeachNonZeroValue.cs",
                FindLine(source, "return value;"),
                20,
                "value != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesPriorAssignedFiniteForeachNonZeroValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new[] { 1, 2 };
        foreach (var value in values)
        {
            return value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "PriorAssignedFiniteForeachNonZeroValue.cs",
                FindLine(source, "return value;"),
                20,
                "value != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_DoesNotUseFiniteForeachFactsAfterUnknownReassignment()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] replacement)
    {
        var values = new[] { 1, 2 };
        values = replacement;
        foreach (var value in values)
        {
            return value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ReassignedFiniteForeachValue.cs",
                FindLine(source, "return value;"),
                20,
                "value != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesLockReceiverNonNull()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(object gate)
    {
        lock (gate)
        {
            return gate.GetHashCode();
        }
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "LockReceiverNonNull.cs",
                FindLine(source, "return gate.GetHashCode();"),
                13,
                "gate != null",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesNullGuardedLockBodyUnreachable()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(object gate)
    {
        if (gate == null)
        {
            lock (gate)
            {
                return 1;
            }
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().AnalyzeSource(
                source,
                "NullGuardedLockBodyUnreachable.cs",
                FindLine(source, "return 1;"),
                17,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

            Assert.That(result.PathConditions, Is.Not.Empty);
            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
            Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ReassignedLockReceiverDoesNotKeepNonNullFact()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(object gate)
    {
        lock (gate)
        {
            gate = null;
            return gate.GetHashCode();
        }
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "LockReceiverReassigned.cs",
                FindLine(source, "return gate.GetHashCode();"),
                13,
                "gate != null",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesCatchExceptionVariableNonNull()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod()
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Length;
        }
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "CatchExceptionVariableNonNull.cs",
                FindLine(source, "return ex.Message.Length;"),
                13,
                "ex != null",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesCatchFilterCondition()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int value)
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (value > 0)
        {
            return 10 / value;
        }
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "CatchFilterCondition.cs",
                FindLine(source, "return 10 / value;"),
                13,
                "value > 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesContradictoryCatchFilterBranchUnreachable()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int value)
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (value > 0)
        {
            if (value <= 0)
            {
                return value;
            }

            return 0;
        }
    }
}";
            var result = new SymbolicSourceQueryService().AnalyzeSource(
                source,
                "ContradictoryCatchFilterBranch.cs",
                FindLine(source, "return value;"),
                17,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

            Assert.That(result.PathConditions, Is.Not.Empty);
            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
            Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesUsingDeclarationResourceAlias()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod(IDisposable value)
    {
        using (IDisposable resource = value)
        {
            return resource == value ? 1 : 0;
        }
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "UsingDeclarationResourceAlias.cs",
                FindLine(source, "return resource == value ? 1 : 0;"),
                13,
                "resource == value",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesUsingDeclarationThrowGuardedResourceNonNull()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod(IDisposable value)
    {
        using (IDisposable resource = value ?? throw new InvalidOperationException())
        {
            return resource.GetHashCode();
        }
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "UsingDeclarationThrowGuardedResourceNonNull.cs",
                FindLine(source, "return resource.GetHashCode();"),
                13,
                "resource != null",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesUsingDeclarationNullBranchUnreachable()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod(IDisposable value)
    {
        using (IDisposable resource = value ?? throw new InvalidOperationException())
        {
            if (resource == null)
            {
                return 1;
            }

            return 0;
        }
    }
}";
            var result = new SymbolicSourceQueryService().AnalyzeSource(
                source,
                "UsingDeclarationNullBranchUnreachable.cs",
                FindLine(source, "return 1;"),
                17,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

            Assert.That(result.PathConditions, Is.Not.Empty);
            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
            Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_ProvesForLoopMonotonicIndexBounds()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        var sum = 0;
        for (var index = 0; index < values.Length; index++)
        {
            sum += values[index];
        }

        return sum;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "ForLoopMonotonicIndexBounds.cs",
                FindLine(source, "sum += values[index];"),
                20,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
                impliedConditions: new[] { "index >= 0", "index < values.Length" });

            Assert.That(result.ConditionProofs, Has.Count.EqualTo(2));
            Assert.That(result.ConditionProofs.Select(static proof => proof.TruthValue), Is.All.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_ProvesReverseForLoopMonotonicIndexBounds()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        var sum = 0;
        for (var index = values.Length - 1; index >= 0; index--)
        {
            sum += values[index];
        }

        return sum;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "ReverseForLoopMonotonicIndexBounds.cs",
                FindLine(source, "sum += values[index];"),
                20,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
                impliedConditions: new[] { "index >= 0", "index < values.Length" });

            Assert.That(result.ConditionProofs, Has.Count.EqualTo(2));
            Assert.That(result.ConditionProofs.Select(static proof => proof.TruthValue), Is.All.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_DoesNotInferForLoopLowerBoundWhenUpdaterDecrements()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        for (var index = 1; index < values.Length; index--)
        {
            return values[index];
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "ForLoopDecrementNoLowerBound.cs",
                FindLine(source, "return values[index];"),
                20,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
                impliedConditions: new[] { "index >= 0" });

            Assert.That(result.ConditionProofs, Has.Count.EqualTo(1));
            Assert.That(result.ConditionProofs[0].TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_DoesNotInferReverseForLoopUpperBoundWhenUpdaterIncrements()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        for (var index = values.Length - 1; index >= 0; index++)
        {
            return values[index];
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "ReverseForLoopIncrementNoUpperBound.cs",
                FindLine(source, "return values[index];"),
                20,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
                impliedConditions: new[] { "index < values.Length" });

            Assert.That(result.ConditionProofs, Has.Count.EqualTo(1));
            Assert.That(result.ConditionProofs[0].TruthValue, Is.Not.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_DropsStaleIfConditionAfterReassignment()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (index < 0)
        {
            index = 0;
            return values[index];
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "StaleIfConditionAfterReassignment.cs",
                FindLine(source, "return values[index];"),
                20,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
                impliedConditions: new[] { "index < 0", "index >= 0", "values == null" });

            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Reachable));
            Assert.That(result.ConditionProofs, Has.Count.EqualTo(3));
            Assert.That(result.ConditionProofs[0].TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse));
            Assert.That(result.ConditionProofs[1].TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
            Assert.That(result.ConditionProofs[2].TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_DropsStaleWhileConditionAfterReassignment()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        while (index < values.Length)
        {
            index = values.Length;
            return values[index];
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "StaleWhileConditionAfterReassignment.cs",
                FindLine(source, "return values[index];"),
                20,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
                impliedConditions: new[] { "index < values.Length", "index >= values.Length" });

            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Reachable));
            Assert.That(result.ConditionProofs, Has.Count.EqualTo(2));
            Assert.That(result.ConditionProofs[0].TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse));
            Assert.That(result.ConditionProofs[1].TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesWhileNormalExitImplication()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        while (index < values.Length)
        {
            index++;
        }

        return index;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ProveConditionWhileExit.cs",
                FindLine(source, "return index;"),
                13,
                "index >= values.Length",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesEnumImplication()
        {
            const string source = @"
public enum Mode
{
    None = 0,
    Ready = 1
}

public class TestClass
{
    public int TestMethod(Mode state)
    {
        if (state == Mode.Ready)
        {
            return 1;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ProveConditionEnum.cs",
                FindLine(source, "return 1;"),
                13,
                "state != Mode.None",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesSwitchExpressionValueImplication()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int mode)
    {
        var divisor = mode switch
        {
            0 => 1,
            1 => 2,
            _ => 3
        };

        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "SwitchExpressionValueImplication.cs",
                FindLine(source, "return 10 / divisor;"),
                16,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_DoesNotLowerSwitchExpressionWithoutDiscardFallback()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int mode)
    {
        var divisor = mode switch
        {
            0 => 1,
            1 => 2
        };

        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "SwitchExpressionNoFallback.cs",
                FindLine(source, "return 10 / divisor;"),
                15,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesSwitchStatementMergedImplication()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int mode)
    {
        var divisor = 0;
        switch (mode)
        {
            case 0:
                divisor = 1;
                break;
            case 1:
                divisor = 2;
                break;
            default:
                divisor = 3;
                break;
        }

        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "SwitchStatementMergedImplication.cs",
                FindLine(source, "return 10 / divisor;"),
                24,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_DoesNotMergeSwitchStatementWithoutDefault()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int mode)
    {
        var divisor = 0;
        switch (mode)
        {
            case 0:
                divisor = 1;
                break;
            case 1:
                divisor = 2;
                break;
        }

        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "SwitchStatementNoDefault.cs",
                FindLine(source, "return 10 / divisor;"),
                21,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesSwitchStatementExitingSectionExclusion()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 0:
                return 0;
        }

        return 10 / value;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "SwitchStatementExitingSectionExclusion.cs",
                FindLine(source, "return 10 / value;"),
                13,
                "value != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionFalse()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == 0)
        {
            return value;
        }

        return 1;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ProveConditionFalse.cs",
                FindLine(source, "return value;"),
                13,
                "value != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ReportsUnreachablePoint()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            if (value <= 0)
            {
                return value;
            }
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ProveConditionUnreachable.cs",
                FindLine(source, "return value;"),
                17,
                "value > 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unreachable));
            Assert.That(proof.Reason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_CollectsExpressionContextFacts()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(string first, string second)
    {
        return first ?? second;
    }
}";
            var line = FindLine(source, "return first ?? second;");
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "QuerySourceExpressionFacts.cs",
                line,
                source.Split('\n')[line - 1].IndexOf("second", StringComparison.Ordinal) + 1,
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(result.NodeKind, Is.EqualTo("IdentifierName"));
            Assert.That(result.Facts, Is.Not.Empty);
            Assert.That(result.Facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                                 fact.Contains("first", StringComparison.Ordinal) &&
                                                 fact.Contains("Null", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_CollectsCoalesceThrowAssignmentFacts()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod(string value)
    {
        var safe = value ?? throw new InvalidOperationException();
        if (safe == null)
        {
            return 0;
        }

        return safe.Length;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "QuerySourceCoalesceThrowFacts.cs",
                FindLine(source, "if (safe == null)"),
                9,
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(result.NodeKind, Is.EqualTo("IfStatement"));
            Assert.That(result.Facts, Is.Not.Empty);
            Assert.That(result.Facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                                 fact.Contains("safe", StringComparison.Ordinal) &&
                                                 fact.Contains("value", StringComparison.Ordinal)), Is.True);
            Assert.That(result.Facts.Any(fact => fact.Contains("NotEqual", StringComparison.Ordinal) &&
                                                 fact.Contains("value", StringComparison.Ordinal) &&
                                                 fact.Contains("Null", StringComparison.Ordinal)), Is.True);
            Assert.That(result.Facts.Any(fact => fact.Contains("Length", StringComparison.Ordinal) &&
                                                 fact.Contains("safe", StringComparison.Ordinal) &&
                                                 fact.Contains("value", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesCoalesceAssignmentNonNullLiteral()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string value)
    {
        value ??= ""safe"";
        return value.Length;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "CoalesceAssignmentNonNullLiteral.cs",
                FindLine(source, "return value.Length;"),
                16,
                "value != null",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesCoalesceAssignmentGuardedFallbackNullBranchUnreachable()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string value, string fallback)
    {
        if (fallback == null)
        {
            return 0;
        }

        value ??= fallback;
        if (value == null)
        {
            return 1;
        }

        return value.Length;
    }
}";
            var result = new SymbolicSourceQueryService().AnalyzeSource(
                source,
                "CoalesceAssignmentGuardedFallback.cs",
                FindLine(source, "return 1;"),
                20,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

            Assert.That(result.PathConditions, Is.Not.Empty);
            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
            Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullDominatedCoalesceAssignmentLength()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values != null)
        {
            return 0;
        }

        values ??= new int[1];
        return values.Length;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullDominatedCoalesceAssignmentLength.cs",
                FindLine(source, "return values.Length;"),
                16,
                "values.Length == 1",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_PreservesKnownNonNullCoalesceAssignmentLength()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[2];
        values ??= new int[1];
        return values.Length;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "KnownNonNullCoalesceAssignmentLength.cs",
                FindLine(source, "return values.Length;"),
                16,
                "values.Length == 2",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullDominatedNullableCoalesceAssignmentValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        int? maybe = null;
        maybe ??= 5;
        return maybe.Value;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullDominatedNullableCoalesceAssignmentValue.cs",
                FindLine(source, "return maybe.Value;"),
                16,
                "maybe.Value == 5",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_PreservesKnownHasValueNullableCoalesceAssignmentValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        int? maybe = 7;
        maybe ??= 5;
        return maybe.Value;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "KnownHasValueNullableCoalesceAssignmentValue.cs",
                FindLine(source, "return maybe.Value;"),
                16,
                "maybe.Value == 7",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesNullableCoalesceAssignmentNoValueBranchUnreachable()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int? maybe)
    {
        maybe ??= 5;
        if (!maybe.HasValue)
        {
            return 0;
        }

        return maybe.Value;
    }
}";
            var result = new SymbolicSourceQueryService().AnalyzeSource(
                source,
                "NullableCoalesceAssignmentNoValueBranch.cs",
                FindLine(source, "return 0;"),
                20,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

            Assert.That(result.PathConditions, Is.Not.Empty);
            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
            Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_CollectsConditionalThrowAssignmentFacts()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod(string value)
    {
        var safe = value != null ? value : throw new InvalidOperationException();
        if (safe == null)
        {
            return 0;
        }

        return safe.Length;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "QuerySourceConditionalThrowFacts.cs",
                FindLine(source, "if (safe == null)"),
                9,
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(result.NodeKind, Is.EqualTo("IfStatement"));
            Assert.That(result.Facts, Is.Not.Empty);
            Assert.That(result.Facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                                 fact.Contains("safe", StringComparison.Ordinal) &&
                                                 fact.Contains("value", StringComparison.Ordinal)), Is.True);
            Assert.That(result.Facts.Any(fact => fact.Contains("NotEqual", StringComparison.Ordinal) &&
                                                 fact.Contains("value", StringComparison.Ordinal) &&
                                                 fact.Contains("Null", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_CollectsPatternVariableBindingFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value is > 0 and var divisor)
        {
            return 10 / divisor;
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "QuerySourcePatternBindingFacts.cs",
                FindLine(source, "return 10 / divisor;"),
                13,
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(result.Facts, Is.Not.Empty);
            Assert.That(result.Facts.Any(fact => fact.Contains("GreaterThan", StringComparison.Ordinal) &&
                                                 fact.Contains("value", StringComparison.Ordinal) &&
                                                 fact.Contains("0", StringComparison.Ordinal)), Is.True);
            Assert.That(result.Facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                                 fact.Contains("divisor", StringComparison.Ordinal) &&
                                                 fact.Contains("value", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_CollectsPropertyPatternVariableBindingFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string text)
    {
        if (text is { Length: > 0 and var length })
        {
            return 10 / length;
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "QuerySourcePropertyPatternBindingFacts.cs",
                FindLine(source, "return 10 / length;"),
                13,
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(result.Facts, Is.Not.Empty);
            Assert.That(result.Facts.Any(fact => fact.Contains("GreaterThan", StringComparison.Ordinal) &&
                                                 fact.Contains("Length", StringComparison.Ordinal) &&
                                                 fact.Contains("0", StringComparison.Ordinal)), Is.True);
            Assert.That(result.Facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                                 fact.Contains("length", StringComparison.Ordinal) &&
                                                 fact.Contains("Length", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_CollectsListPatternElementBindingFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [> 0 and var divisor, ..])
        {
            return 10 / divisor;
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "QuerySourceListPatternElementBindingFacts.cs",
                FindLine(source, "return 10 / divisor;"),
                13,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
                impliedConditions: new[] { "divisor != 0" });

            Assert.That(result.Facts, Is.Not.Empty);
            Assert.That(result.Facts.Any(fact => fact.Contains("GreaterThan", StringComparison.Ordinal) &&
                                                 fact.Contains("[0]", StringComparison.Ordinal) &&
                                                 fact.Contains("0", StringComparison.Ordinal)), Is.True);
            Assert.That(result.Facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                                 fact.Contains("divisor", StringComparison.Ordinal) &&
                                                 fact.Contains("[0]", StringComparison.Ordinal)), Is.True);
            Assert.That(result.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_CollectsTrailingListPatternElementBindingFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [.., > 0 and var divisor])
        {
            return 10 / divisor;
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "QuerySourceTrailingListPatternElementBindingFacts.cs",
                FindLine(source, "return 10 / divisor;"),
                13,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
                impliedConditions: new[] { "divisor != 0" });

            Assert.That(result.Facts, Is.Not.Empty);
            Assert.That(result.Facts.Any(fact => fact.Contains("GreaterThan", StringComparison.Ordinal) &&
                                                 fact.Contains("[^1]", StringComparison.Ordinal) &&
                                                 fact.Contains("0", StringComparison.Ordinal)), Is.True);
            Assert.That(result.Facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                                 fact.Contains("divisor", StringComparison.Ordinal) &&
                                                 fact.Contains("[^1]", StringComparison.Ordinal)), Is.True);
            Assert.That(result.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_ProvesArrayElementReadFromListPatternFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [> 0, ..])
        {
            var divisor = values[0];
            return 10 / divisor;
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "QuerySourceArrayElementReadFromListPatternFacts.cs",
                FindLine(source, "return 10 / divisor;"),
                13,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
                impliedConditions: new[] { "divisor != 0" });

            Assert.That(result.Facts, Is.Not.Empty);
            Assert.That(result.Facts.Any(fact => fact.Contains("GreaterThan", StringComparison.Ordinal) &&
                                                 fact.Contains("[0]", StringComparison.Ordinal) &&
                                                 fact.Contains("0", StringComparison.Ordinal)), Is.True);
            Assert.That(result.Facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                                 fact.Contains("divisor", StringComparison.Ordinal) &&
                                                 fact.Contains("[0]", StringComparison.Ordinal)), Is.True);
            Assert.That(result.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_QuerySource_UsesArrayElementWriteFactAfterElementMutation()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [> 0, ..])
        {
            values[0] = 0;
            var divisor = values[0];
            return 10 / divisor;
        }

        return 0;
    }
}";
            var result = new SymbolicSourceQueryService().QuerySource(
                source,
                "QuerySourceArrayElementWriteThenRead.cs",
                FindLine(source, "return 10 / divisor;"),
                13,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
                impliedConditions: new[] { "divisor != 0" });

            Assert.That(result.Facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                                 fact.Contains("[0]", StringComparison.Ordinal) &&
                                                 fact.Contains("0", StringComparison.Ordinal)), Is.True);
            Assert.That(result.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse));
        }

        [Test]
        public void SymbolicInvariantService_CollectsCompoundAssignmentUpdateFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 0;
        divisor += 1;
        return 10 / divisor;
    }
}",
                "return 10 / divisor;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("divisor", StringComparison.Ordinal) &&
                                           fact.Contains("Add", StringComparison.Ordinal) &&
                                           fact.Contains("1", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsIncrementUpdateFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 0;
        divisor++;
        return 10 / divisor;
    }
}",
                "return 10 / divisor;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("divisor", StringComparison.Ordinal) &&
                                           fact.Contains("Add", StringComparison.Ordinal) &&
                                           fact.Contains("1", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsTupleAssignmentFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 0;
        var other = 0;
        (divisor, other) = (1, 2);
        return 10 / divisor;
    }
}",
                "return 10 / divisor;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("divisor", StringComparison.Ordinal) &&
                                           fact.Contains("1", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsTupleDeconstructionDeclarationFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod()
    {
        var (divisor, other) = (1, 2);
        return 10 / divisor;
    }
}",
                "return 10 / divisor;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("divisor", StringComparison.Ordinal) &&
                                           fact.Contains("1", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesInlineFiniteArrayElementAssignedNonZeroValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = (new[] { 1, 2 })[0];
        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "InlineFiniteArrayElementAssignedNonZeroValue.cs",
                FindLine(source, "return 10 / divisor;"),
                20,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesPriorFiniteArrayElementAssignedNonZeroValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new[] { 1, 2 };
        var divisor = values[0];
        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "PriorFiniteArrayElementAssignedNonZeroValue.cs",
                FindLine(source, "return 10 / divisor;"),
                20,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesInlineFiniteArrayFromEndElementAssignedNonZeroValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = (new[] { 1, 2 })[^1];
        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "InlineFiniteArrayFromEndElementAssignedNonZeroValue.cs",
                FindLine(source, "return 10 / divisor;"),
                20,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesPriorFiniteArrayFromEndElementAssignedNonZeroValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new[] { 1, 2 };
        var divisor = values[^1];
        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "PriorFiniteArrayFromEndElementAssignedNonZeroValue.cs",
                FindLine(source, "return 10 / divisor;"),
                20,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalFiniteArrayElementAssignedNonZeroValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var values = new[] { 1, 2 };
        var divisor = flag ? values[0] : values[1];
        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ConditionalFiniteArrayElementAssignedNonZeroValue.cs",
                FindLine(source, "return 10 / divisor;"),
                20,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_DoesNotInferPriorFiniteArrayElementAfterUnknownReassignment()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] replacement)
    {
        var values = new[] { 1, 2 };
        values = replacement;
        var divisor = values[0];
        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ReassignedFiniteArrayElementAssignedValue.cs",
                FindLine(source, "return 10 / divisor;"),
                20,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_DoesNotInferPriorFiniteArrayElementFromTargetSelfReference()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag, int input)
    {
        var divisor = input;
        var values = new[] { divisor, 2 };
        divisor = flag ? values[0] : values[1];
        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "SelfReferencingFiniteArrayElementAssignedValue.cs",
                FindLine(source, "return 10 / divisor;"),
                20,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleElementAssignedNonZeroValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (1, 2);
        var divisor = pair.Item1;
        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "TupleElementAssignedNonZeroValue.cs",
                FindLine(source, "return 10 / divisor;"),
                20,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNamedTupleElementAssignedNonZeroValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (divisor: 1, other: 2);
        var divisor = pair.divisor;
        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NamedTupleElementAssignedNonZeroValue.cs",
                FindLine(source, "return 10 / divisor;"),
                20,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleLocalDeconstructionAssignedNonZeroValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (1, 2);
        var divisor = 0;
        var other = 0;
        (divisor, other) = pair;
        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "TupleLocalDeconstructionAssignedNonZeroValue.cs",
                FindLine(source, "return 10 / divisor;"),
                20,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleLocalDeconstructionDeclaredNonZeroValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (1, 2);
        var (divisor, other) = pair;
        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "TupleLocalDeconstructionDeclaredNonZeroValue.cs",
                FindLine(source, "return 10 / divisor;"),
                20,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicInvariantService_CollectsIfElseThenExitFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
            return 0;
        }
        else
        {
        }

        return 10 / divisor;
    }
}",
                "return 10 / divisor;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Not", StringComparison.Ordinal) &&
                                           fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("divisor", StringComparison.Ordinal) &&
                                           fact.Contains("0", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsIfElseElseExitFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
        }
        else
        {
            return 0;
        }

        return 10 / divisor;
    }
}",
                "return 10 / divisor;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("divisor", StringComparison.Ordinal) &&
                                           fact.Contains("0", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_IfElseSurvivingMutationSuppressesStaleFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
            return 0;
        }
        else
        {
            divisor = 0;
        }

        return 10 / divisor;
    }
}",
                "return 10 / divisor;");

            Assert.That(facts.Any(fact => fact.Contains("Not", StringComparison.Ordinal) &&
                                           fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("divisor", StringComparison.Ordinal) &&
                                           fact.Contains("0", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void SymbolicInvariantService_MergesIdenticalIfElseAssignmentFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var divisor = 0;
        if (flag)
        {
            divisor = 1;
        }
        else
        {
            divisor = 1;
        }

        return 10 / divisor;
    }
}",
                "return 10 / divisor;");

            Assert.That(facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("divisor", StringComparison.Ordinal) &&
                                           fact.Contains("1", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_DoesNotCollapseDivergentIfElseToSingleValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var divisor = 0;
        if (flag)
        {
            divisor = 1;
        }
        else
        {
            divisor = 2;
        }

        return 10 / divisor;
    }
}";
            var service = new SymbolicSourceQueryService();
            var divisorIsOne = service.ProveConditionAtSource(
                source,
                "DivergentIfElseSingleValue.cs",
                FindLine(source, "return 10 / divisor;"),
                16,
                "divisor == 1",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());
            var divisorIsTwo = service.ProveConditionAtSource(
                source,
                "DivergentIfElseSingleValue.cs",
                FindLine(source, "return 10 / divisor;"),
                16,
                "divisor == 2",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(divisorIsOne.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
            Assert.That(divisorIsTwo.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesDivergentIfElseMergedImplication()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var divisor = 0;
        if (flag)
        {
            divisor = 1;
        }
        else
        {
            divisor = 2;
        }

        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "DivergentIfElseMergedImplication.cs",
                FindLine(source, "return 10 / divisor;"),
                16,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_DoesNotReuseMutatedBranchConditionForMerge()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var divisor = 0;
        if (flag)
        {
            flag = false;
            divisor = 1;
        }
        else
        {
            flag = true;
            divisor = 2;
        }

        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "MutatedIfElseMergedImplication.cs",
                FindLine(source, "return 10 / divisor;"),
                18,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesImplicitElseMergedImplication()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var divisor = 1;
        if (flag)
        {
            divisor = 2;
        }

        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ImplicitElseMergedImplication.cs",
                FindLine(source, "return 10 / divisor;"),
                14,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_DoesNotReuseMutatedImplicitElseConditionForMerge()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var divisor = 1;
        if (flag)
        {
            flag = false;
            divisor = 2;
        }

        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "MutatedImplicitElseMergedImplication.cs",
                FindLine(source, "return 10 / divisor;"),
                15,
                "divisor != 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_MergesIdenticalImplicitElseFactWithMutatedCondition()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var divisor = 1;
        if (flag)
        {
            flag = false;
            divisor = 1;
        }

        return 10 / divisor;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "MutatedImplicitElseIdenticalFact.cs",
                FindLine(source, "return 10 / divisor;"),
                15,
                "divisor == 1",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicInvariantService_CollectsDefaultLiteralAssignmentFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod()
    {
        int divisor = default;
        return 10 / divisor;
    }
}",
                "return 10 / divisor;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("divisor", StringComparison.Ordinal) &&
                                           fact.Contains("0", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicInvariantService_CollectsDefaultReferenceAssignmentFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod()
    {
        string value = default;
        return value.Length;
    }
}",
                "return value.Length;");

            Assert.That(facts, Is.Not.Empty);
            Assert.That(facts.Any(fact => fact.Contains("Equal", StringComparison.Ordinal) &&
                                           fact.Contains("value", StringComparison.Ordinal) &&
                                           fact.Contains("Null", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesFreshObjectAssignmentNonNull()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        object value = new object();
        return value.GetHashCode();
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "FreshObjectAssignmentNonNull.cs",
                FindLine(source, "return value.GetHashCode();"),
                16,
                "value != null",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalFreshObjectAssignmentNonNull()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        object value = flag ? new object() : new object();
        return value.GetHashCode();
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ConditionalFreshObjectAssignmentNonNull.cs",
                FindLine(source, "return value.GetHashCode();"),
                16,
                "value != null",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesCoalescedFreshObjectAssignmentNonNull()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(object input)
    {
        object value = input ?? new object();
        return value.GetHashCode();
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "CoalescedFreshObjectAssignmentNonNull.cs",
                FindLine(source, "return value.GetHashCode();"),
                16,
                "value != null",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalArrayLength()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var values = flag ? new int[1] : new int[1];
        return values.Length;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ConditionalArrayLength.cs",
                FindLine(source, "return values.Length;"),
                16,
                "values.Length == 1",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalArrayLengthDisjunction()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var values = flag ? new int[1] : new int[2];
        return values.Length;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ConditionalArrayLengthDisjunction.cs",
                FindLine(source, "return values.Length;"),
                16,
                "values.Length == 1 || values.Length == 2",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesCoalescedArrayFallbackLength()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        if (input != null)
        {
            return 0;
        }

        var values = input ?? new int[1];
        return values.Length;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "CoalescedArrayFallbackLength.cs",
                FindLine(source, "return values.Length;"),
                16,
                "values.Length == 1",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesCoalescedArrayLengthDisjunction()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        var values = input ?? new int[1];
        return values.Length;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "CoalescedArrayLengthDisjunction.cs",
                FindLine(source, "return values.Length;"),
                16,
                "values.Length == input.Length || values.Length == 1",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableLiteralAssignmentFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        int? value = 5;
        return value.Value;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullableLiteralAssignmentFacts.cs",
                FindLine(source, "return value.Value;"),
                16,
                "value.HasValue && value.Value == 5",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableEqualsConstantGuardValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (value == 5)
        {
            return value.Value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullableEqualsConstantGuardValue.cs",
                FindLine(source, "return value.Value;"),
                20,
                "value.HasValue && value.Value == 5",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableGreaterThanGuardValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (value > 0)
        {
            return value.Value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullableGreaterThanGuardValue.cs",
                FindLine(source, "return value.Value;"),
                20,
                "value.HasValue && value.Value > 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesRecursivePatternAliasMemberFact()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (value is { Length: > 0 } text)
        {
            return text.Length;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "RecursivePatternAliasMemberFact.cs",
                FindLine(source, "return text.Length;"),
                20,
                "text != null && text.Length > 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableNotNullGuardHasValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (value != null)
        {
            return value.Value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullableNotNullGuardHasValue.cs",
                FindLine(source, "return value.Value;"),
                20,
                "value.HasValue",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableNullGuardNoValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (value == null)
        {
            return 0;
        }

        return value.Value;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullableNullGuardNoValue.cs",
                FindLine(source, "return 0;"),
                20,
                "!value.HasValue",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableIsNotNullPatternHasValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (value is not null)
        {
            return value.Value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullableIsNotNullPatternHasValue.cs",
                FindLine(source, "return value.Value;"),
                20,
                "value.HasValue",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableIsNullPatternNoValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (value is null)
        {
            return 0;
        }

        return value.Value;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullableIsNullPatternNoValue.cs",
                FindLine(source, "return 0;"),
                20,
                "!value.HasValue",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableRecursivePatternHasValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (value is { })
        {
            return value.Value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullableRecursivePatternHasValue.cs",
                FindLine(source, "return value.Value;"),
                20,
                "value.HasValue",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableNotRecursivePatternNoValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (value is not { })
        {
            return 0;
        }

        return value.Value;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullableNotRecursivePatternNoValue.cs",
                FindLine(source, "return 0;"),
                20,
                "!value.HasValue",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesGuardedConditionalNullableHasValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        if (flag)
        {
            int? value = flag ? 5 : null;
            return value.Value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "GuardedConditionalNullableFacts.cs",
                FindLine(source, "return value.Value;"),
                16,
                "value.HasValue",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesAsExpressionNullSourceResultNull()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod()
    {
        object value = null;
        var text = value as string;
        return text;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "AsExpressionNullSourceResultNull.cs",
                FindLine(source, "return text;"),
                16,
                "text == null",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesAsExpressionNonNullResultImpliesSourceNonNull()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(object value)
    {
        var text = value as string;
        if (text != null)
        {
            return text;
        }

        return string.Empty;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "AsExpressionNonNullResultImpliesSourceNonNull.cs",
                FindLine(source, "return text;"),
                20,
                "value != null",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalAccessNullSourceNullableResultHasNoValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        string text = null;
        int? length = text?.Length;
        return length.GetValueOrDefault();
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ConditionalAccessNullSourceNullableResultHasNoValue.cs",
                FindLine(source, "return length.GetValueOrDefault();"),
                16,
                "!length.HasValue",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalAccessHasValueImpliesReceiverNonNull()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string text)
    {
        int? length = text?.Length;
        if (length.HasValue)
        {
            return length.Value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ConditionalAccessHasValueImpliesReceiverNonNull.cs",
                FindLine(source, "return length.Value;"),
                20,
                "text != null",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalAccessNullableValueWhenPresent()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        string text = ""abc"";
        int? length = text?.Length;
        if (length.HasValue)
        {
            return length.Value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ConditionalAccessNullableValueWhenPresent.cs",
                FindLine(source, "return length.Value;"),
                20,
                "length.Value == 3",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableCoalesceFromConditionalAccessWhenReceiverNonNull()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        string text = ""abc"";
        int length = text?.Length ?? 0;
        return length;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullableCoalesceFromConditionalAccessWhenReceiverNonNull.cs",
                FindLine(source, "return length;"),
                16,
                "length == 3",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableCoalesceFromConditionalAccessWhenReceiverNull()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        string text = null;
        int length = text?.Length ?? 0;
        return length;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullableCoalesceFromConditionalAccessWhenReceiverNull.cs",
                FindLine(source, "return length;"),
                16,
                "length == 0",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableDeclarationPatternBinding()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        int? maybe = 5;
        if (maybe is int value)
        {
            return value;
        }

        return 0;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullableDeclarationPatternBinding.cs",
                FindLine(source, "return value;"),
                20,
                "value == 5",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableRelationalPattern()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        int? maybe = 5;
        return maybe.GetValueOrDefault();
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "NullableRelationalPattern.cs",
                FindLine(source, "return maybe.GetValueOrDefault();"),
                16,
                "maybe is > 3 and < 10",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalAccessReferenceNullSourceResultNull()
        {
            const string source = @"
public sealed class Holder
{
    public string Text;
}

public class TestClass
{
    public string TestMethod()
    {
        Holder holder = null;
        var text = holder?.Text;
        return text;
    }
}";
            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "ConditionalAccessReferenceNullSourceResultNull.cs",
                FindLine(source, "return text;"),
                16,
                "text == null",
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicInvariantService_TupleAssignmentSwapInvalidatesTargetFacts()
        {
            var facts = CollectProgramPointFacts(
                @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 1;
        var other = 2;
        (divisor, other) = (other, divisor);
        return 10 / divisor;
    }
}",
                "return 10 / divisor;");

            Assert.That(facts.Any(fact => fact.Contains("divisor", StringComparison.Ordinal) &&
                                           fact.Contains("Equal", StringComparison.Ordinal)), Is.False);
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
        public void ExecutionVisibility_WideningIntegralCastContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("int value", "(long)value > 0L && value <= 0"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_EnumContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse(
                    "Mode state",
                    "state == Mode.Ready && state != Mode.Ready",
                    "public enum Mode { None = 0, Ready = 1 }"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_NarrowingIntegralCast_RemainsUnknown()
        {
            Assert.That(
                IsConditionAlwaysFalse("int value", "(byte)value == 0 && value == 256"),
                Is.False);
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
        public void ExecutionVisibility_ArrayNestedSliceListPatternContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("int[] values", "values is [.. [_, _]] && values.Length < 2"),
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
        public void ExecutionVisibility_StrictRegexLiteralImpliesStringLength()
        {
            Assert.That(
                IsConditionAlwaysFalse(
                    "string text",
                    @"Regex.IsMatch(text, @""\A[A-Z][0-9]\z"") && text.Length != 2",
                    "using System.Text.RegularExpressions;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_DollarRegexAnchorAllowsTrailingNewline()
        {
            Assert.That(
                IsConditionAlwaysFalse(
                    "string text",
                    "Regex.IsMatch(text, \"^AB$\") && text == \"AB\\n\"",
                    "using System.Text.RegularExpressions;"),
                Is.False);
        }

        [Test]
        public void ExecutionVisibility_StrictRegexLiteralContradictsStringEquality()
        {
            Assert.That(
                IsConditionAlwaysFalse(
                    "string text",
                    @"Regex.IsMatch(text, @""\AAB\z"") && text != ""AB""",
                    "using System.Text.RegularExpressions;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_InstanceRegexLiteralContradictsStringEquality()
        {
            Assert.That(
                IsConditionAlwaysFalse(
                    "string text",
                    @"new Regex(@""\AAB\z"").IsMatch(text) && text != ""AB""",
                    "using System.Text.RegularExpressions;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_RegexIsMatchImpliesInputNonNull()
        {
            Assert.That(
                IsConditionAlwaysFalse(
                    "string text",
                    "Regex.IsMatch(text, \"A\") && text == null",
                    "using System.Text.RegularExpressions;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_ShorthandRegexImpliesStringLength()
        {
            Assert.That(
                IsConditionAlwaysFalse(
                    "string text",
                    @"Regex.IsMatch(text, @""\A\d\s\w\z"") && text.Length != 3",
                    "using System.Text.RegularExpressions;"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_NegatedShorthandRegexClassRemainsConservative()
        {
            Assert.That(
                IsConditionAlwaysFalse(
                    "string text",
                    @"Regex.IsMatch(text, @""\A[^\d]\z"") && text == ""A""",
                    "using System.Text.RegularExpressions;"),
                Is.False);
        }

        [Test]
        public void ExecutionVisibility_UnsupportedRegexOptionsRemainConservative()
        {
            Assert.That(
                IsConditionAlwaysFalse(
                    "string text",
                    "Regex.IsMatch(text, \"^ab$\", RegexOptions.IgnoreCase) && text == \"AB\"",
                    "using System.Text.RegularExpressions;"),
                Is.False);
        }

        [Test]
        public void ExecutionVisibility_StringContainsContradictsStringEquality()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "text.Contains(\"Z\") && text == \"ABC\""),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_StringLiteralEqualityImpliesNonNull()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "text == \"A\" && text == null"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_StringConcatContradictsStringEquality()
        {
            Assert.That(
                IsConditionAlwaysFalse(
                    "string left, string right",
                    "left == \"A\" && right == \"B\" && (left + right) != \"AB\""),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_NullStringConcatUsesEmptyString()
        {
            Assert.That(
                IsConditionAlwaysFalse(
                    "string text",
                    "text == null && (text + \"X\") != \"X\""),
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
        public void ExecutionVisibility_SourceSwitchStatementPredicateContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("int value", "SourcePredicates.IsZeroWithSwitch(value) && value != 0", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourceSwitchStatementPatternPredicateContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse(
                    "int value",
                    "SourcePredicates.IsSmallPositiveWithSwitch(value) && (value <= 0 || value >= 10)",
                    SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourceMultiGuardIndexPredicateContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse(
                    "int[] values, int index",
                    "SourcePredicates.IsValidIndex(values, index) && (values == null || index < 0 || index >= values.Length)",
                    SourcePredicateSource),
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
        public void ExecutionVisibility_SourceHasTextGuardPredicateContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "SourcePredicates.HasTextWithGuard(text) && (text == null || text.Length <= 0)", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourceHasTextIfElsePredicateContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "SourcePredicates.HasTextWithIfElse(text) && (text == null || text.Length <= 0)", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourceHasTextLocalAliasPredicateContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "SourcePredicates.HasTextViaLocal(text) && (text == null || text.Length <= 0)", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourceHasTextLocalAssignmentPredicateContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("string text", "SourcePredicates.HasTextViaAssignment(text) && (text == null || text.Length <= 0)", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourceLocalAssignmentIntegerPredicateContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("int value", "SourcePredicates.IsPositiveAfterLocalAssignment(value) && value < -1", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_SourceBooleanPropertyContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("SourcePredicateBox box", "box.HasText && (box.Value == null || box.Value.Length <= 0)", SourcePredicateSource),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_InstanceSourceBooleanMethodContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("SourcePredicateBox box", "box.HasTextMethod() && (box.Value == null || box.Value.Length <= 0)", SourcePredicateSource),
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
        public void ExecutionVisibility_BitwiseBooleanAndContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("int value", "(value == 0) & (value != 0)"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_BitwiseBooleanOrFalseBranchContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("int value", "!((value < 0) | (value > 0)) && value != 0"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_BooleanExclusiveOrContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("bool left, bool right", "(left ^ right) && left == right"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_DefaultLiteralNullContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("string value", "value != null && value == default"),
                Is.True);
        }

        [Test]
        public void ExecutionVisibility_DefaultExpressionZeroContradiction_IsAlwaysFalse()
        {
            Assert.That(
                IsConditionAlwaysFalse("int value", "value == default(int) && value != 0"),
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
        public async Task Ps0002_CoalesceThrowAssignedNonNullContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string value)
    {
        var safe = value ?? throw new InvalidOperationException();
        if (safe == null)
        {
            Console.WriteLine(safe);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_ConditionalThrowAssignedNonNullContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string value)
    {
        var safe = value != null ? value : throw new InvalidOperationException();
        if (safe == null)
        {
            Console.WriteLine(safe);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_FreshObjectAssignedNonNullContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(bool flag)
    {
        object value = flag ? new object() : new object();
        if (value == null)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
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
        public async Task Ps0002_ImplicitElseMergedNonZeroGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(bool flag)
    {
        var divisor = 1;
        if (flag)
        {
            divisor = 2;
        }

        if (divisor == 0)
        {
            Console.WriteLine(divisor);
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
        public async Task Ps0002_BooleanPredicateAliasContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        var isZero = value == 0;
        if (isZero && value != 0)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_BitwiseBooleanAndContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        if ((value == 0) & (value != 0))
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_WideningIntegralCastContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        if ((long)value > 0L && value <= 0)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_EnumContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public enum Mode
{
    None = 0,
    Ready = 1
}

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Mode state)
    {
        if (state == Mode.Ready && state != Mode.Ready)
        {
            Console.WriteLine(state);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_WhileNormalExitConditionContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values, int index)
    {
        while (index < values.Length)
        {
            index++;
        }

        if (index < values.Length)
        {
            Console.WriteLine(index);
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
        public async Task Ps0002_ConditionalArrayLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(bool flag)
    {
        var values = flag ? new int[1] : new int[1];
        if (values.Length != 1)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_CoalescedArrayFallbackLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] input)
    {
        if (input != null)
        {
            return;
        }

        var values = input ?? new int[1];
        if (values.Length != 1)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullDominatedCoalesceAssignmentLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        if (values != null)
        {
            return;
        }

        values ??= new int[1];
        if (values.Length != 1)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_KnownNonNullCoalesceAssignmentLengthContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var values = new int[2];
        values ??= new int[1];
        if (values.Length != 2)
        {
            Console.WriteLine(values.Length);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullDominatedNullableCoalesceAssignmentContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int? maybe = null;
        maybe ??= 5;
        if (!maybe.HasValue || maybe.Value != 5)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_KnownHasValueNullableCoalesceAssignmentContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int? maybe = 7;
        maybe ??= 5;
        if (!maybe.HasValue || maybe.Value != 7)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableCoalesceAssignmentFallbackHasValueContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? maybe)
    {
        maybe ??= 5;
        if (!maybe.HasValue)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
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
        public async Task Ps0002_SourceHasTextGuardPredicateContradictoryImpureCall_DoesNotReport()
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
        if (SourcePredicates.HasTextWithGuard(text) && (text == null || text.Length <= 0))
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SourceHasTextIfElsePredicateContradictoryImpureCall_DoesNotReport()
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
        if (SourcePredicates.HasTextWithIfElse(text) && (text == null || text.Length <= 0))
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SourceHasTextLocalAliasPredicateContradictoryImpureCall_DoesNotReport()
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
        if (SourcePredicates.HasTextViaLocal(text) && (text == null || text.Length <= 0))
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SourceHasTextLocalAssignmentPredicateContradictoryImpureCall_DoesNotReport()
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
        if (SourcePredicates.HasTextViaAssignment(text) && (text == null || text.Length <= 0))
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SourceLocalAssignmentIntegerPredicateContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        if (SourcePredicates.IsPositiveAfterLocalAssignment(value) && value < -1)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SourceMultiGuardIndexPredicateContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values, int index)
    {
        if (SourcePredicates.IsValidIndex(values, index) &&
            (values == null || index < 0 || index >= values.Length))
        {
            Console.WriteLine(index);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SourceSwitchStatementPredicateContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        if (SourcePredicates.IsZeroWithSwitch(value) && value != 0)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SourceSwitchStatementPatternPredicateContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        if (SourcePredicates.IsSmallPositiveWithSwitch(value) &&
            (value <= 0 || value >= 10))
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SourceBooleanPropertyContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(SourcePredicateBox box)
    {
        if (box.HasText && (box.Value == null || box.Value.Length <= 0))
        {
            Console.WriteLine(box.Value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_InstanceSourceBooleanMethodContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

" + SourcePredicateSource + @"

public class TestClass
{
    [EnforcePure]
    public void TestMethod(SourcePredicateBox box)
    {
        if (box.HasTextMethod() && (box.Value == null || box.Value.Length <= 0))
        {
            Console.WriteLine(box.Value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_MetadataStringPredicateContradictoryBranch_DoesNotReport()
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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_StringConcatContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string left, string right)
    {
        var value = left + right;
        if (left == ""A"" && right == ""B"" && value != ""AB"")
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
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
        public async Task Ps0002_SwitchExpressionAssignedNonZeroGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int mode)
    {
        var divisor = mode switch
        {
            0 => 1,
            1 => 2,
            _ => 3
        };

        if (divisor == 0)
        {
            Console.WriteLine(divisor);
        }
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
        public async Task Ps0002_SwitchStatementExitingCasePostCondition_DoesNotReport()
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
            case 0:
                return;
        }

        if (x == 0)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_SwitchStatementContinuingMutationDoesNotUseStalePostCondition_Reports()
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
            case 0:
                return;
            default:
                x = 0;
                break;
        }

        if (x == 0)
        {
            Console.WriteLine(x);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
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
        public async Task Ps0002_NullableHasValueContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int? value = 5;
        if (!value.HasValue)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableEqualsConstantContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value == 5)
        {
            if (!value.HasValue || value.Value != 5)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableGreaterThanContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value > 0)
        {
            if (!value.HasValue || value.Value <= 0)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_RecursivePatternAliasMemberContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string value)
    {
        if (value is { Length: > 0 } text)
        {
            if (text == null || text.Length <= 0)
            {
                Console.WriteLine(text);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableNotNullGuardContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value != null)
        {
            if (!value.HasValue)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableNullGuardContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value == null)
        {
            if (value.HasValue)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableIsNotNullPatternContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value is not null)
        {
            if (!value.HasValue)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableIsNullPatternContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value is null)
        {
            if (value.HasValue)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableRecursivePatternContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value is { })
        {
            if (!value.HasValue)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableNotRecursivePatternContradictoryImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int? value)
    {
        if (value is not { })
        {
            if (value.HasValue)
            {
                Console.WriteLine(value);
            }
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableDefaultReassignmentReachableGuard_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int? value = 5;
        value = default;
        if (!value.HasValue)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_AsExpressionNullSourceContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        object value = null;
        var text = value as string;
        if (text != null)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_AsExpressionNonNullSourceNullResultGuard_RemainsConservativeReports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        object value = new object();
        var text = value as string;
        if (text == null)
        {
            Console.WriteLine(text);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.True);
        }

        [Test]
        public async Task Ps0002_ConditionalAccessNullSourceHasValueGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        string text = null;
        int? length = text?.Length;
        if (length.HasValue)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_ConditionalAccessNonNullSourceHasValueGuard_Reports()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        string text = ""value"";
        int? length = text?.Length;
        if (length.HasValue)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
        }

        [Test]
        public async Task Ps0002_ConditionalAccessNullableValueContradictoryGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        string text = ""abc"";
        int? length = text?.Length;
        if (length.HasValue && length.Value != 3)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableCoalesceConditionalAccessNonNullReceiverContradictoryGuard_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        string text = ""abc"";
        int length = text?.Length ?? 0;
        if (length != 3)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableCoalesceConditionalAccessNullReceiverContradictoryGuard_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        string text = null;
        int length = text?.Length ?? 0;
        if (length != 0)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableDeclarationPatternNullInputGuardedImpureCall_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int? maybe = null;
        if (maybe is int value)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableDeclarationPatternBindingContradictoryGuard_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int? maybe = 5;
        if (maybe is int value && value != 5)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0002_NullableRelationalPatternContradictoryGuard_DoesNotReport()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        int? maybe = 5;
        if (maybe is < 0)
        {
            Console.ReadLine();
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.ReadLine", StringComparison.Ordinal) == true),
                Is.False);
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
        public async Task Ps0010_BitwiseBooleanAndGuardImpliesZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor, bool ready)
    {
        if ((divisor == 0) & ready)
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
        public async Task Ps0010_WideningIntegralCastGuardImpliesZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if ((long)divisor == 0L)
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
        public async Task Ps0010_EnumGuardImpliesZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public enum Mode
{
    None = 0,
    Ready = 1
}

public class TestClass
{
    public int TestMethod(int value, Mode state)
    {
        if (state == Mode.None)
        {
            return value / (int)state;
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
        public async Task Ps0010_IfElseElseExitImpliesZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
        }
        else
        {
            return 0;
        }

        return 10 / divisor;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_DefaultLiteralDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        int divisor = default;
        return 10 / divisor;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_BooleanPredicateAliasImpliesZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        var isZero = divisor == 0;
        if (isZero)
        {
            return 10 / divisor;
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
        public async Task Ps0010_SourceGuardPredicateImpliesZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
" + SourcePredicateSource + @"

public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (SourcePredicates.IsZeroWithGuard(divisor))
        {
            return 10 / divisor;
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
        public async Task Ps0010_SourceLocalAliasPredicateImpliesZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
" + SourcePredicateSource + @"

public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (SourcePredicates.IsZeroViaLocal(divisor))
        {
            return 10 / divisor;
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
        public async Task Ps0010_SourceLocalAssignmentPredicateImpliesZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
" + SourcePredicateSource + @"

public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (SourcePredicates.IsZeroViaAssignment(divisor))
        {
            return 10 / divisor;
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
        public async Task Ps0010_SourceSwitchStatementPredicateImpliesZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
" + SourcePredicateSource + @"

public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (SourcePredicates.IsZeroWithSwitch(divisor))
        {
            return 10 / divisor;
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
        public async Task Ps0010_SourceMultiGuardIndexPredicateInRange_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
" + SourcePredicateSource + @"

public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (SourcePredicates.IsValidIndex(values, index))
        {
            return values[index];
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SourceBooleanPropertyImpliesZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
" + SourcePredicateSource + @"

public class TestClass
{
    public int TestMethod(SourcePredicateBox box)
    {
        if (box.IsZeroDivisor)
        {
            return 10 / box.Divisor;
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
        public async Task Ps0010_InstanceSourceBooleanMethodImpliesZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
" + SourcePredicateSource + @"

public class TestClass
{
    public int TestMethod(SourcePredicateBox box)
    {
        if (box.IsZeroDivisorMethod())
        {
            return 10 / box.Divisor;
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
        public async Task Ps0010_RelationalPatternVariableBindingExcludesZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value is > 0 and var divisor)
        {
            return 10 / divisor;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_PropertyPatternVariableBindingExcludesZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string text)
    {
        if (text is { Length: > 0 and var length })
        {
            return 10 / length;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchStatementPatternVariableBindingExcludesZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case > 0 and var divisor:
                return 10 / divisor;
            default:
                return 0;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchStatementPriorSectionExcludesZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 0:
                return 0;
            case var divisor:
                return 10 / divisor;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchExpressionPatternVariableBindingExcludesZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value switch
        {
            > 0 and var divisor => 10 / divisor,
            _ => 0
        };
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchExpressionFallbackExcludesZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value switch
        {
            0 => 0,
            _ => 10 / value
        };
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchExpressionAssignedNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int mode)
    {
        var divisor = mode switch
        {
            0 => 1,
            1 => 2,
            _ => 3
        };

        return value / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchStatementAssignedNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int mode)
    {
        var divisor = 0;
        switch (mode)
        {
            case 0:
                divisor = 1;
                break;
            case 1:
                divisor = 2;
                break;
            default:
                divisor = 3;
                break;
        }

        return value / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchStatementDefaultExcludesZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        switch (divisor)
        {
            case 0:
                return 0;
            default:
                return 10 / divisor;
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchStatementExitingCaseExcludesZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        switch (divisor)
        {
            case 0:
                return 0;
        }

        return 10 / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchStatementContinuingMutationReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        switch (divisor)
        {
            case 0:
                return 0;
            default:
                divisor = 0;
                break;
        }

        return 10 / divisor;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
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
        public async Task Ps0010_CompoundAssignedNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 0;
        divisor += 1;
        return 10 / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_IncrementedNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 0;
        divisor++;
        return 10 / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_TupleAssignedNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 0;
        var other = 0;
        (divisor, other) = (1, 2);
        return 10 / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_TupleDeconstructionDeclaredNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var (divisor, other) = (1, 2);
        return 10 / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_InlineFiniteArrayElementAssignedNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = (new[] { 1, 2 })[0];
        return 10 / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_PriorFiniteArrayElementAssignedNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new[] { 1, 2 };
        var divisor = values[0];
        return 10 / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_InlineFiniteArrayFromEndElementAssignedNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = (new[] { 1, 2 })[^1];
        return 10 / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_PriorFiniteArrayFromEndElementAssignedNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new[] { 1, 2 };
        var divisor = values[^1];
        return 10 / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ConditionalFiniteArrayElementAssignedNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var values = new[] { 1, 2 };
        var divisor = flag ? values[0] : values[1];
        return 10 / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_TupleElementAssignedNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (1, 2);
        var divisor = pair.Item1;
        return 10 / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NamedTupleElementAssignedNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (divisor: 1, other: 2);
        var divisor = pair.divisor;
        return 10 / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_TupleLocalDeconstructionAssignedNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (1, 2);
        var divisor = 0;
        var other = 0;
        (divisor, other) = pair;
        return 10 / divisor;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_TupleLocalDeconstructionDeclaredNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (1, 2);
        var (divisor, other) = pair;
        return 10 / divisor;
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
        public async Task Ps0010_ListPatternElementBindingNonZero_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [> 0 and var divisor, ..])
        {
            return 10 / divisor;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_TrailingListPatternElementBindingNonZero_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [.., > 0 and var divisor])
        {
            return 10 / divisor;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ArrayElementReadFromListPatternFacts_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [> 0, ..])
        {
            var divisor = values[0];
            return 10 / divisor;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ArrayElementWriteThenReadZeroDivisor_ReportsDivideByZero()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        values[0] = 0;
        var divisor = values[0];
        return 10 / divisor;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
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
        public async Task Ps0010_ConditionalArrayLengthIndex_ReportsIndexOutOfRange()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var values = flag ? new int[1] : new int[1];
        return values[1];
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_CoalescedArrayFallbackLengthIndex_ReportsIndexOutOfRange()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        if (input != null)
        {
            return 0;
        }

        var values = input ?? new int[1];
        return values[1];
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_NullDominatedCoalesceAssignmentLengthIndex_ReportsIndexOutOfRange()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values != null)
        {
            return 0;
        }

        values ??= new int[1];
        return values[1];
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_KnownNonNullCoalesceAssignmentLengthIndex_ReportsIndexOutOfRange()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[2];
        values ??= new int[1];
        return values[2];
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_WhileNormalExitIndex_ReportsIndexOutOfRange()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        while (index < values.Length)
        {
            index++;
        }

        return values[index];
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_WhileBreakExitIndex_RemainsConservativeDoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values, int index, bool stop)
    {
        while (index < values.Length)
        {
            if (stop)
            {
                break;
            }

            index++;
        }

        return values[index];
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_EmptyArrayFromEndIndex_ReportsIndexOutOfRange()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[0];
        return values[^1];
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_EmptyStringFromEndIndex_ReportsIndexOutOfRange()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public char TestMethod()
    {
        var text = string.Empty;
        return text[^1];
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_FromEndZeroIndex_ReportsIndexOutOfRange()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        return values[^0];
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_NonEmptyListPatternFromEndIndex_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [_, ..])
        {
            return values[^1];
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
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
        public async Task Ps0010_NestedSliceListPatternIndex_DoesNotReport()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [.. [_, _]])
        {
            return values[1];
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
        public async Task Ps0010_DefaultLiteralReference_ReportsNullReference()
        {
            var diagnostics = await GetExceptionDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        string value = default;
        return value.Length;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
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

        private static string[] CollectCompletedLoopExitFacts(string source, string loopPrefix)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "CompletedLoopFactHost",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var loopStatement = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<StatementSyntax>()
                .Single(node => node.ToString().StartsWith(loopPrefix, StringComparison.Ordinal));

            return SymbolicProgramPointFacts
                .CollectCompletedLoopExitInvariantFacts(loopStatement, semanticModel, CancellationToken.None)
                .Select(static fact => fact.ToString() ?? string.Empty)
                .ToArray();
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
