namespace SharpProof.Ir;

// Each compiler-certified frame slot must be initialized in certificate order.
// Storage widths alone cannot establish which references the frame retains.
internal static class IrClosureFrameSite
{
    internal const string Description = "RootClosureFrameAllocation";
    private const string FieldPrefix = IrFieldSites.Prefix + "$closure-capture:";

    internal static string FieldName(int slot)
    {
        if (slot < 0 || slot >= ResourceAllocationShape.MaximumFields)
        { throw new ArgumentOutOfRangeException(nameof(slot)); }
        return FieldPrefix + slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static bool Matches(IrFactory factory, IrAllocationInstruction allocation)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        allocation = RepositoryParameterGuard.NotNull(allocation, nameof(allocation));
        return factory.GetOperationInfo(allocation.Operation).Description is { } description &&
            factory.GetString(description) == Description;
    }

    internal static bool IsCaptureField(IrFactory factory, IrMemberId field)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.ThrowIfInvalid(field.IsDefault, nameof(field));
        return factory.GetString(factory.GetMemberInfo(field).Name).StartsWith(FieldPrefix, StringComparison.Ordinal);
    }

    internal static bool TryGetCapture(IrFactory factory, IrAllocationInstruction allocation, IrInstruction? following,
        int slot, ResourceStorageKind storage, out IrMemberId field)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        allocation = RepositoryParameterGuard.NotNull(allocation, nameof(allocation));
        var expectedName = FieldName(slot);
        field = default;
        if (allocation.Target is not { } target || following is not IrWriteInstruction
            { IsFieldStore: true, IsStore: true, Index: null, Length: null, Target: { } receiver, Value: not null, Field: { } candidate } ||
            receiver.Id != factory.Cast(factory.ObjectType, factory.Variable(target)).Id)
        { return false; }
        var member = factory.GetMemberInfo(candidate);
        if (member.IsStatic || member.DeclaringType != factory.ObjectType || !member.ParameterTypes.IsEmpty ||
            factory.GetString(member.Name) != expectedName || Storage(factory.GetTypeInfo(member.ReturnType)) != storage)
        { return false; }
        field = candidate;
        return true;
    }

    private static ResourceStorageKind Storage(IrTypeInfo type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return type.Kind switch
        {
            IrTypeKind.Reference or IrTypeKind.String or IrTypeKind.Sequence => ResourceStorageKind.Reference,
            IrTypeKind.Boolean => ResourceStorageKind.Byte,
            IrTypeKind.Integer => type.Width switch
            {
                8 => ResourceStorageKind.Byte,
                16 => ResourceStorageKind.Word16,
                32 => ResourceStorageKind.Word32,
                64 => ResourceStorageKind.Word64,
                _ => ResourceStorageKind.Unspecified
            },
            _ => ResourceStorageKind.Unspecified
        };
    }
}
