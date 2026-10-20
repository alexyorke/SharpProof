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
        if (program.Kind == ResourceProgramKind.AllocateArray ? program.LowerLength is null : program.LowerLength is not null)
        { throw new InvalidDataException("MalformedResourceLowerExtent"); }
        if (program.Kind == ResourceProgramKind.CreditLoop
            ? program.Credit is null || program.Rank is null || program.Children.Length != 2 || program.RepeatKind != ResourceRepeatKind.Loop
            : program.Credit is not null)
        { throw new InvalidDataException("MalformedResourceCredit"); }
        if (program.LowerLength is { } lowerLength)
        { ResourceExpressionOwnership.Validate(lowerLength, callableId, owner, cancellation); }
        if (program.Kind is ResourceProgramKind.Activation or ResourceProgramKind.UpperEnvelope && program.Children.Length != 1 ||
            program.Kind != ResourceProgramKind.Activation && program.Activation is not null)
        { throw new InvalidDataException("MalformedResourceActivation"); }
        if (program.Kind == ResourceProgramKind.AllocateManaged && (program.Allocation is null || program.Length is not null))
        { throw new InvalidDataException("MalformedResourceAllocation"); }
        if (program.Allocation is { } allocation)
        {
            if (program.Kind == ResourceProgramKind.AllocateArray
                ? allocation.Kind != ResourceAllocationKind.Array || !allocation.Extent.Equals(program.Length)
                : program.Kind != ResourceProgramKind.AllocateManaged || allocation.Kind is ResourceAllocationKind.Unspecified or ResourceAllocationKind.Array ||
                    !Enum.IsDefined(typeof(ResourceAllocationKind), allocation.Kind))
            { throw new InvalidDataException("MalformedResourceAllocation"); }
            ResourceExpressionOwnership.Validate(allocation.Extent, callableId, owner, cancellation);
        }
        if (program.Rank is { } rank)
        {
            ResourceExpressionOwnership.Validate(rank.Initial, callableId, owner, cancellation);
            ResourceExpressionOwnership.Validate(rank.Limit, callableId, owner, cancellation);
            ResourceExpressionOwnership.Validate(rank.LowerInitial, callableId, owner, cancellation);
            ResourceExpressionOwnership.Validate(rank.LowerLimit, callableId, owner, cancellation);
        }
        foreach (var child in program.Children)
        { Visit(child, callableId, owner, cancellation); }
    }
}
