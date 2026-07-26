namespace SharpProof.Ir;

public sealed class IrFactory {
    private static long s_nextScope;
    private readonly object _gate = new();
    private readonly Dictionary<ExternalIdentityKey, IrIdentityId>
        _externalIdentityIds = [];
    private readonly Dictionary<string, IrStringId> _stringIds = new(StringComparer.Ordinal);
    private readonly List<string> _strings = [];
    private readonly Dictionary<TypeKey, IrTypeId> _typeIds = [];
    private readonly List<IrTypeInfo> _types = [];
    private readonly List<IrVariableInfo> _variables = [];
    private readonly Dictionary<MemberKey, IrMemberId> _memberIds = [];
    private readonly List<IrMemberInfo> _members = [];
    private readonly List<IrOperationInfo> _operations = [];
    private readonly Dictionary<TermKey, IrTerm> _termIds = [];
    private readonly List<IrTerm> _terms = [];
    private int _identityCount;
    private readonly long _scope;

    public IrFactory() {
        _scope = Interlocked.Increment(ref s_nextScope);
        BooleanType = GetOrCreateTypeCore(
            CreateIdentityCore(),
            "bool",
            IrTypeKind.Boolean,
            null);
        IntegerType = GetOrCreateTypeCore(
            CreateIdentityCore(),
            "int",
            IrTypeKind.Integer,
            null);
        StringType = GetOrCreateTypeCore(
            CreateIdentityCore(),
            "string",
            IrTypeKind.String,
            null);
        ObjectType = GetOrCreateTypeCore(
            CreateIdentityCore(),
            "object",
            IrTypeKind.Reference,
            null);
    }

    public IrTypeId BooleanType { get; }
    public IrTypeId IntegerType { get; }
    public IrTypeId StringType { get; }
    public IrTypeId ObjectType { get; }

    public IrIdentityId CreateIdentity() {
        lock (_gate) return CreateIdentityCore();
    }

    public IrIdentityId InternExternalIdentity<T>(
        T identity,
        IEqualityComparer<T> comparer)
        where T : notnull {
        if (identity == null) throw new ArgumentNullException(nameof(identity));
        if (comparer == null) throw new ArgumentNullException(nameof(comparer));
        if (typeof(T) == typeof(string))
            throw new ArgumentException(
                "Semantic identities cannot be interned from strings.",
                nameof(identity));
        lock (_gate) {
            var key = new ExternalIdentityKey<T>(identity, comparer);
            if (_externalIdentityIds.TryGetValue(key, out var existing))
                return existing;
            var id = CreateIdentityCore();
            _externalIdentityIds.Add(key, id);
            return id;
        }
    }

    public IrStringId InternString(string value) {
        if (value == null) throw new ArgumentNullException(nameof(value));
        lock (_gate) return InternStringCore(value);
    }

    public string GetString(IrStringId id) {
        lock (_gate) {
            EnsureScope(id.Scope, nameof(id));
            return GetAt(_strings, id.Value, nameof(id));
        }
    }

    public IrTypeId GetOrCreateReferenceType(
        IrIdentityId identity,
        string displayName) {
        ValidateName(displayName, nameof(displayName));
        lock (_gate) {
            EnsureScope(identity.Scope, nameof(identity));
            return GetOrCreateTypeCore(
                identity,
                displayName,
                IrTypeKind.Reference,
                null);
        }
    }

    public IrTypeId GetOrCreateSequenceType(IrTypeId elementType) {
        lock (_gate) {
            var element = GetTypeInfoCore(elementType, nameof(elementType));
            var key = new TypeKey(
                IrTypeKind.Sequence,
                identity: -1,
                elementType.Value);
            if (_typeIds.TryGetValue(key, out var existing)) return existing;
            return GetOrCreateTypeCore(
                default,
                GetStringCore(element.Name) + "[]",
                IrTypeKind.Sequence,
                elementType);
        }
    }

