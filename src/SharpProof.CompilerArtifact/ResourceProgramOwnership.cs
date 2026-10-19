using SharpProof.Ir;

namespace SharpProof.CompilerArtifact;

// This validates detached resource inputs. The manifest envelope establishes
// external source ownership; a matching label alone is not authentication.
internal static class ResourceProgramOwnership
{
    internal static void Validate(ResourceProgram program, string callableId,
        CompilerTotalCallablePreparation owner, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        cancellation.ThrowIfCancellationRequested();
        ResourceExpressionOwnership.Validate(ResourceExpression.Zero, callableId, owner, cancellation);
        Visit(program, callableId, owner, cancellation);
    }

    private static void Visit(ResourceProgram program, string callableId,
        CompilerTotalCallablePreparation owner, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        cancellation.ThrowIfCancellationRequested();
        if (program.Length is { } length)
        { ResourceExpressionOwnership.Validate(length, callableId, owner, cancellation); }
        if (program.Allocation is { } allocation)
        {
            if (program.Kind != ResourceProgramKind.AllocateArray || allocation.Kind != ResourceAllocationKind.Array ||
                !allocation.Extent.Equals(program.Length))
            { throw new InvalidDataException("MalformedResourceAllocation"); }
            ResourceExpressionOwnership.Validate(allocation.Extent, callableId, owner, cancellation);
        }
        if (program.Rank is { } rank)
        {
            ResourceExpressionOwnership.Validate(rank.Initial, callableId, owner, cancellation);
            ResourceExpressionOwnership.Validate(rank.Limit, callableId, owner, cancellation);
        }
        foreach (var child in program.Children)
        { Visit(child, callableId, owner, cancellation); }
    }
}
