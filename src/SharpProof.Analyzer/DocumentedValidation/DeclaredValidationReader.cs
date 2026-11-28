namespace SharpProof.Analyzer;

// What the declarations on one parameter or return value establish. Unknown
// means a declaration exists that this reader cannot read, so no conclusion
// about the subject is safe and the analyzer stays silent.
internal sealed record DeclaredValidation(
    bool NonNull,
    long? Lower,
    long? Upper,
    bool Unknown)
{
    internal static DeclaredValidation None => new(NonNull: false, Lower: null, Upper: null, Unknown: false);

    internal static DeclaredValidation Opaque => new(NonNull: false, Lower: null, Upper: null, Unknown: true);

    internal DeclaredValidation And(DeclaredValidation other)
    {
        other = RepositoryParameterGuard.NotNull(other, nameof(other));
        return new(
            NonNull || other.NonNull,
            Tighter(Lower, other.Lower, Math.Max),
            Tighter(Upper, other.Upper, Math.Min),
            Unknown || other.Unknown);
    }

    private static long? Tighter(long? left, long? right, Func<long, long, long> choose)
    {
        return left is { } l && right is { } r ? choose(l, r) : left ?? right;
    }
}

internal static class IntegralDomain
{
    // The integral types the closed Positive and InRange contracts admit,
    // whose bounds fit in the long used by the claim grammar. ulong is excluded.
    internal static bool TryGet(ITypeSymbol type, out long minimum, out long maximum)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_SByte:
                (minimum, maximum) = (sbyte.MinValue, sbyte.MaxValue);
                return true;
            case SpecialType.System_Byte:
                (minimum, maximum) = (byte.MinValue, byte.MaxValue);
                return true;
            case SpecialType.System_Int16:
                (minimum, maximum) = (short.MinValue, short.MaxValue);
                return true;
            case SpecialType.System_UInt16:
                (minimum, maximum) = (ushort.MinValue, ushort.MaxValue);
                return true;
            case SpecialType.System_Int32:
                (minimum, maximum) = (int.MinValue, int.MaxValue);
                return true;
            case SpecialType.System_UInt32:
                (minimum, maximum) = (uint.MinValue, uint.MaxValue);
                return true;
            case SpecialType.System_Int64:
                (minimum, maximum) = (long.MinValue, long.MaxValue);
                return true;
            default:
                (minimum, maximum) = (0, 0);
                return false;
        }
    }
}

// Reads the SharpProof declarations on a method's parameters and return value.
// Attributes are recognized by resolved symbol, so a source type that merely
// shares a name is not evidence. Clauses are read from the same effective
// inventory SharpProof binds, and any clause this reader does not understand
// that mentions the subject makes the subject Unknown.
internal sealed class DeclaredValidationReader
{
    private readonly EffectiveContractSourceResolution _resolution;
    private readonly IMethodSymbol _method;
    private readonly INamedTypeSymbol? _nonNull;
    private readonly INamedTypeSymbol? _positive;
    private readonly INamedTypeSymbol? _inRange;
    private readonly INamedTypeSymbol? _contract;

