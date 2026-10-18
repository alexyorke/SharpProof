namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalExpressionLowerer
{
    internal TotalBodyValue LowerReceiverValue(IOperation operation, IrBlockId block, int depth)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (depth < 256 && operation is IConversionOperation { OperatorMethod: null } conversion &&
            (conversion.Conversion.IsIdentity || conversion.Conversion.IsReference && TotalLoweringContext.IsArrayInterface(conversion.Type)) &&
            _context.ConcreteArray(operation) != null)
        { return LowerReceiverValue(conversion.Operand, block, depth + 1); }
        return LowerBodyValue(operation, block, depth);
    }

    private GuardedExpression? ArrayCollectionClause(IPropertyReferenceOperation property, TotalParameterState state, int depth)
    {
        property = RepositoryParameterGuard.NotNull(property, nameof(property));
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
        property = RepositoryParameterGuard.NotNull(property, nameof(property));
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
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
        if (stored is { } value)
        {
            _builder!.ElementStore(block, _context.Site(operation), array.Value, index.Value, value.Value);
            return new(value.Value, block, FrontendSubsetClassification.Exact);
        }
        TotalBodyValue read;
        if (ApproximateElementReads)
        {
            var unknown = _context.Temporary(_context.Type(property.Type));
            _builder!.Havoc(block, _context.Site(operation), IrHavocKind.Variables, IrHavocOrigin.Approximation, unknown);
            read = new(_factory.Variable(unknown), block, FrontendSubsetClassification.Exact);
        }
        else
        { read = new(_factory.SequenceAccess(array.Value, index.Value), block, FrontendSubsetClassification.Exact); }
        if (operation is IIncrementOrDecrementOperation or ICompoundAssignmentOperation)
        {
            // A compound setter reads before its RHS. Capture that value even
            // when the RHS writes the same cell or rebinds the receiver/index.
            read = Pin(operation, read);
            TotalBodyValue next;
            if (operation is IIncrementOrDecrementOperation increment)
            {
                next = ApplyClassifiedRule(operation,
                    CSharpOperationSemantics.Increment(_factory, increment, read.Value), read.Continuation);
            }
            else
            {
                var compound = (ICompoundAssignmentOperation)operation;
                var right = LowerBodyValue(compound.Value, read.Continuation, depth + 1);
                if (!right.Classification.IsExact)
                { return Approximate(operation, right.Continuation, right.Classification.Abstention); }
                next = ApplyClassifiedRule(operation,
                    CSharpOperationSemantics.Compound(_factory, compound, read.Value, right.Value, _context.Compilation?.Options.CheckOverflow), right.Continuation);
            }
            if (next.Classification.IsExact)
            {
                // Both accessor calls use the captured receiver/index. Scalar
                // SZ-array length and bounds cannot change between the calls.
                _builder!.ElementStore(next.Continuation, _context.Site(operation), array.Value, index.Value, next.Value);
                if (operation is IIncrementOrDecrementOperation { IsPostfix: true })
                { return new(read.Value, next.Continuation, next.Classification); }
            }
            return next;
        }
        return PinElementReads ? Pin(operation, read) : read;
    }

    private IrBlockId ArrayInterfaceBoundsEffects(IPropertyReferenceOperation property, IOperation operation,
        IrTerm index, IrTerm outside, IrBlockId block)
    {
        property = RepositoryParameterGuard.NotNull(property, nameof(property));
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        index = RepositoryParameterGuard.NotNull(index, nameof(index));
        outside = RepositoryParameterGuard.NotNull(outside, nameof(outside));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var site = _context.Site(operation);
        var invalid = _builder!.CreateBlock("interface-array:invalid-index");
        var continuation = _builder.CreateBlock("interface-array:checked-index");
        _builder.Branch(block, site, outside, invalid, continuation);
        // Resource lookup can invoke user CultureInfo callbacks. Those callbacks
        // may mutate reachable state or throw before the intended bounds fault.
        var effects = IrOpaqueCallSite.WithCapabilities(IrOpaqueCallEffects.All | IrOpaqueCallEffects.ReadsAmbient, int.MaxValue);
        if (ShadowCallSkeleton)
        {
            var marker = _context.Temporary(_factory.BooleanType);
            _builder.Havoc(invalid, _context.OpaqueCallSite(operation, effects, "interface-array:bounds"),
                IrHavocKind.VariablesAndMemory, IrHavocOrigin.Approximation, marker);
        }
        else
        {
            var member = OpaqueCallMember(property.Property.GetMethod!, [index], _context.Type(property.Type), out var display);
            _builder.Call(invalid, _context.OpaqueCallSite(operation, effects, display + ":bounds"), null, member, null, index);
        }
        var mayThrow = _context.Temporary(_factory.BooleanType);
        _builder.Havoc(invalid, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, mayThrow);
        invalid = ApplyRule(operation, new TotalScalarRule(_factory.Boolean(true),
            [new(IrExceptionKind.Unknown, _factory.Variable(mayThrow))], FrontendSubsetClassification.Exact), invalid).Continuation;
        // Cache initialization is possible, not a guaranteed observable write.
        // An approximation guards it so replay cannot invent an exact impurity.
        var mayWrite = _context.Temporary(_factory.BooleanType);
        _builder.Havoc(invalid, _context.OpaqueCallSite(operation, IrOpaqueCallEffects.Writes, "interface-array:cache"),
            IrHavocKind.Variables, IrHavocOrigin.Approximation, mayWrite);
        var cacheWrite = _builder.CreateBlock("interface-array:cache-write");
        var boundsThrow = _builder.CreateBlock("interface-array:bounds-throw");
        _builder.Branch(invalid, site, _factory.Variable(mayWrite), cacheWrite, boundsThrow);
        _builder.Write(cacheWrite, site, IrWriteRegion.Static);
        _builder.Goto(cacheWrite, site, boundsThrow);
        _builder.Throw(boundsThrow, site, IrExceptionKind.ArgumentOutOfRange,
            ExceptionTarget?.Invoke(IrExceptionKind.ArgumentOutOfRange, site) ?? _exceptionalExit);
        return continuation;
    }

}
