using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicInvariantServiceTests
{
    [Test]
    public void ProveImplicationAt_ProvesConditionFromPathFacts()
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

        var (returnStatement, semanticModel, condition) = CreateGuardedReturnContext(source, "return value;");
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var proof = new SymbolicInvariantService().ProveImplicationAt(
            returnStatement,
            semanticModel,
            condition,
            smtAnalysis);

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        Assert.That(proof.Reachability, Is.EqualTo(SymbolicReachability.Reachable));
        Assert.That(proof.SmtDiagnostics.IsConfigured, Is.True);
    }

    [Test]
    public void ProveImplicationAt_ProvesNegatedConditionFalseFromPathFacts()
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

        var (returnStatement, semanticModel, condition) = CreateGuardedReturnContext(source, "return value;");
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var negatedCondition = new SymbolicNotCondition(condition);

        var proof = new SymbolicInvariantService().ProveImplicationAt(
            returnStatement,
            semanticModel,
            negatedCondition,
            smtAnalysis);

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse), proof.Reason);
        Assert.That(proof.Reachability, Is.EqualTo(SymbolicReachability.Reachable));
    }

    [Test]
    public void ProveImplicationAt_ReturnsUnreachableWhenProgramPointIsUnsatisfiable()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0 && value < 0)
        {
            return value;
        }

        return 0;
    }
}";

        var (returnStatement, semanticModel, _) = CreateGuardedReturnContext(source, "return value;");
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var proof = new SymbolicInvariantService().ProveImplicationAt(
            returnStatement,
            semanticModel,
            new SymbolicConstantCondition(true),
            smtAnalysis);

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unreachable), proof.Reason);
        Assert.That(proof.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
    }

    [Test]
    public void ProveImplicationAt_ReturnsUnknownWithoutSmtService()
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

        var (returnStatement, semanticModel, condition) = CreateGuardedReturnContext(source, "return value;");

        var proof = new SymbolicInvariantService().ProveImplicationAt(
            returnStatement,
            semanticModel,
            condition,
            null);

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        Assert.That(proof.Reason, Is.EqualTo("smt_required"));
        Assert.That(proof.SmtDiagnostics.IsConfigured, Is.False);
    }

    [Test]
    public void ProveImplicationAt_ProvesBooleanLocalAliasInitializer()
    {
        const string source = @"
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
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "SymbolicBooleanAlias.cs");
        var compilation = CSharpCompilation.Create(
            "SymbolicBooleanAlias",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();
        var returnStatement = root.DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .First(statement => statement.Expression is BinaryExpressionSyntax binary &&
                                binary.IsKind(SyntaxKind.DivideExpression));
        var initializerCondition = root.DescendantNodes()
            .OfType<EqualsValueClauseSyntax>()
            .Single()
            .Value;
        var loweringContext = new SymbolicLoweringContext(semanticModel, default);
        Assert.That(
            TypedSymbolicTestLowering.TryLowerCondition(initializerCondition, loweringContext, out var condition),
            Is.True);
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var proof = new SymbolicInvariantService().ProveImplicationAt(
            returnStatement,
            semanticModel,
            condition,
            smtAnalysis);

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
    }

    [Test]
    public void RuntimeHazards_ProveDivideByZeroThroughBooleanLocalAlias()
    {
        const string source = @"
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
}";
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = new SymbolicRuntimeHazardQueryService().QuerySourceRuntimeHazards(
            source,
            "SymbolicBooleanAliasHazard.cs",
            smtAnalysis,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            options: new SymbolicRuntimeHazardQueryOptions(includeUnprovenCandidates: true));

        var hazard = result.Hazards.Single(candidate => candidate.Kind == SymbolicRuntimeHazardKind.DivideByZero);
        Assert.That(
            hazard.Status,
            Is.EqualTo(SymbolicRuntimeHazardStatus.Proven),
            hazard.StatusReason);
    }

    [Test]
    public void RuntimeHazards_RejectZeroAfterNonZeroCompoundAssignment()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 0;
        divisor += 1;
        return 10 / divisor;
    }

}";
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = new SymbolicRuntimeHazardQueryService().QuerySourceRuntimeHazards(
            source,
            "SymbolicCompoundAssignmentHazard.cs",
            smtAnalysis,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            options: new SymbolicRuntimeHazardQueryOptions(includeUnprovenCandidates: true));
        var hazard = result.Hazards.Single(candidate => candidate.Kind == SymbolicRuntimeHazardKind.DivideByZero);

        Assert.That(
            hazard.Status,
            Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable),
            hazard.StatusReason);
    }

    [Test]
    public void RuntimeHazards_ProveMemorySliceOutOfRangeThroughLengthAlias()
    {
        const string source = @"
using System;

public class TestClass
{
    public Memory<int> TestMethod(Memory<int> values, int start)
    {
        var copy = values;
        if (start > copy.Length)
        {
            return values.Slice(start);
        }

        return values.Slice(0, 0);
    }
}";
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = new SymbolicRuntimeHazardQueryService().QuerySourceRuntimeHazards(
            source,
            "SymbolicMemorySliceAliasHazard.cs",
            smtAnalysis,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            options: new SymbolicRuntimeHazardQueryOptions(includeUnprovenCandidates: true));
        var hazard = result.Hazards.Single(candidate =>
            candidate.Kind == SymbolicRuntimeHazardKind.ArgumentOutOfRange &&
            candidate.OperationText.Contains("values.Slice(start)", StringComparison.Ordinal));

        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven), hazard.StatusReason);
    }

    [Test]
    public void RuntimeHazards_RetainNullableLoopCarriedMissingValue()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(bool repeat)
    {
        int? value = 1;
        var result = 0;
        while (repeat)
        {
            result = value.Value;
            value = null;
        }

        return result;
    }
}";
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = new SymbolicRuntimeHazardQueryService().QuerySourceRuntimeHazards(
            source,
            "SymbolicNullableLoopHazard.cs",
            smtAnalysis,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            options: new SymbolicRuntimeHazardQueryOptions(includeUnprovenCandidates: true));
        var hazard = result.Hazards.Single(candidate =>
            candidate.Kind == SymbolicRuntimeHazardKind.NullableValueWithoutValue);

        Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven), hazard.StatusReason);
    }

    [Test]
    public void ProveImplicationAt_RejectsZeroAfterNonZeroCompoundAssignment()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 0;
        divisor += 1;
        return 10 / divisor;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "SymbolicCompoundAssignment",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();
        var division = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .Single(expression => expression.IsKind(SyntaxKind.DivideExpression));
        var loweringContext = new SymbolicLoweringContext(semanticModel, default);
        Assert.That(TypedSymbolicTestLowering.TryLowerTerm(division.Right, loweringContext, out var divisor), Is.True);
        var zeroCondition = SymbolicIrLowerer.CreateIntegerZeroCondition(
            divisor,
            division.Right,
            "ir.test.compound-assignment.zero");
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var proof = new SymbolicInvariantService().ProveImplicationAt(
            division,
            semanticModel,
            zeroCondition,
            smtAnalysis);

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse), proof.Reason);
    }

    private static (ReturnStatementSyntax ReturnStatement, SemanticModel SemanticModel, SymbolicCondition GuardCondition
        )
        CreateGuardedReturnContext(string source, string returnMarker)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "SymbolicInvariantServiceProof.cs");
        var compilation = CSharpCompilation.Create(
            "SymbolicInvariantServiceProof",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();
        var returnPosition = source.IndexOf(returnMarker, StringComparison.Ordinal);
        Assert.That(returnPosition, Is.GreaterThanOrEqualTo(0));

        var returnStatement = root
            .DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Single(statement => statement.SpanStart == returnPosition);
        var ifStatement = returnStatement.Ancestors().OfType<IfStatementSyntax>().First();

        var loweringContext = new SymbolicLoweringContext(semanticModel, default);
        Assert.That(TypedSymbolicTestLowering.TryLowerCondition(ifStatement.Condition, loweringContext, out var guardCondition),
            Is.True);
        Assert.That(guardCondition, Is.Not.Null);

        return (returnStatement, semanticModel, guardCondition!);
    }
}