    public IrTypeId GetOrCreateSequenceType(
        IrIdentityId identity,
        IrTypeId elementType,
        string displayName) {
        ValidateName(displayName, nameof(displayName));
        lock (_gate) {
            EnsureScope(identity.Scope, nameof(identity));
            GetTypeInfoCore(elementType, nameof(elementType));
            return GetOrCreateTypeCore(
                identity,
                displayName,
                IrTypeKind.Sequence,
                elementType);
        }
    }

    public IrTypeInfo GetTypeInfo(IrTypeId id) {
        lock (_gate) return GetTypeInfoCore(id, nameof(id));
    }

    public IrVarId CreateVariable(string name, IrTypeId type) {
        ValidateName(name, nameof(name));
        lock (_gate) {
            GetTypeInfoCore(type, nameof(type));
            var id = new IrVarId(_scope, _variables.Count);
            _variables.Add(new IrVariableInfo(id, InternStringCore(name), type));
            return id;
        }
    }

    public IrVariableInfo GetVariableInfo(IrVarId id) {
        lock (_gate) {
            EnsureScope(id.Scope, nameof(id));
            return GetAt(_variables, id.Value, nameof(id));
        }
    }

    public IrMemberId GetOrCreateMember(
        IrIdentityId identity,
        IrTypeId declaringType,
        string name,
        IrTypeId returnType,
        bool isStatic,
        params IrTypeId[] parameterTypes) {
        ValidateName(name, nameof(name));
        if (parameterTypes == null) throw new ArgumentNullException(nameof(parameterTypes));
        lock (_gate) {
            EnsureScope(identity.Scope, nameof(identity));
            GetTypeInfoCore(declaringType, nameof(declaringType));
            GetTypeInfoCore(returnType, nameof(returnType));
            foreach (var parameterType in parameterTypes)
                GetTypeInfoCore(parameterType, nameof(parameterTypes));
            var nameId = InternStringCore(name);
            var parameters = parameterTypes.ToImmutableArray();
            var key = new MemberKey(
                identity.Value,
                declaringType.Value,
                returnType.Value,
                isStatic,
                [.. parameters.Select(static value => value.Value)]);
            if (_memberIds.TryGetValue(key, out var existing)) return existing;
            var id = new IrMemberId(_scope, _members.Count);
            _memberIds.Add(key, id);
            _members.Add(new IrMemberInfo(
                id,
                identity,
                declaringType,
                nameId,
                returnType,
                isStatic,
                parameters));
            return id;
        }
    }

    public IrMemberInfo GetMemberInfo(IrMemberId id) {
        lock (_gate) {
            EnsureScope(id.Scope, nameof(id));
            return GetAt(_members, id.Value, nameof(id));
        }
    }

    public OperationId CreateOperation(string? description = null) {
        lock (_gate) {
            var id = new OperationId(_scope, _operations.Count);
            var descriptionId = string.IsNullOrWhiteSpace(description)
                ? (IrStringId?)null
                : InternStringCore(description!);
            _operations.Add(new IrOperationInfo(id, descriptionId));
            return id;
        }
    }

    public IrOperationInfo GetOperationInfo(OperationId id) {
        lock (_gate) {
            EnsureScope(id.Scope, nameof(id));
            return GetAt(_operations, id.Value, nameof(id));
        }
    }

    public IrTerm GetTerm(IrId id) {
        lock (_gate) {
            EnsureScope(id.Scope, nameof(id));
            return GetAt(_terms, id.Value, nameof(id));
        }
    }

    public IrValue CreateBooleanValue(bool value) => IrValue.CreateBoolean(BooleanType, value);

    public IrValue CreateIntegerValue(long value) => IrValue.CreateInteger(IntegerType, value);

    public IrValue CreateStringValue(string value) {
        if (value == null) throw new ArgumentNullException(nameof(value));
        return IrValue.CreateString(StringType, value);
    }

    public IrValue CreateNullValue(IrTypeId type) {
        lock (_gate) {
            var info = GetTypeInfoCore(type, nameof(type));
            if (!IsNullable(info.Kind))
                throw new ArgumentException("Null requires a string, reference, or sequence type.", nameof(type));
            return IrValue.CreateNull(type);
        }
    }

