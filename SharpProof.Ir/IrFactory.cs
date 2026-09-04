namespace SharpProof.Ir;

public sealed class IrFactory
{
    private static long s_nextScope;
    private readonly object _gate = new();
    private readonly Dictionary<ExternalIdentityBucketKey, ExternalIdentityBucket> _externalIdentityBuckets = [];
    private readonly Dictionary<string, IrStringId> _stringIds = new(StringComparer.Ordinal);
    private readonly List<string> _strings = [];
    private readonly Dictionary<(IrTypeKind Kind, int Identity, int ElementType), IrTypeId> _typeIds = [];
    private readonly List<IrTypeInfo> _types = [];
    private readonly List<IrVariableInfo> _variables = [];
    private readonly Dictionary<StructuralKey, IrMemberId> _memberIds = [];
    private readonly List<IrMemberInfo> _members = [];
    private readonly List<IrOperationInfo> _operations = [];
    private readonly Dictionary<StructuralKey, IrTerm> _termIds = [];
    private readonly List<IrTerm> _terms = [];
    private readonly long _scope;
    private int _identityCount;

    public IrFactory()
    {
        _scope = Interlocked.Increment(ref s_nextScope);
        BooleanType = CreateBuiltInType("bool", IrTypeKind.Boolean);
        IntegerType = CreateBuiltInType("int", IrTypeKind.Integer);
        StringType = CreateBuiltInType("string", IrTypeKind.String);
        ObjectType = CreateBuiltInType("object", IrTypeKind.Reference);
    }

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
                bucket = (ExternalIdentityBucket<T>)existingBucket;
            }
            else
            {
                bucket = new ExternalIdentityBucket<T>();
                _externalIdentityBuckets.Add(bucketKey, bucket);
            }
        }

        var comparedCount = 0;
        while (true)
        {
            ExternalIdentityEntry<T>[] candidates;
            lock (_gate)
            {
                var candidateCount = bucket.Entries.Count - comparedCount;
                candidates = new ExternalIdentityEntry<T>[candidateCount];
                bucket.Entries.CopyTo(
                    comparedCount,
                    candidates,
                    0,
                    candidateCount);
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
                if (bucket.Entries.Count != comparedCount)
                {
                    continue;
                }

                var id = CreateIdentityCore();
                bucket.Entries.Add(new ExternalIdentityEntry<T>(identity, id));
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
            return GetOrCreateTypeCore(identity, displayName, IrTypeKind.Reference, null);
        }
    }

    public IrTypeId GetOrCreateSequenceType(IrTypeId elementType)
    {
        lock (_gate)
        {
            var element = GetTypeInfoCore(elementType, nameof(elementType));
            var key = (IrTypeKind.Sequence, -1, elementType.Value);
            if (_typeIds.TryGetValue(key, out var existing))
            {
                return existing;
            }

            return CreateTypeCore(
                key,
                GetStringCore(element.Name) + "[]",
                IrTypeKind.Sequence,
                elementType);
        }
    }

    public IrTypeId GetOrCreateSequenceType(IrIdentityId identity, IrTypeId elementType, string displayName)
    {
        ValidateName(displayName, nameof(displayName));
        lock (_gate)
        {
            EnsureScope(identity.Scope, nameof(identity));
            GetTypeInfoCore(elementType, nameof(elementType));
            return GetOrCreateTypeCore(identity, displayName, IrTypeKind.Sequence, elementType);
        }
    }

    public IrTypeInfo GetTypeInfo(IrTypeId id)
    {
        lock (_gate)
        {
            return GetTypeInfoCore(id, nameof(id));
        }
    }

    public IrVarId CreateVariable(string name, IrTypeId type)
    {
        ValidateName(name, nameof(name));
        lock (_gate)
        {
            GetTypeInfoCore(type, nameof(type));
            var id = new IrVarId(_scope, _variables.Count);
            _variables.Add(new IrVariableInfo(id, InternStringCore(name), type));
            return id;
        }
    }

    public IrVariableInfo GetVariableInfo(IrVarId id)
    {
        lock (_gate)
        {
            return GetVariableInfoCore(id, nameof(id));
        }
    }

    public IrMemberId GetOrCreateMember(IrIdentityId identity, IrTypeId declaringType, string name, IrTypeId returnType, bool isStatic,
        params IrTypeId[] parameterTypes)
    {
        ArgumentNullGuard.NotNull(parameterTypes, nameof(parameterTypes));
        var parameterBuilder =
            ImmutableArray.CreateBuilder<IrTypeId>(parameterTypes.Length);
        var parameterIdBuilder =
            ImmutableArray.CreateBuilder<int>(parameterTypes.Length);
        foreach (var parameterType in parameterTypes)
        {
            parameterBuilder.Add(parameterType);
            parameterIdBuilder.Add(parameterType.Value);
        }
        var parameters = parameterBuilder.MoveToImmutable();
        var parameterIds = parameterIdBuilder.MoveToImmutable();
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
            _memberIds.Add(key, id);
            _members.Add(new IrMemberInfo(id, identity, declaringType, nameId, returnType, isStatic, parameters));
            return id;
        }
    }

    public IrMemberInfo GetMemberInfo(IrMemberId id)
    {
        lock (_gate)
        {
            return GetMemberInfoCore(id, nameof(id));
        }
    }

    public OperationId CreateOperation(string? description = null)
    {
        lock (_gate)
        {
            var id = new OperationId(_scope, _operations.Count);
            var descriptionId = string.IsNullOrWhiteSpace(description) ? (IrStringId?)null : InternStringCore(description!);
            _operations.Add(new IrOperationInfo(id, descriptionId));
            return id;
        }
    }

    public IrOperationInfo GetOperationInfo(OperationId id)
    {
        lock (_gate)
        {
            return GetOperationInfoCore(id, nameof(id));
        }
    }

    public IrTerm GetTerm(IrId id)
    {
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
        return new(IntegerType, IrValueKind.Integer, value);
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
                static (id, state) => new IrBooleanTerm(id, state.BooleanType, state.value));
        }
    }

    public IrIntegerTerm Integer(long value)
    {
        lock (_gate)
        {
            return Intern(
                new StructuralKey(IrTermKind.Integer, IntegerType.Value, number: value),
                (IntegerType, value),
                static (id, state) => new IrIntegerTerm(id, state.IntegerType, state.value));
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
                static (id, state) => new IrStringTerm(id, state.StringType, state.stringId));
        }
    }

    public IrNullTerm Null(IrTypeId type)
    {
        lock (_gate)
        {
            RequireNullableTypeCore(type, nameof(type));

            return Intern(
                new StructuralKey(IrTermKind.Null, type.Value),
                type,
                static (id, state) => new IrNullTerm(id, state));
        }
    }

    public IrVariableTerm Variable(IrVarId variable)
    {
        lock (_gate)
        {
            var info = GetVariableInfoCore(variable, nameof(variable));
            return Intern(
                new StructuralKey(IrTermKind.Variable, info.Type.Value, variable.Value),
                (info.Type, variable),
                static (id, state) => new IrVariableTerm(id, state.Type, state.variable));
        }
    }

    public IrOpaqueTerm PureOpaque(IrMemberId member, IrTerm? receiver, params IrTerm[] arguments)
    {
        ArgumentNullGuard.NotNull(arguments, nameof(arguments));
        return Opaque(member, receiver, arguments, IrOpaquePurity.Pure, default);
    }

    public IrOpaqueTerm ImpureOpaque(OperationId operation, IrMemberId member, IrTerm? receiver, params IrTerm[] arguments)
    {
        ArgumentNullGuard.NotNull(arguments, nameof(arguments));
        return Opaque(member, receiver, arguments, IrOpaquePurity.Impure, operation);
    }

    public IrTerm Unary(IrUnaryOperator @operator, IrTerm operand)
    {
        ArgumentNullGuard.NotNull(operand, nameof(operand));

        lock (_gate)
        {
            EnsureTermCore(operand, nameof(operand));
            var semantics = IrOperatorCatalog.Get(@operator);
            var expectedType = IrOperatorCatalog.GetBuiltInType(
                this,
                semantics.Operand);
            if (operand.Type != expectedType)
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
                static (id, state) => new IrUnaryTerm(
                    id, state.expectedType, state.@operator, state.operand));
        }
    }

    public IrTerm Binary(IrBinaryOperator @operator, IrTerm left, IrTerm right)
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
            var folded = IrTermServices.FoldBinary(this, @operator, left, right);
            if (folded != null)
            {
                return folded;
            }

            return Intern(
                new StructuralKey(IrTermKind.Binary, resultType.Value, semantics.Key,
                    second: left.Id.Value, third: right.Id.Value),
                (resultType, @operator, left, right),
                static (id, state) => new IrBinaryTerm(
                    id, state.resultType, state.@operator, state.left, state.right));
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
                static (id, state) => new IrConditionalTerm(
                    id, state.Type, state.condition, state.whenTrue, state.whenFalse));
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
                return Null(targetType);
            }

            return Intern(
                new StructuralKey(IrTermKind.Cast, targetType.Value,
                    first: operand.Id.Value),
                (targetType, operand),
                static (id, state) => new IrCastTerm(id, state.targetType, state.operand));
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
                static (id, state) => new IrLengthTerm(id, state.IntegerType, state.value));
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
                static (id, state) => new IrSequenceAccessTerm(
                    id, state.elementType, state.sequence, state.index));
        }
    }

    internal void EnsureTerm(IrTerm term, string parameterName)
    {
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

    private IrOpaqueTerm Opaque(IrMemberId member, IrTerm? receiver, IrTerm[] arguments, IrOpaquePurity purity, OperationId operation)
    {
        ArgumentNullGuard.NotNull(arguments, nameof(arguments));
        var argumentBuilder =
            ImmutableArray.CreateBuilder<IrTerm>(arguments.Length);
        var childIdBuilder =
            ImmutableArray.CreateBuilder<int>(arguments.Length + 1);
        childIdBuilder.Add(receiver?.Id.Value ?? -1);
        foreach (var argument in arguments)
        {
            argumentBuilder.Add(argument);
            childIdBuilder.Add(argument.Id.Value);
        }
        var immutableArguments = argumentBuilder.MoveToImmutable();
        var childIds = childIdBuilder.MoveToImmutable();

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
            if (purity == IrOpaquePurity.Pure)
            {
                if (!operation.IsDefault)
                {
                    throw new ArgumentException(
                    "Pure opaque terms cannot carry an operation identity.", nameof(operation));
                }
            }
            else
            {
                GetOperationInfoCore(operation, nameof(operation));
            }

            return Intern(new StructuralKey(
                    IrTermKind.Opaque, memberInfo.ReturnType.Value, member.Value, PurityKey(purity),
                    operation.IsDefault ? -1 : operation.Value, children: childIds),
                (memberInfo.ReturnType, member, receiver, immutableArguments, purity, operation),
                static (id, state) => new IrOpaqueTerm(
                    id, state.ReturnType, state.member, state.receiver,
                    state.immutableArguments, state.purity, state.operation));
        }
    }

    private static int PurityKey(IrOpaquePurity purity)
    {
        return IrOperatorCatalog.GetPurityKey(purity);
    }

    private static void ValidateName(string? value, string parameterName)
    {
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
        _stringIds.Add(value, id);
        _strings.Add(value);
        return id;
    }

    private string GetStringCore(IrStringId id)
    {
        return GetScoped(id.Scope, id.Value, _strings, nameof(id));
    }

    private IrIdentityId CreateIdentityCore()
    {
        return new(_scope, _identityCount++);
    }

    private IrTypeId CreateBuiltInType(string name, IrTypeKind kind)
    {
        return GetOrCreateTypeCore(CreateIdentityCore(), name, kind, null);
    }

    private IrTypeId GetOrCreateTypeCore(IrIdentityId identity, string name, IrTypeKind kind, IrTypeId? elementType)
    {
        var key = (kind, identity.IsDefault ? -1 : identity.Value, elementType?.Value ?? -1);
        if (_typeIds.TryGetValue(key, out var existing))
        {
            return existing;
        }

        return CreateTypeCore(key, name, kind, elementType);
    }

    private IrTypeId CreateTypeCore(
        (IrTypeKind Kind, int Identity, int ElementType) key,
        string name,
        IrTypeKind kind,
        IrTypeId? elementType)
    {
        var nameId = InternStringCore(name);
        var id = new IrTypeId(_scope, _types.Count);
        _typeIds.Add(key, id);
        _types.Add(new IrTypeInfo(id, nameId, kind, elementType));
        return id;
    }

    private IrTypeInfo GetTypeInfoCore(IrTypeId id, string parameterName)
    {
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

    private IrVariableInfo GetVariableInfoCore(IrVarId id, string name)
    {
        return GetScoped(id.Scope, id.Value, _variables, name);
    }

    private IrMemberInfo GetMemberInfoCore(IrMemberId id, string parameterName)
    {
        return GetScoped(id.Scope, id.Value, _members, parameterName);
    }

    private IrOperationInfo GetOperationInfoCore(OperationId id, string parameterName)
    {
        return GetScoped(id.Scope, id.Value, _operations, parameterName);
    }

    private void EnsureTermCore(IrTerm term, string parameterName)
    {
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
        if (scope != _scope)
        {
            throw new ArgumentException("The identifier belongs to a different IR factory.", parameterName);
        }
    }

    private T Intern<TState, T>(
        StructuralKey key,
        TState state,
        Func<IrId, TState, T> create)
        where T : IrTerm
    {
        if (_termIds.TryGetValue(key, out var existing))
        {
            return (T)existing;
        }

        var id = new IrId(_scope, _terms.Count);
        var term = create(id, state);
        _termIds.Add(key, term);
        _terms.Add(term);
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

    private sealed class ExternalIdentityBucket<T> : ExternalIdentityBucket where T : notnull
    {
        public List<ExternalIdentityEntry<T>> Entries
        {
            get;
        } = [];
    }

    private sealed class ExternalIdentityEntry<T>(T value, IrIdentityId id) where T : notnull
    {
        public T Value
        {
            get;
        } = value;

        public IrIdentityId Id
        {
            get;
        } = id;
    }

    private readonly struct ExternalIdentityBucketKey(
        Type identityType,
        object comparer,
        int valueHashCode) : IEquatable<ExternalIdentityBucketKey>
    {
        private readonly Type _identityType = identityType;
        private readonly object _comparer = comparer;
        private readonly int _valueHashCode = valueHashCode;

        public bool Equals(ExternalIdentityBucketKey other)
        {
            return ReferenceEquals(_identityType, other._identityType) &&
                ReferenceEquals(_comparer, other._comparer) &&
                _valueHashCode == other._valueHashCode;
        }

        public override bool Equals(object? obj)
        {
            return obj is ExternalIdentityBucketKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_identityType);
                hash = hash * 397 ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_comparer);
                return hash * 397 ^ _valueHashCode;
            }
        }
    }

    private readonly struct StructuralKey(
        IrTermKind kind, int type, int first = 0, int second = 0, int third = 0,
        long number = 0, ImmutableArray<int> children = default) : IEquatable<StructuralKey>
    {
        private readonly (IrTermKind Kind, int Type, int First, int Second, int Third, long Number, IntSequenceKey Children) _value =
            (kind, type, first, second, third, number, new(children));
        public bool Equals(StructuralKey other)
        {
            return _value.Equals(other._value);
        }

        public override bool Equals(object? obj)
        {
            return obj is StructuralKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }
    }

    private readonly struct IntSequenceKey(ImmutableArray<int> values) : IEquatable<IntSequenceKey>
    {
        private readonly ImmutableArray<int> _values = values.IsDefault ? [] : values;

        public bool Equals(IntSequenceKey other)
        {
            if (_values.Length != other._values.Length)
            {
                return false;
            }

            for (var index = 0; index < _values.Length; index++)
            {
                if (_values[index] != other._values[index])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            return obj is IntSequenceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 0;
                foreach (var value in _values)
                {
                    hash = hash * 397 ^ value;
                }

                return hash;
            }
        }
    }
}
