namespace SharpProof.Effects;

internal enum SwitchExpressionSelection
{
    Never,
    Maybe,
    Always
}

internal static class SwitchExpressionFacts
{
    internal static IReadOnlyList<ISwitchExpressionArmOperation> GetReachableArms(
        ISwitchExpressionOperation operation,
        Func<IOperation?, bool> canCompleteNormally)
    {
        if (!canCompleteNormally(operation.Value))
        {
            return [];
        }

        if (operation.Value.ConstantValue is not { HasValue: true } constant)
        {
            return GetReachableArmsForUnknownValue(
                operation,
                canCompleteNormally);
        }

        var reachable = new List<ISwitchExpressionArmOperation>();
        foreach (var arm in operation.Arms)
        {
            var pattern = GetPatternSelection(arm.Pattern, constant.Value);
            var selection = GetArmSelection(arm, constant.Value);
            if (selection != SwitchExpressionSelection.Never)
            {
                reachable.Add(arm);
            }
            if (selection == SwitchExpressionSelection.Always ||
                pattern == SwitchExpressionSelection.Always &&
                arm.Guard != null &&
                !canCompleteNormally(arm.Guard))
            {
                break;
            }
        }
        return reachable;
    }

    internal static bool HasReachableUnmatchedPath(
        ISwitchExpressionOperation operation,
        Func<IOperation?, bool> canCompleteNormally)
    {
        if (!canCompleteNormally(operation.Value))
        {
            return false;
        }
        if (operation.IsExhaustive)
        {
            return false;
        }
        if (operation.Value.ConstantValue is not { HasValue: true } constant)
        {
            foreach (var arm in operation.Arms)
            {
                var pattern = GetPatternSelectionForUnknownValue(
                    arm.Pattern,
                    operation.Value.Type);
                var selection = ApplyGuard(pattern, arm.Guard);
                if (selection == SwitchExpressionSelection.Always)
                {
                    return false;
                }
                if (pattern == SwitchExpressionSelection.Always &&
                    arm.Guard != null &&
                    !canCompleteNormally(arm.Guard))
                {
                    return false;
                }
            }
            return true;
        }

        foreach (var arm in operation.Arms)
        {
            var pattern = GetPatternSelection(arm.Pattern, constant.Value);
            var selection = GetArmSelection(arm, constant.Value);
            if (selection == SwitchExpressionSelection.Always)
            {
                return false;
            }
            if (pattern == SwitchExpressionSelection.Always &&
                arm.Guard != null &&
                !canCompleteNormally(arm.Guard))
            {
                return false;
            }
        }
        return true;
    }

    internal static SwitchExpressionSelection GetArmSelection(
        ISwitchExpressionArmOperation arm,
        object? value)
    {
        var pattern = GetPatternSelection(arm.Pattern, value);
        return ApplyGuard(pattern, arm.Guard);
    }

    private static IReadOnlyList<ISwitchExpressionArmOperation>
        GetReachableArmsForUnknownValue(
            ISwitchExpressionOperation operation,
            Func<IOperation?, bool> canCompleteNormally)
    {
        var reachable = new List<ISwitchExpressionArmOperation>();
        foreach (var arm in operation.Arms)
        {
            var pattern = GetPatternSelectionForUnknownValue(
                arm.Pattern,
                operation.Value.Type);
            var selection = ApplyGuard(pattern, arm.Guard);
            if (selection != SwitchExpressionSelection.Never)
            {
                reachable.Add(arm);
            }
            if (selection == SwitchExpressionSelection.Always ||
                pattern == SwitchExpressionSelection.Always &&
                arm.Guard != null &&
                !canCompleteNormally(arm.Guard))
            {
                break;
            }
        }
        return reachable;
    }

    private static SwitchExpressionSelection GetPatternSelectionForUnknownValue(
        IPatternOperation pattern,
        ITypeSymbol? inputType)
    {
        if (pattern is IDiscardPatternOperation or
            IDeclarationPatternOperation { MatchesNull: true })
        {
            return SwitchExpressionSelection.Always;
        }
        var matchedType = pattern switch
        {
            ITypePatternOperation typePattern => typePattern.MatchedType,
            IDeclarationPatternOperation declarationPattern =>
                declarationPattern.MatchedType,
            _ => null
        };
        return inputType?.IsValueType == true &&
            SymbolEqualityComparer.Default.Equals(matchedType, inputType)
                ? SwitchExpressionSelection.Always
                : SwitchExpressionSelection.Maybe;
    }

    private static SwitchExpressionSelection ApplyGuard(
        SwitchExpressionSelection pattern,
        IOperation? guard)
    {
        if (pattern == SwitchExpressionSelection.Never || guard == null)
        {
            return pattern;
        }
        return guard.ConstantValue is { HasValue: true, Value: bool value }
            ? value ? pattern : SwitchExpressionSelection.Never
            : SwitchExpressionSelection.Maybe;
    }

    internal static SwitchExpressionSelection GetPatternSelection(
        IPatternOperation pattern,
        object? value)
    {
        return pattern switch
        {
            IDiscardPatternOperation => SwitchExpressionSelection.Always,
            IConstantPatternOperation constant
                when constant.Value.ConstantValue is { HasValue: true } item =>
                Equals(value, item.Value)
                    ? SwitchExpressionSelection.Always
                    : SwitchExpressionSelection.Never,
            IRelationalPatternOperation relational
                when relational.Value.ConstantValue is { HasValue: true } item &&
                TryMatchRelationalConstants(
                    value,
                    item.Value,
                    relational.OperatorKind,
                    out var matches) =>
                matches
                    ? SwitchExpressionSelection.Always
                    : SwitchExpressionSelection.Never,
            _ => SwitchExpressionSelection.Maybe
        };
    }

    private static bool TryMatchRelationalConstants(
        object? left,
        object? right,
        BinaryOperatorKind operatorKind,
        out bool matches)
    {
        matches = false;
        if (left == null || right == null || left.GetType() != right.GetType() ||
            left is not IComparable comparable)
        {
            return false;
        }
        if (left is float leftSingle && right is float rightSingle)
        {
            matches = MatchesFloating(
                operatorKind,
                leftSingle,
                rightSingle);
            return true;
        }
        if (left is double leftDouble && right is double rightDouble)
        {
            matches = MatchesFloating(
                operatorKind,
                leftDouble,
                rightDouble);
            return true;
        }

        matches = Matches(operatorKind, comparable.CompareTo(right));
        return true;
    }

    private static bool MatchesFloating(
        BinaryOperatorKind operatorKind,
        double left,
        double right)
    {
        return operatorKind switch
        {
            BinaryOperatorKind.LessThan => left < right,
            BinaryOperatorKind.LessThanOrEqual => left <= right,
            BinaryOperatorKind.GreaterThan => left > right,
            BinaryOperatorKind.GreaterThanOrEqual => left >= right,
            _ => false
        };
    }

    private static bool Matches(BinaryOperatorKind operatorKind, int comparison)
    {
        return operatorKind switch
        {
            BinaryOperatorKind.LessThan => comparison < 0,
            BinaryOperatorKind.LessThanOrEqual => comparison <= 0,
            BinaryOperatorKind.GreaterThan => comparison > 0,
            BinaryOperatorKind.GreaterThanOrEqual => comparison >= 0,
            _ => false
        };
    }
}
