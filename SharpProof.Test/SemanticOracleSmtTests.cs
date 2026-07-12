using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SearchLib.Purity;
using SearchLib.Smt;
using SharpProof.Analyzer;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using SharpProof.Test.Smt;
using CanonicalSymbolicLowering = SharpProof.Test.TypedSymbolicTestLowering;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
[Category("SmtHeavy")]
public class SemanticOracleSmtTests
{
    private const string GeneratedRegexFactorySource = @"
using System.Text.RegularExpressions;

public static partial class RegexFactories
{
    [GeneratedRegex(@""\AAB\z"")]
    public static partial Regex Ab();

    [GeneratedRegex(@""\A.\z"", RegexOptions.Singleline)]
    public static partial Regex SinglelineAny();
}";

    private const string StaticRegexCacheSource = @"
using System.Text.RegularExpressions;

public static class RegexCache
{
    public static readonly Regex Ab = new Regex(@""\AAB\z"");

    public static Regex MutableAb = new Regex(@""\AAB\z"");
}";

    private const string InstanceRegexCacheSource = @"
using System.Text.RegularExpressions;

public sealed class RegexBox
{
    public readonly Regex Ab = new Regex(@""\AAB\z"");

    public readonly Regex SinglelineAny = new Regex(@""\A.\z"", RegexOptions.Singleline);

    public readonly Regex MultilineAb = new Regex(@""\AAB\z"", RegexOptions.Multiline);

    public Regex MutableAb = new Regex(@""\AAB\z"");
}

public sealed class GeneratedRegexBox
{
    public readonly Regex Ab = RegexFactories.Ab();
}

public sealed class ConstructorAssignedRegexBox
{
    public readonly Regex Ab;

    public ConstructorAssignedRegexBox()
    {
        Ab = new Regex(@""\AAB\z"");
    }
}

public static class StaticCtorRegexCache
{
    public static readonly Regex Ab = new Regex(@""\AAB\z"");

    static StaticCtorRegexCache()
    {
        Ab = new Regex(@""\ACD\z"");
    }
}";

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

    private const string ExtendedPropertyPatternSource = @"
public sealed class ExtendedPatternBox
{
    public ExtendedPatternBox(ExtendedPatternChild child)
    {
        Child = child;
    }

    public ExtendedPatternChild Child { get; }
}

public sealed class ExtendedPatternChild
{
    public ExtendedPatternChild(int value)
    {
        Value = value;
    }

    public int Value { get; }
}
";

    private const string NotNullIfNotNullSource = @"
using System.Diagnostics.CodeAnalysis;

public static class NotNullIfNotNullPredicates
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string Echo(string value) => value;
}

