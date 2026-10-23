namespace SharpProof.Ir;

// A compiler-owned explicit static method binding captures no receiver. A
// delegate type alone cannot establish this: instance bindings retain targets.
internal static class IrStaticDelegateAllocationSite
{
    internal const string Description = "StaticDelegateAllocation";

    internal static bool Matches(IrFactory factory, IrAllocationInstruction allocation)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        allocation = RepositoryParameterGuard.NotNull(allocation, nameof(allocation));
        return factory.IsDelegateReferenceType(allocation.AllocatedType) &&
            factory.GetOperationInfo(allocation.Operation).Description is { } description &&
            factory.GetString(description) == Description;
    }
}
