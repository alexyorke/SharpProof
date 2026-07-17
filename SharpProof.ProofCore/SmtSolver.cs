using System.Diagnostics;
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
    private readonly SmtConcreteFactPreprocessor _preprocessor = new();
    private long _lastObservedRlimitCount;

    /// <summary>
    ///     Total Z3 rlimit units consumed by checks on this solver instance. Grows
    ///     deterministically with solver work, so callers can enforce cumulative
    ///     budgets that do not depend on machine speed or load.
    /// </summary>
    public long ConsumedResourceCount { get; private set; }

    internal int RegexValidationCacheCount => _preprocessor.RegexValidationCacheCount;

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
            // smaller observation means the 32-bit counter wrapped - count the
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
        var query = Prepare(pathConditions);
        if (query.Status != SmtConcreteFactPreparationStatus.Ready)
            return query.Status == SmtConcreteFactPreparationStatus.Unsatisfiable
                ? Feasibility.Unsatisfiable
                : Feasibility.Unknown;

        return IsSatisfiableRaw(query.Conditions, timeout, query.ContainsApproximateRegex);
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
        var query = Prepare(pathConditions);
        if (query.Status != SmtConcreteFactPreparationStatus.Ready)
            return query.Status == SmtConcreteFactPreparationStatus.Unsatisfiable
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

        if (query.WasChanged && !query.ContainsApproximateRegex)
            try
            {
                return CheckSatisfiabilityRawWithWitness(
                    query.OriginalConditions,
                    query.OriginalConditions,
                    timeout,
                    false,
                    adjustApproximation,
                    false);
            }
            catch (Exception ex) when (IsConservativeSolverFailure(ex))
            {
                // Exact concrete facts may still use operations that the encoder cannot represent.
                // The preparation pass already validated them, so continue with the reduced query.
            }

        try
        {
            return CheckSatisfiabilityRawWithWitness(
                query.Conditions,
                query.OriginalConditions,
                timeout,
                query.WasChanged,
                adjustApproximation,
                query.ContainsApproximateRegex);
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

    private Feasibility IsSatisfiableRaw(
        IEnumerable<SmtFormula> pathConditions,
        TimeSpan timeout,
        bool? containsApproximateRegex = null)
    {
        if (timeout <= TimeSpan.Zero) return Feasibility.Unknown;

        try
        {
            var conditions = pathConditions as SmtFormula[] ?? pathConditions.ToArray();
            var isApproximate = containsApproximateRegex ?? ContainsApproximateRegex(conditions);
            using var solver = _encoder.CreateSolver(timeout);
            foreach (var formula in conditions) solver.Assert(_encoder.EncodeCondition(formula));

            return AdjustForApproximation(ToFeasibility(CheckAndAccountResources(solver)), isApproximate);
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
        bool adjustApproximation,
        bool? containsApproximateRegex = null)
    {
        var isApproximate = containsApproximateRegex ?? ContainsApproximateRegex(conditions);
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

        var witnessStatus = isApproximate || preprocessedModel
            ? SmtWitnessStatus.Approximate
            : SmtWitnessStatus.Exact;
        var witnessReason = isApproximate
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
                ? AdjustForApproximation(feasibility, isApproximate)
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
        var pathQuery = Prepare(pathConditions);
        if (pathQuery.Status != SmtConcreteFactPreparationStatus.Ready)
            return (pathQuery.Status == SmtConcreteFactPreparationStatus.Unsatisfiable
                ? Feasibility.Unsatisfiable
                : Feasibility.Unknown, Feasibility.Unknown);

        if (timeout <= TimeSpan.Zero) return (Feasibility.Unknown, Feasibility.Unknown);

        try
        {
            using var solver = _encoder.CreateSolver(timeout);
            foreach (var formula in pathQuery.Conditions) solver.Assert(_encoder.EncodeCondition(formula));

            var pathFeasibility = ToFeasibility(CheckAndAccountResources(solver));
            if (pathFeasibility != Feasibility.Satisfiable) return (pathFeasibility, Feasibility.Unknown);

            // A SAT path under regex approximation is only "may be feasible"; still check the
            // combined query because UNSAT under the over-approximation remains a safe proof.
            //
            // Use the original path facts for the combined query. The path-only preparation pass
            // may remove equalities as already-satisfied facts, but those equalities can still be
            // required to prove the hazard condition unreachable.
            var combinedQuery = Prepare(pathQuery.OriginalConditions.Append(impurityCondition));
            if (combinedQuery.Status != SmtConcreteFactPreparationStatus.Ready)
                return (pathFeasibility, combinedQuery.Status == SmtConcreteFactPreparationStatus.Unsatisfiable
                    ? Feasibility.Unsatisfiable
                    : Feasibility.Unknown);

            if (combinedQuery.WasChanged)
                return (pathFeasibility, IsSatisfiableRaw(
                    combinedQuery.Conditions,
                    timeout,
                    combinedQuery.ContainsApproximateRegex));

            solver.Push();
            try
            {
                solver.Assert(_encoder.EncodeCondition(impurityCondition));
                return (pathFeasibility, AdjustForApproximation(
                    ToFeasibility(CheckAndAccountResources(solver)),
                    combinedQuery.ContainsApproximateRegex));
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

    private PreparedSmtQuery Prepare(IEnumerable<SmtFormula> conditions)
    {
        var original = conditions.ToArray();
        var status = _preprocessor.Prepare(original, out var prepared);
        return new PreparedSmtQuery(
            status,
            original,
            prepared,
            !ReferenceEquals(original, prepared),
            ContainsApproximateRegex(original));
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

    private static IReadOnlyList<SmtVariable> CollectVariables(IEnumerable<SmtFormula> formulas)
    {
        var variables = new HashSet<SmtVariable>();
        foreach (var formula in formulas)
            foreach (var candidate in SmtFormulaTraversal.Enumerate(formula))
                if (candidate is SmtVariable variable)
                    variables.Add(variable);

        return variables.ToArray();
    }

    private readonly record struct PreparedSmtQuery(
        SmtConcreteFactPreparationStatus Status,
        SmtFormula[] OriginalConditions,
        SmtFormula[] Conditions,
        bool WasChanged,
        bool ContainsApproximateRegex);
}
