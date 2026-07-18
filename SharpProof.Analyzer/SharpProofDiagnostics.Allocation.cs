using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

public static partial class SharpProofDiagnostics
{
    public static readonly DiagnosticDescriptor AllocationInZeroAllocationMethodRule = CreateDescriptor(
        AllocationInZeroAllocationMethodId,
        "Allocation In [ZeroAllocations] Method",
        "Method '{1}' is marked [ZeroAllocations], but operation '{0}' allocates",
        "Allocation",
        DiagnosticSeverity.Warning,
        "Reports direct source-visible allocation sites inside methods annotated with [ZeroAllocations].");
}
