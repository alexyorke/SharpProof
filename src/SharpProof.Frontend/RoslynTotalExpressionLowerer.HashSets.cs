namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalExpressionLowerer
{
    private IrMemberId HashSetField(string name, IrTypeId value, IrTypeId? key = null)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        RepositoryParameterGuard.ThrowIfInvalid(value.IsDefault, nameof(value));
        var identity = _factory.InternExternalIdentity(("hash-set", name, value, key),
            EqualityComparer<(string, string, IrTypeId, IrTypeId?)>.Default);
        return _factory.GetOrCreateMember(identity, _factory.ObjectType, IrFieldSites.Prefix + "hash-set:" + name,
            value, false, key is { } index ? [index] : []);
    }

    private GuardedExpression? HashSetClause(IOperation operation, TotalParameterState state, int depth)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        var property = operation as IPropertyReferenceOperation;
        var method = property?.Property.GetMethod;
        var instance = property?.Instance;
        if (method == null || instance == null || !CSharpOperationSemantics.IsHashSetStateMember(method,
            _context.KnownValueType(method.ContainingType.TypeArguments.FirstOrDefault())) || property!.Property.Name != "Count")
        { return null; }
        var receiver = LowerClause(instance, state, depth + 1);
        if (!receiver.Classification.IsExact)
        { return Failed(operation, receiver.Classification.Abstention); }
        var owner = AsObject(receiver.Value);
        var safe = And(receiver.SafeCondition, Not(_factory.Binary(IrBinaryOperator.Equal, receiver.Value, _factory.Null(receiver.Value.Type))));
        // Contains may initialize primitive comparer/runtime caches. Its body
        // retains those effects; it is not admitted as a pure logical clause.
        return new(HashSetCount(owner), safe, FrontendSubsetClassification.Exact);
    }

    private IrOpaqueTerm HashSetCount(IrTerm owner)
    {
        owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
        // Reentrant equality callbacks can corrupt even an API-constructed set's
        // internal free count. Preserve the signed observable getter; only exact
        // modeled transitions from known construction establish nonnegativity.
        return _factory.PureOpaque(HashSetField("count", _factory.IntegerType), owner);
    }

    private TotalBodyValue? HashSetBody(IOperation operation, IrBlockId block, int depth)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (ShadowCallSkeleton)
        { return null; }
        var creation = operation as IObjectCreationOperation;
        var invocation = operation as IInvocationOperation;
        var property = operation as IPropertyReferenceOperation;
        var method = creation?.Constructor ?? invocation?.TargetMethod ?? property?.Property.GetMethod;
        var instance = invocation?.Instance ?? property?.Instance;
        if (method == null || creation == null && instance == null || creation?.Initializer != null ||
            !CSharpOperationSemantics.IsHashSetStateMember(method,
                _context.KnownValueType(method.ContainingType.TypeArguments.FirstOrDefault())))
        { return null; }
        var isCount = method.AssociatedSymbol is IPropertySymbol { Name: "Count" };
        if (creation == null && !isCount && !_context.IsDefaultHashSetOrigin(instance!))
        { return null; }
        TotalBodyValue? receiver = null;
        if (creation == null)
        {
            receiver = LowerBodyValue(instance!, block, depth + 1);
            if (!receiver.Value.Classification.IsExact)
            { return Approximate(operation, receiver.Value.Continuation, receiver.Value.Classification.Abstention); }
            receiver = Pin(operation, receiver.Value);
            block = receiver.Value.Continuation;
        }
        var arguments = creation?.Arguments ?? invocation?.Arguments ?? property!.Arguments;
        var valuesBuilder = ImmutableArray.CreateBuilder<IrTerm>(arguments.Length);
        valuesBuilder.Count = arguments.Length;
        foreach (var argument in arguments.OrderBy(argument => argument is null ? throw new ArgumentNullException(nameof(argument)) : argument.Syntax.SpanStart))
        {
            var value = LowerBodyValue(argument.Value, block, depth + 1);
            if (!value.Classification.IsExact)
            { return Approximate(operation, value.Continuation, value.Classification.Abstention); }
            value = Pin(operation, value);
            valuesBuilder[argument.Parameter!.Ordinal] = value.Value;
            block = value.Continuation;
        }
        var values = valuesBuilder.ToImmutable();
        var site = _context.Site(operation);
        if (creation != null)
        {
            block = ListRuntimeEffects(operation, method, values, values.Length == 0 ? _factory.Boolean(false) :
                _factory.Binary(IrBinaryOperator.LessThan, values[0], _factory.Integer(0)), block, initialization: true);
            var element = _context.KnownValueType(method.ContainingType.TypeArguments[0]) ?? method.ContainingType.TypeArguments[0];
            if (!CSharpOperationSemantics.IsHashSetPrimitiveElement(element))
            {
                // An unobserved generic comparer/runtime initializer receives no
                // caller-heap preservation certificate. Its result is unpublished,
                // so establish empty state only after its normal continuation.
                block = EmitOpaqueCall(operation, method, values, block, null, IrOpaqueCallEffects.All).Continuation;
            }
            var allocated = AllocateValue(operation, block);
            _builder!.FieldStore(allocated.Continuation, site, IrWriteRegion.Field, AsObject(allocated.Value),
                HashSetField("default-comparer", _factory.BooleanType), _factory.Boolean(true));
            return allocated;
        }
        var owner = AsObject(receiver!.Value.Value);
        block = CheckReceiver(operation, receiver.Value.Value, block);
        if (isCount)
        { return new(HashSetCount(owner), block, FrontendSubsetClassification.Exact); }
        // Lineage identifies default construction, but an unknown writer can
        // invalidate comparer and membership facts. Never infer a comparer merely
        // from a HashSet-typed incoming value or a custom-comparer constructor.
        var primitive = _builder!.CreateBlock("hash-set:default-comparer");
        var custom = _builder.CreateBlock("hash-set:unknown-comparer");
        var done = _builder.CreateBlock("hash-set:comparer-joined");
        _builder.Branch(block, site, _factory.PureOpaque(HashSetField("default-comparer", _factory.BooleanType), owner), primitive, custom);
        var modeled = DefaultHashSetState(operation, method, owner, values[0], primitive);
        var opaque = EmitOpaqueCall(operation, method, [owner, .. values], custom, null, IrOpaqueCallEffects.All);
        var result = _context.Temporary(_factory.BooleanType);
        _builder.Assign(modeled.Continuation, site, result, modeled.Value);
        _builder.Goto(modeled.Continuation, site, done);
        _builder.Assign(opaque.Continuation, site, result, opaque.Value);
        _builder.Goto(opaque.Continuation, site, done);
        return new(_factory.Variable(result), done, FrontendSubsetClassification.Exact);
    }

    private TotalBodyValue DefaultHashSetState(IOperation operation, IMethodSymbol method, IrTerm owner, IrTerm key, IrBlockId block)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
        key = RepositoryParameterGuard.NotNull(key, nameof(key));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        // Even primitive equality can require framework initialization. Preserve
        // allocation, static writes and failure instead of granting unconditional
        // no-throw/no-allocation facts to a membership operation.
        block = ListRuntimeEffects(operation, method, [owner, key], _factory.Boolean(false), block, initialization: true);
        // Keyed heap fields accept integer/reference keys. Boolean equality has
        // the injective integer representation false=0, true=1; keep the original
        // argument for calls and effects instead of changing the IR key contract.
        var lookup = key.Type == _factory.BooleanType ? _factory.Conditional(key, _factory.Integer(1), _factory.Integer(0)) : key;
        var membership = HashSetField("present", _factory.BooleanType, lookup.Type);
        var present = _factory.PureOpaque(membership, owner, lookup);
        if (method.Name == "Contains")
        { return new(present, block, FrontendSubsetClassification.Exact); }
        var site = _context.Site(operation);
        var changed = _builder!.CreateBlock("hash-set:changed");
        var unchanged = _builder.CreateBlock("hash-set:unchanged");
        var done = _builder.CreateBlock("hash-set:stored");
        var adding = method.Name == "Add";
        _builder.Branch(block, site, present, adding ? unchanged : changed, adding ? changed : unchanged);
        if (adding)
        {
            changed = ListRuntimeEffects(operation, method, [owner, key],
                _factory.Binary(IrBinaryOperator.Equal, HashSetCount(owner), _factory.Integer(int.MaxValue)), changed);
        }
        _builder.FieldStore(changed, site, IrWriteRegion.Field, owner, HashSetField("count", _factory.IntegerType),
            _factory.Binary(adding ? IrBinaryOperator.Add : IrBinaryOperator.Subtract, HashSetCount(owner), _factory.Integer(1)));
        _builder.FieldStore(changed, site, IrWriteRegion.Field, owner, membership, _factory.Boolean(adding), lookup);
        var result = _context.Temporary(_factory.BooleanType);
        _builder.Assign(changed, site, result, _factory.Boolean(true));
        _builder.Goto(changed, site, done);
        _builder.Assign(unchanged, site, result, _factory.Boolean(false));
        _builder.Goto(unchanged, site, done);
        return new(_factory.Variable(result), done, FrontendSubsetClassification.Exact);
    }
}
