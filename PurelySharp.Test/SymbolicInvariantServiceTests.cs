using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Ir;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
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
                smtAnalysis: null);

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
            Assert.That(proof.Reason, Is.EqualTo("smt_required"));
            Assert.That(proof.SmtDiagnostics.IsConfigured, Is.False);
        }

        private static (ReturnStatementSyntax ReturnStatement, SemanticModel SemanticModel, SymbolicCondition GuardCondition)
            CreateGuardedReturnContext(string source, string returnMarker)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                path: "SymbolicInvariantServiceProof.cs");
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
            Assert.That(SymbolicIrLowerer.TryLowerCondition(ifStatement.Condition, loweringContext, out var guardCondition), Is.True);
            Assert.That(guardCondition, Is.Not.Null);

            return (returnStatement, semanticModel, guardCondition!);
        }
    }
}
