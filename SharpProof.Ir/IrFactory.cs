namespace SharpProof.Ir;

public sealed class IrFactory
{
    private static long s_nextScope;
    private readonly object _gate = new();
    private ImmutableDictionary<ExternalIdentityBucketKey, ExternalIdentityBucket> _externalIdentityBuckets = ImmutableDictionary.Create<ExternalIdentityBucketKey, ExternalIdentityBucket>(ExternalIdentityBucketKeyComparer.Instance);
    private ImmutableDictionary<string, IrStringId> _stringIds = ImmutableDictionary.Create<string, IrStringId>(StringComparer.Ordinal);
    private ImmutableList<string> _strings = [];
    private ImmutableDictionary<(IrTypeKind Kind, int Identity, int ElementType, int Width, bool Signed), IrTypeId> _typeIds = ImmutableDictionary<(IrTypeKind Kind, int Identity, int ElementType, int Width, bool Signed), IrTypeId>.Empty;
    private ImmutableList<IrTypeInfo> _types = [];
    private ImmutableHashSet<IrTypeId> _closedSealedReferenceTypes = [];
    private ImmutableList<IrVariableInfo> _variables = [];
    private ImmutableDictionary<StructuralKey, IrMemberId> _memberIds = ImmutableDictionary.Create<StructuralKey, IrMemberId>(StructuralKeyComparer.Instance);
    private ImmutableList<IrMemberInfo> _members = [];
    private ImmutableList<IrOperationInfo> _operations = [];
    private ImmutableDictionary<StructuralKey, IrTerm> _termIds = ImmutableDictionary.Create<StructuralKey, IrTerm>(StructuralKeyComparer.Instance);
    private ImmutableList<IrTerm> _terms = [];
    private ImmutableDictionary<IrTypeId, IrValue> _emptyArrays = ImmutableDictionary<IrTypeId, IrValue>.Empty;
    private readonly long _scope;
    private int _identityCount;

    public IrFactory() : this(IrExecutionSemantics.Legacy) { }

    public IrFactory(IrExecutionSemantics semantics)
    {
        Semantics = ArgumentNullGuard.RequireDefined(semantics, nameof(semantics));
        _scope = Interlocked.Increment(ref s_nextScope);
        BooleanType = CreateBuiltInType("bool", IrBooleanTypeShape.Instance);
        IntegerType = semantics == IrExecutionSemantics.Total
            ? GetOrCreateIntegerType(32, signed: true)
            : CreateBuiltInType("int", IrIntegerTypeShape.Instance);
        StringType = CreateBuiltInType("string", IrNullableTypeShape.String);
        ObjectType = CreateBuiltInType("object", IrNullableTypeShape.Reference);
    }

    public IrExecutionSemantics Semantics { get; }

    public IrTypeId BooleanType
    {
        get;
    }
    public IrTypeId IntegerType
    {
        get;
    }
    public IrTypeId StringType
    {
        get;
    }
    public IrTypeId ObjectType
    {
        get;
    }

