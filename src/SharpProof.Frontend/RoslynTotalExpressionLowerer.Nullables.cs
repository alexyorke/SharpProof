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

    // `T -> T?` or `S? -> T?`, possibly converting an integer on the way.
    private bool IsNullableConversion(IConversionOperation conversion, out ITypeSymbol target)
    {
        target = null!;
        if (conversion is not { OperatorMethod: null, IsTryCast: false, Conversion: { Exists: true, IsNullable: true, IsUserDefined: false } } ||
            CSharpOperationSemantics.ModeledNullableUnderlying(conversion.Type) is not { } underlying ||
            (CSharpOperationSemantics.ModeledNullableUnderlying(conversion.Operand.Type) ?? conversion.Operand.Type) is not { } source ||
            !CanCarry(source, underlying) ||
            !SymbolEqualityComparer.Default.Equals(source, underlying) && ConversionChecked(conversion) == null)
        { return false; }
        target = underlying;
        return true;
    }

    // Roslyn reports IsChecked as false for nullable conversions, so the
    // context is read from the syntax, as for enum conversions.
    private bool? ConversionChecked(IConversionOperation conversion)
    {
        return CSharpOperationSemantics.CheckedContext(conversion, _context.Compilation?.Options.CheckOverflow);
    }

    // Decided from types alone, before any operand is lowered.
    private static bool CanCarry(ITypeSymbol source, ITypeSymbol target)
    {
        return SymbolEqualityComparer.Default.Equals(source, target) ||
            CSharpOperationSemantics.IsLiftedArithmeticType(source) && CSharpOperationSemantics.IsLiftedArithmeticType(target);
    }

    // `null` or `default` reaching a T? through built-in conversions only:
    // a user-defined conversion such as `operator S?(string)` runs code and
    // may return a value.
    private static bool IsNullLiteral(IOperation operation)
    {
        return operation is IDefaultValueOperation ||
            operation is ILiteralOperation { ConstantValue: { HasValue: true, Value: null } } ||
            operation is IConversionOperation { OperatorMethod: null, IsTryCast: false, Conversion: { Exists: true, IsUserDefined: false } } conversion &&
                conversion.Operand.ConstantValue is { HasValue: true, Value: null } && IsNullLiteral(conversion.Operand);
    }

    // The value a nullable conversion carries over: the operand itself, or an
    // integer conversion of it, which in a checked context faults on overflow.
    // hasValue guards the faults: an empty operand converts to an empty T?.
    private TotalScalarRule? CarriedValue(IConversionOperation conversion, ITypeSymbol source, ITypeSymbol target, IrTerm value, IrTerm hasValue)
    {
        if (SymbolEqualityComparer.Default.Equals(source, target))
        { return new(value, [], FrontendSubsetClassification.Exact); }
        if (!CanCarry(source, target))
        { return null; }
        if (ConversionChecked(conversion) is not { } isChecked)
        { return null; }
        var rule = CSharpOperationSemantics.ConvertInteger(_factory, value, _context.Type(target), isChecked);
        return rule.Classification.IsExact
            ? new(rule.Value, [.. rule.Throws.Select(fault => new TotalThrow(fault.Kind, And(hasValue, fault.Condition)))], FrontendSubsetClassification.Exact)
            : null;
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
                    CarriedValue(conversion, liftedSource, target, inner.Parts.Value, inner.Parts.HasValue) is not { } widened)
                { return lifted is { } failed && !failed.Lowered.Classification.IsExact ? failed : null; }
                var checkedLifted = ApplyRule(conversion, widened, inner.Lowered.Continuation);
                return (new(inner.Parts.HasValue, checkedLifted.Value), checkedLifted);
            }
            if (conversion.Operand.Type is not { } source)
            { return null; }
            var operand = LowerBodyValue(conversion.Operand, block, depth + 1);
            if (!operand.Classification.IsExact)
            { return (none, operand); }
            if (CarriedValue(conversion, source, target, operand.Value, _factory.Boolean(true)) is not { } carried)
            { return null; }
            var checkedOperand = ApplyRule(conversion, carried, operand.Continuation);
            return (new(_factory.Boolean(true), checkedOperand.Value), checkedOperand);
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
            // `(T)x` unwraps, then converts an integer value as `(U)x.Value` would.
            case IConversionOperation
            {
                OperatorMethod: null, IsTryCast: false, Conversion: { Exists: true, IsNullable: true, IsUserDefined: false }
            } unwrap when CSharpOperationSemantics.ModeledNullableUnderlying(unwrap.Operand.Type) is { } underlying &&
                unwrap.Type is { } unwrapped && CSharpOperationSemantics.ModeledNullableUnderlying(unwrapped) == null &&
                CanCarry(underlying, unwrapped) &&
                (SymbolEqualityComparer.Default.Equals(underlying, unwrapped) || ConversionChecked(unwrap) != null):
                return WithParts(operation, unwrap.Operand, block, depth, (parts, current) =>
                {
                    var value = RequireNullableValue(operation, unwrap.Operand.Type!, parts, current);
                    return SymbolEqualityComparer.Default.Equals(underlying, unwrapped) || !value.Classification.IsExact ? value
                        : ApplyRule(operation, CSharpOperationSemantics.ConvertInteger(_factory, value.Value, _context.Type(unwrapped), ConversionChecked(unwrap)!.Value),
                            value.Continuation);
                });
            case IPropertyReferenceOperation { Instance: { } instance, Property.Name: "HasValue" or "Value" } property when
                CSharpOperationSemantics.ModeledNullableUnderlying(instance.Type) is { }:
                return WithParts(operation, instance, block, depth, (parts, current) => property.Property.Name == "HasValue"
                    ? Capture(operation, new(parts.HasValue, current, FrontendSubsetClassification.Exact))
                    : RequireNullableValue(operation, instance.Type!, parts, current));
            case IInvocationOperation { Instance: { } instance, TargetMethod.Name: "GetValueOrDefault" } invocation when
                CSharpOperationSemantics.ModeledNullableUnderlying(instance.Type) is { } && invocation.Arguments.Length <= 1:
                return WithParts(operation, instance, block, depth, (parts, current) =>
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
                return WithParts(operation, tested, block, depth, (parts, current) =>
                    new(Not(parts.HasValue), current, FrontendSubsetClassification.Exact));
            case IIsPatternOperation { Value: var input } isPattern when CSharpOperationSemantics.ModeledNullableUnderlying(input.Type) is { } underlying:
                return WithParts(operation, input, block, depth, (parts, current) =>
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
            // `n++`, `--n`: an empty variable stays empty.
            case IIncrementOrDecrementOperation { IsLifted: true, OperatorMethod: null } increment when
                CSharpOperationSemantics.ModeledNullableUnderlying(increment.Target.Type) is { } stepped &&
                (CSharpOperationSemantics.IsLiftedArithmeticType(stepped) || CSharpOperationSemantics.IsWideEnum(stepped)):
                return LiftedStore(operation, increment.Target, null, block, depth, current =>
                {
                    var parts = ReferenceParts(increment.Target.Type!, current);
                    return (parts.HasValue, CSharpOperationSemantics.IntegerArithmetic(_factory,
                        increment.Kind == OperationKind.Increment ? IrBinaryOperator.Add : IrBinaryOperator.Subtract,
                        parts.Value, _factory.Integer(parts.Value.Type, 1L), increment.IsChecked));
                }, returnsOld: increment.IsPostfix);
            // `n += x`: the target is read, then x is evaluated, then the lifted operator applies.
            case ICompoundAssignmentOperation { IsLifted: true, OperatorMethod: null } compound when
                CSharpOperationSemantics.ModeledNullableUnderlying(compound.Target.Type) is { } targetType &&
                CSharpOperationSemantics.ModeledNullableUnderlying(compound.Value.Type) is { } valueType &&
                (!compound.InConversion.Exists || compound.InConversion.IsIdentity) &&
                (!compound.OutConversion.Exists || compound.OutConversion.IsIdentity) &&
                LiftedShape(compound.OperatorKind, targetType, valueType, targetType) != LiftedKind.None:
                return LiftedStore(operation, compound.Target, compound, block, depth, null, returnsOld: false);
            case IConversionOperation { OperatorMethod: null, IsTryCast: false, Conversion: { Exists: true, IsUserDefined: false }, Type.SpecialType: SpecialType.System_Object } boxing when
                CSharpOperationSemantics.ModeledNullableUnderlying(boxing.Operand.Type) is { } boxed && IsBoxableValue(boxed):
                return WithParts(operation, boxing.Operand, block, depth, (parts, current) => BoxNullable(operation, parts, current, boxed));
            case IInvocationOperation { Instance: { } instance, Arguments.Length: <= 1 } member when
                CSharpOperationSemantics.ModeledNullableUnderlying(instance.Type) is { } underlying && CSharpOperationSemantics.IsScalar(underlying) &&
                member.TargetMethod.ContainingType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                NullableMember(member, underlying) is { } shape:
                return WithParts(operation, instance, block, depth, (parts, current) => shape(parts, current));
            default:
                return null;
        }
    }

    // Reads a T? local or parameter, computes its new value and stores it:
    // step gives whether the new value has one and its rule, and a compound
    // assignment evaluates its right side after the read.
    private TotalBodyValue? LiftedStore(IOperation operation, IOperation target, ICompoundAssignmentOperation? compound, IrBlockId block, int depth,
        Func<IrTerm, (IrTerm HasValue, TotalScalarRule Rule)>? step, bool returnsOld)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var symbol = target switch
        {
            ILocalReferenceOperation local => (ISymbol)local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null
        };
        // A closure's copy of the variable would not see the store.
        if (symbol == null || RootClosure?.Captures.Contains(symbol, SymbolEqualityComparer.Default) == true ||
            !TryStorage(target, out var variable) || target.Type is not { } nullable)
        { return null; }
        var site = _context.Site(operation);
        var old = _context.Temporary(_factory.GetVariableInfo(variable).Type);
        _builder!.Assign(block, site, old, _factory.Variable(variable));
        IrTerm hasValue;
        TotalScalarRule rule;
        if (compound != null)
        {
            if (BodyParts(compound.Value, block, depth + 1) is not { } right)
            { return Approximate(operation, block, FrontendAbstention.UnsupportedOperationKind); }
            if (!right.Lowered.Classification.IsExact)
            { return Approximate(operation, right.Lowered.Continuation, right.Lowered.Classification.Abstention); }
            block = right.Lowered.Continuation;
            (hasValue, rule) = LiftedValue(compound.OperatorKind, compound.IsChecked, ReferenceParts(nullable, _factory.Variable(old)), right.Parts);
        }
        else
        { (hasValue, rule) = RepositoryParameterGuard.NotNull(step, nameof(step))(_factory.Variable(old)); }
        if (!rule.Classification.IsExact)
        { return Approximate(operation, block, rule.Classification.Abstention); }
        var updated = MaterializeNullable(operation, nullable, hasValue, present => ApplyRule(operation, rule, present), block);
        if (!updated.Classification.IsExact)
        { return updated; }
        var mutation = _builder.Assign(updated.Continuation, site, variable, _context.BridgeValue(updated.Value, _factory.GetVariableInfo(variable).Type));
        _builder.Write(updated.Continuation, mutation.Operation, IrWriteRegion.Local);
        return new(returnsOld ? _factory.Variable(old) : updated.Value, updated.Continuation, FrontendSubsetClassification.Exact);
    }

    // Boxing a T? yields null when it has no value, and otherwise a fresh box
    // of the value, as boxing the value itself does.
    private TotalBodyValue BoxNullable(IOperation operation, NullableParts parts, IrBlockId block, ITypeSymbol? boxed = null)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var type = _context.Type(operation.Type);
        if (parts.HasValue is IrBooleanTerm { Value: false })
        { return new(_factory.Null(type), block, FrontendSubsetClassification.Exact); }
        var site = _context.Site(operation);
        var result = _context.Temporary(type);
        var present = _builder!.CreateBlock("nullable:box");
        var absent = _builder.CreateBlock("nullable:box-null");
        var join = _builder.CreateBlock("nullable:box-join");
        _builder.Branch(block, site, parts.HasValue, present, absent);
        var box = boxed != null ? AllocateBox(operation, boxed, parts.Value, present) : AllocateValue(operation, present);
        _builder.Assign(box.Continuation, site, result, box.Value);
        _builder.Goto(box.Continuation, site, join);
        _builder.Assign(absent, site, result, _factory.Null(type));
        _builder.Goto(absent, site, join);
        return new(_factory.Variable(result), join, FrontendSubsetClassification.Exact);
    }

    // Equals(object) against null, a boxed T or a boxed T?; GetHashCode for
    // int, uint, bool and enums over them; ToString. The shape is decided
    // from the operation before anything is lowered.
    private Func<NullableParts, IrBlockId, TotalBodyValue>? NullableMember(IInvocationOperation member, ITypeSymbol underlying)
    {
        switch (member.TargetMethod.Name)
        {
            case "Equals" when member.Arguments.Length == 1 && member.TargetMethod.Parameters[0].Type.SpecialType == SpecialType.System_Object:
                {
                    var argument = member.Arguments[0].Value;
                    if (IsNullLiteral(argument))
                    { return (parts, current) => Capture(member, new(Not(parts.HasValue), current, FrontendSubsetClassification.Exact)); }
                    if (argument is not IConversionOperation { OperatorMethod: null, IsTryCast: false, Conversion: { Exists: true, IsUserDefined: false }, Type.SpecialType: SpecialType.System_Object } boxing ||
                        boxing.Operand.Type is not { IsValueType: true } boxedType)
                    {
                        // Any other object: equal when it is a box of exactly T holding the value.
                        if (!IsReferenceInput(argument.Type) || !IsBoxableValue(underlying))
                        { return null; }
                        return (parts, current) =>
                        {
                            var other = LowerBodyValue(argument, current, 1);
                            if (!other.Classification.IsExact)
                            { return Approximate(member, other.Continuation, other.Classification.Abstention); }
                            var isNull = _factory.Binary(IrBinaryOperator.Equal, other.Value, _factory.Null(other.Value.Type));
                            var boxOfT = RuntimeTest(other.Value, underlying, RuntimeTypeRelations.TestKind.Box);
                            var same = _factory.Conditional(boxOfT,
                                _factory.Binary(IrBinaryOperator.Equal, BoxRead(other.Value, underlying, _factory.Boolean(true)), parts.Value),
                                _factory.Boolean(false));
                            return Capture(member, new(_factory.Conditional(isNull, Not(parts.HasValue), And(parts.HasValue, same)),
                                other.Continuation, FrontendSubsetClassification.Exact));
                        };
                    }
                    if (SymbolEqualityComparer.Default.Equals(boxedType, underlying))
                    {
                        return (parts, current) =>
                        {
                            var value = LowerBodyValue(boxing.Operand, current, 1);
                            if (!value.Classification.IsExact)
                            { return Approximate(member, value.Continuation, value.Classification.Abstention); }
                            var box = AllocateBox(boxing, boxedType, value.Value, value.Continuation);
                            return Capture(member, new(And(parts.HasValue, _factory.Binary(IrBinaryOperator.Equal, parts.Value, value.Value)),
                                box.Continuation, FrontendSubsetClassification.Exact));
                        };
                    }
                    if (!SymbolEqualityComparer.Default.Equals(boxedType, member.Instance!.Type))
                    { return null; }
                    return (parts, current) =>
                    {
                        if (BodyParts(boxing.Operand, current, 1) is not { } other)
                        { return Approximate(member, current, FrontendAbstention.UnsupportedOperationKind); }
                        if (!other.Lowered.Classification.IsExact)
                        { return Approximate(member, other.Lowered.Continuation, other.Lowered.Classification.Abstention); }
                        var box = BoxNullable(boxing, other.Parts, other.Lowered.Continuation, underlying);
                        return LiftedComparison(BinaryOperatorKind.Equals, parts, other.Parts) is { } equal
                            ? Capture(member, new(equal, box.Continuation, FrontendSubsetClassification.Exact))
                            : Approximate(member, box.Continuation, FrontendAbstention.UnsupportedOperationKind);
                    };
                }
            case "GetHashCode" when member.Arguments.IsEmpty &&
                CSharpOperationSemantics.ScalarSpecialType(underlying) is SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Boolean:
                return (parts, current) =>
                {
                    var hash = underlying.SpecialType == SpecialType.System_Boolean
                        ? _factory.Conditional(parts.Value, _factory.Integer(1), _factory.Integer(0))
                        : CSharpOperationSemantics.ConvertInteger(_factory, parts.Value, _factory.IntegerType, false).Value;
                    return Capture(member, new(_factory.Conditional(parts.HasValue, hash, _factory.Integer(0)), current, FrontendSubsetClassification.Exact));
                };
            case "ToString" when member.Arguments.IsEmpty:
                return (parts, current) => NullableToString(member, underlying, parts, current);
            default:
                return null;
        }
    }

    // "" without a value; "True" or "False" for bool; otherwise the value's
    // ToString, which formats through the current culture: an unknown call.
    private TotalBodyValue NullableToString(IInvocationOperation operation, ITypeSymbol underlying, NullableParts parts, IrBlockId block)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var empty = _factory.String(string.Empty);
        if (underlying.SpecialType == SpecialType.System_Boolean)
        {
            return Capture(operation, new(_factory.Conditional(parts.HasValue,
                _factory.Conditional(parts.Value, _factory.String("True"), _factory.String("False")), empty), block, FrontendSubsetClassification.Exact));
        }
        if (underlying.GetMembers("ToString").OfType<IMethodSymbol>().FirstOrDefault(method => method.Parameters.IsEmpty && !method.IsStatic) is not { } format)
        { return Approximate(operation, block, FrontendAbstention.UnsupportedOperationKind); }
        var site = _context.Site(operation);
        var result = _context.Temporary(empty.Type);
        var present = _builder!.CreateBlock("nullable:format");
        var absent = _builder.CreateBlock("nullable:format-empty");
        var join = _builder.CreateBlock("nullable:format-join");
        _builder.Branch(block, site, parts.HasValue, present, absent);
        if (ShadowCallSkeleton)
        { _builder.Havoc(present, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, result); }
        else
        {
            var formatted = EmitOpaqueCall(operation, format, [parts.Value], present, null, IrOpaqueCallEffects.All);
            present = formatted.Continuation;
            _builder.Assign(present, site, result, formatted.Value);
        }
        _builder.Goto(present, site, join);
        _builder.Assign(absent, site, result, empty);
        _builder.Goto(absent, site, join);
        return new(_factory.Variable(result), join, FrontendSubsetClassification.Exact);
    }

    // An operand that does not lower exactly leaves the whole operation
    // approximated, at the operation's own type.
    private TotalBodyValue? WithParts(IOperation operation, IOperation operand, IrBlockId block, int depth,
        Func<NullableParts, IrBlockId, TotalBodyValue> use)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        use = RepositoryParameterGuard.NotNull(use, nameof(use));
        if (BodyParts(operand, block, depth + 1) is not { } lowered)
        { return null; }
        return lowered.Lowered.Classification.IsExact ? use(lowered.Parts, lowered.Lowered.Continuation)
            : Approximate(operation, lowered.Lowered.Continuation, lowered.Lowered.Classification.Abstention);
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
            : LiftedShape(binary.OperatorKind, leftType, rightType, CSharpOperationSemantics.ModeledNullableUnderlying(binary.Type)) == LiftedKind.None)
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
        var (hasValue, rule) = LiftedValue(binary.OperatorKind, binary.IsChecked, left.Parts, right.Parts);
        if (!rule.Classification.IsExact)
        { return Approximate(binary, current, rule.Classification.Abstention); }
        // An integer operator runs, and may fault, only when both have a value.
        return MaterializeNullable(binary, binary.Type!, hasValue, present => ApplyRule(binary, rule, present), current);
    }

    private enum LiftedKind
    {
        None = 0,
        Integer = 1,
        Boolean = 2
    }

    // Lifted integer arithmetic on equal operand types (any shift count),
    // wide enum addition and subtraction, and the bool? operators & | ^.
    private static LiftedKind LiftedShape(BinaryOperatorKind kind, ITypeSymbol left, ITypeSymbol right, ITypeSymbol? result)
    {
        if (result == null)
        { return LiftedKind.None; }
        if (left.SpecialType == SpecialType.System_Boolean && right.SpecialType == SpecialType.System_Boolean &&
            result.SpecialType == SpecialType.System_Boolean)
        { return kind is BinaryOperatorKind.And or BinaryOperatorKind.Or or BinaryOperatorKind.ExclusiveOr ? LiftedKind.Boolean : LiftedKind.None; }
        if (CSharpOperationSemantics.IsWideEnumArithmetic(kind, left, right))
        { return CSharpOperationSemantics.IsScalar(result) ? LiftedKind.Integer : LiftedKind.None; }
        // Bitwise operators on two values of one enum type act on the underlying integers.
        if (CSharpOperationSemantics.IsEnum(left) && SymbolEqualityComparer.Default.Equals(left, right) &&
            SymbolEqualityComparer.Default.Equals(left, result))
        { return kind is BinaryOperatorKind.And or BinaryOperatorKind.Or or BinaryOperatorKind.ExclusiveOr ? LiftedKind.Integer : LiftedKind.None; }
        return CSharpOperationSemantics.IsLiftedArithmeticType(result) && CSharpOperationSemantics.IsLiftedArithmeticType(left) &&
            CSharpOperationSemantics.IsLiftedArithmeticType(right) &&
            (CSharpOperationSemantics.IsShift(kind) || SymbolEqualityComparer.Default.Equals(left, right))
            ? LiftedKind.Integer : LiftedKind.None;
    }

    // Whether the result has a value, and the rule for that value. bool? & and
    // | are three-valued: false & null is false and true | null is true.
    private (IrTerm HasValue, TotalScalarRule Rule) LiftedValue(BinaryOperatorKind kind, bool isChecked, NullableParts left, NullableParts right)
    {
        var both = And(left.HasValue, right.HasValue);
        if (left.Value.Type == _factory.BooleanType && kind is BinaryOperatorKind.And or BinaryOperatorKind.Or)
        {
            var leftDecides = And(left.HasValue, kind == BinaryOperatorKind.And ? Not(left.Value) : left.Value);
            var rightDecides = And(right.HasValue, kind == BinaryOperatorKind.And ? Not(right.Value) : right.Value);
            var value = kind == BinaryOperatorKind.And ? And(both, And(left.Value, right.Value)) : Or(leftDecides, rightDecides);
            return (Or(both, Or(leftDecides, rightDecides)), new(value, [], FrontendSubsetClassification.Exact));
        }
        return (both, CSharpOperationSemantics.ApplyBinaryScalar(_factory, kind, isChecked, left.Value, right.Value));
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
                    CarriedValue(conversion, liftedSource, target, inner.Parts.Value, inner.Parts.HasValue) is not { } widened)
                { return null; }
                return (new(inner.Parts.HasValue, widened.Value), WithFaults(inner.Lowered, widened));
            }
            if (conversion.Operand.Type is not { } source)
            { return null; }
            var operand = LowerClause(conversion.Operand, state, depth + 1);
            if (!operand.Classification.IsExact)
            { return (none, operand); }
            return CarriedValue(conversion, source, target, operand.Value, _factory.Boolean(true)) is { } carried
                ? (new(_factory.Boolean(true), carried.Value), WithFaults(operand, carried)) : null;
        }
        // Lifted arithmetic: it faults only when both operands have a value.
        if (operation is IBinaryOperation { IsLifted: true, OperatorMethod: null } binary &&
            CSharpOperationSemantics.ModeledNullableUnderlying(binary.LeftOperand.Type) is { } leftType &&
            CSharpOperationSemantics.ModeledNullableUnderlying(binary.RightOperand.Type) is { } rightType &&
            LiftedShape(binary.OperatorKind, leftType, rightType, underlying) != LiftedKind.None)
        {
            if (ClauseParts(binary.LeftOperand, state, depth + 1) is not { } left || ClauseParts(binary.RightOperand, state, depth + 1) is not { } right)
            { return null; }
            var classification = First([left.Lowered.Classification, right.Lowered.Classification]);
            if (!classification.IsExact)
            { return (none, Failed(operation, classification.Abstention)); }
            var (both, rule) = LiftedValue(binary.OperatorKind, binary.IsChecked, left.Parts, right.Parts);
            if (!rule.Classification.IsExact)
            { return (none, Failed(operation, rule.Classification.Abstention)); }
            IrTerm safe = And(left.Lowered.SafeCondition, right.Lowered.SafeCondition);
            foreach (var fault in rule.Throws)
            { safe = And(safe, Or(Not(both), Not(fault.Condition))); }
            return (new(both, _factory.Conditional(both, rule.Value, none.Value)), new(_factory.Boolean(true), safe, FrontendSubsetClassification.Exact));
        }
        var reference = LowerClause(operation, state, depth + 1);
        return (reference.Classification.IsExact ? ReferenceParts(operation.Type!, reference.Value) : none, reference);
    }

    private GuardedExpression WithFaults(GuardedExpression lowered, TotalScalarRule rule)
    {
        lowered = RepositoryParameterGuard.NotNull(lowered, nameof(lowered));
        var safe = lowered.SafeCondition;
        foreach (var fault in rule.Throws)
        { safe = And(safe, Not(fault.Condition)); }
        return new(lowered.Value, safe, lowered.Classification);
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
                unwrap.Type is { } unwrapped && CSharpOperationSemantics.ModeledNullableUnderlying(unwrapped) == null &&
                CanCarry(underlying, unwrapped) &&
                (SymbolEqualityComparer.Default.Equals(underlying, unwrapped) || ConversionChecked(unwrap) != null):
                return WithClauseParts(operation, unwrap.Operand, state, depth, (parts, lowered) =>
                {
                    var present = new GuardedExpression(parts.Value, And(lowered.SafeCondition, parts.HasValue), FrontendSubsetClassification.Exact);
                    if (SymbolEqualityComparer.Default.Equals(underlying, unwrapped))
                    { return present; }
                    var rule = CSharpOperationSemantics.ConvertInteger(_factory, parts.Value, _context.Type(unwrapped), ConversionChecked(unwrap)!.Value);
                    return rule.Classification.IsExact ? WithFaults(new(rule.Value, present.SafeCondition, FrontendSubsetClassification.Exact), rule)
                        : Failed(operation, rule.Classification.Abstention);
                });
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