    public IrValue CreateReferenceValue(IrTypeId type, object identity) {
        if (identity == null) throw new ArgumentNullException(nameof(identity));
        lock (_gate) {
            var info = GetTypeInfoCore(type, nameof(type));
            if (info.Kind != IrTypeKind.Reference)
                throw new ArgumentException("Reference values require a reference type.", nameof(type));
            return IrValue.CreateReference(type, identity);
        }
    }

    public IrValue CreateSequenceValue(IrTypeId type, IEnumerable<IrValue> elements) {
        if (elements == null) throw new ArgumentNullException(nameof(elements));
        lock (_gate) {
            var info = GetTypeInfoCore(type, nameof(type));
            if (info.Kind != IrTypeKind.Sequence || info.ElementType == null)
                throw new ArgumentException("Sequence values require a sequence type.", nameof(type));
            var values = elements.ToImmutableArray();
            if (values.Any(value => value == null || value.Type != info.ElementType.Value))
                throw new ArgumentException(
                    "Every sequence element must match the sequence element type.",
                    nameof(elements));
            return IrValue.CreateSequence(type, values);
        }
    }

    public IrBooleanTerm Boolean(bool value) {
        lock (_gate) {
            var key = new TermKey(IrTermKind.Boolean, BooleanType.Value, value ? 1 : 0);
            return Intern(key, id => new IrBooleanTerm(id, BooleanType, value));
        }
    }

    public IrIntegerTerm Integer(long value) {
        lock (_gate) {
            var key = new TermKey(IrTermKind.Integer, IntegerType.Value, number: value);
            return Intern(key, id => new IrIntegerTerm(id, IntegerType, value));
        }
    }

    public IrStringTerm String(string value) {
        if (value == null) throw new ArgumentNullException(nameof(value));
        lock (_gate) {
            var stringId = InternStringCore(value);
            var key = new TermKey(IrTermKind.String, StringType.Value, stringId.Value);
            return Intern(key, id => new IrStringTerm(id, StringType, stringId));
        }
    }

    public IrNullTerm Null(IrTypeId type) {
        lock (_gate) {
            var info = GetTypeInfoCore(type, nameof(type));
            if (!IsNullable(info.Kind))
                throw new ArgumentException("Null requires a string, reference, or sequence type.", nameof(type));
            var key = new TermKey(IrTermKind.Null, type.Value);
            return Intern(key, id => new IrNullTerm(id, type));
        }
    }

    public IrVariableTerm Variable(IrVarId variable) {
        lock (_gate) {
            var info = GetVariableInfoCore(variable, nameof(variable));
            var key = new TermKey(IrTermKind.Variable, info.Type.Value, variable.Value);
            return Intern(key, id => new IrVariableTerm(id, info.Type, variable));
        }
    }

    public IrOpaqueTerm PureOpaque(IrMemberId member, IrTerm? receiver, params IrTerm[] arguments) =>
        Opaque(member, receiver, arguments, IrOpaquePurity.Pure, default);

    public IrOpaqueTerm ImpureOpaque(
        OperationId operation,
        IrMemberId member,
        IrTerm? receiver,
        params IrTerm[] arguments) =>
        Opaque(member, receiver, arguments, IrOpaquePurity.Impure, operation);

    public IrTerm Unary(IrUnaryOperator @operator, IrTerm operand) {
        if (operand == null) throw new ArgumentNullException(nameof(operand));
        lock (_gate) {
            EnsureTermCore(operand, nameof(operand));
            var expectedType = @operator switch {
                IrUnaryOperator.Not => BooleanType,
                IrUnaryOperator.Negate => IntegerType,
                _ => throw new ArgumentOutOfRangeException(nameof(@operator))
            };
            if (operand.Type != expectedType)
                throw new ArgumentException("The operand type is not valid for the unary operator.", nameof(operand));
            var folded = FoldUnary(@operator, operand);
            if (folded != null) return folded;
            var key = new TermKey(
                IrTermKind.Unary,
                expectedType.Value,
                (int)@operator,
                children: [operand.Id.Value]);
            return Intern<IrUnaryTerm>(key, id => new IrUnaryTerm(id, expectedType, @operator, operand));
        }
    }

