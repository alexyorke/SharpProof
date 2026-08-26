namespace SharpProof.Ir;

/// <summary>
/// Owns term-shape validation and semantics-preserving constant folding for
/// <see cref="IrFactory"/>.
/// </summary>
internal static class IrTermServices
{
    internal static void ValidateCallShape(
        IrFactory factory,
        IrMemberInfo member,
        IrTerm? receiver,
        IReadOnlyList<IrTerm> arguments,
        string parameterName,
        bool opaque)
    {
        var receiverParameter = opaque ? nameof(receiver) : parameterName;
        if (member.IsStatic && receiver != null)
        {
            throw new ArgumentException(
                "A static member cannot have a receiver.",
                receiverParameter);
        }

        if (!member.IsStatic && receiver == null)
        {
            if (opaque)
            {
                ArgumentNullGuard.NotNull(
                    receiver,
                    nameof(receiver),
                    "An instance member requires a receiver.");
            }

            throw new ArgumentException(
                "An instance member requires a receiver.",
                parameterName);
        }

        if (receiver != null)
        {
            factory.EnsureTerm(receiver, receiverParameter);
            if (receiver.Type != member.DeclaringType)
            {
                throw new ArgumentException(
                    "An instance receiver must match the member declaring type.",
                    receiverParameter);
            }
        }

        if (arguments.Count != member.ParameterTypes.Length)
        {
            throw new ArgumentException(
                "The argument count does not match the member signature.",
                parameterName);
        }

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index] ??
                throw new ArgumentException(
                    opaque
                        ? "Opaque arguments cannot contain null."
                        : "Arguments cannot contain null.",
                    parameterName);
            factory.EnsureTerm(argument, parameterName);
            if (argument.Type != member.ParameterTypes[index])
            {
                throw new ArgumentException(
                    opaque
                        ? "An opaque argument type does not match the member signature."
                        : "An argument does not match the member signature.",
                    parameterName);
            }
        }
    }

    internal static IrTypeId ValidateSequenceTerms(
        IrFactory factory,
        IrTerm sequence,
        IrTerm index,
        string sequenceMessage,
        string indexMessage,
        string sequenceParameter,
        string indexParameter)
    {
        ArgumentNullGuard.NotNull(sequence, sequenceParameter);
        ArgumentNullGuard.NotNull(index, indexParameter);

        factory.EnsureTerm(sequence, sequenceParameter);
        factory.EnsureTerm(index, indexParameter);
        var type = factory.GetTypeInfo(sequence.Type);
        if (type.Kind != IrTypeKind.Sequence || type.ElementType == null)
        {
            throw new ArgumentException(sequenceMessage, sequenceParameter);
        }

        if (index.Type != factory.IntegerType)
        {
            throw new ArgumentException(indexMessage, indexParameter);
        }

        return type.ElementType.Value;
    }

    internal static IrTerm? FoldUnary(
        IrFactory factory,
        IrUnaryOperator @operator,
        IrTerm operand)
    {
        return (@operator, operand) switch
        {
            (IrUnaryOperator.Not, IrBooleanTerm value) =>
                factory.Boolean(!value.Value),
            (IrUnaryOperator.Negate, IrIntegerTerm { Value: not long.MinValue } value) =>
                factory.Integer(-value.Value),
            _ => null
        };
    }

    internal static IrTerm? FoldBinary(
        IrFactory factory,
        IrBinaryOperator @operator,
        IrTerm left,
        IrTerm right)
    {
        if (@operator == IrBinaryOperator.AndAlso &&
            left is IrBooleanTerm andLeft)
        {
            return andLeft.Value ? right : left;
        }

        if (@operator == IrBinaryOperator.OrElse &&
            left is IrBooleanTerm orLeft)
        {
            return orLeft.Value ? left : right;
        }

        if (@operator is IrBinaryOperator.Equal or IrBinaryOperator.NotEqual)
        {
            var equal = TryCompareConstants(left, right);
            if (equal.HasValue)
            {
                return factory.Boolean(
                    @operator == IrBinaryOperator.Equal
                        ? equal.Value
                        : !equal.Value);
            }
        }

        if (left is IrIntegerTerm leftInteger &&
            right is IrIntegerTerm rightInteger)
        {
            return FoldIntegerBinary(
                factory,
                @operator,
                leftInteger.Value,
                rightInteger.Value);
        }

        if (left is IrStringTerm leftString &&
            right is IrStringTerm rightString &&
            @operator == IrBinaryOperator.StringConcat)
        {
            return factory.String(
                factory.GetString(leftString.Value) +
                factory.GetString(rightString.Value));
        }

        return null;
    }

    internal static IrTypeId ValidateBinaryAndGetResultType(
        IrFactory factory,
        IrBinaryOperator @operator,
        IrTypeKind? operandKind,
        IrTypeKind resultKind,
        IrTerm left,
        IrTerm right)
    {
        if (!operandKind.HasValue)
        {
            if (left.Type != right.Type)
            {
                throw new ArgumentException(
                    "Equality operands must have the same type.",
                    nameof(right));
            }

            return GetBuiltInType(factory, resultKind);
        }

        return RequireTypes(
            left,
            right,
            GetBuiltInType(factory, operandKind.Value),
            GetBuiltInType(factory, resultKind),
            @operator);
    }

    internal static bool IsNullable(IrTypeKind kind)
    {
        return IrOperatorCatalog.IsNullable(kind);
    }

    internal static void ValidateCast(
        IrTypeKind targetKind,
        IrTypeKind operandKind)
    {
        if (!IsNullable(targetKind) || !IsNullable(operandKind))
        {
            throw new ArgumentException(
                "Casts require reference-like source and target types.");
        }
    }

    private static IrTerm? FoldIntegerBinary(
        IrFactory factory,
        IrBinaryOperator @operator,
        long left,
        long right)
    {
        var result = IrScalarOperations.Evaluate(@operator, left, right);
        return result.Kind switch
        {
            IrScalarResultKind.Integer => factory.Integer(result.Value),
            IrScalarResultKind.Boolean => factory.Boolean(result.Value != 0),
            _ => null
        };
    }

    private static bool? TryCompareConstants(IrTerm left, IrTerm right)
    {
        if (left is IrBooleanTerm leftBoolean &&
            right is IrBooleanTerm rightBoolean)
        {
            return leftBoolean.Value == rightBoolean.Value;
        }

        if (left is IrIntegerTerm leftInteger &&
            right is IrIntegerTerm rightInteger)
        {
            return leftInteger.Value == rightInteger.Value;
        }

        if (left is IrStringTerm leftString &&
            right is IrStringTerm rightString)
        {
            return leftString.Value == rightString.Value;
        }

        if (left is IrNullTerm && right is IrNullTerm)
        {
            return true;
        }

        if (left is IrNullTerm && IsNonNullLiteral(right) ||
            right is IrNullTerm && IsNonNullLiteral(left))
        {
            return false;
        }

        return null;
    }

    private static IrTypeId RequireTypes(
        IrTerm left,
        IrTerm right,
        IrTypeId expected,
        IrTypeId result,
        IrBinaryOperator @operator)
    {
        if (left.Type != expected || right.Type != expected)
        {
            throw new ArgumentException(
                "Operands are not valid for binary operator " + @operator + ".",
                nameof(right));
        }

        return result;
    }

    private static bool IsNonNullLiteral(IrTerm term)
    {
        return term is IrBooleanTerm or IrIntegerTerm or IrStringTerm;
    }

    internal static IrTypeId GetBuiltInType(
        IrFactory factory,
        IrTypeKind kind)
    {
        return IrOperatorCatalog.GetBuiltInType(factory, kind);
    }
}
