namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalExpressionLowerer
{
    internal TotalBodyValue LowerReceiverValue(IOperation operation, IrBlockId block, int depth)
    {
        if (depth < 256 && operation is IConversionOperation { OperatorMethod: null, IsTryCast: false } conversion &&
            (conversion.Conversion.IsIdentity || conversion.Conversion.IsReference && TotalLoweringContext.IsArrayInterface(conversion.Type)) &&
            _context.ConcreteArray(operation) != null)
        { return LowerReceiverValue(conversion.Operand, block, depth + 1); }
        return LowerBodyValue(operation, block, depth);
    }

    private GuardedExpression? ArrayCollectionClause(IPropertyReferenceOperation property, TotalParameterState state, int depth)
    {
        if (_context.ArrayCollectionReceiver(property) is not { } operand)
        { return null; }
        var array = LowerClause(operand, state, depth + 1);
        if (!array.Classification.IsExact)
        { return Failed(property, array.Classification.Abstention); }
        if (property.Arguments.IsEmpty)
        {
            return new(_factory.Length(array.Value), And(array.SafeCondition,
                Not(_factory.Binary(IrBinaryOperator.Equal, array.Value, _factory.Null(array.Value.Type)))),
                FrontendSubsetClassification.Exact);
        }
        var index = LowerClause(property.Arguments[0].Value, state, depth + 1);
        if (!index.Classification.IsExact)
        { return Failed(property, index.Classification.Abstention); }
        return Compose(property, array, index,
            new TotalScalarRule(_factory.SequenceAccess(array.Value, index.Value),
                CSharpOperationSemantics.ArrayCollectionGuard(_factory, array.Value, index.Value).Throws,
                FrontendSubsetClassification.Exact));
    }

    private TotalBodyValue? ArrayCollectionBody(IPropertyReferenceOperation property, IOperation operation,
        IOperation? assigned, IrBlockId block, int depth)
    {
        if (_context.ArrayCollectionReceiver(property) is not { } operand)
        { return null; }
        var array = LowerReceiverValue(operand, block, depth + 1);
        if (!array.Classification.IsExact)
        { return Approximate(operation, array.Continuation, array.Classification.Abstention); }
        block = array.Continuation;
        if (property.Arguments.IsEmpty)
        {
            return ApplyRule(operation, new TotalScalarRule(_factory.Length(array.Value),
                [new(IrExceptionKind.NullReference, _factory.Binary(IrBinaryOperator.Equal, array.Value, _factory.Null(array.Value.Type)))],
                FrontendSubsetClassification.Exact), block);
        }
        var index = LowerBodyValue(property.Arguments[0].Value, block, depth + 1);
        if (!index.Classification.IsExact)
        { return Approximate(operation, index.Continuation, index.Classification.Abstention); }
        block = index.Continuation;
        TotalBodyValue? stored = null;
        if (assigned != null)
        {
            stored = LowerBodyValue(assigned, block, depth + 1);
            if (!stored.Value.Classification.IsExact)
            { return Approximate(operation, stored.Value.Continuation, stored.Value.Classification.Abstention); }
            block = stored.Value.Continuation;
        }
        // Like a method call, the setter evaluates its value before dereferencing
        // the receiver. Bounds faults follow the null check.
        var guard = CSharpOperationSemantics.ArrayCollectionGuard(_factory, array.Value, index.Value);
        block = ApplyRule(operation, new TotalScalarRule(_factory.Boolean(true), [guard.Throws[0]], guard.Classification), block).Continuation;
        block = ArrayInterfaceBoundsEffects(property, operation, index.Value, guard.Throws[1].Condition, block);
        block = ApplyRule(operation, guard, block).Continuation;
        if (stored is { } value)
        {
            _builder!.ElementStore(block, _context.Site(operation), array.Value, index.Value, value.Value);
            return new(value.Value, block, FrontendSubsetClassification.Exact);
        }
        if (ApproximateElementReads)
        {
            var unknown = _context.Temporary(_context.Type(property.Type));
            _builder!.Havoc(block, _context.Site(operation), IrHavocKind.Variables, IrHavocOrigin.Approximation, unknown);
            return new(_factory.Variable(unknown), block, FrontendSubsetClassification.Exact);
        }
        var read = new TotalBodyValue(_factory.SequenceAccess(array.Value, index.Value), block, FrontendSubsetClassification.Exact);
        return PinElementReads ? Pin(operation, read) : read;
    }

    private IrBlockId ArrayInterfaceBoundsEffects(IPropertyReferenceOperation property, IOperation operation,
        IrTerm index, IrTerm outside, IrBlockId block)
    {
        var site = _context.Site(operation);
        var invalid = _builder!.CreateBlock("interface-array:invalid-index");
        var continuation = _builder.CreateBlock("interface-array:checked-index");
        _builder.Branch(block, site, outside, invalid, continuation);
        // ThrowHelper may allocate and use ambient resources. It receives no
        // array or element references, so it cannot mutate caller array cells.
        // Its framework cache write is represented separately as ambient state.
        var effects = IrOpaqueCallSite.WithCapabilities((IrOpaqueCallEffects.All | IrOpaqueCallEffects.ReadsAmbient) & ~IrOpaqueCallEffects.Writes, int.MaxValue);
        if (ShadowCallSkeleton)
        {
            var marker = _context.Temporary(_factory.BooleanType);
            _builder.Havoc(invalid, _context.OpaqueCallSite(operation, effects, "interface-array:bounds"),
                IrHavocKind.Variables, IrHavocOrigin.Approximation, marker);
        }
        else
        {
            var member = OpaqueCallMember(property.Property.GetMethod!, [index], _context.Type(property.Type), out var display);
            _builder.Call(invalid, _context.OpaqueCallSite(operation, effects, display + ":bounds"), null, member, null, index);
        }
        // Cache initialization is possible, not a guaranteed observable write.
        // An approximation guards it so replay cannot invent an exact impurity.
        var mayWrite = _context.Temporary(_factory.BooleanType);
        _builder.Havoc(invalid, _context.OpaqueCallSite(operation, IrOpaqueCallEffects.Writes, "interface-array:cache"),
            IrHavocKind.Variables, IrHavocOrigin.Approximation, mayWrite);
        var cacheWrite = _builder.CreateBlock("interface-array:cache-write");
        _builder.Branch(invalid, site, _factory.Variable(mayWrite), cacheWrite, continuation);
        _builder.Write(cacheWrite, site, IrWriteRegion.Static);
        _builder.Goto(cacheWrite, site, continuation);
        return continuation;
    }

}
