using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace SharpProof.Test.Smt;

internal sealed class SmtPathOracle : IDisposable
{
    private readonly SmtSolver _solver = new();

    public void Dispose()
    {
        _solver.Dispose();
    }

    public Feasibility IsSatisfiable(
        IEnumerable<ExpressionSyntax> pathConditions,
        SemanticModel semanticModel,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!TryTranslateAll(pathConditions, semanticModel, cancellationToken, out var formulas))
            return Feasibility.Unknown;

        return _solver.IsSatisfiable(formulas, timeout);
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
            !CSharpConditionToFormula.TryTranslate(conclusion, semanticModel, cancellationToken,
                out var conclusionFormula))
            return Feasibility.Unknown;

        return _solver.Implies(formulas, conclusionFormula, timeout);
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
        return _solver.IsSatisfiable(pathConditions, timeout);
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
            if (!CSharpConditionToFormula.TryTranslate(pathCondition, semanticModel, cancellationToken,
                    out var formula))
            {
                formulas.Clear();
                return false;
            }

            formulas.Add(formula);
        }

        return true;
    }
}