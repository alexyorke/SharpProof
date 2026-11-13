namespace SharpProof.Ir;

internal abstract class IrTypeShape
{
    private protected IrTypeShape(IrTypeKind kind) { Kind = kind; }

    internal IrTypeKind Kind { get; }

    internal abstract IrValue CreateDefaultValue(IrFactory factory, IrTypeId type);
}

internal sealed class IrBooleanTypeShape : IrTypeShape
{
    internal static IrBooleanTypeShape Instance { get; } = new();

    private IrBooleanTypeShape() : base(IrTypeKind.Boolean) { }

    internal override IrValue CreateDefaultValue(IrFactory factory, IrTypeId type)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        return factory.CreateBooleanValue(false);
    }
}

internal sealed class IrIntegerTypeShape : IrTypeShape
{
    internal static IrIntegerTypeShape Instance { get; } = new();

    private IrIntegerTypeShape() : base(IrTypeKind.Integer) { }

    internal override IrValue CreateDefaultValue(IrFactory factory, IrTypeId type)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        return factory.CreateIntegerValueFromBits(type, 0);
    }
}

internal sealed class IrNullableTypeShape : IrTypeShape
{
    internal static IrNullableTypeShape String { get; } = new(IrTypeKind.String);
    internal static IrNullableTypeShape Reference { get; } = new(IrTypeKind.Reference);
    internal static IrNullableTypeShape Sequence { get; } = new(IrTypeKind.Sequence);

    private IrNullableTypeShape(IrTypeKind kind) : base(kind) { }

    internal override IrValue CreateDefaultValue(IrFactory factory, IrTypeId type)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        return factory.CreateNullValue(type);
    }
}

internal sealed class IrTypeInfo
{
    private readonly IrTypeShape _shape;

    internal IrValue CreateDefaultValue(IrFactory factory)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        return _shape.CreateDefaultValue(factory, Id);
    }

    internal IrTypeInfo(IrTypeId id, IrStringId name, IrTypeShape shape, IrTypeId? elementType,
        int width = 0, bool signed = false)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(name.IsDefault, nameof(name));
        shape = RepositoryParameterGuard.NotNull(shape, nameof(shape));
        (Id, Name, Kind, ElementType) =
            (id, name, shape.Kind, elementType);
        (Width, Signed) = (width, signed);
        _shape = shape;
    }

    public IrTypeId Id { get; }

    public IrStringId Name { get; }

    public IrTypeKind Kind { get; }

    public IrTypeId? ElementType { get; }

    // Width zero is the temporary legacy integer semantics used during shadow rollout.
    public int Width { get; }

    public bool Signed { get; }
}

internal sealed class IrVariableInfo
{
    internal IrVariableInfo(IrVarId id, IrStringId name, IrTypeId type)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(name.IsDefault, nameof(name));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        (Id, Name, Type) =
            (id, name, type);
    }

    public IrVarId Id { get; }

    public IrStringId Name { get; }

    public IrTypeId Type { get; }
}

internal sealed class IrMemberInfo
{
    internal IrMemberInfo(
        IrMemberId id,
        IrIdentityId identity,
        IrTypeId declaringType,
        IrStringId name,
        IrTypeId returnType,
        bool isStatic,
        ImmutableArray<IrTypeId> parameterTypes
    )
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(identity.IsDefault, nameof(identity));
        RepositoryParameterGuard.ThrowIfInvalid(declaringType.IsDefault, nameof(declaringType));
        RepositoryParameterGuard.ThrowIfInvalid(name.IsDefault, nameof(name));
        RepositoryParameterGuard.ThrowIfInvalid(returnType.IsDefault, nameof(returnType));
        RepositoryParameterGuard.RequireInitialized(parameterTypes.IsDefault, nameof(parameterTypes), "Array must be initialized.");
        (Id, Identity, DeclaringType, Name, ReturnType, IsStatic, ParameterTypes) =
            (id, identity, declaringType, name, returnType, isStatic, parameterTypes);
    }

    public IrMemberId Id { get; }

    public IrIdentityId Identity { get; }

    public IrTypeId DeclaringType { get; }

    public IrStringId Name { get; }

    public IrTypeId ReturnType { get; }

    public bool IsStatic { get; }

    public ImmutableArray<IrTypeId> ParameterTypes { get; }
}

internal sealed class IrOperationInfo
{
    internal IrOperationInfo(OperationId id, IrStringId? description, IrSourceSpan? sourceSpan = null)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        (Id, Description) =
            (id, description);
        SourceSpan = sourceSpan;
    }

    public OperationId Id { get; }

    public IrStringId? Description { get; }
    public IrSourceSpan? SourceSpan { get; }
}
