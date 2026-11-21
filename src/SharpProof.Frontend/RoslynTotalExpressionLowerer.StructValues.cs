namespace SharpProof.Frontend;

// A value of a mutable modeled struct is an immutable object, as a readonly
// struct's is. A store into a field or auto-property of a struct variable
// builds a new object that holds the variable's fields with that one
// replaced, and assigns it to the variable. Every other variable, element or
// box that held the old value keeps it: C#'s copy semantics. A mutating call
// on a variable runs on the callee's own copy of `this`, which the caller
// assigns back to the variable on normal return (see InlineSourceMember).
internal sealed partial class RoslynTotalExpressionLowerer
{
    // The variable a mutation through `instance` changes: a local or by-value
    // parameter of this frame, or `this` of a frame whose caller assigns it
    // back. A field of an object, an element, a ref local or parameter, a
    // readonly local (whose members C# calls on a copy), a variable another
    // frame or the root closure shares, and the root's own `this`, the
    // caller's storage, are not.
    internal bool TryStructStorage(IOperation? instance, out IrVarId storage)
    {
        storage = default;
        if (instance == null)
        { return false; }
        if (IsImplicitThis(instance))
        {
            if (!_context.MutatesOwnReceiver || _context.Receiver is not { } self)
            { return false; }
            storage = self.Current;
            return true;
        }
        var symbol = instance switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            IFlowCaptureReferenceOperation capture => _context.StorageCaptureSymbol(capture.Id),
            _ => (ISymbol?)null
        };
        var owned = symbol switch
        {
            ILocalSymbol { RefKind: RefKind.None, IsConst: false, IsForEach: false, IsUsing: false, IsFixed: false } local =>
                SymbolEqualityComparer.Default.Equals(local.ContainingSymbol, _context.Target),
            IParameterSymbol { RefKind: RefKind.None, IsThis: false } parameter => _context.OwnsParameter(parameter),
            _ => false
        };
        if (!owned || RootClosure != null && RootClosure.Captures.Contains(symbol!, SymbolEqualityComparer.Default))
        { return false; }
        storage = _context.Variable(symbol!);
        return true;
    }

    // A receiver C# copies before calling a member that is not readonly: the
    // value of a call or `new`, a property's value, a foreach or using
    // variable, or `this` of a readonly member. A mutation changes only the
    // copy.
    internal bool IsCopiedStructReceiver(IOperation instance)
    {
        instance = RepositoryParameterGuard.NotNull(instance, nameof(instance));
        if (IsImplicitThis(instance))
        { return _context.Target.IsReadOnly && _context.Target.MethodKind != MethodKind.Constructor; }
        return instance is IInvocationOperation { TargetMethod: { ReturnsByRef: false, ReturnsByRefReadonly: false } } or
            IObjectCreationOperation or
            IPropertyReferenceOperation { Property: { ReturnsByRef: false, ReturnsByRefReadonly: false } } or
            ILocalReferenceOperation { Local: { RefKind: RefKind.None } and ({ IsForEach: true } or { IsUsing: true }) };
    }

    // Whether an unknown call of `method` on `instance` could change the
    // place that holds a mutable modeled struct: the struct's own member
    // that is not readonly, an interface member it may implement, or a
    // virtual member of object or ValueType it overrides with one. Any other
    // inherited member runs on a boxed copy.
    private bool MayMutateStructPlace(IMethodSymbol method, IOperation? instance)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        // A type-parameter receiver is the struct its frame specializes it to.
        if (instance == null || _context.SpecializeSourceType(instance.Type) is not INamedTypeSymbol type || method.IsStatic || method.IsReadOnly ||
            !CSharpOperationSemantics.IsMutableModeledStruct(type) || IsCopiedStructReceiver(instance))
        { return false; }
        if (SymbolEqualityComparer.Default.Equals(method.ContainingType, type) || method.ContainingType.TypeKind == TypeKind.Interface)
        { return true; }
        foreach (var candidate in type.GetMembers(method.Name).OfType<IMethodSymbol>())
        {
            Spend?.Invoke();
            for (var overridden = candidate.OverriddenMethod; overridden != null; overridden = overridden.OverriddenMethod)
            {
                if (SymbolEqualityComparer.Default.Equals(overridden.OriginalDefinition, method.OriginalDefinition))
                { return !candidate.IsReadOnly; }
            }
        }
        return false;
    }

    // `s.F = v`, `s.F += v`, `s.F++` and the same through an auto-property,
    // for a variable `s` of a mutable modeled struct. C# evaluates the
    // right-hand side, which may itself change `s`, before the store, so the
    // new value copies the variable's fields as they are then.
    private TotalBodyValue? StructMutation(IOperation operation, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var target = operation switch
        {
            ISimpleAssignmentOperation { IsRef: false } assigned => assigned.Target,
            ICompoundAssignmentOperation compounded => compounded.Target,
            IIncrementOrDecrementOperation incremented => incremented.Target,
            _ => null
        };
        IFieldSymbol? field = null;
        IOperation? instance = null;
        if (target is IFieldReferenceOperation { Field.IsStatic: false, Field.IsReadOnly: false } fieldTarget)
        {
            field = fieldTarget.Field;
            instance = fieldTarget.Instance;
        }
        // A setter is a method of the struct, so calling it runs the type's
        // initializer (ECMA-335 II.10.5.3.1); a store of its backing field
        // stands for it only when that initializer has no effect, or when
        // the caller belongs to the struct and has run it already.
        else if (target is IPropertyReferenceOperation { Arguments.Length: 0 } propertyTarget &&
            CSharpOperationSemantics.StructAutoPropertyField(propertyTarget.Property) is { } backing &&
            (SymbolEqualityComparer.Default.Equals(_context.Target.ContainingType, backing.ContainingType) ||
                TypeInitializationFree?.Invoke(backing.ContainingType) == true))
        {
            field = backing;
            instance = propertyTarget.Instance;
        }
        // A constructor stores into the object it is building.
        if (field == null || instance == null || field.ContainingType is not { } owner ||
            !CSharpOperationSemantics.IsMutableModeledStruct(owner) || !SymbolEqualityComparer.Default.Equals(instance.Type, owner) ||
            _context.Target.MethodKind == MethodKind.Constructor && IsImplicitThis(instance) ||
            Reject(operation, depth) != FrontendAbstention.None || !CanReplaceStructField(owner) ||
            !TryStructStorage(instance, out var storage))
        { return null; }
        if (operation is ISimpleAssignmentOperation simple)
        {
            var right = LowerBodyValue(simple.Value, block, depth + 1);
            if (!right.Classification.IsExact)
            { return right; }
            ReplaceStructField(operation, storage, owner, field, right.Value, right.Continuation);
            return new(right.Value, right.Continuation, FrontendSubsetClassification.Exact);
        }
        // The old value is read before the right-hand side runs.
        var old = Pin(operation, new TotalBodyValue(StructField(operation, owner, field, _factory.Variable(storage), block), block,
            FrontendSubsetClassification.Exact));
        TotalBodyValue next;
        if (operation is IIncrementOrDecrementOperation increment)
        { next = ApplyClassifiedRule(operation, CSharpOperationSemantics.Increment(_factory, increment, old.Value), old.Continuation); }
        else
        {
            var compound = (ICompoundAssignmentOperation)operation;
            var right = LowerBodyValue(compound.Value, old.Continuation, depth + 1);
            if (!right.Classification.IsExact)
            { return Approximate(operation, right.Continuation, right.Classification.Abstention); }
            var rule = CSharpOperationSemantics.Compound(_factory, compound, old.Value, right.Value, _context.Compilation?.Options.CheckOverflow);
            next = ApplyClassifiedRule(operation, rule, right.Continuation);
        }
        if (!next.Classification.IsExact)
        { return next; }
        ReplaceStructField(operation, storage, owner, field, next.Value, next.Continuation);
        return operation is IIncrementOrDecrementOperation { IsPostfix: true }
            ? new(old.Value, next.Continuation, FrontendSubsetClassification.Exact)
            : new(next.Value, next.Continuation, FrontendSubsetClassification.Exact);
    }

    // Every instance field of the struct is a modeled slot, so a copy can
    // hold each of them.
    private bool CanReplaceStructField(INamedTypeSymbol type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        foreach (var member in type.GetMembers())
        {
            Spend?.Invoke();
            if (member is IFieldSymbol { IsStatic: false, IsConst: false } field && FieldMember(field) == null)
            { return false; }
        }
        return true;
    }

    // A field of the struct value `owner`. It is a pure read of the
    // immutable object, approximated where field reads are.
    private IrTerm StructField(IOperation operation, INamedTypeSymbol type, IFieldSymbol field, IrTerm owner, IrBlockId block)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        field = RepositoryParameterGuard.NotNull(field, nameof(field));
        owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var member = FieldMember(field) ?? throw new InvalidOperationException("A modeled struct field has no slot.");
        if (!ApproximateElementReads)
        { return StructGuardedRead(type, member, owner); }
        var value = _context.Temporary(_factory.GetMemberInfo(member).ReturnType);
        _builder!.Havoc(block, _context.Site(operation), IrHavocKind.Variables, IrHavocOrigin.Approximation, value);
        return _factory.Variable(value);
    }

    // Allocates a new value holding the fields `storage` holds now, with
    // `changed` set to `value`, and assigns it to `storage`. The stores write
    // an object nothing else has seen; the assignment is a local write.
    private void ReplaceStructField(IOperation operation, IrVarId storage, INamedTypeSymbol type, IFieldSymbol changed, IrTerm value,
        IrBlockId block)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        RepositoryParameterGuard.ThrowIfInvalid(storage.IsDefault, nameof(storage));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        changed = RepositoryParameterGuard.NotNull(changed, nameof(changed));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var allocated = _context.Type(type);
        _factory.RegisterValueStructType(allocated);
        var current = _context.BridgeValue(_factory.Variable(storage), allocated);
        var target = _context.Temporary(allocated);
        _builder!.Allocate(block, _context.StructUpdateSite(operation), allocated, target);
        var created = _factory.Variable(target);
        foreach (var member in type.GetMembers())
        {
            if (member is not IFieldSymbol { IsStatic: false, IsConst: false } field)
            { continue; }
            Spend?.Invoke();
            var slot = FieldMember(field) ?? throw new InvalidOperationException("A modeled struct field has no slot.");
            IrTerm stored;
            if (SymbolEqualityComparer.Default.Equals(field, changed))
            { stored = _context.BridgeValue(value, _factory.GetMemberInfo(slot).ReturnType); }
            else
            {
                // A copied field is read into a temporary, as any other read
                // is, so the entry value it may name is part of the model.
                var copied = _context.Temporary(_factory.GetMemberInfo(slot).ReturnType);
                _builder!.Assign(block, _context.Site(operation), copied, StructField(operation, type, field, current, block));
                stored = _factory.Variable(copied);
            }
            if (!StoreInitializer(field, created, operation, stored, block, fresh: true))
            { throw new InvalidOperationException("A modeled struct field could not be copied."); }
        }
        var assigned = _builder.Assign(block, _context.Site(operation), storage, _context.BridgeValue(created, _factory.GetVariableInfo(storage).Type));
        _builder.Write(block, assigned.Operation, IrWriteRegion.Local);
    }
}
