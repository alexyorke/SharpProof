using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Frontend;

// Runtime types. An allocation of a class, delegate or array, and a box of a
// value, records its object's exact runtime type; type tests, `as`,
// unboxing and downcasts then read it. For any object whose allocation the
// program does not see, the answer stays unknown (see RuntimeTypeRelations).
internal sealed partial class RoslynTotalExpressionLowerer
{
    private static bool IsReferenceInput(ITypeSymbol? type)
    {
        return type is { IsReferenceType: true } and not ITypeParameterSymbol && type.TypeKind != TypeKind.Dynamic;
    }

    // A value that can be boxed and later unboxed exactly: a scalar (enums
    // included) or a modeled readonly struct.
    private static bool IsBoxableValue(ITypeSymbol? type)
    {
        return type is { IsValueType: true } && CSharpOperationSemantics.ModeledNullableUnderlying(type) == null &&
            (CSharpOperationSemantics.IsScalar(type) || CSharpOperationSemantics.IsModeledStruct(type));
    }

    // Whether value passes a test of the given kind against target.
    private IrTerm RuntimeTest(IrTerm value, ITypeSymbol target, RuntimeTypeRelations.TestKind kind)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        var tested = kind == RuntimeTypeRelations.TestKind.Reference ? _context.Type(target) : RuntimeTypeRelations.BoxType(_factory, target, kind);
        RuntimeTypeRelations.RegisterTested(_factory, _context.Compilation, tested, target, kind);
        return _factory.TypeTest(_factory.GetTypeInfo(value.Type).Kind == IrTypeKind.Reference ? value : AsObject(value), tested, exact: false);
    }

    // `x is T`, `x is T t`: T a class, interface, delegate or array type, or
    // a boxable value type.
    private IrTerm? RuntimeTypeIs(IrTerm value, ITypeSymbol target)
    {
        if (target is ITypeParameterSymbol || target.TypeKind == TypeKind.Dynamic)
        { return null; }
        if (target.IsValueType)
        { return IsBoxableValue(target) ? RuntimeTest(value, target, RuntimeTypeRelations.TestKind.Box) : null; }
        return target.IsReferenceType ? RuntimeTest(value, target, RuntimeTypeRelations.TestKind.Reference) : null;
    }

    // The value a box holds, read only where test passed.
    private IrTerm BoxRead(IrTerm box, ITypeSymbol valueType, IrTerm test)
    {
        var type = _context.Type(valueType);
        var read = _factory.PureOpaque(RuntimeTypeRelations.BoxValueField(_factory, type), AsObject(box));
        return _factory.Conditional(test, read, CSharpOperationSemantics.DefaultValue(_factory, type));
    }

    // A fresh object of exactly runtime's type.
    private TotalBodyValue AllocateRuntime(IOperation operation, IrTypeId allocated, IrTypeId runtime, ITypeSymbol symbol, bool box, IrBlockId block)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        RuntimeTypeRelations.RegisterRuntime(_factory, _context.Compilation, runtime, symbol, box);
        var target = _context.Temporary(allocated);
        var site = _context.Site(operation);
        _builder!.Allocate(block, site, allocated, target);
        _factory.RegisterAllocationRuntimeType(site, runtime);
        return new(_factory.Variable(target), block, FrontendSubsetClassification.Exact);
    }

    // A box of value, of runtime type box(V), seen as operation's type.
    internal TotalBodyValue AllocateBox(IOperation operation, ITypeSymbol valueType, IrTerm value, IrBlockId block)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        valueType = RepositoryParameterGuard.NotNull(valueType, nameof(valueType));
        var allocated = AllocateRuntime(operation, _context.Type(operation.Type), RuntimeTypeRelations.BoxType(_factory, valueType),
            valueType, true, block);
        _builder!.FieldStore(block, _context.FreshWriteSite(operation), IrWriteRegion.Field, AsObject(allocated.Value),
            RuntimeTypeRelations.BoxValueField(_factory, value.Type), value);
        return allocated;
    }

    // The exact runtime type an allocation for this operation creates, when
    // the operation fixes it.
    private static bool TryAllocationRuntimeType(IOperation operation, out ITypeSymbol runtime)
    {
        runtime = operation switch
        {
            IObjectCreationOperation { Type: { IsReferenceType: true, TypeKind: TypeKind.Class or TypeKind.Delegate } created } => created,
            IDelegateCreationOperation { Type: { TypeKind: TypeKind.Delegate } created } => created,
            IArrayCreationOperation { Type: IArrayTypeSymbol created } => created,
            _ => null!
        };
        return runtime != null && !RuntimeTypeRelationsIsOpen(runtime);
    }

    private static bool RuntimeTypeRelationsIsOpen(ITypeSymbol type)
    {
        return RuntimeTypeRelations.Converts(null, type, false, type, RuntimeTypeRelations.TestKind.Box) == null;
    }

    private TotalBodyValue? RuntimeTypeBody(IOperation operation, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        switch (operation)
        {
            // Boxing a scalar, an enum or a modeled struct to object, ValueType,
            // Enum or an interface allocates a box holding the value.
            case IConversionOperation { OperatorMethod: null, IsTryCast: false, Conversion: { Exists: true, IsImplicit: true, IsUserDefined: false } } boxing when
                IsBoxableValue(boxing.Operand.Type) && boxing.Type is { IsReferenceType: true } &&
                boxing.Type is not ITypeParameterSymbol:
                {
                    var value = LowerBodyValue(boxing.Operand, block, depth + 1);
                    return value.Classification.IsExact ? AllocateBox(operation, boxing.Operand.Type!, value.Value, value.Continuation)
                        : Approximate(operation, value.Continuation, value.Classification.Abstention);
                }
            // `(V)x` and `(V?)x` from a reference: null throws (or is empty for
            // V?), a box that does not unbox to V throws InvalidCastException.
            case IConversionOperation { OperatorMethod: null, IsTryCast: false, Conversion: { Exists: true, IsUserDefined: false } } unboxing when
                IsReferenceInput(unboxing.Operand.Type) &&
                (IsBoxableValue(unboxing.Type) || CSharpOperationSemantics.ModeledNullableUnderlying(unboxing.Type) is { } nullableTarget && IsBoxableValue(nullableTarget)):
                return Unboxing(operation, unboxing, block, depth);
            // `x as T` for a reference T, and `x as V?`.
            case IConversionOperation { OperatorMethod: null, IsTryCast: true } tryCast when IsReferenceInput(tryCast.Operand.Type) &&
                tryCast.Type is { } asType && (asType.IsReferenceType && asType is not (ITypeParameterSymbol or IArrayTypeSymbol) &&
                    asType.SpecialType != SpecialType.System_String ||
                    CSharpOperationSemantics.ModeledNullableUnderlying(asType) is { } asValue && IsBoxableValue(asValue)):
                return TryCast(operation, tryCast, block, depth);
            case IIsTypeOperation { ValueOperand: var tested, TypeOperand: var typeOperand } isType when
                IsReferenceInput(tested.Type) && (typeOperand.IsReferenceType || IsBoxableValue(typeOperand)) && typeOperand is not ITypeParameterSymbol:
                {
                    var value = LowerBodyValue(tested, block, depth + 1);
                    if (!value.Classification.IsExact)
                    { return Approximate(operation, value.Continuation, value.Classification.Abstention); }
                    if (RuntimeTypeIs(value.Value, typeOperand) is not { } test)
                    { return Approximate(operation, value.Continuation, FrontendAbstention.UnsupportedOperationKind); }
                    return Capture(operation, new(isType.IsNegated ? Not(test) : test, value.Continuation, FrontendSubsetClassification.Exact));
                }
            case IIsPatternOperation { Value: var input, Pattern: IDeclarationPatternOperation or ITypePatternOperation } pattern when
                IsReferenceInput(input.Type):
                return TypePattern(operation, pattern, input, block, depth);
            // `x.Equals(o)` on a bool, char or integer: o is a box of exactly
            // that type holding the same value.
            case IInvocationOperation { Instance: { Type: { } receiverType } receiver, TargetMethod: { Name: "Equals", Parameters.Length: 1 } method, Arguments.Length: 1 } equals when
                CSharpOperationSemantics.IsScalar(receiverType) && !CSharpOperationSemantics.IsEnum(receiverType) &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_Object &&
                SymbolEqualityComparer.Default.Equals(method.ContainingType, receiverType) &&
                (receiver is ILocalReferenceOperation or IParameterReferenceOperation || receiver.ConstantValue.HasValue):
                {
                    // The call keeps the receiver variable's address, so its
                    // value is read after the argument, which may assign it.
                    var other = LowerBodyValue(equals.Arguments[0].Value, block, depth + 1);
                    if (!other.Classification.IsExact)
                    { return Approximate(operation, other.Continuation, other.Classification.Abstention); }
                    var self = LowerBodyValue(receiver, other.Continuation, depth + 1);
                    if (!self.Classification.IsExact)
                    { return Approximate(operation, self.Continuation, self.Classification.Abstention); }
                    other = new(other.Value, self.Continuation, other.Classification);
                    var boxOfT = RuntimeTest(other.Value, receiverType, RuntimeTypeRelations.TestKind.Box);
                    return Capture(operation, new(_factory.Conditional(boxOfT,
                        _factory.Binary(IrBinaryOperator.Equal, BoxRead(other.Value, receiverType, _factory.Boolean(true)), self.Value),
                        _factory.Boolean(false)), other.Continuation, FrontendSubsetClassification.Exact));
                }
            default:
                return null;
        }
    }

    private TotalBodyValue Unboxing(IOperation operation, IConversionOperation unboxing, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var source = LowerBodyValue(unboxing.Operand, block, depth + 1);
        if (!source.Classification.IsExact)
        { return Approximate(operation, source.Continuation, source.Classification.Abstention); }
        var isNull = _factory.Binary(IrBinaryOperator.Equal, source.Value, _factory.Null(source.Value.Type));
        if (CSharpOperationSemantics.ModeledNullableUnderlying(unboxing.Type) is { } underlying)
        {
            // Unboxing to V? needs a box of exactly V: unlike `(V)o`, an enum
            // box does not unbox to its underlying type's nullable.
            var fits = RuntimeTest(source.Value, underlying, RuntimeTypeRelations.TestKind.Box);
            var checkedFit = ApplyRule(operation, new TotalScalarRule(_factory.Boolean(false),
                [new(IrExceptionKind.InvalidCast, And(Not(isNull), Not(fits)))], FrontendSubsetClassification.Exact), source.Continuation);
            return MaterializeNullable(operation, unboxing.Type!, Not(isNull),
                present => new(BoxRead(source.Value, underlying, fits), present, FrontendSubsetClassification.Exact), checkedFit.Continuation);
        }
        var unboxes = RuntimeTest(source.Value, unboxing.Type!, RuntimeTypeRelations.TestKind.Unbox);
        return ApplyRule(operation, new TotalScalarRule(BoxRead(source.Value, unboxing.Type!, unboxes),
            [new(IrExceptionKind.NullReference, isNull), new(IrExceptionKind.InvalidCast, And(Not(isNull), Not(unboxes)))],
            FrontendSubsetClassification.Exact), source.Continuation);
    }

    private TotalBodyValue TryCast(IOperation operation, IConversionOperation tryCast, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var source = LowerBodyValue(tryCast.Operand, block, depth + 1);
        if (!source.Classification.IsExact)
        { return Approximate(operation, source.Continuation, source.Classification.Abstention); }
        if (CSharpOperationSemantics.ModeledNullableUnderlying(tryCast.Type) is { } underlying)
        {
            // Unboxing to V? needs a box of exactly V: unlike `(V)o`, an enum
            // box does not unbox to its underlying type's nullable.
            var fits = RuntimeTest(source.Value, underlying, RuntimeTypeRelations.TestKind.Box);
            return MaterializeNullable(operation, tryCast.Type!, fits,
                present => new(BoxRead(source.Value, underlying, fits), present, FrontendSubsetClassification.Exact), source.Continuation);
        }
        var type = _context.Type(tryCast.Type);
        var test = RuntimeTest(source.Value, tryCast.Type!, RuntimeTypeRelations.TestKind.Reference);
        return Capture(operation, new(_factory.Conditional(test, _context.BridgeValue(source.Value, type), _factory.Null(type)),
            source.Continuation, FrontendSubsetClassification.Exact));
    }

    // `x is T` and `x is T t` over a reference input.
    private TotalBodyValue? TypePattern(IOperation operation, IIsPatternOperation pattern, IOperation input, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var matched = pattern.Pattern switch
        {
            IDeclarationPatternOperation declaration => declaration.MatchedType,
            ITypePatternOperation type => type.MatchedType,
            _ => null
        };
        var declared = pattern.Pattern is IDeclarationPatternOperation { DeclaredSymbol: ILocalSymbol local } ? local : null;
        if (matched == null || matched is ITypeParameterSymbol || matched.TypeKind == TypeKind.Dynamic ||
            !(matched.IsReferenceType || IsBoxableValue(matched)) ||
            declared != null && (declared.RefKind != RefKind.None || RootClosure?.Captures.Contains(declared, SymbolEqualityComparer.Default) == true ||
                // As for `as`, an object is not bridged to a string or array
                // value: those are other value kinds, which no cast reaches.
                matched is IArrayTypeSymbol || matched.SpecialType == SpecialType.System_String))
        { return null; }
        var value = LowerBodyValue(input, block, depth + 1);
        if (!value.Classification.IsExact)
        { return Approximate(operation, value.Continuation, value.Classification.Abstention); }
        if (RuntimeTypeIs(value.Value, matched) is not { } test)
        { return Approximate(operation, value.Continuation, FrontendAbstention.UnsupportedOperationKind); }
        var current = value.Continuation;
        if (declared != null)
        {
            // C# lets the variable be read only where the test passed, so
            // giving it the type's default elsewhere is unobservable and keeps
            // it defined on every path.
            var type = _context.Type(matched);
            var bound = matched.IsReferenceType
                ? _factory.Conditional(test, _context.BridgeValue(value.Value, type), _factory.Null(type))
                : BoxRead(value.Value, matched, test);
            var mutation = _builder!.Assign(current, _context.Site(operation), _context.Variable(declared), bound);
            _builder.Write(current, mutation.Operation, IrWriteRegion.Local);
        }
        return Capture(operation, new(test, current, FrontendSubsetClassification.Exact));
    }
}
