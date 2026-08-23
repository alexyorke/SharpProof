namespace SharpProof.Effects;

internal enum SwitchExpressionSelection
{
    Never,
    Maybe,
    Always
}

internal static class SwitchExpressionFacts
{
    internal static IOperation? GetGoverningValue(IPatternOperation pattern)
    {
        var current = (IOperation)pattern;
        while (current.Parent is INegatedPatternOperation or
            IBinaryPatternOperation)
        {
            current = current.Parent;
        }
        return current.Parent switch
        {
            IIsPatternOperation isPattern
                when ReferenceEquals(isPattern.Pattern, current) =>
                isPattern.Value,
            ISwitchExpressionArmOperation
            { Parent: ISwitchExpressionOperation expression } =>
                expression.Value,
            IPatternCaseClauseOperation
            {
                Parent: ISwitchCaseOperation
                { Parent: ISwitchOperation statement }
            } => statement.Value,
            _ => null
        };
    }

    internal static IMethodSymbol? GetCallableListPatternMember(ISymbol? symbol)
    {
        return symbol switch
        {
            IPropertySymbol property => property.GetMethod,
            IMethodSymbol method => method,
            _ => null
        };
    }

    internal static IReadOnlyList<ISwitchExpressionArmOperation> GetReachableArms(
        ISwitchExpressionOperation operation,
        Func<IOperation?, bool> canCompleteNormally,
        bool inputDefinitelyNonNull = false)
    {
        if (!canCompleteNormally(operation.Value))
        {
            return [];
        }

        inputDefinitelyNonNull |=
            DefiniteOperationFacts.IsDefinitelyNonNull(operation.Value);

        if (operation.Value.ConstantValue is not { HasValue: true } constant)
        {
            return GetReachableArmsForUnknownValue(
                operation,
                canCompleteNormally,
                inputDefinitelyNonNull);
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
                IsPatternEvaluationUnavoidable(
                    arm.Pattern,
                    operation.Value.Type,
                    inputDefinitelyNonNull) &&
                !canCompleteNormally(arm.Pattern) ||
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
        Func<IOperation?, bool> canCompleteNormally,
        bool inputDefinitelyNonNull = false)
    {
        if (!canCompleteNormally(operation.Value))
        {
            return false;
        }
        if (operation.IsExhaustive)
        {
            return false;
        }
        inputDefinitelyNonNull |=
            DefiniteOperationFacts.IsDefinitelyNonNull(operation.Value);
        if (operation.Value.ConstantValue is not { HasValue: true } constant)
        {
            foreach (var arm in operation.Arms)
            {
                var pattern = GetPatternSelectionForUnknownValue(
                    arm.Pattern,
                    operation.Value.Type,
                    inputDefinitelyNonNull);
                var selection = ApplyGuard(pattern, arm.Guard);
                if (selection == SwitchExpressionSelection.Always)
                {
                    return false;
                }
                if (IsPatternEvaluationUnavoidable(
                        arm.Pattern,
                        operation.Value.Type,
                        inputDefinitelyNonNull) &&
                    !canCompleteNormally(arm.Pattern))
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
            if (IsPatternEvaluationUnavoidable(
                    arm.Pattern,
                    operation.Value.Type,
                    inputDefinitelyNonNull) &&
                !canCompleteNormally(arm.Pattern))
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

    private static List<ISwitchExpressionArmOperation>
        GetReachableArmsForUnknownValue(
            ISwitchExpressionOperation operation,
            Func<IOperation?, bool> canCompleteNormally,
            bool inputDefinitelyNonNull)
    {
        var reachable = new List<ISwitchExpressionArmOperation>();
        foreach (var arm in operation.Arms)
        {
            var pattern = GetPatternSelectionForUnknownValue(
                arm.Pattern,
                operation.Value.Type,
                inputDefinitelyNonNull);
            var selection = ApplyGuard(pattern, arm.Guard);
            if (selection != SwitchExpressionSelection.Never)
            {
                reachable.Add(arm);
            }
            if (selection == SwitchExpressionSelection.Always ||
                IsPatternEvaluationUnavoidable(
                    arm.Pattern,
                    operation.Value.Type,
                    inputDefinitelyNonNull) &&
                !canCompleteNormally(arm.Pattern) ||
                pattern == SwitchExpressionSelection.Always &&
                arm.Guard != null &&
                !canCompleteNormally(arm.Guard))
            {
                break;
            }
        }
        return reachable;
    }

    internal static SwitchExpressionSelection GetPatternSelectionForUnknownValue(
        IPatternOperation pattern,
        ITypeSymbol? inputType,
        bool inputDefinitelyNonNull = false)
    {
        return pattern switch
        {
            IConstantPatternOperation
            { Value.ConstantValue: { HasValue: true, Value: null } }
                when inputDefinitelyNonNull => SwitchExpressionSelection.Never,
            INegatedPatternOperation negated => Negate(
                GetPatternSelectionForUnknownValue(
                    negated.Pattern,
                    inputType,
                    inputDefinitelyNonNull)),
            IBinaryPatternOperation binary
                when binary.OperatorKind == BinaryOperatorKind.And => And(
                    GetPatternSelectionForUnknownValue(
                        binary.LeftPattern,
                        inputType,
                        inputDefinitelyNonNull),
                    GetPatternSelectionForUnknownValue(
                        binary.RightPattern,
                        inputType,
                        inputDefinitelyNonNull)),
            IBinaryPatternOperation binary
                when binary.OperatorKind == BinaryOperatorKind.Or => Or(
                    GetPatternSelectionForUnknownValue(
                        binary.LeftPattern,
                        inputType,
                        inputDefinitelyNonNull),
                    GetPatternSelectionForUnknownValue(
                        binary.RightPattern,
                        inputType,
                        inputDefinitelyNonNull)),
            _ when IsTotalPattern(
                pattern,
                inputType,
                inputDefinitelyNonNull) => SwitchExpressionSelection.Always,
            _ => SwitchExpressionSelection.Maybe
        };
    }

    internal static bool IsTotalPattern(
        IPatternOperation pattern,
        ITypeSymbol? inputType,
        bool inputDefinitelyNonNull = false)
    {
        if (pattern is IDiscardPatternOperation or
            IDeclarationPatternOperation { MatchesNull: true })
        {
            return true;
        }
        if (pattern is IListPatternOperation listPattern)
        {
            return (inputType?.IsValueType == true ||
                    inputDefinitelyNonNull) &&
                listPattern.Patterns.Length == 1 &&
                listPattern.Patterns[0] is
                    ISlicePatternOperation slicePattern &&
                (slicePattern.Pattern == null ||
                 IsTotalPattern(
                     slicePattern.Pattern,
                     slicePattern.Pattern.InputType));
        }
        var matchedType = pattern switch
        {
            ITypePatternOperation typePattern => typePattern.MatchedType,
            IDeclarationPatternOperation declarationPattern =>
                declarationPattern.MatchedType,
            IRecursivePatternOperation recursive => recursive.MatchedType,
            _ => null
        };
        if (inputType?.IsValueType != true && !inputDefinitelyNonNull ||
            !SymbolEqualityComparer.Default.Equals(matchedType, inputType))
        {
            return false;
        }
        return pattern is not IRecursivePatternOperation recursivePattern ||
            recursivePattern.DeconstructionSubpatterns.All(
                subpattern => IsTotalPattern(
                    subpattern,
                    subpattern.InputType)) &&
            recursivePattern.PropertySubpatterns.All(
                subpattern => IsTotalPattern(
                    subpattern.Pattern,
                    subpattern.Pattern.InputType));
    }

    internal static bool IsPatternEvaluationUnavoidable(
        IPatternOperation pattern,
        ITypeSymbol? inputType,
        bool inputDefinitelyNonNull = false)
    {
        if (pattern is IDiscardPatternOperation or
            IDeclarationPatternOperation { MatchesNull: true })
        {
            return true;
        }
        if (pattern is IListPatternOperation)
        {
            return inputType?.IsValueType == true || inputDefinitelyNonNull;
        }
        if (pattern is INegatedPatternOperation negated)
        {
            return IsPatternEvaluationUnavoidable(
                negated.Pattern,
                inputType,
                inputDefinitelyNonNull);
        }
        if (pattern is IBinaryPatternOperation binary)
        {
            var leftIsUnavoidable = IsPatternEvaluationUnavoidable(
                binary.LeftPattern,
                inputType,
                inputDefinitelyNonNull);
            var leftSelection = GetPatternSelectionForUnknownValue(
                binary.LeftPattern,
                inputType,
                inputDefinitelyNonNull);
            return leftIsUnavoidable ||
                binary.OperatorKind == BinaryOperatorKind.And &&
                leftSelection == SwitchExpressionSelection.Always &&
                IsPatternEvaluationUnavoidable(
                    binary.RightPattern,
                    inputType,
                    inputDefinitelyNonNull) ||
                binary.OperatorKind == BinaryOperatorKind.Or &&
                leftSelection == SwitchExpressionSelection.Never &&
                IsPatternEvaluationUnavoidable(
                    binary.RightPattern,
                    inputType,
                    inputDefinitelyNonNull);
        }
        var matchedType = pattern switch
        {
            ITypePatternOperation typePattern => typePattern.MatchedType,
            IDeclarationPatternOperation declarationPattern =>
                declarationPattern.MatchedType,
            IRecursivePatternOperation recursive => recursive.MatchedType,
            _ => null
        };
        return (inputType?.IsValueType == true || inputDefinitelyNonNull) &&
            SymbolEqualityComparer.Default.Equals(matchedType, inputType);
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
            INegatedPatternOperation negated =>
                Negate(GetPatternSelection(negated.Pattern, value)),
            IBinaryPatternOperation binary
                when binary.OperatorKind == BinaryOperatorKind.And =>
                And(
                    GetPatternSelection(binary.LeftPattern, value),
                    GetPatternSelection(binary.RightPattern, value)),
            IBinaryPatternOperation binary
                when binary.OperatorKind == BinaryOperatorKind.Or =>
                Or(
                    GetPatternSelection(binary.LeftPattern, value),
                    GetPatternSelection(binary.RightPattern, value)),
            _ when IsTotalPattern(pattern, pattern.InputType) =>
                SwitchExpressionSelection.Always,
            _ => SwitchExpressionSelection.Maybe
        };
    }

    private static SwitchExpressionSelection Negate(
        SwitchExpressionSelection selection)
    {
        return selection switch
        {
            SwitchExpressionSelection.Never => SwitchExpressionSelection.Always,
            SwitchExpressionSelection.Always => SwitchExpressionSelection.Never,
            _ => SwitchExpressionSelection.Maybe
        };
    }

    private static SwitchExpressionSelection And(
        SwitchExpressionSelection left,
        SwitchExpressionSelection right)
    {
        if (left == SwitchExpressionSelection.Never ||
            right == SwitchExpressionSelection.Never)
        {
            return SwitchExpressionSelection.Never;
        }
        return left == SwitchExpressionSelection.Always &&
            right == SwitchExpressionSelection.Always
                ? SwitchExpressionSelection.Always
                : SwitchExpressionSelection.Maybe;
    }

    private static SwitchExpressionSelection Or(
        SwitchExpressionSelection left,
        SwitchExpressionSelection right)
    {
        if (left == SwitchExpressionSelection.Always ||
            right == SwitchExpressionSelection.Always)
        {
            return SwitchExpressionSelection.Always;
        }
        return left == SwitchExpressionSelection.Never &&
            right == SwitchExpressionSelection.Never
                ? SwitchExpressionSelection.Never
                : SwitchExpressionSelection.Maybe;
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