    public IrTerm Binary(IrBinaryOperator @operator, IrTerm left, IrTerm right) {
        if (left == null) throw new ArgumentNullException(nameof(left));
        if (right == null) throw new ArgumentNullException(nameof(right));
        lock (_gate) {
            EnsureTermCore(left, nameof(left));
            EnsureTermCore(right, nameof(right));
            var resultType = ValidateBinaryAndGetResultType(@operator, left, right);
            var folded = FoldBinary(@operator, left, right);
            if (folded != null) return folded;
            var key = new TermKey(
                IrTermKind.Binary,
                resultType.Value,
                (int)@operator,
                children: [left.Id.Value, right.Id.Value]);
            return Intern<IrBinaryTerm>(key, id => new IrBinaryTerm(id, resultType, @operator, left, right));
        }
    }

    public IrTerm Conditional(IrTerm condition, IrTerm whenTrue, IrTerm whenFalse) {
        if (condition == null) throw new ArgumentNullException(nameof(condition));
        if (whenTrue == null) throw new ArgumentNullException(nameof(whenTrue));
        if (whenFalse == null) throw new ArgumentNullException(nameof(whenFalse));
        lock (_gate) {
            EnsureTermCore(condition, nameof(condition));
            EnsureTermCore(whenTrue, nameof(whenTrue));
            EnsureTermCore(whenFalse, nameof(whenFalse));
            if (condition.Type != BooleanType)
                throw new ArgumentException("The conditional guard must be boolean.", nameof(condition));
            if (whenTrue.Type != whenFalse.Type)
                throw new ArgumentException("Conditional branches must have the same type.", nameof(whenFalse));
            if (condition is IrBooleanTerm literal) return literal.Value ? whenTrue : whenFalse;
            var key = new TermKey(
                IrTermKind.Conditional,
                whenTrue.Type.Value,
                children: [condition.Id.Value, whenTrue.Id.Value, whenFalse.Id.Value]);
            return Intern<IrConditionalTerm>(
                key,
                id => new IrConditionalTerm(id, whenTrue.Type, condition, whenTrue, whenFalse));
        }
    }

    public IrTerm Cast(IrTypeId targetType, IrTerm operand) {
        if (operand == null) throw new ArgumentNullException(nameof(operand));
        lock (_gate) {
            var target = GetTypeInfoCore(targetType, nameof(targetType));
            EnsureTermCore(operand, nameof(operand));
            if (operand.Type == targetType) return operand;
            if (operand is IrNullTerm && IsNullable(target.Kind)) return Null(targetType);
            var key = new TermKey(
                IrTermKind.Cast,
                targetType.Value,
                children: [operand.Id.Value]);
            return Intern<IrCastTerm>(key, id => new IrCastTerm(id, targetType, operand));
        }
    }

    public IrTerm Length(IrTerm value) {
        if (value == null) throw new ArgumentNullException(nameof(value));
        lock (_gate) {
            EnsureTermCore(value, nameof(value));
            var info = GetTypeInfoCore(value.Type, nameof(value));
            if (info.Kind is not (IrTypeKind.String or IrTypeKind.Sequence))
                throw new ArgumentException("Length requires a string or sequence value.", nameof(value));
            if (value is IrStringTerm text) return Integer(GetStringCore(text.Value).Length);
            var key = new TermKey(
                IrTermKind.Length,
                IntegerType.Value,
                children: [value.Id.Value]);
            return Intern<IrLengthTerm>(key, id => new IrLengthTerm(id, IntegerType, value));
        }
    }

