using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using PurelySharp.Symbolic.Ir;
using PurelySharp.Symbolic.Smt;
using SearchLib.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class SymbolicIrTests
    {
        [Test]
        public void LowerCondition_EncodesIntegerRangeWithSameFormulaAsLegacyTranslator()
        {
            var context = CreateExpressionContext(
                "int x",
                "x > 0 && x < 10");

            Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition), Is.True);
            Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var irFormula), Is.True);
            Assert.That(CSharpConditionToFormula.TryTranslate(context.Expression, context.SemanticModel, CancellationToken.None, out var legacyFormula), Is.True);

            Assert.That(irFormula, Is.EqualTo(legacyFormula));
        }

        [Test]
        public void LowerCondition_RepresentsStringLengthAsSharedLengthAtom()
        {
            var context = CreateExpressionContext(
                "string s, int n",
                "s.Length == n");

            Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition), Is.True);
            var fact = AssertFactCondition<SymbolicRelationAtom>(condition);

            Assert.That(fact.Operator, Is.EqualTo(SymbolicRelationOperator.Equal));
            Assert.That(fact.Left, Is.TypeOf<SymbolicLengthTerm>());
            Assert.That(((SymbolicLengthTerm)fact.Left).Value, Is.TypeOf<SymbolicStringContentTerm>());
            Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
            Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
        }

        [Test]
        public void KnownApiLowering_StringStartsWithEmitsDeclarativeStringPredicate()
        {
            var context = CreateExpressionContext(
                "string s",
                """s.StartsWith("A")""");

            Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition), Is.True);
            var fact = AssertFactCondition<SymbolicStringPredicateAtom>(condition);

            Assert.That(fact.Predicate, Is.EqualTo(SymbolicStringPredicateKind.StartsWith));
            Assert.That(fact.Value, Is.TypeOf<SymbolicStringContentTerm>());
            Assert.That(fact.Argument, Is.EqualTo(new SymbolicStringConstantTerm("A")));
            Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
            Assert.That(formula, Is.TypeOf<SmtStringStartsWithFormula>());
        }

        [Test]
        public void KnownApiLowering_StringComparisonOverloadFallsBackToLegacyTranslator()
        {
            var context = CreateExpressionContext(
                "string s",
                """s.StartsWith("A", System.StringComparison.OrdinalIgnoreCase)""");

            Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out _), Is.False);
            Assert.That(CSharpConditionToFormula.TryTranslate(context.Expression, context.SemanticModel, CancellationToken.None, out var legacyFormula), Is.True);
            Assert.That(legacyFormula, Is.Not.Null);
        }

        [Test]
        public void LowerCondition_UnguardedDivisionStaysOnLegacyConservativePath()
        {
            var context = CreateExpressionContext(
                "int value, int divisor",
                "value / divisor == 2");

            Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out _), Is.False);
        }

        [Test]
        public void Encoder_ExceptionPreconditionUsesTriggerFormulaWithoutSpecialAnalyzerRule()
        {
            var divisor = new SymbolicVariableTerm("d#1", SmtValueKind.Int);
            var trigger = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    divisor,
                    new SymbolicIntegerConstantTerm(0)),
                SyntaxFactory.ParseExpression("d == 0"),
                "test.divide-by-zero"));
            var condition = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicExceptionPreconditionAtom(
                    SymbolicExceptionPreconditionKind.DivideByZero,
                    divisor,
                    trigger),
                SyntaxFactory.ParseExpression("1 / d"),
                "test.exception-precondition"));

            Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
            Assert.That(formula, Is.EqualTo(new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtVariable("d#1", SmtValueKind.Int),
                new SmtIntegerConstant(0))));
        }

        private static TAtom AssertFactCondition<TAtom>(SymbolicCondition condition)
            where TAtom : SymbolicAtom
        {
            Assert.That(condition, Is.TypeOf<SymbolicFactCondition>());
            var factCondition = (SymbolicFactCondition)condition;
            Assert.That(factCondition.Fact.Confidence, Is.EqualTo(SymbolicFactConfidence.Exact));
            Assert.That(factCondition.Fact.Atom, Is.TypeOf<TAtom>());
            return (TAtom)factCondition.Fact.Atom;
        }

        private static ExpressionContext CreateExpressionContext(string parameters, string expression)
        {
            var source = $$"""
                public sealed class C
                {
                    public bool M({{parameters}})
                    {
                        return {{expression}};
                    }
                }
                """;
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "SymbolicIrTest",
                new[] { syntaxTree },
                AnalyzerTestHost.GetMinimalFrameworkReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var returnStatement = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Single();

            return new ExpressionContext(
                returnStatement.Expression!,
                semanticModel,
                new SymbolicLoweringContext(semanticModel, CancellationToken.None));
        }

        private sealed record ExpressionContext(
            ExpressionSyntax Expression,
            SemanticModel SemanticModel,
            SymbolicLoweringContext LoweringContext);
    }
}
