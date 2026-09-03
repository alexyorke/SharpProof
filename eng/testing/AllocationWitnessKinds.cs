using System.Collections.Immutable;

internal static class AllocationWitnessKinds
{
    internal static readonly ImmutableArray<string> Managed = [
        "managed-allocation",
        "managed-array-allocation"
    ];
}
