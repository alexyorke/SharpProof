namespace SharpProof.Specs;

/// <summary>
/// Type-checks trusted specification expressions and proves their totality
/// under the declaration's normal-return facts.
/// </summary>
internal static class ApiSpecTermValidator
{
    internal static TermFacts Validate(
        SpecTermDeclaration declaration,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables,
        ApiSpecFacets facets)
    {
        if (declaration == null)
        {
            throw new ArgumentException(
                "Spec expressions cannot contain null.",
                nameof(declaration));
        }

        switch (declaration)
        {
            case SpecVariableDeclaration variable:
                if (!variables.TryGetValue((variable.Role, variable.Ordinal), out var info))
                {
                    throw new ArgumentException(
                        "The spec expression references an unavailable variable slot.",
                        nameof(declaration));
                }

                if (info.Type != variable.Type)
                {
                    throw new ArgumentException(
                        "The spec variable declaration has the wrong type.",
                        nameof(declaration));
                }

                var nonNull = info.Role == SpecVariableRole.Receiver ||
                    info.Role == SpecVariableRole.Result &&
                    (facets.Nullness.Result == SpecNullness.NonNull ||
                     facets.Cardinality.Result is
                         SpecCardinality.Empty or
                         SpecCardinality.NonEmpty or
                         SpecCardinality.Exact);
                return new(info.Type, true, nonNull, null, null);
            case SpecBooleanDeclaration boolean:
                return new(boolean.Type, true, false, null, boolean.Value);
            case SpecIntegerDeclaration integer:
                return new(integer.Type, true, false, integer.Value, null);
            case SpecStringDeclaration text:
                if (text.Value == null)
                {
                    throw new ArgumentException(
                        "String constants cannot be null.",
                        nameof(declaration));
                }

                return new(text.Type, true, true, null, null);
            case SpecNullDeclaration nullValue:
                if (!IrTermServices.IsNullable(nullValue.Type))
                {
                    throw new ArgumentException(
                        "Null requires a nullable spec type.",
                        nameof(declaration));
                }

                return new(nullValue.Type, true, false, null, null);
            case SpecUnaryDeclaration unary:
                return ValidateUnary(unary, variables, facets);
            case SpecBinaryDeclaration binary:
                return ValidateBinary(binary, variables, facets);
            case SpecConditionalDeclaration conditional:
                return ValidateConditional(conditional, variables, facets);
            case SpecLengthDeclaration length:
                var value = Validate(length.Value, variables, facets);
                if (value.Type is not (
                    IrTypeKind.String or IrTypeKind.Sequence))
                {
                    throw new ArgumentException(
                        "Length requires a string or sequence.",
                        nameof(declaration));
                }

                return new(
                    length.Type,
                    value.IsTotal && value.IsNonNull,
                    false,
                    null,
                    null);
            default:
                throw new ArgumentException(
                    "Unsupported spec expression declaration.",
                    nameof(declaration));
        }
    }

    private static TermFacts ValidateUnary(
        SpecUnaryDeclaration unary,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables,
        ApiSpecFacets facets)
    {
        var operand = Validate(unary.Operand, variables, facets);
        var expected = IrOperatorCatalog.Get(unary.Operator).Operand;
        if (operand.Type != expected || unary.Type != expected)
        {
            throw new ArgumentException(
                "Invalid unary spec expression types.",
                nameof(unary));
        }

        long? integer = null;
        if (unary.Operator == IrUnaryOperator.Negate &&
            operand.Integer is { } value &&
            TryNegate(value, out var negated))
        {
            integer = negated;
        }

        return new(
            expected,
            unary.Operator == IrUnaryOperator.Not
                ? operand.IsTotal
                : integer.HasValue,
            false,
            integer,
            unary.Operator == IrUnaryOperator.Not
                ? operand.Boolean is { } booleanValue
                    ? !booleanValue
                    : null
                : null);
    }