    public IrTerm SequenceAccess(IrTerm sequence, IrTerm index) {
        if (sequence == null) throw new ArgumentNullException(nameof(sequence));
        if (index == null) throw new ArgumentNullException(nameof(index));
        lock (_gate) {
            EnsureTermCore(sequence, nameof(sequence));
            EnsureTermCore(index, nameof(index));
            var sequenceType = GetTypeInfoCore(sequence.Type, nameof(sequence));
            if (sequenceType.Kind != IrTypeKind.Sequence || sequenceType.ElementType == null)
                throw new ArgumentException("Sequence access requires a sequence value.", nameof(sequence));
            if (index.Type != IntegerType)
                throw new ArgumentException("Sequence access requires an integer index.", nameof(index));
            var elementType = sequenceType.ElementType.Value;
            var key = new TermKey(
                IrTermKind.SequenceAccess,
                elementType.Value,
                children: [sequence.Id.Value, index.Id.Value]);
            return Intern<IrSequenceAccessTerm>(
                key,
                id => new IrSequenceAccessTerm(id, elementType, sequence, index));
        }
    }

    internal void EnsureTerm(IrTerm term, string parameterName) {
        lock (_gate) EnsureTermCore(term, parameterName);
    }

    private IrOpaqueTerm Opaque(
        IrMemberId member,
        IrTerm? receiver,
        IrTerm[] arguments,
        IrOpaquePurity purity,
        OperationId operation) {
        if (arguments == null) throw new ArgumentNullException(nameof(arguments));
        lock (_gate) {
            var memberInfo = GetMemberInfoCore(member, nameof(member));
            if (memberInfo.IsStatic && receiver != null)
                throw new ArgumentException("A static member cannot have a receiver.", nameof(receiver));
            if (!memberInfo.IsStatic && receiver == null)
                throw new ArgumentNullException(nameof(receiver), "An instance member requires a receiver.");
            if (receiver != null) {
                EnsureTermCore(receiver, nameof(receiver));
                if (receiver.Type != memberInfo.DeclaringType)
                    throw new ArgumentException(
                        "An instance receiver must match the member declaring type.",
                        nameof(receiver));
            }
            if (arguments.Length != memberInfo.ParameterTypes.Length)
                throw new ArgumentException("The argument count does not match the member signature.", nameof(arguments));
            for (var index = 0; index < arguments.Length; index++) {
                var argument = arguments[index] ??
                               throw new ArgumentException("Opaque arguments cannot contain null.", nameof(arguments));
                EnsureTermCore(argument, nameof(arguments));
                if (argument.Type != memberInfo.ParameterTypes[index])
                    throw new ArgumentException("An opaque argument type does not match the member signature.", nameof(arguments));
            }
            if (purity == IrOpaquePurity.Pure) {
                if (!operation.IsDefault)
                    throw new ArgumentException("Pure opaque terms cannot carry an operation identity.", nameof(operation));
            }
            else {
                GetOperationInfoCore(operation, nameof(operation));
            }
            var immutableArguments = arguments.ToImmutableArray();
            ImmutableArray<int> childIds =
                [receiver?.Id.Value ?? -1, .. immutableArguments.Select(static value => value.Id.Value)];
            var key = new TermKey(
                IrTermKind.Opaque,
                memberInfo.ReturnType.Value,
                member.Value,
                (int)purity,
                operation.IsDefault ? -1 : operation.Value,
                children: childIds);
            return Intern<IrOpaqueTerm>(
                key,
                id => new IrOpaqueTerm(
                    id,
                    memberInfo.ReturnType,
                    member,
                    receiver,
                    immutableArguments,
                    purity,
                    operation));
        }
    }

    private IrTerm? FoldUnary(IrUnaryOperator @operator, IrTerm operand) {
        if (@operator == IrUnaryOperator.Not && operand is IrBooleanTerm boolean)
            return Boolean(!boolean.Value);
        if (@operator == IrUnaryOperator.Negate && operand is IrIntegerTerm integer &&
            integer.Value != long.MinValue)
            return Integer(-integer.Value);
        return null;
    }

