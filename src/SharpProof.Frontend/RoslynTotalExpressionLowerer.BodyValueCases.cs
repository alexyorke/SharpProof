namespace SharpProof.Frontend;

// Cases of LowerBodyValue that are large enough to read on their own. Each is
// reached from one condition there, in the same order as before.
internal sealed partial class RoslynTotalExpressionLowerer
{
    // new T[n], with or without constant initializer values.
    private TotalBodyValue LowerArrayCreation(IOperation operation, IArrayCreationOperation arrayCreation, IrBlockId block, int depth)
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

    // An array initializer whose elements are evaluated and stored one by one.
    private TotalBodyValue LowerLocalArrayInitializer(IOperation operation, IArrayCreationOperation localArrayCreation, IrBlockId block, int depth)
    {
        var dimension = LowerBodyValue(localArrayCreation.DimensionSizes[0], block, depth + 1);
        if (!dimension.Classification.IsExact)
        { return Approximate(operation, dimension.Continuation, dimension.Classification.Abstention); }
        var rule = new TotalScalarRule(dimension.Value,
            [new(IrExceptionKind.Overflow, _factory.Binary(IrBinaryOperator.LessThan, dimension.Value, _factory.Integer(0)))],
            FrontendSubsetClassification.Exact);
        block = ApplyRule(operation, rule, dimension.Continuation).Continuation;
        block = ExceedsMaximumArrayLength(operation, localArrayCreation.DimensionSizes[0], dimension.Value, block);
        var type = _context.Type(operation.Type);
        var target = _context.Temporary(type);
        var array = _factory.Variable(target);
        var elements = localArrayCreation.Initializer!.ElementValues;
        // C# allocates the default array before evaluating its elements.
        // A local is read at its own store, so later assignments cannot
        // change an earlier initializer value.
        _builder!.Allocate(block, _context.Site(operation), type, target, _factory.Integer(elements.Length));
        for (var index = 0; index < elements.Length; index++)
        {
            var initializerElement = elements[index];
            IrTerm value;
            if (initializerElement.ConstantValue.HasValue)
            {
                value = CSharpOperationSemantics.Literal(_factory, initializerElement.Type,
                    initializerElement.ConstantValue.Value);
            }
            else
            {
                var lowered = LowerBodyValue(initializerElement, block, depth + 1);
                block = lowered.Continuation;
                value = lowered.Value;
            }
            _builder.ElementStore(block, _context.FreshWriteSite(initializerElement), array,
                _factory.Integer(index), value);
        }
        return new(array, block, FrontendSubsetClassification.Exact);
    }

    // new string(character, count).
    private TotalBodyValue LowerRepeatedCharacterString(IOperation operation, IObjectCreationOperation stringCreation, IrBlockId block, int depth)
    {
        var arguments = new IrTerm[2];
        foreach (var argument in stringCreation.Arguments.OrderBy(argument => argument.Syntax.SpanStart))
        {
            var lowered = LowerBodyValue(argument.Value, block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return Approximate(operation, lowered.Continuation, lowered.Classification.Abstention); }
            var pinned = Pin(argument.Value, lowered);
            arguments[argument.Parameter!.Ordinal] = pinned.Value;
            block = pinned.Continuation;
        }
        // These faults must reach a surrounding handler, so they are explicit
        // branches rather than the allocation's own length check.
        var rule = new TotalScalarRule(arguments[1],
            [new(IrExceptionKind.ArgumentOutOfRange, _factory.Binary(IrBinaryOperator.LessThan, arguments[1], _factory.Integer(0)))],
            FrontendSubsetClassification.Exact);
        block = ApplyRule(operation, rule, block).Continuation;
        var count = stringCreation.Arguments.Single(argument => argument.Parameter!.Ordinal == 1).Value;
        if (count.ConstantValue is not { HasValue: true, Value: <= MaximumStringLength })
        {
            block = MayFault(operation, _factory.Binary(IrBinaryOperator.GreaterThan, arguments[1],
                _factory.Integer(MaximumStringLength)), "string:beyond-maximum-length", block);
        }
        var target = _context.Temporary(_factory.StringType);
        var character = stringCreation.Arguments.Single(argument => argument.Parameter!.Ordinal == 0).Value;
        var fill = _factory.Integer(_context.Type(character.Type), (char)character.ConstantValue.Value!);
        _builder!.Allocate(block, _context.Site(operation), _factory.StringType, target, arguments[1], [fill]);
        return new(_factory.Variable(target), block, FrontendSubsetClassification.Exact);
    }

    // Construction of a core exception type.
    private TotalBodyValue LowerExceptionCreation(IOperation operation, IObjectCreationOperation exceptionCreation, IrBlockId block, int depth)
    {
        var argumentValues = ImmutableArray.CreateBuilder<IrTerm>(exceptionCreation.Arguments.Length);
        argumentValues.Count = exceptionCreation.Arguments.Length;
        foreach (var argument in exceptionCreation.Arguments.OrderBy(argument => argument.Syntax.SpanStart))
        {
            var lowered = LowerBodyValue(argument.Value, block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return Approximate(operation, lowered.Continuation, lowered.Classification.Abstention); }
            lowered = Pin(operation, lowered);
            argumentValues[argument.Parameter!.Ordinal] = lowered.Value;
            block = lowered.Continuation;
        }
        var values = argumentValues.MoveToImmutable();
        // newobj allocates before the constructor executes. A callback
        // failure must not erase that already observable allocation.
        var allocated = AllocateValue(operation, block);
        block = ExceptionConstructorRuntimeEffects(exceptionCreation, values, allocated.Continuation);
        return new(allocated.Value, block, allocated.Classification);
    }

    // An explicit delegate creation from a method group.
    private TotalBodyValue LowerExplicitDelegateCreation(IOperation operation, IDelegateCreationOperation delegateCreation, IrBlockId block, int depth)
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
            if (DelegateBinding?.Invoke(((IMethodReferenceOperation)delegateCreation.Target).Method) == true)
            { return AllocateCapturedDelegate(operation, receiver.Value, block); }
        }
        return erased
            ? new(_factory.Null(_context.Type(operation.Type)), block, FrontendSubsetClassification.Exact)
            : AllocateValue(operation, block);
    }

    // An implicit boxing conversion passed as an argument to an opaque call.
    private TotalBodyValue LowerOpaqueBoxing(IOperation operation, IConversionOperation opaqueBoxing, IrBlockId block, int depth)
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

    // A type test whose result is unknown.
    private TotalBodyValue LowerOpaqueTypeTest(IOperation operation, IOperation typeTestOperand, IrBlockId block, int depth)
    {
        var tested = LowerBodyValue(typeTestOperand, block, depth + 1);
        if (!tested.Classification.IsExact)
        { return Approximate(operation, tested.Continuation, tested.Classification.Abstention); }
        var matched = _context.Temporary(_factory.BooleanType);
        var site = _context.Site(operation);
        if (CSharpOperationSemantics.OpaqueTypeTestMayAllocate(operation, _context.Compilation, Spend))
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
}
