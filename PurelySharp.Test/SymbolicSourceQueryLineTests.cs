using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class SymbolicSourceQueryLineTests
    {
        [Test]
        public void QuerySyntaxTreeLine_ReturnsEveryProgramPointOnLine()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0) { return value; }
        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "LineQuery.cs");
            var compilation = CSharpCompilation.Create(
                "LineQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = new SymbolicSourceQueryService().QuerySyntaxTreeLine(
                syntaxTree,
                compilation,
                FindLine(source, "if (value > 0)"),
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "value > 0" });

            Assert.That(result.ProgramPoints.Select(point => point.NodeKind), Does.Contain("IfStatement"));
            var returnPoint = result.ProgramPoints.Single(point => point.NodeKind == "ReturnStatement");
            Assert.That(returnPoint.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
            Assert.That(returnPoint.MergedInvariantText, Is.EqualTo("value > 0"));
            var summary = SymbolicInvariantService.MergeInvariantFacts(result.ProgramPoints.Select(point => point.Facts));
            Assert.That(summary.Facts, Is.EquivalentTo(result.ProgramPoints.SelectMany(point => point.Facts).Distinct()));
            Assert.That(summary.MergedInvariantText, Does.Contain("value"));
            Assert.That(result.Facts, Is.EquivalentTo(summary.Facts));
            Assert.That(result.ObservedInvariant.MergedInvariantText, Is.EqualTo(summary.MergedInvariantText));
            Assert.That(result.ObservedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.DistinctFactUnion));
            Assert.That(result.MergedInvariantText, Is.EqualTo("unknown(value)"));
            Assert.That(result.MergedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge));
            Assert.That(result.MergedInvariant.ConditionCount, Is.EqualTo(result.MergedPathFacts.MergedFacts.Count));
            Assert.That(result.MergedPathFacts.AlwaysFacts, Is.Empty);
            Assert.That(result.MergedPathFacts.MaybeFacts, Does.Contain("value > 0"));
            Assert.That(result.MergedPathFacts.ConservativeUnknowns, Is.EquivalentTo(new[] { "unknown(value)" }));
            Assert.That(result.ProgramPointSummary.ProgramPointCount, Is.EqualTo(result.ProgramPoints.Count));
            Assert.That(
                result.ProgramPointSummary.TotalPathConditionCount,
                Is.EqualTo(result.ProgramPoints.Sum(point => point.PathConditionCount)));
            Assert.That(
                result.ProgramPointSummary.MaxPathConditionCount,
                Is.EqualTo(result.ProgramPoints.Max(point => point.PathConditionCount)));
            Assert.That(
                result.ProgramPointSummary.ProofOutcomes.TotalCount,
                Is.EqualTo(result.ProgramPoints.Sum(point => point.ConditionProofs.Count)));
            Assert.That(returnPoint.Invariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.Conjunction));
            Assert.That(returnPoint.Invariant.MergedInvariantText, Is.EqualTo(returnPoint.MergedInvariantText));
            Assert.That(returnPoint.PathConditions.Select(condition => condition.Text), Is.EquivalentTo(new[] { "value > 0" }));
            Assert.That(returnPoint.PathConditionCount, Is.EqualTo(returnPoint.PathConditions.Count));
            Assert.That(returnPoint.ProofOutcomes.TotalCount, Is.EqualTo(returnPoint.ConditionProofs.Count));
            Assert.That(returnPoint.ProofOutcomes.ProvenTrueCount, Is.EqualTo(1));
            Assert.That(returnPoint.PathConditions.All(condition => condition.HasSmtFormula), Is.True);
            Assert.That(returnPoint.PathConditions.Single().Target, Is.EqualTo("value"));
            Assert.That(returnPoint.PathConditions.All(condition => !string.IsNullOrWhiteSpace(condition.FormulaKind)), Is.True);
        }

        [Test]
        public void QuerySyntaxTreeLine_WithExpressionProgramPoints_IncludesExpressionNodesOnLine()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            return value + 1;
        }

        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "LineExpressionQuery.cs");
            var compilation = CSharpCompilation.Create(
                "LineExpressionQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = new SymbolicSourceQueryService().QuerySyntaxTreeLine(
                syntaxTree,
                compilation,
                FindLine(source, "return value + 1;"),
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "value > 0" });

            Assert.That(defaultResult.ProgramPoints.Select(point => point.NodeKind), Does.Not.Contain("AddExpression"));

            var expressionResult = new SymbolicSourceQueryService().QuerySyntaxTreeLine(
                syntaxTree,
                compilation,
                FindLine(source, "return value + 1;"),
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "value > 0" },
                includeExpressionProgramPoints: true);

            Assert.That(expressionResult.ProgramPoints.Select(point => point.NodeKind), Does.Contain("ReturnStatement"));
            Assert.That(expressionResult.ProgramPoints.Single(point => point.NodeKind == "ReturnStatement").ProgramPointKind, Is.EqualTo(SymbolicProgramPointKinds.Statement));
            var addPoint = expressionResult.ProgramPoints.Single(point => point.NodeKind == "AddExpression");
            Assert.That(addPoint.ProgramPointKind, Is.EqualTo(SymbolicProgramPointKinds.Expression));
            Assert.That(addPoint.MergedInvariantText, Is.EqualTo("value > 0"));
            Assert.That(addPoint.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
            Assert.That(addPoint.NodeStartLine, Is.EqualTo(FindLine(source, "return value + 1;")));
        }

        [Test]
        public void QuerySyntaxTreeAtPosition_ReturnsFormattedInvariantAtAbsolutePosition()
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
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "PositionQuery.cs");
            var compilation = CSharpCompilation.Create(
                "PositionQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var position = FindPosition(source, "return value;");

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = new SymbolicSourceQueryService().QuerySyntaxTreeAtPosition(
                syntaxTree,
                compilation,
                position,
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "value > 0" });

            Assert.That(result.Position, Is.EqualTo(position));
            Assert.That(result.Line, Is.EqualTo(FindLine(source, "return value;")));
            Assert.That(result.Column, Is.EqualTo(FindColumn(source, "return value;")));
            Assert.That(result.NodeKind, Is.EqualTo("ReturnStatement"));
            Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Reachable));
            Assert.That(result.Facts.Any(fact => fact.Contains("GreaterThan", StringComparison.Ordinal) &&
                                                 fact.Contains("value", StringComparison.Ordinal)), Is.True);
            Assert.That(result.PathConditions.Select(condition => condition.Text), Is.EquivalentTo(new[] { "value > 0" }));
            Assert.That(result.MergedInvariantText, Is.EqualTo("value > 0"));
            Assert.That(result.Invariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.Conjunction));
            Assert.That(result.Invariant.Conditions.Single().Target, Is.EqualTo("value"));
            Assert.That(result.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void QuerySyntaxTreeLine_ConservativeMergeReportsUnknownForBranchFacts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0) { return 1; } else { return 2; }
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "BranchLineQuery.cs");
            var compilation = CSharpCompilation.Create(
                "BranchLineQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = new SymbolicSourceQueryService().QuerySyntaxTreeLine(
                syntaxTree,
                compilation,
                FindLine(source, "if (value > 0)"),
                smtAnalysis: smtAnalysis);

            Assert.That(result.ProgramPoints.Count(point => point.NodeKind == "ReturnStatement"), Is.EqualTo(2));
            var conditionTexts = result.ProgramPoints.SelectMany(point => point.PathConditions).Select(condition => condition.Text);
            Assert.That(conditionTexts, Does.Contain("value > 0"));
            Assert.That(conditionTexts, Does.Contain("!(value > 0)"));
            Assert.That(result.ObservedInvariant.MergedInvariantText, Does.Contain("GreaterThan"));
            Assert.That(result.ObservedInvariant.MergedInvariantText, Does.Contain("value"));
            Assert.That(result.MergedPathFacts.AlwaysFacts, Is.Empty);
            Assert.That(result.MergedPathFacts.MaybeFacts, Is.EquivalentTo(new[] { "value > 0", "!(value > 0)" }));
            Assert.That(result.MergedPathFacts.ConservativeUnknowns, Is.EquivalentTo(new[] { "unknown(value)" }));
            var diagnostic = result.MergedPathFacts.ConservativeUnknownDiagnostics.Single();
            Assert.That(diagnostic.UnknownText, Is.EqualTo("unknown(value)"));
            Assert.That(diagnostic.Target, Is.EqualTo("value"));
            Assert.That(diagnostic.Reason, Is.EqualTo("not_common_to_all_candidate_program_points"));
            Assert.That(diagnostic.MaybeFacts, Is.EquivalentTo(new[] { "value > 0", "!(value > 0)" }));
            Assert.That(diagnostic.CandidateProgramPointCount, Is.EqualTo(result.MergedPathFacts.CandidateProgramPointCount));
            Assert.That(result.MergedInvariantText, Is.EqualTo("unknown(value)"));
            Assert.That(result.MergedInvariant.Conditions.Single().IsConservativeUnknown, Is.True);
            Assert.That(result.MergedInvariant.Conditions.Single().Target, Is.EqualTo("value"));
        }

        [Test]
        public void QuerySyntaxTreeLine_InvariantQuerySummarizesMustMaybeUnknownFactsAndBudget()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0) { return value; } else { return -value; }
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "InvariantQueryLine.cs");
            var compilation = CSharpCompilation.Create(
                "InvariantQueryLine",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var smtAnalysis = new SmtAnalysisService(
                SmtAnalysisOptions.ForMode(SmtAnalysisMode.Bounded).WithOverrides(
                    TimeSpan.FromMilliseconds(321),
                    TimeSpan.FromMilliseconds(2345),
                    maxPathConditions: 17,
                    maxExpressionNodes: 99));

            var result = new SymbolicSourceQueryService().QuerySyntaxTreeLine(
                syntaxTree,
                compilation,
                FindLine(source, "if (value > 0)"),
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "value > 0" });

            Assert.That(result.InvariantQuery.Text, Is.EqualTo(result.MergedInvariantText));
            Assert.That(result.InvariantQuery.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge));
            Assert.That(result.InvariantQuery.MustFacts, Is.Empty);
            Assert.That(result.InvariantQuery.MaybeFacts, Is.EquivalentTo(new[] { "value > 0", "!(value > 0)" }));
            Assert.That(result.InvariantQuery.UnknownFacts, Is.EquivalentTo(new[] { "unknown(value)" }));
            Assert.That(result.InvariantQuery.HasMaybeFacts, Is.True);
            Assert.That(result.InvariantQuery.HasUnknowns, Is.True);
            Assert.That(result.InvariantQuery.HasUnresolvedAnalysis, Is.True);
            Assert.That(result.InvariantQuery.CandidateProgramPointCount, Is.EqualTo(result.MergedPathFacts.CandidateProgramPointCount));
            Assert.That(result.InvariantQuery.SmtDiagnostics.QueryTimeoutMs, Is.EqualTo(321));
            Assert.That(result.InvariantQuery.SmtDiagnostics.MethodBudgetMs, Is.EqualTo(2345));
            Assert.That(result.InvariantQuery.SmtDiagnostics.MaxPathConditions, Is.EqualTo(17));
            Assert.That(result.InvariantQuery.SmtDiagnostics.MaxExpressionNodes, Is.EqualTo(99));

            var positiveReturn = result.ProgramPoints
                .Where(static point => point.NodeKind == "ReturnStatement")
                .Single(point => point.MergedInvariantText == "value > 0");
            Assert.That(positiveReturn.InvariantQuery.MustFacts, Is.EquivalentTo(new[] { "value > 0" }));
            Assert.That(positiveReturn.InvariantQuery.MaybeFacts, Is.Empty);
            Assert.That(positiveReturn.InvariantQuery.UnknownFacts, Is.Empty);
            Assert.That(positiveReturn.InvariantQuery.HasUnresolvedAnalysis, Is.False);
            Assert.That(positiveReturn.InvariantQuery.ProofOutcomes.ProvenTrueCount, Is.EqualTo(1));
        }

        [Test]
        public void QuerySyntaxTreeSpan_ReturnsMergedInvariantQueryForSourceSpan()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var copy = value;
        if (copy > 0)
        {
            return copy;
        }

        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "InvariantSpanQuery.cs");
            var compilation = CSharpCompilation.Create(
                "InvariantSpanQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var spanStart = FindPosition(source, "if (copy > 0)");
            var spanEnd = FindPosition(source, "return 0;") + "return 0;".Length;

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = new SymbolicSourceQueryService().QuerySyntaxTreeSpan(
                syntaxTree,
                compilation,
                spanStart,
                spanEnd,
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "copy > 0" });

            Assert.That(result.SpanStart, Is.EqualTo(spanStart));
            Assert.That(result.SpanEnd, Is.EqualTo(spanEnd));
            Assert.That(result.StartLine, Is.EqualTo(FindLine(source, "if (copy > 0)")));
            Assert.That(result.EndLine, Is.EqualTo(FindLine(source, "return 0;")));
            Assert.That(result.ProgramPoints.Select(static point => point.NodeKind), Does.Contain("IfStatement"));
            Assert.That(result.ProgramPoints.Count(static point => point.NodeKind == "ReturnStatement"), Is.EqualTo(2));
            Assert.That(result.InvariantQuery.MaybeFacts, Does.Contain("copy > 0"));
            Assert.That(result.InvariantQuery.MaybeFacts, Does.Contain("!(copy > 0)"));
            Assert.That(result.InvariantQuery.UnknownFacts, Does.Contain("unknown(copy)"));
            Assert.That(result.InvariantQuery.CandidateProgramPointCount, Is.EqualTo(result.ProgramPoints.Count));

            var guardedReturn = result.ProgramPoints
                .Where(static point => point.NodeKind == "ReturnStatement")
                .Single(point => point.PathConditions.Any(static condition => condition.Text == "copy > 0"));
            Assert.That(guardedReturn.InvariantQuery.MustFacts, Does.Contain("copy > 0"));
            Assert.That(guardedReturn.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void QuerySyntaxTreeLine_ClassifiesImpossibleReturnAsUnreachable()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0 && value <= 0) { return value; }
        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "ImpossibleLineQuery.cs");
            var compilation = CSharpCompilation.Create(
                "ImpossibleLineQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = new SymbolicSourceQueryService().QuerySyntaxTreeLine(
                syntaxTree,
                compilation,
                FindLine(source, "value > 0 && value <= 0"),
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "value > 0" });

            var impossibleReturn = result.ProgramPoints.Single(point => point.NodeKind == "ReturnStatement");
            Assert.That(impossibleReturn.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
            Assert.That(impossibleReturn.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.Unreachable));
            Assert.That(result.ProgramPointSummary.Reachability.UnreachableCount, Is.GreaterThanOrEqualTo(1));

            var unreachableOnly = result.Filter(new SymbolicSourceQueryFilter(
                reachability: new[] { SymbolicReachability.Unreachable }));
            Assert.That(unreachableOnly.ProgramPoints, Is.Not.Empty);
            Assert.That(unreachableOnly.ProgramPoints.All(point => point.Reachability == SymbolicReachability.Unreachable), Is.True);
            Assert.That(unreachableOnly.MergedPathFacts.IsUnreachable, Is.True);
            Assert.That(unreachableOnly.MergedInvariantText, Is.EqualTo("false"));
            Assert.That(unreachableOnly.MergedInvariant.Conditions.Single().Text, Is.EqualTo("false"));
        }

        [Test]
        public void SymbolicLineQueryResult_Filter_RecomputesLineSummary()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0) { return value; }
        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "LineFilterQuery.cs");
            var compilation = CSharpCompilation.Create(
                "LineFilterQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var result = new SymbolicSourceQueryService().QuerySyntaxTreeLine(
                syntaxTree,
                compilation,
                FindLine(source, "if (value > 0)"));
            var filtered = result.Filter(new SymbolicSourceQueryFilter(nodeKinds: new[] { "ReturnStatement" }));

            Assert.That(filtered.ProgramPoints, Has.Count.EqualTo(1));
            Assert.That(filtered.ProgramPoints.Single().NodeKind, Is.EqualTo("ReturnStatement"));
            Assert.That(filtered.Facts, Is.EquivalentTo(filtered.ProgramPoints.Single().Facts));
            Assert.That(filtered.MergedInvariantText, Is.EqualTo(filtered.ProgramPoints.Single().MergedInvariantText));
            Assert.That(filtered.ObservedInvariant.Conditions.Select(condition => condition.Text), Is.EquivalentTo(filtered.Facts));
            Assert.That(filtered.ObservedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.DistinctFactUnion));
            Assert.That(filtered.MergedInvariant.Conditions.Select(condition => condition.Text), Is.EquivalentTo(filtered.MergedPathFacts.MergedFacts));
            Assert.That(filtered.MergedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge));
            Assert.That(filtered.MergedPathFacts.ConservativeUnknowns, Is.Empty);
            Assert.That(filtered.ProgramPointSummary.ProgramPointCount, Is.EqualTo(filtered.ProgramPoints.Count));
            Assert.That(filtered.ProgramPointSummary.TotalPathConditionCount, Is.EqualTo(filtered.ProgramPoints.Single().PathConditionCount));
            Assert.That(filtered.ProgramPointSummary.ProofOutcomes.TotalCount, Is.Zero);
        }

        [Test]
        public void QuerySourceLine_ReturnsEmptyProgramPointsForBlankLine()
        {
            const string source = @"
public class TestClass
{

    public int TestMethod(int value) => value;
}";

            var result = new SymbolicSourceQueryService().QuerySourceLine(
                source,
                "BlankLineQuery.cs",
                FindBlankLine(source),
                AnalyzerTestHost.GetTrustedPlatformReferences());

            Assert.That(result.ProgramPoints, Is.Empty);
            var summary = SymbolicInvariantService.MergeInvariantFacts(result.ProgramPoints.Select(point => point.Facts));
            Assert.That(summary.Facts, Is.Empty);
            Assert.That(summary.MergedInvariantText, Is.EqualTo("true"));
            Assert.That(result.Facts, Is.Empty);
            Assert.That(result.ObservedFactCount, Is.Zero);
            Assert.That(result.ObservedInvariant.IsTrivial, Is.True);
            Assert.That(result.MergedInvariantText, Is.EqualTo("true"));
            Assert.That(result.MergedInvariant.IsTrivial, Is.True);
            Assert.That(result.MergedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge));
            Assert.That(result.MergedInvariant.ConditionCount, Is.Zero);
            Assert.That(result.MergedPathFacts.ConservativeUnknowns, Is.Empty);
            Assert.That(result.ProgramPointSummary.ProgramPointCount, Is.Zero);
            Assert.That(result.ProgramPointSummary.TotalPathConditionCount, Is.Zero);
            Assert.That(result.ProgramPointSummary.MaxPathConditionCount, Is.Zero);
            Assert.That(result.ProgramPointSummary.ProofOutcomes.TotalCount, Is.Zero);
        }

        [Test]
        public void QuerySyntaxTreeAllLines_ReturnsFileLevelAggregateSummary()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0) { return value; }
        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "AllLinesQuery.cs");
            var compilation = CSharpCompilation.Create(
                "AllLinesQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = new SymbolicSourceQueryService().QuerySyntaxTreeAllLines(
                syntaxTree,
                compilation,
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "value > 0" });

            Assert.That(result.FilePath, Is.EqualTo("AllLinesQuery.cs"));
            Assert.That(result.LineCount, Is.EqualTo(syntaxTree.GetText().Lines.Count));
            Assert.That(result.LinesWithProgramPoints, Is.EqualTo(result.Lines.Count));
            Assert.That(result.ProgramPointCount, Is.EqualTo(result.Lines.Sum(line => line.ProgramPoints.Count)));
            Assert.That(result.ProgramPointCount, Is.GreaterThan(0));
            Assert.That(result.ObservedFacts, Is.EquivalentTo(result.Lines.SelectMany(line => line.ProgramPoints).SelectMany(point => point.Facts).Distinct()));
            Assert.That(result.ObservedFactCount, Is.EqualTo(result.ObservedFacts.Count));
            Assert.That(result.ObservedFacts.Any(fact => fact.Contains("value", StringComparison.Ordinal)), Is.True);
            Assert.That(result.ObservedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.DistinctFactUnion));
            Assert.That(result.ObservedInvariant.ConditionCount, Is.EqualTo(result.ObservedFactCount));
            Assert.That(result.ObservedInvariant.Conditions.Select(condition => condition.Text), Is.EquivalentTo(result.ObservedFacts));
            Assert.That(result.MergedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge));
            Assert.That(result.MergedInvariantText, Is.EqualTo(result.MergedPathFacts.MergedInvariantText));
            Assert.That(result.MergedPathFacts.MaybeFacts.Any(fact => fact.Contains("value", StringComparison.Ordinal)), Is.True);
            Assert.That(result.MergedPathFacts.ConservativeUnknowns, Does.Contain("unknown(value)"));
            Assert.That(result.ProgramPointSummary.ProgramPointCount, Is.EqualTo(result.ProgramPointCount));
            Assert.That(
                result.ProgramPointSummary.TotalPathConditionCount,
                Is.EqualTo(result.Lines.SelectMany(line => line.ProgramPoints).Sum(point => point.PathConditionCount)));
            Assert.That(
                result.ProgramPointSummary.MaxPathConditionCount,
                Is.EqualTo(result.Lines.SelectMany(line => line.ProgramPoints).Max(point => point.PathConditionCount)));
            Assert.That(result.Reachability.ReachableCount, Is.EqualTo(result.ProgramPointCount));
            Assert.That(result.ProgramPointSummary.Reachability.ReachableCount, Is.EqualTo(result.Reachability.ReachableCount));
            var proofSummary = result.ConditionProofs.Single(summary => summary.Condition == "value > 0");
            Assert.That(proofSummary.ProvenTrueCount, Is.GreaterThan(0));
            Assert.That(
                proofSummary.ProvenTrueCount + proofSummary.ProvenFalseCount + proofSummary.UnreachableCount + proofSummary.UnknownCount,
                Is.EqualTo(result.ProgramPointCount));
            Assert.That(result.ProgramPointSummary.ProofOutcomes.TotalCount, Is.EqualTo(result.ProgramPointCount));
            Assert.That(result.ProgramPointSummary.ProofOutcomes.ProvenTrueCount, Is.EqualTo(proofSummary.ProvenTrueCount));
        }

        [Test]
        public void SymbolicFileQueryResult_Filter_RecomputesAggregateSummary()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0) { return value; }
        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "AllLinesFilterQuery.cs");
            var compilation = CSharpCompilation.Create(
                "AllLinesFilterQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = new SymbolicSourceQueryService().QuerySyntaxTreeAllLines(
                syntaxTree,
                compilation,
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "value > 0" });
            var filtered = result.Filter(new SymbolicSourceQueryFilter(
                nodeKinds: new[] { "ReturnStatement" },
                requireFacts: true,
                reachability: new[] { SymbolicReachability.Reachable }));

            Assert.That(filtered.Lines, Is.Not.Empty);
            Assert.That(filtered.ProgramPointCount, Is.EqualTo(filtered.Lines.Sum(line => line.ProgramPoints.Count)));
            Assert.That(filtered.Lines.SelectMany(line => line.ProgramPoints).All(point => point.NodeKind == "ReturnStatement"), Is.True);
            Assert.That(filtered.Lines.SelectMany(line => line.ProgramPoints).All(point => point.Facts.Count != 0), Is.True);
            Assert.That(filtered.Reachability.ReachableCount, Is.EqualTo(filtered.ProgramPointCount));
            Assert.That(filtered.ObservedFacts, Is.EquivalentTo(filtered.Lines.SelectMany(line => line.ProgramPoints).SelectMany(point => point.Facts).Distinct()));
            Assert.That(filtered.ObservedInvariant.ConditionCount, Is.EqualTo(filtered.ObservedFactCount));
            Assert.That(filtered.ObservedInvariant.Conditions.Select(condition => condition.Text), Is.EquivalentTo(filtered.ObservedFacts));
            Assert.That(filtered.MergedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge));
            Assert.That(filtered.MergedInvariantText, Is.EqualTo(filtered.MergedPathFacts.MergedInvariantText));
            Assert.That(filtered.MergedPathFacts.ConservativeUnknowns, Does.Contain("unknown(value)"));
            Assert.That(filtered.ProgramPointSummary.ProgramPointCount, Is.EqualTo(filtered.ProgramPointCount));
            Assert.That(
                filtered.ProgramPointSummary.TotalPathConditionCount,
                Is.EqualTo(filtered.Lines.SelectMany(line => line.ProgramPoints).Sum(point => point.PathConditionCount)));
            Assert.That(filtered.ProgramPointSummary.Reachability.ReachableCount, Is.EqualTo(filtered.ProgramPointCount));
            Assert.That(filtered.ConditionProofs.Single(summary => summary.Condition == "value > 0").ProvenTrueCount, Is.GreaterThan(0));
        }

        [Test]
        public void SymbolicSourceQueryFilter_CanFilterByMethodAndConditionMetadata()
        {
            const string source = @"
public class TestClass
{
    public int First(int value)
    {
        if (value > 0) { return value; }
        return 0;
    }

    public int Second(int other)
    {
        if (other > 0) { return other; }
        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "MetadataFilterQuery.cs");
            var compilation = CSharpCompilation.Create(
                "MetadataFilterQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var result = new SymbolicSourceQueryService().QuerySyntaxTreeAllLines(syntaxTree, compilation);
            var filtered = result.Filter(new SymbolicSourceQueryFilter(
                methodNames: new[] { "First" },
                requirePathConditions: true,
                conditionTargets: new[] { "value" },
                conditionTexts: new[] { "value > 0" },
                conditionTextContains: new[] { "value" }));
            var points = filtered.Lines.SelectMany(static line => line.ProgramPoints).ToArray();

            Assert.That(points, Is.Not.Empty);
            Assert.That(points.All(static point => point.MethodName == "First"), Is.True);
            Assert.That(points.All(static point => point.PathConditions.Any(condition => condition.Target == "value")), Is.True);
            Assert.That(points.All(static point => point.PathConditions.Any(condition => condition.Text == "value > 0")), Is.True);
            Assert.That(points.All(static point => point.PathConditionCount > 0), Is.True);
            Assert.That(points.Select(static point => point.MethodName), Does.Not.Contain("Second"));

            var compact = filtered.ToCompactResult(new SymbolicCompactQueryOptions(maxProgramPoints: 10));
            var compactPoints = compact.Lines.SelectMany(static line => line.ProgramPoints).ToArray();
            Assert.That(compactPoints, Is.Not.Empty);
            Assert.That(compactPoints.All(static point => point.MethodName == "First"), Is.True);
            Assert.That(compactPoints.All(static point => point.ConservativeInvariant.Targets.Contains("value")), Is.True);
        }

        [Test]
        public void SymbolicSourceQueryFilter_CanFilterByLinePointKindMethodSubstringAndProofMetadata()
        {
            const string source = @"
public class TestClass
{
    public int FirstValue(int value)
    {
        if (value > 0)
        {
            return value + 1;
        }

        return 0;
    }

    public int SecondValue(int value)
    {
        if (value > 0)
        {
            return value + 2;
        }

        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "RicherFilterQuery.cs");
            var compilation = CSharpCompilation.Create(
                "RicherFilterQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var firstReturnLine = FindLine(source, "return value + 1;");

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = new SymbolicSourceQueryService().QuerySyntaxTreeAllLines(
                syntaxTree,
                compilation,
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "value > 0" },
                includeExpressionProgramPoints: true);
            var filtered = result.Filter(new SymbolicSourceQueryFilter(
                methodNameContains: new[] { "First" },
                lines: new[] { firstReturnLine },
                lineStart: firstReturnLine,
                lineEnd: firstReturnLine,
                programPointKinds: new[] { SymbolicProgramPointKinds.Expression },
                requireProofs: true,
                proofOutcomes: new[] { SymbolicTruthValue.ProvenTrue },
                proofConditions: new[] { "value > 0" },
                proofConditionContains: new[] { "value" }));
            var points = filtered.Lines.SelectMany(static line => line.ProgramPoints).ToArray();

            Assert.That(points, Has.Length.EqualTo(1));
            Assert.That(points[0].NodeKind, Is.EqualTo("AddExpression"));
            Assert.That(points[0].ProgramPointKind, Is.EqualTo(SymbolicProgramPointKinds.Expression));
            Assert.That(points[0].MethodName, Is.EqualTo("FirstValue"));
            Assert.That(points[0].Line, Is.EqualTo(firstReturnLine));
            Assert.That(points[0].ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
            Assert.That(filtered.ConditionProofs.Single().TotalCount, Is.EqualTo(1));

            var compactPoint = filtered.ToCompactResult(new SymbolicCompactQueryOptions(maxProgramPoints: 10))
                .Lines
                .SelectMany(static line => line.ProgramPoints)
                .Single();
            Assert.That(compactPoint.ProgramPointKind, Is.EqualTo(SymbolicProgramPointKinds.Expression));
            Assert.That(compactPoint.ProofOutcomes.ProvenTrueCount, Is.EqualTo(1));
        }

        [Test]
        public void SymbolicSourceQueryResult_ToCompactResult_AppliesPointBoundsAndJsonShape()
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
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "CompactPointQuery.cs");
            var compilation = CSharpCompilation.Create(
                "CompactPointQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var position = FindPosition(source, "return value;");

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = new SymbolicSourceQueryService().QuerySyntaxTreeAtPosition(
                syntaxTree,
                compilation,
                position,
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "value > 0" });
            var compact = result.ToCompactResult(new SymbolicCompactQueryOptions(
                maxProgramPoints: 0,
                maxFacts: 0,
                maxConditions: 0,
                maxProofs: 0));

            Assert.That(compact.Kind, Is.EqualTo("point"));
            Assert.That(compact.SchemaVersion, Is.EqualTo(1));
            Assert.That(compact.Line, Is.EqualTo(result.Line));
            Assert.That(compact.Column, Is.EqualTo(result.Column));
            Assert.That(compact.Position, Is.EqualTo(position));
            Assert.That(compact.NodeKind, Is.EqualTo("ReturnStatement"));
            Assert.That(compact.ProgramPointKind, Is.EqualTo(SymbolicProgramPointKinds.Statement));
            Assert.That(compact.NodeSpanStart, Is.EqualTo(result.NodeSpanStart));
            Assert.That(compact.NodeSpanEnd, Is.EqualTo(result.NodeSpanEnd));
            Assert.That(compact.NodeSpanLength, Is.EqualTo(result.NodeSpanLength));
            Assert.That(compact.NodeStartLine, Is.EqualTo(result.NodeStartLine));
            Assert.That(compact.NodeStartColumn, Is.EqualTo(result.NodeStartColumn));
            Assert.That(compact.NodeEndLine, Is.EqualTo(result.NodeEndLine));
            Assert.That(compact.NodeEndColumn, Is.EqualTo(result.NodeEndColumn));
            Assert.That(compact.MergedInvariantText, Is.EqualTo(result.MergedInvariantText));
            Assert.That(compact.PointReachability, Is.EqualTo(result.Reachability.ToString()));
            Assert.That(compact.ReachabilityReason, Is.EqualTo(result.ReachabilityReason));
            Assert.That(compact.ProofOutcomes.TotalCount, Is.EqualTo(result.ProofOutcomes.TotalCount));
            Assert.That(compact.ProgramPointCount, Is.EqualTo(1));
            Assert.That(compact.AnalysisSummary.ProgramPointCount, Is.EqualTo(1));
            Assert.That(compact.AnalysisSummary.InvariantConditionCount, Is.EqualTo(result.Invariant.ConditionCount));
            Assert.That(compact.AnalysisSummary.TotalPathConditionCount, Is.EqualTo(result.PathConditionCount));
            Assert.That(compact.AnalysisSummary.MaxPathConditionCount, Is.EqualTo(result.PathConditionCount));
            Assert.That(compact.AnalysisSummary.ReachabilityCheckedCount, Is.EqualTo(1));
            Assert.That(compact.AnalysisSummary.ReachabilityKnownCount, Is.EqualTo(1));
            Assert.That(compact.AnalysisSummary.ProofResolvedCount, Is.EqualTo(1));
            Assert.That(compact.AnalysisSummary.SmtEnabled, Is.True);
            Assert.That(compact.AnalysisSummary.HasUnresolvedAnalysis, Is.False);
            Assert.That(compact.ProgramPoints, Is.Empty);
            Assert.That(compact.Truncation.ProgramPoints, Is.True);
            Assert.That(compact.Truncation.Facts, Is.EqualTo(result.Facts.Count > 0));
            Assert.That(compact.Truncation.Conditions, Is.EqualTo(result.PathConditionCount > 0));
            Assert.That(compact.Truncation.Proofs, Is.EqualTo(result.ConditionProofs.Count > 0));
            Assert.That(compact.ObservedInvariant.RawFactCount, Is.EqualTo(result.Facts.Count));
            Assert.That(compact.ObservedInvariant.RawFacts, Is.Empty);
            Assert.That(compact.ConservativeInvariant.ConditionCount, Is.EqualTo(result.Invariant.ConditionCount));
            Assert.That(compact.ConservativeInvariant.ConservativeUnknownCount, Is.EqualTo(result.Invariant.ConservativeUnknownCount));
            Assert.That(compact.ConservativeInvariant.HasConservativeUnknowns, Is.False);
            Assert.That(compact.ConservativeInvariant.Conditions, Is.Empty);

            var json = JsonSerializer.Serialize(
                compact,
                new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() },
                });
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Assert.That(root.TryGetProperty("kind", out var kind), Is.True);
            Assert.That(kind.GetString(), Is.EqualTo("point"));
            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(root.TryGetProperty("Kind", out _), Is.False);
            Assert.That(root.TryGetProperty("lineCount", out _), Is.False);
            Assert.That(root.GetProperty("programPointKind").GetString(), Is.EqualTo(SymbolicProgramPointKinds.Statement));
            Assert.That(root.GetProperty("nodeSpanStart").GetInt32(), Is.EqualTo(result.NodeSpanStart));
            Assert.That(root.GetProperty("nodeSpanEnd").GetInt32(), Is.EqualTo(result.NodeSpanEnd));
            Assert.That(root.GetProperty("mergedInvariantText").GetString(), Is.EqualTo(result.MergedInvariantText));
            Assert.That(root.GetProperty("pointReachability").GetString(), Is.EqualTo(result.Reachability.ToString()));
            Assert.That(root.GetProperty("proofOutcomes").GetProperty("totalCount").GetInt32(), Is.EqualTo(result.ProofOutcomes.TotalCount));
            var analysisSummary = root.GetProperty("analysisSummary");
            Assert.That(analysisSummary.GetProperty("programPointCount").GetInt32(), Is.EqualTo(1));
            Assert.That(analysisSummary.GetProperty("invariantConditionCount").GetInt32(), Is.EqualTo(result.Invariant.ConditionCount));
            Assert.That(analysisSummary.GetProperty("reachabilityKnownCount").GetInt32(), Is.EqualTo(1));
            Assert.That(analysisSummary.GetProperty("proofResolvedCount").GetInt32(), Is.EqualTo(1));
            Assert.That(analysisSummary.GetProperty("smtEnabled").GetBoolean(), Is.True);
            Assert.That(analysisSummary.GetProperty("hasUnresolvedAnalysis").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("programPoints").GetArrayLength(), Is.Zero);
            Assert.That(root.GetProperty("truncation").GetProperty("isTruncated").GetBoolean(), Is.True);
        }

        [Test]
        public void SymbolicLineQueryResult_ToCompactResult_SeparatesObservedRawFactsFromConservativeMerge()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0) { return value; } else { return 0; }
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "CompactLineQuery.cs");
            var compilation = CSharpCompilation.Create(
                "CompactLineQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = new SymbolicSourceQueryService().QuerySyntaxTreeLine(
                syntaxTree,
                compilation,
                FindLine(source, "if (value > 0)"),
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "value > 0" });
            var compact = result.ToCompactResult(new SymbolicCompactQueryOptions(
                maxProgramPoints: 1,
                maxFacts: 1,
                maxConditions: 1,
                maxProofs: 1));

            Assert.That(compact.Kind, Is.EqualTo("line"));
            Assert.That(compact.ProgramPointCount, Is.EqualTo(result.ProgramPoints.Count));
            Assert.That(compact.ProgramPoints, Has.Count.EqualTo(1));
            Assert.That(compact.Truncation.ProgramPoints, Is.EqualTo(result.ProgramPoints.Count > 1));
            Assert.That(compact.MergedInvariantText, Is.EqualTo(result.MergedInvariantText));
            Assert.That(compact.ProofOutcomes.TotalCount, Is.EqualTo(result.ProgramPointSummary.ProofOutcomes.TotalCount));
            Assert.That(compact.ObservedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.DistinctFactUnion.ToString()));
            Assert.That(compact.ObservedInvariant.RawFactCount, Is.EqualTo(result.Facts.Count));
            Assert.That(compact.ObservedInvariant.RawFacts, Is.EqualTo(result.Facts.Take(1)));
            Assert.That(compact.ObservedInvariant.Text, Does.Contain("GreaterThan"));
            Assert.That(compact.ConservativeInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge.ToString()));
            Assert.That(compact.ConservativeInvariant.Text, Is.EqualTo(result.MergedInvariantText));
            Assert.That(compact.ConservativeInvariant.ConservativeUnknownCount, Is.EqualTo(result.MergedInvariant.ConservativeUnknownCount));
            Assert.That(compact.ConservativeInvariant.HasConservativeUnknowns, Is.True);
            Assert.That(compact.ConservativeInvariant.Targets, Does.Contain("value"));
            Assert.That(compact.ConservativeInvariant.MergedPathFacts, Is.Not.Null);
            Assert.That(compact.ConservativeInvariant.MergedPathFacts!.ConservativeUnknowns, Does.Contain("unknown(value)"));
            var diagnostic = compact.ConservativeInvariant.MergedPathFacts.ConservativeUnknownDiagnostics.Single();
            Assert.That(diagnostic.UnknownText, Is.EqualTo("unknown(value)"));
            Assert.That(diagnostic.Target, Is.EqualTo("value"));
            Assert.That(diagnostic.Reason, Is.EqualTo("not_common_to_all_candidate_program_points"));
            Assert.That(diagnostic.MaybeFacts, Is.Not.Empty);
            Assert.That(compact.Reachability.ReachableCount, Is.EqualTo(result.ProgramPointSummary.Reachability.ReachableCount));
            Assert.That(compact.SmtDiagnostics.IsConfigured, Is.True);
            Assert.That(compact.SmtDiagnostics.Mode, Is.EqualTo(SmtAnalysisMode.Bounded.ToString()));

            var compactPoint = compact.ProgramPoints.Single();
            var sourcePoint = result.ProgramPoints.First();
            Assert.That(compactPoint.FilePath, Is.EqualTo(sourcePoint.FilePath));
            Assert.That(compactPoint.Line, Is.EqualTo(sourcePoint.Line));
            Assert.That(compactPoint.Column, Is.EqualTo(sourcePoint.Column));
            Assert.That(compactPoint.Position, Is.EqualTo(sourcePoint.Position));
            Assert.That(compactPoint.NodeSpanStart, Is.EqualTo(sourcePoint.NodeSpanStart));
            Assert.That(compactPoint.NodeSpanEnd, Is.EqualTo(sourcePoint.NodeSpanEnd));
            Assert.That(compactPoint.NodeSpanLength, Is.EqualTo(sourcePoint.NodeSpanLength));
            Assert.That(compactPoint.NodeStartLine, Is.EqualTo(sourcePoint.NodeStartLine));
            Assert.That(compactPoint.NodeStartColumn, Is.EqualTo(sourcePoint.NodeStartColumn));
            Assert.That(compactPoint.NodeEndLine, Is.EqualTo(sourcePoint.NodeEndLine));
            Assert.That(compactPoint.NodeEndColumn, Is.EqualTo(sourcePoint.NodeEndColumn));
            Assert.That(compactPoint.MethodName, Is.EqualTo(sourcePoint.MethodName));
            Assert.That(compactPoint.ProgramPointKind, Is.EqualTo(sourcePoint.ProgramPointKind));
            Assert.That(compactPoint.MergedInvariantText, Is.EqualTo(sourcePoint.MergedInvariantText));
            Assert.That(compactPoint.Reachability, Is.EqualTo(sourcePoint.Reachability.ToString()));
            Assert.That(compactPoint.ReachabilityReason, Is.EqualTo(sourcePoint.ReachabilityReason));
            Assert.That(compactPoint.ProofOutcomes.TotalCount, Is.EqualTo(sourcePoint.ProofOutcomes.TotalCount));
        }

        [Test]
        public void SymbolicFileQueryResult_ToCompactResult_AppliesOutputBounds()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0) { return value; }
        if (value < 0) { return -value; }
        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "CompactFileQuery.cs");
            var compilation = CSharpCompilation.Create(
                "CompactFileQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = new SymbolicSourceQueryService().QuerySyntaxTreeAllLines(
                syntaxTree,
                compilation,
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "value > 0" });
            var compact = result.ToCompactResult(new SymbolicCompactQueryOptions(
                maxLines: 1,
                maxProgramPoints: 1,
                maxFacts: 0,
                maxConditions: 0,
                maxProofs: 0));

            Assert.That(compact.Kind, Is.EqualTo("file"));
            Assert.That(compact.LineCount, Is.EqualTo(result.LineCount));
            Assert.That(compact.Lines, Has.Count.EqualTo(1));
            Assert.That(compact.ProgramPointCount, Is.EqualTo(result.ProgramPointCount));
            Assert.That(compact.Truncation.Lines, Is.EqualTo(result.Lines.Count > 1));
            Assert.That(compact.Truncation.ProgramPoints, Is.EqualTo(result.ProgramPointCount > 1));
            Assert.That(compact.Truncation.Facts, Is.EqualTo(result.ObservedFactCount > 0));
            Assert.That(compact.Truncation.Conditions, Is.EqualTo(result.MergedInvariant.ConditionCount > 0));
            Assert.That(compact.Truncation.Proofs, Is.EqualTo(result.ConditionProofs.Count > 0));
            Assert.That(compact.MergedInvariantText, Is.EqualTo(result.MergedInvariantText));
            Assert.That(compact.ProofOutcomes.TotalCount, Is.EqualTo(result.ProgramPointSummary.ProofOutcomes.TotalCount));
            Assert.That(compact.ObservedInvariant.RawFactCount, Is.EqualTo(result.ObservedFactCount));
            Assert.That(compact.ObservedInvariant.RawFacts, Is.Empty);
            Assert.That(compact.ConservativeInvariant.Text, Is.EqualTo(result.MergedInvariantText));
            Assert.That(compact.ConservativeInvariant.MergedPathFacts, Is.Not.Null);
            Assert.That(compact.ConservativeInvariant.MergedPathFacts!.MaybeFactCount, Is.EqualTo(result.MergedPathFacts.MaybeFacts.Count));
            Assert.That(compact.ConservativeInvariant.MergedPathFacts.MaybeFacts, Is.Empty);
            Assert.That(compact.SmtDiagnostics.IsConfigured, Is.True);
        }

        [Test]
        public void SymbolicSpanQueryResult_ToCompactResult_ExposesInvariantQueryAndBudgetMetadata()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var copy = value;
        if (copy > 0)
        {
            return copy;
        }

        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "CompactSpanQuery.cs");
            var compilation = CSharpCompilation.Create(
                "CompactSpanQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var spanStart = FindPosition(source, "if (copy > 0)");
            var spanEnd = FindPosition(source, "return 0;") + "return 0;".Length;
            using var smtAnalysis = new SmtAnalysisService(
                SmtAnalysisOptions.ForMode(SmtAnalysisMode.Bounded).WithOverrides(
                    TimeSpan.FromMilliseconds(222),
                    TimeSpan.FromMilliseconds(2222),
                    maxPathConditions: 22,
                    maxExpressionNodes: 222));

            var result = new SymbolicSourceQueryService().QuerySyntaxTreeSpan(
                syntaxTree,
                compilation,
                spanStart,
                spanEnd,
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "copy > 0" });
            var compact = result.ToCompactResult(new SymbolicCompactQueryOptions(
                maxProgramPoints: 2,
                maxFacts: 1,
                maxConditions: 2,
                maxProofs: 1));

            Assert.That(compact.Kind, Is.EqualTo("span"));
            Assert.That(compact.QuerySpanStart, Is.EqualTo(spanStart));
            Assert.That(compact.QuerySpanEnd, Is.EqualTo(spanEnd));
            Assert.That(compact.QueryStartLine, Is.EqualTo(FindLine(source, "if (copy > 0)")));
            Assert.That(compact.QueryEndLine, Is.EqualTo(FindLine(source, "return 0;")));
            Assert.That(compact.InvariantQuery.Text, Is.EqualTo(result.InvariantQuery.Text));
            Assert.That(compact.InvariantQuery.MaybeFactCount, Is.EqualTo(result.InvariantQuery.MaybeFactCount));
            Assert.That(compact.InvariantQuery.MaybeFacts, Is.EquivalentTo(result.InvariantQuery.MaybeFacts.Take(2)));
            Assert.That(compact.InvariantQuery.UnknownFacts, Does.Contain("unknown(copy)"));
            Assert.That(compact.InvariantQuery.HasUnresolvedAnalysis, Is.True);
            Assert.That(compact.AnalysisSummary.MustFactCount, Is.EqualTo(result.InvariantQuery.MustFactCount));
            Assert.That(compact.AnalysisSummary.MaybeFactCount, Is.EqualTo(result.InvariantQuery.MaybeFactCount));
            Assert.That(compact.AnalysisSummary.UnknownFactCount, Is.EqualTo(result.InvariantQuery.UnknownFactCount));
            Assert.That(compact.AnalysisSummary.SmtQueryTimeoutMs, Is.EqualTo(222));
            Assert.That(compact.AnalysisSummary.SmtMethodBudgetMs, Is.EqualTo(2222));
            Assert.That(compact.AnalysisSummary.SmtMaxPathConditions, Is.EqualTo(22));
            Assert.That(compact.AnalysisSummary.SmtMaxExpressionNodes, Is.EqualTo(222));
            Assert.That(compact.SmtDiagnostics.QueryTimeoutMs, Is.EqualTo(222));
            Assert.That(compact.ProgramPoints, Has.Count.EqualTo(2));
        }

        [Test]
        public void SymbolicFileQueryResult_ToCompactResult_SummaryOnlyOmitsNestedResults()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0) { return value; }
        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "CompactSummaryOnlyQuery.cs");
            var compilation = CSharpCompilation.Create(
                "CompactSummaryOnlyQuery",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = new SymbolicSourceQueryService().QuerySyntaxTreeAllLines(
                syntaxTree,
                compilation,
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "value > 0" });
            var compact = result.ToCompactResult(SymbolicCompactQueryOptions.SummaryOnly);

            Assert.That(SymbolicCompactQueryOptions.SummaryOnly.MaxLines, Is.Zero);
            Assert.That(SymbolicCompactQueryOptions.SummaryOnly.MaxProgramPoints, Is.Zero);
            Assert.That(compact.Kind, Is.EqualTo("file"));
            Assert.That(compact.LineCount, Is.EqualTo(result.LineCount));
            Assert.That(compact.LinesWithProgramPoints, Is.EqualTo(result.LinesWithProgramPoints));
            Assert.That(compact.ProgramPointCount, Is.EqualTo(result.ProgramPointCount));
            Assert.That(compact.Lines, Is.Empty);
            Assert.That(compact.ProgramPoints, Is.Empty);
            Assert.That(compact.MergedInvariantText, Is.EqualTo(result.MergedInvariantText));
            Assert.That(compact.ConservativeInvariant.ConditionCount, Is.EqualTo(result.MergedInvariant.ConditionCount));
            Assert.That(compact.ProofOutcomes.TotalCount, Is.EqualTo(result.ProgramPointSummary.ProofOutcomes.TotalCount));
            Assert.That(compact.AnalysisSummary.ProgramPointCount, Is.EqualTo(result.ProgramPointSummary.ProgramPointCount));
            Assert.That(compact.AnalysisSummary.InvariantConditionCount, Is.EqualTo(result.MergedInvariant.ConditionCount));
            Assert.That(compact.AnalysisSummary.ConservativeUnknownCount, Is.EqualTo(result.MergedInvariant.ConservativeUnknownCount));
            Assert.That(compact.AnalysisSummary.TotalPathConditionCount, Is.EqualTo(result.ProgramPointSummary.TotalPathConditionCount));
            Assert.That(compact.AnalysisSummary.MaxPathConditionCount, Is.EqualTo(result.ProgramPointSummary.MaxPathConditionCount));
            Assert.That(
                compact.AnalysisSummary.ReachabilityCheckedCount,
                Is.EqualTo(
                    result.Reachability.ReachableCount +
                    result.Reachability.UnreachableCount +
                    result.Reachability.UnknownCount));
            Assert.That(
                compact.AnalysisSummary.ReachabilityKnownCount,
                Is.EqualTo(result.Reachability.ReachableCount + result.Reachability.UnreachableCount));
            Assert.That(compact.AnalysisSummary.ProofTotalCount, Is.EqualTo(result.ProgramPointSummary.ProofOutcomes.TotalCount));
            Assert.That(
                compact.AnalysisSummary.ProofResolvedCount,
                Is.EqualTo(
                    result.ProgramPointSummary.ProofOutcomes.ProvenTrueCount +
                    result.ProgramPointSummary.ProofOutcomes.ProvenFalseCount +
                    result.ProgramPointSummary.ProofOutcomes.UnreachableCount));
            Assert.That(compact.AnalysisSummary.SmtConfigured, Is.True);
            Assert.That(compact.Truncation.Lines, Is.EqualTo(result.Lines.Count > 0));
            Assert.That(compact.Truncation.ProgramPoints, Is.EqualTo(result.ProgramPointCount > 0));
        }

        [Test]
        public async Task SymbolicCli_CompactJson_EmitsPerPointMetadataWhenDetailsAreBounded()
        {
            var source = @"
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
}
";
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicCliCompactMetadata-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, source);
            try
            {
                var result = await RunSymbolicCliAsync(
                    "--file",
                    sourcePath,
                    "--line",
                    FindLine(source, "return value;").ToString(),
                    "--line-invariants",
                    "--check-reachability",
                    "--implies",
                    "value > 0",
                    "--compact-json",
                    "--max-points",
                    "1",
                    "--max-facts",
                    "0",
                    "--max-conditions",
                    "0",
                    "--max-proofs",
                    "0");

                Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
                using var document = JsonDocument.Parse(result.StandardOutput);
                var root = document.RootElement;
                Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("line"));
                Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
                Assert.That(root.GetProperty("mergedInvariantText").GetString(), Is.EqualTo("value > 0"));
                Assert.That(root.GetProperty("proofOutcomes").GetProperty("totalCount").GetInt32(), Is.EqualTo(1));

                var point = root.GetProperty("programPoints")[0];
                Assert.That(point.GetProperty("filePath").GetString(), Is.EqualTo(Path.GetFullPath(sourcePath)));
                Assert.That(point.GetProperty("line").GetInt32(), Is.EqualTo(FindLine(source, "return value;")));
                Assert.That(point.GetProperty("column").GetInt32(), Is.EqualTo(FindColumn(source, "return value;")));
                Assert.That(point.GetProperty("position").GetInt32(), Is.EqualTo(FindPosition(source, "return value;")));
                Assert.That(point.GetProperty("nodeSpanStart").GetInt32(), Is.EqualTo(FindPosition(source, "return value;")));
                Assert.That(point.GetProperty("nodeSpanEnd").GetInt32(), Is.GreaterThan(point.GetProperty("nodeSpanStart").GetInt32()));
                Assert.That(point.GetProperty("nodeSpanLength").GetInt32(), Is.GreaterThan(0));
                Assert.That(point.GetProperty("nodeStartLine").GetInt32(), Is.EqualTo(FindLine(source, "return value;")));
                Assert.That(point.GetProperty("nodeEndLine").GetInt32(), Is.EqualTo(FindLine(source, "return value;")));
                Assert.That(point.GetProperty("programPointKind").GetString(), Is.EqualTo(SymbolicProgramPointKinds.Statement));
                Assert.That(point.GetProperty("mergedInvariantText").GetString(), Is.EqualTo("value > 0"));
                Assert.That(point.GetProperty("reachability").GetString(), Is.EqualTo(SymbolicReachability.Reachable.ToString()));
                Assert.That(point.GetProperty("reachabilityReason").GetString(), Is.Not.Empty);
                Assert.That(point.GetProperty("conditionProofs").GetArrayLength(), Is.Zero);
                Assert.That(point.GetProperty("proofOutcomes").GetProperty("totalCount").GetInt32(), Is.EqualTo(1));
                Assert.That(point.GetProperty("proofOutcomes").GetProperty("provenTrueCount").GetInt32(), Is.EqualTo(1));
                Assert.That(point.GetProperty("conservativeInvariant").GetProperty("text").GetString(), Is.EqualTo("value > 0"));
                Assert.That(point.GetProperty("conservativeInvariant").GetProperty("conservativeUnknownCount").GetInt32(), Is.Zero);
                Assert.That(point.GetProperty("conservativeInvariant").GetProperty("conditions").GetArrayLength(), Is.Zero);
                Assert.That(point.GetProperty("truncation").GetProperty("conditions").GetBoolean(), Is.True);
                Assert.That(point.GetProperty("truncation").GetProperty("proofs").GetBoolean(), Is.True);
            }
            finally
            {
                File.Delete(sourcePath);
            }
        }

        [Test]
        public async Task SymbolicCli_SummaryOnly_EmitsAggregateCompactJsonWithoutNestedResults()
        {
            var source = @"
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
}
";
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicCliSummaryOnly-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, source);
            try
            {
                var result = await RunSymbolicCliAsync(
                    "--file",
                    sourcePath,
                    "--all-lines",
                    "--check-reachability",
                    "--implies",
                    "value > 0",
                    "--summary-only");

                Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
                using var document = JsonDocument.Parse(result.StandardOutput);
                var root = document.RootElement;
                Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("file"));
                Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
                Assert.That(root.GetProperty("lineCount").GetInt32(), Is.GreaterThan(0));
                Assert.That(root.GetProperty("linesWithProgramPoints").GetInt32(), Is.GreaterThan(0));
                Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.GreaterThan(0));
                Assert.That(root.GetProperty("mergedInvariantText").GetString(), Is.Not.Empty);
                Assert.That(root.GetProperty("proofOutcomes").GetProperty("totalCount").GetInt32(), Is.GreaterThan(0));
                var analysisSummary = root.GetProperty("analysisSummary");
                Assert.That(
                    analysisSummary.GetProperty("programPointCount").GetInt32(),
                    Is.EqualTo(root.GetProperty("programPointCount").GetInt32()));
                Assert.That(analysisSummary.GetProperty("reachabilityCheckedCount").GetInt32(), Is.GreaterThan(0));
                Assert.That(analysisSummary.GetProperty("reachabilityKnownCount").GetInt32(), Is.GreaterThan(0));
                Assert.That(analysisSummary.GetProperty("proofTotalCount").GetInt32(), Is.GreaterThan(0));
                Assert.That(analysisSummary.GetProperty("proofResolvedCount").GetInt32(), Is.GreaterThan(0));
                Assert.That(analysisSummary.GetProperty("smtConfigured").GetBoolean(), Is.True);
                Assert.That(analysisSummary.GetProperty("smtEnabled").GetBoolean(), Is.True);
                Assert.That(root.GetProperty("lines").GetArrayLength(), Is.Zero);
                Assert.That(root.GetProperty("programPoints").GetArrayLength(), Is.Zero);
                Assert.That(root.GetProperty("truncation").GetProperty("lines").GetBoolean(), Is.True);
                Assert.That(root.GetProperty("truncation").GetProperty("programPoints").GetBoolean(), Is.True);
            }
            finally
            {
                File.Delete(sourcePath);
            }
        }

        [Test]
        public async Task SymbolicCli_SpanCompactJson_EmitsInvariantQueryAndBudgetMetadata()
        {
            var source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var copy = value;
        if (copy > 0)
        {
            return copy;
        }

        return 0;
    }
}
";
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicCliSpanInvariantQuery-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, source);
            try
            {
                var spanStart = FindPosition(source, "if (copy > 0)");
                var spanEnd = FindPosition(source, "return 0;") + "return 0;".Length;
                var result = await RunSymbolicCliAsync(
                    "--file",
                    sourcePath,
                    "--span-start",
                    spanStart.ToString(),
                    "--span-end",
                    spanEnd.ToString(),
                    "--check-reachability",
                    "--implies",
                    "copy > 0",
                    "--smt-timeout-ms",
                    "333",
                    "--smt-method-budget-ms",
                    "2333",
                    "--smt-max-path-conditions",
                    "33",
                    "--smt-max-expression-nodes",
                    "333",
                    "--compact-json",
                    "--max-points",
                    "2",
                    "--max-conditions",
                    "3");

                Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
                using var document = JsonDocument.Parse(result.StandardOutput);
                var root = document.RootElement;
                Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("span"));
                Assert.That(root.GetProperty("querySpanStart").GetInt32(), Is.EqualTo(spanStart));
                Assert.That(root.GetProperty("querySpanEnd").GetInt32(), Is.EqualTo(spanEnd));
                Assert.That(root.GetProperty("queryStartLine").GetInt32(), Is.EqualTo(FindLine(source, "if (copy > 0)")));
                Assert.That(root.GetProperty("queryEndLine").GetInt32(), Is.EqualTo(FindLine(source, "return 0;")));
                Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.GreaterThanOrEqualTo(2));

                var invariantQuery = root.GetProperty("invariantQuery");
                Assert.That(invariantQuery.GetProperty("maybeFactCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
                Assert.That(
                    invariantQuery.GetProperty("maybeFacts").EnumerateArray().Select(static fact => fact.GetString()),
                    Does.Contain("copy > 0"));
                Assert.That(
                    invariantQuery.GetProperty("unknownFacts").EnumerateArray().Select(static fact => fact.GetString()),
                    Does.Contain("unknown(copy)"));
                Assert.That(invariantQuery.GetProperty("hasUnresolvedAnalysis").GetBoolean(), Is.True);

                var analysisSummary = root.GetProperty("analysisSummary");
                Assert.That(analysisSummary.GetProperty("maybeFactCount").GetInt32(), Is.EqualTo(invariantQuery.GetProperty("maybeFactCount").GetInt32()));
                Assert.That(analysisSummary.GetProperty("unknownFactCount").GetInt32(), Is.EqualTo(invariantQuery.GetProperty("unknownFactCount").GetInt32()));
                Assert.That(analysisSummary.GetProperty("smtQueryTimeoutMs").GetInt32(), Is.EqualTo(333));
                Assert.That(analysisSummary.GetProperty("smtMethodBudgetMs").GetInt32(), Is.EqualTo(2333));
                Assert.That(analysisSummary.GetProperty("smtMaxPathConditions").GetInt32(), Is.EqualTo(33));
                Assert.That(analysisSummary.GetProperty("smtMaxExpressionNodes").GetInt32(), Is.EqualTo(333));
            }
            finally
            {
                File.Delete(sourcePath);
            }
        }

        [Test]
        public async Task SymbolicCli_LineExpressions_AllowsFilteringToExpressionProgramPoint()
        {
            var source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            return value + 1;
        }

        return 0;
    }
}
";
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicCliLineExpressions-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, source);
            try
            {
                var result = await RunSymbolicCliAsync(
                    "--file",
                    sourcePath,
                    "--line",
                    FindLine(source, "return value + 1;").ToString(),
                    "--line-invariants",
                    "--line-expressions",
                    "--program-point-kind",
                    "Expression",
                    "--node-kind",
                    "AddExpression",
                    "--check-reachability",
                    "--implies",
                    "value > 0",
                    "--compact-json");

                Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
                using var document = JsonDocument.Parse(result.StandardOutput);
                var root = document.RootElement;
                Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("line"));
                Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.EqualTo(1));
                Assert.That(root.GetProperty("mergedInvariantText").GetString(), Is.EqualTo("value > 0"));
                Assert.That(root.GetProperty("proofOutcomes").GetProperty("provenTrueCount").GetInt32(), Is.EqualTo(1));

                var point = root.GetProperty("programPoints")[0];
                Assert.That(point.GetProperty("nodeKind").GetString(), Is.EqualTo("AddExpression"));
                Assert.That(point.GetProperty("programPointKind").GetString(), Is.EqualTo(SymbolicProgramPointKinds.Expression));
                Assert.That(point.GetProperty("mergedInvariantText").GetString(), Is.EqualTo("value > 0"));
                Assert.That(point.GetProperty("conditionProofs")[0].GetProperty("truthValue").GetString(), Is.EqualTo(SymbolicTruthValue.ProvenTrue.ToString()));
            }
            finally
            {
                File.Delete(sourcePath);
            }
        }

        [Test]
        public async Task SymbolicCli_RicherFilters_NarrowExpressionProofResults()
        {
            var source = @"
public class TestClass
{
    public int FirstValue(int value)
    {
        if (value > 0)
        {
            return value + 1;
        }

        return 0;
    }

    public int SecondValue(int value)
    {
        if (value > 0)
        {
            return value + 2;
        }

        return 0;
    }
}
";
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicCliRicherFilters-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, source);
            try
            {
                var firstReturnLine = FindLine(source, "return value + 1;");
                var result = await RunSymbolicCliAsync(
                    "--file",
                    sourcePath,
                    "--all-lines",
                    "--line-expressions",
                    "--method-contains",
                    "First",
                    "--filter-line",
                    firstReturnLine.ToString(),
                    "--line-start",
                    firstReturnLine.ToString(),
                    "--line-end",
                    firstReturnLine.ToString(),
                    "--program-point-kind",
                    "Expression",
                    "--with-proofs",
                    "--proof-outcome",
                    "ProvenTrue",
                    "--proof-condition",
                    "value > 0",
                    "--proof-condition-contains",
                    "value",
                    "--implies",
                    "value > 0",
                    "--compact-json");

                Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
                using var document = JsonDocument.Parse(result.StandardOutput);
                var root = document.RootElement;
                Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("file"));
                Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.EqualTo(1));
                Assert.That(root.GetProperty("proofOutcomes").GetProperty("provenTrueCount").GetInt32(), Is.EqualTo(1));
                Assert.That(root.GetProperty("conditionProofs")[0].GetProperty("totalCount").GetInt32(), Is.EqualTo(1));

                var point = root
                    .GetProperty("lines")[0]
                    .GetProperty("programPoints")[0];
                Assert.That(point.GetProperty("line").GetInt32(), Is.EqualTo(firstReturnLine));
                Assert.That(point.GetProperty("nodeKind").GetString(), Is.EqualTo("AddExpression"));
                Assert.That(point.GetProperty("programPointKind").GetString(), Is.EqualTo(SymbolicProgramPointKinds.Expression));
                Assert.That(point.GetProperty("methodName").GetString(), Is.EqualTo("FirstValue"));
                Assert.That(point.GetProperty("proofOutcomes").GetProperty("provenTrueCount").GetInt32(), Is.EqualTo(1));
            }
            finally
            {
                File.Delete(sourcePath);
            }
        }

        [Test]
        public async Task SymbolicCli_FilterMetadataSwitches_NarrowAllLinesCompactJson()
        {
            var source = @"
public class TestClass
{
    public int First(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 0;
    }

    public int Second(int other)
    {
        if (other > 0)
        {
            return other;
        }

        return 0;
    }
}
";
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicCliMetadataFilters-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, source);
            try
            {
                var result = await RunSymbolicCliAsync(
                    "--file",
                    sourcePath,
                    "--all-lines",
                    "--method",
                    "First",
                    "--with-conditions",
                    "--condition-target",
                    "value",
                    "--condition",
                    "value > 0",
                    "--condition-contains",
                    "value",
                    "--compact-json");

                Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
                using var document = JsonDocument.Parse(result.StandardOutput);
                var root = document.RootElement;
                Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("file"));
                var points = root
                    .GetProperty("lines")
                    .EnumerateArray()
                    .SelectMany(static line => line.GetProperty("programPoints").EnumerateArray())
                    .ToArray();
                Assert.That(points, Is.Not.Empty);
                Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.EqualTo(points.Length));
                foreach (var point in points)
                {
                    Assert.That(point.GetProperty("methodName").GetString(), Is.EqualTo("First"));
                    Assert.That(
                        point.GetProperty("conservativeInvariant").GetProperty("targets").EnumerateArray().Select(static target => target.GetString()),
                        Does.Contain("value"));
                }
            }
            finally
            {
                File.Delete(sourcePath);
            }
        }

        [Test]
        public async Task SymbolicCli_Json_EmitsEnumNames()
        {
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicCliFullJson-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, "public class C { public int M(int value) => value; }\n");
            try
            {
                var result = await RunSymbolicCliAsync(
                    "--file",
                    sourcePath,
                    "--position",
                    "0",
                    "--json");

                Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
                using var document = JsonDocument.Parse(result.StandardOutput);
                var root = document.RootElement;
                Assert.That(root.GetProperty("Reachability").ValueKind, Is.EqualTo(JsonValueKind.String));
                Assert.That(
                    Enum.TryParse<SymbolicReachability>(root.GetProperty("Reachability").GetString(), out _),
                    Is.True);
                Assert.That(root.GetProperty("Invariant").GetProperty("MergeKind").ValueKind, Is.EqualTo(JsonValueKind.String));
            }
            finally
            {
                File.Delete(sourcePath);
            }
        }

        [Test]
        public async Task SymbolicCli_RejectsInvalidCompactOptionCombinations()
        {
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicCliInvalidOptions-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, "public class C { public int M(int value) => value; }\n");
            try
            {
                var jsonAndCompact = await RunSymbolicCliAsync(
                    "--file",
                    sourcePath,
                    "--position",
                    "0",
                    "--json",
                    "--compact-json");
                Assert.That(jsonAndCompact.ExitCode, Is.EqualTo(64));
                Assert.That(jsonAndCompact.StandardError, Does.Contain("--json cannot be combined with --compact-json."));

                var maxLinesWithoutCompact = await RunSymbolicCliAsync(
                    "--file",
                    sourcePath,
                    "--position",
                    "0",
                    "--max-lines",
                    "1");
                Assert.That(maxLinesWithoutCompact.ExitCode, Is.EqualTo(64));
                Assert.That(maxLinesWithoutCompact.StandardError, Does.Contain("require --compact-json"));

                var negativeMaxPoints = await RunSymbolicCliAsync(
                    "--file",
                    sourcePath,
                    "--position",
                    "0",
                    "--compact-json",
                    "--max-points",
                    "-1");
                Assert.That(negativeMaxPoints.ExitCode, Is.EqualTo(64));
                Assert.That(negativeMaxPoints.StandardError, Does.Contain("non-negative integer"));

                var lineExpressionsWithoutLineMode = await RunSymbolicCliAsync(
                    "--file",
                    sourcePath,
                    "--position",
                    "0",
                    "--line-expressions");
                Assert.That(lineExpressionsWithoutLineMode.ExitCode, Is.EqualTo(64));
                Assert.That(lineExpressionsWithoutLineMode.StandardError, Does.Contain("--line-expressions requires --line-invariants, --span-start/--span-end, or --all-lines."));
            }
            finally
            {
                File.Delete(sourcePath);
            }
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

            throw new InvalidOperationException("Text not found: " + text);
        }

        private static int FindColumn(string source, string text)
        {
            var lines = source.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var column = lines[index].IndexOf(text, StringComparison.Ordinal);
                if (column >= 0)
                {
                    return column + 1;
                }
            }

            throw new InvalidOperationException("Text not found: " + text);
        }

        private static int FindPosition(string source, string text)
        {
            var position = source.IndexOf(text, StringComparison.Ordinal);
            if (position < 0)
            {
                throw new InvalidOperationException("Text not found: " + text);
            }

            return position;
        }

        private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunSymbolicCliAsync(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = FindRepositoryRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(Path.Combine("Tools", "PurelySharp.SymbolicCli", "PurelySharp.SymbolicCli.csproj"));
            startInfo.ArgumentList.Add("--");
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start symbolic CLI.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            return (process.ExitCode, await outputTask, await errorTask);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PurelySharp.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not find repository root.");
        }

        private static int FindBlankLine(string source)
        {
            var lines = source.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(lines[index]))
                {
                    return index + 1;
                }
            }

            throw new InvalidOperationException("Blank line not found.");
        }
    }
}
