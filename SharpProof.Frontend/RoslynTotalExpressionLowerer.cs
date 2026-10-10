namespace SharpProof.Frontend;

internal readonly struct TotalBodyValue(IrTerm value, IrBlockId continuation,
    FrontendSubsetClassification classification, ImmutableArray<IrTerm> concatenationOperands = default)
{
    internal IrTerm Value { get; } = value;
    internal IrBlockId Continuation { get; } = continuation;
    internal FrontendSubsetClassification Classification { get; } = classification;
    internal ImmutableArray<IrTerm> ConcatenationOperands { get; } = concatenationOperands;
}

// Pure clauses compose safety; body evaluation emits the same local faults as
// ordered edges, materializing operands before later source effects.
internal sealed class RoslynTotalExpressionLowerer(TotalLoweringContext context,
    IrProgramBuilder? builder = null, IrBlockId exceptionalExit = default)
{
    private readonly TotalLoweringContext _context = context;
    private readonly IrFactory _factory = context.Factory;
    private readonly IrProgramBuilder? _builder = builder;
    private readonly IrBlockId _exceptionalExit = exceptionalExit;
    internal Func<IInvocationOperation, TotalParameterState, GuardedExpression?>? Intrinsic { get; set; }
    internal Func<IrExceptionKind, OperationId, IrBlockId>? ExceptionTarget { get; set; }
    internal Action? Spend { get; set; }
    internal Func<IInvocationOperation, IrBlockId, int, TotalBodyValue?>? SourceCall { get; set; }

    internal Func<IPropertyReferenceOperation, IrBlockId, int, TotalBodyValue?>? SourceGetter { get; set; }
    internal Func<IObjectCreationOperation, IrBlockId, int, TotalBodyValue?>? SourceConstruction { get; set; }
    internal Func<ISimpleAssignmentOperation, IrBlockId, int, TotalBodyValue?>? SourceSetter { get; set; }

    // Shadow skeletons record call edges only; they never take opaque calls.
    internal bool AllowOpaqueCalls { get; set; }
    internal bool ShadowCallSkeleton { get; set; }

    // Element reads are approximations when the program also writes elements
    // or calls opaque code; otherwise a read is a pure function of the array.
    internal bool ApproximateElementReads { get; set; }
    internal bool PinElementReads { get; set; }

    // An API specification narrows what an opaque call may do.
    internal Func<IMethodSymbol, IrOpaqueCallEffects?>? OpaqueEffects { get; set; }

    // `this` (or `base`) of a reference-type instance member is never null.
    internal bool IsImplicitThis(IOperation? instance)
    {
        return (instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } ||
                instance is IFlowCaptureReferenceOperation capture && _context.IsThisCapture(capture.Id)) &&
            !_context.Target.IsStatic && _context.Target.ContainingType.IsReferenceType;
    }

    // A clause's read of a modeled field: through `this` once it is modeled,
    // else through a receiver that must not be null.
    private GuardedExpression? FieldClause(IOperation operation, IrMemberId member, IOperation owner, TotalParameterState state, int depth)
    {
        if (IsImplicitThis(owner))
        {
            return _context.ReceiverValue(state) is { } self
                ? new(_factory.PureOpaque(member, AsObject(self)), _factory.Boolean(true), FrontendSubsetClassification.Exact)
                : null;
        }
        var receiver = LowerClause(owner, state, depth + 1);
        return receiver.Classification.IsExact
            ? new(_factory.PureOpaque(member, AsObject(receiver.Value)),
                And(receiver.SafeCondition, Not(_factory.Binary(IrBinaryOperator.Equal, receiver.Value, _factory.Null(receiver.Value.Type)))),
                receiver.Classification)
            : Failed(operation, receiver.Classification.Abstention);
    }

    // An instance call checks its receiver after evaluating the arguments.
    internal IrBlockId CheckReceiver(IOperation operation, IrTerm receiver, IrBlockId block)
    { return ApplyRule(operation, CSharpOperationSemantics.FieldRead(_factory, _factory.Boolean(true), receiver), block).Continuation; }

    internal GuardedExpression LowerClause(IOperation operation,
        TotalParameterState state = TotalParameterState.Current, int depth = 0)
    {
        var rejected = Reject(operation, depth);
        if (rejected != FrontendAbstention.None)
        {
            return Failed(operation, rejected);
        }
        if (TryLeaf(operation, state, out var leaf))
        {
            return Exact(leaf!);
        }
        switch (operation)
        {
            case IParenthesizedOperation parenthesized:
                return LowerClause(parenthesized.Operand, state, depth + 1);
            case IInvocationOperation invocation:
                return Intrinsic?.Invoke(invocation, state) ?? StringEqualsClause(invocation, state, depth) ??
                    Failed(operation, FrontendAbstention.UnsupportedInvocationShape);
            case IConditionalOperation { WhenFalse: { } whenFalse } conditional:
                {
                    var condition = LowerClause(conditional.Condition, state, depth + 1);
                    var whenTrue = LowerClause(conditional.WhenTrue, state, depth + 1);
                    var otherwise = LowerClause(whenFalse, state, depth + 1);
                    var classification = First(condition.Classification, whenTrue.Classification, otherwise.Classification);
                    if (!classification.IsExact)
                    {
                        return Failed(operation, classification.Abstention);
                    }
                    return new(_factory.Conditional(condition.Value, whenTrue.Value, otherwise.Value),
                        And(condition.SafeCondition, _factory.Conditional(condition.Value, whenTrue.SafeCondition, otherwise.SafeCondition)),
                        classification);
                }
            case IConversionOperation conversion:
                return Compose(operation, [LowerClause(conversion.Operand, state, depth + 1)]);
            // A getter that returns a field reads that field.
            case IPropertyReferenceOperation { Instance: { } owner, Arguments.Length: 0 } property when
                CSharpOperationSemantics.GetterField(property.Property, CSharpOperationSemantics.IsBaseAccess(owner)) is { } backing &&
                FieldMember(backing) is { } member && FieldClause(operation, member, owner, state, depth) is { } propertyRead:
                return propertyRead;
            case IPropertyReferenceOperation { Instance: { } receiver }:
                return Compose(operation, [LowerClause(receiver, state, depth + 1)]);
            case IFieldReferenceOperation { Instance: { } owner } fieldReference when FieldMember(fieldReference.Field) is { } member &&
                FieldClause(operation, member, owner, state, depth) is { } read:
                return read;
            case IArrayElementReferenceOperation { Indices.Length: 1 } access:
                return Compose(operation, [LowerClause(access.ArrayReference, state, depth + 1), LowerClause(access.Indices[0], state, depth + 1)]);
            case IUnaryOperation unary:
                return Compose(operation, [LowerClause(unary.Operand, state, depth + 1)]);
            // A clause only describes the concatenated string; it allocates
            // nothing at run time.
            case IBinaryOperation concatenation when CSharpOperationSemantics.IsStringConcatenation(concatenation):
                {
                    var left = LowerClause(concatenation.LeftOperand, state, depth + 1);
                    var right = LowerClause(concatenation.RightOperand, state, depth + 1);
                    var classification = First(left.Classification, right.Classification);
                    return classification.IsExact
                        ? new(CSharpOperationSemantics.StringConcat(_factory, left.Value, right.Value).Value,
                            And(left.SafeCondition, right.SafeCondition), classification)
                        : Failed(operation, classification.Abstention);
                }
            case IBinaryOperation binary:
                {
                    var operands = CSharpOperationSemantics.EqualityOperands(binary);
                    var left = LowerClause(operands.Left, state, depth + 1);
                    var right = LowerClause(operands.Right, state, depth + 1);
                    var result = Compose(operation, [left, right]);
                    if (result.Classification.IsExact && binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr)
                    {
                        var taken = binary.OperatorKind == BinaryOperatorKind.ConditionalAnd ? left.Value : Not(left.Value);
                        return new(result.Value, And(left.SafeCondition, Or(Not(taken), right.SafeCondition)), result.Classification);
                    }
                    return result;
                }
            default:
                return Failed(operation, FrontendAbstention.UnsupportedOperationKind);
        }
    }

    internal TotalBodyValue LowerBodyValue(IOperation operation, IrBlockId block, int depth = 0)
    {
        Spend?.Invoke();
        if (CSharpOperationSemantics.IsIntercepted(operation, _context.Compilation))
        { return Approximate(operation, block, FrontendAbstention.UnsupportedInvocationShape); }
        if (depth < 256 && operation is IConversionOperation
            { OperatorMethod: null, IsTryCast: false, Operand: ICollectionExpressionOperation converted } collectionConversion &&
            !collectionConversion.Conversion.IsUserDefined && SymbolEqualityComparer.Default.Equals(collectionConversion.Type, converted.Type) &&
            CSharpOperationSemantics.IsSupportedConstantArrayCollection(converted))
        { return LowerBodyValue(converted, block, depth + 1); }
        if (depth < 256 && operation is ICollectionExpressionOperation collection &&
            CSharpOperationSemantics.IsSupportedConstantArrayCollection(collection))
        {
            var initial = collection.Elements.Select(element =>
                CSharpOperationSemantics.Literal(_factory, element.Type, element.ConstantValue.Value)).ToImmutableArray();
            var allocatedType = _context.Type(collection.Type);
            var allocatedTarget = _context.Temporary(allocatedType);
            _builder!.Allocate(block, _context.Site(collection), allocatedType, allocatedTarget, _factory.Integer(initial.Length), initial);
            return new(_factory.Variable(allocatedTarget), block, FrontendSubsetClassification.Exact);
        }
        if (depth < 256 && operation is IArrayCreationOperation arrayCreation && CSharpOperationSemantics.IsSupportedArrayCreation(arrayCreation))
        {
            var dimension = LowerBodyValue(arrayCreation.DimensionSizes[0], block, depth + 1);
            if (!dimension.Classification.IsExact)
            { return Approximate(operation, dimension.Continuation, dimension.Classification.Abstention); }
            var rule = new TotalScalarRule(dimension.Value,
                [new(IrExceptionKind.Overflow, _factory.Binary(IrBinaryOperator.LessThan, dimension.Value, _factory.Integer(0)))],
                FrontendSubsetClassification.Exact);
            block = ApplyRule(operation, rule, dimension.Continuation).Continuation;
            block = ExceedsMaximumArrayLength(operation, arrayCreation.DimensionSizes[0], dimension.Value, block);
            var type = _context.Type(operation.Type);
            var target = _context.Temporary(type);
            var initialValues = arrayCreation.Initializer == null ? ImmutableArray<IrTerm>.Empty
                : arrayCreation.Initializer.ElementValues.Select(element =>
                    CSharpOperationSemantics.Literal(_factory, element.Type, element.ConstantValue.Value)).ToImmutableArray();
            var length = arrayCreation.Initializer == null ? dimension.Value : _factory.Integer(initialValues.Length);
            _builder!.Allocate(block, _context.Site(operation), type, target, length, initialValues);
            return new(_factory.Variable(target), block, FrontendSubsetClassification.Exact);
        }
        if (depth < 256 && CSharpOperationSemantics.IsStringConcatenation(operation))
        { return LowerStringConcatenation(operation, block, depth); }
        if (depth < 256 && operation is IObjectCreationOperation creation &&
            CSharpOperationSemantics.IsCoreObjectCreation(creation))
        { return AllocateValue(operation, block); }
        if (depth < 256 && operation is IObjectCreationOperation sourceCreation &&
            SourceConstruction?.Invoke(sourceCreation, block, depth) is { } constructed)
        { return constructed; }
        if (depth < 256 && operation is IObjectCreationOperation opaqueCreation &&
            !CSharpOperationSemantics.IsCoreExceptionCreation(opaqueCreation) &&
            OpaqueConstruction(opaqueCreation, block, depth) is { } opaquelyConstructed)
        { return opaquelyConstructed; }
        if (depth < 256 && operation is IObjectCreationOperation exceptionCreation &&
            CSharpOperationSemantics.IsCoreExceptionCreation(exceptionCreation))
        {
            foreach (var argument in exceptionCreation.Arguments.OrderBy(argument => argument.Syntax.SpanStart))
            {
                var lowered = LowerBodyValue(argument.Value, block, depth + 1);
                if (!lowered.Classification.IsExact)
                { return Approximate(operation, lowered.Continuation, lowered.Classification.Abstention); }
                block = lowered.Continuation;
            }
            return AllocateValue(operation, block);
        }
        if (depth < 256 && operation is IDelegateCreationOperation delegateCreation &&
            CSharpOperationSemantics.IsExplicitDelegateCreation(delegateCreation))
        {
            var erased = CSharpOperationSemantics.DelegateValueIsErased(delegateCreation, _context.Compilation);
            if (((IMethodReferenceOperation)delegateCreation.Target).Instance is { } instance)
            {
                var receiver = LowerBodyValue(instance, block, depth + 1);
                if (!receiver.Classification.IsExact)
                { return Approximate(operation, receiver.Continuation, receiver.Classification.Abstention); }
                // Erasure retains receiver evaluation, but no constructor or null check.
                if (erased)
                { return new(_factory.Null(_context.Type(operation.Type)), receiver.Continuation, receiver.Classification); }
                IrTerm? mayCheckNull = null;
                if (!CSharpOperationSemantics.DelegateValueEscapesDirectly(delegateCreation))
                {
                    // Release emission can erase an unused constructor and its
                    // null check. Preserve both paths; approximation reads may
                    // support universal proofs but never concrete refutations.
                    var choice = _context.Temporary(_factory.BooleanType);
                    _builder!.Havoc(receiver.Continuation, _context.Site(operation), IrHavocKind.Variables, IrHavocOrigin.Approximation, choice);
                    mayCheckNull = _factory.Variable(choice);
                }
                block = ApplyRule(operation, CSharpOperationSemantics.DelegateReceiver(_factory, receiver.Value, mayCheckNull), receiver.Continuation).Continuation;
            }
            return erased
                ? new(_factory.Null(_context.Type(operation.Type)), block, FrontendSubsetClassification.Exact)
                : AllocateValue(operation, block);
        }
        if (depth < 256 && (_context.AllowObjectWidening || AllowOpaqueCalls) && operation is IConversionOperation
            {
                IsImplicit: true, OperatorMethod: null, Type.SpecialType: SpecialType.System_Object,
                Operand.Type.TypeKind: TypeKind.Class or TypeKind.Interface or TypeKind.Delegate or TypeKind.Array
            } widening &&
            widening.Conversion.IsReference)
        {
            var operand = LowerBodyValue(widening.Operand, block, depth + 1);
            return operand.Classification.IsExact
                ? new(_factory.Cast(_factory.ObjectType, operand.Value), operand.Continuation, operand.Classification)
                : Approximate(operation, operand.Continuation, operand.Classification.Abstention);
        }
        if (depth < 256 && (_context.AllowObjectWidening || AllowOpaqueCalls) && CSharpOperationSemantics.ReferenceUpcast(operation) is { } upcast)
        {
            var operand = LowerBodyValue(upcast.Operand, block, depth + 1);
            return operand.Classification.IsExact
                ? new(_factory.Cast(_context.Type(upcast.Type), operand.Value), operand.Continuation, operand.Classification)
                : Approximate(operation, operand.Continuation, operand.Classification.Abstention);
        }
        if (depth < 256 && CSharpOperationSemantics.ReferenceDowncast(operation) is { } downcast)
        {
            var operand = LowerBodyValue(downcast.Operand, block, depth + 1);
            if (!operand.Classification.IsExact)
            { return Approximate(operation, operand.Continuation, operand.Classification.Abstention); }
            var fits = _context.Temporary(_factory.BooleanType);
            _builder!.Havoc(operand.Continuation, _context.Site(operation), IrHavocKind.Variables, IrHavocOrigin.Approximation, fits);
            return ApplyRule(operation, CSharpOperationSemantics.ReferenceDowncast(_factory, _context.Type(downcast.Type), operand.Value,
                _factory.Variable(fits)), operand.Continuation);
        }
        if (depth < 256 && operation is IConversionOperation boxing && CSharpOperationSemantics.IsScalarBoxing(boxing))
        {
            var operand = LowerBodyValue(boxing.Operand, block, depth + 1);
            return operand.Classification.IsExact ? AllocateValue(operation, operand.Continuation)
                : Approximate(operation, operand.Continuation, operand.Classification.Abstention);
        }
        if (depth < 256 && (AllowOpaqueCalls || ShadowCallSkeleton) && operation is IConversionOperation opaqueBoxing &&
            opaqueBoxing.Parent is IArgumentOperation &&
            CSharpOperationSemantics.IsImplicitOpaqueBoxing(opaqueBoxing, _context.Compilation))
        {
            if (opaqueBoxing.Operand is IDefaultValueOperation &&
                CompilerIdentityBridge.GetNullableUnderlyingType(opaqueBoxing.Operand.Type) != null)
            { return new(_factory.Null(_context.Type(opaqueBoxing.Type)), block, FrontendSubsetClassification.Exact); }
            var operand = LowerBodyValue(opaqueBoxing.Operand, block, depth + 1);
            if (!operand.Classification.IsExact)
            { return Approximate(operation, operand.Continuation, operand.Classification.Abstention); }
            // A box may be elided or reuse a reference. Keep only a possible
            // caller allocation and an unconstrained reference result.
            var site = _context.OpaqueCallSite(operation, IrOpaqueCallEffects.Allocates, "boxing");
            if (!ShadowCallSkeleton)
            {
                var member = _factory.GetOrCreateMember(_factory.CreateIdentity(), _factory.ObjectType,
                    "opaque-call:boxing", _factory.BooleanType, true);
                _builder!.Call(operand.Continuation, site, null, member, null);
            }
            var boxed = _context.Temporary(_context.Type(opaqueBoxing.Type));
            _builder!.Havoc(operand.Continuation, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, boxed);
            return new(_factory.Variable(boxed), operand.Continuation, FrontendSubsetClassification.Exact);
        }
        if (depth < 256 && operation is IInvocationOperation synchronization &&
            CSharpOperationSemantics.IsMonitorAttempt(synchronization))
        {
            var receiver = LowerBodyValue(synchronization.Arguments[0].Value, block, depth + 1);
            if (receiver.Classification.IsExact)
            { _builder!.Lock(receiver.Continuation, _context.Site(operation), receiver.Value); }
            return new(_factory.Boolean(false), receiver.Continuation, receiver.Classification);
        }
        // A constructor's call to object's constructor does nothing.
        if (CSharpOperationSemantics.IsObjectConstructorCall(operation))
        { return new(_factory.Boolean(false), block, FrontendSubsetClassification.Exact); }
        if (depth < 256 && operation is IInvocationOperation { Instance: { } equalsReceiver, Arguments.Length: 1 } instanceEquals &&
            CSharpOperationSemantics.IsStringInstanceEqualsCall(instanceEquals.TargetMethod))
        {
            var receiver = LowerBodyValue(equalsReceiver, block, depth + 1);
            if (!receiver.Classification.IsExact)
            { return receiver; }
            var argument = LowerBodyValue(instanceEquals.Arguments[0].Value, receiver.Continuation, depth + 1);
            if (!argument.Classification.IsExact)
            { return argument; }
            return ApplyRule(operation, CSharpOperationSemantics.StringInstanceEquals(_factory, receiver.Value, argument.Value), argument.Continuation);
        }
        if (depth < 256 && operation is IInvocationOperation invocation &&
            SourceCall?.Invoke(invocation, block, depth) is { } called)
        { return called; }
        if (depth < 256 && operation is IInvocationOperation opaqueInvocation &&
            OpaqueCall(operation, opaqueInvocation.TargetMethod, opaqueInvocation.Instance, opaqueInvocation.Arguments, block, depth) is { } invoked)
        { return invoked; }
        if (depth < 256 && operation is ISimpleAssignmentOperation { IsRef: false, Target: IPropertyReferenceOperation } setter &&
            SourceSetter?.Invoke(setter, block, depth) is { } set)
        { return set; }
        // A setter neither inlined nor storing a modeled backing field is opaque.
        if (depth < 256 && operation is ISimpleAssignmentOperation { IsRef: false, Target: IPropertyReferenceOperation { Property.SetMethod: { } setMethod } opaqueTarget } opaqueStore &&
            CSharpOperationSemantics.SetterField(opaqueTarget.Property) == null &&
            OpaqueCall(operation, setMethod, opaqueTarget.Instance, opaqueTarget.Arguments, block, depth, opaqueStore.Value) is { } setterCalled)
        { return setterCalled; }
        if (depth < 256 && operation is IPropertyReferenceOperation sourceProperty &&
            CSharpOperationSemantics.GetterField(sourceProperty.Property, CSharpOperationSemantics.IsBaseAccess(sourceProperty.Instance)) == null &&
            SourceGetter?.Invoke(sourceProperty, block, depth) is { } gotten)
        { return gotten; }
        if (depth < 256 && operation is IPropertyReferenceOperation { Property.GetMethod: { } getter } opaqueProperty &&
            !CSharpOperationSemantics.IsLength(opaqueProperty) &&
            CSharpOperationSemantics.GetterField(opaqueProperty.Property, CSharpOperationSemantics.IsBaseAccess(opaqueProperty.Instance)) == null &&
            OpaqueCall(operation, getter, opaqueProperty.Instance, opaqueProperty.Arguments, block, depth) is { } read)
        { return read; }
        if (depth < 256 && (operation.ConstantValue.HasValue || AllowOpaqueCalls && operation is IDefaultValueOperation) &&
            CSharpOperationSemantics.IsOpaqueDomain(operation.Type))
        {
            // Opaque constants and defaults have no effects; their values remain unknown.
            var constant = _context.Temporary(_context.Type(operation.Type));
            _builder!.Havoc(block, _context.Site(operation), IrHavocKind.Variables, IrHavocOrigin.Approximation, constant);
            return new(_factory.Variable(constant), block, FrontendSubsetClassification.Exact);
        }
        if (depth < 256 && operation is IArrayElementReferenceOperation element && CSharpOperationSemantics.IsModeledElementAccess(element) &&
            (ApproximateElementReads || element.ArrayReference.Type is not IArrayTypeSymbol { IsSZArray: true } single ||
                !CSharpOperationSemantics.IsScalar(single.ElementType)))
        { return ElementAccess(element, null, block, depth); }
        // Element stores need the collector's guard on element reads, so only
        // claim lowering admits them.
        if (depth < 256 && AllowOpaqueCalls && operation is ISimpleAssignmentOperation { IsRef: false, Target: IArrayElementReferenceOperation stored } store &&
            CSharpOperationSemantics.IsModeledElementAccess(stored))
        { return ElementAccess(stored, store, block, depth); }
        if (depth < 256 && AllowOpaqueCalls && operation is IIncrementOrDecrementOperation { Target: IArrayElementReferenceOperation incremented } &&
            CSharpOperationSemantics.IsModeledElementAccess(incremented) && incremented.Type?.IsValueType == true)
        { return ElementAccess(incremented, operation, block, depth); }
        if (depth < 256 && AllowOpaqueCalls && operation is ICompoundAssignmentOperation { Target: IArrayElementReferenceOperation compounded } &&
            CSharpOperationSemantics.IsModeledElementAccess(compounded) && compounded.Type?.IsValueType == true)
        { return ElementAccess(compounded, operation, block, depth); }
        // A captured `this` is the modeled receiver; without one, as a value
        // it stays closed.
        if (operation is IFlowCaptureReferenceOperation thisReference && _context.IsThisCapture(thisReference.Id))
        {
            return _context.ThisValue() is { } self ? new(self, block, FrontendSubsetClassification.Exact)
                : Approximate(operation, block, FrontendAbstention.UnsupportedOperationKind);
        }
        if (depth < 256 && _context.CapturedField(operation) is { } capturedRead)
        { return FieldRead(operation, capturedRead.Field, capturedRead.Instance, block, depth, captured: true); }
        if (depth < 256 && operation is ISimpleAssignmentOperation { IsRef: false } capturedStore &&
            _context.CapturedField(capturedStore.Target) is { } capturedTarget &&
            CSharpOperationSemantics.IsSupportedFieldWrite(capturedTarget.Field, _context.Target.ContainingAssembly))
        { return FieldWrite(capturedStore, capturedTarget, block, depth, captured: true); }
        if (depth < 256 && operation is IIncrementOrDecrementOperation incrementedField &&
            (incrementedField.Target as IFieldReferenceOperation ?? _context.CapturedField(incrementedField.Target)) is { } incrementTarget &&
            IsMutableField(incrementTarget))
        { return FieldMutation(operation, incrementTarget, block, depth, captured: incrementedField.Target is not IFieldReferenceOperation); }
        // A nonvirtual auto-property's setter only stores its backing field.
        if (depth < 256 && operation is ISimpleAssignmentOperation { IsRef: false, Target: IPropertyReferenceOperation { Arguments.Length: 0 } setProperty } propertyStore &&
            CSharpOperationSemantics.SetterField(setProperty.Property) is { } setField &&
            CSharpOperationSemantics.IsSupportedFieldWrite(setField, _context.Target.ContainingAssembly))
        { return FieldWrite(propertyStore, setField, setProperty.Instance, block, depth); }
        if (depth < 256 && operation is IIncrementOrDecrementOperation { Target: IPropertyReferenceOperation { Arguments.Length: 0 } incrementedProperty } &&
            CSharpOperationSemantics.SetterField(incrementedProperty.Property, readsValue: true) is { } incrementedBacking &&
            IsMutableField(incrementedBacking, incrementedProperty.Instance))
        { return FieldMutation(operation, incrementedProperty, incrementedBacking, incrementedProperty.Instance, block, depth); }
        if (depth < 256 && operation is ICompoundAssignmentOperation { Target: IPropertyReferenceOperation { Arguments.Length: 0 } compoundedProperty } &&
            CSharpOperationSemantics.SetterField(compoundedProperty.Property, readsValue: true) is { } compoundedBacking &&
            IsMutableField(compoundedBacking, compoundedProperty.Instance))
        { return FieldMutation(operation, compoundedProperty, compoundedBacking, compoundedProperty.Instance, block, depth); }
        if (depth < 256 && operation is ICompoundAssignmentOperation compoundedField &&
            (compoundedField.Target as IFieldReferenceOperation ?? _context.CapturedField(compoundedField.Target)) is { } compoundTarget &&
            IsMutableField(compoundTarget))
        { return FieldMutation(operation, compoundTarget, block, depth, captured: compoundedField.Target is not IFieldReferenceOperation); }
        if (depth < 256 && CSharpOperationSemantics.OpaqueTypeTestOperand(operation) is { } typeTestOperand)
        {
            var tested = LowerBodyValue(typeTestOperand, block, depth + 1);
            if (!tested.Classification.IsExact)
            { return Approximate(operation, tested.Continuation, tested.Classification.Abstention); }
            var matched = _context.Temporary(_factory.BooleanType);
            var site = _context.Site(operation);
            if (CSharpOperationSemantics.OpaqueTypeTestMayAllocate(operation, Spend))
            {
                const string boundary = "type-test-boxing";
                var allocationSite = _context.OpaqueCallSite(operation, IrOpaqueCallEffects.Allocates, boundary);
                if (ShadowCallSkeleton)
                {
                    // The matched result is already unknown. Code this havoc
                    // as allocation-only so other effect facets stay known.
                    site = allocationSite;
                }
                else
                {
                    // This is a possible effect, not a definite allocation.
                    // Keep it independent of the unknown Boolean test result.
                    var member = _factory.GetOrCreateMember(
                        _factory.CreateIdentity(),
                        _factory.ObjectType, "opaque-call:" + boundary, _factory.BooleanType, true);
                    _builder!.Call(tested.Continuation, allocationSite, null, member, null);
                }
            }
            _builder!.Havoc(tested.Continuation, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, matched);
            return new(_factory.Variable(matched), tested.Continuation, FrontendSubsetClassification.Exact);
        }
        if (depth < 256 && operation is IFieldReferenceOperation fieldRead &&
            CSharpOperationSemantics.IsSupportedFieldRead(fieldRead.Field))
        { return FieldRead(operation, fieldRead.Field, fieldRead.Instance, block, depth); }
        if (depth < 256 && operation is IPropertyReferenceOperation { Arguments.Length: 0 } propertyRead &&
            CSharpOperationSemantics.GetterField(propertyRead.Property, CSharpOperationSemantics.IsBaseAccess(propertyRead.Instance)) is { } getterField)
        { return FieldRead(operation, getterField, propertyRead.Instance, block, depth); }
        // Roslyn's null test for `??` and `?.` on a reference compares it with
        // null; it runs no code.
        if (depth < 256 && operation is IIsNullOperation { Operand: var nullable } && CSharpOperationSemantics.IsReferenceDomain(nullable.Type))
        {
            var value = LowerBodyValue(nullable, block, depth + 1);
            return value.Classification.IsExact
                ? new(_factory.Binary(IrBinaryOperator.Equal, value.Value, _factory.Null(value.Value.Type)), value.Continuation,
                    FrontendSubsetClassification.Exact)
                : value;
        }
        var rejected = Reject(operation, depth);
        if (rejected != FrontendAbstention.None)
        {
            return Approximate(operation, block, rejected);
        }
        if (TryLeaf(operation, TotalParameterState.Current, out var leaf))
        {
            var parts = operation is IFlowCaptureReferenceOperation capture
                ? _context.ConcatenationOperands(capture.Id) : default;
            return Capture(operation, new(leaf!, block, FrontendSubsetClassification.Exact, parts));
        }
        switch (operation)
        {
            case IParenthesizedOperation parenthesized:
                return LowerBodyValue(parenthesized.Operand, block, depth + 1);
            case ISimpleAssignmentOperation { IsRef: false, Target: IDiscardOperation } assignment:
                // A discard evaluates its RHS with all calls and faults intact.
                // It contributes no storage write of its own.
                return LowerBodyValue(assignment.Value, block, depth + 1);
            // Only a simple store goes through a captured variable: a compound
            // assignment reads its target before its right-hand side.
            case ISimpleAssignmentOperation { IsRef: false } assignment when TryStorage(assignment.Target, out var target) ||
                _context.CapturedStorage(assignment.Target) is { } captured && (target = captured) == captured:
                {
                    var right = LowerBodyValue(assignment.Value, block, depth + 1);
                    if (right.Classification.IsExact)
                    {
                        var mutation = _builder!.Assign(right.Continuation, _context.Site(operation), target, right.Value);
                        _builder.Write(right.Continuation, mutation.Operation, IrWriteRegion.Local);
                    }
                    return right;
                }
            case ISimpleAssignmentOperation { IsRef: false, Target: IFieldReferenceOperation field } assignment
                when CSharpOperationSemantics.IsSupportedFieldWrite(field.Field, _context.Target.ContainingAssembly):
                return FieldWrite(assignment, field, block, depth);
            case IIncrementOrDecrementOperation increment when TryStorage(increment.Target, out var target):
                return Increment(increment, target, block);
            case ICompoundAssignmentOperation compound when TryStorage(compound.Target, out var target):
                return Compound(compound, target, block, depth);
            case IConditionalOperation { WhenFalse: { } whenFalse } conditional:
                return Conditional(operation, conditional.Condition, conditional.WhenTrue, whenFalse, block, depth);
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr } binary:
                return Lazy(binary, block, depth);
            case IConversionOperation conversion:
                return ApplyBody(operation, [LowerBodyValue(conversion.Operand, block, depth + 1)]);
            case IPropertyReferenceOperation { Instance: { } receiver }:
                return ApplyBody(operation, [LowerBodyValue(receiver, block, depth + 1)]);
            case IArrayElementReferenceOperation { Indices.Length: 1 } access:
                {
                    var array = LowerBodyValue(access.ArrayReference, block, depth + 1);
                    var index = LowerBodyValue(access.Indices[0], array.Continuation, depth + 1);
                    var elementRead = ApplyBody(operation, [array, index]);
                    return PinElementReads && elementRead.Classification.IsExact ? Pin(operation, elementRead) : elementRead;
                }
            case IUnaryOperation unary:
                return ApplyBody(operation, [LowerBodyValue(unary.Operand, block, depth + 1)]);
            case IBinaryOperation binary:
                {
                    var operands = CSharpOperationSemantics.EqualityOperands(binary);
                    var left = LowerBodyValue(operands.Left, block, depth + 1);
                    var right = LowerBodyValue(operands.Right, left.Continuation, depth + 1);
                    return ApplyBody(operation, [left, right]);
                }
            default:
                return Approximate(operation, block, FrontendAbstention.UnsupportedOperationKind);
        }
    }

    // Evaluates value here: a later element store must not change it.
    private TotalBodyValue Pin(IOperation operation, TotalBodyValue value)
    {
        var pinned = _context.Temporary(value.Value.Type);
        _builder!.Assign(value.Continuation, _context.Site(operation), pinned, value.Value);
        return new(_factory.Variable(pinned), value.Continuation, value.Classification);
    }

    internal TotalBodyValue AllocateValue(IOperation operation, IrBlockId block)
    {
        var type = _context.Type(operation.Type);
        var target = _context.Temporary(type);
        _builder!.Allocate(block, _context.Site(operation), type, target);
        return new(_factory.Variable(target), block, FrontendSubsetClassification.Exact);
    }

    private TotalBodyValue LowerStringConcatenation(IOperation operation, IrBlockId block, int depth)
    {
        var rejected = Reject(operation, depth);
        if (rejected != FrontendAbstention.None)
        { return Approximate(operation, block, rejected); }
        if (operation.ConstantValue is { HasValue: true, Value: string text })
        { return Capture(operation, new(_factory.String(text), block, FrontendSubsetClassification.Exact)); }
        // A cast-wrapped chain lowered on its own here was not collected by an
        // enclosing chain (e.g. a CFG capture), but the compiler still splices it.
        if (operation.Parent != null && CSharpOperationSemantics.IdentityCastConcatenation(operation.Parent) != null &&
            CSharpOperationSemantics.IsConcatenationOperand(operation.Syntax))
        { return Approximate(operation, block, FrontendAbstention.UnsupportedOperationKind); }
        var leaves = new List<IOperation>();
        Collect(operation, depth);
        var operands = ImmutableArray.CreateBuilder<IrTerm>();
        var constant = new System.Text.StringBuilder();
        foreach (var leaf in leaves)
        {
            var value = LowerBodyValue(leaf, block, depth + 1);
            if (!value.Classification.IsExact)
            { return value; }
            block = value.Continuation;
            if (!value.ConcatenationOperands.IsDefault)
            {
                foreach (var part in value.ConcatenationOperands)
                {
                    if (part is IrStringTerm textPart)
                    { constant.Append(_factory.GetString(textPart.Value)); }
                    else
                    {
                        FlushConstant();
                        operands.Add(part);
                    }
                }
            }
            else if (leaf.ConstantValue.HasValue && leaf.ConstantValue.Value is null or string)
            { constant.Append(leaf.ConstantValue.Value as string); }
            else
            {
                FlushConstant();
                operands.Add(value.Value);
            }
        }
        FlushConstant();
        // The compiler merges adjacent constants and drops constant null/empty
        // strings before choosing a fixed overload or creating a params array.
        if (operands.Count > 4)
        { return Approximate(operation, block, FrontendAbstention.UnsupportedOperationKind); }
        if (operands.Count == 0)
        { return Capture(operation, new(_factory.String(""), block, FrontendSubsetClassification.Exact)); }
        if (operands.Count == 1)
        { operands.Insert(0, _factory.String("")); }
        var parent = operation.Syntax.Parent;
        while (parent is Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedExpressionSyntax)
        { parent = parent.Parent; }
        var deferred = operation.Parent is IFlowCaptureOperation &&
            parent is Microsoft.CodeAnalysis.CSharp.Syntax.BinaryExpressionSyntax binaryParent &&
            binaryParent.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddExpression);
        return CaptureStringConcatenation(operation, operands.ToImmutable(), block, deferred);

        void FlushConstant()
        {
            if (constant.Length != 0)
            {
                operands.Add(_factory.String(constant.ToString()));
                constant.Clear();
            }
        }

        void Collect(IOperation current, int nesting)
        {
            Spend?.Invoke();
            if (nesting < 256 && CSharpOperationSemantics.IsStringConcatenation(current) && !current.ConstantValue.HasValue)
            {
                var binary = (IBinaryOperation)current;
                Collect(binary.LeftOperand, nesting + 1);
                Collect(binary.RightOperand, nesting + 1);
            }
            else if (nesting < 256 && CSharpOperationSemantics.IdentityCastConcatenation(current) is { } castConcatenation)
            { Collect(castConcatenation, nesting + 1); }
            else
            { leaves.Add(current); }
        }
    }

    private TotalBodyValue CaptureStringConcatenation(IOperation operation, ImmutableArray<IrTerm> operands, IrBlockId block,
        bool deferred = false)
    {
        var site = _context.Site(operation);
        var allocation = _builder!.CreateBlock("concat:allocate");
        var join = _builder.CreateBlock("concat:join");
        // A CFG-captured prefix remains part of the emitter's flattened chain.
        // Its value may be carried, but its allocation is deferred to the root.
        _builder.Branch(block, site, deferred ? _factory.Boolean(false) :
            CSharpOperationSemantics.StringConcatenationAllocates(_factory, operands), allocation, join);
        _builder.Allocate(allocation, site, _factory.StringType);
        _builder.Goto(allocation, site, join);
        var result = operands[0];
        foreach (var operand in operands.Skip(1))
        { result = CSharpOperationSemantics.StringConcat(_factory, result, operand).Value; }
        return Capture(operation, new(result, join, FrontendSubsetClassification.Exact,
            deferred ? operands : default), site);
    }

    internal TotalBodyValue LowerDiscardedInstanceFieldMutation(IIncrementOrDecrementOperation operation, IrBlockId block)
    {
        var field = (IFieldReferenceOperation)operation.Target;
        IrTerm? receiver = null;
        var implicitThis = IsImplicitThis(field.Instance);
        // A modeled field keeps its value, so the increment is computed.
        if ((implicitThis ? _context.Receiver != null : field.Instance != null) && !ApproximateElementReads && FieldMember(field.Field) != null)
        { return FieldMutation(operation, field, block, 1); }
        if (!implicitThis)
        {
            if (field.Instance == null)
            { return Approximate(operation, block, FrontendAbstention.UnsupportedOperationKind); }
            var instance = LowerBodyValue(field.Instance, block, 1);
            if (!instance.Classification.IsExact)
            { return Approximate(operation, instance.Continuation, instance.Classification.Abstention); }
            receiver = instance.Value;
            block = instance.Continuation;
        }
        var result = ApplyRule(operation, CSharpOperationSemantics.FieldWrite(_factory, _factory.Integer(0), receiver), block);
        var region = field.Instance is IParameterReferenceOperation ? IrWriteRegion.Parameter : IrWriteRegion.Field;
        _builder!.Write(result.Continuation, ReceiverWriteSite(operation, field.Instance), region);
        return result;
    }
    // A metadata call, or a dispatched source call, that is neither inlined nor
    // modeled. Its result is unknown, it may throw an exception of unknown
    // type, and its effects are unknown; the worker treats its site as a
    // possible allocation, write and lock. Arguments pass by value. A class
    // receiver is null-checked; a struct or type-parameter receiver is an
    // opaque value, so a mutation through `this` is unobservable. Nonvirtual
    // source callees stay with inlining.
    // A setter takes the assigned value last; the assignment's value is it.
    private TotalBodyValue? OpaqueCall(IOperation operation, IMethodSymbol method, IOperation? instance,
        ImmutableArray<IArgumentOperation> arguments, IrBlockId block, int depth, IOperation? assigned = null)
    {
        var dispatched = method.IsVirtual || method.IsAbstract || method.IsOverride || method.ContainingType.TypeKind == TypeKind.Interface;
        if (!AllowOpaqueCalls || !method.DeclaringSyntaxReferences.IsEmpty && !dispatched ||
            IsSharpProofApi(method.ContainingNamespace) ||
            method.ReturnsByRef || method.ReturnsByRefReadonly || method.IsStatic != (instance == null) ||
            instance != null && !CSharpOperationSemantics.IsValueDomain(instance.Type) ||
            !method.ReturnsVoid && !CSharpOperationSemantics.IsValueDomain(method.ReturnType) ||
            method.Parameters.Any(parameter => parameter.RefKind != RefKind.None) ||
            arguments.Any(argument => argument.Parameter == null ||
                argument.ArgumentKind is not (ArgumentKind.Explicit or ArgumentKind.DefaultValue)) ||
            assigned != null && !CSharpOperationSemantics.IsValueDomain(assigned.Type))
        { return null; }
        IrTerm? receiver = null;
        if (instance != null && !IsImplicitThis(instance))
        {
            var lowered = LowerBodyValue(instance, block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return Approximate(operation, lowered.Continuation, lowered.Classification.Abstention); }
            receiver = lowered.Value;
            block = lowered.Continuation;
        }
        var values = new IrTerm[arguments.Length + (assigned != null ? 1 : 0)];
        foreach (var argument in arguments.OrderBy(argument => argument.Syntax.SpanStart))
        {
            var lowered = LowerBodyValue(CSharpOperationSemantics.OpaqueArgument(argument.Value), block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return Approximate(operation, lowered.Continuation, lowered.Classification.Abstention); }
            values[arguments.IndexOf(argument)] = lowered.Value;
            block = lowered.Continuation;
        }
        if (assigned != null)
        {
            var lowered = LowerBodyValue(assigned, block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return Approximate(operation, lowered.Continuation, lowered.Classification.Abstention); }
            values[values.Length - 1] = lowered.Value;
            block = lowered.Continuation;
        }
        if (receiver != null && instance!.Type!.IsReferenceType)
        { block = CheckReceiver(operation, receiver, block); }
        // A dispatched call may run an override the specification does not describe.
        return EmitOpaqueCall(operation, method, values, block, assigned != null ? values[values.Length - 1] : null,
            (dispatched ? null : OpaqueEffects?.Invoke(method)) ?? IrOpaqueCallEffects.All);
    }

    // The call itself is only an effect site; its result and whether it
    // throws are approximation havocs, so no refutation may depend on them. An
    // assignment's value is the assigned one.
    internal TotalBodyValue EmitOpaqueCall(IOperation operation, IMethodSymbol method, IrTerm[] values, IrBlockId block,
        IrTerm? assigned, IrOpaqueCallEffects effects)
    {
        var site = _context.Site(operation);
        var resultType = method.ReturnsVoid ? _factory.BooleanType : _context.Type(method.ReturnType);
        var display = CompilerIdentityBridge.CreateSymbolDisplay(method);
        var member = _factory.GetOrCreateMember(
            CompilerIdentityBridge.InternSymbol(_factory, method), _context.Type(method.ContainingType),
            "opaque-call:" + display, resultType, true, [.. values.Select(value => value.Type)]);
        _builder!.Call(block, _context.OpaqueCallSite(operation, effects, display), null, member, null, values);
        ImmutableArray<TotalThrow> faults = [];
        if ((effects & IrOpaqueCallEffects.Throws) != 0)
        {
            var throws = _context.Temporary(_factory.BooleanType);
            _builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, throws);
            faults = [new(IrExceptionKind.Unknown, _factory.Variable(throws))];
        }
        IrTerm result = assigned ?? _factory.Boolean(false);
        if (!method.ReturnsVoid)
        {
            var target = _context.Temporary(resultType);
            _builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, target);
            result = _factory.Variable(target);
        }
        return ApplyRule(operation, new TotalScalarRule(result, faults, FrontendSubsetClassification.Exact), block);
    }

    // `new T(arguments)` with a metadata constructor evaluates its arguments,
    // allocates the object and runs the constructor as an opaque call, which
    // may throw. Its value is the fresh object.
    private TotalBodyValue? OpaqueConstruction(IObjectCreationOperation creation, IrBlockId block, int depth)
    {
        if (!AllowOpaqueCalls || creation.Constructor is not { } constructor || !constructor.DeclaringSyntaxReferences.IsEmpty ||
            creation.Initializer != null || creation.Type is not { IsReferenceType: true } type ||
            !CSharpOperationSemantics.IsValueDomain(type) || _factory.GetTypeInfo(_context.Type(type)).Kind != IrTypeKind.Reference ||
            IsSharpProofApi(constructor.ContainingNamespace) ||
            constructor.Parameters.Any(parameter => parameter.RefKind != RefKind.None) ||
            creation.Arguments.Any(argument => argument.Parameter == null ||
                argument.ArgumentKind is not (ArgumentKind.Explicit or ArgumentKind.DefaultValue)))
        { return null; }
        var values = new IrTerm[creation.Arguments.Length];
        foreach (var argument in creation.Arguments.OrderBy(argument => argument.Syntax.SpanStart))
        {
            var lowered = LowerBodyValue(CSharpOperationSemantics.OpaqueArgument(argument.Value), block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return Approximate(creation, lowered.Continuation, lowered.Classification.Abstention); }
            values[creation.Arguments.IndexOf(argument)] = lowered.Value;
            block = lowered.Continuation;
        }
        // A rejected implicit source constructor may initialize its type before
        // any instance exists. Its unknown completion precedes allocation.
        var deferAllocation = constructor.IsImplicitlyDeclared && !constructor.ContainingType.DeclaringSyntaxReferences.IsEmpty;
        var created = deferAllocation
            ? new TotalBodyValue(_factory.Boolean(true), block, FrontendSubsetClassification.Exact)
            : AllocateValue(creation, block);
        var display = CompilerIdentityBridge.CreateSymbolDisplay(constructor);
        var member = _factory.GetOrCreateMember(CompilerIdentityBridge.InternSymbol(_factory, constructor), _context.Type(constructor.ContainingType),
            "opaque-call:" + display, _factory.BooleanType, true, [.. values.Select(value => value.Type)]);
        var effects = deferAllocation ? IrOpaqueCallEffects.All : OpaqueEffects?.Invoke(constructor) ?? IrOpaqueCallEffects.All;
        _builder!.Call(created.Continuation, _context.OpaqueCallSite(creation, effects, display), null, member, null, values);
        ImmutableArray<TotalThrow> faults = [];
        if ((effects & IrOpaqueCallEffects.Throws) != 0)
        {
            var throws = _context.Temporary(_factory.BooleanType);
            _builder.Havoc(created.Continuation, _context.Site(creation), IrHavocKind.Variables, IrHavocOrigin.Approximation, throws);
            faults = [new(IrExceptionKind.Unknown, _factory.Variable(throws))];
        }
        var completion = ApplyRule(creation, new TotalScalarRule(created.Value, faults, FrontendSubsetClassification.Exact), created.Continuation);
        return deferAllocation ? AllocateValue(creation, completion.Continuation) : completion;
    }

    // An element read, store, increment or compound assignment. The array and
    // indexes evaluate first. A store evaluates its value before the null and
    // bounds checks; an increment or compound assignment checks first. The
    // element read is an approximation, and every store is an Element write.
    private TotalBodyValue ElementAccess(IArrayElementReferenceOperation access, IOperation? mutation, IrBlockId block, int depth)
    {
        var array = LowerBodyValue(access.ArrayReference, block, depth + 1);
        if (!array.Classification.IsExact)
        { return Approximate(mutation ?? access, array.Continuation, array.Classification.Abstention); }
        block = array.Continuation;
        var indices = new List<IrTerm>();
        foreach (var index in access.Indices)
        {
            var lowered = LowerBodyValue(index, block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return Approximate(mutation ?? access, lowered.Continuation, lowered.Classification.Abstention); }
            indices.Add(lowered.Value);
            block = lowered.Continuation;
        }
        var site = _context.Site(mutation ?? access);
        var writeSite = mutation != null && access.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.ElementAccessExpressionSyntax element &&
            CSharpOperationSemantics.IsFreshReceiver(element.Expression, _context.Compilation)
            ? _context.FreshWriteSite(mutation) : site;
        TotalBodyValue? stored = null;
        if (mutation is ISimpleAssignmentOperation assignment)
        {
            stored = LowerBodyValue(assignment.Value, block, depth + 1);
            if (!stored.Value.Classification.IsExact)
            { return Approximate(mutation, stored.Value.Continuation, stored.Value.Classification.Abstention); }
            block = stored.Value.Continuation;
        }
        IrTerm? outside = null;
        if (indices.Count != 1 || access.ArrayReference.Type is IArrayTypeSymbol { IsSZArray: false })
        {
            var flag = _context.Temporary(_factory.BooleanType);
            _builder!.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, flag);
            outside = _factory.Variable(flag);
        }
        var guard = CSharpOperationSemantics.ElementGuard(_factory, array.Value, outside == null ? indices[0] : null, outside);
        if (!guard.Classification.IsExact)
        { return Approximate(mutation ?? access, block, guard.Classification.Abstention); }
        block = ApplyRule(access, guard, block).Continuation;
        if (outside == null && indices[0].Type != _factory.IntegerType)
        { indices[0] = _factory.Cast(_factory.IntegerType, indices[0]); }
        var elementType = ((IArrayTypeSymbol)access.ArrayReference.Type!).ElementType;
        // A scalar element of a single-dimensional array is stored with its
        // value; other stores are effect sites only.
        var modeled = outside == null && CSharpOperationSemantics.IsScalar(elementType) &&
            _factory.GetTypeInfo(array.Value.Type).ElementType == _context.Type(elementType);
        if (stored is { } value)
        {
            if (CSharpOperationSemantics.ArrayStoreNeedsCompatibility(elementType))
            {
                // A covariant array may reject the stored reference.
                var mismatch = _context.Temporary(_factory.BooleanType);
                _builder!.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, mismatch);
                block = ApplyRule(access, new TotalScalarRule(_factory.Boolean(true),
                    [new(IrExceptionKind.Unknown, _factory.Variable(mismatch))], FrontendSubsetClassification.Exact), block).Continuation;
            }
            if (modeled && value.Value.Type == _context.Type(elementType))
            { _builder!.ElementStore(block, writeSite, array.Value, indices[0], value.Value); }
            else
            { _builder!.Write(block, writeSite, IrWriteRegion.Element); }
            return new(value.Value, block, FrontendSubsetClassification.Exact);
        }
        TotalBodyValue old;
        if (modeled && !ApproximateElementReads)
        { old = Pin(access, new(_factory.SequenceAccess(array.Value, indices[0]), block, FrontendSubsetClassification.Exact)); }
        else
        {
            var current = _context.Temporary(_context.Type(elementType));
            _builder!.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, current);
            old = new(_factory.Variable(current), block, FrontendSubsetClassification.Exact);
        }
        if (mutation == null)
        { return old; }
        TotalBodyValue next;
        if (mutation is IIncrementOrDecrementOperation increment)
        {
            var rule = CSharpOperationSemantics.Increment(_factory, increment, old.Value);
            if (!rule.Classification.IsExact)
            { return Approximate(mutation, block, rule.Classification.Abstention); }
            next = ApplyRule(mutation, rule, block);
        }
        else
        {
            var compound = (ICompoundAssignmentOperation)mutation;
            var right = LowerBodyValue(compound.Value, block, depth + 1);
            if (!right.Classification.IsExact)
            { return Approximate(mutation, right.Continuation, right.Classification.Abstention); }
            var rule = CSharpOperationSemantics.Compound(_factory, compound, old.Value, right.Value);
            if (!rule.Classification.IsExact)
            { return Approximate(mutation, right.Continuation, rule.Classification.Abstention); }
            next = ApplyRule(mutation, rule, right.Continuation);
        }
        if (modeled && next.Value.Type == old.Value.Type)
        { _builder!.ElementStore(next.Continuation, writeSite, array.Value, indices[0], next.Value); }
        else
        { _builder!.Write(next.Continuation, writeSite, IrWriteRegion.Element); }
        return mutation is IIncrementOrDecrementOperation { IsPostfix: true } ? new(old.Value, next.Continuation, next.Classification) : next;
    }

    // Contract and attribute APIs are specifications, never opaque calls.
    private static bool IsSharpProofApi(INamespaceSymbol? space)
    {
        while (space is { IsGlobalNamespace: false, ContainingNamespace.IsGlobalNamespace: false })
        { space = space.ContainingNamespace; }
        return space is { IsGlobalNamespace: false, Name: "SharpProof" };
    }

    private TotalBodyValue FieldRead(IOperation operation, IFieldSymbol field, IOperation? instance,
        IrBlockId block, int depth, bool captured = false)
    { return FieldRead(operation, field, instance, block, depth, captured, out _); }

    private TotalBodyValue FieldRead(IOperation operation, IFieldSymbol field, IOperation? instance,
        IrBlockId block, int depth, bool captured, out IrTerm? receiver)
    {
        receiver = null;
        var implicitThis = IsImplicitThis(instance);
        if (!implicitThis && !field.IsStatic)
        {
            if (instance == null)
            { return Approximate(operation, block, FrontendAbstention.UnsupportedOperationKind); }
            var lowered = LowerBodyValue(instance, block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return Approximate(operation, lowered.Continuation, lowered.Classification.Abstention); }
            receiver = lowered.Value;
            block = lowered.Continuation;
        }
        if ((receiver ?? (implicitThis && !captured ? _context.ReceiverValue() : null)) is { } owner && !ApproximateElementReads &&
            FieldMember(field) is { } member)
        {
            var read = ApplyRule(operation, CSharpOperationSemantics.FieldRead(_factory, _factory.PureOpaque(member, AsObject(owner)), receiver), block);
            return PinElementReads && read.Classification.IsExact ? Pin(operation, read) : read;
        }
        var value = _context.Temporary(_context.Type(field.Type));
        _builder!.Havoc(block, field.IsStatic ? _context.StaticReadSite(operation) : _context.Site(operation),
            IrHavocKind.Variables, IrHavocOrigin.Approximation, value);
        return ApplyRule(operation, CSharpOperationSemantics.FieldRead(_factory, _factory.Variable(value), receiver), block);
    }

    // A scalar or reference instance field of a class: its reads and stores
    // are modeled through an object-typed receiver.
    private IrMemberId? FieldMember(IFieldSymbol field)
    {
        if (field.IsStatic || !field.ContainingType.IsReferenceType ||
            !CSharpOperationSemantics.HasIndependentFieldStorage(field) ||
            !CSharpOperationSemantics.IsScalar(field.Type) && !CSharpOperationSemantics.IsReferenceDomain(field.Type))
        { return null; }
        // Inlined source bodies bind declaration symbols. A field whose type
        // does not change under substitution must name the same slot there
        // and at a constructed caller. Generic-dependent storage stays distinct.
        var definition = field.OriginalDefinition;
        var identity = SymbolEqualityComparer.Default.Equals(field.Type, definition.Type) ? definition : field;
        return _factory.GetOrCreateMember(CompilerIdentityBridge.InternSymbol(_factory, identity), _factory.ObjectType,
            IrFieldSites.Prefix + CompilerIdentityBridge.CreateSymbolDisplay(identity), _context.Type(field.Type), false);
    }

    private IrTerm AsObject(IrTerm receiver)
    { return receiver.Type == _factory.ObjectType ? receiver : _factory.Cast(_factory.ObjectType, receiver); }

    // A field increment or compound assignment on `this`, a parameter or a
    // local reads the field (faulting on a null receiver), computes from that
    // approximated value, and writes it back.
    private bool IsMutableField(IFieldReferenceOperation field)
    { return IsMutableField(field.Field, field.Instance); }

    private bool IsMutableField(IFieldSymbol field, IOperation? instance)
    {
        return CSharpOperationSemantics.IsSupportedFieldRead(field) &&
            CSharpOperationSemantics.IsSupportedFieldWrite(field, _context.Target.ContainingAssembly) &&
            (IsImplicitThis(instance) || instance is IParameterReferenceOperation or ILocalReferenceOperation);
    }

    private TotalBodyValue FieldMutation(IOperation operation, IFieldReferenceOperation field, IrBlockId block, int depth,
        bool captured = false)
    { return FieldMutation(operation, field, field.Field, field.Instance, block, depth, captured); }

    // `access` is the field or auto-property reference being mutated.
    private TotalBodyValue FieldMutation(IOperation operation, IOperation access, IFieldSymbol field, IOperation? instance,
        IrBlockId block, int depth, bool captured = false)
    {
        var old = FieldRead(access, field, instance, block, depth, captured, out var receiver);
        if (!old.Classification.IsExact)
        { return Approximate(operation, old.Continuation, old.Classification.Abstention); }
        if (old.Value is not IrVariableTerm)
        { old = Pin(access, old); }
        TotalBodyValue next;
        if (operation is IIncrementOrDecrementOperation increment)
        {
            var rule = CSharpOperationSemantics.Increment(_factory, increment, old.Value);
            if (!rule.Classification.IsExact)
            { return Approximate(operation, old.Continuation, rule.Classification.Abstention); }
            next = ApplyRule(operation, rule, old.Continuation);
        }
        else
        {
            var compound = (ICompoundAssignmentOperation)operation;
            var right = LowerBodyValue(compound.Value, old.Continuation, depth + 1);
            if (!right.Classification.IsExact)
            { return Approximate(operation, right.Continuation, right.Classification.Abstention); }
            var rule = CSharpOperationSemantics.Compound(_factory, compound, old.Value, right.Value);
            if (!rule.Classification.IsExact)
            { return Approximate(operation, right.Continuation, rule.Classification.Abstention); }
            next = ApplyRule(operation, rule, right.Continuation);
        }
        var stored = ApplyRule(operation, CSharpOperationSemantics.FieldWrite(_factory, next.Value, receiver), next.Continuation);
        var region = instance is IParameterReferenceOperation ? IrWriteRegion.Parameter : IrWriteRegion.Field;
        Store(stored.Continuation, ReceiverWriteSite(operation, instance), region, field,
            receiver ?? (captured ? null : ImplicitReceiver(instance)), next.Value);
        return operation is IIncrementOrDecrementOperation { IsPostfix: true }
            ? new(old.Value, stored.Continuation, FrontendSubsetClassification.Exact)
            : new(next.Value, stored.Continuation, FrontendSubsetClassification.Exact);
    }

    private TotalBodyValue FieldWrite(ISimpleAssignmentOperation assignment,
        IFieldReferenceOperation field, IrBlockId block, int depth, bool captured = false)
    { return FieldWrite(assignment, field.Field, field.Instance, block, depth, captured); }

    private TotalBodyValue FieldWrite(ISimpleAssignmentOperation assignment,
        IFieldSymbol field, IOperation? instance, IrBlockId block, int depth, bool captured = false)
    {
        IrTerm? receiver = null;
        var implicitThis = IsImplicitThis(instance);
        if (!field.IsStatic && !implicitThis)
        {
            if (instance == null)
            { return Approximate(assignment, block, FrontendAbstention.UnsupportedOperationKind); }
            var lowered = LowerBodyValue(instance, block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return Approximate(assignment, lowered.Continuation, lowered.Classification.Abstention); }
            receiver = lowered.Value;
            block = lowered.Continuation;
        }
        // C# captures the receiver before evaluating the RHS, but faults on a
        // null receiver only after the RHS has finished evaluating.
        var right = LowerBodyValue(assignment.Value, block, depth + 1);
        if (!right.Classification.IsExact)
        { return right; }
        var result = ApplyRule(assignment,
            CSharpOperationSemantics.FieldWrite(_factory, right.Value, receiver), right.Continuation);
        var region = field.IsStatic ? IrWriteRegion.Static :
            instance is IParameterReferenceOperation ? IrWriteRegion.Parameter : IrWriteRegion.Field;
        Store(result.Continuation, ReceiverWriteSite(assignment, instance), region, field,
            receiver ?? (captured ? null : ImplicitReceiver(instance)), right.Value);
        return result;
    }

    // The modeled `this`, when the field belongs to it. A capture of a field
    // of `this` reads at a different time than C# does, so it stays approximate.
    private IrTerm? ImplicitReceiver(IOperation? instance)
    { return IsImplicitThis(instance) ? _context.ReceiverValue() : null; }

    // A modeled field of an object is stored with its value; any other field
    // write is an effect site only.
    private void Store(IrBlockId block, OperationId site, IrWriteRegion region, IFieldSymbol field, IrTerm? receiver, IrTerm value)
    {
        if (receiver != null && region is IrWriteRegion.Field or IrWriteRegion.Parameter && FieldMember(field) is { } member &&
            _factory.GetMemberInfo(member).ReturnType == value.Type)
        { _builder!.FieldStore(block, site, region, AsObject(receiver), member, value); }
        else
        { _builder!.Write(block, site, region); }
    }

    private OperationId ReceiverWriteSite(IOperation operation, IOperation? instance)
    {
        return _context.FreshReceiver && IsImplicitThis(instance) ||
            instance != null && !IsImplicitThis(instance) && CSharpOperationSemantics.IsFreshReceiver(instance.Syntax, _context.Compilation)
            ? _context.FreshWriteSite(operation) : _context.Site(operation);
    }

    private GuardedExpression? StringEqualsClause(IInvocationOperation invocation, TotalParameterState state, int depth)
    {
        if (invocation is { Instance: { } instance, Arguments.Length: 1 } &&
            CSharpOperationSemantics.IsStringInstanceEqualsCall(invocation.TargetMethod))
        {
            var receiver = LowerClause(instance, state, depth + 1);
            var argument = LowerClause(invocation.Arguments[0].Value, state, depth + 1);
            return Compose(invocation, receiver, argument,
                CSharpOperationSemantics.StringInstanceEquals(_factory, receiver.Value, argument.Value));
        }
        if (!CSharpOperationSemantics.IsStringEqualsCall(invocation.TargetMethod) || invocation.Arguments.Length != 2)
        { return null; }
        var arguments = new GuardedExpression[2];
        foreach (var argument in invocation.Arguments)
        { arguments[argument.Parameter!.Ordinal] = LowerClause(argument.Value, state, depth + 1); }
        var classification = First(arguments[0].Classification, arguments[1].Classification);
        return classification.IsExact
            ? new(_factory.Binary(IrBinaryOperator.StringEquals, arguments[0].Value, arguments[1].Value),
                And(arguments[0].SafeCondition, arguments[1].SafeCondition), classification)
            : Failed(invocation, classification.Abstention);
    }

    private GuardedExpression Compose(IOperation operation, GuardedExpression receiver, GuardedExpression argument, TotalScalarRule rule)
    {
        var classification = First(receiver.Classification, argument.Classification);
        if (!classification.IsExact)
        { return Failed(operation, classification.Abstention); }
        var safe = And(receiver.SafeCondition, argument.SafeCondition);
        foreach (var fault in rule.Throws)
        { safe = And(safe, Not(fault.Condition)); }
        return new(rule.Value, safe, rule.Classification);
    }

    private GuardedExpression Compose(IOperation operation, ImmutableArray<GuardedExpression> children)
    {
        var classification = First([.. children.Select(value => value.Classification)]);
        if (!classification.IsExact)
        {
            return Failed(operation, classification.Abstention);
        }
        var rule = CSharpOperationSemantics.Apply(_factory, operation, [.. children.Select(value => value.Value)]);
        if (!rule.Classification.IsExact)
        {
            return Failed(operation, rule.Classification.Abstention);
        }
        IrTerm safe = _factory.Boolean(true);
        foreach (var child in children)
        {
            safe = And(safe, child.SafeCondition);
        }
        foreach (var fault in rule.Throws)
        {
            safe = And(safe, Not(fault.Condition));
        }
        return new(rule.Value, safe, rule.Classification);
    }

    private TotalBodyValue ApplyBody(IOperation operation, ImmutableArray<TotalBodyValue> children)
    {
        var block = children[children.Length - 1].Continuation;
        var classification = First([.. children.Select(value => value.Classification)]);
        if (!classification.IsExact)
        {
            return Approximate(operation, block, classification.Abstention);
        }
        if (operation is IBinaryOperation { IsLifted: false, OperatorMethod: null, OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } binary &&
            binary.LeftOperand.Type?.SpecialType is SpecialType.System_Single or SpecialType.System_Double &&
            binary.RightOperand.Type?.SpecialType == binary.LeftOperand.Type?.SpecialType)
        {
            // Built-in floating equality runs no user code; its result is unknown to the IR.
            var compared = _context.Temporary(_factory.BooleanType);
            _builder!.Havoc(block, _context.Site(operation), IrHavocKind.Variables, IrHavocOrigin.Approximation, compared);
            return new(_factory.Variable(compared), block, FrontendSubsetClassification.Exact);
        }
        var rule = CSharpOperationSemantics.Apply(_factory, operation, [.. children.Select(value => value.Value)]);
        if (!rule.Classification.IsExact)
        {
            return Approximate(operation, block, rule.Classification.Abstention);
        }
        return ApplyRule(operation, rule, block);
    }

    internal TotalBodyValue LowerScalarCall(IInvocationOperation invocation, TotalScalarCallModel model, IrBlockId block, int depth)
    {
        // The compiler splices this call's arguments into an enclosing `+`
        // chain, whose operand count then decides its overload and allocation.
        if (model.StringConcatenation && CSharpOperationSemantics.IsConcatenationOperand(invocation.Syntax))
        { return Approximate(invocation, block, FrontendAbstention.UnsupportedOperationKind); }
        var arguments = new IrTerm[model.ParameterCount];
        foreach (var argument in invocation.Arguments)
        {
            var lowered = LowerBodyValue(argument.Value, block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return lowered; }
            arguments[argument.Parameter!.Ordinal] = lowered.Value;
            block = lowered.Continuation;
        }
        RecordScalarEffects(invocation, invocation.TargetMethod, model, [.. arguments], block);
        var rule = model.Apply([.. arguments]);
        if (model.StringConcatenation && rule.Classification.IsExact)
        { return CaptureStringConcatenation(invocation, [.. arguments], block); }
        return rule.Classification.IsExact ? ApplyRule(invocation, rule, block)
            : Approximate(invocation, block, rule.Classification.Abstention);
    }

    // The cached value can be exact even when initializing its runtime cache
    // may allocate. Keep that boundary at the executed call, not on the value.
    internal void RecordScalarEffects(IOperation operation, IMethodSymbol method, TotalScalarCallModel model,
        ImmutableArray<IrTerm> arguments, IrBlockId block)
    {
        if (model.Effects == IrOpaqueCallEffects.None)
        { return; }
        if (ShadowCallSkeleton)
        {
            // Shadow graphs admit only source calls. Keep this external
            // effect boundary scoped for the source effect fixpoint.
            var unknown = _context.Temporary(_factory.BooleanType);
            _builder!.Havoc(block, _context.OpaqueCallSite(operation, model.Effects, CompilerIdentityBridge.CreateSymbolDisplay(method)), IrHavocKind.Variables, IrHavocOrigin.Approximation, unknown);
            return;
        }
        var display = CompilerIdentityBridge.CreateSymbolDisplay(method);
        var member = _factory.GetOrCreateMember(
            CompilerIdentityBridge.InternSymbol(_factory, method), _context.Type(method.ContainingType),
            "opaque-call:" + display, _context.Type(method.ReturnType), true, [.. arguments.Select(value => value.Type)]);
        _builder!.Call(block, _context.OpaqueCallSite(operation, model.Effects, display), null, member, null, [.. arguments]);
    }

    // The runtime rejects an array longer than its maximum element count with
    // OutOfMemoryException, whatever memory is free. .NET Framework's limit for
    // arrays of elements wider than a byte (0x7FEFFFFF) is the smallest. That
    // exception has no modeled kind, so a longer array may fault as an
    // approximation: no proof can rely on its creation, and no concrete
    // refutation is drawn from it.
    private const int SmallestMaximumArrayLength = 0x7FEFFFFF;

    private IrBlockId ExceedsMaximumArrayLength(IOperation operation, IOperation size, IrTerm length, IrBlockId block)
    {
        if (size.ConstantValue is { HasValue: true, Value: <= SmallestMaximumArrayLength })
        { return block; }
        var site = _context.Site(operation);
        var beyond = _builder!.CreateBlock("array:beyond-maximum-length");
        var thrown = _builder.CreateBlock("throw");
        var normal = _builder.CreateBlock("normal");
        _builder.Branch(block, site, _factory.Binary(IrBinaryOperator.GreaterThan, length,
            _factory.Integer(SmallestMaximumArrayLength)), beyond, normal);
        var fails = _context.Temporary(_factory.BooleanType);
        _builder.Havoc(beyond, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, fails);
        _builder.Branch(beyond, site, _factory.Variable(fails), thrown, normal);
        _builder.Throw(thrown, site, IrExceptionKind.Unknown,
            ExceptionTarget?.Invoke(IrExceptionKind.Unknown, site) ?? _exceptionalExit);
        return normal;
    }

    private TotalBodyValue ApplyRule(IOperation operation, TotalScalarRule rule, IrBlockId block)
    {
        foreach (var fault in rule.Throws)
        {
            var thrown = _builder!.CreateBlock("throw");
            var normal = _builder.CreateBlock("normal");
            var site = _context.Site(operation);
            _builder.Branch(block, site, fault.Condition, thrown, normal);
            _builder.Throw(thrown, site, fault.Kind, ExceptionTarget?.Invoke(fault.Kind, site) ?? _exceptionalExit);
            block = normal;
        }
        return Capture(operation, new(rule.Value, block, rule.Classification));
    }

    private TotalBodyValue Lazy(IBinaryOperation operation, IrBlockId block, int depth)
    {
        if (operation.OperatorMethod != null || operation.IsLifted)
        {
            return Approximate(operation, block, FrontendAbstention.UserDefinedOperator);
        }
        var left = LowerBodyValue(operation.LeftOperand, block, depth + 1);
        if (!left.Classification.IsExact)
        {
            return left;
        }
        var rightBlock = _builder!.CreateBlock("lazy:right");
        var skipped = _builder.CreateBlock("lazy:skip");
        var join = _builder.CreateBlock("lazy:join");
        var target = _context.Temporary(_factory.BooleanType);
        var site = _context.Site(operation);
        var isAnd = operation.OperatorKind == BinaryOperatorKind.ConditionalAnd;
        _builder.Branch(left.Continuation, site, left.Value, isAnd ? rightBlock : skipped, isAnd ? skipped : rightBlock);
        _builder.Assign(skipped, site, target, _factory.Boolean(!isAnd));
        _builder.Goto(skipped, site, join);
        var right = LowerBodyValue(operation.RightOperand, rightBlock, depth + 1);
        _builder.Assign(right.Continuation, site, target, right.Value);
        _builder.Goto(right.Continuation, site, join);
        return new(_factory.Variable(target), join, right.Classification);
    }

    private TotalBodyValue Conditional(IOperation operation, IOperation conditionOperation, IOperation whenTrue,
        IOperation whenFalse, IrBlockId block, int depth)
    {
        var condition = LowerBodyValue(conditionOperation, block, depth + 1);
        if (!condition.Classification.IsExact)
        {
            return Approximate(operation, condition.Continuation, condition.Classification.Abstention);
        }
        var trueBlock = _builder!.CreateBlock("conditional:true");
        var falseBlock = _builder.CreateBlock("conditional:false");
        var join = _builder.CreateBlock("conditional:join");
        var target = _context.Temporary(_context.Type(operation.Type));
        var site = _context.Site(operation);
        _builder.Branch(condition.Continuation, site, condition.Value, trueBlock, falseBlock);
        var first = LowerBodyValue(whenTrue, trueBlock, depth + 1);
        _builder.Assign(first.Continuation, site, target, first.Value);
        _builder.Goto(first.Continuation, site, join);
        var second = LowerBodyValue(whenFalse, falseBlock, depth + 1);
        _builder.Assign(second.Continuation, site, target, second.Value);
        _builder.Goto(second.Continuation, site, join);
        return new(_factory.Variable(target), join, First(first.Classification, second.Classification));
    }

    private TotalBodyValue Increment(IIncrementOrDecrementOperation operation, IrVarId target, IrBlockId block)
    {
        var old = Capture(operation, new(_factory.Variable(target), block, FrontendSubsetClassification.Exact));
        var rule = CSharpOperationSemantics.Increment(_factory, operation, old.Value);
        if (!rule.Classification.IsExact)
        { return Approximate(operation, block, rule.Classification.Abstention); }
        var next = ApplyRule(operation, rule, old.Continuation);
        var mutation = _builder!.Assign(next.Continuation, _context.Site(operation), target, next.Value);
        _builder.Write(next.Continuation, mutation.Operation, IrWriteRegion.Local);
        return operation.IsPostfix ? new(old.Value, next.Continuation, next.Classification) : next;
    }

    private TotalBodyValue Compound(ICompoundAssignmentOperation operation, IrVarId target, IrBlockId block, int depth)
    {
        var old = Capture(operation, new(_factory.Variable(target), block, FrontendSubsetClassification.Exact));
        var right = LowerBodyValue(operation.Value, old.Continuation, depth + 1);
        if (!right.Classification.IsExact)
        {
            return Approximate(operation, right.Continuation, right.Classification.Abstention);
        }
        var rule = CSharpOperationSemantics.Compound(_factory, operation, old.Value, right.Value);
        if (!rule.Classification.IsExact)
        { return Approximate(operation, right.Continuation, rule.Classification.Abstention); }
        var result = ApplyRule(operation, rule, right.Continuation);
        var mutation = _builder!.Assign(result.Continuation, _context.Site(operation), target, result.Value);
        _builder.Write(result.Continuation, mutation.Operation, IrWriteRegion.Local);
        return result;
    }

    private TotalBodyValue Capture(IOperation operation, TotalBodyValue value, OperationId? site = null)
    {
        if (value.Value is IrNullTerm)
        { return value; }
        var target = _context.Temporary(value.Value.Type);
        _builder!.Assign(value.Continuation, site ?? _context.Site(operation), target, value.Value);
        return new(_factory.Variable(target), value.Continuation, value.Classification, value.ConcatenationOperands);
    }

    private bool TryLeaf(IOperation operation, TotalParameterState state, out IrTerm? value)
    {
        // Constant division/remainder is emitted as a value. Re-evaluating it
        // as a runtime operator can invent faults: MinValue % -1 folds to zero,
        // whereas the same remainder with runtime operands can overflow.
        if (operation is IBinaryOperation { OperatorKind: BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder } &&
            operation.ConstantValue.HasValue && CSharpOperationSemantics.IsScalar(operation.Type))
        {
            value = CSharpOperationSemantics.Literal(_factory, operation.Type, operation.ConstantValue.Value);
            return true;
        }
        // Opaque constants have no literal; LowerBodyValue approximates them.
        value = operation.ConstantValue.HasValue && CSharpOperationSemantics.IsOpaqueDomain(operation.Type) ? null : operation switch
        {
            ILiteralOperation literal => CSharpOperationSemantics.Literal(_factory, literal.Type!, literal.ConstantValue.Value),
            IDefaultValueOperation when !CSharpOperationSemantics.IsOpaqueDomain(operation.Type) =>
                CSharpOperationSemantics.Literal(_factory, operation.Type!, null),
            IParameterReferenceOperation parameter => _factory.Variable(_context.Variable(parameter.Parameter, state)),
            ILocalReferenceOperation local when local.Local.HasConstantValue => CSharpOperationSemantics.Literal(_factory, local.Type!, local.Local.ConstantValue),
            ILocalReferenceOperation local when local.Local.RefKind == RefKind.None => _factory.Variable(_context.Variable(local.Local)),
            IFlowCaptureReferenceOperation capture => _factory.Variable(_context.Capture(capture.Id, capture.Type)),
            IFieldReferenceOperation field when field.Field.HasConstantValue => CSharpOperationSemantics.Literal(_factory, field.Type!, field.Field.ConstantValue),
            IInstanceReferenceOperation self when IsImplicitThis(self) => _context.ThisValue(state),
            _ => null
        };
        return value != null;
    }

    private bool TryStorage(IOperation operation, out IrVarId target)
    {
        target = operation switch
        {
            IParameterReferenceOperation parameter when _context.OwnsParameter(parameter.Parameter) => _context.Variable(parameter.Parameter),
            ILocalReferenceOperation local when local.Local.RefKind == RefKind.None => _context.Variable(local.Local),
            _ => default
        };
        return !target.IsDefault;
    }

    private FrontendAbstention Reject(IOperation operation, int depth)
    {
        if (depth >= 256)
        { return FrontendAbstention.ExpressionDepthLimit; }
        if (operation is IParameterReferenceOperation parameter && !_context.OwnsParameter(parameter.Parameter))
        { return FrontendAbstention.UnsupportedOperationKind; }
        if (CSharpOperationSemantics.IsIntercepted(operation, _context.Compilation))
        { return FrontendAbstention.UnsupportedInvocationShape; }
        if (!CSharpOperationSemantics.Operations.TryGetValue(operation.Kind, out var decision))
        { return FrontendAbstention.UnknownOperationKind; }
        var admission = decision.Admission;
        if (admission == TotalOperationAdmission.Invalid)
        { return FrontendAbstention.InvalidOperation; }
        if (admission == TotalOperationAdmission.Incomplete)
        {
            // `this` is a value only once the receiver is modeled.
            if ((operation is not IPropertyReferenceOperation property || !CSharpOperationSemantics.IsLength(property) &&
                    !(property is { Instance: { } owner, Arguments.Length: 0 } &&
                        CSharpOperationSemantics.GetterField(property.Property, CSharpOperationSemantics.IsBaseAccess(owner)) is { } backing &&
                        FieldMember(backing) != null)) &&
                (operation is not IInstanceReferenceOperation self || !IsImplicitThis(self) || _context.ThisValue() == null))
            { return FrontendAbstention.UnsupportedOperationKind; }
        }
        if (operation.ConstantValue is { HasValue: true, Value: string text } && !Utf16WellFormedness.IsWellFormed(text))
        { return FrontendAbstention.UnsupportedOperationKind; }
        // Roslyn types a foreach's iteration-variable assignment by nothing;
        // its target's type is its value's.
        var type = operation is ISimpleAssignmentOperation { Type: null } untyped ? untyped.Target.Type : operation.Type;
        return CSharpOperationSemantics.IsValueDomain(type) ||
            operation is ILiteralOperation { ConstantValue.HasValue: true, ConstantValue.Value: null }
            ? FrontendAbstention.None : FrontendAbstention.UnsupportedType;
    }

    private GuardedExpression Failed(IOperation operation, FrontendAbstention reason)
    {
        return new(Default(operation.Type), _factory.Boolean(false), FrontendSubsetClassification.Abstain(reason));
    }
    private GuardedExpression Exact(IrTerm value)
    {
        return new(value, _factory.Boolean(true), FrontendSubsetClassification.Exact);
    }
    private TotalBodyValue Approximate(IOperation operation, IrBlockId block, FrontendAbstention reason)
    {
        var target = _context.Temporary(_context.Type(operation.Type));
        _builder!.Havoc(block, _context.Site(operation), IrHavocKind.Variables, IrHavocOrigin.Approximation, target);
        return new(_factory.Variable(target), block, FrontendSubsetClassification.Abstain(reason));
    }
    private IrTerm Default(ITypeSymbol? type)
    {
        return CSharpOperationSemantics.IsValueDomain(type) && !CSharpOperationSemantics.IsOpaqueDomain(type)
            ? CSharpOperationSemantics.Literal(_factory, type, null) : _factory.Boolean(false);
    }
    private IrTerm And(IrTerm first, IrTerm second)
    {
        return _factory.Binary(IrBinaryOperator.AndAlso, first, second);
    }
    private IrTerm Or(IrTerm first, IrTerm second)
    {
        return _factory.Binary(IrBinaryOperator.OrElse, first, second);
    }
    private IrTerm Not(IrTerm value)
    {
        return _factory.Unary(IrUnaryOperator.Not, value);
    }
    private static FrontendSubsetClassification First(params FrontendSubsetClassification[] classifications)
    {
        return classifications.FirstOrDefault(value => !value.IsExact).Decision == FrontendSubsetDecision.ClosedAbstention
            ? classifications.First(value => !value.IsExact) : FrontendSubsetClassification.Exact;
    }
}
