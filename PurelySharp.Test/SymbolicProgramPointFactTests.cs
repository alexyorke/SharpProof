using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class SymbolicProgramPointFactTests
    {
        [Test]
        public void ProgramPointFacts_ReplayNestedElseIfGuardFactsAfterOuterExit()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value < 0)
        {
            throw new System.InvalidOperationException();
        }
        else if (value == 0)
        {
            return 0;
        }

        return 10 / value;
    }
}";

            var marker = FindMarker(source, "return 10 / value;");
            var proof = ProveAtMarker(source, marker, "value > 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_ReplaySurvivingElseAssignmentAfterTrueBranchExit()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool useFallback, int input)
    {
        var divisor = 0;
        if (useFallback)
        {
            return 0;
        }
        else
        {
            divisor = input < 0 ? 1 : 2;
        }

        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor != 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_ReplaySurvivingTrueAssignmentAfterFalseBranchExit()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool usePrimary, int input)
    {
        var divisor = 0;
        if (usePrimary)
        {
            divisor = input == 0 ? 3 : input;
        }
        else
        {
            throw new System.InvalidOperationException();
        }

        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor != 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_FilterBranchLocalSymbolsWhenReplayingSingleSurvivingBranch()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool stop)
    {
        var divisor = 0;
        if (stop)
        {
            return 0;
        }
        else
        {
            var hidden = 5;
            divisor = hidden;
        }

        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var snapshot = GetSnapshotAtStatement(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor == 5");

            Assert.That(snapshot.Facts.Any(fact => fact.Contains("hidden#", StringComparison.Ordinal)), Is.False);
            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        private static SymbolicInvariantSnapshot GetSnapshotAtStatement(string source, string statementPrefix)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "SymbolicProgramPointFactTests.cs");
            var compilation = CSharpCompilation.Create(
                "SymbolicProgramPointFactTests",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var statement = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<StatementSyntax>()
                .Single(node => node.ToString().StartsWith(statementPrefix, StringComparison.Ordinal));

            return new SymbolicInvariantService().GetInvariantsAt(statement, semanticModel);
        }

        private static SymbolicConditionProofResult ProveAtMarker(
            string source,
            (int Line, int Column, int Position) marker,
            string condition)
        {
            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            return new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "SymbolicProgramPointFactTests.cs",
                marker.Line,
                marker.Column,
                condition,
                smtAnalysis,
                AnalyzerTestHost.GetTrustedPlatformReferences());
        }

        private static (int Line, int Column, int Position) FindMarker(string source, string marker)
        {
            var position = source.IndexOf(marker, StringComparison.Ordinal);
            if (position < 0)
            {
                throw new InvalidOperationException("Marker was not found in source.");
            }

            var lines = source.Split('\n');
            var currentPosition = 0;
            for (var index = 0; index < lines.Length; index++)
            {
                var nextPosition = currentPosition + lines[index].Length + 1;
                if (position < nextPosition)
                {
                    return (index + 1, position - currentPosition + 1, position);
                }

                currentPosition = nextPosition;
            }

            throw new InvalidOperationException("Marker line was not found in source.");
        }
    }
}
