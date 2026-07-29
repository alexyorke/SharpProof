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
                return new(info.Type, true, nonNull, null);
            case SpecBooleanDeclaration boolean:
                return new(boolean.Type, true, false, null);
            case SpecIntegerDeclaration integer:
                return new(integer.Type, true, false, integer.Value);
            case SpecStringDeclaration text:
                if (text.Value == null)
                {
                    throw new ArgumentException(
                        "String constants cannot be null.",
                        nameof(declaration));
                }

                return new(text.Type, true, true, null);
            case SpecNullDeclaration nullValue:
                if (nullValue.Type is not (
                    SpecValueType.String or
                    SpecValueType.Reference or
                    SpecValueType.Sequence))
                {
                    throw new ArgumentException(
                        "Null requires a nullable spec type.",
                        nameof(declaration));
                }

                return new(nullValue.Type, true, false, null);
            case SpecUnaryDeclaration unary:
                return ValidateUnary(unary, variables, facets);
            case SpecBinaryDeclaration binary:
                return ValidateBinary(binary, variables, facets);
            case SpecConditionalDeclaration conditional:
                return ValidateConditional(conditional, variables, facets);
            case SpecLengthDeclaration length:
                var value = Validate(length.Value, variables, facets);
                if (value.Type is not (
                    SpecValueType.String or SpecValueType.Sequence))
                {
                    throw new ArgumentException(
                        "Length requires a string or sequence.",
                        nameof(declaration));
                }

                return new(
                    length.Type,
                    value.IsTotal && value.IsNonNull,
                    false,
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
        var expected = unary.Operator switch
        {
            SpecUnaryOperator.Not => SpecValueType.Boolean,
            SpecUnaryOperator.Negate => SpecValueType.Integer,
            _ => throw new ArgumentOutOfRangeException(nameof(unary))
        };
        if (operand.Type != expected || unary.Type != expected)
        {
            throw new ArgumentException(
                "Invalid unary spec expression types.",
                nameof(unary));
        }

        long? integer = null;
        if (unary.Operator == SpecUnaryOperator.Negate &&
            operand.Integer is { } value &&
            TryNegate(value, out var negated))
        {
            integer = negated;
        }

        return new(
            expected,
            unary.Operator == SpecUnaryOperator.Not
                ? operand.IsTotal
                : integer.HasValue,
            false,
            integer);
    }

    private static TermFacts ValidateBinary(
        SpecBinaryDeclaration binary,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables,
        ApiSpecFacets facets)
    {
        var left = Validate(binary.Left, variables, facets);
        var right = Validate(binary.Right, variables, facets);
        var resultType = binary.Operator switch
        {
            SpecBinaryOperator.Add or
                SpecBinaryOperator.Subtract or
                SpecBinaryOperator.Multiply or
                SpecBinaryOperator.Divide or
                SpecBinaryOperator.Remainder
                when left.Type == SpecValueType.Integer &&
                     right.Type == SpecValueType.Integer =>
                SpecValueType.Integer,
            SpecBinaryOperator.AndAlso or SpecBinaryOperator.OrElse
                when left.Type == SpecValueType.Boolean &&
                     right.Type == SpecValueType.Boolean =>
                SpecValueType.Boolean,
            SpecBinaryOperator.Equal or SpecBinaryOperator.NotEqual
                when left.Type == right.Type =>
                SpecValueType.Boolean,
            SpecBinaryOperator.LessThan or
                SpecBinaryOperator.LessThanOrEqual or
                SpecBinaryOperator.GreaterThan or
                SpecBinaryOperator.GreaterThanOrEqual
                when left.Type == SpecValueType.Integer &&
                     right.Type == SpecValueType.Integer =>
                SpecValueType.Boolean,
            SpecBinaryOperator.StringConcat
                when left.Type == SpecValueType.String &&
                     right.Type == SpecValueType.String =>
                SpecValueType.String,
            _ => throw new ArgumentException(
                "Invalid binary spec expression types.",
                nameof(binary))
        };
        if (binary.Type != resultType)
        {
            throw new ArgumentException(
                "The binary spec result type is incorrect.",
                nameof(binary));
        }

        var arithmetic = binary.Operator is
            SpecBinaryOperator.Add or
            SpecBinaryOperator.Subtract or
            SpecBinaryOperator.Multiply or
            SpecBinaryOperator.Divide or
            SpecBinaryOperator.Remainder;
        long? integer = null;
        if (arithmetic &&
            left.Integer is { } leftValue &&
            right.Integer is { } rightValue &&
            TryArithmetic(binary.Operator, leftValue, rightValue, out var result))
        {
            integer = result;
        }

        return new(
            resultType,
            arithmetic ? integer.HasValue : left.IsTotal && right.IsTotal,
            binary.Operator == SpecBinaryOperator.StringConcat,
            integer);
    }

    private static TermFacts ValidateConditional(
        SpecConditionalDeclaration conditional,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables,
        ApiSpecFacets facets)
    {
        var condition = Validate(conditional.Condition, variables, facets);
        var whenTrue = Validate(conditional.WhenTrue, variables, facets);
        var whenFalse = Validate(conditional.WhenFalse, variables, facets);
        if (condition.Type != SpecValueType.Boolean ||
            whenTrue.Type != whenFalse.Type ||
            conditional.Type != whenTrue.Type)
        {
            throw new ArgumentException(
                "Invalid conditional spec expression types.",
                nameof(conditional));
        }

        return new(
            conditional.Type,
            condition.IsTotal && whenTrue.IsTotal && whenFalse.IsTotal,
            whenTrue.IsNonNull && whenFalse.IsNonNull,
            null);
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
        SpecBinaryOperator @operator,
        long left,
        long right,
        out long result)
    {
        try
        {
            result = @operator switch
            {
                SpecBinaryOperator.Add => checked(left + right),
                SpecBinaryOperator.Subtract => checked(left - right),
                SpecBinaryOperator.Multiply => checked(left * right),
                SpecBinaryOperator.Divide => left / right,
                SpecBinaryOperator.Remainder => left % right,
                _ => throw new ArgumentOutOfRangeException(nameof(@operator))
            };
            return true;
        }
        catch (ArithmeticException)
        {
            result = 0;
            return false;
        }
    }

    internal readonly record struct TermFacts(
        SpecValueType Type,
        bool IsTotal,
        bool IsNonNull,
        long? Integer);
}
