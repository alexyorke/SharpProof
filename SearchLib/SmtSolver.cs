using Microsoft.Z3;

namespace SearchLib.Smt
{
    public enum Feasibility
    {
        Satisfiable,
        Unsatisfiable,
        Unknown,
    }

    public sealed class SmtSolver : IDisposable
    {
        private readonly Z3FormulaEncoder _encoder = new();

        public Feasibility IsSatisfiable(IEnumerable<SmtFormula> pathConditions, TimeSpan timeout)
        {
            using var solver = _encoder.CreateSolver(timeout);
            foreach (var formula in pathConditions)
            {
                solver.Assert(_encoder.EncodeCondition(formula));
            }

            return ToFeasibility(solver.Check());
        }

        public Feasibility Implies(IEnumerable<SmtFormula> pathConditions, SmtFormula conclusion, TimeSpan timeout)
        {
            using var solver = _encoder.CreateSolver(timeout);
            foreach (var formula in pathConditions)
            {
                solver.Assert(_encoder.EncodeCondition(formula));
            }

            solver.Assert(_encoder.Negate(conclusion));
            return ToFeasibility(solver.Check());
        }

        public void Dispose()
        {
            _encoder.Dispose();
        }

        private static Feasibility ToFeasibility(Status status)
        {
            return status switch
            {
                Status.SATISFIABLE => Feasibility.Satisfiable,
                Status.UNSATISFIABLE => Feasibility.Unsatisfiable,
                _ => Feasibility.Unknown,
            };
        }
    }
}
