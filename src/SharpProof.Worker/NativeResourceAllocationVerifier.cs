namespace SharpProof.Worker;

// Conditional on the supplied layout. This is an internal derivation, not a
// contract verdict: target evidence and the owned layout assumption must still
// be bound by the contract verifier before any byte claim can be published.
internal sealed record NativeResourceAllocationCheck(ResourceExpression? ConditionalUpperBound, string? UnknownReason);

internal static class NativeResourceAllocationVerifier
{
    internal static NativeResourceAllocationCheck Verify(ResourceProgram program, string callableId,
        CompilerTotalCallablePreparation owner, ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        try
        {
            // Reject incompatible contexts even for programs without allocations.
            var contextCheck = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(ResourceStorageKind.Byte,
                ResourceExpression.Zero), layout, cancellation);
            if (!contextCheck.Verified)
            { return new(null, contextCheck.UnknownReason); }
            var semantics = NativeResourceProgramVerifier.Verify(program, callableId, owner, cancellation);
            if (!semantics.Verified)
            { return new(null, semantics.UnknownReason); }
            return new(Visit(program, layout, cancellation), null);
        }
        catch (InvalidDataException exception)
        { return new(null, exception.Message); }
        catch (ResourceExpressionLimitException)
        { return new(null, "ResourceAllocationProofBudgetExceeded"); }
    }

    private static ResourceExpression Visit(ResourceProgram program, ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        switch (program.Kind)
        {
            case ResourceProgramKind.Zero:
                return ResourceExpression.Zero;
            case ResourceProgramKind.Activation:
                return Visit(program.Children[0], layout, cancellation);
            case ResourceProgramKind.AllocateManaged:
            case ResourceProgramKind.AllocateArray:
                if (program.Allocation is not { } allocation)
                { throw new InvalidDataException("MissingResourceAllocationStorage"); }
                var footprint = ResourceRuntimeLayout.Bound(allocation, layout, cancellation);
                if (footprint.Bounds is not { } bounds)
                { throw new InvalidDataException(footprint.UnknownReason); }
                return bounds.Upper;
            case ResourceProgramKind.Sequence:
                return ResourceExpression.Add(Visit(program.Children[0], layout, cancellation), Visit(program.Children[1], layout, cancellation));
            case ResourceProgramKind.Choice:
                return ResourceExpression.Max(Visit(program.Children[0], layout, cancellation), Visit(program.Children[1], layout, cancellation));
            case ResourceProgramKind.Repeat:
                var rank = program.Rank ?? throw new InvalidDataException("MalformedResourceProgram");
                return ResourceExpression.Multiply(NativeResourceProgramVerifier.CheckRank(rank, cancellation),
                    Visit(program.Children[0], layout, cancellation));
            default:
                throw new InvalidDataException("UnsupportedResourceAllocationSemantics");
        }
    }
}
