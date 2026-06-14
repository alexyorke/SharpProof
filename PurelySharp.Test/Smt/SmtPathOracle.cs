using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Z3;

namespace PurelySharp.Test.Smt
{
    internal enum Feasibility
    {
        Satisfiable,
        Unsatisfiable,
        Unknown,
    }

    internal sealed class SmtPathOracle : IDisposable
    {
        private readonly Z3FormulaEncoder _encoder = new();

        public Feasibility IsSatisfiable(
            IEnumerable<ExpressionSyntax> pathConditions,
            SemanticModel semanticModel,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (!TryTranslateAll(pathConditions, semanticModel, cancellationToken, out var formulas))
            {
                return Feasibility.Unknown;
            }

            return IsSatisfiable(formulas, timeout);
        }

        public Feasibility IsSatisfiable(
            ExpressionSyntax pathCondition,
            SemanticModel semanticModel,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return IsSatisfiable(new[] { pathCondition }, semanticModel, timeout, cancellationToken);
        }

        public Feasibility Implies(
            IEnumerable<ExpressionSyntax> pathConditions,
            ExpressionSyntax conclusion,
            SemanticModel semanticModel,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (!TryTranslateAll(pathConditions, semanticModel, cancellationToken, out var formulas) ||
                !CSharpConditionToFormula.TryTranslate(conclusion, semanticModel, cancellationToken, out var conclusionFormula))
            {
                return Feasibility.Unknown;
            }

            using var solver = _encoder.CreateSolver(timeout);
            foreach (var formula in formulas)
            {
                solver.Assert(_encoder.EncodeCondition(formula));
            }

            solver.Assert(_encoder.Negate(conclusionFormula));
            return ToFeasibility(solver.Check());
        }

        public Feasibility Implies(
            ExpressionSyntax pathCondition,
            ExpressionSyntax conclusion,
            SemanticModel semanticModel,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Implies(new[] { pathCondition }, conclusion, semanticModel, timeout, cancellationToken);
        }

        public Feasibility IsSatisfiable(IEnumerable<SmtFormula> pathConditions, TimeSpan timeout)
        {
            using var solver = _encoder.CreateSolver(timeout);
            foreach (var formula in pathConditions)
            {
                solver.Assert(_encoder.EncodeCondition(formula));
            }

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

        private static bool TryTranslateAll(
            IEnumerable<ExpressionSyntax> pathConditions,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out List<SmtFormula> formulas)
        {
            formulas = new List<SmtFormula>();
            foreach (var pathCondition in pathConditions)
            {
                if (!CSharpConditionToFormula.TryTranslate(pathCondition, semanticModel, cancellationToken, out var formula))
                {
                    formulas.Clear();
                    return false;
                }

                formulas.Add(formula);
            }

            return true;
        }
    }
}
