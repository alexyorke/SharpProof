using System;
using System.Collections.Generic;
using NUnit.Framework;
using PurelySharp.Symbolic.Smt;
using SearchLib.Purity;
using SearchLib.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public class SmtAnalysisServiceTests
    {
        [Test]
        public void Classify_OffMode_ReturnsConservativeUnknown()
        {
            var service = new SmtAnalysisService(new SmtAnalysisOptions(
                SmtAnalysisMode.Off,
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(500),
                maxPathConditions: 4,
                maxExpressionNodes: 16));

            var result = service.Classify(CreateQuery(Array.Empty<SmtFormula>(), new SmtBooleanConstant(true)));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo("smt_disabled"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(service.CacheEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void Classify_PathConditionBudgetExceeded_ReturnsConservativeUnknownWithoutSolver()
        {
            var x = new SmtVariable("x", SmtValueKind.Int);
            var service = new SmtAnalysisService(new SmtAnalysisOptions(
                SmtAnalysisMode.Bounded,
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(500),
                maxPathConditions: 1,
                maxExpressionNodes: 32));

            var result = service.Classify(CreateQuery(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(0)),
                    new SmtBinaryFormula(SmtBinaryOperator.LessThan, x, new SmtIntegerConstant(10)),
                },
                new SmtBooleanConstant(true)));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo("smt_path_condition_budget_exceeded"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(service.CacheEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void Classify_ExpressionNodeBudgetExceeded_ReturnsConservativeUnknownWithoutSolver()
        {
            var x = new SmtVariable("x", SmtValueKind.Int);
            var trigger = new SmtBinaryFormula(
                SmtBinaryOperator.And,
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, x, new SmtIntegerConstant(10)));
            var service = new SmtAnalysisService(new SmtAnalysisOptions(
                SmtAnalysisMode.Bounded,
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(500),
                maxPathConditions: 4,
                maxExpressionNodes: 3));

            var result = service.Classify(CreateQuery(Array.Empty<SmtFormula>(), trigger));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo("smt_expression_budget_exceeded"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(service.CacheEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void Classify_RepeatedEquivalentQuery_UsesCache()
        {
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
            var service = new SmtAnalysisService(new SmtAnalysisOptions(
                SmtAnalysisMode.Bounded,
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(1000),
                maxPathConditions: 4,
                maxExpressionNodes: 32));
            var query = CreateQuery(new[] { xIsZero }, xIsZero);

            var first = service.Classify(query);
            var second = service.Classify(query);

            Assert.That(first.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
            Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(1));
            Assert.That(service.CacheEntryCount, Is.EqualTo(1));
        }

        private static PurityProofQuery CreateQuery(
            IReadOnlyList<SmtFormula> pathConditions,
            SmtFormula triggerCondition)
        {
            return new PurityProofQuery(
                pathConditions,
                new PurityHazard(
                    PurityHazardKind.ImpureCallReachability,
                    triggerCondition,
                    PurityEffectVisibility.CallerVisible));
        }
    }
}
