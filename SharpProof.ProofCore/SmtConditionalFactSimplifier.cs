namespace SharpProof.ProofCore.Smt;

internal static class SmtConditionalFactSimplifier
{
    internal static SmtConcreteFactPreparationStatus Simplify(
        List<SmtFormula> conditions,
        SmtSyntacticClassifier.SyntacticFactSet facts,
        ref bool changed)
    {
        for (var index = 0; index < conditions.Count; index++)
        {
            var simplified = Simplify(
                conditions[index],
                facts,
                out var conditionChanged);
            changed |= conditionChanged;
            if (simplified is SmtBooleanConstant { Value: false })
                return SmtConcreteFactPreparationStatus.Unsatisfiable;

            conditions[index] = simplified;
        }

        return SmtConcreteFactPreparationStatus.Ready;
    }

    private static SmtFormula Simplify(
        SmtFormula formula,
        SmtSyntacticClassifier.SyntacticFactSet facts,
        out bool changed)
    {
        return SmtFormulaTraversal.RewriteBottomUp(
            formula,
            candidate =>
            {
                if (candidate is not SmtConditionalFormula conditional) return candidate;

                if (SmtFormulaTraversal.AreStructurallyEqual(conditional.WhenTrue, conditional.WhenFalse))
                    return conditional.WhenTrue;

                if (facts.TryEvaluateBoolean(conditional.Condition, out var selectedBranch))
                    return selectedBranch ? conditional.WhenTrue : conditional.WhenFalse;

                return candidate;
            },
            out changed);
    }
}
