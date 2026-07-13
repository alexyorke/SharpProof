using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;
using Microsoft.Z3;

namespace SharpProof.ProofCore.Smt;

internal enum Feasibility
{
    Satisfiable,
    Unsatisfiable,
    Unknown
}

internal sealed class SmtSolver : IDisposable
{
    internal const int MaxRegexValidationCacheEntries = SmtRegexValidator.MaxCacheEntries;
    private readonly Z3FormulaEncoder _encoder = new();
    private readonly SmtRegexValidator _regexValidator = new();
    private long _lastObservedRlimitCount;

    /// <summary>
    ///     Total Z3 rlimit units consumed by checks on this solver instance. Grows
    ///     deterministically with solver work, so callers can enforce cumulative
    ///     budgets that do not depend on machine speed or load.
    /// </summary>
    public long ConsumedResourceCount { get; private set; }

    internal int RegexValidationCacheCount => _regexValidator.CacheCount;

    public void Dispose()
    {
        _encoder.Dispose();
    }

    private Status CheckAndAccountResources(Solver solver)
    {
        var status = solver.Check();
        foreach (var entry in solver.Statistics.Entries)
        {
            if (!string.Equals(entry.Key, "rlimit count", StringComparison.Ordinal) || !entry.IsUInt) continue;

            // The statistic is cumulative per Z3 context; account the delta. A
            // smaller observation means the 32-bit counter wrapped — count the
            // post-wrap portion rather than losing the observation entirely.
            long observed = entry.UIntValue;
            ConsumedResourceCount += observed >= _lastObservedRlimitCount
                ? observed - _lastObservedRlimitCount
                : (1L << 32) - _lastObservedRlimitCount + observed;
            _lastObservedRlimitCount = observed;
            break;
        }

        return status;
    }

    public Feasibility IsSatisfiable(IEnumerable<SmtFormula> pathConditions, TimeSpan timeout)
    {
        var preparedStatus = PrepareConcreteFacts(pathConditions.ToArray(), out var preparedConditions);
        if (preparedStatus != ConcreteFactPreparationStatus.Ready)
            return preparedStatus == ConcreteFactPreparationStatus.Unsatisfiable
                ? Feasibility.Unsatisfiable
                : Feasibility.Unknown;

        return IsSatisfiableRaw(preparedConditions, timeout);
    }

    public SmtFeasibilityResult CheckSatisfiability(
        IEnumerable<SmtFormula> pathConditions,
        TimeSpan timeout)
    {
        return CheckSatisfiability(pathConditions, timeout, true);
    }

    private SmtFeasibilityResult CheckSatisfiability(
        IEnumerable<SmtFormula> pathConditions,
        TimeSpan timeout,
        bool adjustApproximation)
    {
        var originalConditions = pathConditions.ToArray();
        var preparedStatus = PrepareConcreteFacts(originalConditions, out var preparedConditions);
        if (preparedStatus != ConcreteFactPreparationStatus.Ready)
            return preparedStatus == ConcreteFactPreparationStatus.Unsatisfiable
                ? new SmtFeasibilityResult(
                    Feasibility.Unsatisfiable,
                    SmtSatisfyingWitness.None("constraints_unsatisfiable"))
                : new SmtFeasibilityResult(
                    Feasibility.Unknown,
                    SmtSatisfyingWitness.Unsupported("constraint_preparation_unknown"));

        if (timeout <= TimeSpan.Zero)
            return new SmtFeasibilityResult(
                Feasibility.Unknown,
                SmtSatisfyingWitness.Unsupported("solver_timeout"));

        if (!ReferenceEquals(originalConditions, preparedConditions) &&
            !ContainsApproximateRegex(originalConditions))
            try
            {
                return CheckSatisfiabilityRawWithWitness(
                    originalConditions,
                    originalConditions,
                    timeout,
                    false,
                    adjustApproximation);
            }
            catch (Exception ex) when (IsConservativeSolverFailure(ex))
            {
                // Exact concrete facts may still use operations that the encoder cannot represent.
                // The preparation pass already validated them, so continue with the reduced query.
            }

        try
        {
            return CheckSatisfiabilityRawWithWitness(
                preparedConditions,
                originalConditions,
                timeout,
                !ReferenceEquals(originalConditions, preparedConditions),
                adjustApproximation);
        }
        catch (Exception ex) when (IsConservativeSolverFailure(ex))
        {
            return new SmtFeasibilityResult(
                Feasibility.Unknown,
                SmtSatisfyingWitness.Unsupported(GetConservativeSolverFailureReason(ex)));
        }
    }

    public SmtPathAndImpurityCheckResult CheckPathAndImpurityWithWitness(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula impurityCondition,
        TimeSpan timeout)
    {
        var normalizedPathConditions = pathConditions.ToArray();
        var deadline = Stopwatch.StartNew();
        var path = CheckSatisfiability(normalizedPathConditions, timeout, true);
        if (path.Feasibility == Feasibility.Unsatisfiable)
            return new SmtPathAndImpurityCheckResult(
                path,
                new SmtFeasibilityResult(
                    Feasibility.Unknown,
                    SmtSatisfyingWitness.None("path_not_satisfiable")));

        var remaining = timeout - deadline.Elapsed;
        var impurity = remaining <= TimeSpan.Zero
            ? new SmtFeasibilityResult(
                Feasibility.Unknown,
                SmtSatisfyingWitness.Unsupported("solver_timeout"))
            : CheckSatisfiability(
                normalizedPathConditions.Concat(new[] { impurityCondition }),
                remaining);
        return new SmtPathAndImpurityCheckResult(path, impurity);
    }

    private Feasibility IsSatisfiableRaw(IEnumerable<SmtFormula> pathConditions, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) return Feasibility.Unknown;

