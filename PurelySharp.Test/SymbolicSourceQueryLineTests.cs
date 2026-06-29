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
            Assert.That(returnPoint.MergedInvariantText, Does.Contain("value"));
            var summary = SymbolicInvariantService.MergeInvariantFacts(result.ProgramPoints.Select(point => point.Facts));
            Assert.That(summary.Facts, Is.EquivalentTo(result.ProgramPoints.SelectMany(point => point.Facts).Distinct()));
            Assert.That(summary.MergedInvariantText, Does.Contain("value"));
            Assert.That(result.Facts, Is.EquivalentTo(summary.Facts));
            Assert.That(result.MergedInvariantText, Is.EqualTo(summary.MergedInvariantText));
            Assert.That(result.MergedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.DistinctFactUnion));
            Assert.That(result.MergedInvariant.ConditionCount, Is.EqualTo(result.Facts.Count));
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
            Assert.That(returnPoint.PathConditions.Select(condition => condition.Text), Is.EquivalentTo(returnPoint.Facts));
            Assert.That(returnPoint.PathConditionCount, Is.EqualTo(returnPoint.PathConditions.Count));
            Assert.That(returnPoint.ProofOutcomes.TotalCount, Is.EqualTo(returnPoint.ConditionProofs.Count));
            Assert.That(returnPoint.ProofOutcomes.ProvenTrueCount, Is.EqualTo(1));
            Assert.That(returnPoint.PathConditions.All(condition => condition.HasSmtFormula), Is.True);
            Assert.That(returnPoint.PathConditions.All(condition => !string.IsNullOrWhiteSpace(condition.FormulaKind)), Is.True);
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
            Assert.That(filtered.MergedInvariant.Conditions.Select(condition => condition.Text), Is.EquivalentTo(filtered.Facts));
            Assert.That(filtered.MergedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.DistinctFactUnion));
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
            Assert.That(result.MergedInvariantText, Is.EqualTo("true"));
            Assert.That(result.MergedInvariant.IsTrivial, Is.True);
            Assert.That(result.MergedInvariant.ConditionCount, Is.Zero);
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
            Assert.That(filtered.ProgramPointSummary.ProgramPointCount, Is.EqualTo(filtered.ProgramPointCount));
            Assert.That(
                filtered.ProgramPointSummary.TotalPathConditionCount,
                Is.EqualTo(filtered.Lines.SelectMany(line => line.ProgramPoints).Sum(point => point.PathConditionCount)));
            Assert.That(filtered.ProgramPointSummary.Reachability.ReachableCount, Is.EqualTo(filtered.ProgramPointCount));
            Assert.That(filtered.ConditionProofs.Single(summary => summary.Condition == "value > 0").ProvenTrueCount, Is.GreaterThan(0));
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