    private IrTerm? FoldBinary(IrBinaryOperator @operator, IrTerm left, IrTerm right) {
        if (@operator == IrBinaryOperator.AndAlso && left is IrBooleanTerm andLeft)
            return andLeft.Value ? right : left;
        if (@operator == IrBinaryOperator.OrElse && left is IrBooleanTerm orLeft)
            return orLeft.Value ? left : right;
        if (@operator is IrBinaryOperator.Equal or IrBinaryOperator.NotEqual) {
            var equal = TryCompareConstants(left, right);
            if (equal.HasValue)
                return Boolean(@operator == IrBinaryOperator.Equal ? equal.Value : !equal.Value);
        }
        if (left is IrIntegerTerm leftInteger && right is IrIntegerTerm rightInteger)
            return FoldIntegerBinary(@operator, leftInteger.Value, rightInteger.Value);
        if (left is IrStringTerm leftString && right is IrStringTerm rightString &&
            @operator == IrBinaryOperator.StringConcat)
            return String(GetStringCore(leftString.Value) + GetStringCore(rightString.Value));
        return null;
    }

    private IrTerm? FoldIntegerBinary(IrBinaryOperator @operator, long left, long right) {
        try {
            return @operator switch {
                IrBinaryOperator.Add => Integer(checked(left + right)),
                IrBinaryOperator.Subtract => Integer(checked(left - right)),
                IrBinaryOperator.Multiply => Integer(checked(left * right)),
                IrBinaryOperator.Divide when right != 0 && !(left == long.MinValue && right == -1) =>
                    Integer(left / right),
                IrBinaryOperator.Remainder when right != 0 => Integer(left % right),
                IrBinaryOperator.LessThan => Boolean(left < right),
                IrBinaryOperator.LessThanOrEqual => Boolean(left <= right),
                IrBinaryOperator.GreaterThan => Boolean(left > right),
                IrBinaryOperator.GreaterThanOrEqual => Boolean(left >= right),
                _ => null
            };
        }
        catch (OverflowException) {
            return null;
        }
    }

    private static bool? TryCompareConstants(IrTerm left, IrTerm right) {
        if (left is IrBooleanTerm leftBoolean && right is IrBooleanTerm rightBoolean)
            return leftBoolean.Value == rightBoolean.Value;
        if (left is IrIntegerTerm leftInteger && right is IrIntegerTerm rightInteger)
            return leftInteger.Value == rightInteger.Value;
        if (left is IrStringTerm leftString && right is IrStringTerm rightString)
            return leftString.Value == rightString.Value;
        if (left is IrNullTerm && right is IrNullTerm) return true;
        if (left is IrNullTerm && IsNonNullLiteral(right) ||
            right is IrNullTerm && IsNonNullLiteral(left))
            return false;
        return null;
    }

    private IrTypeId ValidateBinaryAndGetResultType(IrBinaryOperator @operator, IrTerm left, IrTerm right) {
        switch (@operator) {
            case IrBinaryOperator.Add:
            case IrBinaryOperator.Subtract:
            case IrBinaryOperator.Multiply:
            case IrBinaryOperator.Divide:
            case IrBinaryOperator.Remainder:
                RequireTypes(left, right, IntegerType, @operator);
                return IntegerType;
            case IrBinaryOperator.AndAlso:
            case IrBinaryOperator.OrElse:
                RequireTypes(left, right, BooleanType, @operator);
                return BooleanType;
            case IrBinaryOperator.Equal:
            case IrBinaryOperator.NotEqual:
                if (left.Type != right.Type)
                    throw new ArgumentException("Equality operands must have the same type.", nameof(right));
                return BooleanType;
            case IrBinaryOperator.LessThan:
            case IrBinaryOperator.LessThanOrEqual:
            case IrBinaryOperator.GreaterThan:
            case IrBinaryOperator.GreaterThanOrEqual:
                RequireTypes(left, right, IntegerType, @operator);
                return BooleanType;
            case IrBinaryOperator.StringConcat:
                RequireTypes(left, right, StringType, @operator);
                return StringType;
            default:
                throw new ArgumentOutOfRangeException(nameof(@operator));
        }
    }

    private static bool IsNonNullLiteral(IrTerm term) =>
        term is IrBooleanTerm or IrIntegerTerm or IrStringTerm;

    private static bool IsNullable(IrTypeKind kind) =>
        kind is IrTypeKind.String or IrTypeKind.Reference or IrTypeKind.Sequence;