    internal DeclaredValidationReader(
        Compilation compilation,
        IMethodSymbol method,
        EffectiveContractSourceResolution resolution)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        _method = RepositoryParameterGuard.NotNull(method, nameof(method));
        _resolution = RepositoryParameterGuard.NotNull(resolution, nameof(resolution));
        _nonNull = compilation.GetTypeByMetadataName("SharpProof.Attributes.NonNullAttribute");
        _positive = compilation.GetTypeByMetadataName("SharpProof.Attributes.PositiveAttribute");
        _inRange = compilation.GetTypeByMetadataName("SharpProof.Attributes.InRangeAttribute");
        _contract = compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract");
    }

    internal DeclaredValidation ForParameter(IParameterSymbol parameter)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        return Read(
            parameter.GetAttributes(),
            BoundContractKind.Requires,
            operation => IsParameter(operation, parameter),
            parameter.Type);
    }

    internal DeclaredValidation ForReturn()
    {
        return Read(
            _method.GetReturnTypeAttributes(),
            BoundContractKind.Ensures,
            IsResultInvocation,
            _method.ReturnType);
    }

    private DeclaredValidation Read(
        ImmutableArray<AttributeData> attributes,
        BoundContractKind kind,
        Predicate<IOperation> isSubject,
        ITypeSymbol type)
    {
        var facts = DeclaredValidation.None;
        foreach (var attribute in attributes)
        {
            facts = facts.And(ReadAttribute(attribute));
        }

        foreach (var clause in _resolution.Inventory.Clauses)
        {
            if (clause.Kind != kind && clause.Kind != BoundContractKind.Assume)
            {
                // A postcondition on a parameter, or a precondition on the
                // result, is not a declaration about that subject's entry value.
                continue;
            }

            if (clause.Kind != kind || !clause.IsValid)
            {
                // An assumption, or a clause SharpProof will not bind as declared,
                // that mentions the subject may say anything about it.
                if (Mentions(clause.Invocation, isSubject))
                {
                    facts = facts.And(DeclaredValidation.Opaque);
                }

                continue;
            }

            if (clause.Invocation.Arguments.FirstOrDefault()?.Value is not { } condition)
            {
                facts = facts.And(DeclaredValidation.Opaque);
                continue;
            }

            facts = facts.And(ReadCondition(condition, isSubject, type));
        }

        return facts;
    }

    private DeclaredValidation ReadAttribute(AttributeData attribute)
    {
        var attributeClass = attribute.AttributeClass;
        if (attributeClass is null)
        {
            return DeclaredValidation.None;
        }

        if (SymbolEqualityComparer.Default.Equals(attributeClass, _nonNull))
        {
            return new(NonNull: true, Lower: null, Upper: null, Unknown: false);
        }

        if (SymbolEqualityComparer.Default.Equals(attributeClass, _positive))
        {
            return new(NonNull: false, Lower: 1, Upper: null, Unknown: false);
        }

        if (SymbolEqualityComparer.Default.Equals(attributeClass, _inRange))
        {
            var arguments = attribute.ConstructorArguments;
            return arguments.Length == 2 &&
                arguments[0].Value is long minimum &&
                arguments[1].Value is long maximum
                ? new(NonNull: false, Lower: minimum, Upper: maximum, Unknown: false)
                : DeclaredValidation.Opaque;
        }

        return DeclaredValidation.None;
    }

    private static DeclaredValidation ReadCondition(
        IOperation condition,
        Predicate<IOperation> isSubject,
        ITypeSymbol type)
    {
        if (!Mentions(condition, isSubject))
        {
            return DeclaredValidation.None;
        }

        switch (Strip(condition))
        {
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.And } conjunction:
                return ReadCondition(conjunction.LeftOperand, isSubject, type)
                    .And(ReadCondition(conjunction.RightOperand, isSubject, type));

            case IBinaryOperation { OperatorKind: BinaryOperatorKind.NotEquals } notEquals
                when IsNullTest(notEquals, isSubject):
                return new(NonNull: true, Lower: null, Upper: null, Unknown: false);

            case IIsPatternOperation { Pattern: INegatedPatternOperation { Pattern: IConstantPatternOperation constant } } pattern
                when isSubject(pattern.Value) && IsNullLiteral(constant.Value):
                return new(NonNull: true, Lower: null, Upper: null, Unknown: false);

            case IBinaryOperation comparison when Bounds(comparison, isSubject, type) is { } bounds:
                return bounds;

            default:
                return DeclaredValidation.Opaque;
        }
    }

    private static DeclaredValidation? Bounds(
        IBinaryOperation comparison,
        Predicate<IOperation> isSubject,
        ITypeSymbol type)
    {
        if (!IntegralDomain.TryGet(type, out _, out _))
        {
            return null;
        }

        var subjectOnLeft = isSubject(comparison.LeftOperand);
        if (subjectOnLeft == isSubject(comparison.RightOperand))
        {
            return null;
        }

        var constantOperand = subjectOnLeft ? comparison.RightOperand : comparison.LeftOperand;
        if (!TryConstant(constantOperand, out var constant))
        {
            return null;
        }

        var kind = subjectOnLeft ? comparison.OperatorKind : Mirror(comparison.OperatorKind);
        return kind switch
        {
            BinaryOperatorKind.Equals => new(NonNull: false, Lower: constant, Upper: constant, Unknown: false),
            BinaryOperatorKind.GreaterThanOrEqual => new(NonNull: false, Lower: constant, Upper: null, Unknown: false),
            BinaryOperatorKind.GreaterThan when constant < long.MaxValue =>
                new(NonNull: false, Lower: constant + 1, Upper: null, Unknown: false),
            BinaryOperatorKind.LessThanOrEqual => new(NonNull: false, Lower: null, Upper: constant, Unknown: false),
            BinaryOperatorKind.LessThan when constant > long.MinValue =>
                new(NonNull: false, Lower: null, Upper: constant - 1, Unknown: false),
            _ => null
        };
    }

    // Moves the subject to the left: 5 < x reads as x > 5.
    private static BinaryOperatorKind Mirror(BinaryOperatorKind kind)
    {
        return kind switch
        {
            BinaryOperatorKind.GreaterThanOrEqual => BinaryOperatorKind.LessThanOrEqual,
            BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThan,
            BinaryOperatorKind.LessThanOrEqual => BinaryOperatorKind.GreaterThanOrEqual,
            BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThan,
            _ => kind
        };
    }

    private static bool TryConstant(IOperation operand, out long value)
    {
        var constant = Strip(operand).ConstantValue;
        value = 0;
        if (!constant.HasValue || constant.Value is null)
        {
            return false;
        }

        switch (constant.Value)
        {
            case sbyte or byte or short or ushort or int or uint or long:
                value = Convert.ToInt64(constant.Value, CultureInfo.InvariantCulture);
                return true;
            case ulong unsigned when unsigned <= long.MaxValue:
                value = (long)unsigned;
                return true;
            default:
                return false;
        }
    }

    private static bool IsNullTest(IBinaryOperation comparison, Predicate<IOperation> isSubject)
    {
        return isSubject(comparison.LeftOperand) && IsNullLiteral(comparison.RightOperand) ||
            isSubject(comparison.RightOperand) && IsNullLiteral(comparison.LeftOperand);
    }

    private static bool IsNullLiteral(IOperation operand)
    {
        return Strip(operand) is ILiteralOperation literal &&
            (literal.Type is null || literal.ConstantValue is { HasValue: true, Value: null });
    }

    private bool IsResultInvocation(IOperation operation)
    {
        return Strip(operation) is IInvocationOperation invocation &&
            invocation.TargetMethod.Name == "Result" &&
            SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType?.OriginalDefinition, _contract);
    }

    private static bool IsParameter(IOperation operation, IParameterSymbol parameter)
    {
        return Strip(operation) is IParameterReferenceOperation reference &&
            SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter);
    }

    private static bool Mentions(IOperation operation, Predicate<IOperation> isSubject)
    {
        if (isSubject(operation))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (Mentions(child, isSubject))
            {
                return true;
            }
        }

        return false;
    }

    // Implicit conversions and parentheses do not change the value compared.
    private static IOperation Strip(IOperation operation)
    {
        while (true)
        {
            if (operation is IConversionOperation { IsImplicit: true } conversion)
            {
                operation = conversion.Operand;
            }
            else if (operation is IParenthesizedOperation parenthesized)
            {
                operation = parenthesized.Operand;
            }
            else
            {
                return operation;
            }
        }
    }
}
