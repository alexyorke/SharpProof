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
        public void Classify_DuplicateAndTruePathConditions_AreNormalizedBeforeBudgetAndCache()
        {
            var x = new SmtVariable("normalized_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
            var y = new SmtVariable("normalized_y_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
            var xAtLeastZero = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                x,
                new SmtIntegerConstant(0));
            var yAtLeastZero = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                y,
                new SmtIntegerConstant(0));
            var fact = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, y),
                new SmtIntegerConstant(0));
            var service = new SmtAnalysisService(new SmtAnalysisOptions(
                SmtAnalysisMode.Bounded,
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(1000),
                maxPathConditions: 2,
                maxExpressionNodes: 64));

            var first = service.ClassifyImplication(
                new SmtFormula[] { xAtLeastZero, yAtLeastZero, new SmtBooleanConstant(true), xAtLeastZero },
                fact);
            var second = service.ClassifyImplication(new[] { yAtLeastZero, xAtLeastZero }, fact);

            Assert.That(first.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(1));
            Assert.That(service.CacheEntryCount, Is.EqualTo(1));
        }

        [Test]
        public void Classify_EquivalentPathConditionOrder_UsesSameCacheEntry()
        {
            var x = new SmtVariable("ordered_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
            var y = new SmtVariable("ordered_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
            var xAtLeastZero = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                x,
                new SmtIntegerConstant(0));
            var yAtLeastZero = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                y,
                new SmtIntegerConstant(0));
            var fact = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, y),
                new SmtIntegerConstant(0));
            var service = new SmtAnalysisService(new SmtAnalysisOptions(
                SmtAnalysisMode.Bounded,
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(1000),
                maxPathConditions: 4,
                maxExpressionNodes: 64));

            var first = service.ClassifyImplication(new[] { xAtLeastZero, yAtLeastZero }, fact);
            var second = service.ClassifyImplication(new[] { yAtLeastZero, xAtLeastZero }, fact);

            Assert.That(first.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(1));
            Assert.That(service.CacheEntryCount, Is.EqualTo(1));
        }

        [Test]
        public void Classify_SyntacticPathContradiction_BypassesBudgetsAndSolver()
        {
            var x = new SmtVariable("x", SmtValueKind.Int);
            var service = new SmtAnalysisService(new SmtAnalysisOptions(
                SmtAnalysisMode.Bounded,
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(500),
                maxPathConditions: 1,
                maxExpressionNodes: 3));

            var result = service.Classify(CreateQuery(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0)),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, x, new SmtIntegerConstant(0)),
                },
                new SmtBooleanConstant(true)));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(service.CacheEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void Classify_SyntacticIntegerIntervalContradiction_BypassesBudgetsAndSolver()
        {
            var x = new SmtVariable("interval_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
            var service = new SmtAnalysisService(new SmtAnalysisOptions(
                SmtAnalysisMode.Bounded,
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(500),
                maxPathConditions: 1,
                maxExpressionNodes: 3));

            var result = service.Classify(CreateQuery(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(10)),
                    new SmtBinaryFormula(SmtBinaryOperator.LessThan, x, new SmtIntegerConstant(10)),
                },
                new SmtBooleanConstant(true)));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(service.CacheEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void Classify_NestedConjunctIntegerContradiction_BypassesSolver()
        {
            var x = new SmtVariable("nested_interval_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
            var nestedBounds = new SmtBinaryFormula(
                SmtBinaryOperator.And,
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(3)),
                new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, x, new SmtIntegerConstant(3)));
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

            var result = service.Classify(CreateQuery(
                new[] { nestedBounds },
                new SmtBooleanConstant(true)));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(service.CacheEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void ClassifyImplication_DirectPathFact_BypassesSolver()
        {
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

            var result = service.ClassifyImplication(new[] { xIsZero }, xIsZero);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unknown));
            Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
            Assert.That(service.PathConditionsImply(new[] { xIsZero }, xIsZero), Is.True);
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(service.CacheEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void ClassifyImplication_IntegerIntervalEntailment_BypassesSolver()
        {
            var x = new SmtVariable("interval_entailment_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
            var xAtMostNine = new SmtBinaryFormula(
                SmtBinaryOperator.LessThanOrEqual,
                x,
                new SmtIntegerConstant(9));
            var xLessThanTen = new SmtBinaryFormula(
                SmtBinaryOperator.LessThan,
                x,
                new SmtIntegerConstant(10));
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

            var result = service.ClassifyImplication(new[] { xAtMostNine }, xLessThanTen);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unknown));
            Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(service.CacheEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void ClassifyImplication_ExactStringLengthEntailment_BypassesSolver()
        {
            var text = new SmtVariable("exact_string_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
            var textIsAbc = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                text,
                new SmtStringConstant("ABC"));
            var lengthAtLeastThree = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                new SmtStringLengthTerm(text),
                new SmtIntegerConstant(3));
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

            var result = service.ClassifyImplication(new[] { textIsAbc }, lengthAtLeastThree);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(service.CacheEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void ClassifyImplication_ExactStringPredicateEntailment_BypassesSolver()
        {
            var text = new SmtVariable("exact_predicate_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
            var textIsAbc = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                text,
                new SmtStringConstant("ABC"));
            var startsWithA = new SmtStringStartsWithFormula(text, new SmtStringConstant("A"));
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

            var result = service.ClassifyImplication(new[] { textIsAbc }, startsWithA);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(service.CacheEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void ClassifyImplication_ExactStringNegatedPredicateEntailment_BypassesSolver()
        {
            var text = new SmtVariable("exact_negated_predicate_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
            var textIsAbc = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                text,
                new SmtStringConstant("ABC"));
            var doesNotEndWithZ = new SmtUnaryFormula(
                SmtUnaryOperator.Not,
                new SmtStringEndsWithFormula(text, new SmtStringConstant("Z")));
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

            var result = service.ClassifyImplication(new[] { textIsAbc }, doesNotEndWithZ);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(service.CacheEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void ClassifyPathFeasibility_ContradictoryExactStringValues_BypassesSolver()
        {
            var text = new SmtVariable("exact_contradiction_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
            var service = new SmtAnalysisService(new SmtAnalysisOptions(
                SmtAnalysisMode.Bounded,
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(500),
                maxPathConditions: 1,
                maxExpressionNodes: 3));

            var result = service.ClassifyPathFeasibility(new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ABC")),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("XYZ")),
            });

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(service.CacheEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void Classify_DivideByZeroContradictedByGuard_BypassesSolver()
        {
            var divisor = new SmtVariable("divisor", SmtValueKind.Int);
            var divisorIsNotZero = new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                divisor,
                new SmtIntegerConstant(0));
            var divisorIsZero = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                divisor,
                new SmtIntegerConstant(0));
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

            var result = service.Classify(new PurityProofQuery(
                new[] { divisorIsNotZero },
                new PurityHazard(PurityHazardKind.DivideByZero, divisorIsZero)));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unknown));
            Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.Reason, Is.EqualTo("divide_by_zero_unreachable"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(service.CacheEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void Classify_DivideByZeroContradictedByPositiveInterval_BypassesSolver()
        {
            var divisor = new SmtVariable("positive_divisor_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
            var divisorIsPositive = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThan,
                divisor,
                new SmtIntegerConstant(0));
            var divisorIsZero = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                divisor,
                new SmtIntegerConstant(0));
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

            var result = service.Classify(new PurityProofQuery(
                new[] { divisorIsPositive },
                new PurityHazard(PurityHazardKind.DivideByZero, divisorIsZero)));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unknown));
            Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.Reason, Is.EqualTo("divide_by_zero_unreachable"));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(service.CacheEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void Classify_ReusedSolverContext_DistinguishesSameNamedVariablesByKind()
        {
            var intX = new SmtVariable("x", SmtValueKind.Int);
            var stringX = new SmtVariable("x", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

            var intResult = service.ClassifyPathFeasibility(new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, intX, new SmtIntegerConstant(1)),
            });
            var stringResult = service.ClassifyPathFeasibility(new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, stringX, new SmtStringConstant("A")),
            });

            Assert.That(intResult.PathFeasibility, Is.EqualTo(Feasibility.Satisfiable));
            Assert.That(stringResult.PathFeasibility, Is.EqualTo(Feasibility.Satisfiable));
            Assert.That(service.ExecutedQueryCount, Is.EqualTo(2));
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
        public void Classify_SharedResultCacheEnabled_ReusesResultAcrossServices()
        {
            var variableName = "shared_" + Guid.NewGuid().ToString("N");
            var x = new SmtVariable(variableName, SmtValueKind.Int);
            var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
            var options = new SmtAnalysisOptions(
                SmtAnalysisMode.Bounded,
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(1000),
                maxPathConditions: 4,
                maxExpressionNodes: 32,
                useSharedResultCache: true);
            var query = CreateQuery(new[] { xIsZero }, xIsZero);
            using var firstService = new SmtAnalysisService(options);
            using var secondService = new SmtAnalysisService(options);

            var first = firstService.Classify(query);
            var second = secondService.Classify(query);

            Assert.That(first.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
            Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
            Assert.That(firstService.ExecutedQueryCount, Is.EqualTo(1));
            Assert.That(secondService.ExecutedQueryCount, Is.EqualTo(0));
            Assert.That(secondService.CacheEntryCount, Is.EqualTo(1));
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
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unknown));
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
        public void ClassifyImplication_ProvesStrictRegexLiteralLengthFact()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[A-Z][0-9]\z"),
            };
            var fact = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(text),
                new SmtIntegerConstant(2));

            var result = service.ClassifyImplication(pathConditions, fact);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void ClassifyPathFeasibility_DollarAnchorAllowsTrailingNewline()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, "^AB$"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AB\n")),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void ClassifyPathFeasibility_CombinesStrictRegexAndStringEquality()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\AAB\z"),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("AB")),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void ClassifyPathFeasibility_CombinesNonCapturingRegexGroupAndStringEquality()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A(?:AB|CD)\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("EF")),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void ClassifyPathFeasibility_CombinesNegatedRegexClassAndStringEquality()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[^A]\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("A")),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void ClassifyPathFeasibility_CombinesRegexHexEscapesAndStringEquality()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, "\\A\\u0041\\x42\\z"),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("AB")),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void ClassifyImplication_ProvesShorthandRegexLengthFact()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\d\s\w\z"),
            };
            var fact = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(text),
                new SmtIntegerConstant(3));

            var result = service.ClassifyImplication(pathConditions, fact);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void ClassifyPathFeasibility_NegatedShorthandRegexClassConcreteMatchIsSelfVerified()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[^\d]\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("A")),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void ClassifyPathFeasibility_ShorthandRegexConcreteMismatchIsRejectedByDotNetValidation()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\d\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("A")),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void ClassifyImplication_ProvesCategoryRegexLengthFact()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\p{Lu}\P{Ll}\z"),
            };
            var fact = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(text),
                new SmtIntegerConstant(2));

            var result = service.ClassifyImplication(pathConditions, fact);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void ClassifyImplication_ProvesWordBoundaryRegexLengthFact()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\bAB\B?\z"),
            };
            var fact = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(text),
                new SmtIntegerConstant(2));

            var result = service.ClassifyImplication(pathConditions, fact);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void ClassifyPathFeasibility_NegatedCategoryRegexClassConcreteMismatchIsRejectedByDotNetValidation()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[^\p{Lu}]\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("A")),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void ClassifyPathFeasibility_InvalidConcreteRegexRemainsUnknown()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, "("),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("A")),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void ClassifyPathFeasibility_CombinesStringContainsAndEquality()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtStringContainsFormula(text, new SmtStringConstant("Z")),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ABC")),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void ClassifyPathFeasibility_ConcreteStringStartsWithMismatchIsRejected()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtStringStartsWithFormula(text, new SmtStringConstant("AB")),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ZAB")),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void ClassifyPathFeasibility_ConcreteNegatedStringEndsWithMismatchIsRejected()
        {
            var text = new SmtVariable("text", SmtValueKind.String);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtUnaryFormula(
                    SmtUnaryOperator.Not,
                    new SmtStringEndsWithFormula(text, new SmtStringConstant("BC"))),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ABC")),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void ClassifyPathFeasibility_CombinesStringConcatAndEquality()
        {
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtBinaryFormula(
                    SmtBinaryOperator.NotEqual,
                    new SmtStringConcatTerm(new SmtStringConstant("A"), new SmtStringConstant("B")),
                    new SmtStringConstant("AB")),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
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

        [Test]
        public void ClassifyPathFeasibility_BooleanAliasComparisonContradiction_IsUnsatisfiable()
        {
            var value = new SmtVariable("value", SmtValueKind.Int);
            var isZero = new SmtVariable("isZero", SmtValueKind.Bool);
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var pathConditions = new SmtFormula[]
            {
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    isZero,
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, value, new SmtIntegerConstant(0))),
                isZero,
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, value, new SmtIntegerConstant(0)),
            };

            var result = service.ClassifyPathFeasibility(pathConditions);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
            Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void ClassifyImplication_RuntimeTypeTestPredicateIsCongruentUnderReferenceEquality()
        {
            var x = new SmtVariable("runtime_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
            var y = new SmtVariable("runtime_y_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
            var xEqualsY = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, y);
            var xIsString = new SmtRuntimeTypeTestFormula(x, "System.String");
            var yIsString = new SmtRuntimeTypeTestFormula(y, "System.String");
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

            var result = service.ClassifyImplication(new SmtFormula[] { xEqualsY, xIsString }, yIsString);

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        }

        [Test]
        public void ClassifyPathFeasibility_RuntimeTypeTestPredicateContradictsItsNegationThroughReferenceEquality()
        {
            var x = new SmtVariable("runtime_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
            var y = new SmtVariable("runtime_y_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
            var xEqualsY = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, y);
            var xIsString = new SmtRuntimeTypeTestFormula(x, "System.String");
            var yIsNotString = new SmtUnaryFormula(
                SmtUnaryOperator.Not,
                new SmtRuntimeTypeTestFormula(y, "System.String"));
            var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

            var result = service.ClassifyPathFeasibility(new SmtFormula[] { xEqualsY, xIsString, yIsNotString });

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
