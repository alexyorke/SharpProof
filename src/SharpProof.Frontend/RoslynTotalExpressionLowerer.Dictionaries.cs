namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalExpressionLowerer
{
    private readonly Dictionary<(string Name, IrTypeId Value, IrTypeId? Key), IrMemberId> _dictionaryFields = [];

    private IrMemberId DictionaryField(string name, IrTypeId value, IrTypeId? key = null)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        RepositoryParameterGuard.ThrowIfInvalid(value.IsDefault, nameof(value));
        var storage = (name, value, key);
        if (_dictionaryFields.TryGetValue(storage, out var field))
        { return field; }
        field = _factory.GetOrCreateMember(_factory.InternExternalIdentity(("dictionary", name, value, key),
            EqualityComparer<(string, string, IrTypeId, IrTypeId?)>.Default), _factory.ObjectType,
            IrFieldSites.Prefix + "dictionary:" + name, value, false, key is { } index ? [index] : []);
        _dictionaryFields.Add(storage, field);
        return field;
    }

    private GuardedExpression? DictionaryClause(IOperation operation, TotalParameterState state, int depth)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        var invocation = operation as IInvocationOperation;
        var property = operation as IPropertyReferenceOperation;
        var method = invocation?.TargetMethod ?? property?.Property.GetMethod;
        var instance = invocation?.Instance ?? property?.Instance;
        if (method == null || instance == null)
        { return null; }
        var keyType = _context.KnownValueType(method.ContainingType.TypeArguments.ElementAtOrDefault(0));
        var valueType = _context.KnownValueType(method.ContainingType.TypeArguments.ElementAtOrDefault(1));
        if (!CSharpOperationSemantics.IsDefaultDictionaryStateMember(method, keyType, valueType) ||
            !_context.IsDefaultDictionaryOrigin(instance))
        { return null; }
        var name = method.AssociatedSymbol is IPropertySymbol { IsIndexer: true } ? "Item" : method.AssociatedSymbol?.Name ?? method.Name;
        if (name is not ("Count" or "Item" or "ContainsKey"))
        { return null; }
        var receiver = LowerClause(instance, state, depth + 1);
        if (!receiver.Classification.IsExact)
        { return Failed(operation, receiver.Classification.Abstention); }
        var owner = AsObject(receiver.Value);
        var safe = And(receiver.SafeCondition, Not(_factory.Binary(IrBinaryOperator.Equal, receiver.Value, _factory.Null(receiver.Value.Type))));
        if (name == "Count")
        {
            return new(_factory.Binary(IrBinaryOperator.BitwiseAnd,
                _factory.PureOpaque(DictionaryField("count", _factory.IntegerType), owner), _factory.Integer(int.MaxValue)),
                safe, FrontendSubsetClassification.Exact);
        }
        var argument = (invocation?.Arguments ?? property!.Arguments)[0];
        var key = LowerClause(argument.Value, state, depth + 1);
        if (!key.Classification.IsExact)
        { return Failed(operation, key.Classification.Abstention); }
        // This is a modeling guard, not an imported assumption. Source call
        // preconditions record it as an obligation and still execute the body.
        // Unknown writers can invalidate the comparer even for a known origin.
        safe = And(safe, And(key.SafeCondition,
            _factory.PureOpaque(DictionaryField("default-comparer", _factory.BooleanType), owner)));
        var present = _factory.PureOpaque(DictionaryField("present", _factory.BooleanType, key.Value.Type), owner, key.Value);
        return name == "ContainsKey"
            ? new(present, safe, FrontendSubsetClassification.Exact)
            : new(_factory.PureOpaque(DictionaryField("value", _context.Type(method.ReturnType), key.Value.Type), owner, key.Value),
                And(safe, present), FrontendSubsetClassification.Exact);
    }

    private IrBlockId DictionaryInitialization(IOperation operation, IMethodSymbol method, IrTerm[] values, IrBlockId block)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        values = RepositoryParameterGuard.NotNull(values, nameof(values));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var source = _factory.GetOperationInfo(_context.Site(operation)).SourceSpan;
        var keyType = _context.KnownValueType(method.ContainingType.TypeArguments[0]) ?? method.ContainingType.TypeArguments[0];
        var site = _factory.CreateOperation(IrOpaqueCallSite.DictionaryInitialization(keyType.SpecialType.ToString()), source);
        var initialize = _context.Temporary(_factory.BooleanType);
        _builder!.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, initialize);
        var effects = _builder.CreateBlock("dictionary:framework-initialization");
        var initialized = _builder.CreateBlock("dictionary:initialized");
        _builder.Branch(block, site, _factory.Variable(initialize), effects, initialized);
        // Primitive runtime initialization can allocate, populate static caches,
        // synchronize or fail. It cannot mutate an existing dictionary's logical
        // contents through user equality/hash callbacks. Retain the ambient
        // uncertainty without invalidating its modeled entries on normal return.
        var member = OpaqueCallMember(method, values, _factory.BooleanType, out var display);
        _builder.Call(effects, _context.OpaqueCallSite(operation, IrOpaqueCallEffects.All & ~IrOpaqueCallEffects.Writes,
            display + ":primitive-runtime-initialization"), null, member, null, values);
        _builder.Write(effects, site, IrWriteRegion.Static);
        _builder.Allocate(effects, site, _factory.ObjectType);
        _builder.Goto(effects, site, initialized);
        var fails = _context.Temporary(_factory.BooleanType);
        _builder.Havoc(initialized, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, fails);
        return ApplyRule(operation, new TotalScalarRule(_factory.Boolean(true),
            [new(IrExceptionKind.Unknown, _factory.Variable(fails))], FrontendSubsetClassification.Exact), initialized).Continuation;
    }

    private TotalBodyValue? DictionaryBody(IOperation operation, IrBlockId block, int depth)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (ShadowCallSkeleton)
        { return null; }
        if (CSharpOperationSemantics.IsEmptyDefaultDictionaryCreation(operation))
        {
            var constructor = ((IObjectCreationOperation)operation).Constructor!;
            var constructorKey = _context.KnownValueType(constructor.ContainingType.TypeArguments[0]) ?? constructor.ContainingType.TypeArguments[0];
            if (!CSharpOperationSemantics.IsComparisonIntegerType(constructorKey))
            {
                // Default-comparer initialization may allocate, fault or affect
                // arbitrary existing state. It cannot reach the unpublished result.
                // Establish fresh logical state only after its normal continuation.
                block = EmitOpaqueCall(operation, constructor, [], block, null, IrOpaqueCallEffects.All).Continuation;
            }
            var allocated = AllocateValue(operation, block);
            _builder!.FieldStore(allocated.Continuation, _context.Site(operation), IrWriteRegion.Field, AsObject(allocated.Value),
                DictionaryField("default-comparer", _factory.BooleanType), _factory.Boolean(true));
            return allocated;
        }
        var assigned = (operation as ISimpleAssignmentOperation)?.Value;
        var access = operation is ISimpleAssignmentOperation assignment ? assignment.Target : operation;
        var invocation = access as IInvocationOperation;
        var property = access as IPropertyReferenceOperation;
        var method = invocation?.TargetMethod ?? (assigned == null ? property?.Property.GetMethod : property?.Property.SetMethod);
        var instance = invocation?.Instance ?? property?.Instance;
        if (method == null || instance == null)
        { return null; }
        var knownKey = _context.KnownValueType(method.ContainingType.TypeArguments.ElementAtOrDefault(0));
        var knownValue = _context.KnownValueType(method.ContainingType.TypeArguments.ElementAtOrDefault(1));
        if (!CSharpOperationSemantics.IsDefaultDictionaryStateMember(method, knownKey, knownValue) ||
            !_context.IsDefaultDictionaryOrigin(instance))
        { return null; }
        var name = method.AssociatedSymbol is IPropertySymbol { IsIndexer: true } ? "Item" : method.AssociatedSymbol?.Name ?? method.Name;
        var arguments = invocation?.Arguments ?? property!.Arguments;
        var receiver = LowerBodyValue(instance, block, depth + 1);
        if (!receiver.Classification.IsExact)
        { return Approximate(operation, receiver.Continuation, receiver.Classification.Abstention); }
        receiver = Pin(operation, receiver);
        block = receiver.Continuation;
        var values = new IrTerm[arguments.Length + (assigned == null ? 0 : 1)];
        foreach (var argument in arguments.OrderBy(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : argument.Syntax.SpanStart))
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
            value = Pin(operation, value);
            values[values.Length - 1] = value.Value;
            block = value.Continuation;
        }
        block = CheckReceiver(operation, receiver.Value, block);
        var owner = AsObject(receiver.Value);
        var count = DictionaryField("count", _factory.IntegerType);
        if (name == "Count")
        { return new(_factory.Binary(IrBinaryOperator.BitwiseAnd, _factory.PureOpaque(count, owner), _factory.Integer(int.MaxValue)), block, FrontendSubsetClassification.Exact); }
        if (_context.IsDefaultDictionary(instance))
        { return DefaultDictionaryState(operation, method, owner, values, name, assigned != null, block); }

        // Constructor lineage survives publication, but its comparer does not.
        // An opaque writer invalidates this heap fact along with the contents.
        var site = _context.Site(operation);
        var primitive = _builder!.CreateBlock("dictionary:default-comparer");
        var custom = _builder.CreateBlock("dictionary:unknown-comparer");
        var done = _builder.CreateBlock("dictionary:comparer-joined");
        _builder.Branch(block, site, _factory.PureOpaque(DictionaryField("default-comparer", _factory.BooleanType), owner), primitive, custom);
        var modeled = DefaultDictionaryState(operation, method, owner, values, name, assigned != null, primitive);
        var opaque = EmitOpaqueCall(operation, method, [owner, .. values], custom, null, IrOpaqueCallEffects.All);
        var result = _context.Temporary(modeled.Value.Type);
        _builder.Assign(modeled.Continuation, site, result, modeled.Value);
        _builder.Goto(modeled.Continuation, site, done);
        _builder.Assign(opaque.Continuation, site, result,
            assigned != null ? values[values.Length - 1] : method.ReturnsVoid ? _factory.Boolean(false) : opaque.Value);
        _builder.Goto(opaque.Continuation, site, done);
        return new(_factory.Variable(result), done, FrontendSubsetClassification.Exact);
    }

    private TotalBodyValue DefaultDictionaryState(IOperation operation, IMethodSymbol method, IrTerm owner, IrTerm[] values,
        string name, bool assigned, IrBlockId block)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
        values = RepositoryParameterGuard.NotNull(values, nameof(values));
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var type = method.ContainingType;
        var count = DictionaryField("count", _factory.IntegerType);
        var keyType = _context.Type(type.TypeArguments[0]);
        var valueType = _context.Type(type.TypeArguments[1]);
        var membership = DictionaryField("present", _factory.BooleanType, keyType);
        var contents = DictionaryField("value", valueType, keyType);
        block = DictionaryInitialization(operation, method, values, block);
        var key = values[0];
        var present = _factory.PureOpaque(membership, owner, key);
        if (name == "ContainsKey")
        { return new(present, block, FrontendSubsetClassification.Exact); }
        var site = _context.Site(operation);
        if (name == "Item" && !assigned)
        {
            var found = _builder!.CreateBlock("dictionary:found");
            var missing = _builder.CreateBlock("dictionary:missing");
            var done = _builder.CreateBlock("dictionary:read");
            var result = _context.Temporary(valueType);
            _builder.Branch(block, site, present, found, missing);
            _builder.Assign(found, site, result, _factory.PureOpaque(contents, owner, key));
            _builder.Goto(found, site, done);
            // Formatting a missing key can run framework resource/culture code.
            // Retain its effects and exception uncertainty until modeled separately.
            var opaque = EmitOpaqueCall(operation, method, [owner, key], missing, null, IrOpaqueCallEffects.All);
            _builder.Assign(opaque.Continuation, site, result, opaque.Value);
            _builder.Goto(opaque.Continuation, site, done);
            return new(_factory.Variable(result), done, FrontendSubsetClassification.Exact);
        }
        var insert = _builder!.CreateBlock("dictionary:insert");
        var existing = _builder.CreateBlock("dictionary:existing");
        var continuation = _builder.CreateBlock("dictionary:stored");
        _builder.Branch(block, site, present, existing, insert);
        var valueToStore = values[1];
        if (name == "Add")
        {
            var opaque = EmitOpaqueCall(operation, method, [owner, key, valueToStore], existing, null, IrOpaqueCallEffects.All);
            _builder.Goto(opaque.Continuation, site, continuation);
        }
        else
        {
            _builder.FieldStore(existing, site, IrWriteRegion.Field, owner, contents, valueToStore, key);
            _builder.Goto(existing, site, continuation);
        }
        // Dictionary creation is already an allocation effect. Successful fresh
        // insertion initializes membership and contents in the same shared heap.
        // Capacity/growth and exceptional partial mutation remain separate gaps.
        var previousCount = _factory.PureOpaque(count, owner);
        _builder.FieldStore(insert, site, IrWriteRegion.Field, owner, count,
            _factory.Binary(IrBinaryOperator.Add, previousCount, _factory.Integer(1)));
        _builder.FieldStore(insert, site, IrWriteRegion.Field, owner, contents, valueToStore, key);
        _builder.FieldStore(insert, site, IrWriteRegion.Field, owner, membership, _factory.Boolean(true), key);
        _builder.Goto(insert, site, continuation);
        return new(!assigned ? _factory.Boolean(false) : valueToStore, continuation, FrontendSubsetClassification.Exact);
    }
}
