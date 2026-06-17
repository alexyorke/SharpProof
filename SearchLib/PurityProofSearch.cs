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
            return ClassifyCore(
                pathConditions,
                impurityCondition,
                timeout,
                pureReason: "impurity_unreachable",
                impureReason: "impurity_reachable",
                unknownReason: "impurity_feasibility_unknown");
        }

        public PurityProofResult Classify(PurityProofQuery query, TimeSpan timeout)
        {
            if (query.Hazard.Visibility == PurityEffectVisibility.InternalOnly)
            {
                var pathFeasibility = _solver.IsSatisfiable(query.PathConditions, timeout);
                return pathFeasibility switch
                {
                    Feasibility.Unsatisfiable => new PurityProofResult(
                        PurityProofOutcome.ProvablyPure,
                        pathFeasibility,
                        Feasibility.Unsatisfiable,
                        "path_unsatisfiable"),
                    Feasibility.Unknown => new PurityProofResult(
                        PurityProofOutcome.Unknown,
                        pathFeasibility,
                        Feasibility.Unknown,
                        "path_feasibility_unknown"),
                    _ => new PurityProofResult(
                        PurityProofOutcome.ProvablyPure,
                        pathFeasibility,
                        Feasibility.Unsatisfiable,
                        "effect_not_caller_visible"),
                };
            }

            return query.Hazard.Kind switch
            {
                PurityHazardKind.BranchReachability => ClassifyBranchReachability(query.PathConditions, query.Hazard.TriggerCondition, timeout),
                PurityHazardKind.ImpureCallReachability => ClassifyImpureCallReachability(query.PathConditions, query.Hazard.TriggerCondition, timeout),
                PurityHazardKind.NullDereference => ClassifyNullDereference(query.PathConditions, query.Hazard.TriggerCondition, timeout),
                PurityHazardKind.DivideByZero => ClassifyDivideByZero(query.PathConditions, query.Hazard.TriggerCondition, timeout),
                _ => new PurityProofResult(PurityProofOutcome.Unknown, Feasibility.Unknown, Feasibility.Unknown, "unsupported_hazard_kind"),
            };
        }

        public PurityProofResult ClassifyBranchReachability(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula branchReachabilityCondition,
            TimeSpan timeout)
        {
            return ClassifyCore(
                pathConditions,
                branchReachabilityCondition,
                timeout,
                pureReason: "branch_unreachable",
                impureReason: "branch_reachable",
                unknownReason: "branch_feasibility_unknown");
        }

        public PurityProofResult ClassifyImpureCallReachability(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula callReachabilityCondition,
            TimeSpan timeout)
        {
            return ClassifyCore(
                pathConditions,
                callReachabilityCondition,
                timeout,
                pureReason: "impure_call_unreachable",
                impureReason: "impure_call_reachable",
                unknownReason: "impure_call_feasibility_unknown");
        }

        public PurityProofResult ClassifyNullDereference(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula receiverIsNullCondition,
            TimeSpan timeout)
        {
            return ClassifyCore(
                pathConditions,
                receiverIsNullCondition,
                timeout,
                pureReason: "null_dereference_unreachable",
                impureReason: "null_dereference_reachable",
                unknownReason: "null_dereference_feasibility_unknown");
        }

        public PurityProofResult ClassifyDivideByZero(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula divisorIsZeroCondition,
            TimeSpan timeout)
        {
            return ClassifyCore(
                pathConditions,
                divisorIsZeroCondition,
                timeout,
                pureReason: "divide_by_zero_unreachable",
                impureReason: "divide_by_zero_reachable",
                unknownReason: "divide_by_zero_feasibility_unknown");
        }

        private PurityProofResult ClassifyCore(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula impurityCondition,
            TimeSpan timeout,
            string pureReason,
            string impureReason,
            string unknownReason)
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
                    pureReason),
                Feasibility.Satisfiable => new PurityProofResult(
                    PurityProofOutcome.ProvablyImpure,
                    pathFeasibility,
                    impurityFeasibility,
                    impureReason),
                _ => new PurityProofResult(
                    PurityProofOutcome.Unknown,
                    pathFeasibility,
                    impurityFeasibility,
                    unknownReason),
            };
        }

        public void Dispose()
        {
            _solver.Dispose();
        }
    }
}
