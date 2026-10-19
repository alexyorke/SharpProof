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
        if (property.Instance is not { } instance || property.Property.GetMethod is not { } getter)
        { return null; }
        var knownElement = _context.KnownValueType(getter.ContainingType.TypeArguments.FirstOrDefault());
        if (!CSharpOperationSemantics.IsListStateMember(getter, knownElement) ||
            !CSharpOperationSemantics.IsConcreteListStateMember(getter, knownElement) && !_context.IsExactList(instance))
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
        if (method == null || instance == null)
        { return null; }
        var knownElement = _context.KnownValueType(method.ContainingType.TypeArguments.FirstOrDefault());
        if (!CSharpOperationSemantics.IsListStateMember(method, knownElement) ||
            !CSharpOperationSemantics.IsConcreteListStateMember(method, knownElement) && !_context.IsExactList(instance))
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
        if (name is "Insert" or "GetRange")
        { return ListRangeBody(operation, method, owner, values, name, contents, countField, count, block); }
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

    private TotalBodyValue ListRangeBody(IOperation operation, IMethodSymbol method, IrTerm owner, IrTerm[] values,
        string name, IrMemberId contents, IrMemberId countField, IrTerm count, IrBlockId block)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
        values = RepositoryParameterGuard.NotNull(values, nameof(values));
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        count = RepositoryParameterGuard.NotNull(count, nameof(count));
        RepositoryParameterGuard.ThrowIfInvalid(contents.IsDefault, nameof(contents));
        RepositoryParameterGuard.ThrowIfInvalid(countField.IsDefault, nameof(countField));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var site = _context.Site(operation);
        var previous = Pin(operation, new(count, block, FrontendSubsetClassification.Exact));
        count = previous.Value;
        block = previous.Continuation;
        var index = values[0];
        block = ListRangeBounds(operation, method, owner, values,
            _factory.Binary(IrBinaryOperator.LessThan, index, _factory.Integer(0)), IrExceptionKind.ArgumentOutOfRange, block);
        if (name == "Insert")
        {
            block = ListRangeBounds(operation, method, owner, values,
                _factory.Binary(IrBinaryOperator.GreaterThan, index, count), IrExceptionKind.ArgumentOutOfRange, block);
            block = ListRuntimeEffects(operation, method, [owner, .. values],
                _factory.Binary(IrBinaryOperator.Equal, count, _factory.Integer(int.MaxValue)), block);
            var done = ListCopySlots(operation, owner, owner, contents, index, count, reverse: true, block);
            _builder!.FieldStore(done, site, IrWriteRegion.Field, owner, contents, values[1], index);
            _builder.FieldStore(done, site, IrWriteRegion.Field, owner, countField,
                _factory.Binary(IrBinaryOperator.Add, count, _factory.Integer(1)));
            return new(_factory.Boolean(false), done, FrontendSubsetClassification.Exact);
        }
        var length = values[1];
        block = ListRangeBounds(operation, method, owner, values,
            _factory.Binary(IrBinaryOperator.LessThan, length, _factory.Integer(0)), IrExceptionKind.ArgumentOutOfRange, block);
        // Subtraction follows the runtime check and avoids index + length overflow.
        block = ListRangeBounds(operation, method, owner, values,
            _factory.Binary(IrBinaryOperator.LessThan, _factory.Binary(IrBinaryOperator.Subtract, count, index), length),
            IrExceptionKind.Argument, block);
        block = ListRuntimeEffects(operation, method, [owner, .. values], _factory.Boolean(false), block, initialization: true);
        var allocated = AllocateValue(operation, block);
        var destination = AsObject(allocated.Value);
        block = allocated.Continuation;
        var copied = ListCopySlots(operation, owner, destination, contents, index, length, reverse: false, block);
        _builder!.FieldStore(copied, site, IrWriteRegion.Field, destination, countField, length);
        return new(allocated.Value, copied, FrontendSubsetClassification.Exact);
    }

    private IrBlockId ListCopySlots(IOperation operation, IrTerm source, IrTerm destination, IrMemberId contents,
        IrTerm index, IrTerm extent, bool reverse, IrBlockId block)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        source = RepositoryParameterGuard.NotNull(source, nameof(source));
        destination = RepositoryParameterGuard.NotNull(destination, nameof(destination));
        index = RepositoryParameterGuard.NotNull(index, nameof(index));
        extent = RepositoryParameterGuard.NotNull(extent, nameof(extent));
        RepositoryParameterGuard.ThrowIfInvalid(contents.IsDefault, nameof(contents));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var site = _context.Site(operation);
        var cursor = _context.Temporary(_factory.IntegerType);
        _builder!.Assign(block, site, cursor, reverse ? extent : _factory.Integer(0));
        var done = _builder.CreateBlock("list:copy-done");
        var position = _factory.Variable(cursor);
        var condition = _factory.Binary(reverse ? IrBinaryOperator.GreaterThan : IrBinaryOperator.LessThan,
            position, reverse ? index : extent);
        var previous = _factory.Binary(IrBinaryOperator.Subtract, position, _factory.Integer(1));
        var from = reverse ? previous : _factory.Binary(IrBinaryOperator.Add, index, position);
        var next = reverse ? previous : _factory.Binary(IrBinaryOperator.Add, position, _factory.Integer(1));
        // Peel a short prefix so small concrete copies need no heap invariant.
        // The residual loop retains every remaining element: this is an exact
        // control-flow optimization, not a maximum supported collection length.
        for (var peeled = 0; peeled < 4; peeled++)
        {
            var copy = _builder.CreateBlock("list:copy-prefix");
            var continuation = _builder.CreateBlock("list:copy-prefix-next");
            _builder.Branch(block, site, condition, copy, done);
            _builder.FieldStore(copy, site, IrWriteRegion.Field, destination, contents,
                _factory.PureOpaque(contents, source, from), position);
            _builder.Assign(copy, site, cursor, next);
            _builder.Goto(copy, site, continuation);
            block = continuation;
        }
        var test = _builder.CreateBlock("list:copy-test");
        var remaining = _builder.CreateBlock("list:copy-remaining");
        _builder.Goto(block, site, test);
        _builder.Branch(test, site, condition, remaining, done);
        _builder.FieldStore(remaining, site, IrWriteRegion.Field, destination, contents,
            _factory.PureOpaque(contents, source, from), position);
        _builder.Assign(remaining, site, cursor, next);
        _builder.Goto(remaining, site, test);
        return done;
    }
    private IrBlockId ListRangeBounds(IOperation operation, IMethodSymbol method, IrTerm owner, IrTerm[] values,
        IrTerm outside, IrExceptionKind fault, IrBlockId block)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
        values = RepositoryParameterGuard.NotNull(values, nameof(values));
        outside = RepositoryParameterGuard.NotNull(outside, nameof(outside));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var site = _context.Site(operation);
        var invalid = _builder!.CreateBlock("list:range-invalid");
        var valid = _builder.CreateBlock("list:range-valid");
        _builder.Branch(block, site, outside, invalid, valid);
        // Framework resource lookup can invoke CultureInfo callbacks, mutate
        // reachable state, allocate, or throw before the intended argument fault.
        IrTerm[] arguments = [owner, .. values];
        var member = OpaqueCallMember(method, arguments, method.ReturnsVoid ? _factory.BooleanType : _context.Type(method.ReturnType), out var display);
        _builder.Call(invalid, _context.OpaqueCallSite(operation, IrOpaqueCallSite.WithCapabilities(IrOpaqueCallEffects.All | IrOpaqueCallEffects.ReadsAmbient, int.MaxValue), display + ":bounds"), null, member, null, arguments);
        var throws = _context.Temporary(_factory.BooleanType);
        _builder.Havoc(invalid, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, throws);
        invalid = ApplyRule(operation, new TotalScalarRule(_factory.Boolean(true),
            [new(IrExceptionKind.Unknown, _factory.Variable(throws))], FrontendSubsetClassification.Exact), invalid).Continuation;
        _builder.Throw(invalid, site, fault, ExceptionTarget?.Invoke(fault, site) ?? _exceptionalExit);
        return valid;
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