    private static void ValidateName(string? value, string parameterName) {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty name is required.", parameterName);
    }

    private static void RequireTypes(IrTerm left, IrTerm right, IrTypeId expected, IrBinaryOperator @operator) {
        if (left.Type != expected || right.Type != expected)
            throw new ArgumentException(
                "Operands are not valid for binary operator " + @operator + ".",
                nameof(right));
    }

    private IrStringId InternStringCore(string value) {
        if (_stringIds.TryGetValue(value, out var existing)) return existing;
        var id = new IrStringId(_scope, _strings.Count);
        _stringIds.Add(value, id);
        _strings.Add(value);
        return id;
    }

    private string GetStringCore(IrStringId id) {
        EnsureScope(id.Scope, nameof(id));
        return GetAt(_strings, id.Value, nameof(id));
    }

    private IrIdentityId CreateIdentityCore() =>
        new(_scope, _identityCount++);

    private IrTypeId GetOrCreateTypeCore(
        IrIdentityId identity,
        string name,
        IrTypeKind kind,
        IrTypeId? elementType) {
        var nameId = InternStringCore(name);
        var key = new TypeKey(
            kind,
            identity.IsDefault ? -1 : identity.Value,
            elementType?.Value ?? -1);
        if (_typeIds.TryGetValue(key, out var existing)) return existing;
        var id = new IrTypeId(_scope, _types.Count);
        _typeIds.Add(key, id);
        _types.Add(new IrTypeInfo(id, nameId, kind, elementType));
        return id;
    }

    private IrTypeInfo GetTypeInfoCore(IrTypeId id, string parameterName) {
        EnsureScope(id.Scope, parameterName);
        return GetAt(_types, id.Value, parameterName);
    }

    private IrVariableInfo GetVariableInfoCore(IrVarId id, string parameterName) {
        EnsureScope(id.Scope, parameterName);
        return GetAt(_variables, id.Value, parameterName);
    }

    private IrMemberInfo GetMemberInfoCore(IrMemberId id, string parameterName) {
        EnsureScope(id.Scope, parameterName);
        return GetAt(_members, id.Value, parameterName);
    }

    private IrOperationInfo GetOperationInfoCore(OperationId id, string parameterName) {
        EnsureScope(id.Scope, parameterName);
        return GetAt(_operations, id.Value, parameterName);
    }

    private void EnsureTermCore(IrTerm term, string parameterName) {
        if (term.Id.Scope != _scope ||
            term.Id.Value < 0 ||
            term.Id.Value >= _terms.Count ||
            !ReferenceEquals(_terms[term.Id.Value], term))
            throw new ArgumentException("The term belongs to a different IR factory.", parameterName);
    }

    private void EnsureScope(long scope, string parameterName) {
        if (scope != _scope)
            throw new ArgumentException("The identifier belongs to a different IR factory.", parameterName);
    }

    private T Intern<T>(TermKey key, Func<IrId, T> create) where T : IrTerm {
        if (_termIds.TryGetValue(key, out var existing)) return (T)existing;
        var id = new IrId(_scope, _terms.Count);
        var term = create(id);
        _termIds.Add(key, term);
        _terms.Add(term);
        return term;
    }

    private static T GetAt<T>(IReadOnlyList<T> items, int index, string parameterName) {
        if (index < 0 || index >= items.Count)
            throw new ArgumentOutOfRangeException(parameterName);
        return items[index];
    }

    private sealed class TypeKey : IEquatable<TypeKey> {
        internal TypeKey(IrTypeKind kind, int identity, int elementType) {
            Kind = kind;
            Identity = identity;
            ElementType = elementType;
        }

        private IrTypeKind Kind { get; }
        private int Identity { get; }
        private int ElementType { get; }

        public bool Equals(TypeKey? other) =>
            other != null &&
            Kind == other.Kind &&
            Identity == other.Identity &&
            ElementType == other.ElementType;

