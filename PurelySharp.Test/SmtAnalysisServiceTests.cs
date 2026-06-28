using System;
using System.Collections.Generic;
using System.Threading;
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
        public void ForMode_Deep_ReturnsExpandedBudgetPreset()
        {
            var options = SmtAnalysisOptions.ForMode(SmtAnalysisMode.Deep);

            Assert.That(options.Mode, Is.EqualTo(SmtAnalysisMode.Deep));
            Assert.That(options.QueryTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(2000)));
            Assert.That(options.MethodBudget, Is.EqualTo(TimeSpan.FromMilliseconds(15000)));
            Assert.That(options.MaxPathConditions, Is.EqualTo(512));
            Assert.That(options.MaxExpressionNodes, Is.EqualTo(8192));
        }

        [Test]
        public void WithOverrides_PreservesModeAndAppliesExplicitBudgets()
        {
            var options = SmtAnalysisOptions.ForMode(SmtAnalysisMode.Deep).WithOverrides(
                queryTimeout: TimeSpan.FromMilliseconds(123),
                methodBudget: TimeSpan.FromMilliseconds(456),
                maxPathConditions: 7,
                maxExpressionNodes: 89);

            Assert.That(options.Mode, Is.EqualTo(SmtAnalysisMode.Deep));
            Assert.That(options.QueryTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(123)));
            Assert.That(options.MethodBudget, Is.EqualTo(TimeSpan.FromMilliseconds(456)));
            Assert.That(options.MaxPathConditions, Is.EqualTo(7));
            Assert.That(options.MaxExpressionNodes, Is.EqualTo(89));
        }

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
        public void Classify_MethodBudgetDoesNotExpireBeforeFirstSolverQueryByWallClock()
        {
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
            var service = new SmtAnalysisService(new SmtAnalysisOptions(
                SmtAnalysisMode.Bounded,
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(1),
                maxPathConditions: 4,
                maxExpressionNodes: 32));

            Thread.Sleep(20);

            var result = service.Classify(CreateQuery(new[] { xIsZero }, xIsZero));

            Assert.That(result.Reason, Is.Not.EqualTo("smt_method_budget_exceeded"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(1));
        }

        [Test]
        public void Classify_MethodBudgetExceededAfterSolverTime_ReturnsConservativeUnknownWithoutSolver()
        {
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
            var xIsPositive = new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(0));
            var service = new SmtAnalysisService(new SmtAnalysisOptions(
                SmtAnalysisMode.Bounded,
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromTicks(1),
                maxPathConditions: 4,
                maxExpressionNodes: 32));

            _ = service.Classify(CreateQuery(new[] { xIsZero }, xIsZero));
            var result = service.Classify(CreateQuery(new[] { xIsPositive }, xIsPositive));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo("smt_method_budget_exceeded"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(1));
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

        [Test]
        public void ClassifyImplication_ProvesFactFromPathConditions()
        {
            var x = new SmtVariable("x", SmtValueKind.Int);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0)),
            };
            var fact = new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, x, new SmtIntegerConstant(1));

            var result = service.ClassifyImplication(pathConditions, fact);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Satisfiable));
            Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(service.PathConditionsImply(pathConditions, fact), Is.True);
        }

        [Test]
        public void ClassifyImplication_ReturnsReachableWhenFactDoesNotFollow()
        {
            var x = new SmtVariable("x", SmtValueKind.Int);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(0)),
            };
            var fact = new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(0));

            var result = service.ClassifyImplication(pathConditions, fact);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Satisfiable));
            Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Satisfiable));
            Assert.That(service.PathConditionsImply(pathConditions, fact), Is.False);
        }

        [Test]
        public void ClassifyPathFeasibility_ReportsContradictoryPathUnsatisfiable()
        {
            var x = new SmtVariable("x", SmtValueKind.Int);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, x, new SmtIntegerConstant(0)),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
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
