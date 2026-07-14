using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.ProofCore.Purity;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using SharpProof.Test.Smt;
using CanonicalSymbolicLowering = SharpProof.Test.TypedSymbolicTestLowering;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
[Category("SmtHeavy")]
public class SemanticOracleSmtTests : SemanticOracleSmtTestBase
{
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

    public sealed record ConditionAlwaysFalseCase(
        string Name,
        string ParameterList,
        string ConditionExpression,
        bool Expected,
        string ExtraSource = "");

    private static readonly ConditionAlwaysFalseCase[] SingleCallConditionAlwaysFalseCaseDataPart1 =
    {
        new("ExecutionVisibility_AffineContradiction_IsAlwaysFalse", "int x", "x + 1 <= 0 && x >= 0", false),
        new("ExecutionVisibility_UlongZeroContradiction_IsAlwaysFalse", "ulong x", "x == 0UL && x != 0UL", true),
        new("ExecutionVisibility_BigIntegerZeroContradiction_IsAlwaysFalse", "BigInteger x", "x == 0 && x != 0", true, "using System.Numerics;"),
        new("ExecutionVisibility_BigIntegerAdditionContradiction_IsAlwaysFalse", "BigInteger x", "x + 1 == 5 && x != 4", true, "using System.Numerics;"),
        new("ExecutionVisibility_BigIntegerGuardedDivisionContradiction_IsAlwaysFalse", "BigInteger value, BigInteger divisor", "divisor != 0 && value / divisor == 2 && value / divisor != 2", true, "using System.Numerics;"),
        new("ExecutionVisibility_DefaultBigIntegerContradiction_IsAlwaysFalse", "", "default(BigInteger) != 0", true, "using System.Numerics;"),
        new("ExecutionVisibility_DecimalZeroContradiction_IsAlwaysFalse", "decimal value", "value == 0m && value != 0m", true),
        new("ExecutionVisibility_DecimalPositiveContradiction_IsAlwaysFalse", "decimal value", "value > 0m && value <= 0m", true),
        new("ExecutionVisibility_DecimalReversedPositiveContradiction_IsAlwaysFalse", "decimal value", "0m < value && value <= 0m", true),
        new("ExecutionVisibility_DecimalFractionalRangeRemainsConservative", "decimal value", "value > 0m && value < 1m", false),
        new("ExecutionVisibility_ConditionalExpressionContradiction_IsAlwaysFalse", "bool flag, int x, int y", "(flag ? x : y) == 5 && flag && x != 5", true),
        new("ExecutionVisibility_WideningIntegralCastContradiction_IsAlwaysFalse", "int value", "(long)value > 0L && value <= 0", true),
        new("ExecutionVisibility_ConstantDivisionContradiction_IsAlwaysFalse", "int value", "value / 2 == 3 && value < 6", true),
        new("ExecutionVisibility_ConstantRemainderContradiction_IsAlwaysFalse", "int value", "value % 5 == 3 && value % 5 == 4", true),
        new("ExecutionVisibility_UncheckedAdditionWraparoundRemainsReachable", "int value", "unchecked(value + 1) <= value && value == int.MaxValue", false),
        new("ExecutionVisibility_UncheckedSubtractionWraparoundRemainsReachable", "int value", "unchecked(value - 1) >= value && value == int.MinValue", false),
        new("ExecutionVisibility_UncheckedMultiplicationWraparoundRemainsReachable", "int value", "unchecked(value * 2) == 0 && value == 1073741824", false),
        new("ExecutionVisibility_GuardedDivisionContradiction_IsAlwaysFalse", "int value, int divisor", "divisor != 0 && value / divisor == 2 && value / divisor != 2", true),
        new("ExecutionVisibility_GuardedRemainderContradiction_IsAlwaysFalse", "int value, int divisor", "divisor != 0 && value % divisor == 0 && value % divisor != 0", true),
        new("ExecutionVisibility_NullableGetValueOrDefaultAbsentContradiction_IsAlwaysFalse", "int? maybe", "!maybe.HasValue && maybe.GetValueOrDefault() != 0", true),
        new("ExecutionVisibility_NullableGetValueOrDefaultPresentContradiction_IsAlwaysFalse", "int? maybe", "maybe.HasValue && maybe.GetValueOrDefault() != maybe.Value", true),
        new("ExecutionVisibility_NullableGetValueOrDefaultFallbackContradiction_IsAlwaysFalse", "int? maybe", "!maybe.HasValue && maybe.GetValueOrDefault(7) != 7", true),
        new("ExecutionVisibility_NullableBoolGetValueOrDefaultAbsentContradiction_IsAlwaysFalse", "bool? maybe", "!maybe.HasValue && maybe.GetValueOrDefault()", true),
        new("ExecutionVisibility_NullableBoolGetValueOrDefaultPresentContradiction_IsAlwaysFalse", "bool? maybe", "maybe.HasValue && maybe.GetValueOrDefault() && maybe.Value == false", true),
        new("ExecutionVisibility_NullableBoolGetValueOrDefaultFallbackContradiction_IsAlwaysFalse", "bool? maybe", "!maybe.HasValue && maybe.GetValueOrDefault(true) == false", true),
        new("ExecutionVisibility_ReferenceCoalesceAssignmentNonNullFallbackContradiction_IsAlwaysFalse", "string value, string fallback", "fallback != null && (value ??= fallback) == null", true),
        new("ExecutionVisibility_NullableCoalesceAssignmentFallbackContradiction_IsAlwaysFalse", "int? maybe", "!maybe.HasValue && (maybe ??= 7) != 7", true),
        new("ExecutionVisibility_NullableBoolCoalesceAssignmentFallbackContradiction_IsAlwaysFalse", "bool? maybe", "!maybe.HasValue && (maybe ??= true) == false", true),
        new("ExecutionVisibility_NullableGetValueOrDefaultUnknownFallback_RemainsUnknown", "int? maybe", "!maybe.HasValue && maybe.GetValueOrDefault(UnknownFallback.Next()) != 7", false, @"
public static class UnknownFallback
{
    public static int Next() => 7;
}"),
        new("ExecutionVisibility_NotNullIfNotNullMethodReturnContradiction_IsAlwaysFalse", "string value", "value != null && NotNullIfNotNullPredicates.Echo(value: value) == null", true, NotNullIfNotNullSource),
    };

    private static IEnumerable<TestCaseData> SingleCallConditionAlwaysFalseCases()
    {
        var cases = SingleCallConditionAlwaysFalseCaseDataPart1
            .Concat(SingleCallConditionAlwaysFalseCaseDataPart2)
            .Concat(SingleCallConditionAlwaysFalseCaseDataPart3)
            .Concat(SingleCallConditionAlwaysFalseCaseDataPart4)
            .Concat(SingleCallConditionAlwaysFalseCaseDataPart5)
            .Concat(SingleCallConditionAlwaysFalseCaseDataPart6)
            .ToArray();

        if (cases.Length != 178 ||
            cases.Count(static testCase => testCase.Expected) != 150 ||
            cases.Count(static testCase => !testCase.Expected) != 28 ||
            cases.Select(static testCase => testCase.Name).Distinct(StringComparer.Ordinal).Count() != 178)
        {
            throw new InvalidOperationException("Single-call condition visibility case invariants failed.");
        }

        return cases.Select(static testCase => new TestCaseData(testCase).SetName(testCase.Name));
    }

    [TestCaseSource(nameof(SingleCallConditionAlwaysFalseCases))]
    public void ExecutionVisibility_SingleCallConditionAlwaysFalseCases(ConditionAlwaysFalseCase testCase)
    {
        Assert.That(
            IsConditionAlwaysFalse(testCase.ParameterList, testCase.ConditionExpression, testCase.ExtraSource),
            Is.EqualTo(testCase.Expected));
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
                SemanticOracleTestSources.ModeEnum + @"public class TestClass
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
    public void ExecutionVisibility_DoesNotImportOuterFactsIntoDeferredLambda()
    {
        Assert.That(
            IsStatementUnreachable(
                @"
public class TestClass
{
    public void TestMethod()
    {
        var value = 0;
        System.Action action = () =>
        {
            if (value != 0)
            {
                System.Console.WriteLine(1);
            }
        };

        value = 1;
        action();
    }
}",
                "System.Console.WriteLine(1);"),
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

    public sealed record SourceProofCase(
        string Name,
        string Source,
        string FilePath,
        string LineText,
        int Column,
        string Condition,
        SymbolicTruthValue Expected);

    private static readonly SourceProofCase[] SingleProofCaseDataPart1 =
    {
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesGuardStyleSourcePredicateImplications", SourcePredicateSource + @"
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
}", "GuardStyleSourcePredicateImplications.cs", "return value.Length;", 13, "value != null && value.Length > 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesIfElseSourcePredicateImplications", SourcePredicateSource + @"
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
}", "IfElseSourcePredicateImplications.cs", "return value.Length;", 13, "value != null && value.Length > 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesLocalAliasSourcePredicateImplications", SourcePredicateSource + @"
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
}", "LocalAliasSourcePredicateImplications.cs", "return value.Length;", 13, "value != null && value.Length > 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesLocalAssignmentSourcePredicateImplications", SourcePredicateSource + @"
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
}", "LocalAssignmentSourcePredicateImplications.cs", "return value.Length;", 13, "value != null && value.Length > 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesMultiGuardSourcePredicateIndexFacts", SourcePredicateSource + @"
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
}", "MultiGuardSourcePredicateIndexFacts.cs", "return values[index];", 13, "values != null && index >= 0 && index < values.Length", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesGuardStyleSourcePredicateExactValue", SourcePredicateSource + @"
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
}", "GuardStyleSourcePredicateExactValue.cs", "return 10 / divisor;", 13, "divisor == 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesLocalAliasSourcePredicateExactValue", SourcePredicateSource + @"
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
}", "LocalAliasSourcePredicateExactValue.cs", "return 10 / divisor;", 13, "divisor == 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesLocalAssignmentSourcePredicateExactValue", SourcePredicateSource + @"
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
}", "LocalAssignmentSourcePredicateExactValue.cs", "return 10 / divisor;", 13, "divisor == 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesReassignedIntegerLocalSourcePredicate", SourcePredicateSource + @"
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
}", "ReassignedIntegerLocalSourcePredicate.cs", "return value;", 13, "value > -1", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesAssignedRangeAsSpanResultLength", @"
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
}", "AssignedRangeAsSpanResultLength.cs", "return view.Length;", 20, "view.Length == text.Length - 2", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesForeachReceiverNonNull", @"
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
}", "ForeachReceiverNonNull.cs", "return values.Length + value;", 13, "values != null", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesForeachArrayLengthPositive", @"
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
}", "ForeachArrayLengthPositive.cs", "return values.Length + value;", 13, "values.Length > 0", SymbolicTruthValue.ProvenTrue),
    };

    private static IEnumerable<TestCaseData> SingleProofCases()
    {
        var cases = SingleProofCaseDataPart1
            .Concat(SingleProofCaseDataPart2)
            .Concat(SingleProofCaseDataPart3)
            .Concat(SingleProofCaseDataPart4)
            .ToArray();

        var allConvertedNames = cases.Select(static testCase => testCase.Name)
            .Concat(SingleCallConditionAlwaysFalseCaseDataPart1.Select(static testCase => testCase.Name))
            .Concat(SingleCallConditionAlwaysFalseCaseDataPart2.Select(static testCase => testCase.Name))
            .Concat(SingleCallConditionAlwaysFalseCaseDataPart3.Select(static testCase => testCase.Name))
            .Concat(SingleCallConditionAlwaysFalseCaseDataPart4.Select(static testCase => testCase.Name))
            .Concat(SingleCallConditionAlwaysFalseCaseDataPart5.Select(static testCase => testCase.Name))
            .Concat(SingleCallConditionAlwaysFalseCaseDataPart6.Select(static testCase => testCase.Name))
            .ToArray();

        if (cases.Length != 95 ||
            cases.Count(static testCase => testCase.Expected == SymbolicTruthValue.ProvenTrue) != 85 ||
            cases.Count(static testCase => testCase.Expected == SymbolicTruthValue.ProvenFalse) != 2 ||
            cases.Count(static testCase => testCase.Expected == SymbolicTruthValue.Unknown) != 8 ||
            cases.Select(static testCase => testCase.Name).Distinct(StringComparer.Ordinal).Count() != 95 ||
            allConvertedNames.Length != 273 ||
            allConvertedNames.Distinct(StringComparer.Ordinal).Count() != 273)
        {
            throw new InvalidOperationException("Single-proof source query case invariants failed.");
        }

        return cases.Select(static testCase => new TestCaseData(testCase).SetName(testCase.Name));
    }

    [TestCaseSource(nameof(SingleProofCases))]
    public void SymbolicSourceQueryService_ProveConditionAtSource_SingleProofCases(SourceProofCase testCase)
    {
        var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
            testCase.Source,
            testCase.FilePath,
            FindLine(testCase.Source, testCase.LineText),
            testCase.Column,
            testCase.Condition,
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(testCase.Expected), proof.Reason);
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

    private static readonly SourceProofCase[] SingleProofCaseDataPart2 =
    {
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesSingleElementForeachValue", @"
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
}", "SingleElementForeachValue.cs", "return value;", 20, "value == 5", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_DoesNotAssumeMultiElementForeachValue", @"
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
}", "MultiElementForeachValue.cs", "return value;", 20, "value == 0", SymbolicTruthValue.Unknown),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesFiniteForeachNonZeroValue", @"
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
}", "FiniteForeachNonZeroValue.cs", "return value;", 20, "value != 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesPriorAssignedFiniteForeachNonZeroValue", @"
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
}", "PriorAssignedFiniteForeachNonZeroValue.cs", "return value;", 20, "value != 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_DoesNotUseFiniteForeachFactsAfterUnknownReassignment", @"
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
}", "ReassignedFiniteForeachValue.cs", "return value;", 20, "value != 0", SymbolicTruthValue.Unknown),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesLockReceiverNonNull", @"
public class TestClass
{
    public int TestMethod(object gate)
    {
        lock (gate)
        {
            return gate.GetHashCode();
        }
    }
}", "LockReceiverNonNull.cs", "return gate.GetHashCode();", 13, "gate != null", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ReassignedLockReceiverDoesNotKeepNonNullFact", @"
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
}", "LockReceiverReassigned.cs", "return gate.GetHashCode();", 13, "gate != null", SymbolicTruthValue.ProvenFalse),
        new("SymbolicSourceQueryService_ProveConditionAtSource_RefMutatedCompletedReceiverDoesNotKeepNonNullFact", @"
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
}", "RefMutatedCompletedReceiver.cs", "return box.GetHashCode();", 16, "box != null", SymbolicTruthValue.Unknown),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesCatchExceptionVariableNonNull", @"
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
}", "CatchExceptionVariableNonNull.cs", "return ex.Message.Length;", 13, "ex != null", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesCatchFilterCondition", @"
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
}", "CatchFilterCondition.cs", "return 10 / value;", 13, "value > 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesUsingDeclarationResourceAlias", @"
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
}", "UsingDeclarationResourceAlias.cs", "return resource == value ? 1 : 0;", 13, "resource == value", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesUsingDeclarationThrowGuardedResourceNonNull", @"
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
}", "UsingDeclarationThrowGuardedResourceNonNull.cs", "return resource.GetHashCode();", 13, "resource != null", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesUsingExpressionThrowGuardedResourceNonNull", @"
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
}", "UsingExpressionThrowGuardedResourceNonNull.cs", "return value.GetHashCode();", 13, "value != null", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullDominatedCoalesceAssignmentLength", @"
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
}", "NullDominatedCoalesceAssignmentLength.cs", "return values.Length;", 16, "values.Length == 1", SymbolicTruthValue.ProvenTrue),
    };











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



    private static readonly SourceProofCase[] SingleProofCaseDataPart3 =
    {
        new("SymbolicSourceQueryService_ProveConditionAtSource_PreservesKnownNonNullCoalesceAssignmentLength", @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[2];
        values ??= new int[1];
        return values.Length;
    }
}", "KnownNonNullCoalesceAssignmentLength.cs", "return values.Length;", 16, "values.Length == 2", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullDominatedNullableCoalesceAssignmentValue", @"
public class TestClass
{
    public int TestMethod()
    {
        int? maybe = null;
        maybe ??= 5;
        return maybe.Value;
    }
}", "NullDominatedNullableCoalesceAssignmentValue.cs", "return maybe.Value;", 16, "maybe.Value == 5", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesInlineFiniteArrayElementAssignedNonZeroValue", SemanticOracleTestSources.InlineFiniteArrayElementNonZeroDivisor, "InlineFiniteArrayElementAssignedNonZeroValue.cs", "return 10 / divisor;", 20, "divisor != 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesPriorFiniteArrayElementAssignedNonZeroValue", SemanticOracleTestSources.PriorFiniteArrayElementNonZeroDivisor, "PriorFiniteArrayElementAssignedNonZeroValue.cs", "return 10 / divisor;", 20, "divisor != 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleElementAssignedNonZeroValue", SemanticOracleTestSources.TupleElementNonZeroDivisor, "TupleElementAssignedNonZeroValue.cs", "return 10 / divisor;", 20, "divisor != 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesValueTuplePositionalPatternElementFact", @"
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
}", "ValueTuplePositionalPatternElementFact.cs", "return pair.Item1;", 20, "pair.Item1 > 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesNamedTupleElementAssignedNonZeroValue", SemanticOracleTestSources.NamedTupleElementNonZeroDivisor, "NamedTupleElementAssignedNonZeroValue.cs", "return 10 / divisor;", 20, "divisor != 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleLocalDeconstructionAssignedNonZeroValue", SemanticOracleTestSources.TupleLocalDeconstructionAssignedNonZeroDivisor, "TupleLocalDeconstructionAssignedNonZeroValue.cs", "return 10 / divisor;", 20, "divisor != 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleLocalDeconstructionDeclaredNonZeroValue", SemanticOracleTestSources.TupleLocalDeconstructionDeclaredNonZeroDivisor, "TupleLocalDeconstructionDeclaredNonZeroValue.cs", "return 10 / divisor;", 20, "divisor != 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleStringLiteralElementContent", @"
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
}", "TupleStringLiteralElementContent.cs", "return 0;", 12, "pair.text == \"abc\"", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleStringLiteralElementLength", @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (text: ""abc"", other: 1);
        return pair.text.Length;
    }
}", "TupleStringLiteralElementLength.cs", "return pair.text.Length;", 16, "pair.text.Length == 3", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleArrayElementLength", @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (values: new int[2], other: 1);
        return pair.values.Length;
    }
}", "TupleArrayElementLength.cs", "return pair.values.Length;", 16, "pair.values.Length == 2", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleMultidimensionalArrayElementGetLength", @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (values: new int[2, 3], other: 1);
        return pair.values.GetLength(1);
    }
}", "TupleMultidimensionalArrayElementGetLength.cs", "return pair.values.GetLength(1);", 16, "pair.values.GetLength(1) == 3", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesCastedMultidimensionalArrayGetLength", @"
public class TestClass
{
    public int TestMethod()
    {
        return ((int[,])new int[2, 3]).GetLength(1);
    }
}", "CastedMultidimensionalArrayGetLength.cs", "return ((int[,])new int[2, 3]).GetLength(1);", 16, "((int[,])new int[2, 3]).GetLength(1) == 3", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesTupleDeconstructedArrayLength", @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (new int[2], ""abc"");
        var (values, text) = pair;
        return values.Length + text.Length;
    }
}", "TupleDeconstructedArrayLength.cs", "return values.Length + text.Length;", 16, "values.Length == 2 && text.Length == 3", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesDivergentIfElseMergedImplication", @"
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
}", "DivergentIfElseMergedImplication.cs", "return 10 / divisor;", 16, "divisor != 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_DoesNotReuseMutatedBranchConditionForMerge", @"
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
}", "MutatedIfElseMergedImplication.cs", "return 10 / divisor;", 18, "divisor == 1", SymbolicTruthValue.Unknown),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesImplicitElseMergedImplication", @"
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
}", "ImplicitElseMergedImplication.cs", "return 10 / divisor;", 14, "divisor != 0", SymbolicTruthValue.ProvenTrue),
    };



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
            SemanticOracleTestSources.CompoundAssignedNonZeroDivisor,
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
            SemanticOracleTestSources.IncrementedNonZeroDivisor,
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
            SemanticOracleTestSources.TupleAssignedNonZeroDivisor,
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
            SemanticOracleTestSources.TupleDeconstructionDeclaredNonZeroDivisor,
            "return 10 / divisor;");

        Assert.That(facts, Is.Not.Empty);
        Assert.That(facts.Any(fact => fact.Contains("==", StringComparison.Ordinal) &&
                                      fact.Contains("divisor", StringComparison.Ordinal) &&
                                      fact.Contains("1", StringComparison.Ordinal)), Is.True);
    }





    [Test]
    public void
        SymbolicSourceQueryService_ProveConditionAtSource_ProvesInlineFiniteArrayFromEndElementAssignedNonZeroValue()
    {
        const string source = SemanticOracleTestSources.InlineFiniteArrayFromEndElementNonZeroDivisor;
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
        const string source = SemanticOracleTestSources.PriorFiniteArrayFromEndElementNonZeroDivisor;
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
        const string source = SemanticOracleTestSources.ConditionalFiniteArrayElementNonZeroDivisor;
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









    private static readonly SourceProofCase[] SingleProofCaseDataPart4 =
    {
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableNullGuardNoValue", @"
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
}", "NullableNullGuardNoValue.cs", "return 0;", 20, "!value.HasValue", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableIsNotNullPatternHasValue", @"
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
}", "NullableIsNotNullPatternHasValue.cs", "return value.Value;", 20, "value.HasValue", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesFreshObjectAssignmentNonNull", @"
public class TestClass
{
    public int TestMethod()
    {
        object value = new object();
        return value.GetHashCode();
    }
}", "FreshObjectAssignmentNonNull.cs", "return value.GetHashCode();", 16, "value != null", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalFreshObjectAssignmentNonNull", @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        object value = flag ? new object() : new object();
        return value.GetHashCode();
    }
}", "ConditionalFreshObjectAssignmentNonNull.cs", "return value.GetHashCode();", 16, "value != null", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesCoalescedFreshObjectAssignmentNonNull", @"
public class TestClass
{
    public int TestMethod(object input)
    {
        object value = input ?? new object();
        return value.GetHashCode();
    }
}", "CoalescedFreshObjectAssignmentNonNull.cs", "return value.GetHashCode();", 16, "value != null", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalArrayLength", @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var values = flag ? new int[1] : new int[1];
        return values.Length;
    }
}", "ConditionalArrayLength.cs", "return values.Length;", 16, "values.Length == 1", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableGreaterThanGuardValue", @"
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
}", "NullableGreaterThanGuardValue.cs", "return value.Value;", 20, "value.HasValue && value.Value > 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesRecursivePatternAliasMemberFact", @"
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
}", "RecursivePatternAliasMemberFact.cs", "return text.Length;", 20, "text != null && text.Length > 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesExtendedPropertyPatternMemberFact", ExtendedPropertyPatternSource + @"
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
}", "ExtendedPropertyPatternMemberFact.cs", "return box.Child.Value;", 20, "box.Child != null && box.Child.Value > 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableNotNullGuardHasValue", @"
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
}", "NullableNotNullGuardHasValue.cs", "return value.Value;", 20, "value.HasValue", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesSwitchStatementMergedImplication", @"
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
}", "SwitchStatementMergedImplication.cs", "return 10 / divisor;", 24, "divisor != 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_DoesNotMergeSwitchStatementWithoutDefault", @"
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
}", "SwitchStatementNoDefault.cs", "return 10 / divisor;", 21, "divisor != 0", SymbolicTruthValue.Unknown),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesSwitchStatementExitingSectionExclusion", @"
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
}", "SwitchStatementExitingSectionExclusion.cs", "return 10 / value;", 13, "value != 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_SwitchExitExclusionSubstitutesPatternBindingInGuard", @"
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
}", "SwitchPatternBindingExitExclusion.cs", "return value;", 9, "value <= 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionFalse", @"
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
}", "ProveConditionFalse.cs", "return value;", 13, "value != 0", SymbolicTruthValue.ProvenFalse),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesCoalesceAssignmentNonNullLiteral", @"
public class TestClass
{
    public int TestMethod(string value)
    {
        value ??= ""safe"";
        return value.Length;
    }
}", "CoalesceAssignmentNonNullLiteral.cs", "return value.Length;", 16, "value != null", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_DoesNotReuseMutatedImplicitElseConditionForMerge", @"
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
}", "MutatedImplicitElseMergedImplication.cs", "return 10 / divisor;", 15, "divisor == 1", SymbolicTruthValue.Unknown),
        new("SymbolicSourceQueryService_ProveConditionAtSource_MergesIdenticalImplicitElseFactWithMutatedCondition", @"
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
}", "MutatedImplicitElseIdenticalFact.cs", "return 10 / divisor;", 15, "divisor == 1", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesStringSubstringTwoArgumentResultLength", @"
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
}", "StringSubstringTwoArgumentResultLength.cs", "return text.Substring(start, length).Length;", 20, "text.Substring(start, length).Length == length", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesStringAsSpanOneArgumentResultLength", @"
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
}", "StringAsSpanOneArgumentResultLength.cs", "return text.AsSpan(start).Length;", 20, "text.AsSpan(start).Length == text.Length - start", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesReadOnlySpanSliceTwoArgumentResultLength", @"
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
}", "ReadOnlySpanSliceTwoArgumentResultLength.cs", "return values.Slice(start, length).Length;", 20, "values.Slice(start, length).Length == length", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesAssignedRangeElementAccessResultLength", @"
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
}", "AssignedRangeElementAccessResultLength.cs", "return slice.Length;", 20, "slice.Length == values.Length - 2", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesWhileNormalExitImplication", @"
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
}", "ProveConditionWhileExit.cs", "return index;", 13, "index >= values.Length", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesEnumImplication", SemanticOracleTestSources.ModeEnum + @"public class TestClass
{
    public int TestMethod(Mode state)
    {
        if (state == Mode.Ready)
        {
            return 1;
        }

        return 0;
    }
}", "ProveConditionEnum.cs", "return 1;", 13, "state != Mode.None", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesSwitchExpressionValueImplication", @"
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
}", "SwitchExpressionValueImplication.cs", "return 10 / divisor;", 16, "divisor != 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_DoesNotLowerSwitchExpressionWithoutDiscardFallback", @"
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
}", "SwitchExpressionNoFallback.cs", "return 10 / divisor;", 15, "divisor != 0", SymbolicTruthValue.Unknown),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesSwitchStatementSourcePredicateExactValue", SourcePredicateSource + @"
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
}", "SwitchStatementSourcePredicateExactValue.cs", "return 10 / divisor;", 13, "divisor == 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesSwitchStatementPatternSourcePredicateRange", SourcePredicateSource + @"
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
}", "SwitchStatementPatternSourcePredicateRange.cs", "return value;", 13, "value > 0 && value < 10", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesSourceBooleanPropertyImplications", SourcePredicateSource + @"
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
}", "SourceBooleanPropertyImplications.cs", "return box.Value.Length;", 13, "box.Value != null && box.Value.Length > 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesSourceBooleanGetterLocalAliasExactValue", SourcePredicateSource + @"
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
}", "SourceBooleanGetterLocalAliasExactValue.cs", "return 10 / box.Divisor;", 13, "box.Divisor == 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesInstanceSourceBooleanMethodImplications", SourcePredicateSource + @"
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
}", "InstanceSourceBooleanMethodImplications.cs", "return box.Value.Length;", 13, "box.Value != null && box.Value.Length > 0", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesArrayRangeResultLength", @"
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
}", "ArrayRangeResultLength.cs", "return values[1..^1].Length;", 20, "values[1..^1].Length == values.Length - 2", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesStringRangeResultLength", @"
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
}", "StringRangeResultLength.cs", "return text[1..^1].Length;", 20, "text[1..^1].Length == text.Length - 2", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesStringSubstringOneArgumentResultLength", @"
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
}", "StringSubstringOneArgumentResultLength.cs", "return text.Substring(start).Length;", 20, "text.Substring(start).Length == text.Length - start", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalArrayLengthDisjunction", @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var values = flag ? new int[1] : new int[2];
        return values.Length;
    }
}", "ConditionalArrayLengthDisjunction.cs", "return values.Length;", 16, "values.Length == 1 || values.Length == 2", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesCoalescedArrayFallbackLength", @"
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
}", "CoalescedArrayFallbackLength.cs", "return values.Length;", 16, "values.Length == 1", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesCoalescedArrayLengthDisjunction", @"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        var values = input ?? new int[1];
        return values.Length;
    }
}", "CoalescedArrayLengthDisjunction.cs", "return values.Length;", 16, "values.Length == input.Length || values.Length == 1", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableLiteralAssignmentFacts", @"
public class TestClass
{
    public int TestMethod()
    {
        int? value = 5;
        return value.Value;
    }
}", "NullableLiteralAssignmentFacts.cs", "return value.Value;", 16, "value.HasValue && value.Value == 5", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableEqualsConstantGuardValue", @"
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
}", "NullableEqualsConstantGuardValue.cs", "return value.Value;", 20, "value.HasValue && value.Value == 5", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableIsNullPatternNoValue", @"
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
}", "NullableIsNullPatternNoValue.cs", "return 0;", 20, "!value.HasValue", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableRecursivePatternHasValue", @"
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
}", "NullableRecursivePatternHasValue.cs", "return value.Value;", 20, "value.HasValue", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableNotRecursivePatternNoValue", @"
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
}", "NullableNotRecursivePatternNoValue.cs", "return 0;", 20, "!value.HasValue", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesGuardedConditionalNullableHasValue", @"
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
}", "GuardedConditionalNullableFacts.cs", "return value.Value;", 16, "value.HasValue", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesAsExpressionNullSourceResultNull", @"
public class TestClass
{
    public string TestMethod()
    {
        object value = null;
        var text = value as string;
        return text;
    }
}", "AsExpressionNullSourceResultNull.cs", "return text;", 16, "text == null", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesAsExpressionNonNullResultImpliesSourceNonNull", @"
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
}", "AsExpressionNonNullResultImpliesSourceNonNull.cs", "return text;", 20, "value != null", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalAccessNullableValueWhenPresent", @"
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
}", "ConditionalAccessNullableValueWhenPresent.cs", "return length.Value;", 20, "length.Value == 3", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableDeclarationPatternBinding", @"
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
}", "NullableDeclarationPatternBinding.cs", "return value;", 20, "value == 5", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesNullableRelationalPattern", @"
public class TestClass
{
    public int TestMethod()
    {
        int? maybe = 5;
        return maybe.GetValueOrDefault();
    }
}", "NullableRelationalPattern.cs", "return maybe.GetValueOrDefault();", 16, "maybe is > 3 and < 10", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesConditionalAccessReferenceNullSourceResultNull", @"
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
}", "ConditionalAccessReferenceNullSourceResultNull.cs", "return text;", 16, "text == null", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ProvesCoalesceNullResultImpliesOperandsNull", @"
public class TestClass
{
    public string TestMethod(string value, string fallback)
    {
        var result = value ?? fallback;
        return result;
    }
}", "CoalesceNullResultImpliesOperandsNull.cs", "return result;", 16, "result != null || (value == null && fallback == null)", SymbolicTruthValue.ProvenTrue),
        new("SymbolicSourceQueryService_ProveConditionAtSource_ConditionalAccessInvocationResultRemainsUnknown", @"
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
}", "ConditionalAccessInvocationResultRemainsUnknown.cs", "return text;", 16, "holder == null || text != null", SymbolicTruthValue.Unknown),
    };

































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



























































    private static readonly ConditionAlwaysFalseCase[] SingleCallConditionAlwaysFalseCaseDataPart2 =
    {
        new("ExecutionVisibility_NotNullIfNotNullIndexerReturnContradiction_IsAlwaysFalse", "NotNullIfNotNullIndexer box, string key", "box != null && key != null && box[key] == null", true, NotNullIfNotNullSource),
        new("ExecutionVisibility_NotNullIfNotNullNullSourceReturn_RemainsUnknown", "string value", "value == null && NotNullIfNotNullPredicates.Echo(value) != null", false, NotNullIfNotNullSource),
        new("ExecutionVisibility_UnguardedVariableDivision_RemainsUnknown", "int value, int divisor", "value / divisor == 2 && value / divisor != 2", false),
        new("ExecutionVisibility_EnumContradiction_IsAlwaysFalse", "Mode state", "state == Mode.Ready && state != Mode.Ready", true, "public enum Mode { None = 0, Ready = 1 }"),
        new("ExecutionVisibility_NarrowingIntegralCast_RemainsUnknown", "int value", "(byte)value == 0 && value == 256", false),
        new("ExecutionVisibility_CheckedNarrowingIntegralCastContradiction_IsAlwaysFalse", "int value", "checked((byte)value) == 5 && value != 5", true),
        new("ExecutionVisibility_CheckedNarrowingIntegralCastOutOfRangeComparison_IsAlwaysFalse", "int value", "checked((byte)value) > 255", true),
        new("ExecutionVisibility_PropertyPatternContradiction_IsAlwaysFalse", "string text", "text is { Length: > 3 } && text.Length <= 3", true),
        new("ExecutionVisibility_ExtendedPropertyPatternContradiction_IsAlwaysFalse", "ExtendedPatternBox box", "box is { Child.Value: > 0 } && box.Child.Value <= 0", true, ExtendedPropertyPatternSource),
        new("ExecutionVisibility_ValueTuplePositionalPatternContradiction_IsAlwaysFalse", "ValueTuple<int, int> pair", "pair is (_, < 10) && pair.Item2 >= 10", true, "using System;"),
        new("ExecutionVisibility_ArrayEmptyListPatternContradiction_IsAlwaysFalse", "int[] values", "values is [] && values.Length > 0", true),
        new("ExecutionVisibility_ArrayNonEmptyListPatternContradiction_IsAlwaysFalse", "int[] values", "values is [_, ..] && values.Length == 0", true),
        new("ExecutionVisibility_ArrayConstrainedNonEmptyListPatternContradiction_IsAlwaysFalse", "int[] values", "values is [0, ..] && values.Length == 0", true),
        new("ExecutionVisibility_ArrayNestedSliceListPatternContradiction_IsAlwaysFalse", "int[] values", "values is [.. [_, _]] && values.Length < 2", true),
        new("ExecutionVisibility_StringListPatternExactLengthContradiction_IsAlwaysFalse", "string text", "text is [_, _] && text.Length != 2", true),
        new("ExecutionVisibility_ArrayLengthNegative_IsAlwaysFalse", "int[] values", "values.Length < 0", true),
        new("ExecutionVisibility_UnsignedCastBoundsCheckImpliesNonNegativeIndex", "int[] values, int index", "(uint)index < (uint)values.Length && index < 0", true),
        new("ExecutionVisibility_UnsignedCastBoundsCheckImpliesUpperBound", "int[] values, int index", "(uint)index < (uint)values.Length && index >= values.Length", true),
        new("ExecutionVisibility_UnsignedCastBoundsCheckFalseBranchImpliesOutOfRange", "int[] values, int index", "!((uint)index < (uint)values.Length) && index >= 0 && index < values.Length", true),
        new("ExecutionVisibility_UnsignedCastUpperBoundGuardImpliesOutOfRange", "string text, int index", "(uint)index >= (uint)text.Length && index >= 0 && index < text.Length", true),
        new("ExecutionVisibility_StringLengthNegative_IsAlwaysFalse", "string text", "text.Length < 0", true),
        new("ExecutionVisibility_StrictRegexLiteralImpliesStringLength", "string text", @"Regex.IsMatch(text, @""\A[A-Z][0-9]\z"") && text.Length != 2", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_ExplicitCaptureOptionRegexImpliesStringLength", "string text", @"Regex.IsMatch(text, @""\A(?n:[A-Z][0-9])\z"") && text.Length != 2", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_StaticExplicitCaptureRegexOptionContradictsStringEquality", "string text", @"Regex.IsMatch(text, @""\A(A)B\z"", RegexOptions.ExplicitCapture) && text != ""AB""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_StaticCompiledRegexOptionContradictsStringEquality", "string text", @"Regex.IsMatch(text, @""\AAB\z"", RegexOptions.Compiled) && text != ""AB""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_StaticCultureInvariantRegexOptionContradictsStringEquality", "string text", @"Regex.IsMatch(text, @""\AAB\z"", RegexOptions.CultureInvariant) && text != ""AB""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_StaticSinglelineRegexOptionAllowsNewlineDot", "string text", @"!Regex.IsMatch(text, @""\A.\z"", RegexOptions.Singleline) && text == ""\n""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_StaticIgnorePatternWhitespaceRegexOptionContradictsStringEquality", "string text", @"Regex.IsMatch(text, @""\A A\ B \z"", RegexOptions.IgnorePatternWhitespace) && text != ""A B""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_StaticCombinedSupportedRegexOptionsContradictsNegatedNewlineMatch", "string text", @"!Regex.IsMatch(text, @""\A . \z"", RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture) && text == ""\n""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_StaticCompiledCombinedWithSinglelineRegexOptionAllowsNewlineDot", "string text", @"!Regex.IsMatch(text, @""\A.\z"", RegexOptions.Compiled | RegexOptions.Singleline) && text == ""\n""", true, "using System.Text.RegularExpressions;"),
    };



























































    private static readonly ConditionAlwaysFalseCase[] SingleCallConditionAlwaysFalseCaseDataPart3 =
    {
        new("ExecutionVisibility_StaticCultureInvariantCombinedWithSinglelineRegexOptionAllowsNewlineDot", "string text", @"!Regex.IsMatch(text, @""\A.\z"", RegexOptions.CultureInvariant | RegexOptions.Singleline) && text == ""\n""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_ScopedSinglelineDisableRegexDotRejectsNewline", "string text", @"Regex.IsMatch(text, @""\A(?s:A(?-s:.)C)\z"") && text == ""A\nC""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_NamedCaptureRegexContradictsStringEquality", "string text", @"Regex.IsMatch(text, @""\A(?<prefix>AB)C\z"") && text != ""ABC""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_DollarRegexAnchorAllowsTrailingNewline", "string text", "Regex.IsMatch(text, \"^AB$\") && text == \"AB\\n\"", false, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_StrictRegexLiteralContradictsStringEquality", "string text", @"Regex.IsMatch(text, @""\AAB\z"") && text != ""AB""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_RegexMatchSuccessImpliesStringLength", "string text", @"Regex.Match(text, @""\A[A-Z][0-9]\z"").Success && text.Length != 2", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_InstanceRegexMatchSuccessImpliesStringLength", "string text", @"new Regex(@""\A[A-Z][0-9]\z"").Match(text).Success && text.Length != 2", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_RegexMatchesCountPositiveImpliesStringLength", "string text", @"Regex.Matches(text, @""\A[A-Z][0-9]\z"").Count > 0 && text.Length != 2", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_RegexMatchesCountZeroImpliesNonMatch", "string text", @"Regex.Matches(text, @""\AAB\z"").Count == 0 && text == ""AB""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_ReversedRegexMatchesCountPositiveImpliesStringLength", "string text", @"1 <= Regex.Matches(text, @""\A[A-Z][0-9]\z"").Count && text.Length != 2", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_InstanceRegexMatchesCountPositiveImpliesStringLength", "string text", @"new Regex(@""\A[A-Z][0-9]\z"").Matches(text).Count != 0 && text.Length != 2", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_RegexMatchesCountThresholdAboveOneRemainsConservative", "string text", @"Regex.Matches(text, ""A"").Count > 1 && text == ""A""", false, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_InstanceRegexLiteralContradictsStringEquality", "string text", @"new Regex(@""\AAB\z"").IsMatch(text) && text != ""AB""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_GeneratedRegexFactoryMatchSuccessImpliesStringLength", "string text", @"RegexFactories.Ab().Match(text).Success && text.Length != 2", true, GeneratedRegexFactorySource),
        new("ExecutionVisibility_GeneratedRegexFactoryMatchesCountImpliesStringLength", "string text", @"RegexFactories.Ab().Matches(text).Count > 0 && text.Length != 2", true, GeneratedRegexFactorySource),
        new("ExecutionVisibility_GeneratedRegexFactorySinglelineOptionAllowsNewlineDot", "string text", @"!RegexFactories.SinglelineAny().IsMatch(text) && text == ""\n""", true, GeneratedRegexFactorySource),
        new("ExecutionVisibility_StaticReadonlyRegexLiteralContradictsStringEquality", "string text", @"RegexCache.Ab.IsMatch(text) && text != ""AB""", true, StaticRegexCacheSource),
        new("ExecutionVisibility_StaticReadonlyRegexMatchSuccessImpliesStringLength", "string text", "RegexCache.Ab.Match(text).Success && text.Length != 2", true, StaticRegexCacheSource),
        new("ExecutionVisibility_StaticReadonlyRegexMatchesCountImpliesStringLength", "string text", "RegexCache.Ab.Matches(text).Count > 0 && text.Length != 2", true, StaticRegexCacheSource),
        new("ExecutionVisibility_MutableStaticRegexFieldRemainsConservative", "string text", @"RegexCache.MutableAb.IsMatch(text) && text != ""AB""", false, StaticRegexCacheSource),
        new("ExecutionVisibility_InstanceReadonlyRegexLiteralContradictsStringEquality", "RegexBox box, string text", @"box.Ab.IsMatch(text) && text != ""AB""", true, InstanceRegexCacheSource),
        new("ExecutionVisibility_InstanceReadonlyRegexMatchSuccessImpliesStringLength", "RegexBox box, string text", "box.Ab.Match(text).Success && text.Length != 2", true, InstanceRegexCacheSource),
        new("ExecutionVisibility_InstanceReadonlyRegexMatchesCountImpliesStringLength", "RegexBox box, string text", "box.Ab.Matches(text).Count > 0 && text.Length != 2", true, InstanceRegexCacheSource),
        new("ExecutionVisibility_InstanceReadonlyRegexSinglelineOptionAllowsNewlineDot", "RegexBox box, string text", @"!box.SinglelineAny.IsMatch(text) && text == ""\n""", true, InstanceRegexCacheSource),
        new("ExecutionVisibility_InstanceReadonlyRegexMultilineOptionStartAtZeroContradictsStringEquality", "RegexBox box, string text", @"box.MultilineAb.IsMatch(text, 0) && text != ""AB""", true, InstanceRegexCacheSource),
        new("ExecutionVisibility_InstanceReadonlyGeneratedRegexFieldContradictsStringEquality", "GeneratedRegexBox box, string text", @"box.Ab.IsMatch(text) && text != ""AB""", true, GeneratedRegexFactorySource + InstanceRegexCacheSource),
        new("ExecutionVisibility_MutableInstanceRegexFieldRemainsConservative", "RegexBox box, string text", @"box.MutableAb.IsMatch(text) && text != ""AB""", false, InstanceRegexCacheSource),
        new("ExecutionVisibility_ConstructorAssignedReadonlyRegexFieldRemainsConservative", "ConstructorAssignedRegexBox box, string text", @"box.Ab.IsMatch(text) && text != ""AB""", false, InstanceRegexCacheSource),
        new("ExecutionVisibility_StaticReadonlyRegexAssignedInStaticConstructorRemainsConservative", "string text", @"StaticCtorRegexCache.Ab.IsMatch(text) && text != ""AB""", false, InstanceRegexCacheSource),
        new("ExecutionVisibility_InstanceRegexStartAtZeroContradictsStringEquality", "string text", @"new Regex(@""\AAB\z"").IsMatch(text, 0) && text != ""AB""", true, "using System.Text.RegularExpressions;"),
    };





















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





























    private static readonly ConditionAlwaysFalseCase[] SingleCallConditionAlwaysFalseCaseDataPart4 =
    {
        new("ExecutionVisibility_InstanceRegexNonZeroStartAtRemainsConservative", "string text", @"!new Regex(""AB"").IsMatch(text, 1) && text == ""AB""", false, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_InstanceCompiledRegexOptionContradictsStringEquality", "string text", @"new Regex(@""\AAB\z"", RegexOptions.Compiled).IsMatch(text) && text != ""AB""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_InstanceCultureInvariantRegexOptionContradictsStringEquality", "string text", @"new Regex(@""\AAB\z"", RegexOptions.CultureInvariant).IsMatch(text) && text != ""AB""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_InstanceSinglelineRegexOptionAllowsNewlineDot", "string text", @"!new Regex(@""\A.\z"", RegexOptions.Singleline).IsMatch(text) && text == ""\n""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_InstanceMultilineRegexOptionStartAtZeroContradictsStringEquality", "string text", @"new Regex(@""\AAB\z"", RegexOptions.Multiline).IsMatch(text, 0) && text != ""AB""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_StaticMultilineRegexOptionContradictsStringEquality", "string text", @"Regex.IsMatch(text, @""\AAB\z"", RegexOptions.Multiline) && text != ""AB""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_InstanceIgnorePatternWhitespaceRegexOptionContradictsStringEquality", "string text", @"new Regex(@""\A A\ B \z"", RegexOptions.IgnorePatternWhitespace).IsMatch(text) && text != ""A B""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_RegexIsMatchImpliesInputNonNull", "string text", "Regex.IsMatch(text, \"A\") && text == null", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_NegatedRegexIsMatchStillImpliesInputNonNull", "string text", "!Regex.IsMatch(text, \"A\") && text == null", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_ZeroRegexMatchesStillImpliesInputNonNull", "string text", "Regex.Matches(text, \"A\").Count == 0 && text == null", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_ShorthandRegexImpliesStringLength", "string text", @"Regex.IsMatch(text, @""\A\d\s\w\z"") && text.Length != 3", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_NegatedShorthandRegexClassRemainsConservative", "string text", @"Regex.IsMatch(text, @""\A[^\d]\z"") && text == ""A""", false, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_CategoryRegexImpliesStringLength", "string text", @"Regex.IsMatch(text, @""\A\p{Lu}\P{Ll}\z"") && text.Length != 2", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_WordBoundaryRegexLengthImplicationRemainsConservative", "string text", @"Regex.IsMatch(text, @""\A\bAB\B?\z"") && text.Length != 2", false, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_NegatedCategoryRegexClassConcreteMismatchIsUnreachable", "string text", @"Regex.IsMatch(text, @""\A[^\p{Lu}]\z"") && text == ""A""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_UnsupportedRegexOptionsRemainConservative", "string text", "Regex.IsMatch(text, \"^ab$\", RegexOptions.IgnoreCase) && text == \"AB\"", false, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_UnsupportedRegexOptionsConcreteMismatchUsesSelfVerification", "string text", "Regex.IsMatch(text, \"^ab$\", RegexOptions.IgnoreCase) && text == \"CD\"", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_CultureInvariantWithUnsupportedRegexOptionsRemainConservative", "string text", "Regex.IsMatch(text, \"^ab$\", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase) && text == \"AB\"", false, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_InstanceUnsupportedRegexOptionsRemainConservative", "string text", "new Regex(\"^ab$\", RegexOptions.IgnoreCase).IsMatch(text) && text == \"AB\"", false, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_InstanceUnsupportedRegexOptionsConcreteMismatchUsesSelfVerification", "string text", "new Regex(\"^ab$\", RegexOptions.IgnoreCase).IsMatch(text) && text == \"CD\"", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_InstanceCultureInvariantWithUnsupportedRegexOptionsRemainConservative", "string text", "new Regex(\"^ab$\", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).IsMatch(text) && text == \"AB\"", false, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_UnsupportedInlineIgnoreCaseRegexConcreteMismatchUsesSelfVerification", "string text", @"Regex.IsMatch(text, @""\A(?i:ab)\z"") && text == ""CD""", true, "using System.Text.RegularExpressions;"),
        new("ExecutionVisibility_StringContainsContradictsStringEquality", "string text", "text.Contains(\"Z\") && text == \"ABC\"", true),
        new("ExecutionVisibility_StringContainsCharContradictsStringEquality", "string text", "text.Contains('Z') && text == \"ABC\"", true),
        new("ExecutionVisibility_StringContainsOrdinalIgnoreCaseContradictsStringEquality", "string text", "text.Contains(\"a\", StringComparison.OrdinalIgnoreCase) && text == \"BBB\"", true, "using System;"),
        new("ExecutionVisibility_StringStartsWithOrdinalIgnoreCaseContradictsStringEquality", "string text", "text.StartsWith(\"ab\", StringComparison.OrdinalIgnoreCase) && text == \"zzAB\"", true, "using System;"),
        new("ExecutionVisibility_StringEndsWithOrdinalIgnoreCaseContradictsStringEquality", "string text", "text.EndsWith(\"xy\", StringComparison.OrdinalIgnoreCase) && text == \"XYzz\"", true, "using System;"),
        new("ExecutionVisibility_StringIndexOfCharFoundContradictsStringEquality", "string text", "text.IndexOf('Z') >= 0 && text == \"ABC\"", true),
        new("ExecutionVisibility_StringIndexOfCharNotFoundContradictsStringEquality", "string text", "text.IndexOf('A') == -1 && text == \"ABC\"", true),
        new("ExecutionVisibility_StringIndexOfCharReversedFoundComparisonContradictsStringEquality", "string text", "0 <= text.IndexOf('Z') && text == \"ABC\"", true),
    };

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

















































    private static readonly ConditionAlwaysFalseCase[] SingleCallConditionAlwaysFalseCaseDataPart5 =
    {
        new("ExecutionVisibility_StringIndexOfOrdinalFoundContradictsStringEquality", "string text", "text.IndexOf(\"ZZ\", StringComparison.Ordinal) >= 0 && text == \"ABC\"", true, "using System;"),
        new("ExecutionVisibility_StringIndexOfOrdinalNotFoundContradictsStringEquality", "string text", "text.IndexOf(\"AB\", StringComparison.Ordinal) < 0 && text == \"ABC\"", true, "using System;"),
        new("ExecutionVisibility_StringIndexOfDefaultStringSearchRemainsConservative", "string text", "text.IndexOf(\"a\") >= 0 && text == \"A\"", false),
        new("ExecutionVisibility_StringIndexOfOrdinalIgnoreCaseContradictsStringEquality", "string text", "text.IndexOf(\"a\", StringComparison.OrdinalIgnoreCase) < 0 && text == \"A\"", true, "using System;"),
        new("ExecutionVisibility_StringLastIndexOfCharFoundContradictsStringEquality", "string text", "text.LastIndexOf('Z') >= 0 && text == \"ABC\"", true),
        new("ExecutionVisibility_StringLastIndexOfOrdinalNotFoundContradictsStringEquality", "string text", "text.LastIndexOf(\"AB\", StringComparison.Ordinal) < 0 && text == \"ABC\"", true, "using System;"),
        new("ExecutionVisibility_StringLastIndexOfDefaultStringSearchRemainsConservative", "string text", "text.LastIndexOf(\"a\") >= 0 && text == \"A\"", false),
        new("ExecutionVisibility_StringLastIndexOfOrdinalIgnoreCaseContradictsStringEquality", "string text", "text.LastIndexOf(\"a\", StringComparison.OrdinalIgnoreCase) < 0 && text == \"A\"", true, "using System;"),
        new("ExecutionVisibility_StringStartsWithCharContradictsEmptyString", "string text", "text.StartsWith('A') && text == string.Empty", true),
        new("ExecutionVisibility_InstanceStringEqualsOrdinalContradictsInequality", "string text", "text.Equals(\"A\", StringComparison.Ordinal) && text != \"A\"", true, "using System;"),
        new("ExecutionVisibility_StaticStringEqualsOrdinalContradictsInequality", "string text", "string.Equals(text, \"A\", StringComparison.Ordinal) && text != \"A\"", true, "using System;"),
        new("ExecutionVisibility_InstanceStringEqualsOrdinalIgnoreCaseContradictsStringEquality", "string text", "text.Equals(\"a\", StringComparison.OrdinalIgnoreCase) && text == \"B\"", true, "using System;"),
        new("ExecutionVisibility_StaticStringEqualsOrdinalIgnoreCaseContradictsStringEquality", "string text", "string.Equals(\"a\", text, StringComparison.OrdinalIgnoreCase) && text == \"B\"", true, "using System;"),
        new("ExecutionVisibility_StringLiteralEqualityImpliesNonNull", "string text", "text == \"A\" && text == null", true),
        new("ExecutionVisibility_StringConcatContradictsStringEquality", "string left, string right", "left == \"A\" && right == \"B\" && (left + right) != \"AB\"", true),
        new("ExecutionVisibility_NullStringConcatUsesEmptyString", "string text", "text == null && (text + \"X\") != \"X\"", true),
        new("ExecutionVisibility_StringConcatLengthContradiction_IsAlwaysFalseAfterNormalCompletion", "string left, string right", "left != null && right != null && (left + right).Length != left.Length + right.Length", true),
        new("ExecutionVisibility_StringPredicateOnConcatContradiction_IsAlwaysFalse", "string suffix", "!(\"PRE\" + suffix).StartsWith(\"PRE\", StringComparison.Ordinal)", true, "using System;"),
        new("ExecutionVisibility_StringSubstringLengthContradiction_IsAlwaysFalse", "string text, int start", "text != null && start >= 0 && start <= text.Length && text.Substring(start).Length != text.Length - start", true),
        new("ExecutionVisibility_StringPrefixSubstringEqualityContradictsStringEquality", "string text", "text.Substring(0, 3) == \"PRE\" && text == \"ALT\"", true),
        new("ExecutionVisibility_StringIsNullOrWhiteSpaceContradictsNonWhitespaceLiteral", "string text", "string.IsNullOrWhiteSpace(text) && text == \"A\"", true),
        new("ExecutionVisibility_StringIsNullOrWhiteSpaceFalseBranchImpliesNonEmpty", "string text", "!string.IsNullOrWhiteSpace(text) && text.Length == 0", true),
        new("ExecutionVisibility_StringIsNullOrWhiteSpaceFalseBranchRejectsWhitespaceLiteral", "string text", "!string.IsNullOrWhiteSpace(text) && text == \" \\t\\r\\n\"", true),
        new("ExecutionVisibility_StringIsNullOrWhiteSpaceAllowsNonEmptyWhitespace", "string text", "string.IsNullOrWhiteSpace(text) && text != null && text.Length > 0", false),
        new("ExecutionVisibility_CustomLengthNegative_RemainsUnknown", "HasLength value", "value.Length < 0", false, "public sealed class HasLength { public int Length => -1; }"),
        new("ExecutionVisibility_SourceNullOrEmptyPredicateTrueBranchLengthContradiction_IsAlwaysFalse", "string text", "SourcePredicates.IsNullOrEmptyLike(text) && text != null && text.Length > 0", true, SourcePredicateSource),
        new("ExecutionVisibility_SourceNullOrEmptyPredicateFalseBranchLengthContradiction_IsAlwaysFalse", "string text", "!SourcePredicates.IsNullOrEmptyLike(text) && text.Length <= 0", true, SourcePredicateSource),
        new("ExecutionVisibility_SourceRangePredicateContradiction_IsAlwaysFalse", "int value", "SourcePredicates.InRange(value) && (value < 10 || value > 20)", true, SourcePredicateSource),
        new("ExecutionVisibility_SourceSwitchStatementPredicateContradiction_IsAlwaysFalse", "int value", "SourcePredicates.IsZeroWithSwitch(value) && value != 0", true, SourcePredicateSource),
        new("ExecutionVisibility_SourceSwitchStatementPatternPredicateContradiction_IsAlwaysFalse", "int value", "SourcePredicates.IsSmallPositiveWithSwitch(value) && (value <= 0 || value >= 10)", true, SourcePredicateSource),
    };



























































    private static readonly ConditionAlwaysFalseCase[] SingleCallConditionAlwaysFalseCaseDataPart6 =
    {
        new("ExecutionVisibility_SourceMultiGuardIndexPredicateContradiction_IsAlwaysFalse", "int[] values, int index", "SourcePredicates.IsValidIndex(values, index) && (values == null || index < 0 || index >= values.Length)", true, SourcePredicateSource),
        new("ExecutionVisibility_SourcePositivePredicateArgumentExpression_IsAlwaysFalse", "int value", "SourcePredicates.IsPositive(value + 1) && value < -1", true, SourcePredicateSource),
        new("ExecutionVisibility_SourcePositivePredicateReachable_RemainsUnknown", "int value", "SourcePredicates.IsPositive(value) && value > 10", false, SourcePredicateSource),
        new("ExecutionVisibility_SourceHasTextPredicateNullContradiction_IsAlwaysFalse", "string text", "SourcePredicates.HasText(text) && text == null", true, SourcePredicateSource),
        new("ExecutionVisibility_SourceHasTextPredicateLengthContradiction_IsAlwaysFalse", "string text", "SourcePredicates.HasText(text) && text.Length <= 0", true, SourcePredicateSource),
        new("ExecutionVisibility_SourceHasTextGuardPredicateContradiction_IsAlwaysFalse", "string text", "SourcePredicates.HasTextWithGuard(text) && (text == null || text.Length <= 0)", true, SourcePredicateSource),
        new("ExecutionVisibility_SourceHasTextIfElsePredicateContradiction_IsAlwaysFalse", "string text", "SourcePredicates.HasTextWithIfElse(text) && (text == null || text.Length <= 0)", true, SourcePredicateSource),
        new("ExecutionVisibility_SourceHasTextLocalAliasPredicateContradiction_IsAlwaysFalse", "string text", "SourcePredicates.HasTextViaLocal(text) && (text == null || text.Length <= 0)", true, SourcePredicateSource),
        new("ExecutionVisibility_SourceHasTextLocalAssignmentPredicateContradiction_IsAlwaysFalse", "string text", "SourcePredicates.HasTextViaAssignment(text) && (text == null || text.Length <= 0)", true, SourcePredicateSource),
        new("ExecutionVisibility_SourceLocalAssignmentIntegerPredicateContradiction_IsAlwaysFalse", "int value", "SourcePredicates.IsPositiveAfterLocalAssignment(value) && value < -1", true, SourcePredicateSource),
        new("ExecutionVisibility_SourceBooleanPropertyContradiction_IsAlwaysFalse", "SourcePredicateBox box", "box.HasText && (box.Value == null || box.Value.Length <= 0)", true, SourcePredicateSource),
        new("ExecutionVisibility_InstanceSourceBooleanMethodContradiction_IsAlwaysFalse", "SourcePredicateBox box", "box.HasTextMethod() && (box.Value == null || box.Value.Length <= 0)", true, SourcePredicateSource),
        new("ExecutionVisibility_StringLiteralLengthContradiction_IsAlwaysFalse", "", "\"abc\".Length != 3", true),
        new("ExecutionVisibility_StringEmptyLengthContradiction_IsAlwaysFalse", "", "string.Empty.Length > 0", true),
        new("ExecutionVisibility_CollectionCountNegativeContradiction_IsAlwaysFalse", "System.Collections.Generic.IReadOnlyCollection<int> values", "values.Count < 0", true),
        new("ExecutionVisibility_SourceNullOrEmptyPredicateNestedInNegation_IsAlwaysFalse", "string text", "!(SourcePredicates.IsNullOrEmptyLike(text)) && text.Length <= 0", true, SourcePredicateSource),
        new("ExecutionVisibility_SourceHasTextPredicateInOrFalseBranch_IsAlwaysFalse", "string text", "!(SourcePredicates.HasText(text) || false) && text.Length > 0 && text != null", true, SourcePredicateSource),
        new("ExecutionVisibility_SourceNullOrEmptyPredicateReachable_RemainsUnknown", "string text", "SourcePredicates.IsNullOrEmptyLike(text) && text != null && text.Length == 0", false, SourcePredicateSource),
        new("ExecutionVisibility_DeclarationPatternImpliesNonNull_IsAlwaysFalse", "object value", "value is string && value == null", true),
        new("ExecutionVisibility_AsExpressionNonNullImpliesSourceNonNull_IsAlwaysFalse", "object value", "(value as string) != null && value == null", true),
        new("ExecutionVisibility_AsExpressionNonNullImpliesRuntimeType_IsAlwaysFalse", "object value", "(value as string) != null && value is not string", true),
        new("ExecutionVisibility_AsExpressionNullContradictsRuntimeType_IsAlwaysFalse", "object value", "(value as string) == null && value is string", true),
        new("ExecutionVisibility_BooleanVariableContradiction_IsAlwaysFalse", "bool ready", "ready && !ready", true),
        new("ExecutionVisibility_BitwiseBooleanAndContradiction_IsAlwaysFalse", "int value", "(value == 0) & (value != 0)", true),
        new("ExecutionVisibility_BitwiseBooleanOrFalseBranchContradiction_IsAlwaysFalse", "int value", "!((value < 0) | (value > 0)) && value != 0", true),
        new("ExecutionVisibility_BooleanExclusiveOrContradiction_IsAlwaysFalse", "bool left, bool right", "(left ^ right) && left == right", true),
        new("ExecutionVisibility_DefaultLiteralNullContradiction_IsAlwaysFalse", "string value", "value != null && value == default", true),
        new("ExecutionVisibility_DefaultExpressionZeroContradiction_IsAlwaysFalse", "int value", "value == default(int) && value != 0", true),
    };





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

}
