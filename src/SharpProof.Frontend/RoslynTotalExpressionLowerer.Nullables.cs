using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    // Nullable<T> over a scalar (bool, an integer or an enum) or a modeled
    // readonly struct. A T? value is modeled as a reference to an immutable
    // object holding the value, and null stands for "no value": every
    // reference of the type is a valid T?. Reads are guarded, boxing is not
    // modeled, and C# has no reference comparison of T? values, so object
    // identity is never observed.
    internal static ITypeSymbol? ModeledNullableUnderlying(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1
        } named && named.TypeArguments[0] is var underlying && (IsScalar(underlying) || IsModeledStruct(underlying)) ? underlying : null;
    }

    // An integer (not an enum) underlying type, on which lifted arithmetic is modeled.
    internal static bool IsLiftedArithmeticType(ITypeSymbol? type)
    {
        return type != null && !IsEnum(type) && type.SpecialType != SpecialType.System_Boolean && IsScalar(type);
    }
}

// A T? operand as the two facts the C# rules consult: whether it has a value
// and that value (the underlying default when it has none).
internal sealed class NullableParts(IrTerm hasValue, IrTerm value)
{
    internal IrTerm HasValue { get; } = hasValue ?? throw new System.ArgumentNullException(nameof(hasValue));
    internal IrTerm Value { get; } = value ?? throw new System.ArgumentNullException(nameof(value));
}

internal sealed partial class RoslynTotalExpressionLowerer
{
    private IrMemberId NullableValueField(ITypeSymbol nullable)
    {
        nullable = RepositoryParameterGuard.NotNull(nullable, nameof(nullable));
        var type = _context.Type(nullable);
        var value = _context.Type(CSharpOperationSemantics.ModeledNullableUnderlying(nullable));
        var identity = _factory.InternExternalIdentity(("nullable-value", type), EqualityComparer<(string, IrTypeId)>.Default);
        return _factory.GetOrCreateMember(identity, _factory.ObjectType,
            IrFieldSites.Prefix + "nullable:" + CompilerIdentityBridge.CreateTypeDisplay(nullable), value, false);
    }

    private NullableParts ReferenceParts(ITypeSymbol nullable, IrTerm reference)
    {
        nullable = RepositoryParameterGuard.NotNull(nullable, nameof(nullable));
        reference = RepositoryParameterGuard.NotNull(reference, nameof(reference));
        var isNull = _factory.Binary(IrBinaryOperator.Equal, reference, _factory.Null(reference.Type));
        var read = _factory.PureOpaque(NullableValueField(nullable), AsObject(reference));
        return new(Not(isNull), _factory.Conditional(isNull, CSharpOperationSemantics.DefaultValue(_factory, read.Type), read));
    }

    // `T -> T?` (possibly widening an integer on the way) or `S? -> T?`.
    private static bool IsNullableConversion(IConversionOperation conversion, out ITypeSymbol target)
    {
        target = null!;
        if (conversion is not { OperatorMethod: null, IsTryCast: false, Conversion: { Exists: true, IsNullable: true, IsUserDefined: false } } ||
            CSharpOperationSemantics.ModeledNullableUnderlying(conversion.Type) is not { } underlying ||
            (CSharpOperationSemantics.ModeledNullableUnderlying(conversion.Operand.Type) ?? conversion.Operand.Type) is not { } source ||
            !CanCarry(conversion, source, underlying))
        { return false; }
        target = underlying;
        return true;
    }

    // Decided from types alone, before any operand is lowered.
    private static bool CanCarry(IConversionOperation conversion, ITypeSymbol source, ITypeSymbol target)
    {
        return SymbolEqualityComparer.Default.Equals(source, target) ||
            conversion.Conversion.IsImplicit && CSharpOperationSemantics.IsLiftedArithmeticType(source) &&
            CSharpOperationSemantics.IsLiftedArithmeticType(target);
    }

    private static bool IsNullLiteral(IOperation operation)
    {
        return operation is IDefaultValueOperation ||
            operation is IConversionOperation { Operand: { ConstantValue: { HasValue: true, Value: null } } } ||
            operation is ILiteralOperation { ConstantValue: { HasValue: true, Value: null } };
    }