        try
        {
            var conditions = pathConditions as SmtFormula[] ?? pathConditions.ToArray();
            var containsApproximateRegex = ContainsApproximateRegex(conditions);
            using var solver = _encoder.CreateSolver(timeout);
            foreach (var formula in conditions) solver.Assert(_encoder.EncodeCondition(formula));

            return AdjustForApproximation(ToFeasibility(CheckAndAccountResources(solver)), containsApproximateRegex);
        }
        catch (Exception ex) when (IsConservativeSolverFailure(ex))
        {
            return Feasibility.Unknown;
        }
    }

    private SmtFeasibilityResult CheckSatisfiabilityRawWithWitness(
        IReadOnlyList<SmtFormula> conditions,
        IReadOnlyList<SmtFormula> modelConditions,
        TimeSpan timeout,
        bool preprocessedModel,
        bool adjustApproximation)
    {
        var containsApproximateRegex = ContainsApproximateRegex(conditions);
        using var solver = _encoder.CreateSolver(timeout);
        foreach (var formula in conditions) solver.Assert(_encoder.EncodeCondition(formula));

        var feasibility = ToFeasibility(CheckAndAccountResources(solver));
        if (feasibility == Feasibility.Unsatisfiable)
            return new SmtFeasibilityResult(
                feasibility,
                SmtSatisfyingWitness.None("constraints_unsatisfiable"));

        if (feasibility != Feasibility.Satisfiable)
            return new SmtFeasibilityResult(
                feasibility,
                SmtSatisfyingWitness.Unsupported("solver_unknown"));

        var witnessStatus = containsApproximateRegex || preprocessedModel
            ? SmtWitnessStatus.Approximate
            : SmtWitnessStatus.Exact;
        var witnessReason = containsApproximateRegex
            ? "approximate_regex_model"
            : preprocessedModel
                ? "model_from_preprocessed_constraints"
                : "satisfying_model";
        using var model = solver.Model;
        var witness = _encoder.CreateWitness(
            model,
            CollectVariables(modelConditions),
            witnessStatus,
            witnessReason);
        return new SmtFeasibilityResult(
            adjustApproximation
                ? AdjustForApproximation(feasibility, containsApproximateRegex)
                : feasibility,
            witness);
    }

    public Feasibility Implies(IEnumerable<SmtFormula> pathConditions, SmtFormula conclusion, TimeSpan timeout)
    {
        var combinedConditions = pathConditions
            .Concat(new[] { new SmtUnaryFormula(SmtUnaryOperator.Not, conclusion) })
            .ToArray();
        return IsSatisfiable(combinedConditions, timeout);
    }

    public (Feasibility PathFeasibility, Feasibility ImpurityFeasibility) CheckPathAndImpurity(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula impurityCondition,
        TimeSpan timeout)
    {
        var originalPathConditions = pathConditions.ToArray();
        var pathPreparationStatus = PrepareConcreteFacts(originalPathConditions, out var preparedPathConditions);
        if (pathPreparationStatus != ConcreteFactPreparationStatus.Ready)
            return (pathPreparationStatus == ConcreteFactPreparationStatus.Unsatisfiable
                ? Feasibility.Unsatisfiable
                : Feasibility.Unknown, Feasibility.Unknown);

        if (timeout <= TimeSpan.Zero) return (Feasibility.Unknown, Feasibility.Unknown);

        try
        {
            using var solver = _encoder.CreateSolver(timeout);
            foreach (var formula in preparedPathConditions) solver.Assert(_encoder.EncodeCondition(formula));

            var pathFeasibility = ToFeasibility(CheckAndAccountResources(solver));
            if (pathFeasibility != Feasibility.Satisfiable) return (pathFeasibility, Feasibility.Unknown);

            // A SAT path under regex approximation is only "may be feasible"; still check the
            // combined query because UNSAT under the over-approximation remains a safe proof.
            //
            // Use the original path facts for the combined query. The path-only preparation pass
            // may remove equalities as already-satisfied facts, but those equalities can still be
            // required to prove the hazard condition unreachable.
            var combinedConditions = originalPathConditions.Concat(new[] { impurityCondition }).ToArray();
            var combinedPreparationStatus =
                PrepareConcreteFacts(combinedConditions, out var preparedCombinedConditions);
            if (combinedPreparationStatus != ConcreteFactPreparationStatus.Ready)
                return (pathFeasibility, combinedPreparationStatus == ConcreteFactPreparationStatus.Unsatisfiable
                    ? Feasibility.Unsatisfiable
                    : Feasibility.Unknown);

            if (!ReferenceEquals(preparedCombinedConditions, combinedConditions))
                return (pathFeasibility, AdjustForApproximation(
                    IsSatisfiableRaw(preparedCombinedConditions, timeout),
                    ContainsApproximateRegex(combinedConditions)));

            solver.Push();
            try
            {
                solver.Assert(_encoder.EncodeCondition(impurityCondition));
                var combinedContainsApproximateRegex = ContainsApproximateRegex(combinedConditions);
                return (pathFeasibility, AdjustForApproximation(
                    ToFeasibility(CheckAndAccountResources(solver)),
                    combinedContainsApproximateRegex));
            }
            finally
            {
                solver.Pop();
            }
        }
        catch (Exception ex) when (IsConservativeSolverFailure(ex))
        {
            return (Feasibility.Unknown, Feasibility.Unknown);
        }
    }

    private static Feasibility ToFeasibility(Status status)
    {
        return status switch
        {
            Status.SATISFIABLE => Feasibility.Satisfiable,
            Status.UNSATISFIABLE => Feasibility.Unsatisfiable,
            _ => Feasibility.Unknown
        };
    }

    private bool ContainsApproximateRegex(IEnumerable<SmtFormula> formulas)
    {
        return formulas.Any(_encoder.ContainsApproximateRegex);
    }

    private static Feasibility AdjustForApproximation(Feasibility feasibility, bool containsApproximateRegex)
    {
        return feasibility == Feasibility.Satisfiable && containsApproximateRegex
            ? Feasibility.Unknown
            : feasibility;
    }

    private static bool IsConservativeSolverFailure(Exception ex)
    {
        return ex is InvalidOperationException ||
               ex is Z3Exception ||
               ex is ArgumentException ||
               ex is InvalidCastException ||
               ex is RegexMatchTimeoutException ||
               ex is ArithmeticException;
    }

    private static string GetConservativeSolverFailureReason(Exception ex)
    {
        return ex switch
        {
            Z3Exception => "z3_transient_failure",
            RegexMatchTimeoutException => "solver_timeout",
            _ => "solver_encoding_failure"
        };
    }

    private ConcreteFactPreparationStatus PrepareConcreteFacts(
        SmtFormula[] conditions,
        out SmtFormula[] preparedConditions)
    {
        if (!SmtFormulaNormalizer.TryNormalizeInitial(
                conditions,
                out var normalizedConditions,
                out var changed))
        {
            preparedConditions = Array.Empty<SmtFormula>();
            return ConcreteFactPreparationStatus.Unsatisfiable;
        }

        var facts = new ConcreteFactContext();
        if (!TryCollectBooleanFacts(normalizedConditions, facts))
        {
            preparedConditions = Array.Empty<SmtFormula>();
            return ConcreteFactPreparationStatus.Unsatisfiable;
        }

        var conditionalStatus = SimplifyKnownConditionalTerms(normalizedConditions, facts, ref changed);
        if (conditionalStatus != ConcreteFactPreparationStatus.Ready)
        {
            preparedConditions = Array.Empty<SmtFormula>();
            return conditionalStatus;
        }

        if (!TryCollectBooleanFacts(normalizedConditions, facts))
        {
            preparedConditions = Array.Empty<SmtFormula>();
            return ConcreteFactPreparationStatus.Unsatisfiable;
        }

        var referenceStatus = TryCollectReferenceFacts(normalizedConditions, facts);
        if (referenceStatus != ConcreteFactPreparationStatus.Ready)
        {
            preparedConditions = Array.Empty<SmtFormula>();
            return referenceStatus;
        }

        if (!TryCollectStringEqualities(normalizedConditions, facts))
        {
            preparedConditions = Array.Empty<SmtFormula>();
            return ConcreteFactPreparationStatus.Unsatisfiable;
        }

        var integerStatus = TryCollectIntegerFacts(normalizedConditions, facts);
        if (integerStatus != ConcreteFactPreparationStatus.Ready)
        {
            preparedConditions = Array.Empty<SmtFormula>();
            return integerStatus;
        }

        foreach (var condition in normalizedConditions)
        {
            integerStatus = ValidateIntegerTermSafety(condition, facts);
            if (integerStatus != ConcreteFactPreparationStatus.Ready)
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return integerStatus;
            }
        }

        var stringLengthEqualities = new Dictionary<SmtFormula, long>();
        foreach (var condition in normalizedConditions)
            if (!TryCollectStringLengthEqualities(condition, stringLengthEqualities))
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return ConcreteFactPreparationStatus.Unsatisfiable;
            }

        foreach (var condition in normalizedConditions)
        {
            var status = TryInferStringEqualitiesFromLengthConstrainedPredicates(
                condition,
                stringLengthEqualities,
                facts);
            if (status != ConcreteFactPreparationStatus.Ready)
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return status;
            }
        }

        var stringShapeStatus = TryApplyStringShapeFacts(
            normalizedConditions,
            stringLengthEqualities,
            facts);
        if (stringShapeStatus != ConcreteFactPreparationStatus.Ready)
        {
            preparedConditions = Array.Empty<SmtFormula>();
            return stringShapeStatus;
        }

        var builder = new List<SmtFormula>(normalizedConditions.Count);
        foreach (var condition in normalizedConditions)
        {
            var status = SimplifyConcreteFacts(
                condition,
                facts,
                out var preparedCondition,
                out var conditionChanged);
            if (status != ConcreteFactPreparationStatus.Ready)
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return status;
            }

            changed |= conditionChanged;
            if (!SmtFormulaNormalizer.TryClassifyCondition(preparedCondition, out var shouldKeep))
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return ConcreteFactPreparationStatus.Unsatisfiable;
            }

            if (!shouldKeep)
            {
                changed = true;
                continue;
            }

            builder.Add(preparedCondition);
        }

        preparedConditions = changed ? builder.ToArray() : conditions;
        return ConcreteFactPreparationStatus.Ready;
    }

    private static ConcreteFactPreparationStatus SimplifyKnownConditionalTerms(
        List<SmtFormula> conditions,
        ConcreteFactContext facts,
        ref bool changed)
    {
        for (var index = 0; index < conditions.Count; index++)
        {
            var simplified = SimplifyKnownConditionalTerms(conditions[index], facts, out var conditionChanged);
            changed |= conditionChanged;
            if (simplified is SmtBooleanConstant { Value: false }) return ConcreteFactPreparationStatus.Unsatisfiable;

            conditions[index] = simplified;
        }

        return ConcreteFactPreparationStatus.Ready;
    }

    private static SmtFormula SimplifyKnownConditionalTerms(
        SmtFormula formula,
        ConcreteFactContext facts,
        out bool changed)
    {
        return SmtFormulaTraversal.RewriteBottomUp(
            formula,
            candidate =>
            {
                if (candidate is not SmtConditionalFormula conditional) return candidate;

                if (SmtFormulaTraversal.AreStructurallyEqual(conditional.WhenTrue, conditional.WhenFalse))
                    return conditional.WhenTrue;

                if (TryEvaluateConcreteBoolean(conditional.Condition, facts, out var selectedBranch))
                    return selectedBranch ? conditional.WhenTrue : conditional.WhenFalse;

                return candidate;
            },
            out changed);
    }

    private static bool TryCollectBooleanFacts(
        IReadOnlyList<SmtFormula> conditions,
        ConcreteFactContext facts)
    {
        var iterationLimit = Math.Max(1, conditions.Count * 4);
        var changed = false;
        do
        {
            changed = false;
            foreach (var condition in conditions)
                if (!TryCollectBooleanFacts(condition, facts, ref changed))
                    return false;

            iterationLimit--;
        } while (changed && iterationLimit > 0);

        return true;
    }

    private static bool TryCollectBooleanFacts(
        SmtFormula formula,
        ConcreteFactContext facts,
        ref bool changed)
    {
        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            return TryCollectBooleanFacts(andFormula.Left, facts, ref changed) &&
                   TryCollectBooleanFacts(andFormula.Right, facts, ref changed);

        if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } notFormula)
            return CanCacheBooleanFact(notFormula.Operand)
                ? TryAddBooleanEquality(facts, notFormula.Operand, false, ref changed)
                : true;

        if (formula is SmtBinaryFormula
            {
                Operator: SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual
            } equalityFormula &&
            equalityFormula.Left.Kind == SmtValueKind.Bool &&
            equalityFormula.Right.Kind == SmtValueKind.Bool)
        {
            if (TryEvaluateConcreteBoolean(equalityFormula.Left, facts, out var leftValue))
            {
                var expectedRight = equalityFormula.Operator == SmtBinaryOperator.Equal
                    ? leftValue
                    : !leftValue;
                return TryAddBooleanEquality(facts, equalityFormula.Right, expectedRight, ref changed);
            }

            if (TryEvaluateConcreteBoolean(equalityFormula.Right, facts, out var rightValue))
            {
                var expectedLeft = equalityFormula.Operator == SmtBinaryOperator.Equal
                    ? rightValue
                    : !rightValue;
                return TryAddBooleanEquality(facts, equalityFormula.Left, expectedLeft, ref changed);
            }
        }

        if (formula.Kind == SmtValueKind.Bool &&
            CanCacheBooleanFact(formula))
            return TryAddBooleanEquality(facts, formula, true, ref changed);

        return true;
    }

    private static bool TryAddBooleanEquality(
        ConcreteFactContext facts,
        SmtFormula formula,
        bool value,
        ref bool changed)
    {
        if (formula.Kind != SmtValueKind.Bool ||
            !CanCacheBooleanFact(formula))
            return true;

        if (facts.BooleanEqualities.TryGetValue(formula, out var existing)) return existing == value;

        facts.BooleanEqualities.Add(formula, value);
        changed = true;
        return true;
    }

    private static bool CanCacheBooleanFact(SmtFormula formula)
    {
        if (formula is SmtVariable { Kind: SmtValueKind.Bool }) return true;

        if (formula is SmtRuntimeTypeTestFormula) return true;

        if (formula is not SmtBinaryFormula binaryFormula) return false;

        if (binaryFormula.Operator is SmtBinaryOperator.And or SmtBinaryOperator.Or) return false;

        if (binaryFormula.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual &&
            binaryFormula.Left.Kind == SmtValueKind.Bool &&
            binaryFormula.Right.Kind == SmtValueKind.Bool)
            return false;

        return !ContainsRegexOrStringPredicate(binaryFormula);
    }

    private static bool ContainsRegexOrStringPredicate(SmtFormula formula)
    {
        return formula switch
        {
            SmtRegexMatchFormula => true,
            SmtStringContainsFormula => true,
            SmtStringStartsWithFormula => true,
            SmtStringEndsWithFormula => true,
            SmtUnaryFormula unaryFormula => ContainsRegexOrStringPredicate(unaryFormula.Operand),
            SmtBinaryFormula binaryFormula => ContainsRegexOrStringPredicate(binaryFormula.Left) ||
                                              ContainsRegexOrStringPredicate(binaryFormula.Right),
            SmtIntegerUnaryTerm integerUnaryTerm => ContainsRegexOrStringPredicate(integerUnaryTerm.Operand),
            SmtIntegerBinaryTerm integerBinaryTerm => ContainsRegexOrStringPredicate(integerBinaryTerm.Left) ||
                                                      ContainsRegexOrStringPredicate(integerBinaryTerm.Right),
            SmtOpaqueIntegerBinaryTerm opaqueIntegerTerm =>
                ContainsRegexOrStringPredicate(opaqueIntegerTerm.Left) ||
                ContainsRegexOrStringPredicate(opaqueIntegerTerm.Right),
            SmtStringLengthTerm stringLengthTerm => ContainsRegexOrStringPredicate(stringLengthTerm.Value),
            SmtStringConcatTerm stringConcatTerm => ContainsRegexOrStringPredicate(stringConcatTerm.Left) ||
                                                    ContainsRegexOrStringPredicate(stringConcatTerm.Right),
            SmtConditionalFormula conditionalFormula => ContainsRegexOrStringPredicate(conditionalFormula.Condition) ||
                                                        ContainsRegexOrStringPredicate(conditionalFormula.WhenTrue) ||
                                                        ContainsRegexOrStringPredicate(conditionalFormula.WhenFalse),
            SmtRuntimeTypeTestFormula runtimeTypeTest => ContainsRegexOrStringPredicate(runtimeTypeTest.Value),
            _ => false
        };
    }

    private static ConcreteFactPreparationStatus TryCollectReferenceFacts(
        IReadOnlyList<SmtFormula> conditions,
        ConcreteFactContext facts)
    {
        var iterationLimit = Math.Max(1, conditions.Count * 4);
        var changed = false;
        do
        {
            changed = false;
            foreach (var condition in conditions)
            {
                var status = TryCollectReferenceFacts(condition, facts, ref changed);
                if (status != ConcreteFactPreparationStatus.Ready) return status;
            }

            iterationLimit--;
        } while (changed && iterationLimit > 0);

        return ConcreteFactPreparationStatus.Ready;
    }

    private static ConcreteFactPreparationStatus TryCollectReferenceFacts(
        SmtFormula formula,
        ConcreteFactContext facts,
        ref bool changed)
    {
        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
        {
            var leftStatus = TryCollectReferenceFacts(andFormula.Left, facts, ref changed);
            if (leftStatus != ConcreteFactPreparationStatus.Ready) return leftStatus;

            return TryCollectReferenceFacts(andFormula.Right, facts, ref changed);
        }

        if (formula is not SmtBinaryFormula
            {
                Operator: SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual
            } binaryFormula ||
            binaryFormula.Left.Kind != SmtValueKind.Reference ||
            binaryFormula.Right.Kind != SmtValueKind.Reference)
            return ConcreteFactPreparationStatus.Ready;

        var isEquality = binaryFormula.Operator == SmtBinaryOperator.Equal;
        if (EqualityComparer<SmtFormula>.Default.Equals(binaryFormula.Left, binaryFormula.Right))
            return isEquality
                ? ConcreteFactPreparationStatus.Ready
                : ConcreteFactPreparationStatus.Unsatisfiable;

        if (binaryFormula.Left is SmtNullConstant)
            return TryAddReferenceNullEquality(facts, binaryFormula.Right, isEquality, ref changed)
                ? ConcreteFactPreparationStatus.Ready
                : ConcreteFactPreparationStatus.Unsatisfiable;

        if (binaryFormula.Right is SmtNullConstant)
            return TryAddReferenceNullEquality(facts, binaryFormula.Left, isEquality, ref changed)
                ? ConcreteFactPreparationStatus.Ready
                : ConcreteFactPreparationStatus.Unsatisfiable;

        var leftKnown = TryEvaluateReferenceNull(binaryFormula.Left, facts, out var leftIsNull);
        var rightKnown = TryEvaluateReferenceNull(binaryFormula.Right, facts, out var rightIsNull);
        if (leftKnown && rightKnown && (leftIsNull || rightIsNull))
        {
            var equal = leftIsNull && rightIsNull;
            return CompareEquality(binaryFormula.Operator, equal)
                ? ConcreteFactPreparationStatus.Ready
                : ConcreteFactPreparationStatus.Unsatisfiable;
        }

        if (!isEquality) return ConcreteFactPreparationStatus.Ready;

        if (leftKnown)
            return TryAddReferenceNullEquality(facts, binaryFormula.Right, leftIsNull, ref changed)
                ? ConcreteFactPreparationStatus.Ready
                : ConcreteFactPreparationStatus.Unsatisfiable;

        if (rightKnown)
            return TryAddReferenceNullEquality(facts, binaryFormula.Left, rightIsNull, ref changed)
                ? ConcreteFactPreparationStatus.Ready
                : ConcreteFactPreparationStatus.Unsatisfiable;

        return ConcreteFactPreparationStatus.Ready;
    }

    private static bool TryAddReferenceNullEquality(
        ConcreteFactContext facts,
        SmtFormula formula,
        bool isNull,
        ref bool changed)
    {
        if (formula.Kind != SmtValueKind.Reference) return true;

        if (formula is SmtNullConstant) return isNull;

        if (facts.ReferenceNullEqualities.TryGetValue(formula, out var existing)) return existing == isNull;

        facts.ReferenceNullEqualities.Add(formula, isNull);
        changed = true;
        return true;
    }

    private static ConcreteFactPreparationStatus TryCollectIntegerFacts(
        IReadOnlyList<SmtFormula> conditions,
        ConcreteFactContext facts)
    {
        var iterationLimit = Math.Max(1, conditions.Count * 4);
        var changed = false;
        do
        {
            changed = false;
            foreach (var condition in conditions)
            {
                var status = TryCollectIntegerFacts(
                    condition,
                    facts,
                    ref changed);
                if (status != ConcreteFactPreparationStatus.Ready) return status;
            }

            iterationLimit--;
        } while (changed && iterationLimit > 0);

        return ConcreteFactPreparationStatus.Ready;
    }

    private static ConcreteFactPreparationStatus TryCollectIntegerFacts(
        SmtFormula formula,
        ConcreteFactContext facts,
        ref bool changed)
    {
        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
        {
            var leftStatus = TryCollectIntegerFacts(
                andFormula.Left,
                facts,
                ref changed);
            if (leftStatus != ConcreteFactPreparationStatus.Ready) return leftStatus;

            return TryCollectIntegerFacts(
                andFormula.Right,
                facts,
                ref changed);
        }

        if (formula is not SmtBinaryFormula binaryFormula) return ConcreteFactPreparationStatus.Ready;

        var boundStatus = TryCollectIntegerBoundFact(binaryFormula, facts, ref changed);
        if (boundStatus != ConcreteFactPreparationStatus.Ready) return boundStatus;

        if (binaryFormula.Operator == SmtBinaryOperator.NotEqual &&
            TryEvaluateInteger(binaryFormula.Left, facts, out var notEqualLeft) &&
            TryEvaluateInteger(binaryFormula.Right, facts, out var notEqualRight) &&
            notEqualLeft == notEqualRight)
            return ConcreteFactPreparationStatus.Unsatisfiable;

        if (binaryFormula.Operator != SmtBinaryOperator.Equal) return ConcreteFactPreparationStatus.Ready;

        var leftIsConcrete = TryEvaluateInteger(binaryFormula.Left, facts, out var leftValue);
        var rightIsConcrete = TryEvaluateInteger(binaryFormula.Right, facts, out var rightValue);
        if (leftIsConcrete && rightIsConcrete)
            return leftValue == rightValue
                ? ConcreteFactPreparationStatus.Ready
                : ConcreteFactPreparationStatus.Unsatisfiable;

        if (TrySolveAffineIntegerEquality(binaryFormula, facts, ref changed, out var affineStatus)) return affineStatus;

        if (leftIsConcrete)
            return TryAddIntegerEquality(facts, binaryFormula.Right, leftValue, ref changed)
                ? ConcreteFactPreparationStatus.Ready
                : ConcreteFactPreparationStatus.Unsatisfiable;

        if (rightIsConcrete)
            return TryAddIntegerEquality(facts, binaryFormula.Left, rightValue, ref changed)
                ? ConcreteFactPreparationStatus.Ready
                : ConcreteFactPreparationStatus.Unsatisfiable;

        return ConcreteFactPreparationStatus.Ready;
    }

    private static bool TryAddIntegerEquality(
        ConcreteFactContext facts,
        SmtFormula formula,
        long value,
        ref bool changed)
    {
        if (formula.Kind != SmtValueKind.Int) return true;

        if (facts.IntegerEqualities.TryGetValue(formula, out var existing)) return existing == value;

        facts.IntegerEqualities.Add(formula, value);
        if (!TryMergeIntegerBounds(
                facts,
                formula,
                value,
                value,
                false,
                ref changed))
            return false;

        changed = true;
        return true;
    }

    private static ConcreteFactPreparationStatus TryCollectIntegerBoundFact(
        SmtBinaryFormula formula,
        ConcreteFactContext facts,
        ref bool changed)
    {
        if (!TryNormalizeIntegerComparisonToConstant(formula, out var expression, out var op, out var constant))
            return ConcreteFactPreparationStatus.Ready;

        long? lower = null;
        long? upper = null;
        var excludesZero = false;
        switch (op)
        {
            case SmtBinaryOperator.Equal:
                lower = constant;
                upper = constant;
                break;
            case SmtBinaryOperator.NotEqual:
                excludesZero = constant == 0;
                break;
            case SmtBinaryOperator.LessThan:
                if (!TryCheckedAdd(constant, -1, out var lessThanUpper))
                    return ConcreteFactPreparationStatus.Unsatisfiable;

                upper = lessThanUpper;

                break;
            case SmtBinaryOperator.LessThanOrEqual:
                upper = constant;
                break;
            case SmtBinaryOperator.GreaterThan:
                if (!TryCheckedAdd(constant, 1, out var greaterThanLower))
                    return ConcreteFactPreparationStatus.Unsatisfiable;

                lower = greaterThanLower;

                break;
            case SmtBinaryOperator.GreaterThanOrEqual:
                lower = constant;
                break;
        }

        return TryMergeIntegerBounds(facts, expression, lower, upper, excludesZero, ref changed)
            ? ConcreteFactPreparationStatus.Ready
            : ConcreteFactPreparationStatus.Unsatisfiable;
    }

    private static bool TryMergeIntegerBounds(
        ConcreteFactContext facts,
        SmtFormula expression,
        long? lower,
        long? upper,
        bool excludesZero,
        ref bool changed)
    {
        if (expression.Kind != SmtValueKind.Int) return true;

        facts.IntegerBounds.TryGetValue(expression, out var bounds);
        if (lower.HasValue && (!bounds.Lower.HasValue || lower.Value > bounds.Lower.Value))
        {
            bounds.Lower = lower.Value;
            changed = true;
        }

        if (upper.HasValue && (!bounds.Upper.HasValue || upper.Value < bounds.Upper.Value))
        {
            bounds.Upper = upper.Value;
            changed = true;
        }

        if (excludesZero && !bounds.ExcludesZero)
        {
            bounds.ExcludesZero = true;
            changed = true;
        }

        if (bounds.IsUnsatisfiable) return false;

        facts.IntegerBounds[expression] = bounds;
        return true;
    }

    private static bool TrySolveAffineIntegerEquality(
        SmtBinaryFormula formula,
        ConcreteFactContext facts,
        ref bool changed,
        out ConcreteFactPreparationStatus status)
    {
        status = ConcreteFactPreparationStatus.Ready;
        if (formula.Operator != SmtBinaryOperator.Equal) return false;

        if (TryGetAffineTerm(formula.Left, facts, out var leftBase, out var leftCoefficient, out var leftConstant) &&
            leftBase is not null &&
            TryEvaluateInteger(formula.Right, facts, out var rightValue))
            return TrySolveAffineEquality(
                facts,
                leftBase,
                leftCoefficient,
                leftConstant,
                rightValue,
                ref changed,
                out status);

        if (TryGetAffineTerm(formula.Right, facts, out var rightBase, out var rightCoefficient,
                out var rightConstant) &&
            rightBase is not null &&
            TryEvaluateInteger(formula.Left, facts, out var leftValue))
            return TrySolveAffineEquality(
                facts,
                rightBase,
                rightCoefficient,
                rightConstant,
                leftValue,
                ref changed,
                out status);

        return false;
    }

    private static bool TrySolveAffineEquality(
        ConcreteFactContext facts,
        SmtFormula variable,
        long coefficient,
        long constant,
        long value,
        ref bool changed,
        out ConcreteFactPreparationStatus status)
    {
        if (coefficient == 0)
        {
            status = constant == value
                ? ConcreteFactPreparationStatus.Ready
                : ConcreteFactPreparationStatus.Unsatisfiable;
            return true;
        }

        var adjusted = (BigInteger)value - constant;
        var bigCoefficient = (BigInteger)coefficient;
        if (adjusted % bigCoefficient != 0)
        {
            status = ConcreteFactPreparationStatus.Unsatisfiable;
            return true;
        }

        var solved = adjusted / bigCoefficient;
        if (solved < long.MinValue || solved > long.MaxValue)
        {
            status = ConcreteFactPreparationStatus.Ready;
            return false;
        }

        var solvedValue = (long)solved;
        status = TryAddIntegerEquality(facts, variable, solvedValue, ref changed)
            ? ConcreteFactPreparationStatus.Ready
            : ConcreteFactPreparationStatus.Unsatisfiable;
        return true;
    }

    private static bool TryGetAffineTerm(
        SmtFormula formula,
        ConcreteFactContext facts,
        out SmtFormula? variable,
        out long coefficient,
        out long constant)
    {
        variable = null;
        coefficient = 0;
        constant = 0;

        if (TryEvaluateInteger(formula, facts, out var concrete))
        {
            constant = concrete;
            return true;
        }

        switch (formula)
        {
            case SmtVariable { Kind: SmtValueKind.Int }:
                variable = formula;
                coefficient = 1;
                return true;
            case SmtIntegerUnaryTerm { Operator: SmtIntegerUnaryOperator.Negate } unaryTerm:
                if (!TryGetAffineTerm(unaryTerm.Operand, facts, out variable, out coefficient, out constant) ||
                    !TryCheckedNegate(coefficient, out coefficient) ||
                    !TryCheckedNegate(constant, out constant))
                    return false;

                return true;
            case SmtIntegerBinaryTerm binaryTerm:
                return TryGetAffineTerm(binaryTerm, facts, out variable, out coefficient, out constant);
            default:
                return false;
        }
    }

    private static bool TryGetAffineTerm(
        SmtIntegerBinaryTerm term,
        ConcreteFactContext facts,
        out SmtFormula? variable,
        out long coefficient,
        out long constant)
    {
        variable = null;
        coefficient = 0;
        constant = 0;

        if (term.Operator is SmtIntegerBinaryOperator.Add or SmtIntegerBinaryOperator.Subtract)
        {
            if (!TryGetAffineTerm(term.Left, facts, out var leftVariable, out var leftCoefficient,
                    out var leftConstant) ||
                !TryGetAffineTerm(term.Right, facts, out var rightVariable, out var rightCoefficient,
                    out var rightConstant))
                return false;

            if (term.Operator == SmtIntegerBinaryOperator.Subtract)
                if (!TryCheckedNegate(rightCoefficient, out rightCoefficient) ||
                    !TryCheckedNegate(rightConstant, out rightConstant))
                    return false;

            if (leftVariable is not null &&
                rightVariable is not null &&
                !EqualityComparer<SmtFormula>.Default.Equals(leftVariable, rightVariable))
                return false;

            variable = leftVariable ?? rightVariable;
            return TryCheckedAdd(leftCoefficient, rightCoefficient, out coefficient) &&
                   TryCheckedAdd(leftConstant, rightConstant, out constant);
        }

        if (term.Operator == SmtIntegerBinaryOperator.Multiply)
        {
            if (TryEvaluateInteger(term.Left, facts, out var leftConstant) &&
                TryGetAffineTerm(term.Right, facts, out variable, out coefficient, out constant))
                return TryCheckedMultiply(coefficient, leftConstant, out coefficient) &&
                       TryCheckedMultiply(constant, leftConstant, out constant);

            if (TryEvaluateInteger(term.Right, facts, out var rightConstant) &&
                TryGetAffineTerm(term.Left, facts, out variable, out coefficient, out constant))
                return TryCheckedMultiply(coefficient, rightConstant, out coefficient) &&
                       TryCheckedMultiply(constant, rightConstant, out constant);
        }

        return false;
    }

    private static bool TryCheckedAdd(long left, long right, out long value)
    {
        return TryCheckedBinary(left, right, static (first, second) => checked(first + second), out value);
    }

    private static bool TryCheckedSubtract(long left, long right, out long value)
    {
        return TryCheckedBinary(left, right, static (first, second) => checked(first - second), out value);
    }

    private static bool TryCheckedMultiply(long left, long right, out long value)
    {
        return TryCheckedBinary(left, right, static (first, second) => checked(first * second), out value);
    }

    private static bool TryCheckedNegate(long operand, out long value)
    {
        return TryCheckedUnary(operand, static item => checked(-item), out value);
    }

    private static bool TryCheckedBinary(
        long left,
        long right,
        Func<long, long, long> operation,
        out long value)
    {
        try
        {
            value = operation(left, right);
            return true;
        }
        catch (OverflowException)
        {
            value = default;
            return false;
        }
    }

    private static bool TryCheckedUnary(long operand, Func<long, long> operation, out long value)
    {
        try
        {
            value = operation(operand);
            return true;
        }
        catch (OverflowException)
        {
            value = default;
            return false;
        }
    }

    private static bool TryNormalizeIntegerComparisonToConstant(
        SmtBinaryFormula formula,
        out SmtFormula expression,
        out SmtBinaryOperator op,
        out long constant)
    {
        if (formula.Left is SmtIntegerConstant leftConstant && formula.Right.Kind == SmtValueKind.Int)
        {
            expression = formula.Right;
            op = SwapComparisonOperator(formula.Operator);
            constant = leftConstant.Value;
            return IsIntegerComparisonOperator(op);
        }

        if (formula.Right is SmtIntegerConstant rightConstant && formula.Left.Kind == SmtValueKind.Int)
        {
            expression = formula.Left;
            op = formula.Operator;
            constant = rightConstant.Value;
            return IsIntegerComparisonOperator(op);
        }

        expression = null!;
        op = default;
        constant = default;
        return false;
    }

    private static bool IsIntegerComparisonOperator(SmtBinaryOperator op)
    {
        return op is SmtBinaryOperator.Equal or
            SmtBinaryOperator.NotEqual or
            SmtBinaryOperator.LessThan or
            SmtBinaryOperator.LessThanOrEqual or
            SmtBinaryOperator.GreaterThan or
            SmtBinaryOperator.GreaterThanOrEqual;
    }

    private static SmtBinaryOperator SwapComparisonOperator(SmtBinaryOperator op)
    {
        return op switch
        {
            SmtBinaryOperator.LessThan => SmtBinaryOperator.GreaterThan,
            SmtBinaryOperator.LessThanOrEqual => SmtBinaryOperator.GreaterThanOrEqual,
            SmtBinaryOperator.GreaterThan => SmtBinaryOperator.LessThan,
            SmtBinaryOperator.GreaterThanOrEqual => SmtBinaryOperator.LessThanOrEqual,
            _ => op
        };
    }

    private static ConcreteFactPreparationStatus ValidateIntegerTermSafety(
        SmtFormula formula,
        ConcreteFactContext facts)
    {
        switch (formula)
        {
            case SmtUnaryFormula unaryFormula:
                return ValidateIntegerTermSafety(unaryFormula.Operand, facts);
            case SmtBinaryFormula binaryFormula:
                var leftStatus = ValidateIntegerTermSafety(binaryFormula.Left, facts);
                if (leftStatus != ConcreteFactPreparationStatus.Ready) return leftStatus;

                return ValidateIntegerTermSafety(binaryFormula.Right, facts);
            case SmtIntegerUnaryTerm integerUnaryTerm:
                return ValidateIntegerTermSafety(integerUnaryTerm.Operand, facts);
            case SmtIntegerBinaryTerm integerBinaryTerm:
                var integerLeftStatus = ValidateIntegerTermSafety(integerBinaryTerm.Left, facts);
                if (integerLeftStatus != ConcreteFactPreparationStatus.Ready) return integerLeftStatus;

                var integerRightStatus = ValidateIntegerTermSafety(integerBinaryTerm.Right, facts);
                if (integerRightStatus != ConcreteFactPreparationStatus.Ready) return integerRightStatus;

                if (integerBinaryTerm.Operator is not (SmtIntegerBinaryOperator.Divide
                    or SmtIntegerBinaryOperator.Remainder)) return ConcreteFactPreparationStatus.Ready;

                if (TryEvaluateInteger(integerBinaryTerm.Right, facts, out var denominator))
                    return denominator == 0
                        ? ConcreteFactPreparationStatus.Unknown
                        : ConcreteFactPreparationStatus.Ready;

                if (TryIntegerIntervalExcludesZero(integerBinaryTerm.Right, facts))
                    return ConcreteFactPreparationStatus.Ready;

                // Z3 assigns a totalized value to division and remainder by zero,
                // while C# throws. Only encode the operation when the path facts
                // prove that the divisor cannot be zero.
                return ConcreteFactPreparationStatus.Unknown;
            case SmtOpaqueIntegerBinaryTerm opaqueIntegerTerm:
                var opaqueLeftStatus = ValidateIntegerTermSafety(opaqueIntegerTerm.Left, facts);
                if (opaqueLeftStatus != ConcreteFactPreparationStatus.Ready) return opaqueLeftStatus;

                return ValidateIntegerTermSafety(opaqueIntegerTerm.Right, facts);
            case SmtStringLengthTerm stringLengthTerm:
                return ValidateIntegerTermSafety(stringLengthTerm.Value, facts);
            case SmtStringConcatTerm stringConcatTerm:
                var concatLeftStatus = ValidateIntegerTermSafety(stringConcatTerm.Left, facts);
                if (concatLeftStatus != ConcreteFactPreparationStatus.Ready) return concatLeftStatus;

                return ValidateIntegerTermSafety(stringConcatTerm.Right, facts);
            case SmtStringContainsFormula stringContainsFormula:
                var containsValueStatus = ValidateIntegerTermSafety(stringContainsFormula.Value, facts);
                if (containsValueStatus != ConcreteFactPreparationStatus.Ready) return containsValueStatus;

                return ValidateIntegerTermSafety(stringContainsFormula.Search, facts);
            case SmtStringStartsWithFormula stringStartsWithFormula:
                var startsWithValueStatus = ValidateIntegerTermSafety(stringStartsWithFormula.Value, facts);
                if (startsWithValueStatus != ConcreteFactPreparationStatus.Ready) return startsWithValueStatus;

                return ValidateIntegerTermSafety(stringStartsWithFormula.Prefix, facts);
            case SmtStringEndsWithFormula stringEndsWithFormula:
                var endsWithValueStatus = ValidateIntegerTermSafety(stringEndsWithFormula.Value, facts);
                if (endsWithValueStatus != ConcreteFactPreparationStatus.Ready) return endsWithValueStatus;

                return ValidateIntegerTermSafety(stringEndsWithFormula.Suffix, facts);
            case SmtRegexMatchFormula regexMatchFormula:
                return ValidateIntegerTermSafety(regexMatchFormula.Value, facts);
            case SmtRuntimeTypeTestFormula runtimeTypeTest:
                return ValidateIntegerTermSafety(runtimeTypeTest.Value, facts);
            case SmtConditionalFormula conditionalFormula:
                var conditionStatus = ValidateIntegerTermSafety(conditionalFormula.Condition, facts);
                if (conditionStatus != ConcreteFactPreparationStatus.Ready) return conditionStatus;

                if (TryEvaluateConcreteBoolean(conditionalFormula.Condition, facts, out var selectedBranch))
                    return ValidateIntegerTermSafety(
                        selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                        facts);

                var trueStatus = ValidateIntegerTermSafety(conditionalFormula.WhenTrue, facts);
                if (trueStatus != ConcreteFactPreparationStatus.Ready) return trueStatus;

                return ValidateIntegerTermSafety(conditionalFormula.WhenFalse, facts);
            default:
                return ConcreteFactPreparationStatus.Ready;
        }
    }

    private static bool TryIntegerIntervalExcludesZero(SmtFormula formula, ConcreteFactContext facts)
    {
        if (TryGetIntegerInterval(formula, facts, out var lower, out var upper))
            return (lower.HasValue && lower.Value > 0) ||
                   (upper.HasValue && upper.Value < 0);

        return facts.IntegerBounds.TryGetValue(formula, out var bounds) &&
               bounds.ExcludesZero;
    }

    private static bool TryGetIntegerInterval(
        SmtFormula formula,
        ConcreteFactContext facts,
        out long? lower,
        out long? upper)
    {
        lower = null;
        upper = null;

        if (TryEvaluateInteger(formula, facts, out var concrete))
        {
            lower = concrete;
            upper = concrete;
            return true;
        }

        var foundInterval = false;
        if (facts.IntegerBounds.TryGetValue(formula, out var bounds))
        {
            lower = bounds.Lower;
            upper = bounds.Upper;
            foundInterval = lower.HasValue || upper.HasValue;
        }

        long? structuralLower = null;
        long? structuralUpper = null;
        var foundStructuralInterval = false;
        switch (formula)
        {
            case SmtStringLengthTerm stringLengthTerm:
                foundStructuralInterval = TryGetStringLengthInterval(
                    stringLengthTerm.Value,
                    facts,
                    out structuralLower,
                    out structuralUpper);
                break;
            case SmtIntegerUnaryTerm { Operator: SmtIntegerUnaryOperator.Negate } unaryTerm:
                if (!TryGetIntegerInterval(unaryTerm.Operand, facts, out var operandLower, out var operandUpper)) break;

                if (operandUpper.HasValue)
                {
                    if (!TryCheckedNegate(operandUpper.Value, out var negatedUpper)) break;

                    structuralLower = negatedUpper;
                }

                if (operandLower.HasValue)
                {
                    if (!TryCheckedNegate(operandLower.Value, out var negatedLower)) break;

                    structuralUpper = negatedLower;
                }

                foundStructuralInterval = true;
                break;
            case SmtIntegerBinaryTerm binaryTerm:
                foundStructuralInterval = TryGetIntegerBinaryInterval(
                    binaryTerm,
                    facts,
                    out structuralLower,
                    out structuralUpper);
                break;
        }

        if (foundStructuralInterval)
        {
            if (structuralLower.HasValue && (!lower.HasValue || structuralLower.Value > lower.Value))
                lower = structuralLower.Value;

            if (structuralUpper.HasValue && (!upper.HasValue || structuralUpper.Value < upper.Value))
                upper = structuralUpper.Value;

            foundInterval = foundInterval || structuralLower.HasValue || structuralUpper.HasValue;
        }

        return foundInterval;
    }

    private static bool TryGetStringLengthInterval(
        SmtFormula value,
        ConcreteFactContext facts,
        out long? lower,
        out long? upper)
    {
        lower = 0;
        upper = null;

        if (TryGetConcreteString(value, facts, out var concrete))
        {
            lower = concrete.Length;
            upper = concrete.Length;
            return true;
        }

        if (value is SmtStringConcatTerm concat)
        {
            if (!TryGetStringLengthInterval(concat.Left, facts, out var leftLower, out var leftUpper) ||
                !TryGetStringLengthInterval(concat.Right, facts, out var rightLower, out var rightUpper))
                return false;

            return TryCombineBounds(leftLower, rightLower, TryCheckedAdd, out lower) &&
                   TryCombineBounds(leftUpper, rightUpper, TryCheckedAdd, out upper);
        }

        return value.Kind == SmtValueKind.String;
    }

    private static bool TryGetIntegerBinaryInterval(
        SmtIntegerBinaryTerm term,
        ConcreteFactContext facts,
        out long? lower,
        out long? upper)
    {
        lower = null;
        upper = null;
        if (!TryGetIntegerInterval(term.Left, facts, out var leftLower, out var leftUpper) ||
            !TryGetIntegerInterval(term.Right, facts, out var rightLower, out var rightUpper))
            return false;

        switch (term.Operator)
        {
            case SmtIntegerBinaryOperator.Add:
                return TryCombineBounds(leftLower, rightLower, TryCheckedAdd, out lower) &&
                       TryCombineBounds(leftUpper, rightUpper, TryCheckedAdd, out upper);
            case SmtIntegerBinaryOperator.Subtract:
                return TryCombineBounds(leftLower, rightUpper, TryCheckedSubtract, out lower) &&
                       TryCombineBounds(leftUpper, rightLower, TryCheckedSubtract, out upper);
            case SmtIntegerBinaryOperator.Multiply:
                if (TryEvaluateInteger(term.Left, facts, out var leftConstant))
                    return TryScaleBounds(rightLower, rightUpper, leftConstant, out lower, out upper);

                if (TryEvaluateInteger(term.Right, facts, out var rightConstant))
                    return TryScaleBounds(leftLower, leftUpper, rightConstant, out lower, out upper);

                return false;
            case SmtIntegerBinaryOperator.Remainder:
                if (!HasNonNegativeDividendAndPositiveDivisor(leftLower, rightLower)) return false;

                lower = 0;
                if (rightUpper.HasValue &&
                    TryCheckedAdd(rightUpper.Value, -1, out var remainderUpper))
                    upper = remainderUpper;

                return true;
            default:
                return false;
        }
    }

    private static bool HasNonNegativeDividendAndPositiveDivisor(long? dividendLower, long? divisorLower)
    {
        return dividendLower.HasValue &&
               dividendLower.Value >= 0 &&
               divisorLower.HasValue &&
               divisorLower.Value > 0;
    }

    private static bool TryCombineBounds(
        long? left,
        long? right,
        CheckedLongBinaryOperation operation,
        out long? value)
    {
        value = null;
        if (!left.HasValue || !right.HasValue) return true;

        if (!operation(left.Value, right.Value, out var combined)) return false;

        value = combined;
        return true;
    }

    private static bool TryScaleBounds(
        long? lower,
        long? upper,
        long multiplier,
        out long? scaledLower,
        out long? scaledUpper)
    {
        scaledUpper = null;
        if (multiplier == 0)
        {
            scaledLower = 0;
            scaledUpper = 0;
            return true;
        }

        if (multiplier > 0)
            return TryScaleBound(lower, multiplier, out scaledLower) &&
                   TryScaleBound(upper, multiplier, out scaledUpper);

        return TryScaleBound(upper, multiplier, out scaledLower) &&
               TryScaleBound(lower, multiplier, out scaledUpper);
    }

    private static bool TryScaleBound(long? bound, long multiplier, out long? scaled)
    {
        scaled = null;
        if (!bound.HasValue) return true;

        if (!TryCheckedMultiply(bound.Value, multiplier, out var scaledValue)) return false;

        scaled = scaledValue;
        return true;
    }

    private static bool TryEvaluateConcreteBoolean(
        SmtFormula formula,
        ConcreteFactContext facts,
        out bool value)
    {
        switch (formula)
        {
            case SmtBooleanConstant booleanConstant:
                value = booleanConstant.Value;
                return true;
            case SmtUnaryFormula { Operator: SmtUnaryOperator.Not } unaryFormula
                when TryEvaluateConcreteBoolean(unaryFormula.Operand, facts, out var operand):
                value = !operand;
                return true;
            case SmtBinaryFormula binaryFormula:
                return TryEvaluateConcreteBinaryBoolean(binaryFormula, facts, out value);
            case SmtStringContainsFormula or SmtStringStartsWithFormula or SmtStringEndsWithFormula:
                if (TryGetPositiveStringPredicateFact(formula, out var predicate) &&
                    TryGetConcreteString(predicate.Value, facts, out var concreteValue) &&
                    TryGetConcreteString(predicate.Argument, facts, out var concreteArgument))
                {
                    value = EvaluateStringPredicate(predicate.Kind, concreteValue, concreteArgument);
                    return true;
                }

                break;
            case SmtConditionalFormula { Kind: SmtValueKind.Bool } conditionalFormula:
                if (TryEvaluateConcreteBoolean(conditionalFormula.Condition, facts, out var selectedBranch))
                    return TryEvaluateConcreteBoolean(
                        selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                        facts,
                        out value);

                break;
        }

        return facts.BooleanEqualities.TryGetValue(formula, out value);
    }

    private static bool ShouldPreserveSourceFact(SmtFormula formula)
    {
        if (formula is not SmtBinaryFormula binaryFormula ||
            !IsIntegerComparisonOperator(binaryFormula.Operator))
            return false;

        if (IsLiteral(binaryFormula.Left) && IsLiteral(binaryFormula.Right)) return false;

        return binaryFormula.Left.Kind is SmtValueKind.Int or SmtValueKind.String or SmtValueKind.Reference ||
               binaryFormula.Right.Kind is SmtValueKind.Int or SmtValueKind.String or SmtValueKind.Reference;
    }

    private static bool IsLiteral(SmtFormula formula)
    {
        return formula is SmtBooleanConstant or
            SmtIntegerConstant or
            SmtStringConstant or
            SmtNullConstant;
    }

    private static bool TryEvaluateConcreteBinaryBoolean(
        SmtBinaryFormula formula,
        ConcreteFactContext facts,
        out bool value)
    {
        if (formula.Operator == SmtBinaryOperator.And)
        {
            if (TryEvaluateConcreteBoolean(formula.Left, facts, out var left))
            {
                if (!left)
                {
                    value = false;
                    return true;
                }

                if (TryEvaluateConcreteBoolean(formula.Right, facts, out var right))
                {
                    value = right;
                    return true;
                }
            }

            value = false;
            return false;
        }

        if (formula.Operator == SmtBinaryOperator.Or)
        {
            if (TryEvaluateConcreteBoolean(formula.Left, facts, out var left))
            {
                if (left)
                {
                    value = true;
                    return true;
                }

                if (TryEvaluateConcreteBoolean(formula.Right, facts, out var right))
                {
                    value = right;
                    return true;
                }
            }

            value = false;
            return false;
        }

        if (TryEvaluateStringLengthComparison(formula, facts, out value)) return true;

        if (formula.Left.Kind == SmtValueKind.Int &&
            formula.Right.Kind == SmtValueKind.Int &&
            TryEvaluateIntegerIntervalComparison(formula, facts, out value))
            return true;

        if (formula.Left.Kind == SmtValueKind.Int &&
            formula.Right.Kind == SmtValueKind.Int &&
            TryEvaluateInteger(formula.Left, facts, out var leftInteger) &&
            TryEvaluateInteger(formula.Right, facts, out var rightInteger))
        {
            value = CompareIntegers(formula.Operator, leftInteger, rightInteger);
            return true;
        }

        if (formula.Left.Kind == SmtValueKind.String &&
            formula.Right.Kind == SmtValueKind.String &&
            TryGetConcreteString(formula.Left, facts, out var leftString) &&
            TryGetConcreteString(formula.Right, facts, out var rightString))
        {
            value = CompareEquality(formula.Operator, string.Equals(leftString, rightString, StringComparison.Ordinal));
            return formula.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual;
        }

        if (formula.Left.Kind == SmtValueKind.Reference &&
            formula.Right.Kind == SmtValueKind.Reference &&
            formula.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual)
        {
            if (TryEvaluateReferenceNull(formula.Left, facts, out var leftIsNull) &&
                TryEvaluateReferenceNull(formula.Right, facts, out var rightIsNull) &&
                (leftIsNull || rightIsNull))
            {
                value = CompareEquality(formula.Operator, leftIsNull && rightIsNull);
                return true;
            }

            if (EqualityComparer<SmtFormula>.Default.Equals(formula.Left, formula.Right))
            {
                value = formula.Operator == SmtBinaryOperator.Equal;
                return true;
            }
        }

        if (formula.Left.Kind == SmtValueKind.Bool &&
            formula.Right.Kind == SmtValueKind.Bool &&
            TryEvaluateConcreteBoolean(formula.Left, facts, out var leftBoolean) &&
            TryEvaluateConcreteBoolean(formula.Right, facts, out var rightBoolean))
        {
            value = CompareEquality(formula.Operator, leftBoolean == rightBoolean);
            return formula.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual;
        }

        if (formula.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual &&
            ((formula.Left is SmtNullConstant && formula.Right is SmtNullConstant) ||
             EqualityComparer<SmtFormula>.Default.Equals(formula.Left, formula.Right)))
        {
            value = formula.Operator == SmtBinaryOperator.Equal;
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryEvaluateStringLengthComparison(
        SmtBinaryFormula formula,
        ConcreteFactContext facts,
        out bool value)
    {
        if (!TryNormalizeStringLengthComparison(formula, out var stringValue, out var op, out var constant))
        {
            value = false;
            return false;
        }

        if (TryGetConcreteString(stringValue, facts, out var concreteString))
        {
            value = CompareIntegers(op, concreteString.Length, constant);
            return true;
        }

        value = op switch
        {
            SmtBinaryOperator.Equal => constant < 0 ? false : default,
            SmtBinaryOperator.NotEqual => constant < 0 ? true : default,
            SmtBinaryOperator.LessThan => constant <= 0 ? false : default,
            SmtBinaryOperator.LessThanOrEqual => constant < 0 ? false : default,
            SmtBinaryOperator.GreaterThan => constant < 0 ? true : default,
            SmtBinaryOperator.GreaterThanOrEqual => constant <= 0 ? true : default,
            _ => default
        };

        return op switch
        {
            SmtBinaryOperator.Equal => constant < 0,
            SmtBinaryOperator.NotEqual => constant < 0,
            SmtBinaryOperator.LessThan => constant <= 0,
            SmtBinaryOperator.LessThanOrEqual => constant < 0,
            SmtBinaryOperator.GreaterThan => constant < 0,
            SmtBinaryOperator.GreaterThanOrEqual => constant <= 0,
            _ => false
        };
    }

    private static bool TryEvaluateIntegerIntervalComparison(
        SmtBinaryFormula formula,
        ConcreteFactContext facts,
        out bool value)
    {
        value = false;
        if (!IsIntegerComparisonOperator(formula.Operator)) return false;

        if (TryEvaluateRemainderRangeComparison(formula, facts, out value)) return true;

        if (!TryGetIntegerInterval(formula.Left, facts, out var leftLower, out var leftUpper) ||
            !TryGetIntegerInterval(formula.Right, facts, out var rightLower, out var rightUpper))
            return false;

        if (IntervalIsInconsistent(leftLower, leftUpper) ||
            IntervalIsInconsistent(rightLower, rightUpper))
        {
            value = false;
            return true;
        }

        switch (formula.Operator)
        {
            case SmtBinaryOperator.Equal:
                if (leftLower.HasValue &&
                    leftUpper.HasValue &&
                    rightLower.HasValue &&
                    rightUpper.HasValue &&
                    leftLower.Value == leftUpper.Value &&
                    rightLower.Value == rightUpper.Value)
                {
                    value = leftLower.Value == rightLower.Value;
                    return true;
                }

                if (IntervalsAreDisjoint(leftLower, leftUpper, rightLower, rightUpper))
                {
                    value = false;
                    return true;
                }

                return false;
            case SmtBinaryOperator.NotEqual:
                if (IntervalsAreDisjoint(leftLower, leftUpper, rightLower, rightUpper))
                {
                    value = true;
                    return true;
                }

                if (leftLower.HasValue &&
                    leftUpper.HasValue &&
                    rightLower.HasValue &&
                    rightUpper.HasValue &&
                    leftLower.Value == leftUpper.Value &&
                    rightLower.Value == rightUpper.Value)
                {
                    value = leftLower.Value != rightLower.Value;
                    return true;
                }

                return false;
            case SmtBinaryOperator.LessThan:
                if (leftUpper.HasValue && rightLower.HasValue && leftUpper.Value < rightLower.Value)
                {
                    value = true;
                    return true;
                }

                if (leftLower.HasValue && rightUpper.HasValue && leftLower.Value >= rightUpper.Value)
                {
                    value = false;
                    return true;
                }

                return false;
            case SmtBinaryOperator.LessThanOrEqual:
                if (leftUpper.HasValue && rightLower.HasValue && leftUpper.Value <= rightLower.Value)
                {
                    value = true;
                    return true;
                }

                if (leftLower.HasValue && rightUpper.HasValue && leftLower.Value > rightUpper.Value)
                {
                    value = false;
                    return true;
                }

                return false;
            case SmtBinaryOperator.GreaterThan:
                if (leftLower.HasValue && rightUpper.HasValue && leftLower.Value > rightUpper.Value)
                {
                    value = true;
                    return true;
                }

                if (leftUpper.HasValue && rightLower.HasValue && leftUpper.Value <= rightLower.Value)
                {
                    value = false;
                    return true;
                }

                return false;
            case SmtBinaryOperator.GreaterThanOrEqual:
                if (leftLower.HasValue && rightUpper.HasValue && leftLower.Value >= rightUpper.Value)
                {
                    value = true;
                    return true;
                }

                if (leftUpper.HasValue && rightLower.HasValue && leftUpper.Value < rightLower.Value)
                {
                    value = false;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static bool TryEvaluateRemainderRangeComparison(
        SmtBinaryFormula formula,
        ConcreteFactContext facts,
        out bool value)
    {
        if (formula.Left is SmtIntegerBinaryTerm leftRemainder &&
            TryEvaluateRemainderComparison(leftRemainder, formula.Operator, formula.Right, facts, out value))
            return true;

        if (formula.Right is SmtIntegerBinaryTerm rightRemainder &&
            TryEvaluateRemainderComparison(
                rightRemainder,
                SwapComparisonOperator(formula.Operator),
                formula.Left,
                facts,
                out value))
            return true;

        value = false;
        return false;
    }

    private static bool TryEvaluateRemainderComparison(
        SmtIntegerBinaryTerm remainder,
        SmtBinaryOperator op,
        SmtFormula other,
        ConcreteFactContext facts,
        out bool value)
    {
        value = false;
        if (remainder.Operator != SmtIntegerBinaryOperator.Remainder ||
            !TryGetIntegerInterval(remainder.Left, facts, out var dividendLower, out _) ||
            !TryGetIntegerInterval(remainder.Right, facts, out var divisorLower, out _) ||
            !HasNonNegativeDividendAndPositiveDivisor(dividendLower, divisorLower) ||
            !EqualityComparer<SmtFormula>.Default.Equals(other, remainder.Right))
            return false;

        switch (op)
        {
            case SmtBinaryOperator.LessThan:
            case SmtBinaryOperator.LessThanOrEqual:
            case SmtBinaryOperator.NotEqual:
                value = true;
                return true;
            case SmtBinaryOperator.Equal:
            case SmtBinaryOperator.GreaterThan:
            case SmtBinaryOperator.GreaterThanOrEqual:
                value = false;
                return true;
            default:
                return false;
        }
    }

    private static bool IntervalsAreDisjoint(
        long? leftLower,
        long? leftUpper,
        long? rightLower,
        long? rightUpper)
    {
        return (leftUpper.HasValue && rightLower.HasValue && leftUpper.Value < rightLower.Value) ||
               (rightUpper.HasValue && leftLower.HasValue && rightUpper.Value < leftLower.Value);
    }

    private static bool IntervalIsInconsistent(long? lower, long? upper)
    {
        return lower.HasValue && upper.HasValue && lower.Value > upper.Value;
    }

    private static bool TryNormalizeStringLengthComparison(
        SmtBinaryFormula formula,
        out SmtFormula stringValue,
        out SmtBinaryOperator op,
        out long constant)
    {
        if (formula.Left is SmtStringLengthTerm leftLength &&
            formula.Right is SmtIntegerConstant rightConstant)
        {
            stringValue = leftLength.Value;
            op = formula.Operator;
            constant = rightConstant.Value;
            return IsIntegerComparisonOperator(op);
        }

        if (formula.Left is SmtIntegerConstant leftConstant &&
            formula.Right is SmtStringLengthTerm rightLength)
        {
            stringValue = rightLength.Value;
            op = SwapComparisonOperator(formula.Operator);
            constant = leftConstant.Value;
            return IsIntegerComparisonOperator(op);
        }

        stringValue = null!;
        op = default;
        constant = default;
        return false;
    }

    private static bool TryEvaluateInteger(
        SmtFormula formula,
        ConcreteFactContext facts,
        out long value)
    {
        try
        {
            if (formula is SmtIntegerConstant integerConstant)
            {
                value = integerConstant.Value;
                return true;
            }

            if (facts.IntegerEqualities.TryGetValue(formula, out value)) return true;

            switch (formula)
            {
                case SmtIntegerUnaryTerm { Operator: SmtIntegerUnaryOperator.Negate } unaryTerm
                    when TryEvaluateInteger(unaryTerm.Operand, facts, out var operand):
                    value = checked(-operand);
                    return true;
                case SmtIntegerBinaryTerm binaryTerm:
                    return TryEvaluateIntegerBinary(binaryTerm, facts, out value);
                case SmtConditionalFormula { Kind: SmtValueKind.Int } conditionalFormula:
                    if (TryEvaluateConcreteBoolean(conditionalFormula.Condition, facts, out var selectedBranch))
                        return TryEvaluateInteger(
                            selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                            facts,
                            out value);

                    value = default;
                    return false;
                default:
                    value = default;
                    return false;
            }
        }
        catch (OverflowException)
        {
            value = default;
            return false;
        }
        catch (DivideByZeroException)
        {
            value = default;
            return false;
        }
    }

    private static bool TryEvaluateIntegerBinary(
        SmtIntegerBinaryTerm term,
        ConcreteFactContext facts,
        out long value)
    {
        value = default;
        if (!TryEvaluateInteger(term.Left, facts, out var left) ||
            !TryEvaluateInteger(term.Right, facts, out var right))
            return false;

        value = term.Operator switch
        {
            SmtIntegerBinaryOperator.Add => checked(left + right),
            SmtIntegerBinaryOperator.Subtract => checked(left - right),
            SmtIntegerBinaryOperator.Multiply => checked(left * right),
            SmtIntegerBinaryOperator.Divide => checked(left / right),
            SmtIntegerBinaryOperator.Remainder => checked(left % right),
            _ => default
        };
        return true;
    }

    private static bool CompareIntegers(SmtBinaryOperator op, long left, long right)
    {
        return op switch
        {
            SmtBinaryOperator.Equal => left == right,
            SmtBinaryOperator.NotEqual => left != right,
            SmtBinaryOperator.LessThan => left < right,
            SmtBinaryOperator.LessThanOrEqual => left <= right,
            SmtBinaryOperator.GreaterThan => left > right,
            SmtBinaryOperator.GreaterThanOrEqual => left >= right,
            _ => false
        };
    }

    private static bool CompareEquality(SmtBinaryOperator op, bool equality)
    {
        return op switch
        {
            SmtBinaryOperator.Equal => equality,
            SmtBinaryOperator.NotEqual => !equality,
            _ => false
        };
    }

    private static bool TryEvaluateReferenceNull(
        SmtFormula formula,
        ConcreteFactContext facts,
        out bool isNull)
    {
        if (formula is SmtNullConstant)
        {
            isNull = true;
            return true;
        }

        if (facts.ReferenceNullEqualities.TryGetValue(formula, out isNull)) return true;

        if (formula is SmtConditionalFormula { Kind: SmtValueKind.Reference } conditionalFormula &&
            TryEvaluateConcreteBoolean(conditionalFormula.Condition, facts, out var selectedBranch))
            return TryEvaluateReferenceNull(
                selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                facts,
                out isNull);

        isNull = false;
        return false;
    }

    private static bool TryCollectStringEqualities(
        IReadOnlyList<SmtFormula> conditions,
        ConcreteFactContext facts)
    {
        var iterationLimit = Math.Max(1, conditions.Count * 4);
        var changed = false;
        do
        {
            changed = false;
            foreach (var condition in conditions)
                if (!TryCollectStringEqualities(condition, facts, ref changed))
                    return false;

            iterationLimit--;
        } while (changed && iterationLimit > 0);

        return true;
    }

    private static bool TryCollectStringEqualities(
        SmtFormula formula,
        ConcreteFactContext facts,
        ref bool changed)
    {
        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            return TryCollectStringEqualities(andFormula.Left, facts, ref changed) &&
                   TryCollectStringEqualities(andFormula.Right, facts, ref changed);

        if (formula is not SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } equalFormula) return true;

        if (TryGetConcreteString(equalFormula.Left, facts, out var leftValue) &&
            TryGetConcreteString(equalFormula.Right, facts, out var rightValue))
            return string.Equals(leftValue, rightValue, StringComparison.Ordinal);

        if (equalFormula.Left is SmtStringConstant leftConstant)
            return TryAddStringEquality(facts, equalFormula.Right, leftConstant.Value, ref changed);

        if (equalFormula.Right is SmtStringConstant rightConstant)
            return TryAddStringEquality(facts, equalFormula.Left, rightConstant.Value, ref changed);

        if (TryGetConcreteString(equalFormula.Left, facts, out leftValue))
            return TryAddStringEquality(facts, equalFormula.Right, leftValue, ref changed);

        if (TryGetConcreteString(equalFormula.Right, facts, out rightValue))
            return TryAddStringEquality(facts, equalFormula.Left, rightValue, ref changed);

        return true;
    }

    private static bool TryAddStringEquality(
        ConcreteFactContext facts,
        SmtFormula formula,
        string value,
        ref bool changed)
    {
        if (facts.StringEqualities.TryGetValue(formula, out var existing))
            return string.Equals(existing, value, StringComparison.Ordinal);

        facts.StringEqualities.Add(formula, value);
        changed = true;
        return true;
    }

    private static bool TryAddStringEquality(
        ConcreteFactContext facts,
        SmtFormula formula,
        string value)
    {
        var changed = false;
        return TryAddStringEquality(facts, formula, value, ref changed);
    }

    private static bool TryCollectStringLengthEqualities(
        SmtFormula formula,
        Dictionary<SmtFormula, long> stringLengthEqualities)
    {
        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            return TryCollectStringLengthEqualities(andFormula.Left, stringLengthEqualities) &&
                   TryCollectStringLengthEqualities(andFormula.Right, stringLengthEqualities);

        if (!TryGetStringLengthEquality(formula, out var value, out var length)) return true;

        if (length < 0) return false;

        if (stringLengthEqualities.TryGetValue(value, out var existing)) return existing == length;

        stringLengthEqualities.Add(value, length);
        return true;
    }

    private static bool TryGetStringLengthEquality(
        SmtFormula formula,
        out SmtFormula value,
        out long length)
    {
        value = null!;
        length = default;
        if (formula is not SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } equalFormula) return false;

        if (equalFormula.Left is SmtStringLengthTerm leftLength &&
            equalFormula.Right is SmtIntegerConstant rightConstant)
        {
            value = leftLength.Value;
            length = rightConstant.Value;
            return true;
        }

        if (equalFormula.Left is SmtIntegerConstant leftConstant &&
            equalFormula.Right is SmtStringLengthTerm rightLength)
        {
            value = rightLength.Value;
            length = leftConstant.Value;
            return true;
        }

        return false;
    }

    private static ConcreteFactPreparationStatus TryInferStringEqualitiesFromLengthConstrainedPredicates(
        SmtFormula formula,
        IReadOnlyDictionary<SmtFormula, long> stringLengthEqualities,
        ConcreteFactContext facts)
    {
        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
        {
            var leftStatus = TryInferStringEqualitiesFromLengthConstrainedPredicates(
                andFormula.Left,
                stringLengthEqualities,
                facts);
            if (leftStatus != ConcreteFactPreparationStatus.Ready) return leftStatus;

            return TryInferStringEqualitiesFromLengthConstrainedPredicates(
                andFormula.Right,
                stringLengthEqualities,
                facts);
        }

        if (!TryGetPositiveStringPredicateFact(formula, out var predicate) ||
            !stringLengthEqualities.TryGetValue(predicate.Value, out var knownLength) ||
            !TryGetConcreteString(predicate.Argument, facts, out var concreteArgument))
            return ConcreteFactPreparationStatus.Ready;

        if (knownLength < concreteArgument.Length) return ConcreteFactPreparationStatus.Unsatisfiable;

        if (knownLength == concreteArgument.Length)
            if (!TryAddStringEquality(facts, predicate.Value, concreteArgument))
                return ConcreteFactPreparationStatus.Unsatisfiable;

        return ConcreteFactPreparationStatus.Ready;
    }

    private static ConcreteFactPreparationStatus TryApplyStringShapeFacts(
        IReadOnlyList<SmtFormula> conditions,
        IReadOnlyDictionary<SmtFormula, long> stringLengthEqualities,
        ConcreteFactContext facts)
    {
        var shapeFacts = new Dictionary<SmtFormula, StringShapeFact>();
        foreach (var condition in conditions)
        {
            var status = TryCollectStringShapeFacts(condition, facts, shapeFacts);
            if (status != ConcreteFactPreparationStatus.Ready) return status;
        }

        foreach (var entry in shapeFacts)
        {
            var value = entry.Key;
            var shape = entry.Value;
            long? exactLength = null;
            if (stringLengthEqualities.TryGetValue(value, out var knownLength))
                exactLength = knownLength;
            else if (TryGetConcreteString(value, facts, out var concreteValue)) exactLength = concreteValue.Length;

            if (exactLength.HasValue)
            {
                if (shape.MinLength > exactLength.Value) return ConcreteFactPreparationStatus.Unsatisfiable;

                if (!TryApplyExactLengthStringShape(value, exactLength.Value, shape, facts))
                    return ConcreteFactPreparationStatus.Unsatisfiable;
            }
        }

        return ConcreteFactPreparationStatus.Ready;
    }

    private static ConcreteFactPreparationStatus TryCollectStringShapeFacts(
        SmtFormula formula,
        ConcreteFactContext facts,
        Dictionary<SmtFormula, StringShapeFact> shapeFacts)
    {
        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
        {
            var leftStatus = TryCollectStringShapeFacts(andFormula.Left, facts, shapeFacts);
            if (leftStatus != ConcreteFactPreparationStatus.Ready) return leftStatus;

            return TryCollectStringShapeFacts(andFormula.Right, facts, shapeFacts);
        }

        if (!TryGetPositiveStringPredicateFact(formula, out var predicate) ||
            !TryGetConcreteString(predicate.Argument, facts, out var argument))
            return ConcreteFactPreparationStatus.Ready;

        var shape = shapeFacts.TryGetValue(predicate.Value, out var existing)
            ? existing
            : default;

        var status = predicate.Kind switch
        {
            StringPredicateKind.Contains => shape.AddContains(argument),
            StringPredicateKind.StartsWith => shape.AddPrefix(argument),
            StringPredicateKind.EndsWith => shape.AddSuffix(argument),
            _ => ConcreteFactPreparationStatus.Ready
        };

        if (status != ConcreteFactPreparationStatus.Ready) return status;

        shapeFacts[predicate.Value] = shape;
        return ConcreteFactPreparationStatus.Ready;
    }

    private static bool TryApplyExactLengthStringShape(
        SmtFormula value,
        long exactLength,
        StringShapeFact shape,
        ConcreteFactContext facts)
    {
        if (exactLength > int.MaxValue) return true;

        var length = (int)exactLength;
        var prefix = shape.Prefix;
        var suffix = shape.Suffix;
        if (prefix is not null &&
            prefix.Length != 0 &&
            prefix.Length == length)
            return TryAddStringEquality(facts, value, prefix);

        if (suffix is not null &&
            suffix.Length != 0 &&
            suffix.Length == length)
            return TryAddStringEquality(facts, value, suffix);

        if (prefix is not null &&
            suffix is not null &&
            prefix.Length != 0 &&
            suffix.Length != 0 &&
            prefix.Length + suffix.Length >= length)
        {
            var characters = new char?[length];
            if (!TryOverlayString(characters, 0, prefix) ||
                !TryOverlayString(characters, length - suffix.Length, suffix))
                return false;

            if (characters.All(static c => c.HasValue))
                return TryAddStringEquality(
                    facts,
                    value,
                    new string(characters.Select(static c => c!.Value).ToArray()));
        }

        return true;
    }

    private static bool TryOverlayString(char?[] target, int start, string value)
    {
        if (start < 0 ||
            start + value.Length > target.Length)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            var index = start + i;
            if (target[index].HasValue && target[index]!.Value != value[i]) return false;

            target[index] = value[i];
        }

        return true;
    }

    private ConcreteFactPreparationStatus SimplifyConcreteFacts(
        SmtFormula formula,
        ConcreteFactContext facts,
        out SmtFormula preparedFormula,
        out bool changed)
    {
        preparedFormula = formula;
        changed = false;

        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
        {
            var leftStatus = SimplifyConcreteFacts(
                andFormula.Left,
                facts,
                out var left,
                out var leftChanged);
            if (leftStatus != ConcreteFactPreparationStatus.Ready) return leftStatus;

            var rightStatus = SimplifyConcreteFacts(
                andFormula.Right,
                facts,
                out var right,
                out var rightChanged);
            if (rightStatus != ConcreteFactPreparationStatus.Ready) return rightStatus;

            changed = leftChanged || rightChanged;
            if (left is SmtBooleanConstant { Value: false } ||
                right is SmtBooleanConstant { Value: false })
            {
                preparedFormula = new SmtBooleanConstant(false);
                changed = true;
                return ConcreteFactPreparationStatus.Ready;
            }

            if (left is SmtBooleanConstant { Value: true })
            {
                preparedFormula = right;
                changed = true;
                return ConcreteFactPreparationStatus.Ready;
            }

            if (right is SmtBooleanConstant { Value: true })
            {
                preparedFormula = left;
                changed = true;
                return ConcreteFactPreparationStatus.Ready;
            }

            if (changed) preparedFormula = new SmtBinaryFormula(SmtBinaryOperator.And, left, right);

            return ConcreteFactPreparationStatus.Ready;
        }

        if (TryEvaluateConcreteBoolean(formula, facts, out var concreteBoolean))
        {
            if (concreteBoolean && ShouldPreserveSourceFact(formula)) return ConcreteFactPreparationStatus.Ready;

            preparedFormula = new SmtBooleanConstant(concreteBoolean);
            changed = true;
            return ConcreteFactPreparationStatus.Ready;
        }

        if (TryGetRegexFact(formula, out var regexMatch, out var expectedMatch) &&
            TryGetConcreteString(regexMatch.Value, facts, out var concreteInput))
        {
            if (!_regexValidator.TryValidate(
                    concreteInput,
                    regexMatch.Pattern,
                    regexMatch.Options,
                    out var actualMatch))
                return ConcreteFactPreparationStatus.Unknown;

            if (actualMatch != expectedMatch) return ConcreteFactPreparationStatus.Unsatisfiable;

            preparedFormula = new SmtBooleanConstant(true);
            changed = true;
        }

        return ConcreteFactPreparationStatus.Ready;
    }

    private static bool EvaluateStringPredicate(
        StringPredicateKind kind,
        string value,
        string argument)
    {
        return kind switch
        {
            StringPredicateKind.Contains => value.Contains(argument),
            StringPredicateKind.StartsWith => value.StartsWith(argument, StringComparison.Ordinal),
            StringPredicateKind.EndsWith => value.EndsWith(argument, StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool TryGetRegexFact(
        SmtFormula formula,
        out SmtRegexMatchFormula regexMatch,
        out bool expectedMatch)
    {
        return TryGetPolarizedFact(formula, TryGetPositiveRegexFact, out regexMatch, out expectedMatch);
    }

    private static bool TryGetPositiveRegexFact(SmtFormula formula, out SmtRegexMatchFormula regexMatch)
    {
        if (formula is SmtRegexMatchFormula match)
        {
            regexMatch = match;
            return true;
        }

        regexMatch = null!;
        return false;
    }

    private static bool TryGetPolarizedFact<TFact>(
        SmtFormula formula,
        TryGetPositiveFact<TFact> tryGetPositiveFact,
        out TFact fact,
        out bool expectedValue)
    {
        if (tryGetPositiveFact(formula, out fact))
        {
            expectedValue = true;
            return true;
        }

        if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } notFormula &&
            tryGetPositiveFact(notFormula.Operand, out fact))
        {
            expectedValue = false;
            return true;
        }

        fact = default!;
        expectedValue = false;
        return false;
    }

    private static bool TryGetPositiveStringPredicateFact(
        SmtFormula formula,
        out StringPredicateFact predicate)
    {
        switch (formula)
        {
            case SmtStringContainsFormula contains:
                predicate = new StringPredicateFact(StringPredicateKind.Contains, contains.Value, contains.Search);
                return true;
            case SmtStringStartsWithFormula startsWith:
                predicate = new StringPredicateFact(StringPredicateKind.StartsWith, startsWith.Value,
                    startsWith.Prefix);
                return true;
            case SmtStringEndsWithFormula endsWith:
                predicate = new StringPredicateFact(StringPredicateKind.EndsWith, endsWith.Value, endsWith.Suffix);
                return true;
            default:
                predicate = default;
                return false;
        }
    }

    private static bool TryGetConcreteString(
        SmtFormula formula,
        ConcreteFactContext facts,
        out string value)
    {
        if (formula is SmtStringConstant stringConstant)
        {
            value = stringConstant.Value;
            return true;
        }

        if (facts.StringEqualities.TryGetValue(formula, out var found))
        {
            value = found;
            return true;
        }

        if (formula is SmtStringConcatTerm stringConcatTerm &&
            TryGetConcreteString(stringConcatTerm.Left, facts, out var left) &&
            TryGetConcreteString(stringConcatTerm.Right, facts, out var right))
        {
            value = string.Concat(left, right);
            return true;
        }

        if (formula is SmtConditionalFormula { Kind: SmtValueKind.String } conditionalFormula &&
            TryEvaluateConcreteBoolean(conditionalFormula.Condition, facts, out var selectedBranch))
            return TryGetConcreteString(
                selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                facts,
                out value);

        value = string.Empty;
        return false;
    }

    private enum ConcreteFactPreparationStatus
    {
        Ready,
        Unsatisfiable,
        Unknown
    }

    private sealed class ConcreteFactContext
    {
        public Dictionary<SmtFormula, string> StringEqualities { get; } = new();

        public Dictionary<SmtFormula, long> IntegerEqualities { get; } = new();

        public Dictionary<SmtFormula, IntegerBounds> IntegerBounds { get; } = new();

        public Dictionary<SmtFormula, bool> BooleanEqualities { get; } = new();

        public Dictionary<SmtFormula, bool> ReferenceNullEqualities { get; } = new();
    }

    private struct IntegerBounds
    {
        public long? Lower;

        public long? Upper;

        public bool ExcludesZero;

        public bool IsUnsatisfiable =>
            (Lower.HasValue &&
             Upper.HasValue &&
             Lower.Value > Upper.Value) ||
            (ExcludesZero &&
             Lower.HasValue &&
             Upper.HasValue &&
             Lower.Value == 0 &&
             Upper.Value == 0);
    }

    private delegate bool CheckedLongBinaryOperation(long left, long right, out long value);

    private delegate bool TryGetPositiveFact<TFact>(SmtFormula formula, out TFact fact);

    private struct StringShapeFact
    {
        public string? Prefix;

        public string? Suffix;

        public long MinLength;

        public ConcreteFactPreparationStatus AddContains(string value)
        {
            return ApplyMinimumLength(value.Length);
        }

        public ConcreteFactPreparationStatus AddPrefix(string value)
        {
            return AddAffix(value, isPrefix: true);
        }

        public ConcreteFactPreparationStatus AddSuffix(string value)
        {
            return AddAffix(value, isPrefix: false);
        }

        private ConcreteFactPreparationStatus AddAffix(string value, bool isPrefix)
        {
            var current = isPrefix ? Prefix : Suffix;
            if (current != null && !AreCompatibleAffixes(current, value, isPrefix))
                return ConcreteFactPreparationStatus.Unsatisfiable;

            if (current == null || value.Length > current.Length)
            {
                if (isPrefix)
                    Prefix = value;
                else
                    Suffix = value;
            }

            return ApplyMinimumLength(value.Length);
        }

        private ConcreteFactPreparationStatus ApplyMinimumLength(int length)
        {
            if (length > MinLength) MinLength = length;

            return ConcreteFactPreparationStatus.Ready;
        }

        private static bool AreCompatibleAffixes(string left, string right, bool isPrefix)
        {
            var minLength = Math.Min(left.Length, right.Length);
            var leftStart = isPrefix ? 0 : left.Length - minLength;
            var rightStart = isPrefix ? 0 : right.Length - minLength;
            return string.Equals(
                left.Substring(leftStart, minLength),
                right.Substring(rightStart, minLength),
                StringComparison.Ordinal);
        }
    }

    private static IReadOnlyList<SmtVariable> CollectVariables(IEnumerable<SmtFormula> formulas)
    {
        var variables = new HashSet<SmtVariable>();
        foreach (var formula in formulas)
            foreach (var candidate in SmtFormulaTraversal.Enumerate(formula))
                if (candidate is SmtVariable variable)
                    variables.Add(variable);

        return variables.ToArray();
    }

    private enum StringPredicateKind
    {
        Contains,
        StartsWith,
        EndsWith
    }

    private readonly struct StringPredicateFact
    {
        public StringPredicateFact(StringPredicateKind kind, SmtFormula value, SmtFormula argument)
        {
            Kind = kind;
            Value = value;
            Argument = argument;
        }

        public StringPredicateKind Kind { get; }

        public SmtFormula Value { get; }

        public SmtFormula Argument { get; }
    }

}
