using System.Collections.Immutable;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Smt;

internal static class SmtPathConditionMerger
{
    private static readonly PathConditionMergeStrategy<SmtFormula> Strategy = new(
        SmtFormulaStructuralKey.Create,
        TryGetMergeTargetKey,
        static formulas => Combine(SmtBinaryOperator.And, formulas),
        static formulas => Combine(SmtBinaryOperator.Or, formulas));

    internal static ImmutableArray<SmtFormula> MergeAcrossAll(
        IReadOnlyList<ImmutableArray<SmtFormula>> pathConditionSets,
        SmtPathConditionMergeOptions options)
    {
        return PathConditionMergeEngine.MergeAcrossAll(
            pathConditionSets.Select(static set => (IReadOnlyList<SmtFormula>)set).ToArray(),
            Strategy,
            new PathConditionMergeLimits(
                options.MaxMergedPathConditions,
                options.MaxFactsPerTargetPerState,
                options.MaxFactChoiceCombinationsPerTarget,
                options.MaxGuardFactsPerTargetPerState));
    }

    private static SmtFormula Combine(
        SmtBinaryOperator op,
        IReadOnlyList<SmtFormula> formulas)
    {
        var result = formulas[0];
        for (var index = 1; index < formulas.Count; index++)
            result = new SmtBinaryFormula(op, result, formulas[index]);

        return result;
    }

    private static bool TryGetMergeTargetKey(SmtFormula formula, out string targetKey)
    {
        if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not, Operand: { } operand })
            formula = operand;

        switch (formula)
        {
            case SmtBinaryFormula
            {
                Operator: SmtBinaryOperator.Equal,
                Left: SmtVariable target,
                Right: { } right
            } when target.Kind == right.Kind:
                targetKey = GetKey(target);
                return true;
            case SmtBinaryFormula
            {
                Operator: SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual,
                Left: SmtVariable target,
                Right: SmtNullConstant
            }:
                targetKey = GetKey(target);
                return true;
            case SmtBinaryFormula
            {
                Operator: SmtBinaryOperator.Equal or
                    SmtBinaryOperator.NotEqual or
                    SmtBinaryOperator.GreaterThan or
                    SmtBinaryOperator.GreaterThanOrEqual or
                    SmtBinaryOperator.LessThan or
                    SmtBinaryOperator.LessThanOrEqual,
                Left: { } left,
                Right: { } right
            } when TryGetMergeTargetTermKey(left, out targetKey) ||
                   TryGetMergeTargetTermKey(right, out targetKey):
                return true;
            case SmtVariable { Kind: SmtValueKind.Bool } target:
                targetKey = GetKey(target);
                return true;
            default:
                targetKey = string.Empty;
                return false;
        }
    }

    private static bool TryGetMergeTargetTermKey(SmtFormula formula, out string targetKey)
    {
        switch (formula)
        {
            case SmtVariable variable:
                targetKey = GetKey(variable);
                return true;
            case SmtStringLengthTerm stringLength:
                targetKey = GetKey(stringLength);
                return true;
            default:
                targetKey = string.Empty;
                return false;
        }
    }

    private static string GetKey(SmtFormula formula)
    {
        return SmtFormulaStructuralKey.Create(formula);
    }
}

internal readonly struct SmtPathConditionMergeOptions(
    int maxMergedPathConditions,
    int maxFactsPerTargetPerState,
    int maxFactChoiceCombinationsPerTarget,
    int maxGuardFactsPerTargetPerState)
{
    public int MaxMergedPathConditions { get; } = maxMergedPathConditions;

    public int MaxFactsPerTargetPerState { get; } = maxFactsPerTargetPerState;

    public int MaxFactChoiceCombinationsPerTarget { get; } = maxFactChoiceCombinationsPerTarget;

    public int MaxGuardFactsPerTargetPerState { get; } = maxGuardFactsPerTargetPerState;
}
