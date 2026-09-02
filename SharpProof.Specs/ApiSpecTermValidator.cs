namespace SharpProof.Specs;

/// <summary>
/// Type-checks trusted specification expressions and proves their totality
/// under the declaration's normal-return facts.
/// </summary>
internal static class ApiSpecTermValidator
{
    private const int MaximumExpressionDepth = 256;

    internal static TermFacts Validate(
        SpecTermDeclaration declaration,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables,
        ApiSpecFacets facets)
    {
        return new ValidationContext(variables, facets).Validate(declaration, depth: 1);
    }

    private sealed class ValidationContext(
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables,
        ApiSpecFacets facets)
    {
        private readonly Dictionary<SpecTermDeclaration, TermFacts> _validated =
            new(DeclarationReferenceComparer.Instance);

        internal TermFacts Validate(SpecTermDeclaration declaration, int depth)
        {
            if (declaration == null)
            {
                throw new ArgumentException(
                    "Spec expressions cannot contain null.",
                    nameof(declaration));
            }

            if (depth > MaximumExpressionDepth)
            {
                throw new ArgumentException(
                    "Spec expressions exceed the expression depth limit.",
                    nameof(declaration));
            }

            if (_validated.TryGetValue(declaration, out var facts))
            {
                return facts;
            }

            facts = ValidateCore(declaration, depth);
            _validated.Add(declaration, facts);
            return facts;
        }

        private TermFacts ValidateCore(SpecTermDeclaration declaration, int depth)
        {
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
                        facets.Nullness.Result == SpecNullness.NonNull;
                    return new(info.Type, true, nonNull, null);
                case SpecBooleanDeclaration boolean:
                    return new(
                        boolean.Type,
                        true,
                        false,
                        null,
                        boolean.Value);
                case SpecIntegerDeclaration integer:
                    return new(integer.Type, true, false, integer.Value);
                case SpecStringDeclaration text:
                    if (text.Value == null)
                    {
                        throw new ArgumentException(
                            "String constants cannot be null.",
                            nameof(declaration));
                    }

                    if (!Utf16WellFormedness.IsWellFormed(text.Value))
                    {
                        throw new ArgumentException(
                            "String constants require well-formed UTF-16.",
                            nameof(declaration));
                    }

                    return new(text.Type, true, true, null);
                case SpecNullDeclaration nullValue:
                    if (!IrOperatorCatalog.IsNullable(nullValue.Type))
                    {
                        throw new ArgumentException(
                            "Null requires a nullable spec type.",
                            nameof(declaration));
                    }

                    return new(nullValue.Type, true, false, null);
                case SpecUnaryDeclaration unary:
                    return ValidateUnary(unary, depth);
                case SpecBinaryDeclaration binary:
                    return ValidateBinary(binary, depth);
                case SpecConditionalDeclaration conditional:
                    return ValidateConditional(conditional, depth);
                case SpecLengthDeclaration length:
                    var value = Validate(length.Value, depth + 1);
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
                        null);
                default:
                    throw new ArgumentException(
                        "Unsupported spec expression declaration.",
                        nameof(declaration));
            }
        }

        private TermFacts ValidateUnary(SpecUnaryDeclaration unary, int depth)
        {
            var operand = Validate(unary.Operand, depth + 1);
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
                unary.Operator == IrUnaryOperator.Not &&
                operand.Boolean is { } boolean
                    ? !boolean
                    : null);
        }

        private TermFacts ValidateBinary(SpecBinaryDeclaration binary, int depth)
        {
            var left = Validate(binary.Left, depth + 1);
            var right = Validate(binary.Right, depth + 1);
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

            var boolean = binary.Operator switch
            {
                IrBinaryOperator.AndAlso when left.Boolean == false => false,
                IrBinaryOperator.AndAlso when left.Boolean == true => right.Boolean,
                IrBinaryOperator.OrElse when left.Boolean == true => true,
                IrBinaryOperator.OrElse when left.Boolean == false => right.Boolean,
                _ => null
            };
            return new(
                shape.Result,
                arithmetic
                    ? integer.HasValue
                    : IsBinaryTotal(binary.Operator, left, right),
                binary.Operator == IrBinaryOperator.StringConcat,
                integer,
                boolean);
        }

        private TermFacts ValidateConditional(
            SpecConditionalDeclaration conditional, int depth)
        {
            var condition = Validate(conditional.Condition, depth + 1);
            var whenTrue = Validate(conditional.WhenTrue, depth + 1);
            var whenFalse = Validate(conditional.WhenFalse, depth + 1);
            if (condition.Type != IrTypeKind.Boolean ||
                whenTrue.Type != whenFalse.Type ||
                conditional.Type != whenTrue.Type)
            {
                throw new ArgumentException(
                    "Invalid conditional spec expression types.",
                    nameof(conditional));
            }

            var selected = condition.Boolean switch
            {
                true => whenTrue,
                false => whenFalse,
                null => (TermFacts?)null
            };
            return selected is { } branch
                ? new(
                    conditional.Type,
                    condition.IsTotal && branch.IsTotal,
                    branch.IsNonNull,
                    branch.Integer,
                    branch.Boolean)
                : new(
                    conditional.Type,
                    condition.IsTotal && whenTrue.IsTotal && whenFalse.IsTotal,
                    whenTrue.IsNonNull && whenFalse.IsNonNull,
                    null);
        }
    }

    private static bool IsBinaryTotal(
        IrBinaryOperator @operator,
        TermFacts left,
        TermFacts right)
    {
        return @operator switch
        {
            IrBinaryOperator.AndAlso =>
                left.IsTotal &&
                (left.Boolean == false || right.IsTotal),
            IrBinaryOperator.OrElse =>
                left.IsTotal &&
                (left.Boolean == true || right.IsTotal),
            _ => left.IsTotal && right.IsTotal
        };
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
        bool? Boolean = null);

    private sealed class DeclarationReferenceComparer :
        IEqualityComparer<SpecTermDeclaration>
    {
        internal static DeclarationReferenceComparer Instance { get; } =
            new();

        public bool Equals(
            SpecTermDeclaration? left,
            SpecTermDeclaration? right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(SpecTermDeclaration declaration)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(
                declaration);
        }
    }
}
