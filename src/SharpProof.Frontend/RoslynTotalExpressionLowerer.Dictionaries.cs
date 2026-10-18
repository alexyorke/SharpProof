namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalExpressionLowerer
{
    private readonly Dictionary<(IrTypeId Owner, string Name, IrTypeId Value, IrTypeId? Key), IrMemberId> _dictionaryFields = [];

    private IrMemberId DictionaryField(INamedTypeSymbol type, string name, IrTypeId value, IrTypeId? key = null)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        RepositoryParameterGuard.ThrowIfInvalid(value.IsDefault, nameof(value));
        var storage = (_context.Type(type), name, value, key);
        if (_dictionaryFields.TryGetValue(storage, out var field))
        { return field; }
        field = _factory.GetOrCreateMember(_factory.InternExternalIdentity(storage,
            EqualityComparer<(IrTypeId Owner, string Name, IrTypeId Value, IrTypeId? Key)>.Default), _factory.ObjectType,
            IrFieldSites.Prefix + "dictionary:" + name, value, false, key is { } index ? [index] : []);
        _dictionaryFields.Add(storage, field);
        return field;
    }

    private IrBlockId DictionaryInitialization(IOperation operation, IMethodSymbol method, IrTerm[] values, IrBlockId block)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        values = RepositoryParameterGuard.NotNull(values, nameof(values));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var source = _factory.GetOperationInfo(_context.Site(operation)).SourceSpan;
        var site = _factory.CreateOperation(IrOpaqueCallSite.DictionaryInitialization(method.ContainingType.TypeArguments[0].SpecialType.ToString()), source);
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
        if (CSharpOperationSemantics.IsEmptyScalarDictionaryCreation(operation))
        { return AllocateValue(operation, block); }
        var assigned = (operation as ISimpleAssignmentOperation)?.Value;
        var access = operation is ISimpleAssignmentOperation assignment ? assignment.Target : operation;
        var invocation = access as IInvocationOperation;
        var property = access as IPropertyReferenceOperation;
        var method = invocation?.TargetMethod ?? (assigned == null ? property?.Property.GetMethod : property?.Property.SetMethod);
        var instance = invocation?.Instance ?? property?.Instance;
        if (method == null || instance == null || !CSharpOperationSemantics.IsScalarDictionaryMember(method) ||
            !_context.IsDefaultScalarDictionary(instance))
        { return null; }
        var name = method.AssociatedSymbol is IPropertySymbol { IsIndexer: true } ? "Item" : method.AssociatedSymbol?.Name ?? method.Name;
        if (name is not ("Count" or "Item" or "Add" or "ContainsKey"))
        { return null; }
        var arguments = invocation?.Arguments ?? property!.Arguments;
        var receiver = LowerBodyValue(instance, block, depth + 1);
        if (!receiver.Classification.IsExact)
        { return Approximate(operation, receiver.Continuation, receiver.Classification.Abstention); }
        block = receiver.Continuation;
        var values = new IrTerm[arguments.Length + (assigned == null ? 0 : 1)];
        foreach (var argument in arguments.OrderBy(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : argument.Syntax.SpanStart))
        {
            var value = LowerBodyValue(argument.Value, block, depth + 1);
            if (!value.Classification.IsExact)
            { return Approximate(operation, value.Continuation, value.Classification.Abstention); }
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
        var type = method.ContainingType;
        var count = DictionaryField(type, "count", _factory.IntegerType);
        if (name == "Count")
        { return new(_factory.Binary(IrBinaryOperator.BitwiseAnd, _factory.PureOpaque(count, owner), _factory.Integer(int.MaxValue)), block, FrontendSubsetClassification.Exact); }
        var keyType = _context.Type(type.TypeArguments[0]);
        var valueType = _context.Type(type.TypeArguments[1]);
        var membership = DictionaryField(type, "present", _factory.BooleanType, keyType);
        var contents = DictionaryField(type, "value", valueType, keyType);
        block = DictionaryInitialization(operation, method, values, block);
        var key = values[0];
        var present = _factory.PureOpaque(membership, owner, key);
        if (name == "ContainsKey")
        { return new(present, block, FrontendSubsetClassification.Exact); }
        var site = _context.Site(operation);
        if (name == "Item" && assigned == null)
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
        return new(assigned == null ? _factory.Boolean(false) : valueToStore, continuation, FrontendSubsetClassification.Exact);
    }
}
