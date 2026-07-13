namespace SharpProof.ProofCore.Smt;

internal static class SmtConditionalFactSimplifier
{
    internal static SmtConcreteFactPreparationStatus Simplify(
        List<SmtFormula> conditions,
        ConcreteFactContext facts,
        SmtConcreteBooleanEvaluator evaluateBoolean,
        ref bool changed)
    {
        for (var index = 0; index < conditions.Count; index++)
        {
            var simplified = Simplify(
                conditions[index],
                facts,
                evaluateBoolean,
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
        ConcreteFactContext facts,
        SmtConcreteBooleanEvaluator evaluateBoolean,
        out bool changed)
    {
        return SmtFormulaTraversal.RewriteBottomUp(
            formula,
            candidate =>
            {
                if (candidate is not SmtConditionalFormula conditional) return candidate;

                if (SmtFormulaTraversal.AreStructurallyEqual(conditional.WhenTrue, conditional.WhenFalse))
                    return conditional.WhenTrue;

                if (evaluateBoolean(conditional.Condition, facts, out var selectedBranch))
                    return selectedBranch ? conditional.WhenTrue : conditional.WhenFalse;

                return candidate;
            },
            out changed);
    }
}
