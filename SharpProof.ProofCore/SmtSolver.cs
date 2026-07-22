using System.Diagnostics;
namespace SharpProof.ProofCore.Smt;
internal enum Feasibility {
    Satisfiable,
    Unsatisfiable,
    Unknown
}
internal sealed class SmtSolver : IDisposable {
    internal const int MaxRegexValidationCacheEntries = SmtRegexValidator.MaxCacheEntries;
    private readonly Z3FormulaEncoder _encoder = new();
    private readonly SmtQuerySafety _safety = new();
    private long _lastObservedRlimitCount;
    /// <summary>
    ///     Total Z3 rlimit units consumed by checks on this solver instance. Grows
    ///     deterministically with solver work, so callers can enforce cumulative
    ///     budgets that do not depend on machine speed or load.
    /// </summary>
    public long ConsumedResourceCount { get; private set; }
    internal int RegexValidationCacheCount => _safety.RegexValidationCacheCount;
    public void Dispose() => _encoder.Dispose();
    private Status CheckAndAccountResources(Solver solver) {
        var status = solver.Check();
        foreach (var entry in solver.Statistics.Entries) {
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
    public SmtFeasibilityResult CheckSatisfiability(IEnumerable<SmtFormula> pathConditions, TimeSpan timeout)
        => CheckSatisfiability(pathConditions, timeout, true);
    private SmtFeasibilityResult CheckSatisfiability(IEnumerable<SmtFormula> pathConditions, TimeSpan timeout, bool adjustApproximation) {
        if (timeout <= TimeSpan.Zero)
            return new SmtFeasibilityResult(Feasibility.Unknown, SmtSatisfyingWitness.Unsupported("solver_timeout"));
        var original = pathConditions.ToArray();
        if (!_safety.TryPrepare(original, out var conditions, out var changed))
            return new SmtFeasibilityResult(Feasibility.Unknown, SmtSatisfyingWitness.Unsupported("constraint_preparation_unknown"));
        try {
            var clock = Stopwatch.StartNew();
            if (!HasSafeArithmetic(conditions, timeout))
                return new SmtFeasibilityResult(Feasibility.Unknown, SmtSatisfyingWitness.Unsupported("constraint_preparation_unknown"));
            var remaining = timeout - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
                return new SmtFeasibilityResult(Feasibility.Unknown, SmtSatisfyingWitness.Unsupported("solver_timeout"));
            return CheckSatisfiabilityRawWithWitness(
                conditions,
                original,
                remaining,
                changed,
                adjustApproximation,
                conditions.Any(_encoder.ContainsApproximateRegex));
        }
        catch (Exception ex) when (IsConservativeSolverFailure(ex)) {
            return new SmtFeasibilityResult(Feasibility.Unknown, SmtSatisfyingWitness.Unsupported(GetConservativeSolverFailureReason(ex)));
        }
    }
    public SmtPathAndHazardCheckResult CheckPathAndHazardWithWitness(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula impurityCondition,
        TimeSpan timeout) {
        var normalizedPathConditions = pathConditions.ToArray();
        var deadline = Stopwatch.StartNew();
        var path = CheckSatisfiability(normalizedPathConditions, timeout, true);
        if (path.Feasibility == Feasibility.Unsatisfiable)
            return new SmtPathAndHazardCheckResult(
                path,
                new SmtFeasibilityResult(Feasibility.Unknown, SmtSatisfyingWitness.None("path_not_satisfiable")));
        var remaining = timeout - deadline.Elapsed;
        var impurity = remaining <= TimeSpan.Zero
            ? new SmtFeasibilityResult(Feasibility.Unknown, SmtSatisfyingWitness.Unsupported("solver_timeout"))
            : CheckSatisfiability(
                normalizedPathConditions.Concat(new[] { impurityCondition }),
                remaining);
        return new SmtPathAndHazardCheckResult(path, impurity);
    }
    private SmtFeasibilityResult CheckSatisfiabilityRawWithWitness(
        IReadOnlyList<SmtFormula> conditions,
        IReadOnlyList<SmtFormula> modelConditions,
        TimeSpan timeout,
        bool preprocessedModel,
        bool adjustApproximation,
        bool? containsApproximateRegex = null) {
        var isApproximate = containsApproximateRegex ?? conditions.Any(_encoder.ContainsApproximateRegex);
        using var solver = _encoder.CreateSolver(timeout);
        foreach (var formula in conditions) solver.Assert(_encoder.EncodeCondition(formula));
        AssertIntegerDomains(solver, conditions);
        var feasibility = ToFeasibility(CheckAndAccountResources(solver));
        if (feasibility == Feasibility.Unsatisfiable)
            return new SmtFeasibilityResult(feasibility, SmtSatisfyingWitness.None("constraints_unsatisfiable"));
        if (feasibility != Feasibility.Satisfiable)
            return new SmtFeasibilityResult(feasibility, SmtSatisfyingWitness.Unsupported("solver_unknown"));
        var witnessStatus = isApproximate || preprocessedModel
            ? SmtWitnessStatus.Approximate
            : SmtWitnessStatus.Exact;
        var witnessReason = isApproximate
            ? "approximate_regex_model"
            : preprocessedModel
                ? "model_from_preprocessed_constraints"
                : "satisfying_model";
        using var model = solver.Model;
        var witness = _encoder.CreateWitness(model, CollectVariables(modelConditions), witnessStatus, witnessReason);
        return new SmtFeasibilityResult(adjustApproximation ? AdjustForApproximation(feasibility, isApproximate) : feasibility, witness);
    }
    private static Feasibility ToFeasibility(Status status) => status switch {
        Status.SATISFIABLE => Feasibility.Satisfiable,
        Status.UNSATISFIABLE => Feasibility.Unsatisfiable,
        _ => Feasibility.Unknown
    };
    private bool HasSafeArithmetic(IReadOnlyList<SmtFormula> conditions, TimeSpan timeout) {
        var checks = SmtQuerySafety.CreateUnsafeArithmeticChecks(conditions);
        if (checks.Count == 0) return true;
        using var solver = _encoder.CreateSolver(timeout);
        foreach (var condition in conditions)
            if (!SmtQuerySafety.ContainsUnsafeArithmetic(condition))
                solver.Assert(_encoder.EncodeCondition(condition));
        AssertIntegerDomains(solver, conditions);
        solver.Assert(_encoder.EncodeCondition(checks.Aggregate(
            static (left, right) => new SmtBinaryFormula(SmtBinaryOperator.Or, left, right))));
        return CheckAndAccountResources(solver) == Status.UNSATISFIABLE;
    }
    private void AssertIntegerDomains(Solver solver, IEnumerable<SmtFormula> conditions) {
        foreach (var variable in CollectVariables(conditions).Where(static value => value.Kind == SmtValueKind.Int)) {
            solver.Assert(_encoder.EncodeCondition(new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                variable,
                new SmtIntegerConstant(long.MinValue))));
            solver.Assert(_encoder.EncodeCondition(new SmtBinaryFormula(
                SmtBinaryOperator.LessThanOrEqual,
                variable,
                new SmtIntegerConstant(long.MaxValue))));
        }
    }
    private static Feasibility AdjustForApproximation(Feasibility feasibility, bool containsApproximateRegex)
        => feasibility == Feasibility.Satisfiable && containsApproximateRegex
            ? Feasibility.Unknown
            : feasibility;
    private static bool IsConservativeSolverFailure(Exception ex) => ex is InvalidOperationException ||
               ex is Z3Exception ||
               ex is ArgumentException ||
               ex is InvalidCastException ||
               ex is RegexMatchTimeoutException ||
               ex is ArithmeticException;
    private static string GetConservativeSolverFailureReason(Exception ex) => ex switch {
        Z3Exception => "z3_transient_failure",
        RegexMatchTimeoutException => "solver_timeout",
        _ => "solver_encoding_failure"
    };
    private static IReadOnlyList<SmtVariable> CollectVariables(IEnumerable<SmtFormula> formulas) {
        var variables = new HashSet<SmtVariable>();
        foreach (var formula in formulas)
            foreach (var candidate in SmtFormulaTraversal.Enumerate(formula))
                if (candidate is SmtVariable variable)
                    variables.Add(variable);
        return variables.ToArray();
    }
}
