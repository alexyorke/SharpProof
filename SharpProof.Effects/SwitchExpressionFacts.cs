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

    internal static IMethodSymbol? GetCallableListPatternMember(
        IListPatternOperation pattern,
        IPatternOperation item)
    {
        return item is ISlicePatternOperation slice
            ? slice.Pattern == null
                ? null
                : GetCallableListPatternMember(slice.SliceSymbol)
            : GetCallableListPatternMember(pattern.IndexerSymbol);
    }

    internal static (int RequiredLength, bool HasSlice) GetListPatternShape(
        IListPatternOperation pattern)
    {
        return (
            pattern.Patterns.Count(static item => item is not ISlicePatternOperation),
            pattern.Patterns.Any(static item => item is ISlicePatternOperation));
    }

    internal static bool HasListPatternLengthMismatch(
        int requiredLength,
        bool hasSlice,
        long length)
    {
        return hasSlice ? length < requiredLength : length != requiredLength;
    }

    internal static bool IsCompilerIntrinsicListPatternMember(
        Compilation compilation,
        IListPatternOperation pattern,
        IMethodSymbol method)
    {
        if (method.DeclaringSyntaxReferences.Length != 0)
        {
            return false;
        }

        return pattern.InputType is IArrayTypeSymbol ||
            IsCompilerIntrinsicRefLikeMember(compilation, method);
    }

    internal static bool IsCompilerIntrinsicRefLikeMember(
        Compilation compilation,
        IMethodSymbol method)
    {
        return method.DeclaringSyntaxReferences.Length == 0 &&
            (IsRuntimeSpanMember(compilation, method, FrameworkTypeMetadataNames.Span) ||
             IsRuntimeSpanMember(
                 compilation,
                 method,
                 FrameworkTypeMetadataNames.ReadOnlySpan));
    }

    private static bool IsRuntimeSpanMember(
        Compilation compilation,
        IMethodSymbol method,
        string metadataName)
    {
        var runtimeType = compilation
            .GetSpecialType(SpecialType.System_Object)
            .ContainingAssembly
            .GetTypeByMetadataName(metadataName);
        if (runtimeType == null ||
            !SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                runtimeType.OriginalDefinition))
        {
            return false;
        }
        return true;
    }

    internal static IReadOnlyList<ISwitchExpressionArmOperation> GetReachableArms(
        ISwitchExpressionOperation operation,
        Func<IOperation?, bool> canCompleteNormally,
        bool inputDefinitelyNonNull = false,
        bool valueAlreadyComplete = false)
    {
        return GetArms(
            operation,
            canCompleteNormally,
            inputDefinitelyNonNull,
            patternOnly: false,
            valueAlreadyComplete: valueAlreadyComplete);
    }

    internal static IReadOnlyList<ISwitchExpressionArmOperation>
        GetEvaluatedPatternOnlyArms(
            ISwitchExpressionOperation operation,
            Func<IOperation?, bool> canCompleteNormally,
            bool inputDefinitelyNonNull = false,
            bool valueAlreadyComplete = false)
    {
        return GetArms(
            operation,
            canCompleteNormally,
            inputDefinitelyNonNull,
            patternOnly: true,
            valueAlreadyComplete: valueAlreadyComplete);
    }

    private static List<ISwitchExpressionArmOperation> GetArms(
        ISwitchExpressionOperation operation,
        Func<IOperation?, bool> canCompleteNormally,
        bool inputDefinitelyNonNull,
        bool patternOnly,
        bool valueAlreadyComplete)
    {
        if (!valueAlreadyComplete && !canCompleteNormally(operation.Value))
        {
            return [];
        }

        inputDefinitelyNonNull |=
            DefiniteOperationFacts.IsDefinitelyNonNull(operation.Value);

        if (operation.Value.ConstantValue is not { HasValue: true } constant)
        {
            return GetArmsForUnknownValue(
                operation,
                canCompleteNormally,
                inputDefinitelyNonNull,
                patternOnly);
        }

        var reachable = new List<ISwitchExpressionArmOperation>();
        foreach (var arm in operation.Arms)
        {
            var pattern = GetPatternSelection(arm.Pattern, constant.Value);
            var selection = GetArmSelection(arm, constant.Value);
            if (ShouldIncludeArm(selection, patternOnly))
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

    private static bool ShouldIncludeArm(
        SwitchExpressionSelection selection,
        bool patternOnly)
    {
        return patternOnly
            ? selection == SwitchExpressionSelection.Never
            : selection != SwitchExpressionSelection.Never;
    }

    internal static bool HasReachableUnmatchedPath(
        ISwitchExpressionOperation operation,
        Func<IOperation?, bool> canCompleteNormally,
        bool inputDefinitelyNonNull = false,
        bool valueAlreadyComplete = false)
    {
        if (!valueAlreadyComplete && !canCompleteNormally(operation.Value))
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
        GetArmsForUnknownValue(
            ISwitchExpressionOperation operation,
            Func<IOperation?, bool> canCompleteNormally,
            bool inputDefinitelyNonNull,
            bool patternOnly)
    {
        var reachable = new List<ISwitchExpressionArmOperation>();
        foreach (var arm in operation.Arms)
        {
            var pattern = GetPatternSelectionForUnknownValue(
                arm.Pattern,
                operation.Value.Type,
                inputDefinitelyNonNull);
            var selection = ApplyGuard(pattern, arm.Guard);
            if (ShouldIncludeArm(selection, patternOnly))
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
        var matchedType = GetMatchedType(pattern);
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
            var left = GetPatternEvaluationFacts(
                binary.LeftPattern,
                inputType,
                inputDefinitelyNonNull);
            return left.IsUnavoidable ||
                binary.OperatorKind == BinaryOperatorKind.And &&
                left.Selection == SwitchExpressionSelection.Always &&
                IsPatternEvaluationUnavoidable(
                    binary.RightPattern,
                    inputType,
                    inputDefinitelyNonNull) ||
                binary.OperatorKind == BinaryOperatorKind.Or &&
                left.Selection == SwitchExpressionSelection.Never &&
                IsPatternEvaluationUnavoidable(
                    binary.RightPattern,
                    inputType,
                    inputDefinitelyNonNull);
        }
        var matchedType = GetMatchedType(pattern);
        return (inputType?.IsValueType == true || inputDefinitelyNonNull) &&
            SymbolEqualityComparer.Default.Equals(matchedType, inputType);
    }

    private static PatternEvaluationFacts GetPatternEvaluationFacts(
        IPatternOperation pattern,
        ITypeSymbol? inputType,
        bool inputDefinitelyNonNull)
    {
        if (pattern is IConstantPatternOperation
            { Value.ConstantValue: { HasValue: true, Value: null } }
            && inputDefinitelyNonNull)
        {
            return new(
                SwitchExpressionSelection.Never,
                false);
        }

        if (pattern is IDiscardPatternOperation or
            IDeclarationPatternOperation { MatchesNull: true })
        {
            return new(
                SwitchExpressionSelection.Always,
                true);
        }

        if (pattern is IListPatternOperation)
        {
            return new(
                IsTotalPattern(
                    pattern,
                    inputType,
                    inputDefinitelyNonNull)
                    ? SwitchExpressionSelection.Always
                    : SwitchExpressionSelection.Maybe,
                inputType?.IsValueType == true || inputDefinitelyNonNull);
        }

        if (pattern is INegatedPatternOperation negated)
        {
            var nested = GetPatternEvaluationFacts(
                negated.Pattern,
                inputType,
                inputDefinitelyNonNull);
            return new(
                Negate(nested.Selection),
                nested.IsUnavoidable);
        }

        if (pattern is IBinaryPatternOperation binary)
        {
            if (binary.OperatorKind is not
                (BinaryOperatorKind.And or BinaryOperatorKind.Or))
            {
                return new(
                    GetPatternSelectionForUnknownValue(
                        pattern,
                        inputType,
                        inputDefinitelyNonNull),
                    IsPatternEvaluationUnavoidable(
                        binary.LeftPattern,
                        inputType,
                        inputDefinitelyNonNull));
            }

            var left = GetPatternEvaluationFacts(
                binary.LeftPattern,
                inputType,
                inputDefinitelyNonNull);
            var right = GetPatternEvaluationFacts(
                binary.RightPattern,
                inputType,
                inputDefinitelyNonNull);
            var combinedSelection = binary.OperatorKind == BinaryOperatorKind.And
                ? And(left.Selection, right.Selection)
                : Or(left.Selection, right.Selection);
            var rightRequired = binary.OperatorKind == BinaryOperatorKind.And
                ? left.Selection == SwitchExpressionSelection.Always
                : left.Selection == SwitchExpressionSelection.Never;
            return new(
                combinedSelection,
                left.IsUnavoidable ||
                rightRequired && right.IsUnavoidable);
        }

        var selection = IsTotalPattern(
            pattern,
            inputType,
            inputDefinitelyNonNull)
                ? SwitchExpressionSelection.Always
                : SwitchExpressionSelection.Maybe;
        var matchedType = GetMatchedType(pattern);
        return new(
            selection,
            (inputType?.IsValueType == true || inputDefinitelyNonNull) &&
            SymbolEqualityComparer.Default.Equals(matchedType, inputType));
    }

    private readonly record struct PatternEvaluationFacts(
        SwitchExpressionSelection Selection,
        bool IsUnavoidable);

    private static ITypeSymbol? GetMatchedType(IPatternOperation pattern)
    {
        return pattern switch
        {
            ITypePatternOperation typePattern => typePattern.MatchedType,
            IDeclarationPatternOperation declarationPattern =>
                declarationPattern.MatchedType,
            IRecursivePatternOperation recursive => recursive.MatchedType,
            _ => null
        };
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

    internal static SwitchExpressionSelection GetPatternSelection(
        Compilation compilation,
        IPatternOperation pattern,
        object? value,
        ITypeSymbol? inputType)
    {
        return pattern switch
        {
            IDiscardPatternOperation => SwitchExpressionSelection.Always,
            ITypePatternOperation typePattern => MatchTypePattern(
                compilation,
                typePattern.MatchedType,
                value,
                inputType,
                matchesNull: false),
            IDeclarationPatternOperation
            { MatchedType: { } matchedType } declaration => MatchTypePattern(
                compilation,
                matchedType,
                value,
                inputType,
                declaration.MatchesNull),
            IDeclarationPatternOperation => SwitchExpressionSelection.Maybe,
            IConstantPatternOperation constant
                when constant.Value.ConstantValue is { HasValue: true } item =>
                Equals(value, item.Value)
                    ? SwitchExpressionSelection.Always
                    : SwitchExpressionSelection.Never,
            IRelationalPatternOperation relational =>
                MatchRelationalPattern(relational, value),
            INegatedPatternOperation negated => Negate(GetPatternSelection(
                compilation,
                negated.Pattern,
                value,
                inputType)),
            IBinaryPatternOperation binary
                when binary.OperatorKind == BinaryOperatorKind.And => And(
                    GetPatternSelection(
                        compilation,
                        binary.LeftPattern,
                        value,
                        inputType),
                    GetPatternSelection(
                        compilation,
                        binary.RightPattern,
                        value,
                        inputType)),
            IBinaryPatternOperation binary
                when binary.OperatorKind == BinaryOperatorKind.Or => Or(
                    GetPatternSelection(
                        compilation,
                        binary.LeftPattern,
                        value,
                        inputType),
                    GetPatternSelection(
                        compilation,
                        binary.RightPattern,
                        value,
                        inputType)),
            _ => SwitchExpressionSelection.Maybe
        };
    }

    private static SwitchExpressionSelection MatchTypePattern(
        Compilation compilation,
        ITypeSymbol matchedType,
        object? value,
        ITypeSymbol? inputType,
        bool matchesNull)
    {
        if (value == null)
        {
            return matchesNull
                ? SwitchExpressionSelection.Always
                : SwitchExpressionSelection.Never;
        }
        var actualType = inputType?.TypeKind == TypeKind.Enum
            ? inputType
            : value switch
            {
                bool => compilation.GetSpecialType(SpecialType.System_Boolean),
                byte => compilation.GetSpecialType(SpecialType.System_Byte),
                sbyte => compilation.GetSpecialType(SpecialType.System_SByte),
                short => compilation.GetSpecialType(SpecialType.System_Int16),
                ushort => compilation.GetSpecialType(SpecialType.System_UInt16),
                int => compilation.GetSpecialType(SpecialType.System_Int32),
                uint => compilation.GetSpecialType(SpecialType.System_UInt32),
                long => compilation.GetSpecialType(SpecialType.System_Int64),
                ulong => compilation.GetSpecialType(SpecialType.System_UInt64),
                char => compilation.GetSpecialType(SpecialType.System_Char),
                float => compilation.GetSpecialType(SpecialType.System_Single),
                double => compilation.GetSpecialType(SpecialType.System_Double),
                decimal => compilation.GetSpecialType(SpecialType.System_Decimal),
                string => compilation.GetSpecialType(SpecialType.System_String),
                _ => null
            };
        if (actualType == null || actualType.TypeKind == TypeKind.Error)
        {
            return SwitchExpressionSelection.Maybe;
        }
        return compilation.ClassifyCommonConversion(actualType, matchedType).IsImplicit
            ? SwitchExpressionSelection.Always
            : SwitchExpressionSelection.Never;
    }

    private static SwitchExpressionSelection MatchRelationalPattern(
        IRelationalPatternOperation pattern,
        object? value)
    {
        var constantValue = pattern.Value.ConstantValue;
        if (value is not IComparable comparable ||
            !constantValue.HasValue ||
            constantValue.Value == null)
        {
            return SwitchExpressionSelection.Never;
        }
        var constant = constantValue.Value;
        if (value is double valueDouble && double.IsNaN(valueDouble) ||
            constant is double constantDouble && double.IsNaN(constantDouble) ||
            value is float valueFloat && float.IsNaN(valueFloat) ||
            constant is float constantFloat && float.IsNaN(constantFloat))
        {
            return SwitchExpressionSelection.Never;
        }

        int comparison;
        try
        {
            comparison = comparable.CompareTo(constant);
        }
        catch (ArgumentException)
        {
            return SwitchExpressionSelection.Maybe;
        }

        var matches = Matches(pattern.OperatorKind, comparison);
        return matches
            ? SwitchExpressionSelection.Always
            : SwitchExpressionSelection.Never;
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
        return double.IsNaN(left) || double.IsNaN(right)
            ? false
            : Matches(operatorKind, left.CompareTo(right));
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
