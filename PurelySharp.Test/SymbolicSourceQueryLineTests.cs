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