    public IrTypeId GetOrCreateIntegerType(int width, bool signed)
    {
        if (width is not (8 or 16 or 32 or 64))
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Integer widths are 8, 16, 32, or 64 bits.");
        }
        lock (_gate)
        {
            var key = (IrTypeKind.Integer, -1, -1, width, signed);
            if (_typeIds.TryGetValue(key, out var existing))
            {
                return existing;
            }
            var name = (width, signed) switch
            {
                (8, true) => "sbyte",
                (8, false) => "byte",
                (16, true) => "short",
                (16, false) => "ushort",
                (32, true) => "int",
                (32, false) => "uint",
                (64, true) => "long",
                _ => "ulong"
            };
            return CreateTypeCore(key, name, IrIntegerTypeShape.Instance, null);
        }
    }

    public IrIdentityId CreateIdentity()
    {
        lock (_gate)
        {
            return CreateIdentityCore();
        }
    }

    public IrIdentityId InternExternalIdentity<T>(T identity, IEqualityComparer<T> comparer) where T : notnull
    {
        ArgumentNullGuard.NotNull(identity, nameof(identity));
        ArgumentNullGuard.NotNull(comparer, nameof(comparer));

        if (identity is string)
        {
            throw new ArgumentException(
            "Semantic identities cannot be interned from strings.", nameof(identity));
        }

        var valueHashCode = comparer.GetHashCode(identity);
        var bucketKey = new ExternalIdentityBucketKey(
            typeof(T),
            comparer,
            valueHashCode);
        ExternalIdentityBucket<T> bucket;
        lock (_gate)
        {
            if (_externalIdentityBuckets.TryGetValue(
                    bucketKey,
                    out var existingBucket))
            {
                bucket = existingBucket as ExternalIdentityBucket<T> ??
                    throw new InvalidOperationException("External identity bucket has an unexpected type.");
            }
            else
            {
                bucket = new ExternalIdentityBucket<T>([]);
                _externalIdentityBuckets = _externalIdentityBuckets.Add(bucketKey, bucket);
            }
        }

        var comparedCount = 0;
        while (true)
        {
            ImmutableArray<ExternalIdentityEntry<T>> candidates;
            lock (_gate)
            {
                bucket = (ExternalIdentityBucket<T>)_externalIdentityBuckets[bucketKey];
                candidates = bucket.Entries.Skip(comparedCount).ToImmutableArray();
            }

            foreach (var candidate in candidates)
            {
                if (comparer.Equals(candidate.Value, identity))
                {
                    return candidate.Id;
                }
            }

            comparedCount += candidates.Length;
            lock (_gate)
            {
                bucket = (ExternalIdentityBucket<T>)_externalIdentityBuckets[bucketKey];
                if (bucket.Entries.Count != comparedCount)
                {
                    continue;
                }

                var id = CreateIdentityCore();
                _externalIdentityBuckets = _externalIdentityBuckets.SetItem(bucketKey,
                    new ExternalIdentityBucket<T>(bucket.Entries.Add(new ExternalIdentityEntry<T>(identity, id))));
                return id;
            }
        }
    }

    public IrStringId InternString(string value)
    {
        ArgumentNullGuard.NotNull(value, nameof(value));

        lock (_gate)
        {
            return InternStringCore(value);
        }
    }

    public string GetString(IrStringId id)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        lock (_gate)
        {
            return GetScoped(id.Scope, id.Value, _strings, nameof(id));
        }
    }

    public IrTypeId GetOrCreateReferenceType(IrIdentityId identity, string displayName)
    {
        ValidateName(displayName, nameof(displayName));
        lock (_gate)
        {
            EnsureScope(identity.Scope, nameof(identity));
            return GetOrCreateTypeCore(identity, displayName, IrNullableTypeShape.Reference, null);
        }
    }

    internal void RegisterClosedSealedReferenceType(IrTypeId type)
    {
        lock (_gate)
        {
            if (Semantics != IrExecutionSemantics.Total || type == ObjectType ||
                GetTypeInfoCore(type, nameof(type)).Kind != IrTypeKind.Reference)
            { throw new ArgumentException("A certificate requires a Total nominal reference type.", nameof(type)); }
            _closedSealedReferenceTypes = _closedSealedReferenceTypes.Add(type);
        }
    }

    internal bool IsClosedSealedReferenceType(IrTypeId type)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        lock (_gate)
        {
            GetTypeInfoCore(type, nameof(type));
            return _closedSealedReferenceTypes.Contains(type);
        }
    }

    public IrTypeId GetOrCreateSequenceType(IrTypeId elementType)
    {
        RepositoryParameterGuard.ThrowIfInvalid(elementType.IsDefault, nameof(elementType));
        lock (_gate)
        {
            var element = GetTypeInfoCore(elementType, nameof(elementType));
            var key = (IrTypeKind.Sequence, -1, elementType.Value, 0, false);
            if (_typeIds.TryGetValue(key, out var existing))
            {
                return existing;
            }

            return CreateTypeCore(
                key,
                GetStringCore(element.Name) + "[]",
                IrNullableTypeShape.Sequence,
                elementType);
        }
    }

    public IrTypeId GetOrCreateSequenceType(IrIdentityId identity, IrTypeId elementType, string displayName)
    {
        RepositoryParameterGuard.ThrowIfInvalid(elementType.IsDefault, nameof(elementType));
        ValidateName(displayName, nameof(displayName));
        lock (_gate)
        {
            EnsureScope(identity.Scope, nameof(identity));
            GetTypeInfoCore(elementType, nameof(elementType));
            return GetOrCreateTypeCore(identity, displayName, IrNullableTypeShape.Sequence, elementType);
        }
    }

    public IrTypeInfo GetTypeInfo(IrTypeId id)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        lock (_gate)
        {
            return GetTypeInfoCore(id, nameof(id));
        }
    }

    public IrVarId CreateVariable(string name, IrTypeId type)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        ValidateName(name, nameof(name));
        lock (_gate)
        {
            GetTypeInfoCore(type, nameof(type));
            var id = new IrVarId(_scope, _variables.Count);
            _variables = _variables.Add(new IrVariableInfo(id, InternStringCore(name), type));
            return id;
        }
    }

    public IrVariableInfo GetVariableInfo(IrVarId id)
    {
        if (id.IsDefault)
        {
            throw new System.ArgumentException("Variable ID is not owned by this factory.", nameof(id));
        }
        lock (_gate)
        {
            return GetVariableInfoCore(id, nameof(id));
        }
    }

    public IrMemberId GetOrCreateMember(IrIdentityId identity /* SPINT001: all values valid - GetOrCreateMemberSnapshot validates the identity scope under the factory gate */, IrTypeId declaringType, string name /* SPINT001: all values valid - GetOrCreateMemberSnapshot validates names after parameter types to preserve guard ordering */, IrTypeId returnType, bool isStatic,
        params IrTypeId[] parameterTypes)
    {
        RepositoryParameterGuard.ThrowIfInvalid(declaringType.IsDefault, nameof(declaringType));
        RepositoryParameterGuard.ThrowIfInvalid(returnType.IsDefault, nameof(returnType));
        ArgumentNullGuard.NotNull(parameterTypes, nameof(parameterTypes));
        return GetOrCreateMemberSnapshot(identity, declaringType, name, returnType, isStatic, ImmutableArray.CreateRange(parameterTypes));
    }

    internal IrMemberId GetOrCreateMemberSnapshot(IrIdentityId identity, IrTypeId declaringType, string name, IrTypeId returnType, bool isStatic,
        ImmutableArray<IrTypeId> parameterTypes)
    {
        RepositoryParameterGuard.ThrowIfInvalid(declaringType.IsDefault, nameof(declaringType));
        RepositoryParameterGuard.ThrowIfInvalid(returnType.IsDefault, nameof(returnType));
        RepositoryParameterGuard.RequireInitialized(parameterTypes.IsDefault, nameof(parameterTypes), "Array must be initialized.");
        var parameters = parameterTypes;
        var parameterIds = ImmutableArray.CreateRange(
            parameters.Select(static parameter => parameter.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(parameter)) : (parameter.Value)));
        ValidateName(name, nameof(name));

        lock (_gate)
        {
            EnsureScope(identity.Scope, nameof(identity));
            GetTypeInfoCore(declaringType, nameof(declaringType));
            GetTypeInfoCore(returnType, nameof(returnType));
            foreach (var parameterType in parameters)
            {
                GetTypeInfoCore(parameterType, nameof(parameterTypes));
            }

            var key = new StructuralKey(
                default, declaringType.Value, identity.Value, returnType.Value, isStatic ? 1 : 0,
                children: parameterIds);
            if (_memberIds.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var nameId = InternStringCore(name);
            var id = new IrMemberId(_scope, _members.Count);
            _memberIds = _memberIds.Add(key, id);
            _members = _members.Add(new IrMemberInfo(id, identity, declaringType, nameId, returnType, isStatic, parameters));
            return id;
        }
    }

    public IrMemberInfo GetMemberInfo(IrMemberId id)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        lock (_gate)
        {
            return GetMemberInfoCore(id, nameof(id));
        }
    }

    public OperationId CreateOperation(string? description = null, IrSourceSpan? sourceSpan = null)
    {
        lock (_gate)
        {
            var id = new OperationId(_scope, _operations.Count);
            var descriptionId = string.IsNullOrWhiteSpace(description) ? (IrStringId?)null : InternStringCore(description!);
            _operations = _operations.Add(new IrOperationInfo(id, descriptionId, sourceSpan));
            return id;
        }
    }

    public IrOperationInfo GetOperationInfo(OperationId id)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        lock (_gate)
        {
            return GetOperationInfoCore(id, nameof(id));
        }
    }

    public IrTerm GetTerm(IrId id)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        lock (_gate)
        {
            return GetScoped(id.Scope, id.Value, _terms, nameof(id));
        }
    }

    public IrValue CreateBooleanValue(bool value)
    {
        return new(BooleanType, IrValueKind.Boolean, value);
    }

    public IrValue CreateIntegerValue(long value)
    {
        return CreateIntegerValue(IntegerType, value);
    }

    public IrValue CreateIntegerValue(IrTypeId type, long value)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        return new(type, IrValueKind.Integer, IrInteger.FromNumber(GetTypeInfo(type), value));
    }

    public IrValue CreateIntegerValue(IrTypeId type, ulong value)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        return new(type, IrValueKind.Integer, IrInteger.FromNumber(GetTypeInfo(type), value));
    }

    public IrValue CreateIntegerValueFromBits(IrTypeId type, ulong bits)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        return new(type, IrValueKind.Integer, IrInteger.FromBits(GetTypeInfo(type), bits));
    }

    public IrValue CreateStringValue(string value)
    {
        ArgumentNullGuard.NotNull(value, nameof(value));
        if (!Utf16WellFormedness.IsWellFormed(value))
        {
            throw new ArgumentException(
                "String values require well-formed UTF-16.",
                nameof(value));
        }

        return new IrValue(StringType, IrValueKind.String, value);
    }

    public IrValue CreateNullValue(IrTypeId type)
    {
        lock (_gate)
        {
            RequireNullableTypeCore(type, nameof(type));

            return new IrValue(type, IrValueKind.Null, null);
        }
    }

    public IrValue CreateReferenceValue(IrTypeId type, object identity)
    {
        ArgumentNullGuard.NotNull(identity, nameof(identity));

        lock (_gate)
        {
            if (GetTypeInfoCore(type, nameof(type)).Kind != IrTypeKind.Reference)
            {
                throw new ArgumentException("Reference values require a reference type.", nameof(type));
            }

            return new IrValue(type, IrValueKind.Reference, identity);
        }
    }

    public IrValue CreateSequenceValue(IrTypeId type, IEnumerable<IrValue> elements)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        ArgumentNullGuard.NotNull(elements, nameof(elements));

        IrTypeId elementType;
        lock (_gate)
        {
            var info = GetTypeInfoCore(type, nameof(type));
            if (info.Kind != IrTypeKind.Sequence || info.ElementType == null)
            {
                throw new ArgumentException("Sequence values require a sequence type.", nameof(type));
            }

            elementType = info.ElementType.Value;
        }

        var values = ImmutableArray.CreateBuilder<IrValue>();
        foreach (var value in elements)
        {
            if (value == null || value.Type != elementType)
            {
                throw new ArgumentException(
                    "Every sequence element must match the sequence element type.",
                    nameof(elements));
            }

            values.Add(value);
        }

        return new IrValue(
            type,
            IrValueKind.Sequence,
            values.ToImmutable());
    }

    public IrBooleanTerm Boolean(bool value)
    {
        lock (_gate)
        {
            return Intern(
                new StructuralKey(IrTermKind.Boolean, BooleanType.Value, value ? 1 : 0),
                (BooleanType, value),
                static (id, state) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (new IrBooleanTerm(id, state.BooleanType, state.value)));
        }
    }

    public IrIntegerTerm Integer(long value)
    {
        return Integer(IntegerType, value);
    }

    public IrIntegerTerm Integer(IrTypeId type, long value)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        return IntegerCore(type, IrInteger.FromNumber(GetTypeInfo(type), value));
    }

    public IrIntegerTerm Integer(IrTypeId type, ulong value)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        return IntegerCore(type, IrInteger.FromNumber(GetTypeInfo(type), value));
    }

    public IrIntegerTerm IntegerBits(IrTypeId type, ulong bits)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        return IntegerCore(type, IrInteger.FromBits(GetTypeInfo(type), bits));
    }

    private IrIntegerTerm IntegerCore(IrTypeId type, IrInteger value /* SPINT001: all values valid - every integer payload, including default zero, is representable */)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        lock (_gate)
        {
            return Intern(
                new StructuralKey(IrTermKind.Integer, type.Value, number: unchecked((long)value.Bits)),
                (type, value),
                static (id, state) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (new IrIntegerTerm(id, state.type, state.value)));
        }
    }

    public IrStringTerm String(string value)
    {
        ArgumentNullGuard.NotNull(value, nameof(value));
        if (!Utf16WellFormedness.IsWellFormed(value))
        {
            throw new ArgumentException(
                "String terms require well-formed UTF-16.",
                nameof(value));
        }

        lock (_gate)
        {
            var stringId = InternStringCore(value);
            return Intern(
                new StructuralKey(IrTermKind.String, StringType.Value, stringId.Value),
                (StringType, stringId),
                static (id, state) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (new IrStringTerm(id, state.StringType, state.stringId)));
        }
    }

    public IrNullTerm Null(IrTypeId type)
    {
        lock (_gate)
        {
            RequireNullableTypeCore(type, nameof(type));
            return NullCore(type);
        }
    }

    public IrEmptyArrayTerm EmptyArray(IrTypeId type)
    {
        lock (_gate)
        {
            if (Semantics != IrExecutionSemantics.Total || GetTypeInfo(type).Kind != IrTypeKind.Sequence)
            { throw new ArgumentException("An empty array requires a Total sequence type.", nameof(type)); }
            return Intern(new StructuralKey(IrTermKind.EmptyArray, type.Value), type,
                static (id, valueType) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : valueType.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(valueType)) : (new IrEmptyArrayTerm(id, valueType)));
        }
    }

    public IrValue CreateEmptyArrayValue(IrTypeId type)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        lock (_gate)
        {
            EmptyArray(type);
            if (!_emptyArrays.TryGetValue(type, out var value))
            { _emptyArrays = _emptyArrays.Add(type, value = CreateSequenceValue(type, Array.Empty<IrValue>())); }
            return value;
        }
    }

    public IrVariableTerm Variable(IrVarId variable)
    {
        RepositoryParameterGuard.ThrowIfInvalid(variable.IsDefault, nameof(variable));
        lock (_gate)
        {
            var info = GetVariableInfoCore(variable, nameof(variable));
            return Intern(
                new StructuralKey(IrTermKind.Variable, info.Type.Value, variable.Value),
                (info.Type, variable),
                static (id, state) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (new IrVariableTerm(id, state.Type, state.variable)));
        }
    }

    public IrOpaqueTerm PureOpaque(IrMemberId member, IrTerm? receiver, params IrTerm[] arguments)
    {
        RepositoryParameterGuard.ThrowIfInvalid(member.IsDefault, nameof(member));
        ArgumentNullGuard.NotNull(arguments, nameof(arguments));
        return Opaque(member, receiver, ImmutableArray.CreateRange(arguments), IrOpaquePurity.Pure, default);
    }

    public IrOpaqueTerm ImpureOpaque(OperationId operation, IrMemberId member, IrTerm? receiver, params IrTerm[] arguments)
    {
        RepositoryParameterGuard.ThrowIfInvalid(member.IsDefault, nameof(member));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        ArgumentNullGuard.NotNull(arguments, nameof(arguments));
        return Opaque(member, receiver, ImmutableArray.CreateRange(arguments), IrOpaquePurity.Impure, operation);
    }

    public IrTerm Unary(IrUnaryOperator @operator, IrTerm operand)
    {
        ArgumentNullGuard.NotNull(operand, nameof(operand));

        lock (_gate)
        {
            EnsureTermCore(operand, nameof(operand));
            var semantics = IrOperatorCatalog.Get(@operator);
            var expectedType = operand.Type;
            if (GetTypeInfoCore(operand.Type, nameof(operand)).Kind != semantics.Operand)
            {
                throw new ArgumentException("The operand type is not valid for the unary operator.", nameof(operand));
            }

            var folded = IrTermServices.FoldUnary(this, @operator, operand);
            if (folded != null)
            {
                return folded;
            }

            return Intern(
                new StructuralKey(IrTermKind.Unary, expectedType.Value, semantics.Key,
                    second: operand.Id.Value),
                (expectedType, @operator, operand),
                static (id, state) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (new IrUnaryTerm(
                    id, state.expectedType, state.@operator, state.operand)));
        }
    }

    public IrTerm Binary(IrBinaryOperator @operator, IrTerm left, IrTerm right)
    {
        left = RepositoryParameterGuard.NotNull(left, nameof(left));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        return Binary(@operator, left, right, foldStringConcat: true);
    }

    internal IrTerm RewriteBinary(IrBinaryOperator @operator, IrTerm left, IrTerm right)
    {
        left = RepositoryParameterGuard.NotNull(left, nameof(left));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        return Binary(@operator, left, right, foldStringConcat: false);
    }

    private IrTerm Binary(IrBinaryOperator @operator, IrTerm left, IrTerm right, bool foldStringConcat)
    {
        ArgumentNullGuard.NotNull(left, nameof(left));
        ArgumentNullGuard.NotNull(right, nameof(right));

        lock (_gate)
        {
            EnsureTermCore(left, nameof(left));
            EnsureTermCore(right, nameof(right));
            var semantics = IrOperatorCatalog.Get(@operator);
            var resultType = IrTermServices.ValidateBinaryAndGetResultType(
                this,
                @operator,
                semantics.Operand,
                semantics.Result,
                left,
                right);
            var folded = foldStringConcat || @operator != IrBinaryOperator.StringConcat
                ? IrTermServices.FoldBinary(this, @operator, left, right)
                : null;
            if (folded != null)
            {
                return folded;
            }

            return Intern(
                new StructuralKey(IrTermKind.Binary, resultType.Value, semantics.Key,
                    second: left.Id.Value, third: right.Id.Value),
                (resultType, @operator, left, right),
                static (id, state) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (new IrBinaryTerm(
                    id, state.resultType, state.@operator, state.left, state.right)));
        }
    }

    public IrTerm Conditional(IrTerm condition, IrTerm whenTrue, IrTerm whenFalse)
    {
        ArgumentNullGuard.NotNull(condition, nameof(condition));
        ArgumentNullGuard.NotNull(whenTrue, nameof(whenTrue));
        ArgumentNullGuard.NotNull(whenFalse, nameof(whenFalse));

        lock (_gate)
        {
            EnsureTermCore(condition, nameof(condition));
            EnsureTermCore(whenTrue, nameof(whenTrue));
            EnsureTermCore(whenFalse, nameof(whenFalse));
            if (condition.Type != BooleanType)
            {
                throw new ArgumentException("The conditional guard must be boolean.", nameof(condition));
            }

            if (whenTrue.Type != whenFalse.Type)
            {
                throw new ArgumentException(
                "Conditional branches must have the same type.", nameof(whenFalse));
            }

            if (condition is IrBooleanTerm literal)
            {
                return literal.Value ? whenTrue : whenFalse;
            }

            return Intern(new StructuralKey(
                    IrTermKind.Conditional, whenTrue.Type.Value,
                    first: condition.Id.Value, second: whenTrue.Id.Value,
                    third: whenFalse.Id.Value),
                (whenTrue.Type, condition, whenTrue, whenFalse),
                static (id, state) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (new IrConditionalTerm(
                    id, state.Type, state.condition, state.whenTrue, state.whenFalse)));
        }
    }

    public IrTerm Cast(IrTypeId targetType, IrTerm operand)
    {
        ArgumentNullGuard.NotNull(operand, nameof(operand));

        lock (_gate)
        {
            var target = GetTypeInfoCore(targetType, nameof(targetType));
            EnsureTermCore(operand, nameof(operand));
            if (operand.Type == targetType)
            {
                return operand;
            }

            var source = GetTypeInfoCore(operand.Type, nameof(operand));
            if (source.Kind == IrTypeKind.Integer && target.Kind == IrTypeKind.Integer &&
                source.Width != 0 && target.Width != 0)
            {
                if (operand is IrIntegerTerm integer)
                {
                    return IntegerBits(targetType, integer.Integer.ConvertBits(target.Width));
                }
                return Intern(
                    new StructuralKey(IrTermKind.Cast, targetType.Value, first: operand.Id.Value),
                    (targetType, operand),
                    static (id, state) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (new IrCastTerm(id, state.targetType, state.operand)));
            }
            var isUnboxing =
                source.Kind == IrTypeKind.Reference &&
                target.Kind is IrTypeKind.Boolean or IrTypeKind.Integer;
            if (!IrOperatorCatalog.IsNullable(source.Kind))
            {
                throw new ArgumentException(
                    "Non-identity casts require a string, reference, or sequence operand.",
                    nameof(operand));
            }

            if (!IrOperatorCatalog.IsNullable(target.Kind) && !isUnboxing)
            {
                throw new ArgumentException(
                    "Non-identity casts require a reference-like target or " +
                    "a boolean or integer unboxing target.",
                    nameof(targetType));
            }

            if (operand is IrNullTerm && IrOperatorCatalog.IsNullable(target.Kind))
            {
                return NullCore(targetType);
            }

            return Intern(
                new StructuralKey(IrTermKind.Cast, targetType.Value,
                    first: operand.Id.Value),
                (targetType, operand),
                static (id, state) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (new IrCastTerm(id, state.targetType, state.operand)));
        }
    }

    public IrTerm Length(IrTerm value)
    {
        ArgumentNullGuard.NotNull(value, nameof(value));

        lock (_gate)
        {
            EnsureTermCore(value, nameof(value));
            var info = GetTypeInfoCore(value.Type, nameof(value));
            if (info.Kind is not (IrTypeKind.String or IrTypeKind.Sequence))
            {
                throw new ArgumentException(
                "Length requires a string or sequence value.", nameof(value));
            }

            if (value is IrStringTerm text)
            {
                return Integer(GetStringCore(text.Value).Length);
            }

            return Intern(
                new StructuralKey(IrTermKind.Length, IntegerType.Value,
                    first: value.Id.Value),
                (value, IntegerType),
                static (id, state) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (new IrLengthTerm(id, state.IntegerType, state.value)));
        }
    }

    public IrTerm SequenceAccess(IrTerm sequence, IrTerm index)
    {
        ArgumentNullGuard.NotNull(sequence, nameof(sequence));
        ArgumentNullGuard.NotNull(index, nameof(index));

        lock (_gate)
        {
            var elementType = IrTermServices.ValidateSequenceTerms(
                this,
                sequence,
                index,
                "Sequence access requires a sequence value.",
                "Sequence access requires an integer index.",
                nameof(sequence),
                nameof(index));
            return Intern(
                new StructuralKey(IrTermKind.SequenceAccess, elementType.Value,
                    first: sequence.Id.Value, second: index.Id.Value),
                (elementType, sequence, index),
                static (id, state) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (new IrSequenceAccessTerm(
                    id, state.elementType, state.sequence, state.index)));
        }
    }

    internal void EnsureTerm(IrTerm term, string parameterName)
    {
        ArgumentNullGuard.NotNull(term, parameterName);
        lock (_gate)
        {
            EnsureTermCore(term, parameterName);
        }
    }

    internal static IrTerm RequireBooleanTerm(
        IrFactory factory,
        IrTerm? term,
        string parameterName,
        string message = "A Boolean IR term is required.")
    {
        message = RepositoryParameterGuard.NotNull(message, nameof(message));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        term = ArgumentNullGuard.NotNull(term, parameterName);

        factory.EnsureTerm(term, parameterName);
        if (term.Type != factory.BooleanType)
        {
            throw new ArgumentException(message, parameterName);
        }

        return term;
    }

    internal IrTypeId ValidateSequenceTerms(
        IrTerm sequence, IrTerm index, string sequenceMessage, string indexMessage,
        string sequenceParameter, string indexParameter)
    {
        lock (_gate)
        {
            return IrTermServices.ValidateSequenceTerms(
                this,
                sequence,
                index,
                sequenceMessage,
                indexMessage,
                sequenceParameter,
                indexParameter);
        }
    }

    internal void ValidateCallShape(
        IrMemberInfo member, IrTerm? receiver, IReadOnlyList<IrTerm> arguments,
        string parameterName)
    {
        ArgumentNullGuard.NotNull(member, nameof(member));
        ArgumentNullGuard.NotNull(arguments, nameof(arguments));
        lock (_gate)
        {
            IrTermServices.ValidateCallShape(
                this,
                member,
                receiver,
                arguments,
                parameterName,
                opaque: false);
        }
    }

    internal IrOpaqueTerm RebuildOpaque(IrMemberId member, IrTerm? receiver, ImmutableArray<IrTerm> arguments, IrOpaquePurity purity, OperationId operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(member.IsDefault, nameof(member));
        RepositoryParameterGuard.RequireInitialized(arguments.IsDefault, nameof(arguments), "Array must be initialized.");
        var normalizedPurity = purity == IrOpaquePurity.Pure ? IrOpaquePurity.Pure : IrOpaquePurity.Impure;
        RepositoryParameterGuard.ThrowIfInvalid(normalizedPurity == IrOpaquePurity.Impure && operation.IsDefault, nameof(operation));
        return Opaque(member, receiver, arguments, normalizedPurity, normalizedPurity == IrOpaquePurity.Pure ? default : operation);
    }

    private IrOpaqueTerm Opaque(IrMemberId member, IrTerm? receiver, ImmutableArray<IrTerm> arguments, IrOpaquePurity purity, OperationId operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(member.IsDefault, nameof(member));
        // Pure opaque terms use the default operation ID as an intentional sentinel.
        RepositoryParameterGuard.ThrowIfInvalid(purity == IrOpaquePurity.Impure && operation.IsDefault, nameof(operation));
        RepositoryParameterGuard.RequireInitialized(arguments.IsDefault, nameof(arguments), "Array must be initialized.");
        var immutableArguments = arguments;

        lock (_gate)
        {
            var memberInfo = GetMemberInfoCore(member, nameof(member));
            IrTermServices.ValidateCallShape(
                this,
                memberInfo,
                receiver,
                immutableArguments,
                nameof(arguments),
                opaque: true);
            if (purity != IrOpaquePurity.Pure)
            {
                GetOperationInfoCore(operation, nameof(operation));
            }
            var childIds = ImmutableArray.Create(receiver?.Id.Value ?? -1).AddRange(
                immutableArguments.Select(static (IrTerm /* SPINT001: all values valid - call-shape validation rejects null arguments before interning */ argument) => argument.Id.Value));

            return Intern(new StructuralKey(
                    IrTermKind.Opaque, memberInfo.ReturnType.Value, member.Value,
                    IrOperatorCatalog.GetPurityKey(purity),
                    operation.IsDefault ? -1 : operation.Value,
                    children: childIds),
                (memberInfo.ReturnType, member, receiver, immutableArguments, purity, operation),
                static (id, state) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (new IrOpaqueTerm(
                    id, state.ReturnType, state.member, state.receiver,
                    state.immutableArguments, state.purity, state.operation)));
        }
    }

    private static void ValidateName(string? value, string parameterName)
    {
        parameterName = RepositoryParameterGuard.NotNull(parameterName, nameof(parameterName));
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty name is required.", parameterName);
        }
    }

    private IrStringId InternStringCore(string value)
    {
        if (_stringIds.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var id = new IrStringId(_scope, _strings.Count);
        _stringIds = _stringIds.Add(value, id);
        _strings = _strings.Add(value);
        return id;
    }

    private string GetStringCore(IrStringId id)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        return GetScoped(id.Scope, id.Value, _strings, nameof(id));
    }

    private IrIdentityId CreateIdentityCore()
    {
        return new(_scope, _identityCount++);
    }

    private IrTypeId CreateBuiltInType(string name, IrTypeShape shape)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        shape = RepositoryParameterGuard.NotNull(shape, nameof(shape));
        return GetOrCreateTypeCore(CreateIdentityCore(), name, shape, null);
    }

    private IrTypeId GetOrCreateTypeCore(IrIdentityId identity, string name, IrTypeShape shape, IrTypeId? elementType)
    {
        RepositoryParameterGuard.ThrowIfInvalid(identity.IsDefault, nameof(identity));
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        shape = RepositoryParameterGuard.NotNull(shape, nameof(shape));
        var key = (shape.Kind, identity.IsDefault ? -1 : identity.Value, elementType?.Value ?? -1,
            0, shape.Kind == IrTypeKind.Integer);
        if (_typeIds.TryGetValue(key, out var existing))
        {
            return existing;
        }

        return CreateTypeCore(key, name, shape, elementType);
    }

    private IrTypeId CreateTypeCore(
        (IrTypeKind Kind, int Identity, int ElementType, int Width, bool Signed) key,
        string name,
        IrTypeShape shape,
        IrTypeId? elementType)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        shape = RepositoryParameterGuard.NotNull(shape, nameof(shape));
        var nameId = InternStringCore(name);
        var id = new IrTypeId(_scope, _types.Count);
        _typeIds = _typeIds.Add(key, id);
        _types = _types.Add(new IrTypeInfo(id, nameId, shape, elementType, key.Width, key.Signed));
        return id;
    }

    private IrTypeInfo GetTypeInfoCore(IrTypeId id, string parameterName)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        parameterName = RepositoryParameterGuard.NotNull(parameterName, nameof(parameterName));
        return GetScoped(id.Scope, id.Value, _types, parameterName);
    }

    private void RequireNullableTypeCore(IrTypeId type, string parameterName)
    {
        if (!IrOperatorCatalog.IsNullable(GetTypeInfoCore(type, parameterName).Kind))
        {
            throw new ArgumentException(
                "Null requires a string, reference, or sequence type.", parameterName);
        }
    }

    private IrNullTerm NullCore(IrTypeId type)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        return Intern(
            new StructuralKey(IrTermKind.Null, type.Value),
            type,
            static (id, state) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : state.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(state)) : (new IrNullTerm(id, state)));
    }

    private IrVariableInfo GetVariableInfoCore(IrVarId id, string name)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        return GetScoped(id.Scope, id.Value, _variables, name);
    }

    private IrMemberInfo GetMemberInfoCore(IrMemberId id, string parameterName)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        parameterName = RepositoryParameterGuard.NotNull(parameterName, nameof(parameterName));
        return GetScoped(id.Scope, id.Value, _members, parameterName);
    }

    private IrOperationInfo GetOperationInfoCore(OperationId id, string parameterName)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        parameterName = RepositoryParameterGuard.NotNull(parameterName, nameof(parameterName));
        return GetScoped(id.Scope, id.Value, _operations, parameterName);
    }

    private void EnsureTermCore(IrTerm term, string parameterName)
    {
        parameterName = RepositoryParameterGuard.NotNull(parameterName, nameof(parameterName));
        if (term.Id.Scope != _scope ||
            term.Id.Value < 0 ||
            term.Id.Value >= _terms.Count ||
            !ReferenceEquals(_terms[term.Id.Value], term))
        {
            throw new ArgumentException("The term belongs to a different IR factory.", parameterName);
        }
    }

    private void EnsureScope(long scope, string parameterName)
    {
        parameterName = RepositoryParameterGuard.NotNull(parameterName, nameof(parameterName));
        if (scope != _scope)
        {
            throw new ArgumentException("The identifier belongs to a different IR factory.", parameterName);
        }
    }

    private T Intern<TState, T>(
        StructuralKey key,
        TState state /* SPINT001: all values valid - opaque caller state is forwarded unchanged and permits every value */,
        Func<IrId, TState, T> create)
        where T : IrTerm
    {
        create = RepositoryParameterGuard.NotNull(create, nameof(create));
        if (_termIds.TryGetValue(key, out var existing))
        {
            return existing as T ??
                throw new InvalidOperationException("Interned term has an unexpected type.");
        }

        var id = new IrId(_scope, _terms.Count);
        var term = create(id, state);
        _termIds = _termIds.Add(key, term);
        _terms = _terms.Add(term);
        return term;
    }

    private T GetScoped<T>(long scope, int value, IReadOnlyList<T> items, string parameterName)
    {
        EnsureScope(scope, parameterName);
        return items[ArgumentNullGuard.RequireIndex(
            value,
            items.Count,
            parameterName)];
    }

    private abstract class ExternalIdentityBucket;

    private sealed class ExternalIdentityBucket<T>(ImmutableList<ExternalIdentityEntry<T>> entries) : ExternalIdentityBucket where T : notnull
    {
        public ImmutableList<ExternalIdentityEntry<T>> Entries { get; } = ArgumentNullGuard.NotNull(entries, nameof(entries));
    }

    private sealed class ExternalIdentityEntry<T>(T value, IrIdentityId id) where T : notnull
    {
        public T Value
        {
            get;
        } = ArgumentNullGuard.NotNull(value, nameof(value));

        public IrIdentityId Id
        {
            get;
        } = (id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : id);
    }

    // Dictionary-owned comparers keep all key equality and hashing on the
    // same typed path. These private keys never cross an object boundary.
    private readonly struct ExternalIdentityBucketKey(
        Type identityType, object comparer, int valueHashCode)
    {
        internal Type IdentityType { get; } = (identityType ?? throw new System.ArgumentNullException(nameof(identityType)));
        internal object Comparer { get; } = (comparer ?? throw new System.ArgumentNullException(nameof(comparer)));
        internal int ValueHashCode { get; } = valueHashCode;
    }

    private sealed class ExternalIdentityBucketKeyComparer : IEqualityComparer<ExternalIdentityBucketKey>
    {
        internal static ExternalIdentityBucketKeyComparer Instance { get; } = new();

        public bool Equals(ExternalIdentityBucketKey left, ExternalIdentityBucketKey right)
        {
            return ReferenceEquals(left.IdentityType, right.IdentityType) &&
                ReferenceEquals(left.Comparer, right.Comparer) &&
                left.ValueHashCode == right.ValueHashCode;
        }

        public int GetHashCode(ExternalIdentityBucketKey key /* SPINT001: all values valid - all key fields, including null defaults, support identity hashing */)
        {
            unchecked
            {
                var hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.IdentityType);
                hash = hash * 397 ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.Comparer);
                return hash * 397 ^ key.ValueHashCode;
            }
        }
    }

    private readonly struct StructuralKey(
        IrTermKind kind, int type, int first = 0, int second = 0, int third = 0,
        long number = 0, ImmutableArray<int> /* SPINT001: all values valid - default is normalized to an empty child list */ children = default)
    {
        internal (IrTermKind Kind, int Type, int First, int Second, int Third, long Number) Prefix { get; } =
            (kind, type, first, second, third, number);
        internal ImmutableArray<int> Children { get; } = children.IsDefault ? [] : children;
    }

    private sealed class StructuralKeyComparer : IEqualityComparer<StructuralKey>
    {
        internal static StructuralKeyComparer Instance { get; } = new();

        public bool Equals(StructuralKey left, StructuralKey right)
        {
            return left.Prefix.Equals(right.Prefix) &&
                IntSequenceComparer.Instance.Equals(left.Children, right.Children);
        }

        public int GetHashCode(StructuralKey key)
        {
            RepositoryParameterGuard.RequireInitialized(key.Children.IsDefault, nameof(key), "Value must be initialized.");

            // Preserve the original seven-element tuple hash: the sequence
            // comparer returns exactly the hash of the former sequence key.
            return (key.Prefix.Kind, key.Prefix.Type, key.Prefix.First,
                key.Prefix.Second, key.Prefix.Third, key.Prefix.Number,
                IntSequenceComparer.Instance.GetHashCode(key.Children)).GetHashCode();
        }
    }

    private sealed class IntSequenceComparer : IEqualityComparer<ImmutableArray<int>>
    {
        internal static IntSequenceComparer Instance { get; } = new();

        public bool Equals(ImmutableArray<int> left, ImmutableArray<int> right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(ImmutableArray<int> values)
        {
            RepositoryParameterGuard.RequireInitialized(values.IsDefault, nameof(values), "Values must be initialized.");

            unchecked
            {
                var hash = 0;
                foreach (var value in values)
                {
                    hash = hash * 397 ^ value;
                }

                return hash;
            }
        }
    }
}
