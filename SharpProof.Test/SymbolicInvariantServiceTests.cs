using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicInvariantServiceTests {
    private const string PositiveGuardSource =
        "public class TestClass { public int TestMethod(int value) { if(value>0){return value;} return 0; } }";
    private const string BooleanAliasSource =
        "public class TestClass { public int TestMethod(int divisor) { var isZero=divisor==0; if(isZero){return 10/divisor;} return 0; } }";
    private const string CompoundAssignmentSource =
        "public class TestClass { public int TestMethod() { var divisor=0; divisor+=1; return 10/divisor; } }";

    private sealed record GuardedProofCase(string Source, bool Negate, bool Constant, SymbolicTruthValue Expected);

    private static IEnumerable<TestCaseData> GuardedProofCases() {
        yield return GuardedCase("ProveImplicationAt_ProvesConditionFromPathFacts", PositiveGuardSource, false, false, SymbolicTruthValue.ProvenTrue);
        yield return GuardedCase("ProveImplicationAt_ProvesNegatedConditionFalseFromPathFacts", PositiveGuardSource, true, false, SymbolicTruthValue.ProvenFalse);
        yield return GuardedCase("ProveImplicationAt_ReturnsUnreachableWhenProgramPointIsUnsatisfiable",
            "public class TestClass { public int TestMethod(int value) { if(value>0&&value<0){return value;} return 0; } }",
            false, true, SymbolicTruthValue.Unreachable);
    }

    [TestCaseSource(nameof(GuardedProofCases))]
    public void GuardedProofMatrix(object value) {
        var testCase = (GuardedProofCase)value;
        var (returnStatement, semanticModel, guard) = CreateGuardedReturnContext(testCase.Source, "return value;");
        var condition = testCase.Constant ? new SymbolicConstantCondition(true) : testCase.Negate ? new SymbolicNotCondition(guard) : guard;
        using var smt = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var proof = ProveCondition(returnStatement, semanticModel, condition, smt);
        Assert.That(proof.TruthValue, Is.EqualTo(testCase.Expected), proof.Reason);
    }

    private static TestCaseData GuardedCase(
        string name, string source, bool negate, bool constant, SymbolicTruthValue expected) =>
        new TestCaseData(new GuardedProofCase(source, negate, constant, expected)).SetName(name);

    [Test]
    public void ProveImplicationAt_ReturnsUnknownWithoutSmtService() {
        var (statement, model, condition) = CreateGuardedReturnContext(PositiveGuardSource, "return value;");
        var line = statement.GetLocation().GetLineSpan().StartLinePosition;
        var exception = Assert.Throws<ArgumentException>(() => new SymbolicQueryExecutor().Prove(
            new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(model.SyntaxTree, model.Compilation),
                SharpProofTargetFactory.Point(line.Line + 1, line.Character + 1)),
            SymbolicFormulaDisplay.Format(condition)));
        Assert.That(exception!.Message, Does.Contain("SMT analysis"));
    }

    [Test]
    public void ProveImplicationAt_ProvesBooleanLocalAliasInitializer() {
        var context = AnalyzerTestHost.CreateSourceContext(BooleanAliasSource, "SymbolicBooleanAlias");
        var statement = context.Root.DescendantNodes().OfType<ReturnStatementSyntax>()
            .First(node => node.Expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.DivideExpression));
        var initializer = context.Root.DescendantNodes().OfType<EqualsValueClauseSyntax>().Single().Value;
        Assert.That(TypedSymbolicTestLowering.TryLowerCondition( initializer, new SymbolicLoweringContext(context.SemanticModel, default), out var condition), Is.True);
        using var smt = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var proof = ProveCondition(statement, context.SemanticModel, condition, smt);
        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
    }

    private sealed record HazardCase(
        string Source,
        string FileName,
        SymbolicRuntimeHazardKind Kind,
        SymbolicRuntimeHazardStatus Expected,
        string? Operation = null);

    private static IEnumerable<TestCaseData> HazardCases() {
        yield return Hazard("RuntimeHazards_ProveDivideByZeroThroughBooleanLocalAlias", BooleanAliasSource,
            "SymbolicBooleanAliasHazard.cs", SymbolicRuntimeHazardKind.DivideByZero, SymbolicRuntimeHazardStatus.Proven);
        yield return Hazard("RuntimeHazards_RejectZeroAfterNonZeroCompoundAssignment", CompoundAssignmentSource,
            "SymbolicCompoundAssignmentHazard.cs", SymbolicRuntimeHazardKind.DivideByZero, SymbolicRuntimeHazardStatus.Unreachable);
        yield return Hazard("RuntimeHazards_ProveMemorySliceOutOfRangeThroughLengthAlias",
            "using System; public class TestClass { public Memory<int> TestMethod(Memory<int> values,int start) { var copy=values; if(start>copy.Length){return values.Slice(start);} return values.Slice(0,0); } }",
            "SymbolicMemorySliceAliasHazard.cs", SymbolicRuntimeHazardKind.ArgumentOutOfRange,
            SymbolicRuntimeHazardStatus.Proven, "values.Slice(start)");
        yield return Hazard("RuntimeHazards_RetainNullableLoopCarriedMissingValue",
            "public class TestClass { public int TestMethod(bool repeat) { int? value=1; var result=0; while(repeat){result=value.Value; value=null;} return result; } }",
            "SymbolicNullableLoopHazard.cs", SymbolicRuntimeHazardKind.NullableValueWithoutValue,
            SymbolicRuntimeHazardStatus.Proven);
    }

    [TestCaseSource(nameof(HazardCases))]
    public void RuntimeHazardMatrix(object value) {
        var testCase = (HazardCase)value;
        using var smt = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var (tree, compilation) = SymbolicSourceCompilation.Create(
            testCase.Source, testCase.FileName, SymbolicSourceCompilationKind.RuntimeHazards,
            AnalyzerTestHost.GetTrustedPlatformReferences(), default);
        var result = new SymbolicRuntimeHazardQueryService().QuerySyntaxTreeRuntimeHazards(
            tree, compilation, SharpProofTargetFactory.AllLines(), smt, default,
            new SymbolicRuntimeHazardQueryOptions(includeUnprovenCandidates: true));
        var hazard = result.Hazards.Single(candidate => candidate.Kind == testCase.Kind &&
            (testCase.Operation == null || candidate.OperationText.Contains(testCase.Operation, StringComparison.Ordinal)));
        Assert.That(hazard.Status, Is.EqualTo(testCase.Expected), hazard.StatusReason);
    }

    private static TestCaseData Hazard(
        string name, string source, string fileName, SymbolicRuntimeHazardKind kind,
        SymbolicRuntimeHazardStatus expected, string? operation = null) =>
        new TestCaseData(new HazardCase(source, fileName, kind, expected, operation)).SetName(name);

    [Test]
    public void ProveImplicationAt_RejectsZeroAfterNonZeroCompoundAssignment() {
        var context = AnalyzerTestHost.CreateSourceContext(CompoundAssignmentSource, "SymbolicCompoundAssignment");
        var division = context.Root.DescendantNodes().OfType<BinaryExpressionSyntax>()
            .Single(expression => expression.IsKind(SyntaxKind.DivideExpression));
        Assert.That(TypedSymbolicTestLowering.TryLowerTerm( division.Right, new SymbolicLoweringContext(context.SemanticModel, default), out var divisor), Is.True);
        var condition = SymbolicIrLowerer.CreateIntegerZeroCondition(
            divisor, division.Right, "ir.test.compound-assignment.zero");
        using var smt = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var proof = ProveCondition(division, context.SemanticModel, condition, smt);
        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse), proof.Reason);
    }

    private static (ReturnStatementSyntax Statement, SemanticModel Model, SymbolicCondition Guard)
        CreateGuardedReturnContext(string source, string marker) {
        var context = AnalyzerTestHost.CreateSourceContext(source, "SymbolicInvariantServiceProof");
        var position = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(position, Is.GreaterThanOrEqualTo(0));
        var statement = context.Root.DescendantNodes().OfType<ReturnStatementSyntax>()
            .Single(node => node.SpanStart == position);
        var ifStatement = statement.Ancestors().OfType<IfStatementSyntax>().First();
        Assert.That(TypedSymbolicTestLowering.TryLowerCondition( ifStatement.Condition, new SymbolicLoweringContext(context.SemanticModel, default), out var guard), Is.True);
        return (statement, context.SemanticModel, guard);
    }

    private static SymbolicConditionProofResult ProveCondition(
        SyntaxNode node, SemanticModel semanticModel, SymbolicCondition condition, SmtAnalysisService smt) =>
        new SymbolicConditionProofEngine(new SymbolicInvariantService()).ProveAtSyntaxNode(
            semanticModel, node, SymbolicFormulaDisplay.Format(condition), condition, new SymbolicState(), smt, false);
}