    private static TermFacts ValidateBinary(
        SpecBinaryDeclaration binary,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables,
        ApiSpecFacets facets)
    {
        var left = Validate(binary.Left, variables, facets);
        var right = Validate(binary.Right, variables, facets);
        var shape = IrOperatorCatalog.Get(binary.Operator);
        var operandTypesMatch = shape.Operand.HasValue
            ? left.Type == shape.Operand.Value &&
              right.Type == shape.Operand.Value
            : left.Type == right.Type;
        if (!operandTypesMatch)
        {
            throw new ArgumentException(
                "Invalid binary spec expression types.",
                nameof(binary));
        }

        if (binary.Type != shape.Result)
        {
            throw new ArgumentException(
                "The binary spec result type is incorrect.",
                nameof(binary));
        }

        var arithmetic =
            shape.Operand == IrTypeKind.Integer &&
            shape.Result == IrTypeKind.Integer;
        long? integer = null;
        if (arithmetic &&
            left.Integer is { } leftValue &&
            right.Integer is { } rightValue &&
            TryArithmetic(binary.Operator, leftValue, rightValue, out var result))
        {
            integer = result;
        }

        var isTotal = arithmetic
            ? integer.HasValue
            : left.IsTotal && right.IsTotal;
        if (binary.Operator is IrBinaryOperator.AndAlso or IrBinaryOperator.OrElse)
        {
            isTotal = left.Boolean is { } leftBoolean
                ? left.IsTotal &&
                  (binary.Operator == IrBinaryOperator.AndAlso
                      ? !leftBoolean || right.IsTotal
                      : leftBoolean || right.IsTotal)
                : left.IsTotal && right.IsTotal;
        }

        return new(
            shape.Result,
            isTotal,
            binary.Operator == IrBinaryOperator.StringConcat,
            integer,
            TryBoolean(binary.Operator, left, right));
    }

    private static TermFacts ValidateConditional(
        SpecConditionalDeclaration conditional,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables,
        ApiSpecFacets facets)
    {
        var condition = Validate(conditional.Condition, variables, facets);
        var whenTrue = Validate(conditional.WhenTrue, variables, facets);
        var whenFalse = Validate(conditional.WhenFalse, variables, facets);
        if (condition.Type != IrTypeKind.Boolean ||
            whenTrue.Type != whenFalse.Type ||
            conditional.Type != whenTrue.Type)
        {
            throw new ArgumentException(
                "Invalid conditional spec expression types.",
                nameof(conditional));
        }

        return condition.Boolean switch
        {
            true => new(
                conditional.Type,
                condition.IsTotal && whenTrue.IsTotal,
                whenTrue.IsNonNull,
                whenTrue.Integer,
                whenTrue.Boolean),
            false => new(
                conditional.Type,
                condition.IsTotal && whenFalse.IsTotal,
                whenFalse.IsNonNull,
                whenFalse.Integer,
                whenFalse.Boolean),
            _ => new(
                conditional.Type,
                condition.IsTotal && whenTrue.IsTotal && whenFalse.IsTotal,
                whenTrue.IsNonNull && whenFalse.IsNonNull,
                null,
                whenTrue.Boolean == whenFalse.Boolean
                    ? whenTrue.Boolean
                    : null)
        };
    }

    private static bool? TryBoolean(
        IrBinaryOperator @operator,
        TermFacts left,
        TermFacts right)
    {
        if (@operator == IrBinaryOperator.AndAlso && left.Boolean is { } leftAnd)
        {
            return leftAnd ? right.Boolean : false;
        }

        if (@operator == IrBinaryOperator.OrElse && left.Boolean is { } leftOr)
        {
            return leftOr ? true : right.Boolean;
        }

        if (left.Boolean is { } leftBoolean &&
            right.Boolean is { } rightBoolean)
        {
            return @operator switch
            {
                IrBinaryOperator.Equal => leftBoolean == rightBoolean,
                IrBinaryOperator.NotEqual => leftBoolean != rightBoolean,
                _ => null
            };
        }

        if (left.Integer is { } leftInteger &&
            right.Integer is { } rightInteger)
        {
            return @operator switch
            {
                IrBinaryOperator.Equal => leftInteger == rightInteger,
                IrBinaryOperator.NotEqual => leftInteger != rightInteger,
                IrBinaryOperator.LessThan => leftInteger < rightInteger,
                IrBinaryOperator.LessThanOrEqual => leftInteger <= rightInteger,
                IrBinaryOperator.GreaterThan => leftInteger > rightInteger,
                IrBinaryOperator.GreaterThanOrEqual => leftInteger >= rightInteger,
                _ => null
            };
        }

        return null;
    }

    private static bool TryNegate(long value, out long result)
    {
        try
        {
            result = checked(-value);
            return true;
        }
        catch (OverflowException)
        {
            result = 0;
            return false;
        }
    }

    private static bool TryArithmetic(
        IrBinaryOperator @operator,
        long left,
        long right,
        out long result)
    {
        var evaluated = IrScalarOperations.Evaluate(@operator, left, right);
        result = evaluated.Value;
        return evaluated.Kind == IrScalarResultKind.Integer;
    }

    internal readonly record struct TermFacts(
        IrTypeKind Type,
        bool IsTotal,
        bool IsNonNull,
        long? Integer,
        bool? Boolean);
}
