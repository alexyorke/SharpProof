namespace SharpProof.Ir;

// A closed instance binding owns one immutable target edge. The following
// scoped store is mandatory; a delegate allocation marker cannot omit it.
internal static class IrDelegateCaptureSite
{
    internal const string Description = "InstanceDelegateAllocation";
    internal const string TargetFieldName = IrFieldSites.Prefix + "$delegate-target";

    internal static bool Matches(IrFactory factory, IrAllocationInstruction allocation)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        allocation = RepositoryParameterGuard.NotNull(allocation, nameof(allocation));
        return factory.IsDelegateReferenceType(allocation.AllocatedType) &&
            factory.GetOperationInfo(allocation.Operation).Description is { } description &&
            factory.GetString(description) == Description;
    }

    internal static bool IsTargetField(IrFactory factory, IrMemberId field)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.ThrowIfInvalid(field.IsDefault, nameof(field));
        var member = factory.GetMemberInfo(field);
        return !member.IsStatic && member.DeclaringType == factory.ObjectType && member.ReturnType == factory.ObjectType &&
            member.ParameterTypes.IsEmpty && factory.GetString(member.Name) == TargetFieldName;
    }

    internal static bool TryGetCapture(IrFactory factory, IrAllocationInstruction allocation, IrInstruction? following,
        out IrMemberId field)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        allocation = RepositoryParameterGuard.NotNull(allocation, nameof(allocation));
        field = default;
        if (allocation.Target is not { } target || following is not IrWriteInstruction
            { IsFieldStore: true, IsStore: true, Index: null, Length: null, Target: { } receiver, Value: not null, Field: { } candidate } ||
            !IsTargetField(factory, candidate) || receiver.Id != factory.Cast(factory.ObjectType, factory.Variable(target)).Id)
        { return false; }
        field = candidate;
        return true;
    }
}
