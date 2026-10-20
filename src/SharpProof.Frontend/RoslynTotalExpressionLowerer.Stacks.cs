namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalExpressionLowerer
{
    private readonly ImmutableDictionary<(string Name, IrTypeId Value), IrMemberId>.Builder _stackFields = ImmutableDictionary.CreateBuilder<(string Name, IrTypeId Value), IrMemberId>();

    private IrMemberId StackField(string name, IrTypeId value)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        RepositoryParameterGuard.ThrowIfInvalid(value.IsDefault, nameof(value));
        var storage = (Name: name, Value: value);
        if (_stackFields.TryGetValue(storage, out var field))
        { return field; }
        var identity = _factory.InternExternalIdentity(("stack", name, value), EqualityComparer<(string, string, IrTypeId)>.Default);
        field = _factory.GetOrCreateMemberSnapshot(identity, _factory.ObjectType, IrFieldSites.Prefix + "stack:" + name, value,
            false, name == "count" ? [] : [_factory.IntegerType]);
        _stackFields.AddUnique(storage, field);
        return field;
    }

    private IrTerm StackCount(IrTerm owner)
    {
        owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
        return _factory.Binary(IrBinaryOperator.BitwiseAnd,
            _factory.PureOpaque(StackField("count", _factory.IntegerType), owner), _factory.Integer(int.MaxValue));
    }

    private GuardedExpression? StackClause(IPropertyReferenceOperation property, TotalParameterState state, int depth)
    {
        property = RepositoryParameterGuard.NotNull(property, nameof(property));
        if (property.Instance is not { } instance || property.Property.GetMethod is not { } getter ||
            !CSharpOperationSemantics.IsStackStateMember(getter))
        { return null; }
        var receiver = LowerClause(instance, state, depth + 1);
        if (!receiver.Classification.IsExact)
        { return Failed(property, receiver.Classification.Abstention); }
        var safe = And(receiver.SafeCondition, Not(_factory.Binary(IrBinaryOperator.Equal, receiver.Value, _factory.Null(receiver.Value.Type))));
        return new(StackCount(AsObject(receiver.Value)), safe, FrontendSubsetClassification.Exact);
    }

    private TotalBodyValue? StackBody(IOperation operation, IrBlockId block, int depth)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (ShadowCallSkeleton)
        { return null; }
        if (operation is IObjectCreationOperation { Constructor: { } constructor } creation &&
            CSharpOperationSemantics.IsStackStateMember(constructor))
        {
            var valueBuilder = ImmutableArray.CreateBuilder<IrTerm>(creation.Arguments.Length);
            foreach (var argument in creation.Arguments)
            {
                var value = LowerBodyValue(argument.Value, block, depth + 1);
                if (!value.Classification.IsExact)
                { return Approximate(operation, value.Continuation, value.Classification.Abstention); }
                value = Pin(operation, value);
                valueBuilder.Add(value.Value);
                block = value.Continuation;
            }
            var values = valueBuilder.MoveToImmutable();
            if (values.Length != 0)
            {
                block = StackExceptionalPath(operation, constructor, values,
                    _factory.Binary(IrBinaryOperator.LessThan, values[0], _factory.Integer(0)), block);
            }
            // Runtime initialization and backing storage may allocate or fail;
            // successful copying never invokes element callbacks.
            block = ListRuntimeEffects(operation, constructor, values, _factory.Boolean(false), block, initialization: true);
            return AllocateValue(operation, block);
        }
        var invocation = operation as IInvocationOperation;
        var property = operation as IPropertyReferenceOperation;
        var method = invocation?.TargetMethod ?? property?.Property.GetMethod;
        var instance = invocation?.Instance ?? property?.Instance;
        if (method == null || instance == null || !CSharpOperationSemantics.IsStackStateMember(method,
            _context.KnownValueType(method.ContainingType.TypeArguments.FirstOrDefault())))
        { return null; }
        // Concrete nonvirtual framework members always use base Stack storage,
        // including incoming, aliased, rebound and derived receivers.
        var receiver = LowerBodyValue(instance, block, depth + 1);
        if (!receiver.Classification.IsExact)
        { return Approximate(operation, receiver.Continuation, receiver.Classification.Abstention); }
        receiver = Pin(operation, receiver);
        block = receiver.Continuation;
        IrTerm? item = null;
        if (invocation is { Arguments.Length: 1 })
        {
            var value = LowerBodyValue(invocation.Arguments[0].Value, block, depth + 1);
            if (!value.Classification.IsExact)
            { return Approximate(operation, value.Continuation, value.Classification.Abstention); }
            value = Pin(operation, value);
            item = value.Value;
            block = value.Continuation;
        }
        block = CheckReceiver(operation, receiver.Value, block);
        var owner = AsObject(receiver.Value);
        var count = StackCount(owner);
        var name = method.AssociatedSymbol?.Name ?? method.Name;
        if (name == "Count")
        { return new(count, block, FrontendSubsetClassification.Exact); }
        var previous = Pin(operation, new(count, block, FrontendSubsetClassification.Exact));
        block = previous.Continuation;
        var countField = StackField("count", _factory.IntegerType);
        var contents = StackField("value", _context.Type(method.ContainingType.TypeArguments[0]));
        if (name == "Push")
        {
            block = ListRuntimeEffects(operation, method, [owner, item!],
                _factory.Binary(IrBinaryOperator.Equal, previous.Value, _factory.Integer(int.MaxValue)), block);
            _builder!.FieldStore(block, _context.Site(operation), IrWriteRegion.Field, owner, contents, item!, previous.Value);
            _builder.FieldStore(block, _context.Site(operation), IrWriteRegion.Field, owner, countField,
                _factory.Binary(IrBinaryOperator.Add, previous.Value, _factory.Integer(1)));
            return new(_factory.Boolean(false), block, FrontendSubsetClassification.Exact);
        }
        block = StackExceptionalPath(operation, method, [owner],
            _factory.Binary(IrBinaryOperator.Equal, previous.Value, _factory.Integer(0)), block);
        var index = _factory.Binary(IrBinaryOperator.Subtract, previous.Value, _factory.Integer(1));
        var popped = Pin(operation, new(_factory.PureOpaque(contents, owner, index), block, FrontendSubsetClassification.Exact));
        _builder!.FieldStore(popped.Continuation, _context.Site(operation), IrWriteRegion.Field, owner, countField, index);
        return popped;
    }

    private IrBlockId StackExceptionalPath(IOperation operation, IMethodSymbol method, ImmutableArray<IrTerm> values, IrTerm invalid, IrBlockId block)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        RepositoryParameterGuard.RequireInitialized(values.IsDefault, nameof(values), "Array must be initialized.");
        invalid = RepositoryParameterGuard.NotNull(invalid, nameof(invalid));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var failure = _builder!.CreateBlock("stack:invalid");
        var normal = _builder.CreateBlock("stack:normal");
        var site = _context.Site(operation);
        _builder.Branch(block, site, invalid, failure, normal);
        // Resource/culture callbacks and ancillary faults remain opaque on invalid
        // capacity or an empty Pop; do not assert a stronger exception contract.
        var effects = EmitOpaqueCall(operation, method, values, failure, null,
            OpaqueEffects?.Invoke(method) ?? IrOpaqueCallEffects.All).Continuation;
        _builder.Throw(effects, site, IrExceptionKind.Unknown,
            ExceptionTarget?.Invoke(IrExceptionKind.Unknown, site) ?? _exceptionalExit);
        return normal;
    }
}
