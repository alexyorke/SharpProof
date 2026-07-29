namespace SharpProof.Ir;

public sealed class IrFactory
{
    private static long s_nextScope;
    private readonly object _gate = new();
    private readonly Dictionary<ExternalIdentityKey, IrIdentityId> _externalIdentityIds = [];
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
        if (identity == null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

        if (comparer == null)
        {
            throw new ArgumentNullException(nameof(comparer));
        }

        if (typeof(T) == typeof(string))
        {
            throw new ArgumentException(
            "Semantic identities cannot be interned from strings.", nameof(identity));
        }

        lock (_gate)
        {
            var key = new ExternalIdentityKey<T>(identity, comparer);
            if (_externalIdentityIds.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var id = CreateIdentityCore();
            _externalIdentityIds.Add(key, id);
            return id;
        }
    }

    public IrStringId InternString(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

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

            return GetOrCreateTypeCore(default, GetStringCore(element.Name) + "[]", IrTypeKind.Sequence, elementType);
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
        ValidateName(name, nameof(name));
        if (parameterTypes == null)
        {
            throw new ArgumentNullException(nameof(parameterTypes));
        }

        lock (_gate)
        {
            EnsureScope(identity.Scope, nameof(identity));
            GetTypeInfoCore(declaringType, nameof(declaringType));
            GetTypeInfoCore(returnType, nameof(returnType));
            foreach (var parameterType in parameterTypes)
            {
                GetTypeInfoCore(parameterType, nameof(parameterTypes));
            }

            var nameId = InternStringCore(name);
            var parameters = parameterTypes.ToImmutableArray();
            var key = new StructuralKey(
                default, declaringType.Value, identity.Value, returnType.Value, isStatic ? 1 : 0,
                children: [.. parameters.Select(static value => value.Value)]);
            if (_memberIds.TryGetValue(key, out var existing))
            {
                return existing;
            }

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
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new IrValue(StringType, IrValueKind.String, value);
    }

    public IrValue CreateNullValue(IrTypeId type)
    {
        lock (_gate)
        {
            if (!IsNullable(GetTypeInfoCore(type, nameof(type)).Kind))
            {
                throw new ArgumentException("Null requires a string, reference, or sequence type.", nameof(type));
            }

            return new IrValue(type, IrValueKind.Null, null);
        }
    }

    public IrValue CreateReferenceValue(IrTypeId type, object identity)
    {
        if (identity == null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

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
        if (elements == null)
        {
            throw new ArgumentNullException(nameof(elements));
        }

        lock (_gate)
        {
            var info = GetTypeInfoCore(type, nameof(type));
            if (info.Kind != IrTypeKind.Sequence || info.ElementType == null)
            {
                throw new ArgumentException("Sequence values require a sequence type.", nameof(type));
            }

            var values = elements.ToImmutableArray();
            if (values.Any(value => value == null || value.Type != info.ElementType.Value))
            {
                throw new ArgumentException("Every sequence element must match the sequence element type.", nameof(elements));
            }

            return new IrValue(type, IrValueKind.Sequence, values);
        }
    }

    public IrBooleanTerm Boolean(bool value)
    {
        lock (_gate)
        {
            return Intern(
            new StructuralKey(IrTermKind.Boolean, BooleanType.Value, value ? 1 : 0),
            id => new IrBooleanTerm(id, BooleanType, value));
        }
    }

    public IrIntegerTerm Integer(long value)
    {
        lock (_gate)
        {
            return Intern(
            new StructuralKey(IrTermKind.Integer, IntegerType.Value, number: value),
            id => new IrIntegerTerm(id, IntegerType, value));
        }
    }

    public IrStringTerm String(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        lock (_gate)
        {
            var stringId = InternStringCore(value);
            return Intern(
                new StructuralKey(IrTermKind.String, StringType.Value, stringId.Value),
                id => new IrStringTerm(id, StringType, stringId));
        }
    }

    public IrNullTerm Null(IrTypeId type)
    {
        lock (_gate)
        {
            if (!IsNullable(GetTypeInfoCore(type, nameof(type)).Kind))
            {
                throw new ArgumentException("Null requires a string, reference, or sequence type.", nameof(type));
            }

            return Intern(
                new StructuralKey(IrTermKind.Null, type.Value),
                id => new IrNullTerm(id, type));
        }
    }

    public IrVariableTerm Variable(IrVarId variable)
    {
        lock (_gate)
        {
            var info = GetVariableInfoCore(variable, nameof(variable));
            return Intern(
                new StructuralKey(IrTermKind.Variable, info.Type.Value, variable.Value),
                id => new IrVariableTerm(id, info.Type, variable));
        }
    }

    public IrOpaqueTerm PureOpaque(IrMemberId member, IrTerm? receiver, params IrTerm[] arguments)
    {
        return Opaque(member, receiver, arguments, IrOpaquePurity.Pure, default);
    }

    public IrOpaqueTerm ImpureOpaque(OperationId operation, IrMemberId member, IrTerm? receiver, params IrTerm[] arguments)
    {
        return Opaque(member, receiver, arguments, IrOpaquePurity.Impure, operation);
    }

    public IrTerm Unary(IrUnaryOperator @operator, IrTerm operand)
    {
        if (operand == null)
        {
            throw new ArgumentNullException(nameof(operand));
        }

        lock (_gate)
        {
            EnsureTermCore(operand, nameof(operand));
            var semantics = IrOperatorCatalog.Get(@operator);
            var expectedType = GetBuiltInType(semantics.Operand);
            if (operand.Type != expectedType)
            {
                throw new ArgumentException("The operand type is not valid for the unary operator.", nameof(operand));
            }

            var folded = FoldUnary(@operator, operand);
            if (folded != null)
            {
                return folded;
            }

            return Intern(
                new StructuralKey(IrTermKind.Unary, expectedType.Value, semantics.Key,
                    children: [operand.Id.Value]),
                id => new IrUnaryTerm(id, expectedType, @operator, operand));
        }
    }

    public IrTerm Binary(IrBinaryOperator @operator, IrTerm left, IrTerm right)
    {
        if (left == null)
        {
            throw new ArgumentNullException(nameof(left));
        }

        if (right == null)
        {
            throw new ArgumentNullException(nameof(right));
        }

        lock (_gate)
        {
            EnsureTermCore(left, nameof(left));
            EnsureTermCore(right, nameof(right));
            var semantics = IrOperatorCatalog.Get(@operator);
            var resultType = ValidateBinaryAndGetResultType(
                @operator, semantics.Operand, semantics.Result, left, right);
            var folded = FoldBinary(@operator, left, right);
            if (folded != null)
            {
                return folded;
            }

            return Intern(
                new StructuralKey(IrTermKind.Binary, resultType.Value, semantics.Key,
                    children: [left.Id.Value, right.Id.Value]),
                id => new IrBinaryTerm(id, resultType, @operator, left, right));
        }
    }

    public IrTerm Conditional(IrTerm condition, IrTerm whenTrue, IrTerm whenFalse)
    {
        if (condition == null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        if (whenTrue == null)
        {
            throw new ArgumentNullException(nameof(whenTrue));
        }

        if (whenFalse == null)
        {
            throw new ArgumentNullException(nameof(whenFalse));
        }

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
                    children: [condition.Id.Value, whenTrue.Id.Value, whenFalse.Id.Value]),
                id => new IrConditionalTerm(id, whenTrue.Type, condition, whenTrue, whenFalse));
        }
    }

    public IrTerm Cast(IrTypeId targetType, IrTerm operand)
    {
        if (operand == null)
        {
            throw new ArgumentNullException(nameof(operand));
        }

        lock (_gate)
        {
            var target = GetTypeInfoCore(targetType, nameof(targetType));
            EnsureTermCore(operand, nameof(operand));
            if (operand.Type == targetType)
            {
                return operand;
            }

            if (operand is IrNullTerm && IsNullable(target.Kind))
            {
                return Null(targetType);
            }

            return Intern(
                new StructuralKey(IrTermKind.Cast, targetType.Value, children: [operand.Id.Value]),
                id => new IrCastTerm(id, targetType, operand));
        }
    }

    public IrTerm Length(IrTerm value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

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
                new StructuralKey(IrTermKind.Length, IntegerType.Value, children: [value.Id.Value]),
                id => new IrLengthTerm(id, IntegerType, value));
        }
    }

    public IrTerm SequenceAccess(IrTerm sequence, IrTerm index)
    {
        if (sequence == null)
        {
            throw new ArgumentNullException(nameof(sequence));
        }

        if (index == null)
        {
            throw new ArgumentNullException(nameof(index));
        }

        lock (_gate)
        {
            var elementType = ValidateSequenceTermsCore(
                sequence, index,
                "Sequence access requires a sequence value.",
                "Sequence access requires an integer index.",
                nameof(sequence), nameof(index));
            return Intern(
                new StructuralKey(IrTermKind.SequenceAccess, elementType.Value,
                    children: [sequence.Id.Value, index.Id.Value]),
                id => new IrSequenceAccessTerm(id, elementType, sequence, index));
        }
    }

    internal void EnsureTerm(IrTerm term, string parameterName)
    {
        lock (_gate)
        {
            EnsureTermCore(term, parameterName);
        }
    }

    internal IrTypeId ValidateSequenceTerms(
        IrTerm sequence, IrTerm index, string sequenceMessage, string indexMessage,
        string sequenceParameter, string indexParameter)
    {
        lock (_gate)
        {
            return ValidateSequenceTermsCore(
            sequence, index, sequenceMessage, indexMessage, sequenceParameter, indexParameter);
        }
    }

    internal void ValidateCallShape(
        IrMemberInfo member, IrTerm? receiver, IReadOnlyList<IrTerm> arguments,
        string parameterName)
    {
        lock (_gate)
        {
            ValidateCallShapeCore(member, receiver, arguments, parameterName, opaque: false);
        }
    }

    private IrOpaqueTerm Opaque(IrMemberId member, IrTerm? receiver, IrTerm[] arguments, IrOpaquePurity purity, OperationId operation)
    {
        if (arguments == null)
        {
            throw new ArgumentNullException(nameof(arguments));
        }

        lock (_gate)
        {
            var memberInfo = GetMemberInfoCore(member, nameof(member));
            ValidateCallShapeCore(memberInfo, receiver, arguments, nameof(arguments), opaque: true);
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

            var immutableArguments = arguments.ToImmutableArray();
            ImmutableArray<int> childIds =
                [receiver?.Id.Value ?? -1, .. immutableArguments.Select(static value => value.Id.Value)];
            return Intern(new StructuralKey(
                    IrTermKind.Opaque, memberInfo.ReturnType.Value, member.Value, PurityKey(purity),
                    operation.IsDefault ? -1 : operation.Value, children: childIds),
                id => new IrOpaqueTerm(id, memberInfo.ReturnType, member, receiver,
                    immutableArguments, purity, operation));
        }
    }

    private void ValidateCallShapeCore(
        IrMemberInfo member, IrTerm? receiver, IReadOnlyList<IrTerm> arguments,
        string parameterName, bool opaque)
    {
        var receiverParameter = opaque ? nameof(receiver) : parameterName;
        if (member.IsStatic && receiver != null)
        {
            throw new ArgumentException("A static member cannot have a receiver.", receiverParameter);
        }

        if (!member.IsStatic && receiver == null)
        {
            if (opaque)
            {
                throw new ArgumentNullException(nameof(receiver), "An instance member requires a receiver.");
            }

            throw new ArgumentException("An instance member requires a receiver.", parameterName);
        }
        if (receiver != null)
        {
            EnsureTermCore(receiver, receiverParameter);
            if (receiver.Type != member.DeclaringType)
            {
                throw new ArgumentException(
                    "An instance receiver must match the member declaring type.", receiverParameter);
            }
        }
        if (arguments.Count != member.ParameterTypes.Length)
        {
            throw new ArgumentException("The argument count does not match the member signature.", parameterName);
        }

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index] ??
                throw new ArgumentException(opaque
                    ? "Opaque arguments cannot contain null."
                    : "Arguments cannot contain null.", parameterName);
            EnsureTermCore(argument, parameterName);
            if (argument.Type != member.ParameterTypes[index])
            {
                throw new ArgumentException(opaque
                    ? "An opaque argument type does not match the member signature."
                    : "An argument does not match the member signature.", parameterName);
            }
        }
    }

    private IrTypeId ValidateSequenceTermsCore(
        IrTerm sequence, IrTerm index, string sequenceMessage, string indexMessage,
        string sequenceParameter, string indexParameter)
    {
        if (sequence == null)
        {
            throw new ArgumentNullException(sequenceParameter);
        }

        if (index == null)
        {
            throw new ArgumentNullException(indexParameter);
        }

        EnsureTermCore(sequence, sequenceParameter);
        EnsureTermCore(index, indexParameter);
        var type = GetTypeInfoCore(sequence.Type, sequenceParameter);
        if (type.Kind != IrTypeKind.Sequence || type.ElementType == null)
        {
            throw new ArgumentException(sequenceMessage, sequenceParameter);
        }

        if (index.Type != IntegerType)
        {
            throw new ArgumentException(indexMessage, indexParameter);
        }

        return type.ElementType.Value;
    }

    private IrTerm? FoldUnary(IrUnaryOperator @operator, IrTerm operand)
    {
        return (@operator, operand) switch
        {
            (IrUnaryOperator.Not, IrBooleanTerm value) => Boolean(!value.Value),
            (IrUnaryOperator.Negate, IrIntegerTerm { Value: not long.MinValue } value) => Integer(-value.Value),
            _ => null
        };
    }

    private IrTerm? FoldBinary(IrBinaryOperator @operator, IrTerm left, IrTerm right)
    {
        if (@operator == IrBinaryOperator.AndAlso && left is IrBooleanTerm andLeft)
        {
            return andLeft.Value ? right : left;
        }

        if (@operator == IrBinaryOperator.OrElse && left is IrBooleanTerm orLeft)
        {
            return orLeft.Value ? left : right;
        }

        if (@operator is IrBinaryOperator.Equal or IrBinaryOperator.NotEqual)
        {
            var equal = TryCompareConstants(left, right);
            if (equal.HasValue)
            {
                return Boolean(@operator == IrBinaryOperator.Equal ? equal.Value : !equal.Value);
            }
        }
        if (left is IrIntegerTerm leftInteger && right is IrIntegerTerm rightInteger)
        {
            return FoldIntegerBinary(@operator, leftInteger.Value, rightInteger.Value);
        }

        if (left is IrStringTerm leftString && right is IrStringTerm rightString &&
            @operator == IrBinaryOperator.StringConcat)
        {
            return String(GetStringCore(leftString.Value) + GetStringCore(rightString.Value));
        }

        return null;
    }

    private IrTerm? FoldIntegerBinary(IrBinaryOperator @operator, long left, long right)
    {
        var result = IrScalarOperations.Evaluate(@operator, left, right);
        return result.Kind switch
        {
            IrScalarResultKind.Integer => Integer(result.Value),
            IrScalarResultKind.Boolean => Boolean(result.Value != 0),
            _ => null
        };
    }

    private static bool? TryCompareConstants(IrTerm left, IrTerm right)
    {
        if (left is IrBooleanTerm leftBoolean && right is IrBooleanTerm rightBoolean)
        {
            return leftBoolean.Value == rightBoolean.Value;
        }

        if (left is IrIntegerTerm leftInteger && right is IrIntegerTerm rightInteger)
        {
            return leftInteger.Value == rightInteger.Value;
        }

        if (left is IrStringTerm leftString && right is IrStringTerm rightString)
        {
            return leftString.Value == rightString.Value;
        }

        if (left is IrNullTerm && right is IrNullTerm)
        {
            return true;
        }

        if (left is IrNullTerm && IsNonNullLiteral(right) || right is IrNullTerm && IsNonNullLiteral(left))
        {
            return false;
        }

        return null;
    }

    private IrTypeId ValidateBinaryAndGetResultType(
        IrBinaryOperator @operator,
        IrTypeKind? operandKind,
        IrTypeKind resultKind,
        IrTerm left,
        IrTerm right)
    {
        if (!operandKind.HasValue)
        {
            if (left.Type != right.Type)
            {
                throw new ArgumentException(
                    "Equality operands must have the same type.",
                    nameof(right));
            }

            return GetBuiltInType(resultKind);
        }
        return RequireTypes(
            left,
            right,
            GetBuiltInType(operandKind.Value),
            GetBuiltInType(resultKind),
            @operator);
    }

    private static IrTypeId RequireTypes(
        IrTerm left, IrTerm right, IrTypeId expected, IrTypeId result,
        IrBinaryOperator @operator)
    {
        if (left.Type != expected || right.Type != expected)
        {
            throw new ArgumentException(
            "Operands are not valid for binary operator " + @operator + ".", nameof(right));
        }

        return result;
    }

    private static bool IsNonNullLiteral(IrTerm term)
    {
        return term is IrBooleanTerm or IrIntegerTerm or IrStringTerm;
    }

    private static bool IsNullable(IrTypeKind kind)
    {
        return kind is IrTypeKind.String or IrTypeKind.Reference or IrTypeKind.Sequence;
    }

    private IrTypeId GetBuiltInType(IrTypeKind kind)
    {
        return kind switch
        {
            IrTypeKind.Boolean => BooleanType,
            IrTypeKind.Integer => IntegerType,
            IrTypeKind.String => StringType,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static int PurityKey(IrOpaquePurity purity)
    {
        return purity switch
        {
            IrOpaquePurity.Pure => 0,
            IrOpaquePurity.Impure => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(purity))
        };
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
        var nameId = InternStringCore(name);
        var key = (kind, identity.IsDefault ? -1 : identity.Value, elementType?.Value ?? -1);
        if (_typeIds.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var id = new IrTypeId(_scope, _types.Count);
        _typeIds.Add(key, id);
        _types.Add(new IrTypeInfo(id, nameId, kind, elementType));
        return id;
    }

    private IrTypeInfo GetTypeInfoCore(IrTypeId id, string parameterName)
    {
        return GetScoped(id.Scope, id.Value, _types, parameterName);
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

    private T Intern<T>(StructuralKey key, Func<IrId, T> create) where T : IrTerm
    {
        if (_termIds.TryGetValue(key, out var existing))
        {
            return (T)existing;
        }

        var id = new IrId(_scope, _terms.Count);
        var term = create(id);
        _termIds.Add(key, term);
        _terms.Add(term);
        return term;
    }

    private T GetScoped<T>(long scope, int value, IReadOnlyList<T> items, string parameterName)
    {
        EnsureScope(scope, parameterName);
        if (value < 0 || value >= items.Count)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return items[value];
    }

    private abstract class ExternalIdentityKey;

    private sealed class ExternalIdentityKey<T>(T value, IEqualityComparer<T> comparer) :
        ExternalIdentityKey, IEquatable<ExternalIdentityKey<T>> where T : notnull
    {
        private readonly T _value = value;
        private readonly IEqualityComparer<T> _comparer = comparer;
        public bool Equals(ExternalIdentityKey<T>? other)
        {
            return other != null && ReferenceEquals(_comparer, other._comparer) && _comparer.Equals(_value, other._value);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as ExternalIdentityKey<T>);
        }

        public override int GetHashCode()
        {
            return unchecked(
                    System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_comparer) * 397 ^ _comparer.GetHashCode(_value));
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
