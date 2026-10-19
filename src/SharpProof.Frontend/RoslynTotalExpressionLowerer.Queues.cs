namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsQueueStateMember(IMethodSymbol method, ITypeSymbol? knownElement = null)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return IsFrameworkCollectionMember(method) && !method.IsStatic && !method.IsVirtual &&
            method.ContainingType is { TypeArguments.Length: 1 } type &&
            type.OriginalDefinition.GetDocumentationCommentId() == "T:System.Collections.Generic.Queue`1" &&
            (method.AssociatedSymbol ?? (ISymbol)method).OriginalDefinition.GetDocumentationCommentId() is
                "M:System.Collections.Generic.Queue`1.#ctor" or "M:System.Collections.Generic.Queue`1.#ctor(System.Int32)" or
                "M:System.Collections.Generic.Queue`1.Enqueue(`0)" or "M:System.Collections.Generic.Queue`1.Dequeue" or
                "P:System.Collections.Generic.Queue`1.Count" &&
            (method.MethodKind == MethodKind.Constructor || method.AssociatedSymbol is IPropertySymbol { Name: "Count" } ||
                IsValueDomain(knownElement ?? type.TypeArguments[0]));
    }
}

internal sealed partial class RoslynTotalExpressionLowerer
{
    private IrMemberId QueueField(string name, IrTypeId value)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        RepositoryParameterGuard.ThrowIfInvalid(value.IsDefault, nameof(value));
        var identity = _factory.InternExternalIdentity(("queue", name, value), EqualityComparer<(string, string, IrTypeId)>.Default);
        return _factory.GetOrCreateMember(identity, _factory.ObjectType, IrFieldSites.Prefix + "queue:" + name, value,
            false, name == "value" ? [_factory.IntegerType] : []);
    }

    // A logical ring of 2^31 positions embeds every possible CLR queue (Count
    // is at most Int32.MaxValue). Head may wrap indefinitely without losing
    // live entries. Physical capacity and implementation-specific array indices
    // are deliberately absent; unknown writes invalidate these shared fields.
    private IrTerm QueuePosition(IrTerm position)
    {
        position = RepositoryParameterGuard.NotNull(position, nameof(position));
        return _factory.Binary(IrBinaryOperator.BitwiseAnd, position, _factory.Integer(int.MaxValue));
    }

    private GuardedExpression? QueueClause(IPropertyReferenceOperation property, TotalParameterState state, int depth)
    {
        property = RepositoryParameterGuard.NotNull(property, nameof(property));
        if (property.Instance is not { } instance || property.Property.GetMethod is not { } getter ||
            !CSharpOperationSemantics.IsQueueStateMember(getter))
        { return null; }
        var receiver = LowerClause(instance, state, depth + 1);
        if (!receiver.Classification.IsExact)
        { return Failed(property, receiver.Classification.Abstention); }
        return new(QueuePosition(_factory.PureOpaque(QueueField("count", _factory.IntegerType), AsObject(receiver.Value))),
            And(receiver.SafeCondition, Not(_factory.Binary(IrBinaryOperator.Equal, receiver.Value, _factory.Null(receiver.Value.Type)))),
            FrontendSubsetClassification.Exact);
    }

    private TotalBodyValue? QueueBody(IOperation operation, IrBlockId block, int depth)
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
        if (method == null || creation == null && instance == null || !CSharpOperationSemantics.IsQueueStateMember(method,
            _context.KnownValueType(method.ContainingType.TypeArguments.FirstOrDefault())) || creation?.Initializer != null)
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
        var values = new IrTerm[arguments.Length];
        foreach (var argument in arguments.OrderBy(argument => argument is null ? throw new ArgumentNullException(nameof(argument)) : argument.Syntax.SpanStart))
        {
            var value = LowerBodyValue(argument.Value, block, depth + 1);
            if (!value.Classification.IsExact)
            { return Approximate(operation, value.Continuation, value.Classification.Abstention); }
            value = Pin(operation, value);
            values[argument.Parameter!.Ordinal] = value.Value;
            block = value.Continuation;
        }
        var countField = QueueField("count", _factory.IntegerType);
        var headField = QueueField("head", _factory.IntegerType);
        if (creation != null)
        {
            block = ListRuntimeEffects(operation, method, values, values.Length == 0 ? _factory.Boolean(false) :
                _factory.Binary(IrBinaryOperator.LessThan, values[0], _factory.Integer(0)), block, initialization: true);
            var allocated = AllocateValue(operation, block);
            var allocatedOwner = AsObject(allocated.Value);
            _builder!.FieldStore(block, _context.Site(operation), IrWriteRegion.Field, allocatedOwner, countField, _factory.Integer(0));
            _builder.FieldStore(block, _context.Site(operation), IrWriteRegion.Field, allocatedOwner, headField, _factory.Integer(0));
            return allocated;
        }
        block = CheckReceiver(operation, receiver!.Value.Value, block);
        var owner = AsObject(receiver.Value.Value);
        var count = QueuePosition(_factory.PureOpaque(countField, owner));
        var name = method.AssociatedSymbol?.Name ?? method.Name;
        if (name == "Count")
        { return new(count, block, FrontendSubsetClassification.Exact); }
        var oldCount = Pin(operation, new(count, block, FrontendSubsetClassification.Exact));
        var oldHead = Pin(operation, new(QueuePosition(_factory.PureOpaque(headField, owner)), oldCount.Continuation, FrontendSubsetClassification.Exact));
        block = oldHead.Continuation;
        var contents = QueueField("value", _context.Type(method.ContainingType.TypeArguments[0]));
        if (name == "Enqueue")
        {
            block = ListRuntimeEffects(operation, method, [owner, values[0]],
                _factory.Binary(IrBinaryOperator.Equal, oldCount.Value, _factory.Integer(int.MaxValue)), block);
            var tail = QueuePosition(_factory.Binary(IrBinaryOperator.Add, oldHead.Value, oldCount.Value));
            _builder!.FieldStore(block, _context.Site(operation), IrWriteRegion.Field, owner, contents, values[0], tail);
            _builder.FieldStore(block, _context.Site(operation), IrWriteRegion.Field, owner, countField,
                _factory.Binary(IrBinaryOperator.Add, oldCount.Value, _factory.Integer(1)));
            return new(_factory.Boolean(false), block, FrontendSubsetClassification.Exact);
        }
        var empty = _builder!.CreateBlock("queue:empty");
        var normal = _builder.CreateBlock("queue:dequeue");
        _builder.Branch(block, _context.Site(operation), _factory.Binary(IrBinaryOperator.Equal, oldCount.Value, _factory.Integer(0)), empty, normal);
        // Empty removal may use culture/resources or fail while constructing
        // InvalidOperationException. Keep those effects and arbitrary faults.
        var fault = EmitOpaqueCall(operation, method, [owner], empty, null, IrOpaqueCallEffects.All).Continuation;
        _builder.Throw(fault, _context.Site(operation), IrExceptionKind.Unknown,
            ExceptionTarget?.Invoke(IrExceptionKind.Unknown, _context.Site(operation)) ?? _exceptionalExit);
        var result = Pin(operation, new(_factory.PureOpaque(contents, owner, oldHead.Value), normal, FrontendSubsetClassification.Exact));
        _builder.FieldStore(result.Continuation, _context.Site(operation), IrWriteRegion.Field, owner, headField,
            QueuePosition(_factory.Binary(IrBinaryOperator.Add, oldHead.Value, _factory.Integer(1))));
        _builder.FieldStore(result.Continuation, _context.Site(operation), IrWriteRegion.Field, owner, countField,
            _factory.Binary(IrBinaryOperator.Subtract, oldCount.Value, _factory.Integer(1)));
        return result;
    }
}
