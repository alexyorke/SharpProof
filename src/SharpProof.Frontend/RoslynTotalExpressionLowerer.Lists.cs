namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalExpressionLowerer
{
    private readonly Dictionary<(string Name, IrTypeId Value), IrMemberId> _listFields = [];

    private IrMemberId ListField(string name, IrTypeId value)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        RepositoryParameterGuard.ThrowIfInvalid(value.IsDefault, nameof(value));
        var storage = (Name: name, Value: value);
        if (_listFields.TryGetValue(storage, out var field))
        { return field; }
        var identity = _factory.InternExternalIdentity(("list", name, value), EqualityComparer<(string, string, IrTypeId)>.Default);
        field = _factory.GetOrCreateMember(identity, _factory.ObjectType, IrFieldSites.Prefix + "list:" + name, value,
            false, name == "count" ? [] : [_factory.IntegerType]);
        _listFields.Add(storage, field);
        return field;
    }

    private IrTerm ListCount(IrTerm owner)
    {
        owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
        return _factory.Binary(IrBinaryOperator.BitwiseAnd,
            _factory.PureOpaque(ListField("count", _factory.IntegerType), owner), _factory.Integer(int.MaxValue));
    }

    private GuardedExpression? ListClause(IPropertyReferenceOperation property, TotalParameterState state, int depth)
    {
        property = RepositoryParameterGuard.NotNull(property, nameof(property));
        if (property.Instance is not { } instance || property.Property.GetMethod is not { } getter ||
            !CSharpOperationSemantics.IsConcreteListStateMember(getter))
        { return null; }
        var receiver = LowerClause(instance, state, depth + 1);
        if (!receiver.Classification.IsExact)
        { return Failed(property, receiver.Classification.Abstention); }
        var owner = AsObject(receiver.Value);
        var safe = And(receiver.SafeCondition, Not(_factory.Binary(IrBinaryOperator.Equal, receiver.Value, _factory.Null(receiver.Value.Type))));
        var count = ListCount(owner);
        if (property.Arguments.IsEmpty)
        { return new(count, safe, FrontendSubsetClassification.Exact); }
        var index = LowerClause(property.Arguments[0].Value, state, depth + 1);
        if (!index.Classification.IsExact)
        { return Failed(property, index.Classification.Abstention); }
        safe = And(safe, And(index.SafeCondition, And(
            _factory.Binary(IrBinaryOperator.GreaterThanOrEqual, index.Value, _factory.Integer(0)),
            _factory.Binary(IrBinaryOperator.LessThan, index.Value, count))));
        return new(_factory.PureOpaque(ListField("value", _context.Type(property.Type)), owner, index.Value), safe, FrontendSubsetClassification.Exact);
    }

    private TotalBodyValue? ListBody(IOperation operation, IrBlockId block, int depth)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (ShadowCallSkeleton)
        { return null; }
        if (CSharpOperationSemantics.IsEmptyListCreation(operation))
        {
            // The framework initializer allocates its empty array and writes its
            // own cache, without calling element constructors or callbacks.
            var constructor = ((IObjectCreationOperation)operation).Constructor!;
            block = ListRuntimeEffects(operation, constructor, [], _factory.Boolean(false), block, initialization: true);
            return AllocateValue(operation, block);
        }
        var assigned = (operation as ISimpleAssignmentOperation)?.Value;
        var access = operation is ISimpleAssignmentOperation assignment ? assignment.Target : operation;
        var invocation = access as IInvocationOperation;
        var property = access as IPropertyReferenceOperation;
        var method = invocation?.TargetMethod ?? (assigned == null ? property?.Property.GetMethod : property?.Property.SetMethod);
        var instance = invocation?.Instance ?? property?.Instance;
        if (method == null || instance == null || !CSharpOperationSemantics.IsListStateMember(method) ||
            !CSharpOperationSemantics.IsConcreteListStateMember(method) && !_context.IsFreshList(instance))
        { return null; }
        var receiver = LowerBodyValue(instance, block, depth + 1);
        if (!receiver.Classification.IsExact)
        { return Approximate(operation, receiver.Continuation, receiver.Classification.Abstention); }
        receiver = Pin(operation, receiver);
        block = receiver.Continuation;
        var arguments = invocation?.Arguments ?? property!.Arguments;
        var values = new IrTerm[arguments.Length + (assigned == null ? 0 : 1)];
        foreach (var argument in arguments.OrderBy(argument => argument is null ? throw new ArgumentNullException(nameof(argument)) : argument.Syntax.SpanStart))
        {
            var value = LowerBodyValue(argument.Value, block, depth + 1);
            if (!value.Classification.IsExact)
            { return Approximate(operation, value.Continuation, value.Classification.Abstention); }
            value = Pin(operation, value);
            values[argument.Parameter!.Ordinal] = value.Value;
            block = value.Continuation;
        }
        if (assigned != null)
        {
            var value = LowerBodyValue(assigned, block, depth + 1);
            if (!value.Classification.IsExact)
            { return Approximate(operation, value.Continuation, value.Classification.Abstention); }
            values[values.Length - 1] = value.Value;
            block = value.Continuation;
        }
        block = CheckReceiver(operation, receiver.Value, block);
        var owner = AsObject(receiver.Value);
        var countField = ListField("count", _factory.IntegerType);
        var count = ListCount(owner);
        var name = method.AssociatedSymbol?.Name ?? method.Name;
        if (name == "Count")
        { return new(count, block, FrontendSubsetClassification.Exact); }
        var valueType = _context.Type(method.ContainingType.TypeArguments[0]);
        var contents = ListField("value", valueType);
        if (name == "Add")
        {
            var previous = Pin(operation, new(count, block, FrontendSubsetClassification.Exact));
            block = ListRuntimeEffects(operation, method, [owner, values[0]],
                _factory.Binary(IrBinaryOperator.Equal, previous.Value, _factory.Integer(int.MaxValue)), previous.Continuation);
            _builder!.FieldStore(block, _context.Site(operation), IrWriteRegion.Field, owner, contents, values[0], previous.Value);
            _builder.FieldStore(block, _context.Site(operation), IrWriteRegion.Field, owner, countField,
                _factory.Binary(IrBinaryOperator.Add, previous.Value, _factory.Integer(1)));
            return new(_factory.Boolean(false), block, FrontendSubsetClassification.Exact);
        }
        var index = values[0];
        var outside = _factory.Binary(IrBinaryOperator.OrElse,
            _factory.Binary(IrBinaryOperator.LessThan, index, _factory.Integer(0)),
            _factory.Binary(IrBinaryOperator.GreaterThanOrEqual, index, count));
        // The invalid accessor retains resource/culture callback effects and
        // arbitrary ancillary faults before ArgumentOutOfRangeException.
        block = ArrayInterfaceBoundsEffects(property!, operation, index, outside, block);
        if (assigned != null)
        {
            _builder!.FieldStore(block, _context.Site(operation), IrWriteRegion.Field, owner, contents, values[1], index);
            return new(values[1], block, FrontendSubsetClassification.Exact);
        }
        return new(_factory.PureOpaque(contents, owner, index), block, FrontendSubsetClassification.Exact);
    }

    private IrBlockId ListRuntimeEffects(IOperation operation, IMethodSymbol method, IrTerm[] values, IrTerm full, IrBlockId block, bool initialization = false)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        values = RepositoryParameterGuard.NotNull(values, nameof(values));
        full = RepositoryParameterGuard.NotNull(full, nameof(full));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var site = _context.Site(operation);
        var fails = _context.Temporary(_factory.BooleanType);
        _builder!.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, fails);
        var failure = _builder.CreateBlock("list:growth-failure");
        var normal = _builder.CreateBlock("list:growth-normal");
        _builder.Branch(block, site, _factory.Binary(IrBinaryOperator.OrElse, _factory.Variable(fails),
            full), failure, normal);
        // Preserve independently reviewed effect facets just as an ordinary
        // bound call does. Missing facets still use the opaque fallback.
        var effects = EmitOpaqueCall(operation, method, values, failure, null,
            OpaqueEffects?.Invoke(method) ?? IrOpaqueCallEffects.All).Continuation;
        _builder.Throw(effects, site, IrExceptionKind.Unknown,
            ExceptionTarget?.Invoke(IrExceptionKind.Unknown, site) ?? _exceptionalExit);
        // Growth is possible, not guaranteed. A successful scalar/reference copy
        // invokes no element callbacks and preserves the existing logical entries.
        var grows = _context.Temporary(_factory.BooleanType);
        _builder.Havoc(normal, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, grows);
        var allocation = _builder.CreateBlock("list:growth-allocation");
        var continuation = _builder.CreateBlock("list:grown");
        _builder.Branch(normal, site, _factory.Variable(grows), allocation, continuation);
        _builder.Allocate(allocation, site, _factory.ObjectType);
        if (initialization)
        { _builder.Write(allocation, site, IrWriteRegion.Static); }
        _builder.Goto(allocation, site, continuation);
        return continuation;
    }
}