        public override bool Equals(object? obj) => Equals(obj as TypeKey);
        public override int GetHashCode() {
            unchecked {
                return (((int)Kind * 397) ^ Identity) * 397 ^ ElementType;
            }
        }
    }

    private sealed class MemberKey : IEquatable<MemberKey> {
        internal MemberKey(
            int identity,
            int declaringType,
            int returnType,
            bool isStatic,
            ImmutableArray<int> parameterTypes) {
            Identity = identity;
            DeclaringType = declaringType;
            ReturnType = returnType;
            IsStatic = isStatic;
            ParameterTypes = parameterTypes;
        }

        private int Identity { get; }
        private int DeclaringType { get; }
        private int ReturnType { get; }
        private bool IsStatic { get; }
        private ImmutableArray<int> ParameterTypes { get; }

        public bool Equals(MemberKey? other) =>
            other != null &&
            Identity == other.Identity &&
            DeclaringType == other.DeclaringType &&
            ReturnType == other.ReturnType &&
            IsStatic == other.IsStatic &&
            SequenceEqual(ParameterTypes, other.ParameterTypes);

        public override bool Equals(object? obj) => Equals(obj as MemberKey);
        public override int GetHashCode() {
            unchecked {
                var hash = Identity;
                hash = hash * 397 ^ DeclaringType;
                hash = hash * 397 ^ ReturnType;
                hash = hash * 397 ^ (IsStatic ? 1 : 0);
                foreach (var parameterType in ParameterTypes) hash = hash * 397 ^ parameterType;
                return hash;
            }
        }
    }

    private abstract class ExternalIdentityKey {
    }

    private sealed class ExternalIdentityKey<T>(
        T value,
        IEqualityComparer<T> comparer)
        : ExternalIdentityKey,
          IEquatable<ExternalIdentityKey<T>>
        where T : notnull {
        private readonly T _value = value;
        private readonly IEqualityComparer<T> _comparer = comparer;

        public bool Equals(ExternalIdentityKey<T>? other) =>
            other != null &&
            ReferenceEquals(_comparer, other._comparer) &&
            _comparer.Equals(_value, other._value);

        public override bool Equals(object? obj) =>
            Equals(obj as ExternalIdentityKey<T>);

        public override int GetHashCode() {
            unchecked {
                return (System.Runtime.CompilerServices.RuntimeHelpers
                            .GetHashCode(_comparer) * 397) ^
                       _comparer.GetHashCode(_value);
            }
        }
    }

    private sealed class TermKey : IEquatable<TermKey> {
        internal TermKey(
            IrTermKind kind,
            int type,
            int first = 0,
            int second = 0,
            int third = 0,
            long number = 0,
            ImmutableArray<int> children = default) {
            Kind = kind;
            Type = type;
            First = first;
            Second = second;
            Third = third;
            Number = number;
            Children = children.IsDefault ? [] : children;
        }

        private IrTermKind Kind { get; }
        private int Type { get; }
        private int First { get; }
        private int Second { get; }
        private int Third { get; }
        private long Number { get; }
        private ImmutableArray<int> Children { get; }

        public bool Equals(TermKey? other) =>
            other != null &&
            Kind == other.Kind &&
            Type == other.Type &&
            First == other.First &&
            Second == other.Second &&
            Third == other.Third &&
            Number == other.Number &&
            SequenceEqual(Children, other.Children);

        public override bool Equals(object? obj) => Equals(obj as TermKey);
        public override int GetHashCode() {
            unchecked {
                var hash = (int)Kind;
                hash = hash * 397 ^ Type;
                hash = hash * 397 ^ First;
                hash = hash * 397 ^ Second;
                hash = hash * 397 ^ Third;
                hash = hash * 397 ^ (int)Number;
                hash = hash * 397 ^ (int)(Number >> 32);
                foreach (var child in Children) hash = hash * 397 ^ child;
                return hash;
            }
        }
    }

    private static bool SequenceEqual(ImmutableArray<int> left, ImmutableArray<int> right) {
        if (left.Length != right.Length) return false;
        for (var index = 0; index < left.Length; index++)
            if (left[index] != right[index])
                return false;
        return true;
    }
}