    // The value a nullable conversion carries over: the operand itself, or an
    // implicit integer widening of it, which never faults.
    private IrTerm? CarriedValue(IConversionOperation conversion, ITypeSymbol source, ITypeSymbol target, IrTerm value)
    {
        if (SymbolEqualityComparer.Default.Equals(source, target))
        { return value; }
        if (!CanCarry(conversion, source, target))
        { return null; }
        var rule = CSharpOperationSemantics.ConvertInteger(_factory, value, _context.Type(target), false);
        return rule.Classification.IsExact && rule.Throws.IsEmpty ? rule.Value : null;
    }

    // ---- Body ----

    private (NullableParts Parts, TotalBodyValue Lowered)? BodyParts(IOperation operation, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (CSharpOperationSemantics.ModeledNullableUnderlying(operation.Type) is not { } underlying)
        { return null; }
        var none = new NullableParts(_factory.Boolean(false), CSharpOperationSemantics.DefaultValue(_factory, _context.Type(underlying)));
        if (operation is IConversionOperation { OperatorMethod: null, IsTryCast: false, Conversion: { IsImplicit: true, IsUserDefined: false } } identity &&
            SymbolEqualityComparer.Default.Equals(identity.Operand.Type, identity.Type))
        { return BodyParts(identity.Operand, block, depth + 1); }
        if (IsNullLiteral(operation))
        { return (none, new(_factory.Null(_context.Type(operation.Type)), block, FrontendSubsetClassification.Exact)); }
        if (operation is IConversionOperation conversion && IsNullableConversion(conversion, out var target))
        {
            if (CSharpOperationSemantics.ModeledNullableUnderlying(conversion.Operand.Type) is { } liftedSource)
            {
                var lifted = BodyParts(conversion.Operand, block, depth + 1);
                if (lifted is not { } inner || !inner.Lowered.Classification.IsExact ||
                    CarriedValue(conversion, liftedSource, target, inner.Parts.Value) is not { } widened)
                { return lifted is { } failed && !failed.Lowered.Classification.IsExact ? failed : null; }
                return (new(inner.Parts.HasValue, widened), inner.Lowered);
            }
            if (conversion.Operand.Type is not { } source)
            { return null; }
            var operand = LowerBodyValue(conversion.Operand, block, depth + 1);
            if (!operand.Classification.IsExact)
            { return (none, operand); }
            return CarriedValue(conversion, source, target, operand.Value) is { } carried
                ? (new(_factory.Boolean(true), carried), operand) : null;
        }
        var reference = LowerBodyValue(operation, block, depth + 1);
        return (reference.Classification.IsExact ? ReferenceParts(operation.Type!, reference.Value) : none, reference);
    }