public sealed class NotNullIfNotNullIndexer
{
    [NotNullIfNotNull(""key"")]
    public string this[string key] => key;
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
            "SharpProof.SymbolicFileQuery." + Guid.NewGuid().ToString("N") + ".cs");
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
        var context =
            AnalyzerTestHost.CreateConditionContext("bool flag, int x, int y",
                "(flag ? x : y) == 10 && flag && x != 10");
        using var oracle = new SmtPathOracle();

        Assert.That(
            oracle.IsSatisfiable(context.Expression, context.SemanticModel, TimeSpan.FromMilliseconds(250)),
            Is.EqualTo(Feasibility.Unsatisfiable));
    }

    [Test]
    public void Oracle_CoalesceExpressionContradiction_IsUnsatisfiable()
    {
        var context = AnalyzerTestHost.CreateConditionContext("string value, string fallback",
            "(value ?? fallback) == null && value != null");
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
            oracle.Implies(context.PathCondition, context.Conclusion, context.SemanticModel,
                TimeSpan.FromMilliseconds(50)),
            Is.EqualTo(Feasibility.Unsatisfiable));
    }

    [Test]
    public void Oracle_DisjunctiveNonZeroGuard_ImpliesNotZero()
    {
        var context =
            AnalyzerTestHost.CreateConditionImplicationContext("int divisor", "divisor < 0 || divisor > 0",
                "divisor != 0");
        using var oracle = new SmtPathOracle();

        Assert.That(
            oracle.Implies(context.PathCondition, context.Conclusion, context.SemanticModel,
                TimeSpan.FromMilliseconds(50)),
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
            oracle.Implies(context.PathCondition, context.Conclusion, context.SemanticModel,
                TimeSpan.FromMilliseconds(50)),
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
        var (semanticModel, root) = CreateElementAccessRangeFormulaHost(
            source,
            "ElementAccessInRangeHost");
        var guard = root.DescendantNodes().OfType<IfStatementSyntax>().Single().Condition;
        var elementAccess = root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();

        Assert.That(
            CanonicalSymbolicLowering.TryCreateBuiltInElementAccessInRangeCondition(
                elementAccess,
                semanticModel,
                CancellationToken.None,
                out var inRangeFormula),
            Is.True);
        Assert.That(
            CanonicalSymbolicLowering.TryTranslateConditionFormula(
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
    public void CSharpConditionToFormula_ElementAccessInRange_TranslatesMultidimensionalArrayBounds()
    {
        var source = @"
public class TestClass
{
    public int TestMethod(int[,] values, int row, int column)
    {
        if (row >= 0 && row < values.GetLength(0) && column >= 0 && column < values.GetLength(1))
        {
            return values[row, column];
        }

        return 0;
    }
}";
        var (semanticModel, root) = CreateElementAccessRangeFormulaHost(
            source,
            "ElementAccessMultidimensionalInRangeHost");
        var guard = root.DescendantNodes().OfType<IfStatementSyntax>().Single().Condition;
        var elementAccess = root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();

        Assert.That(
            CanonicalSymbolicLowering.TryCreateBuiltInElementAccessInRangeCondition(
                elementAccess,
                semanticModel,
                CancellationToken.None,
                out var inRangeFormula),
            Is.True);
        Assert.That(
            CanonicalSymbolicLowering.TryTranslateConditionFormula(
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
    public void CSharpConditionToFormula_ElementAccessInRange_TranslatesRangeEndpoints()
    {
        var source = @"
public class TestClass
{
    public int[] TestMethod(int[] values)
    {
        if (values.Length >= 2)
        {
            return values[1..^1];
        }

        return values;
    }
}";
        var (semanticModel, root) = CreateElementAccessRangeFormulaHost(
            source,
            "ElementAccessRangeInRangeHost");
        var guard = root.DescendantNodes().OfType<IfStatementSyntax>().Single().Condition;
        var elementAccess = root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();

        Assert.That(
            CanonicalSymbolicLowering.TryCreateBuiltInElementAccessInRangeCondition(
                elementAccess,
                semanticModel,
                CancellationToken.None,
                out var inRangeFormula),
            Is.True);
        Assert.That(
            CanonicalSymbolicLowering.TryTranslateConditionFormula(
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
    public void CSharpConditionToFormula_ElementAccessInRange_ProvesInvalidConstantRangeOutOfRange()
    {
        var source = @"
public class TestClass
{
    public int[] TestMethod(int[] values)
    {
        return values[2..1];
    }
}";
        var (semanticModel, root) = CreateElementAccessRangeFormulaHost(
            source,
            "ElementAccessInvalidRangeHost");
        var elementAccess = root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();

        Assert.That(
            CanonicalSymbolicLowering.TryCreateBuiltInElementAccessInRangeCondition(
                elementAccess,
                semanticModel,
                CancellationToken.None,
                out var inRangeFormula),
            Is.True);

        var outOfRangeFormula = new SmtUnaryFormula(SmtUnaryOperator.Not, inRangeFormula);
        var proof = new SmtAnalysisService(SmtAnalysisOptions.Default)
            .ClassifyImplication(Array.Empty<SmtFormula>(), outOfRangeFormula);

        Assert.That(proof.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
    }

    [Test]
    public void CSharpConditionToFormula_ElementAccessInRange_TranslatesLocalRangeEndpoints()
    {
        var source = @"
using System;

public class TestClass
{
    public int[] TestMethod(int[] values, int start, int end)
    {
        if (start >= 0 && start <= end && end <= values.Length)
        {
            Range range = start..end;
            return values[range];
        }

        return values;
    }
}";
        var (semanticModel, root) = CreateElementAccessRangeFormulaHost(
            source,
            "ElementAccessLocalRangeInRangeHost");
        var guard = root.DescendantNodes().OfType<IfStatementSyntax>().Single().Condition;
        var elementAccess = root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();

        Assert.That(
            CanonicalSymbolicLowering.TryCreateBuiltInElementAccessInRangeCondition(
                elementAccess,
                semanticModel,
                CancellationToken.None,
                out var inRangeFormula),
            Is.True);
        Assert.That(
            CanonicalSymbolicLowering.TryTranslateConditionFormula(
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
    public void CSharpConditionToFormula_ElementAccessInRange_TranslatesParameterAssignedFromEndRangeEndpoints()
    {
        var source = @"
using System;

public class TestClass
{
    public int[] TestMethod(int[] values, Range range)
    {
        if (values.Length >= 2)
        {
            range = 1..^1;
            return values[range];
        }

        return values;
    }
}";
        var (semanticModel, root) = CreateElementAccessRangeFormulaHost(
            source,
            "ElementAccessParameterRangeInRangeHost");
        var guard = root.DescendantNodes().OfType<IfStatementSyntax>().Single().Condition;
        var elementAccess = root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();

        Assert.That(
            CanonicalSymbolicLowering.TryCreateBuiltInElementAccessInRangeCondition(
                elementAccess,
                semanticModel,
                CancellationToken.None,
                out var inRangeFormula),
            Is.True);
        Assert.That(
            CanonicalSymbolicLowering.TryTranslateConditionFormula(
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
    public void CSharpConditionToFormula_ElementAccessInRange_ProvesInvalidLocalRangeOutOfRange()
    {
        var source = @"
using System;

public class TestClass
{
    public int[] TestMethod(int[] values)
    {
        Range range = 2..1;
        return values[range];
    }
}";
        var (semanticModel, root) = CreateElementAccessRangeFormulaHost(
            source,
            "ElementAccessInvalidLocalRangeHost");
        var elementAccess = root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();

        Assert.That(
            CanonicalSymbolicLowering.TryCreateBuiltInElementAccessInRangeCondition(
                elementAccess,
                semanticModel,
                CancellationToken.None,
                out var inRangeFormula),
            Is.True);

        var outOfRangeFormula = new SmtUnaryFormula(SmtUnaryOperator.Not, inRangeFormula);
        var proof = new SmtAnalysisService(SmtAnalysisOptions.Default)
            .ClassifyImplication(Array.Empty<SmtFormula>(), outOfRangeFormula);

        Assert.That(proof.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
    }

    [Test]
    public void CSharpConditionToFormula_ElementAccessInRange_TranslatesLatestReassignedLocalRange()
    {
        var source = @"
using System;

public class TestClass
{
    public int[] TestMethod(int[] values)
    {
        Range range = 0..^0;
        range = 1..^1;
        return values[range];
    }
}";
        var (semanticModel, root) = CreateElementAccessRangeFormulaHost(
            source,
            "ElementAccessReassignedLocalRangeHost");
        var elementAccess = root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();

        Assert.That(
            CanonicalSymbolicLowering.TryCreateBuiltInElementAccessInRangeCondition(
                elementAccess,
                semanticModel,
                CancellationToken.None,
                out var inRangeFormula),
            Is.True);
        Assert.That(inRangeFormula, Is.Not.Null);
    }

    [Test]
    public void CSharpConditionToFormula_ElementAccessInRange_RejectsUnknownReassignedLocalRange()
    {
        var source = @"
using System;

public class TestClass
{
    public int[] TestMethod(int[] values, Range other)
    {
        Range range = 0..^0;
        range = other;
        return values[range];
    }
}";
        var (semanticModel, root) = CreateElementAccessRangeFormulaHost(
            source,
            "ElementAccessUnknownReassignedLocalRangeHost");
        var elementAccess = root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();

        Assert.That(
            CanonicalSymbolicLowering.TryCreateBuiltInElementAccessInRangeCondition(
                elementAccess,
                semanticModel,
                CancellationToken.None,
                out _),
            Is.False);
    }

    private static (SemanticModel SemanticModel, SyntaxNode Root) CreateElementAccessRangeFormulaHost(
        string source,
        string assemblyName)
    {
        var context = AnalyzerTestHost.CreateSourceContext(
            source,
            assemblyName,
            AnalyzerTestHost.GetMinimalFrameworkReferences());

        return (context.SemanticModel, context.Root);
    }

    [Test]
    public void CSharpConditionToFormula_MathMin_ProvesUpperBound()
    {
        var (semanticModel, root) = CreateElementAccessRangeFormulaHost(
            @"
public class TestClass
{
    public int TestMethod(int value)
    {
        return System.Math.Min(value, 10);
    }
}",
            "MathMinFormulaHost");
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();

        Assert.That(
            CanonicalSymbolicLowering.TryTranslateValueWithPathFacts(
                invocation,
                semanticModel,
                CancellationToken.None,
                Array.Empty<SmtFormula>(),
                out var minFormula),
            Is.True);
        Assert.That(minFormula, Is.Not.Null);

        var proof = new SmtAnalysisService(SmtAnalysisOptions.Default)
            .ClassifyImplication(
                Array.Empty<SmtFormula>(),
                new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, minFormula!, new SmtIntegerConstant(10)));

        Assert.That(proof.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
    }

    [Test]
    public void CSharpConditionToFormula_MathMax_ProvesLowerBound()
    {
        var (semanticModel, root) = CreateElementAccessRangeFormulaHost(
            @"
public class TestClass
{
    public int TestMethod(int value)
    {
        return System.Math.Max(value, 0);
    }
}",
            "MathMaxFormulaHost");
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();

        Assert.That(
            CanonicalSymbolicLowering.TryTranslateValueWithPathFacts(
                invocation,
                semanticModel,
                CancellationToken.None,
                Array.Empty<SmtFormula>(),
                out var maxFormula),
            Is.True);
        Assert.That(maxFormula, Is.Not.Null);

        var proof = new SmtAnalysisService(SmtAnalysisOptions.Default)
            .ClassifyImplication(
                Array.Empty<SmtFormula>(),
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, maxFormula!, new SmtIntegerConstant(0)));

        Assert.That(proof.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
    }

    [Test]
    public void CSharpConditionToFormula_MathClamp_UsesLengthGuardForIndexBounds()
    {
        var (semanticModel, root) = CreateElementAccessRangeFormulaHost(
            @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (values.Length > 0)
        {
            return System.Math.Clamp(index, 0, values.Length - 1);
        }

        return 0;
    }
}",
            "MathClampFormulaHost");
        var guard = root.DescendantNodes().OfType<IfStatementSyntax>().Single().Condition;
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var lengthExpression = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .First(memberAccess => string.Equals(memberAccess.ToString(), "values.Length", StringComparison.Ordinal));

        Assert.That(
            CanonicalSymbolicLowering.TryTranslateConditionFormula(
                guard,
                semanticModel,
                CancellationToken.None,
                out var guardFormula),
            Is.True);
        Assert.That(guardFormula, Is.Not.Null);
        Assert.That(
            CanonicalSymbolicLowering.TryTranslateValueWithPathFacts(
                invocation,
                semanticModel,
                CancellationToken.None,
                new[] { guardFormula! },
                out var clampedFormula),
            Is.True);
        Assert.That(clampedFormula, Is.Not.Null);
        Assert.That(
            CanonicalSymbolicLowering.TryTranslateValue(
                lengthExpression,
                semanticModel,
                CancellationToken.None,
                out var lengthFormula,
                null),
            Is.True);
        Assert.That(lengthFormula, Is.Not.Null);

        var inRangeFormula = new SmtBinaryFormula(
            SmtBinaryOperator.And,
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, clampedFormula!, new SmtIntegerConstant(0)),
            new SmtBinaryFormula(SmtBinaryOperator.LessThan, clampedFormula!, lengthFormula!));
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
    public void ExecutionVisibility_UncheckedOverflowBoundary_IsNotAlwaysFalse()
    {
        Assert.That(IsConditionAlwaysFalse("int x", "x + 1 < x"), Is.False);
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
    public void ExecutionVisibility_CoalesceRightInvocationAfterNullExit_IsStaticallyUnreachable()
    {
        Assert.That(
            IsExpressionUnreachable(
                @"
public class TestClass
{
    public string TestMethod(string value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value ?? Impure();
    }

    private static string Impure() => string.Empty;
}",
                "Impure()"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_ConditionalAccessInvocationAfterNonNullExit_IsStaticallyUnreachable()
    {
        Assert.That(
            IsExpressionUnreachable(
                @"
public class TestClass
{
    public string TestMethod(Worker worker)
    {
        if (worker != null)
        {
            return string.Empty;
        }

        return worker?.Impure() ?? string.Empty;
    }
}

public sealed class Worker
{
    public string Impure() => string.Empty;
}",
                ".Impure()"),
            Is.True);
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
    public void ExecutionVisibility_EnumLocalAssignment_PrunesUnreachableIfBranch()
    {
        Assert.That(
            IsStatementUnreachable(
                @"
public enum Mode
{
    None = 0,
    Ready = 1
}

public class TestClass
{
    public int TestMethod()
    {
        Mode state;
        state = Mode.Ready;
        if (state != Mode.Ready)
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
    public void ExecutionVisibility_EarlyExitGuardPrunesSwitchSectionWithPathFacts()
    {
        Assert.That(
            IsStatementUnreachable(
                @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value < 0)
        {
            return 0;
        }

        switch (value)
        {
            case < 0:
                return 1;
        }

        return 2;
    }
}",
                "return 1;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_EarlyExitGuardMutationBeforeSwitch_RemainsConservative()
    {
        Assert.That(
            IsStatementUnreachable(
                @"
public class TestClass
{
    public int TestMethod(int value, int replacement)
    {
        if (value < 0)
        {
            return 0;
        }

        value = replacement;
        switch (value)
        {
            case < 0:
                return 1;
        }

        return 2;
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
        Assert.That(facts.Any(fact => fact.Contains("!", StringComparison.Ordinal) &&
                                      fact.Contains("<", StringComparison.Ordinal) &&
                                      fact.Contains("Length", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void SymbolicProgramPointFacts_CollectCompletedLoopExitInvariantFacts_ReturnsForLoopExitFacts()
    {
        var facts = SemanticOracleSmtTestBase.CollectCompletedLoopExitFacts(
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
        Assert.That(facts.Any(fact => fact.Contains("!", StringComparison.Ordinal) &&
                                      fact.Contains("<", StringComparison.Ordinal) &&
                                      fact.Contains("Length", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void
        SymbolicProgramPointFacts_CollectCompletedLoopExitInvariantFacts_SuppressesLoopExitFactsWhenBreakCanExitLoop()
    {
        var facts = SemanticOracleSmtTestBase.CollectCompletedLoopExitFacts(
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
        Assert.That(facts.Any(fact => fact.Contains("Regex.IsMatch", StringComparison.Ordinal) &&
                                      fact.Contains("^[A-Z][0-9]$", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void SymbolicInvariantService_CollectsRegexMatchSuccessReachabilityFacts()
    {
        var facts = CollectProgramPointFacts(
            @"
using System.Text.RegularExpressions;

public class TestClass
{
    public int TestMethod(string input)
    {
        if (Regex.Match(input, ""^[A-Z][0-9]$"").Success)
        {
            return input.Length;
        }

        return 0;
    }
}",
            "return input.Length;");

        Assert.That(facts, Is.Not.Empty);
        Assert.That(facts.Any(fact => fact.Contains("Regex.IsMatch", StringComparison.Ordinal) &&
                                      fact.Contains("^[A-Z][0-9]$", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void SymbolicInvariantService_CollectsRegexMatchesCountReachabilityFacts()
    {
        var facts = CollectProgramPointFacts(
            @"
using System.Text.RegularExpressions;

public class TestClass
{
    public int TestMethod(string input)
    {
        if (Regex.Matches(input, ""^[A-Z][0-9]$"").Count > 0)
        {
            return input.Length;
        }

        return 0;
    }
}",
            "return input.Length;");

        Assert.That(facts, Is.Not.Empty);
        Assert.That(facts.Any(fact => fact.Contains("Regex.IsMatch", StringComparison.Ordinal) &&
                                      fact.Contains("^[A-Z][0-9]$", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void SymbolicInvariantService_CollectsLocalRegexReachabilityFacts()
    {
        var facts = CollectProgramPointFacts(
            @"
using System.Text.RegularExpressions;

public class TestClass
{
    public int TestMethod(string input)
    {
        var regex = new Regex(@""\A[A-Z][0-9]\z"");
        if (regex.IsMatch(input))
        {
            return input.Length;
        }

        return 0;
    }
}",
            "return input.Length;");

        Assert.That(facts, Is.Not.Empty);
        Assert.That(facts.Any(fact => fact.Contains("Regex.IsMatch", StringComparison.Ordinal) &&
                                      fact.Contains(@"\\A[A-Z][0-9]\\z", StringComparison.Ordinal)), Is.True);
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
        Assert.That(facts.Any(fact => fact.Contains(".Contains", StringComparison.Ordinal) &&
                                      fact.Contains("SKU", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void SymbolicInvariantService_CollectsOrdinalIgnoreCaseIndexOfReachabilityFacts()
    {
        var facts = CollectProgramPointFacts(
            @"
using System;

public class TestClass
{
    public int TestMethod(string input)
    {
        if (input.IndexOf(""sku"", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return input.Length;
        }

        return 0;
    }
}",
            "return input.Length;");

        Assert.That(facts, Is.Not.Empty);
        Assert.That(facts.Any(fact => fact.Contains("Regex.IsMatch", StringComparison.Ordinal) &&
                                      fact.Contains("sku", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void SymbolicInvariantService_CollectsOrdinalIgnoreCaseStringEqualsReachabilityFacts()
    {
        var facts = CollectProgramPointFacts(
            @"
using System;

public class TestClass
{
    public int TestMethod(string input)
    {
        if (string.Equals(input, ""sku"", StringComparison.OrdinalIgnoreCase))
        {
            return input.Length;
        }

        return 0;
    }
}",
            "return input.Length;");

        Assert.That(facts, Is.Not.Empty);
        Assert.That(facts.Any(fact => fact.Contains("Regex.IsMatch", StringComparison.Ordinal) &&
                                      fact.Contains(@"\\Asku\\z", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void SymbolicInvariantService_CollectsOrdinalIgnoreCaseStringPredicateReachabilityFacts()
    {
        var facts = CollectProgramPointFacts(
            @"
using System;

public class TestClass
{
    public int TestMethod(string input)
    {
        if (input.Contains(""sku"", StringComparison.OrdinalIgnoreCase) &&
            input.StartsWith(""pre"", StringComparison.OrdinalIgnoreCase) &&
            input.EndsWith(""tail"", StringComparison.OrdinalIgnoreCase))
        {
            return input.Length;
        }

        return 0;
    }
}",
            "return input.Length;");

        Assert.That(facts, Is.Not.Empty);
        Assert.That(facts.Any(fact => fact.Contains("Regex.IsMatch", StringComparison.Ordinal) &&
                                      fact.Contains("sku", StringComparison.Ordinal)), Is.True);
        Assert.That(facts.Any(fact => fact.Contains("Regex.IsMatch", StringComparison.Ordinal) &&
                                      fact.Contains(@"\\Apre", StringComparison.Ordinal)), Is.True);
        Assert.That(facts.Any(fact => fact.Contains("Regex.IsMatch", StringComparison.Ordinal) &&
                                      fact.Contains(@"tail\\z", StringComparison.Ordinal)), Is.True);
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
        Assert.That(facts.Any(fact => fact.Contains("+", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains("<", StringComparison.Ordinal)), Is.True);
        Assert.That(facts.Any(fact => fact.Contains(">=", StringComparison.Ordinal)), Is.True);
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
                                      fact.Contains("&&", StringComparison.Ordinal) &&
                                      fact.Contains("<", StringComparison.Ordinal)), Is.True);
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
        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains("!", StringComparison.Ordinal) &&
                                      fact.Contains("==", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains(">", StringComparison.Ordinal) &&
                                      fact.Contains("value", StringComparison.Ordinal) &&
                                      fact.Contains("0", StringComparison.Ordinal)), Is.True);
        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains("!", StringComparison.Ordinal) &&
                                      fact.Contains("==", StringComparison.Ordinal) &&
                                      fact.Contains("value", StringComparison.Ordinal) &&
                                      fact.Contains("0", StringComparison.Ordinal)), Is.True);
        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains("!", StringComparison.Ordinal) &&
                                      fact.Contains("==", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
                                      fact.Contains("value", StringComparison.Ordinal) &&
                                      fact.Contains("0", StringComparison.Ordinal)), Is.True);
        Assert.That(facts.Any(fact => fact.Contains("!", StringComparison.Ordinal) &&
                                      fact.Contains("==", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains(">", StringComparison.Ordinal) &&
                                      fact.Contains("10", StringComparison.Ordinal)), Is.True);
        Assert.That(facts.Any(fact => fact.Contains("<", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains("!", StringComparison.Ordinal) &&
                                      fact.Contains("==", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains(">", StringComparison.Ordinal) &&
                                      fact.Contains("value", StringComparison.Ordinal) &&
                                      fact.Contains("0", StringComparison.Ordinal)), Is.True);
        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
                                      fact.Contains("first", StringComparison.Ordinal) &&
                                      fact.Contains("null", StringComparison.Ordinal)), Is.True);
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
        Assert.That(facts.Any(fact => fact.Contains("!=", StringComparison.Ordinal) &&
                                      fact.Contains("value", StringComparison.Ordinal) &&
                                      fact.Contains("null", StringComparison.Ordinal)), Is.True);
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
        Assert.That(result.MergedInvariantText, Does.Contain("&&"));
        Assert.That(result.Facts.Any(fact => fact.Contains("<", StringComparison.Ordinal)), Is.True);
        Assert.That(result.Facts.Any(fact => fact.Contains(">=", StringComparison.Ordinal)), Is.True);
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
        var context = AnalyzerTestHost.CreateSourceContext(
            source,
            "SymbolicProgramPointAnalysisHost",
            AnalyzerTestHost.GetMinimalFrameworkReferences());
        var statement = context.Root
            .DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Single(node => node.Expression?.ToString() == "value");

        var analysis = new SymbolicInvariantService().AnalyzeAt(statement, context.SemanticModel,
            cancellationToken: CancellationToken.None);

        Assert.That(analysis.PathConditions, Is.Not.Empty);
        Assert.That(analysis.PathConditions.Any(condition => condition is SmtBinaryFormula), Is.True);
        Assert.That(analysis.MergedInvariantText, Does.Contain("value > 0"));
        Assert.That(analysis.Facts, Does.Contain("value > 0"));
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
        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
        Assert.That(result.Analysis.PathConditions.Any(condition => condition is SmtBinaryFormula), Is.True);
        Assert.That(result.SymbolicFacts, Is.Not.Empty);
        Assert.That(result.MergedInvariantText, Is.EqualTo(result.Analysis.MergedInvariantText));
        Assert.That(result.Facts.Any(fact => fact.Contains("value > 0", StringComparison.Ordinal)), Is.True);
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

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
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
        Assert.That(result.ConditionProofs.Select(static proof => proof.TruthValue),
            Is.All.EqualTo(SymbolicTruthValue.ProvenTrue));
        Assert.That(result.SmtDiagnostics.IsConfigured, Is.True);
        Assert.That(result.SmtDiagnostics.Mode, Is.EqualTo(SmtAnalysisMode.Bounded));
        Assert.That(result.SmtDiagnostics.QueryTimeoutMs, Is.EqualTo(750));
        Assert.That(result.SmtDiagnostics.MethodBudgetMs, Is.EqualTo(5000));
        Assert.That(result.SmtDiagnostics.MaxPathConditions, Is.EqualTo(192));
        Assert.That(result.SmtDiagnostics.MaxExpressionNodes, Is.EqualTo(2048));
        Assert.That(result.SmtDiagnostics.ExecutedQueryCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.SmtDiagnostics.CacheEntryCount, Is.GreaterThanOrEqualTo(1));
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
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesSwitchStatementFallbackSourcePredicateExactValue()
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

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
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
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesInstanceSourceBooleanMethodLocalAliasExactValue()
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
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesArrayRangeResultLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values.Length >= 2)
        {
            return values[1..^1].Length;
        }

        return 0;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "ArrayRangeResultLength.cs",
            FindLine(source, "return values[1..^1].Length;"),
            20,
            "values[1..^1].Length == values.Length - 2",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesStringRangeResultLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(string text)
    {
        if (text != null && text.Length >= 3)
        {
            return text[1..^1].Length;
        }

        return 0;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "StringRangeResultLength.cs",
            FindLine(source, "return text[1..^1].Length;"),
            20,
            "text[1..^1].Length == text.Length - 2",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesStringSubstringOneArgumentResultLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(string text, int start)
    {
        if (text != null && start >= 0 && start <= text.Length)
        {
            return text.Substring(start).Length;
        }

        return 0;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "StringSubstringOneArgumentResultLength.cs",
            FindLine(source, "return text.Substring(start).Length;"),
            20,
            "text.Substring(start).Length == text.Length - start",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesStringSubstringTwoArgumentResultLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(string text, int start, int length)
    {
        if (text != null && start >= 0 && length >= 0 && start + length <= text.Length)
        {
            return text.Substring(start, length).Length;
        }

        return 0;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "StringSubstringTwoArgumentResultLength.cs",
            FindLine(source, "return text.Substring(start, length).Length;"),
            20,
            "text.Substring(start, length).Length == length",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesStringAsSpanOneArgumentResultLength()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(string text, int start)
    {
        if (text != null && start >= 0 && start <= text.Length)
        {
            return text.AsSpan(start).Length;
        }

        return 0;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "StringAsSpanOneArgumentResultLength.cs",
            FindLine(source, "return text.AsSpan(start).Length;"),
            20,
            "text.AsSpan(start).Length == text.Length - start",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesReadOnlySpanSliceTwoArgumentResultLength()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(ReadOnlySpan<int> values, int start, int length)
    {
        if (start >= 0 && length >= 0 && start + length <= values.Length)
        {
            return values.Slice(start, length).Length;
        }

        return 0;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "ReadOnlySpanSliceTwoArgumentResultLength.cs",
            FindLine(source, "return values.Slice(start, length).Length;"),
            20,
            "values.Slice(start, length).Length == length",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesAssignedRangeElementAccessResultLength()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values != null && values.Length >= 2)
        {
            Range range = 1..^1;
            int[] slice = values[range];
            return slice.Length;
        }

        return 0;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "AssignedRangeElementAccessResultLength.cs",
            FindLine(source, "return slice.Length;"),
            20,
            "slice.Length == values.Length - 2",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesAssignedRangeAsSpanResultLength()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(string text, Range range)
    {
        if (text != null && text.Length >= 2)
        {
            range = 1..^1;
            ReadOnlySpan<char> view = text.AsSpan(range);
            return view.Length;
        }

        return 0;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "AssignedRangeAsSpanResultLength.cs",
            FindLine(source, "return view.Length;"),
            20,
            "view.Length == text.Length - 2",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
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

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
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

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
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

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
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
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesCompletedLockReceiverNonNull()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(object gate)
    {
        lock (gate)
        {
        }

        if (gate == null)
        {
            return 1;
        }

        return 0;
    }
}";
        var result = new SymbolicSourceQueryService().AnalyzeSource(
            source,
            "CompletedLockReceiverNonNull.cs",
            FindLine(source, "return 1;"),
            17,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
        Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
        Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ReassignedCompletedLockReceiverDoesNotKeepNonNullFact()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(object gate)
    {
        lock (gate)
        {
            gate = null;
        }

        return gate.GetHashCode();
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "ReassignedCompletedLockReceiver.cs",
            FindLine(source, "return gate.GetHashCode();"),
            13,
            "gate != null",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
    }

    [Test]
    public void SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesThrowExpressionGuardedNullBranchUnreachable()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(string value)
    {
        _ = value ?? throw new InvalidOperationException();

        if (value == null)
        {
            return 1;
        }

        return value.Length;
    }
}";
        var result = new SymbolicSourceQueryService().AnalyzeSource(
            source,
            "ThrowExpressionGuardedNullBranch.cs",
            FindLine(source, "return 1;"),
            17,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
        Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
        Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesCompletedReceiverNullBranchUnreachable()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(object value)
    {
        var hash = value.GetHashCode();

        if (value == null)
        {
            return 1;
        }

        return hash;
    }
}";
        var result = new SymbolicSourceQueryService().AnalyzeSource(
            source,
            "CompletedReceiverNullBranch.cs",
            FindLine(source, "return 1;"),
            17,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
        Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
        Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void
        SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesCompletedAwaitedReceiverNullBranchUnreachable()
    {
        const string source = @"
using System.Threading.Tasks;

public sealed class Service
{
    public Task<int> GetAsync() => Task.FromResult(1);
}

public class TestClass
{
    public async Task<int> TestMethod(Service service)
    {
        var value = await service.GetAsync();

        if (service == null)
        {
            return 1;
        }

        return value;
    }
}";
        var result = new SymbolicSourceQueryService().AnalyzeSource(
            source,
            "CompletedAwaitedReceiverNullBranch.cs",
            FindLine(source, "return 1;"),
            17,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
        Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
        Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesCompletedAwaitableNullBranchUnreachable()
    {
        const string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> TestMethod(Task<int> task)
    {
        var value = await task;

        if (task == null)
        {
            return 1;
        }

        return value;
    }
}";
        var result = new SymbolicSourceQueryService().AnalyzeSource(
            source,
            "CompletedAwaitableNullBranch.cs",
            FindLine(source, "return 1;"),
            17,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
        Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
        Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_RefMutatedCompletedAwaitedReceiverDoesNotKeepNonNullFact()
    {
        const string source = @"
using System.Threading.Tasks;

public sealed class Service
{
    public Task<int> MutateAsync(ref Service value)
    {
        value = null;
        return Task.FromResult(1);
    }
}

public class TestClass
{
    public async Task<int> TestMethod(Service service)
    {
        await service.MutateAsync(ref service);
        return service == null ? 1 : 0;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "RefMutatedCompletedAwaitedReceiver.cs",
            FindLine(source, "return service == null ? 1 : 0;"),
            16,
            "service != null",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_RefMutatedCompletedReceiverDoesNotKeepNonNullFact()
    {
        const string source = @"
public sealed class Box
{
    public void Clear(ref Box value)
    {
        value = null;
    }
}

public class TestClass
{
    public int TestMethod(Box box)
    {
        box.Clear(ref box);
        return box.GetHashCode();
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "RefMutatedCompletedReceiver.cs",
            FindLine(source, "return box.GetHashCode();"),
            16,
            "box != null",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
    }

    [Test]
    public void
        SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesCompletedElementAccessOutOfRangeBranchUnreachable()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        _ = values[index];

        if (index < 0 || index >= values.Length)
        {
            return 1;
        }

        return 0;
    }
}";
        var result = new SymbolicSourceQueryService().AnalyzeSource(
            source,
            "CompletedElementAccessOutOfRangeBranch.cs",
            FindLine(source, "return 1;"),
            17,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
        Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
        Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
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

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
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

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
        Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
        Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesUsingExpressionThrowGuardedResourceNonNull()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(IDisposable value)
    {
        using (value ?? throw new InvalidOperationException())
        {
            return value.GetHashCode();
        }
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "UsingExpressionThrowGuardedResourceNonNull.cs",
            FindLine(source, "return value.GetHashCode();"),
            13,
            "value != null",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesUsingExpressionNullBranchUnreachable()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(IDisposable value)
    {
        using (value ?? throw new InvalidOperationException())
        {
            if (value == null)
            {
                return 1;
            }

            return 0;
        }
    }
}";
        var result = new SymbolicSourceQueryService().AnalyzeSource(
            source,
            "UsingExpressionNullBranchUnreachable.cs",
            FindLine(source, "return 1;"),
            17,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
        Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
        Assert.That(result.ReachabilityReason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void
        SymbolicSourceQueryService_AnalyzeSource_WithSmt_ReassignedUsingExpressionResourceKeepsNullBranchReachable()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(IDisposable value)
    {
        using (value ?? throw new InvalidOperationException())
        {
            value = null;
            if (value == null)
            {
                return 1;
            }

            return 0;
        }
    }
}";
        var result = new SymbolicSourceQueryService().AnalyzeSource(
            source,
            "ReassignedUsingExpressionResourceKeepsNullBranchReachable.cs",
            FindLine(source, "return 1;"),
            18,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

        Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Reachable));
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
        Assert.That(result.ConditionProofs.Select(static proof => proof.TruthValue),
            Is.All.EqualTo(SymbolicTruthValue.ProvenTrue));
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
        Assert.That(result.ConditionProofs.Select(static proof => proof.TruthValue),
            Is.All.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_QuerySource_ProvesWhileLoopMonotonicIndexBounds()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        var sum = 0;
        var index = 0;
        while (index < values.Length)
        {
            sum += values[index];
            index++;
        }

        return sum;
    }
}";
        var result = new SymbolicSourceQueryService().QuerySource(
            source,
            "WhileLoopMonotonicIndexBounds.cs",
            FindLine(source, "sum += values[index];"),
            20,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
            impliedConditions: new[] { "index >= 0", "index < values.Length" });

        Assert.That(result.ConditionProofs, Has.Count.EqualTo(2));
        Assert.That(result.ConditionProofs.Select(static proof => proof.TruthValue),
            Is.All.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_QuerySource_ProvesReverseWhileLoopMonotonicIndexBounds()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        var sum = 0;
        var index = values.Length - 1;
        while (index >= 0)
        {
            sum += values[index];
            index--;
        }

        return sum;
    }
}";
        var result = new SymbolicSourceQueryService().QuerySource(
            source,
            "ReverseWhileLoopMonotonicIndexBounds.cs",
            FindLine(source, "sum += values[index];"),
            20,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
            impliedConditions: new[] { "index >= 0", "index < values.Length" });

        Assert.That(result.ConditionProofs, Has.Count.EqualTo(2));
        Assert.That(result.ConditionProofs.Select(static proof => proof.TruthValue),
            Is.All.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_QuerySource_ProvesDoLoopPreEntryLowerBound()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var sum = 0;
        var index = 0;
        do
        {
            sum += index;
            index++;
        }
        while (index < 10);

        return sum;
    }
}";
        var result = new SymbolicSourceQueryService().QuerySource(
            source,
            "DoLoopPreEntryLowerBound.cs",
            FindLine(source, "sum += index;"),
            20,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default),
            impliedConditions: new[] { "index >= 0" });

        Assert.That(result.ConditionProofs, Has.Count.EqualTo(1));
        Assert.That(result.ConditionProofs[0].TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
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
    public void SymbolicSourceQueryService_QuerySource_DoesNotInferWhileLoopLowerBoundWhenBodyDecrements()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        var index = 1;
        while (index < values.Length)
        {
            var current = values[index];
            index--;
            return current;
        }

        return 0;
    }
}";
        var result = new SymbolicSourceQueryService().QuerySource(
            source,
            "WhileLoopDecrementNoLowerBound.cs",
            FindLine(source, "var current = values[index];"),
            24,
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
    public void SymbolicSourceQueryService_QuerySource_DoesNotInferReverseWhileLoopUpperBoundWhenBodyIncrements()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        var index = values.Length - 1;
        while (index >= 0)
        {
            var current = values[index];
            index++;
            return current;
        }

        return 0;
    }
}";
        var result = new SymbolicSourceQueryService().QuerySource(
            source,
            "ReverseWhileLoopIncrementNoUpperBound.cs",
            FindLine(source, "var current = values[index];"),
            24,
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
    public void SymbolicSourceQueryService_SwitchExitExclusionSubstitutesPatternBindingInGuard()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case int bound when bound > 0:
                return bound;
            default:
                break;
        }

        return value;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "SwitchPatternBindingExitExclusion.cs",
            FindLine(source, "return value;"),
            9,
            "value <= 0",
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
        Assert.That(result.Facts, Does.Contain("first == null"));
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
        Assert.That(result.Facts, Does.Contain("safe == value"));
        Assert.That(result.Facts, Does.Contain("value != null"));
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
    public void
        SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesCoalesceAssignmentGuardedFallbackNullBranchUnreachable()
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

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
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
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_PreservesKnownHasValueNullableCoalesceAssignmentValue()
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
    public void
        SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesNullableCoalesceAssignmentNoValueBranchUnreachable()
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

        Assert.That(result.Analysis.PathConditions, Is.Not.Empty);
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
        Assert.That(result.Facts, Does.Contain("safe == value"));
        Assert.That(result.Facts, Does.Contain("value != null"));
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
        Assert.That(result.Facts.Any(fact => fact.Contains("value > 0", StringComparison.Ordinal)), Is.True);
        Assert.That(result.Facts, Does.Contain("divisor == value"));
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
        Assert.That(result.Facts.Any(fact => fact.Contains("text.Length > 0", StringComparison.Ordinal)), Is.True);
        Assert.That(result.Facts, Does.Contain("length == text.Length"));
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
        Assert.That(result.Facts.Any(fact => fact.Contains("values[0] > 0", StringComparison.Ordinal)), Is.True);
        Assert.That(result.Facts, Does.Contain("divisor == values[0]"));
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
        Assert.That(result.Facts.Any(fact => fact.Contains("values[^1] > 0", StringComparison.Ordinal)), Is.True);
        Assert.That(result.Facts, Does.Contain("divisor == values[^1]"));
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
        Assert.That(result.Facts.Any(fact => fact.Contains("values[0] > 0", StringComparison.Ordinal)), Is.True);
        Assert.That(result.Facts, Does.Contain("divisor == values[0]"));
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

        Assert.That(result.Facts, Does.Contain("values[0] == 0"));
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
        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
                                      fact.Contains("divisor", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
                                      fact.Contains("divisor", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
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
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesInlineFiniteArrayFromEndElementAssignedNonZeroValue()
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
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesPriorFiniteArrayFromEndElementAssignedNonZeroValue()
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
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalFiniteArrayElementAssignedNonZeroValue()
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
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_DoesNotInferPriorFiniteArrayElementAfterUnknownReassignment()
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
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_DoesNotInferPriorFiniteArrayElementFromTargetSelfReference()
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
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesValueTuplePositionalPatternElementFact()
    {
        const string source = @"
using System;

public class TestClass
{
    public int TestMethod(ValueTuple<int, int> pair)
    {
        if (pair is (> 0, _))
        {
            return pair.Item1;
        }

        return 0;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "ValueTuplePositionalPatternElementFact.cs",
            FindLine(source, "return pair.Item1;"),
            20,
            "pair.Item1 > 0",
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
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleStringLiteralElementContent()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (text: ""abc"", other: 1);
        if (pair.text != ""abc"")
        {
            return 1;
        }

        return 0;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "TupleStringLiteralElementContent.cs",
            FindLine(source, "return 0;"),
            12,
            "pair.text == \"abc\"",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleStringLiteralElementLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (text: ""abc"", other: 1);
        return pair.text.Length;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "TupleStringLiteralElementLength.cs",
            FindLine(source, "return pair.text.Length;"),
            16,
            "pair.text.Length == 3",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleArrayElementLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (values: new int[2], other: 1);
        return pair.values.Length;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "TupleArrayElementLength.cs",
            FindLine(source, "return pair.values.Length;"),
            16,
            "pair.values.Length == 2",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleMultidimensionalArrayElementGetLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (values: new int[2, 3], other: 1);
        return pair.values.GetLength(1);
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "TupleMultidimensionalArrayElementGetLength.cs",
            FindLine(source, "return pair.values.GetLength(1);"),
            16,
            "pair.values.GetLength(1) == 3",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesCastedMultidimensionalArrayGetLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        return ((int[,])new int[2, 3]).GetLength(1);
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "CastedMultidimensionalArrayGetLength.cs",
            FindLine(source, "return ((int[,])new int[2, 3]).GetLength(1);"),
            16,
            "((int[,])new int[2, 3]).GetLength(1) == 3",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleDeconstructedArrayLength()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (new int[2], ""abc"");
        var (values, text) = pair;
        return values.Length + text.Length;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "TupleDeconstructedArrayLength.cs",
            FindLine(source, "return values.Length + text.Length;"),
            16,
            "values.Length == 2 && text.Length == 3",
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
        Assert.That(facts.Any(fact => fact.Contains("!", StringComparison.Ordinal) &&
                                      fact.Contains("==", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
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

        Assert.That(facts.Any(fact => fact.Contains("!", StringComparison.Ordinal) &&
                                      fact.Contains("==", StringComparison.Ordinal) &&
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

        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
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
            "divisor == 1",
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
            "divisor == 1",
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
        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
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
        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
                                      fact.Contains("value", StringComparison.Ordinal) &&
                                      fact.Contains("null", StringComparison.Ordinal)), Is.True);
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
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesExtendedPropertyPatternMemberFact()
    {
        const string source = ExtendedPropertyPatternSource + @"
public class TestClass
{
    public int TestMethod(ExtendedPatternBox box)
    {
        if (box is { Child.Value: > 0 })
        {
            return box.Child.Value;
        }

        return 0;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "ExtendedPropertyPatternMemberFact.cs",
            FindLine(source, "return box.Child.Value;"),
            20,
            "box.Child != null && box.Child.Value > 0",
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
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesAsExpressionNonNullResultImpliesRuntimeTypePredicate()
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
            "AsExpressionNonNullResultImpliesRuntimeTypePredicate.cs",
            FindLine(source, "return text;"),
            20,
            "value is string",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesAsExpressionNullResultAndSourceNonNullImpliesNegativeRuntimeTypePredicate()
    {
        const string source = @"
public class TestClass
{
    public string TestMethod(object value)
    {
        var text = value as string;
        if (text == null && value != null)
        {
            return string.Empty;
        }

        return text;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "AsExpressionNullResultAndSourceNonNullImpliesNegativeRuntimeTypePredicate.cs",
            FindLine(source, "return string.Empty;"),
            20,
            "value is not string",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesInlineAsAssignmentNonNullResultImpliesRuntimeTypePredicate()
    {
        const string source = @"
public class TestClass
{
    public string TestMethod(object value)
    {
        string text;
        if ((text = value as string) != null)
        {
            return text;
        }

        return string.Empty;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "InlineAsAssignmentNonNullResultImpliesRuntimeTypePredicate.cs",
            FindLine(source, "return text;"),
            20,
            "value is string",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesInlineAsAssignmentNullResultAndSourceNonNullImpliesNegativeRuntimeTypePredicate()
    {
        const string source = @"
public class TestClass
{
    public string TestMethod(object value)
    {
        string text;
        if ((text = value as string) == null && value != null)
        {
            return string.Empty;
        }

        return text;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "InlineAsAssignmentNullResultAndSourceNonNullImpliesNegativeRuntimeTypePredicate.cs",
            FindLine(source, "return string.Empty;"),
            20,
            "value is not string",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalAccessNullSourceNullableResultHasNoValue()
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
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalAccessHasValueImpliesReceiverNonNull()
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
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableCoalesceFromConditionalAccessWhenReceiverNonNull()
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
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableCoalesceFromConditionalAccessArrayElement()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        int item = values?[0] ?? 42;
        if (values != null)
        {
            return item;
        }

        return 0;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "NullableCoalesceFromConditionalAccessArrayElement.cs",
            FindLine(source, "return item;"),
            16,
            "item == values[0]",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableCoalesceFromConditionalAccessWhenReceiverNull()
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
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesCoalesceNullResultImpliesOperandsNull()
    {
        const string source = @"
public class TestClass
{
    public string TestMethod(string value, string fallback)
    {
        var result = value ?? fallback;
        return result;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "CoalesceNullResultImpliesOperandsNull.cs",
            FindLine(source, "return result;"),
            16,
            "result != null || (value == null && fallback == null)",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalExpressionNullResultImpliesSelectedBranchNull()
    {
        const string source = @"
public class TestClass
{
    public string TestMethod(bool flag, string first, string second)
    {
        var result = flag ? first : second;
        return result;
    }
}";
        var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "ConditionalExpressionNullResultImpliesSelectedBranchNull.cs",
            FindLine(source, "return result;"),
            16,
            "(!flag || result != null || first == null) && (flag || result != null || second == null)",
            smtAnalysis,
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(1));
        Assert.That(smtAnalysis.CacheEntryCount, Is.EqualTo(1));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalAccessMemberNullFacts()
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
        var text = holder?.Text;
        return text;
    }
}";
        var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "ConditionalAccessMemberNullFacts.cs",
            FindLine(source, "return text;"),
            16,
            "text != null || holder == null || holder.Text == null",
            smtAnalysis,
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(1));
        Assert.That(smtAnalysis.CacheEntryCount, Is.EqualTo(1));
    }

    [Test]
    public void SymbolicSourceQueryService_AnalyzeSource_WithSmt_ClassifiesConditionalAccessMemberNullContradiction()
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
        var text = holder?.Text;
        if (holder != null && holder.Text != null && text == null)
        {
            return text;
        }

        return text;
    }
}";
        var result = new SymbolicSourceQueryService().AnalyzeSource(
            source,
            "ConditionalAccessMemberNullContradiction.cs",
            FindLine(source, "return text;"),
            20,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));

        Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
    }

    [Test]
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesCoalesceAssignmentConditionalAccessNullImplication()
    {
        const string source = @"
public sealed class Holder
{
    public string Text;
}

public class TestClass
{
    public string TestMethod(string current, Holder holder)
    {
        var target = current;
        target ??= holder?.Text;
        return target;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "CoalesceAssignmentConditionalAccessNullImplication.cs",
            FindLine(source, "return target;"),
            16,
            "target != null || holder == null || holder.Text == null",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicSourceQueryService_ProveConditionAtSource_ConditionalAccessInvocationResultRemainsUnknown()
    {
        const string source = @"
public sealed class Holder
{
    public string GetText() => null;
}

public class TestClass
{
    public string TestMethod(Holder holder)
    {
        var text = holder?.GetText();
        return text;
    }
}";
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            source,
            "ConditionalAccessInvocationResultRemainsUnknown.cs",
            FindLine(source, "return text;"),
            16,
            "holder == null || text != null",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
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
                                      fact.Contains("==", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void ExecutionVisibility_UlongZeroContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("ulong x", "x == 0UL && x != 0UL"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_BigIntegerZeroContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("BigInteger x", "x == 0 && x != 0", "using System.Numerics;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_BigIntegerAdditionContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("BigInteger x", "x + 1 == 5 && x != 4", "using System.Numerics;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_BigIntegerGuardedDivisionContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "BigInteger value, BigInteger divisor",
                "divisor != 0 && value / divisor == 2 && value / divisor != 2",
                "using System.Numerics;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_DefaultBigIntegerContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("", "default(BigInteger) != 0", "using System.Numerics;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_DecimalZeroContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("decimal value", "value == 0m && value != 0m"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_DecimalPositiveContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("decimal value", "value > 0m && value <= 0m"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_DecimalReversedPositiveContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("decimal value", "0m < value && value <= 0m"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_DecimalFractionalRangeRemainsConservative()
    {
        Assert.That(
            IsConditionAlwaysFalse("decimal value", "value > 0m && value < 1m"),
            Is.False);
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
    public void ExecutionVisibility_ConstantDivisionContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("int value", "value / 2 == 3 && value < 6"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_ConstantRemainderContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("int value", "value % 5 == 3 && value % 5 == 4"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_UncheckedAdditionWraparoundRemainsReachable()
    {
        Assert.That(
            IsConditionAlwaysFalse("int value", "unchecked(value + 1) <= value && value == int.MaxValue"),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_UncheckedSubtractionWraparoundRemainsReachable()
    {
        Assert.That(
            IsConditionAlwaysFalse("int value", "unchecked(value - 1) >= value && value == int.MinValue"),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_UncheckedMultiplicationWraparoundRemainsReachable()
    {
        Assert.That(
            IsConditionAlwaysFalse("int value", "unchecked(value * 2) == 0 && value == 1073741824"),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_GuardedDivisionContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("int value, int divisor",
                "divisor != 0 && value / divisor == 2 && value / divisor != 2"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_GuardedRemainderContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("int value, int divisor",
                "divisor != 0 && value % divisor == 0 && value % divisor != 0"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_NullableGetValueOrDefaultAbsentContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("int? maybe", "!maybe.HasValue && maybe.GetValueOrDefault() != 0"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_NullableGetValueOrDefaultPresentContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("int? maybe", "maybe.HasValue && maybe.GetValueOrDefault() != maybe.Value"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_NullableGetValueOrDefaultFallbackContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("int? maybe", "!maybe.HasValue && maybe.GetValueOrDefault(7) != 7"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_NullableBoolGetValueOrDefaultAbsentContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("bool? maybe", "!maybe.HasValue && maybe.GetValueOrDefault()"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_NullableBoolGetValueOrDefaultPresentContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("bool? maybe",
                "maybe.HasValue && maybe.GetValueOrDefault() && maybe.Value == false"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_NullableBoolGetValueOrDefaultFallbackContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("bool? maybe", "!maybe.HasValue && maybe.GetValueOrDefault(true) == false"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_ReferenceCoalesceAssignmentNonNullFallbackContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("string value, string fallback", "fallback != null && (value ??= fallback) == null"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_NullableCoalesceAssignmentFallbackContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("int? maybe", "!maybe.HasValue && (maybe ??= 7) != 7"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_NullableBoolCoalesceAssignmentFallbackContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("bool? maybe", "!maybe.HasValue && (maybe ??= true) == false"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_NullableGetValueOrDefaultUnknownFallback_RemainsUnknown()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "int? maybe",
                "!maybe.HasValue && maybe.GetValueOrDefault(UnknownFallback.Next()) != 7",
                @"
public static class UnknownFallback
{
    public static int Next() => 7;
}"),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_NotNullIfNotNullMethodReturnContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string value",
                "value != null && NotNullIfNotNullPredicates.Echo(value: value) == null",
                NotNullIfNotNullSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_NotNullIfNotNullIndexerReturnContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "NotNullIfNotNullIndexer box, string key",
                "box != null && key != null && box[key] == null",
                NotNullIfNotNullSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_NotNullIfNotNullNullSourceReturn_RemainsUnknown()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string value",
                "value == null && NotNullIfNotNullPredicates.Echo(value) != null",
                NotNullIfNotNullSource),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_UnguardedVariableDivision_RemainsUnknown()
    {
        Assert.That(
            IsConditionAlwaysFalse("int value, int divisor", "value / divisor == 2 && value / divisor != 2"),
            Is.False);
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
    public void ExecutionVisibility_CheckedNarrowingIntegralCastContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("int value", "checked((byte)value) == 5 && value != 5"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_CheckedNarrowingIntegralCastOutOfRangeComparison_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("int value", "checked((byte)value) > 255"),
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
    public void ExecutionVisibility_ExtendedPropertyPatternContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "ExtendedPatternBox box",
                "box is { Child.Value: > 0 } && box.Child.Value <= 0",
                ExtendedPropertyPatternSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_ValueTuplePositionalPatternContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "ValueTuple<int, int> pair",
                "pair is (_, < 10) && pair.Item2 >= 10",
                "using System;"),
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
            IsConditionAlwaysFalse("int[] values, int index",
                "(uint)index < (uint)values.Length && index >= values.Length"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_UnsignedCastBoundsCheckFalseBranchImpliesOutOfRange()
    {
        Assert.That(
            IsConditionAlwaysFalse("int[] values, int index",
                "!((uint)index < (uint)values.Length) && index >= 0 && index < values.Length"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_UnsignedCastUpperBoundGuardImpliesOutOfRange()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text, int index",
                "(uint)index >= (uint)text.Length && index >= 0 && index < text.Length"),
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
    public void ExecutionVisibility_ExplicitCaptureOptionRegexImpliesStringLength()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.IsMatch(text, @""\A(?n:[A-Z][0-9])\z"") && text.Length != 2",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StaticExplicitCaptureRegexOptionContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.IsMatch(text, @""\A(A)B\z"", RegexOptions.ExplicitCapture) && text != ""AB""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StaticCompiledRegexOptionContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.IsMatch(text, @""\AAB\z"", RegexOptions.Compiled) && text != ""AB""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StaticCultureInvariantRegexOptionContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.IsMatch(text, @""\AAB\z"", RegexOptions.CultureInvariant) && text != ""AB""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StaticSinglelineRegexOptionAllowsNewlineDot()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"!Regex.IsMatch(text, @""\A.\z"", RegexOptions.Singleline) && text == ""\n""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StaticIgnorePatternWhitespaceRegexOptionContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.IsMatch(text, @""\A A\ B \z"", RegexOptions.IgnorePatternWhitespace) && text != ""A B""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StaticCombinedSupportedRegexOptionsContradictsNegatedNewlineMatch()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"!Regex.IsMatch(text, @""\A . \z"", RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture) && text == ""\n""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StaticCompiledCombinedWithSinglelineRegexOptionAllowsNewlineDot()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"!Regex.IsMatch(text, @""\A.\z"", RegexOptions.Compiled | RegexOptions.Singleline) && text == ""\n""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StaticCultureInvariantCombinedWithSinglelineRegexOptionAllowsNewlineDot()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"!Regex.IsMatch(text, @""\A.\z"", RegexOptions.CultureInvariant | RegexOptions.Singleline) && text == ""\n""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_ScopedSinglelineDisableRegexDotRejectsNewline()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.IsMatch(text, @""\A(?s:A(?-s:.)C)\z"") && text == ""A\nC""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_NamedCaptureRegexContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.IsMatch(text, @""\A(?<prefix>AB)C\z"") && text != ""ABC""",
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
    public void ExecutionVisibility_RegexMatchSuccessImpliesStringLength()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.Match(text, @""\A[A-Z][0-9]\z"").Success && text.Length != 2",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceRegexMatchSuccessImpliesStringLength()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"new Regex(@""\A[A-Z][0-9]\z"").Match(text).Success && text.Length != 2",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_RegexMatchesCountPositiveImpliesStringLength()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.Matches(text, @""\A[A-Z][0-9]\z"").Count > 0 && text.Length != 2",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_RegexMatchesCountZeroImpliesNonMatch()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.Matches(text, @""\AAB\z"").Count == 0 && text == ""AB""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_ReversedRegexMatchesCountPositiveImpliesStringLength()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"1 <= Regex.Matches(text, @""\A[A-Z][0-9]\z"").Count && text.Length != 2",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceRegexMatchesCountPositiveImpliesStringLength()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"new Regex(@""\A[A-Z][0-9]\z"").Matches(text).Count != 0 && text.Length != 2",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_LocalRegexMatchesCountPositiveImpliesStringLength()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System.Text.RegularExpressions;

public class TestClass
{
    public int TestMethod(string text)
    {
        var regex = new Regex(@""\A[A-Z][0-9]\z"");
        if (regex.Matches(text).Count >= 1 && text.Length != 2)
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
    public void ExecutionVisibility_RegexMatchesCountThresholdAboveOneRemainsConservative()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.Matches(text, ""A"").Count > 1 && text == ""A""",
                "using System.Text.RegularExpressions;"),
            Is.False);
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

    public void ExecutionVisibility_GeneratedRegexFactoryLiteralContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"RegexFactories.Ab().IsMatch(text) && text != ""AB""",
                GeneratedRegexFactorySource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_GeneratedRegexFactoryMatchSuccessImpliesStringLength()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"RegexFactories.Ab().Match(text).Success && text.Length != 2",
                GeneratedRegexFactorySource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_GeneratedRegexFactoryMatchesCountImpliesStringLength()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"RegexFactories.Ab().Matches(text).Count > 0 && text.Length != 2",
                GeneratedRegexFactorySource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_GeneratedRegexFactorySinglelineOptionAllowsNewlineDot()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"!RegexFactories.SinglelineAny().IsMatch(text) && text == ""\n""",
                GeneratedRegexFactorySource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_LocalGeneratedRegexFactoryLiteralContradictsStringEquality()
    {
        Assert.That(
            IsStatementUnreachable(GeneratedRegexFactorySource + @"

public class TestClass
{
    public int TestMethod(string text)
    {
        var regex = RegexFactories.Ab();
        if (regex.IsMatch(text) && text != ""AB"")
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
    public void ExecutionVisibility_StaticReadonlyRegexLiteralContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"RegexCache.Ab.IsMatch(text) && text != ""AB""",
                StaticRegexCacheSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StaticReadonlyRegexMatchSuccessImpliesStringLength()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "RegexCache.Ab.Match(text).Success && text.Length != 2",
                StaticRegexCacheSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StaticReadonlyRegexMatchesCountImpliesStringLength()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "RegexCache.Ab.Matches(text).Count > 0 && text.Length != 2",
                StaticRegexCacheSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_MutableStaticRegexFieldRemainsConservative()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"RegexCache.MutableAb.IsMatch(text) && text != ""AB""",
                StaticRegexCacheSource),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_InstanceReadonlyRegexLiteralContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "RegexBox box, string text",
                @"box.Ab.IsMatch(text) && text != ""AB""",
                InstanceRegexCacheSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceReadonlyRegexMatchSuccessImpliesStringLength()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "RegexBox box, string text",
                "box.Ab.Match(text).Success && text.Length != 2",
                InstanceRegexCacheSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceReadonlyRegexMatchesCountImpliesStringLength()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "RegexBox box, string text",
                "box.Ab.Matches(text).Count > 0 && text.Length != 2",
                InstanceRegexCacheSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceReadonlyRegexSinglelineOptionAllowsNewlineDot()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "RegexBox box, string text",
                @"!box.SinglelineAny.IsMatch(text) && text == ""\n""",
                InstanceRegexCacheSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceReadonlyRegexMultilineOptionStartAtZeroContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "RegexBox box, string text",
                @"box.MultilineAb.IsMatch(text, 0) && text != ""AB""",
                InstanceRegexCacheSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceReadonlyGeneratedRegexFieldContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "GeneratedRegexBox box, string text",
                @"box.Ab.IsMatch(text) && text != ""AB""",
                GeneratedRegexFactorySource + InstanceRegexCacheSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_MutableInstanceRegexFieldRemainsConservative()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "RegexBox box, string text",
                @"box.MutableAb.IsMatch(text) && text != ""AB""",
                InstanceRegexCacheSource),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_ConstructorAssignedReadonlyRegexFieldRemainsConservative()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "ConstructorAssignedRegexBox box, string text",
                @"box.Ab.IsMatch(text) && text != ""AB""",
                InstanceRegexCacheSource),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_StaticReadonlyRegexAssignedInStaticConstructorRemainsConservative()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"StaticCtorRegexCache.Ab.IsMatch(text) && text != ""AB""",
                InstanceRegexCacheSource),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_InstanceRegexStartAtZeroContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"new Regex(@""\AAB\z"").IsMatch(text, 0) && text != ""AB""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceRegexNonZeroStartAtRemainsConservative()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"!new Regex(""AB"").IsMatch(text, 1) && text == ""AB""",
                "using System.Text.RegularExpressions;"),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_LocalInstanceRegexLiteralContradictsStringEquality()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System.Text.RegularExpressions;

public class TestClass
{
    public int TestMethod(string text)
    {
        var regex = new Regex(@""\AAB\z"");
        if (regex.IsMatch(text) && text != ""AB"")
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
    public void ExecutionVisibility_InstanceCompiledRegexOptionContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"new Regex(@""\AAB\z"", RegexOptions.Compiled).IsMatch(text) && text != ""AB""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceCultureInvariantRegexOptionContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"new Regex(@""\AAB\z"", RegexOptions.CultureInvariant).IsMatch(text) && text != ""AB""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceSinglelineRegexOptionAllowsNewlineDot()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"!new Regex(@""\A.\z"", RegexOptions.Singleline).IsMatch(text) && text == ""\n""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceMultilineRegexOptionStartAtZeroContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"new Regex(@""\AAB\z"", RegexOptions.Multiline).IsMatch(text, 0) && text != ""AB""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StaticMultilineRegexOptionContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.IsMatch(text, @""\AAB\z"", RegexOptions.Multiline) && text != ""AB""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_LocalInstanceSinglelineRegexOptionAllowsNewlineDot()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System.Text.RegularExpressions;

public class TestClass
{
    public int TestMethod(string text)
    {
        var regex = new Regex(@""\A.\z"", RegexOptions.Singleline);
        if (!regex.IsMatch(text) && text == ""\n"")
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
    public void ExecutionVisibility_ReassignedLocalRegexRemainsConservative()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System.Text.RegularExpressions;

public class TestClass
{
    public int TestMethod(string text)
    {
        var regex = new Regex(@""\ACD\z"");
        regex = new Regex(@""\AAB\z"");
        if (regex.IsMatch(text) && text != ""AB"")
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
    public void ExecutionVisibility_InstanceIgnorePatternWhitespaceRegexOptionContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"new Regex(@""\A A\ B \z"", RegexOptions.IgnorePatternWhitespace).IsMatch(text) && text != ""A B""",
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
    public void ExecutionVisibility_NegatedRegexIsMatchStillImpliesInputNonNull()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "!Regex.IsMatch(text, \"A\") && text == null",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_ZeroRegexMatchesStillImpliesInputNonNull()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "Regex.Matches(text, \"A\").Count == 0 && text == null",
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
    public void ExecutionVisibility_CategoryRegexImpliesStringLength()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.IsMatch(text, @""\A\p{Lu}\P{Ll}\z"") && text.Length != 2",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_WordBoundaryRegexLengthImplicationRemainsConservative()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.IsMatch(text, @""\A\bAB\B?\z"") && text.Length != 2",
                "using System.Text.RegularExpressions;"),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_NegatedCategoryRegexClassConcreteMismatchIsUnreachable()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.IsMatch(text, @""\A[^\p{Lu}]\z"") && text == ""A""",
                "using System.Text.RegularExpressions;"),
            Is.True);
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
    public void ExecutionVisibility_UnsupportedRegexOptionsConcreteMismatchUsesSelfVerification()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "Regex.IsMatch(text, \"^ab$\", RegexOptions.IgnoreCase) && text == \"CD\"",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_CultureInvariantWithUnsupportedRegexOptionsRemainConservative()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "Regex.IsMatch(text, \"^ab$\", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase) && text == \"AB\"",
                "using System.Text.RegularExpressions;"),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_InstanceUnsupportedRegexOptionsRemainConservative()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "new Regex(\"^ab$\", RegexOptions.IgnoreCase).IsMatch(text) && text == \"AB\"",
                "using System.Text.RegularExpressions;"),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_InstanceUnsupportedRegexOptionsConcreteMismatchUsesSelfVerification()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "new Regex(\"^ab$\", RegexOptions.IgnoreCase).IsMatch(text) && text == \"CD\"",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceCultureInvariantWithUnsupportedRegexOptionsRemainConservative()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "new Regex(\"^ab$\", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).IsMatch(text) && text == \"AB\"",
                "using System.Text.RegularExpressions;"),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_UnsupportedInlineIgnoreCaseRegexConcreteMismatchUsesSelfVerification()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                @"Regex.IsMatch(text, @""\A(?i:ab)\z"") && text == ""CD""",
                "using System.Text.RegularExpressions;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringContainsContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text", "text.Contains(\"Z\") && text == \"ABC\""),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringContainsCharContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text", "text.Contains('Z') && text == \"ABC\""),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringContainsOrdinalIgnoreCaseContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "text.Contains(\"a\", StringComparison.OrdinalIgnoreCase) && text == \"BBB\"",
                "using System;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringStartsWithOrdinalIgnoreCaseContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "text.StartsWith(\"ab\", StringComparison.OrdinalIgnoreCase) && text == \"zzAB\"",
                "using System;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringEndsWithOrdinalIgnoreCaseContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "text.EndsWith(\"xy\", StringComparison.OrdinalIgnoreCase) && text == \"XYzz\"",
                "using System;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringIndexOfCharFoundContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text", "text.IndexOf('Z') >= 0 && text == \"ABC\""),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringIndexOfCharNotFoundContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text", "text.IndexOf('A') == -1 && text == \"ABC\""),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringIndexOfCharReversedFoundComparisonContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text", "0 <= text.IndexOf('Z') && text == \"ABC\""),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringIndexOfOrdinalFoundContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "text.IndexOf(\"ZZ\", StringComparison.Ordinal) >= 0 && text == \"ABC\"",
                "using System;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringIndexOfOrdinalNotFoundContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "text.IndexOf(\"AB\", StringComparison.Ordinal) < 0 && text == \"ABC\"",
                "using System;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringIndexOfDefaultStringSearchRemainsConservative()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text", "text.IndexOf(\"a\") >= 0 && text == \"A\""),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_StringIndexOfOrdinalIgnoreCaseContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "text.IndexOf(\"a\", StringComparison.OrdinalIgnoreCase) < 0 && text == \"A\"",
                "using System;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringLastIndexOfCharFoundContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text", "text.LastIndexOf('Z') >= 0 && text == \"ABC\""),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringLastIndexOfOrdinalNotFoundContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "text.LastIndexOf(\"AB\", StringComparison.Ordinal) < 0 && text == \"ABC\"",
                "using System;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringLastIndexOfDefaultStringSearchRemainsConservative()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text", "text.LastIndexOf(\"a\") >= 0 && text == \"A\""),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_StringLastIndexOfOrdinalIgnoreCaseContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "text.LastIndexOf(\"a\", StringComparison.OrdinalIgnoreCase) < 0 && text == \"A\"",
                "using System;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringStartsWithCharContradictsEmptyString()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text", "text.StartsWith('A') && text == string.Empty"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceStringEqualsOrdinalContradictsInequality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "text.Equals(\"A\", StringComparison.Ordinal) && text != \"A\"",
                "using System;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StaticStringEqualsOrdinalContradictsInequality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "string.Equals(text, \"A\", StringComparison.Ordinal) && text != \"A\"",
                "using System;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceStringEqualsOrdinalIgnoreCaseContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "text.Equals(\"a\", StringComparison.OrdinalIgnoreCase) && text == \"B\"",
                "using System;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StaticStringEqualsOrdinalIgnoreCaseContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "string.Equals(\"a\", text, StringComparison.OrdinalIgnoreCase) && text == \"B\"",
                "using System;"),
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
    public void ExecutionVisibility_StringConcatLengthContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string left, string right",
                "left != null && right != null && (left + right).Length != left.Length + right.Length"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringPredicateOnConcatContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string suffix",
                "!(\"PRE\" + suffix).StartsWith(\"PRE\", StringComparison.Ordinal)",
                "using System;"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringSubstringLengthContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text, int start",
                "text != null && start >= 0 && start <= text.Length && text.Substring(start).Length != text.Length - start"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringPrefixSubstringEqualityContradictsStringEquality()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "text.Substring(0, 3) == \"PRE\" && text == \"ALT\""),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringIsNullOrWhiteSpaceContradictsNonWhitespaceLiteral()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "string.IsNullOrWhiteSpace(text) && text == \"A\""),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringIsNullOrWhiteSpaceFalseBranchImpliesNonEmpty()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "!string.IsNullOrWhiteSpace(text) && text.Length == 0"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringIsNullOrWhiteSpaceFalseBranchRejectsWhitespaceLiteral()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "!string.IsNullOrWhiteSpace(text) && text == \" \\t\\r\\n\""),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_StringIsNullOrWhiteSpaceAllowsNonEmptyWhitespace()
    {
        Assert.That(
            IsConditionAlwaysFalse(
                "string text",
                "string.IsNullOrWhiteSpace(text) && text != null && text.Length > 0"),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_CustomLengthNegative_RemainsUnknown()
    {
        Assert.That(
            IsConditionAlwaysFalse("HasLength value", "value.Length < 0",
                "public sealed class HasLength { public int Length => -1; }"),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_SourceNullOrEmptyPredicateTrueBranchLengthContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text",
                "SourcePredicates.IsNullOrEmptyLike(text) && text != null && text.Length > 0", SourcePredicateSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_SourceNullOrEmptyPredicateFalseBranchLengthContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text", "!SourcePredicates.IsNullOrEmptyLike(text) && text.Length <= 0",
                SourcePredicateSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_SourceRangePredicateContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("int value", "SourcePredicates.InRange(value) && (value < 10 || value > 20)",
                SourcePredicateSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_SourceSwitchStatementPredicateContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("int value", "SourcePredicates.IsZeroWithSwitch(value) && value != 0",
                SourcePredicateSource),
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
            IsConditionAlwaysFalse("int value", "SourcePredicates.IsPositive(value + 1) && value < -1",
                SourcePredicateSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_SourcePositivePredicateReachable_RemainsUnknown()
    {
        Assert.That(
            IsConditionAlwaysFalse("int value", "SourcePredicates.IsPositive(value) && value > 10",
                SourcePredicateSource),
            Is.False);
    }

    [Test]
    public void ExecutionVisibility_LocalDelegatePredicateDirectCallContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System;

public class TestClass
{
    public int TestMethod(int value)
    {
        Func<int, bool> predicate = x => x > 0;
        if (predicate(value) && value <= 0)
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
    public void ExecutionVisibility_LocalDelegatePredicateInvokeCallContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System;

public class TestClass
{
    public int TestMethod(string text)
    {
        Predicate<string> predicate = value => value != null;
        if (predicate.Invoke(text) && text == null)
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
    public void ExecutionVisibility_LocalDelegatePredicateBlockBodyContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System;

public class TestClass
{
    public int TestMethod(int value)
    {
        Func<int, bool> predicate = x => { return x == 42; };
        if (predicate(value) && value != 42)
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
    public void ExecutionVisibility_LocalDelegateMultiParameterPredicateContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System;

public class TestClass
{
    public int TestMethod(int value, int upperBound)
    {
        Func<int, int, bool> predicate = (candidate, limit) => candidate >= 0 && candidate < limit;
        if (predicate(value, upperBound) && (value < 0 || value >= upperBound))
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
    public void ExecutionVisibility_LocalDelegateTypedLambdaPredicateContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System;

public class TestClass
{
    public int TestMethod(int value)
    {
        Func<int, bool> predicate = (int x) => x >= 10;
        if (predicate(value) && value < 10)
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
    public void ExecutionVisibility_LocalDelegateAnonymousMethodPredicateContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System;

public class TestClass
{
    public int TestMethod(string text)
    {
        Predicate<string> predicate = delegate (string value) { return value != null && value.Length > 0; };
        if (predicate(text) && (text == null || text.Length <= 0))
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
    public void ExecutionVisibility_LocalDelegateStaticMethodGroupPredicateContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsStatementUnreachable("using System;" + SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(int value)
    {
        Func<int, bool> predicate = SourcePredicates.IsPositive;
        if (predicate(value) && value <= 0)
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
    public void ExecutionVisibility_LocalDelegateStaticMethodGroupMultiParameterPredicateContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsStatementUnreachable("using System;" + SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        Func<int[], int, bool> predicate = SourcePredicates.IsValidIndex;
        if (predicate(values, index) && (values == null || index < 0 || index >= values.Length))
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
    public void ExecutionVisibility_LocalDelegateStaticMethodGroupStringPredicateContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsStatementUnreachable("using System;" + SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(string text)
    {
        Predicate<string> predicate = SourcePredicates.HasText;
        if (predicate.Invoke(text) && (text == null || text.Length <= 0))
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
    public void ExecutionVisibility_LocalDelegateStaticLocalFunctionMethodGroupContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System;

public class TestClass
{
    public int TestMethod(int value)
    {
        static bool IsPositive(int x) => x > 0;
        Func<int, bool> predicate = IsPositive;
        if (predicate(value) && value <= 0)
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
    public void ExecutionVisibility_LocalDelegateCapturedLocalFunctionMethodGroupRemainsConservative()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System;

public class TestClass
{
    public int TestMethod(int value, int minimum)
    {
        bool IsPositive(int x) => x > minimum;
        Func<int, bool> predicate = IsPositive;
        if (predicate(value) && value <= minimum)
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
    public void ExecutionVisibility_LocalDelegateInstanceMethodGroupRemainsConservative()
    {
        Assert.That(
            IsStatementUnreachable("using System;" + SourcePredicateSource + @"
public class TestClass
{
    public int TestMethod(SourcePredicateBox box)
    {
        Func<bool> predicate = box.HasTextMethod;
        if (predicate() && (box.Value == null || box.Value.Length <= 0))
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
    public void ExecutionVisibility_LocalDelegateRepeatedUseRemainsConservative()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System;

public class TestClass
{
    public int TestMethod(int value)
    {
        Func<int, bool> predicate = x => x > 0;
        if (predicate(value) && predicate(value + 1) && value <= 0)
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
    public void ExecutionVisibility_LocalDelegatePredicateCaptureRemainsConservative()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System;

public class TestClass
{
    public int TestMethod(int value, int minimum)
    {
        Func<int, bool> predicate = x => x > minimum;
        if (predicate(value) && value <= minimum)
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
    public void ExecutionVisibility_ReassignedLocalDelegatePredicateRemainsConservative()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System;

public class TestClass
{
    public int TestMethod(int value)
    {
        Func<int, bool> predicate = x => x > 0;
        predicate = x => x < 0;
        if (predicate(value) && value >= 0)
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
    public void ExecutionVisibility_EscapedLocalDelegatePredicateRemainsConservative()
    {
        Assert.That(
            IsStatementUnreachable(@"
using System;

public class TestClass
{
    public int TestMethod(int value)
    {
        Func<int, bool> predicate = x => x > 0;
        Consume(predicate);
        if (predicate(value) && value <= 0)
        {
            return 1;
        }

        return 0;
    }

    private static void Consume(Func<int, bool> predicate)
    {
    }
}",
                "return 1;"),
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
            IsConditionAlwaysFalse("string text", "SourcePredicates.HasText(text) && text == null",
                SourcePredicateSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_SourceHasTextPredicateLengthContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text", "SourcePredicates.HasText(text) && text.Length <= 0",
                SourcePredicateSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_SourceHasTextGuardPredicateContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text",
                "SourcePredicates.HasTextWithGuard(text) && (text == null || text.Length <= 0)", SourcePredicateSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_SourceHasTextIfElsePredicateContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text",
                "SourcePredicates.HasTextWithIfElse(text) && (text == null || text.Length <= 0)",
                SourcePredicateSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_SourceHasTextLocalAliasPredicateContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text",
                "SourcePredicates.HasTextViaLocal(text) && (text == null || text.Length <= 0)", SourcePredicateSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_SourceHasTextLocalAssignmentPredicateContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text",
                "SourcePredicates.HasTextViaAssignment(text) && (text == null || text.Length <= 0)",
                SourcePredicateSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_SourceLocalAssignmentIntegerPredicateContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("int value", "SourcePredicates.IsPositiveAfterLocalAssignment(value) && value < -1",
                SourcePredicateSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_SourceBooleanPropertyContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("SourcePredicateBox box",
                "box.HasText && (box.Value == null || box.Value.Length <= 0)", SourcePredicateSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_InstanceSourceBooleanMethodContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("SourcePredicateBox box",
                "box.HasTextMethod() && (box.Value == null || box.Value.Length <= 0)", SourcePredicateSource),
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
    public void ExecutionVisibility_CollectionCountNegativeContradiction_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("System.Collections.Generic.IReadOnlyCollection<int> values", "values.Count < 0"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_SourceNullOrEmptyPredicateNestedInNegation_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text", "!(SourcePredicates.IsNullOrEmptyLike(text)) && text.Length <= 0",
                SourcePredicateSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_SourceHasTextPredicateInOrFalseBranch_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text",
                "!(SourcePredicates.HasText(text) || false) && text.Length > 0 && text != null", SourcePredicateSource),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_SourceNullOrEmptyPredicateReachable_RemainsUnknown()
    {
        Assert.That(
            IsConditionAlwaysFalse("string text",
                "SourcePredicates.IsNullOrEmptyLike(text) && text != null && text.Length == 0", SourcePredicateSource),
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
    public void ExecutionVisibility_AsExpressionNonNullImpliesSourceNonNull_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("object value", "(value as string) != null && value == null"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_AsExpressionNonNullImpliesRuntimeType_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("object value", "(value as string) != null && value is not string"),
            Is.True);
    }

    [Test]
    public void ExecutionVisibility_AsExpressionNullContradictsRuntimeType_IsAlwaysFalse()
    {
        Assert.That(
            IsConditionAlwaysFalse("object value", "(value as string) == null && value is string"),
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
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_smt_mode", "deep"));

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
                .Add("sharpproof_smt_mode", "deep")
                .Add("sharpproof_smt_timeout_ms", "321")
                .Add("sharpproof_smt_method_budget_ms", "4321")
                .Add("sharpproof_smt_max_path_conditions", "123")
                .Add("sharpproof_smt_max_expression_nodes", "4567"));

        Assert.That(options.Mode, Is.EqualTo("Deep"));
        Assert.That(options.TimeoutMs, Is.EqualTo(321));
        Assert.That(options.MethodBudgetMs, Is.EqualTo(4321));
        Assert.That(options.MaxPathConditions, Is.EqualTo(123));
        Assert.That(options.MaxExpressionNodes, Is.EqualTo(4567));
    }

    [Test]
    public void SmtConfiguration_DisabledMode_DisablesService()
    {
        var options = ReadSmtOptions(
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_smt_mode", "disabled"));

        Assert.That(options.Mode, Is.EqualTo("Off"));
        Assert.That(options.IsEnabled, Is.False);
    }

    [TestCase("0")]
    [TestCase("no")]
    public void SmtConfiguration_DisableAliases_AreRejected(string mode)
    {
        var options = ReadSmtOptions(
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_smt_mode", mode));

        Assert.That(options.Mode, Is.EqualTo("Bounded"));
        Assert.That(options.IsEnabled, Is.True);
    }

    private static readonly Type ExecutionVisibilityType = typeof(SharpProofAnalyzer).Assembly
        .GetType("SharpProof.Analyzer.Engine.ExecutionVisibility", true)!;

    private static readonly MethodInfo IsConditionAlwaysFalseMethod = ExecutionVisibilityType
        .GetMethod("IsConditionAlwaysFalse", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo IsConditionAlwaysTrueMethod = ExecutionVisibilityType
        .GetMethod("IsConditionAlwaysTrue", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo IsInStaticallyUnreachableBranchMethod = ExecutionVisibilityType
        .GetMethod("IsInStaticallyUnreachableBranch",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

    private static bool IsConditionAlwaysFalse(string parameterList, string conditionExpression,
        string extraSource = "")
    {
        var context = AnalyzerTestHost.CreateConditionContext(parameterList, conditionExpression, extraSource);
        return (bool)IsConditionAlwaysFalseMethod.Invoke(null,
            new object?[] { context.Expression, context.SemanticModel, CancellationToken.None })!;
    }

    private static bool IsConditionAlwaysTrue(string parameterList, string conditionExpression, string extraSource = "")
    {
        var context = AnalyzerTestHost.CreateConditionContext(parameterList, conditionExpression, extraSource);
        return (bool)IsConditionAlwaysTrueMethod.Invoke(null,
            new object?[] { context.Expression, context.SemanticModel, CancellationToken.None })!;
    }

    private static bool IsStatementUnreachable(string source, string statementText)
    {
        var context = AnalyzerTestHost.CreateSourceContext(
            source,
            "StatementReachabilityHost",
            AnalyzerTestHost.GetMinimalFrameworkReferences());
        var statement = context.Root
            .DescendantNodes()
            .OfType<StatementSyntax>()
            .Single(node => string.Equals(node.ToString(), statementText, StringComparison.Ordinal));

        return (bool)IsInStaticallyUnreachableBranchMethod.Invoke(null,
            new object?[] { statement, context.SemanticModel, CancellationToken.None })!;
    }

    private static bool IsExpressionUnreachable(string source, string expressionText)
    {
        var context = AnalyzerTestHost.CreateSourceContext(
            source,
            "ExpressionReachabilityHost",
            AnalyzerTestHost.GetMinimalFrameworkReferences());
        var expression = context.Root
            .DescendantNodes()
            .OfType<ExpressionSyntax>()
            .Where(node => string.Equals(node.ToString(), expressionText, StringComparison.Ordinal))
            .OrderBy(static node => node.Span.Length)
            .First();

        return (bool)IsInStaticallyUnreachableBranchMethod.Invoke(null,
            new object?[] { expression, context.SemanticModel, CancellationToken.None })!;
    }

    private static string[] CollectProgramPointFacts(string source, string statementPrefix)
    {
        var context = AnalyzerTestHost.CreateSourceContext(
            source,
            "ProgramPointFactHost",
            AnalyzerTestHost.GetMinimalFrameworkReferences());
        var statement = context.Root
            .DescendantNodes()
            .OfType<StatementSyntax>()
            .Single(node => node.ToString().StartsWith(statementPrefix, StringComparison.Ordinal));
        var snapshot =
            new SymbolicInvariantService().GetInvariantsAt(statement, context.SemanticModel, CancellationToken.None);

        return snapshot.Facts.ToArray();
    }

    private static string[] CollectExpressionProgramPointFacts(string source, string expressionPrefix)
    {
        var context = AnalyzerTestHost.CreateSourceContext(
            source,
            "SymbolicFactsTest",
            ImmutableArray.Create<MetadataReference>(
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)),
            parseOptions: null);
        var expression = context.Root
            .DescendantNodes()
            .OfType<ExpressionSyntax>()
            .Single(node => node.ToString().StartsWith(expressionPrefix, StringComparison.Ordinal));
        var snapshot =
            new SymbolicInvariantService().GetInvariantsAt(expression, context.SemanticModel, CancellationToken.None);

        return snapshot.Facts.ToArray();
    }

    internal static int FindLine(string source, string text)
    {
        var lines = source.Split('\n');
        for (var index = 0; index < lines.Length; index++)
            if (lines[index].Contains(text, StringComparison.Ordinal))
                return index + 1;

        throw new InvalidOperationException("Text was not found in source.");
    }

    private static SmtOptionsSnapshot ReadSmtOptions(ImmutableDictionary<string, string> globalOptions)
    {
        var analyzerOptions = AnalyzerTestHost.CreateAnalyzerOptions(globalOptions);
        var configurationType = typeof(SharpProofAnalyzer).Assembly
            .GetType("SharpProof.Analyzer.Configuration.AnalyzerConfiguration", true)!;
        var fromOptions = configurationType.GetMethod(
            "FromOptions",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
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
