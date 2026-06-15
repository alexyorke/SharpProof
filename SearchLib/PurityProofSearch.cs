using SearchLib.Smt;

namespace SearchLib.Purity
{
    public enum PurityProofOutcome
    {
        ProvablyPure,
        ProvablyImpure,
        Unknown,
    }

    public sealed record PurityProofResult(
        PurityProofOutcome Outcome,
        Feasibility PathFeasibility,
        Feasibility ImpurityFeasibility,
        string Reason);

    public sealed class PurityProofSearch : IDisposable
    {
        private readonly SmtSolver _solver = new();

        public PurityProofResult Classify(SmtFormula impurityCondition, TimeSpan timeout)
        {
            return Classify(Array.Empty<SmtFormula>(), impurityCondition, timeout);
        }

        public PurityProofResult Classify(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula impurityCondition,
            TimeSpan timeout)
        {
            var normalizedPathConditions = pathConditions.ToArray();
            var pathFeasibility = _solver.IsSatisfiable(normalizedPathConditions, timeout);
            if (pathFeasibility == Feasibility.Unsatisfiable)
            {
                return new PurityProofResult(
                    PurityProofOutcome.ProvablyPure,
                    pathFeasibility,
                    Feasibility.Unsatisfiable,
                    "path_unsatisfiable");
            }

            if (pathFeasibility == Feasibility.Unknown)
            {
                return new PurityProofResult(
                    PurityProofOutcome.Unknown,
                    pathFeasibility,
                    Feasibility.Unknown,
                    "path_feasibility_unknown");
            }

            var combinedConditions = normalizedPathConditions.Concat(new[] { impurityCondition });
            var impurityFeasibility = _solver.IsSatisfiable(combinedConditions, timeout);
            return impurityFeasibility switch
            {
                Feasibility.Unsatisfiable => new PurityProofResult(
                    PurityProofOutcome.ProvablyPure,
                    pathFeasibility,
                    impurityFeasibility,
                    "impurity_unreachable"),
                Feasibility.Satisfiable => new PurityProofResult(
                    PurityProofOutcome.ProvablyImpure,
                    pathFeasibility,
                    impurityFeasibility,
                    "impurity_reachable"),
                _ => new PurityProofResult(
                    PurityProofOutcome.Unknown,
                    pathFeasibility,
                    impurityFeasibility,
                    "impurity_feasibility_unknown"),
            };
        }

        public void Dispose()
        {
            _solver.Dispose();
        }
    }
}