    // A fresh T? object holding value.
    private TotalBodyValue WrapNullable(IOperation operation, ITypeSymbol nullable, IrTerm value, IrBlockId block)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var type = _context.Type(nullable);
        _factory.RegisterValueStructType(type);
        var target = _context.Temporary(type);
        _builder!.Allocate(block, _context.Site(operation), type, target);
        _builder.FieldStore(block, _context.FreshWriteSite(operation), IrWriteRegion.Field, AsObject(_factory.Variable(target)),
            NullableValueField(nullable), value);
        return new(_factory.Variable(target), block, FrontendSubsetClassification.Exact);
    }

    // The T? whose value, when hasValue holds, compute produces in its block.
    private TotalBodyValue MaterializeNullable(IOperation operation, ITypeSymbol nullable, IrTerm hasValue,
        Func<IrBlockId, TotalBodyValue> compute, IrBlockId block)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        compute = RepositoryParameterGuard.NotNull(compute, nameof(compute));
        var type = _context.Type(nullable);
        if (hasValue is IrBooleanTerm { Value: false })
        { return new(_factory.Null(type), block, FrontendSubsetClassification.Exact); }
        if (hasValue is IrBooleanTerm { Value: true })
        {
            var value = compute(block);
            return value.Classification.IsExact ? Capture(operation, WrapNullable(operation, nullable, value.Value, value.Continuation)) : value;
        }
        var site = _context.Site(operation);
        var result = _context.Temporary(type);
        var present = _builder!.CreateBlock("nullable:value");
        var absent = _builder.CreateBlock("nullable:null");
        var join = _builder.CreateBlock("nullable:join");
        _builder.Branch(block, site, hasValue, present, absent);
        var computed = compute(present);
        if (!computed.Classification.IsExact)
        { return Approximate(operation, computed.Continuation, computed.Classification.Abstention); }
        var wrapped = WrapNullable(operation, nullable, computed.Value, computed.Continuation);
        _builder.Assign(wrapped.Continuation, site, result, wrapped.Value);
        _builder.Goto(wrapped.Continuation, site, join);
        _builder.Assign(absent, site, result, _factory.Null(type));
        _builder.Goto(absent, site, join);
        return new(_factory.Variable(result), join, FrontendSubsetClassification.Exact);
    }

    // `x.Value` or `(T)x`: without a value the getter builds an
    // InvalidOperationException, whose message comes from runtime resources,
    // and throws it.
    private TotalBodyValue RequireNullableValue(IOperation operation, ITypeSymbol nullable, NullableParts parts, IrBlockId block)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (parts.HasValue is IrBooleanTerm { Value: true })
        { return Capture(operation, new(parts.Value, block, FrontendSubsetClassification.Exact)); }
        if (nullable.GetMembers("Value").OfType<IPropertySymbol>().FirstOrDefault()?.GetMethod is not { } getter)
        { return Approximate(operation, block, FrontendAbstention.UnsupportedOperationKind); }
        var site = _context.Site(operation);
        var absent = _builder!.CreateBlock("nullable:no-value");
        var present = _builder.CreateBlock("nullable:value");
        _builder.Branch(block, site, parts.HasValue, present, absent);
        // A call skeleton holds source calls only, as for exception constructors.
        var fault = ShadowCallSkeleton ? absent : EmitOpaqueCall(operation, getter, [], absent, null, IrOpaqueCallEffects.All).Continuation;
        _builder.Throw(fault, site, IrExceptionKind.Unknown, ExceptionTarget?.Invoke(IrExceptionKind.Unknown, site) ?? _exceptionalExit);
        return Capture(operation, new(parts.Value, present, FrontendSubsetClassification.Exact));
    }

    private TotalBodyValue? NullableBody(IOperation operation, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        switch (operation)
        {
            // A conversion to its own type, such as the one a target-typed `?:` puts over
            // its flow capture, keeps the value.
            case IConversionOperation { OperatorMethod: null, IsTryCast: false, Conversion: { IsImplicit: true, IsUserDefined: false } } identity when
                CSharpOperationSemantics.ModeledNullableUnderlying(operation.Type) is { } &&
                SymbolEqualityComparer.Default.Equals(identity.Operand.Type, identity.Type):
                return LowerBodyValue(identity.Operand, block, depth + 1);
            case IDefaultValueOperation or IConversionOperation when
                CSharpOperationSemantics.ModeledNullableUnderlying(operation.Type) is { } && IsNullLiteral(operation):
                return new(_factory.Null(_context.Type(operation.Type)), block, FrontendSubsetClassification.Exact);
            case IConversionOperation conversion when CSharpOperationSemantics.ModeledNullableUnderlying(operation.Type) is { } &&
                IsNullableConversion(conversion, out _):
                {
                    if (BodyParts(operation, block, depth) is not { } converted)
                    { return null; }
                    if (!converted.Lowered.Classification.IsExact)
                    { return Approximate(operation, converted.Lowered.Continuation, converted.Lowered.Classification.Abstention); }
                    var parts = converted.Parts;
                    return MaterializeNullable(operation, operation.Type!, parts.HasValue,
                        current => new(parts.Value, current, FrontendSubsetClassification.Exact), converted.Lowered.Continuation);
                }
            // `(T)x` unwraps; an integer conversion of the unwrapped value is not modeled.
            case IConversionOperation
            {
                OperatorMethod: null, IsTryCast: false, Conversion: { Exists: true, IsNullable: true, IsUserDefined: false }
            } unwrap when CSharpOperationSemantics.ModeledNullableUnderlying(unwrap.Operand.Type) is { } underlying &&
                SymbolEqualityComparer.Default.Equals(underlying, unwrap.Type):
                return WithParts(unwrap.Operand, block, depth, (parts, current) => RequireNullableValue(operation, unwrap.Operand.Type!, parts, current));
            case IPropertyReferenceOperation { Instance: { } instance, Property.Name: "HasValue" or "Value" } property when
                CSharpOperationSemantics.ModeledNullableUnderlying(instance.Type) is { }:
                return WithParts(instance, block, depth, (parts, current) => property.Property.Name == "HasValue"
                    ? Capture(operation, new(parts.HasValue, current, FrontendSubsetClassification.Exact))
                    : RequireNullableValue(operation, instance.Type!, parts, current));
            case IInvocationOperation { Instance: { } instance, TargetMethod.Name: "GetValueOrDefault" } invocation when
                CSharpOperationSemantics.ModeledNullableUnderlying(instance.Type) is { } && invocation.Arguments.Length <= 1:
                return WithParts(instance, block, depth, (parts, current) =>
                {
                    if (invocation.Arguments.IsEmpty)
                    { return Capture(operation, new(parts.Value, current, FrontendSubsetClassification.Exact)); }
                    var fallback = LowerBodyValue(invocation.Arguments[0].Value, current, depth + 1);
                    return fallback.Classification.IsExact
                        ? Capture(operation, new(_factory.Conditional(parts.HasValue, parts.Value, fallback.Value), fallback.Continuation,
                            FrontendSubsetClassification.Exact))
                        : Approximate(operation, fallback.Continuation, fallback.Classification.Abstention);
                });
            // Roslyn's null test for `??` and `?.` runs no code.
            case IIsNullOperation { Operand: var tested } when CSharpOperationSemantics.ModeledNullableUnderlying(tested.Type) is { }:
                return WithParts(tested, block, depth, (parts, current) =>
                    new(Not(parts.HasValue), current, FrontendSubsetClassification.Exact));
            case IIsPatternOperation { Value: var input } isPattern when CSharpOperationSemantics.ModeledNullableUnderlying(input.Type) is { } underlying:
                return WithParts(input, block, depth, (parts, current) =>
                {
                    if (NullablePattern(isPattern.Pattern, input.Type!, underlying, parts, depth + 1, top: true) is not { } matched)
                    { return Approximate(operation, current, FrontendAbstention.UnsupportedOperationKind); }
                    // `x is T v` binds v to the value; unmatched, v keeps T's default, which is the value read then.
                    // A closure's copy of the variable would not see the store.
                    if (isPattern.Pattern is IDeclarationPatternOperation { DeclaredSymbol: ILocalSymbol declared })
                    {
                        if (RootClosure?.Captures.Contains(declared, SymbolEqualityComparer.Default) == true || declared.RefKind != RefKind.None)
                        { return Approximate(operation, current, FrontendAbstention.UnsupportedOperationKind); }
                        var mutation = _builder!.Assign(current, _context.Site(operation), _context.Variable(declared), parts.Value);
                        _builder.Write(current, mutation.Operation, IrWriteRegion.Local);
                    }
                    return Capture(operation, new(matched, current, FrontendSubsetClassification.Exact));
                });
            case IBinaryOperation { IsLifted: true, OperatorMethod: null } binary:
                return LiftedBinary(binary, block, depth);
            case IUnaryOperation { IsLifted: true, OperatorMethod: null } unary:
                return LiftedUnary(unary, block, depth);
            default:
                return null;
        }
    }

    private TotalBodyValue? WithParts(IOperation operand, IrBlockId block, int depth, Func<NullableParts, IrBlockId, TotalBodyValue> use)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        use = RepositoryParameterGuard.NotNull(use, nameof(use));
        if (BodyParts(operand, block, depth + 1) is not { } lowered)
        { return null; }
        return lowered.Lowered.Classification.IsExact ? use(lowered.Parts, lowered.Lowered.Continuation)
            : Approximate(operand, lowered.Lowered.Continuation, lowered.Lowered.Classification.Abstention);
    }

    private static bool IsComparison(BinaryOperatorKind kind)
    {
        return kind is BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or BinaryOperatorKind.GreaterThan or
            BinaryOperatorKind.GreaterThanOrEqual;
    }

    // `==` is true when neither or both have a value and the values are
    // equal; an ordering is false unless both have a value.
    private IrTerm? LiftedComparison(BinaryOperatorKind kind, NullableParts left, NullableParts right)
    {
        var rule = CSharpOperationSemantics.ApplyBinaryScalar(_factory,
            kind == BinaryOperatorKind.NotEquals ? BinaryOperatorKind.Equals : kind, false, left.Value, right.Value);
        if (!rule.Classification.IsExact || !rule.Throws.IsEmpty)
        { return null; }
        var both = And(left.HasValue, right.HasValue);
        if (IsComparison(kind))
        { return And(both, rule.Value); }
        var equal = Or(And(Not(left.HasValue), Not(right.HasValue)), And(both, rule.Value));
        return kind == BinaryOperatorKind.Equals ? equal : Not(equal);
    }

    private TotalBodyValue? LiftedBinary(IBinaryOperation binary, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (CSharpOperationSemantics.ModeledNullableUnderlying(binary.LeftOperand.Type) is not { } leftType ||
            CSharpOperationSemantics.ModeledNullableUnderlying(binary.RightOperand.Type) is not { } rightType ||
            !CSharpOperationSemantics.IsScalar(leftType) || !CSharpOperationSemantics.IsScalar(rightType))
        { return null; }
        var comparison = binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals || IsComparison(binary.OperatorKind);
        if (comparison ? binary.Type?.SpecialType != SpecialType.System_Boolean ||
                IsComparison(binary.OperatorKind) && leftType.SpecialType == SpecialType.System_Boolean
            : CSharpOperationSemantics.ModeledNullableUnderlying(binary.Type) is not { } resultType ||
                !CSharpOperationSemantics.IsLiftedArithmeticType(resultType) || !CSharpOperationSemantics.IsLiftedArithmeticType(leftType) ||
                !CSharpOperationSemantics.IsLiftedArithmeticType(rightType) ||
                !CSharpOperationSemantics.IsShift(binary.OperatorKind) && !SymbolEqualityComparer.Default.Equals(leftType, rightType))
        { return null; }
        if (BodyParts(binary.LeftOperand, block, depth + 1) is not { } left)
        { return null; }
        if (!left.Lowered.Classification.IsExact)
        { return Approximate(binary, left.Lowered.Continuation, left.Lowered.Classification.Abstention); }
        if (BodyParts(binary.RightOperand, left.Lowered.Continuation, depth + 1) is not { } right)
        { return null; }
        if (!right.Lowered.Classification.IsExact)
        { return Approximate(binary, right.Lowered.Continuation, right.Lowered.Classification.Abstention); }
        var current = right.Lowered.Continuation;
        if (comparison)
        {
            return LiftedComparison(binary.OperatorKind, left.Parts, right.Parts) is { } compared
                ? Capture(binary, new(compared, current, FrontendSubsetClassification.Exact))
                : Approximate(binary, current, FrontendAbstention.UnsupportedOperationKind);
        }
        var rule = CSharpOperationSemantics.ApplyBinaryScalar(_factory, binary.OperatorKind, binary.IsChecked, left.Parts.Value, right.Parts.Value);
        if (!rule.Classification.IsExact)
        { return Approximate(binary, current, rule.Classification.Abstention); }
        // The operator runs, and may fault, only when both have a value.
        return MaterializeNullable(binary, binary.Type!, And(left.Parts.HasValue, right.Parts.HasValue),
            present => ApplyRule(binary, rule, present), current);
    }

    private TotalBodyValue? LiftedUnary(IUnaryOperation unary, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (CSharpOperationSemantics.ModeledNullableUnderlying(unary.Operand.Type) is not { } operandType ||
            CSharpOperationSemantics.ModeledNullableUnderlying(unary.Type) is not { } resultType ||
            !(unary.OperatorKind == UnaryOperatorKind.Not && operandType.SpecialType == SpecialType.System_Boolean ||
                unary.OperatorKind is UnaryOperatorKind.Minus or UnaryOperatorKind.Plus or UnaryOperatorKind.BitwiseNegation &&
                CSharpOperationSemantics.IsLiftedArithmeticType(operandType) && CSharpOperationSemantics.IsLiftedArithmeticType(resultType)))
        { return null; }
        if (BodyParts(unary.Operand, block, depth + 1) is not { } operand)
        { return null; }
        if (!operand.Lowered.Classification.IsExact)
        { return Approximate(unary, operand.Lowered.Continuation, operand.Lowered.Classification.Abstention); }
        var rule = CSharpOperationSemantics.ApplyUnaryScalar(_factory, unary.OperatorKind, unary.IsChecked, operand.Parts.Value,
            _context.Type(resultType));
        if (!rule.Classification.IsExact)
        { return Approximate(unary, operand.Lowered.Continuation, rule.Classification.Abstention); }
        return MaterializeNullable(unary, unary.Type!, operand.Parts.HasValue, present => ApplyRule(unary, rule, present),
            operand.Lowered.Continuation);
    }

    // A pattern over a T? input: null, a constant or relational test of the
    // value, the type T itself (with or without a declaration), not, and/or.
    private IrTerm? NullablePattern(IPatternOperation pattern, ITypeSymbol nullable, ITypeSymbol underlying, NullableParts parts, int depth,
        bool top = false)
    {
        Spend?.Invoke();
        if (depth >= 256)
        { return null; }
        if (SymbolEqualityComparer.Default.Equals(pattern.InputType, underlying))
        {
            // The right side of `and` sees the value the left side narrowed to.
            return CSharpOperationSemantics.IsScalar(underlying) && BuiltInPattern(pattern, underlying, parts.Value, depth + 1) is { } narrowed
                ? And(parts.HasValue, narrowed) : null;
        }
        if (!SymbolEqualityComparer.Default.Equals(pattern.InputType, nullable))
        { return null; }
        switch (pattern)
        {
            case IDiscardPatternOperation:
                return _factory.Boolean(true);
            case IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: null } }:
                return Not(parts.HasValue);
            case IConstantPatternOperation constant when CSharpOperationSemantics.IsScalar(underlying):
                {
                    var value = constant.Value is IConversionOperation { Conversion.IsNullable: true } converted ? converted.Operand : constant.Value;
                    return CSharpOperationSemantics.ScalarPatternConstant(_factory, value, underlying, parts.Value.Type) is { } literal
                        ? And(parts.HasValue, _factory.Binary(IrBinaryOperator.Equal, parts.Value, literal)) : null;
                }
            case IRelationalPatternOperation relational when CSharpOperationSemantics.IsLiftedArithmeticType(underlying) &&
                IsComparison(relational.OperatorKind) && CSharpOperationSemantics.TryBinary(relational.OperatorKind, out var kind):
                {
                    var value = relational.Value is IConversionOperation { Conversion.IsNullable: true } converted ? converted.Operand : relational.Value;
                    return CSharpOperationSemantics.ScalarPatternConstant(_factory, value, underlying, parts.Value.Type) is { } bound
                        ? And(parts.HasValue, _factory.Binary(kind, parts.Value, bound)) : null;
                }
            case ITypePatternOperation typePattern when SymbolEqualityComparer.Default.Equals(typePattern.MatchedType, underlying):
                return parts.HasValue;
            case IDeclarationPatternOperation declaration when top || declaration.DeclaredSymbol == null:
                return SymbolEqualityComparer.Default.Equals(declaration.MatchedType, underlying) ? parts.HasValue : null;
            case INegatedPatternOperation negated:
                return NullablePattern(negated.Pattern, nullable, underlying, parts, depth + 1) is { } inner ? Not(inner) : null;
            case IBinaryPatternOperation { OperatorKind: BinaryOperatorKind.And or BinaryOperatorKind.Or } combined:
                {
                    if (NullablePattern(combined.LeftPattern, nullable, underlying, parts, depth + 1) is not { } left ||
                        NullablePattern(combined.RightPattern, nullable, underlying, parts, depth + 1) is not { } right)
                    { return null; }
                    return combined.OperatorKind == BinaryOperatorKind.And ? And(left, right) : Or(left, right);
                }
            default:
                return null;
        }
    }

    // ---- Clauses ----

    private (NullableParts Parts, GuardedExpression Lowered)? ClauseParts(IOperation operation, TotalParameterState state, int depth)
    {
        if (CSharpOperationSemantics.ModeledNullableUnderlying(operation.Type) is not { } underlying)
        { return null; }
        var none = new NullableParts(_factory.Boolean(false), CSharpOperationSemantics.DefaultValue(_factory, _context.Type(underlying)));
        if (operation is IConversionOperation { OperatorMethod: null, IsTryCast: false, Conversion: { IsImplicit: true, IsUserDefined: false } } identity &&
            SymbolEqualityComparer.Default.Equals(identity.Operand.Type, identity.Type))
        { return ClauseParts(identity.Operand, state, depth + 1); }
        if (IsNullLiteral(operation))
        { return (none, Exact(_factory.Boolean(true))); }
        if (operation is IConversionOperation conversion && IsNullableConversion(conversion, out var target))
        {
            if (CSharpOperationSemantics.ModeledNullableUnderlying(conversion.Operand.Type) is { } liftedSource)
            {
                if (ClauseParts(conversion.Operand, state, depth + 1) is not { } inner ||
                    !inner.Lowered.Classification.IsExact ||
                    CarriedValue(conversion, liftedSource, target, inner.Parts.Value) is not { } widened)
                { return null; }
                return (new(inner.Parts.HasValue, widened), inner.Lowered);
            }
            if (conversion.Operand.Type is not { } source)
            { return null; }
            var operand = LowerClause(conversion.Operand, state, depth + 1);
            if (!operand.Classification.IsExact)
            { return (none, operand); }
            return CarriedValue(conversion, source, target, operand.Value) is { } carried
                ? (new(_factory.Boolean(true), carried), operand) : null;
        }
        // Lifted arithmetic: it faults only when both operands have a value.
        if (operation is IBinaryOperation { IsLifted: true, OperatorMethod: null } binary &&
            CSharpOperationSemantics.IsLiftedArithmeticType(underlying) &&
            CSharpOperationSemantics.ModeledNullableUnderlying(binary.LeftOperand.Type) is { } leftType &&
            CSharpOperationSemantics.ModeledNullableUnderlying(binary.RightOperand.Type) is { } rightType &&
            CSharpOperationSemantics.IsLiftedArithmeticType(leftType) && CSharpOperationSemantics.IsLiftedArithmeticType(rightType) &&
            (CSharpOperationSemantics.IsShift(binary.OperatorKind) || SymbolEqualityComparer.Default.Equals(leftType, rightType)))
        {
            if (ClauseParts(binary.LeftOperand, state, depth + 1) is not { } left || ClauseParts(binary.RightOperand, state, depth + 1) is not { } right)
            { return null; }
            var classification = First([left.Lowered.Classification, right.Lowered.Classification]);
            if (!classification.IsExact)
            { return (none, Failed(operation, classification.Abstention)); }
            var rule = CSharpOperationSemantics.ApplyBinaryScalar(_factory, binary.OperatorKind, binary.IsChecked, left.Parts.Value, right.Parts.Value);
            if (!rule.Classification.IsExact)
            { return (none, Failed(operation, rule.Classification.Abstention)); }
            var both = And(left.Parts.HasValue, right.Parts.HasValue);
            IrTerm safe = And(left.Lowered.SafeCondition, right.Lowered.SafeCondition);
            foreach (var fault in rule.Throws)
            { safe = And(safe, Or(Not(both), Not(fault.Condition))); }
            return (new(both, _factory.Conditional(both, rule.Value, none.Value)), new(_factory.Boolean(true), safe, FrontendSubsetClassification.Exact));
        }
        var reference = LowerClause(operation, state, depth + 1);
        return (reference.Classification.IsExact ? ReferenceParts(operation.Type!, reference.Value) : none, reference);
    }

    private GuardedExpression? NullableClause(IOperation operation, TotalParameterState state, int depth)
    {
        switch (operation)
        {
            case IPropertyReferenceOperation { Instance: { } instance, Property.Name: "HasValue" or "Value" } property when
                CSharpOperationSemantics.ModeledNullableUnderlying(instance.Type) is { }:
                return WithClauseParts(operation, instance, state, depth, (parts, lowered) => property.Property.Name == "HasValue"
                    ? new(parts.HasValue, lowered.SafeCondition, FrontendSubsetClassification.Exact)
                    : new(parts.Value, And(lowered.SafeCondition, parts.HasValue), FrontendSubsetClassification.Exact));
            case IConversionOperation
            {
                OperatorMethod: null, IsTryCast: false, Conversion: { Exists: true, IsNullable: true, IsUserDefined: false }
            } unwrap when CSharpOperationSemantics.ModeledNullableUnderlying(unwrap.Operand.Type) is { } underlying &&
                SymbolEqualityComparer.Default.Equals(underlying, unwrap.Type):
                return WithClauseParts(operation, unwrap.Operand, state, depth, (parts, lowered) =>
                    new(parts.Value, And(lowered.SafeCondition, parts.HasValue), FrontendSubsetClassification.Exact));
            case IInvocationOperation { Instance: { } instance, TargetMethod.Name: "GetValueOrDefault" } invocation when
                CSharpOperationSemantics.ModeledNullableUnderlying(instance.Type) is { } && invocation.Arguments.Length <= 1:
                return WithClauseParts(operation, instance, state, depth, (parts, lowered) =>
                {
                    if (invocation.Arguments.IsEmpty)
                    { return new(parts.Value, lowered.SafeCondition, FrontendSubsetClassification.Exact); }
                    var fallback = LowerClause(invocation.Arguments[0].Value, state, depth + 1);
                    return fallback.Classification.IsExact
                        ? new(_factory.Conditional(parts.HasValue, parts.Value, fallback.Value), And(lowered.SafeCondition, fallback.SafeCondition),
                            FrontendSubsetClassification.Exact)
                        : Failed(operation, fallback.Classification.Abstention);
                });
            case IBinaryOperation { IsLifted: true, OperatorMethod: null } binary when
                binary.Type?.SpecialType == SpecialType.System_Boolean &&
                (binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals || IsComparison(binary.OperatorKind)) &&
                CSharpOperationSemantics.ModeledNullableUnderlying(binary.LeftOperand.Type) is { } leftType &&
                CSharpOperationSemantics.ModeledNullableUnderlying(binary.RightOperand.Type) is { } rightType &&
                CSharpOperationSemantics.IsScalar(leftType) && CSharpOperationSemantics.IsScalar(rightType) &&
                !(IsComparison(binary.OperatorKind) && leftType.SpecialType == SpecialType.System_Boolean):
                {
                    if (ClauseParts(binary.LeftOperand, state, depth + 1) is not { } left || ClauseParts(binary.RightOperand, state, depth + 1) is not { } right)
                    { return null; }
                    var classification = First([left.Lowered.Classification, right.Lowered.Classification]);
                    if (!classification.IsExact)
                    { return Failed(operation, classification.Abstention); }
                    return LiftedComparison(binary.OperatorKind, left.Parts, right.Parts) is { } compared
                        ? new(compared, And(left.Lowered.SafeCondition, right.Lowered.SafeCondition), FrontendSubsetClassification.Exact)
                        : Failed(operation, FrontendAbstention.UnsupportedOperationKind);
                }
            default:
                return null;
        }
    }

    private GuardedExpression? WithClauseParts(IOperation operation, IOperation operand, TotalParameterState state, int depth,
        Func<NullableParts, GuardedExpression, GuardedExpression> use)
    {
        use = RepositoryParameterGuard.NotNull(use, nameof(use));
        if (ClauseParts(operand, state, depth + 1) is not { } lowered)
        { return null; }
        return lowered.Lowered.Classification.IsExact ? use(lowered.Parts, lowered.Lowered)
            : Failed(operation, lowered.Lowered.Classification.Abstention);
    }
}
