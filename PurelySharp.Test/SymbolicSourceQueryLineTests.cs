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
            Assert.That(result.Reachability.ReachableCount, Is.EqualTo(result.ProgramPointCount));
            var proofSummary = result.ConditionProofs.Single(summary => summary.Condition == "value > 0");
            Assert.That(proofSummary.ProvenTrueCount, Is.GreaterThan(0));
            Assert.That(
                proofSummary.ProvenTrueCount + proofSummary.ProvenFalseCount + proofSummary.UnreachableCount + proofSummary.UnknownCount,
                Is.EqualTo(result.ProgramPointCount));
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
