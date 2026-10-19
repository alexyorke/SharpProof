using System.Numerics;

namespace SharpProof.Worker;

internal sealed record NativeResourceCost(ResourceExpression LoopEntries, ResourceExpression Arrays, ResourceExpression Elements)
{
    public ResourceExpression LoopEntries { get; init; } = ArgumentNullGuard.NotNull(LoopEntries, nameof(LoopEntries));
    public ResourceExpression Arrays { get; init; } = ArgumentNullGuard.NotNull(Arrays, nameof(Arrays));
    public ResourceExpression Elements { get; init; } = ArgumentNullGuard.NotNull(Elements, nameof(Elements));
}

internal sealed record NativeResourceProgramCheck(NativeResourceCost? Cost, string? UnknownReason)
{
    internal bool Verified => Cost is not null;
}

// This checks the closed resource semantics, not producer-computed bounds.
// External source ownership still requires the validated compiler envelope.
internal static class NativeResourceProgramVerifier
{
    private static readonly NativeResourceCost Zero = new(ResourceExpression.Zero, ResourceExpression.Zero, ResourceExpression.Zero);

    internal static NativeResourceProgramCheck Verify(ResourceProgram program, string callableId,
        CompilerTotalCallablePreparation owner, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        return Verify(program, callableId, owner, null, cancellation);
    }

    internal static NativeResourceProgramCheck Verify(ResourceProgram program, string callableId,
        CompilerTotalCallablePreparation owner, Func<ResourceMetadataReference, bool>? bindMetadata, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        cancellation.ThrowIfCancellationRequested();
        try
        {
            ResourceProgramOwnership.Validate(program, callableId, owner, cancellation);
            return new(Visit(program, callableId, owner, bindMetadata, cancellation), null);
        }
        catch (InvalidDataException exception)
        { return new(null, exception.Message); }
        catch (ResourceExpressionLimitException)
        { return new(null, "ResourceProofBudgetExceeded"); }
    }

    private static NativeResourceCost Visit(ResourceProgram program, string callableId,
        CompilerTotalCallablePreparation owner, Func<ResourceMetadataReference, bool>? bindMetadata, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        cancellation.ThrowIfCancellationRequested();
        switch (program.Kind)
        {
            case ResourceProgramKind.MetadataBoundary:
                throw new InvalidDataException("ResourceMetadataAssumptionsNotBound");
            case ResourceProgramKind.MetadataCall:
                if (program.Metadata is not { } metadata || bindMetadata?.Invoke(metadata) != true)
                { throw new InvalidDataException("ResourceMetadataAssumptionsNotBound"); }
                return Zero;
            case ResourceProgramKind.AllocateManaged:
            case ResourceProgramKind.Zero:
                return Zero;
            case ResourceProgramKind.AllocateArray:
                var length = program.Length ?? throw new InvalidDataException("MalformedResourceProgram");
                ResourceExpressionOwnership.Validate(length, callableId, owner, cancellation);
                return new(ResourceExpression.Zero, ResourceExpression.One, length);
            case ResourceProgramKind.Sequence:
            case ResourceProgramKind.Choice:
                var left = Visit(program.Children[0], callableId, owner, bindMetadata, cancellation);
                var right = Visit(program.Children[1], callableId, owner, bindMetadata, cancellation);
                return program.Kind == ResourceProgramKind.Sequence
                    ? new(ResourceExpression.Add(left.LoopEntries, right.LoopEntries), ResourceExpression.Add(left.Arrays, right.Arrays), ResourceExpression.Add(left.Elements, right.Elements))
                    : new(ResourceExpression.Max(left.LoopEntries, right.LoopEntries), ResourceExpression.Max(left.Arrays, right.Arrays), ResourceExpression.Max(left.Elements, right.Elements));
            case ResourceProgramKind.Repeat:
                var rank = program.Rank ?? throw new InvalidDataException("MalformedResourceProgram");
                ResourceExpressionOwnership.Validate(rank.Initial, callableId, owner, cancellation);
                ResourceExpressionOwnership.Validate(rank.Limit, callableId, owner, cancellation);
                if (!Enum.IsDefined(program.RepeatKind) ||
                    program.RepeatKind == ResourceRepeatKind.TailRecurrence &&
                    (rank.Kind is not (ResourceLoopKind.DecreasingSubtract or ResourceLoopKind.DecreasingDivide) || rank.Limit.Constant != 0))
                { throw new InvalidDataException("UnsupportedResourceRecurrence"); }
                var count = CheckRank(rank, cancellation);
                var body = Visit(program.Children[0], callableId, owner, bindMetadata, cancellation);
                var entries = program.RepeatKind == ResourceRepeatKind.Loop ? ResourceExpression.Add(ResourceExpression.One, body.LoopEntries) : body.LoopEntries;
                return new(ResourceExpression.Multiply(count, entries), ResourceExpression.Multiply(count, body.Arrays), ResourceExpression.Multiply(count, body.Elements));
            default:
                throw new InvalidDataException("MalformedResourceProgram");
        }
    }

    internal static ResourceExpression CheckRank(ResourceLoopRank rank, CancellationToken cancellation)
    {
        rank = ArgumentNullGuard.NotNull(rank, nameof(rank));
        cancellation.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(rank.Kind) || rank.CounterWidth is not (32 or 64) ||
            rank.Step <= 0 || rank.Step > long.MaxValue)
        { throw new InvalidDataException("MalformedResourceRank"); }
        var maximum = (BigInteger.One << (rank.CounterWidth - 1)) - 1;
        var increasing = rank.Kind is ResourceLoopKind.IncreasingAdd or ResourceLoopKind.IncreasingMultiply;
        if (increasing)
        {
            if (rank.Initial.Constant is not { } start || start > maximum || rank.Limit.Maximum is not { } limit || limit > maximum)
            { throw new InvalidDataException("UnsupportedResourceRankDomain"); }
            if (!rank.CheckedStep && (rank.Kind == ResourceLoopKind.IncreasingAdd
                ? limit - 1 + rank.Step > maximum : (limit - 1) * rank.Step > maximum))
            { throw new InvalidDataException("ResourceCounterMayOverflow"); }
            if (rank.Kind == ResourceLoopKind.IncreasingAdd)
            { return ResourceExpression.DivideCeiling(ResourceExpression.Difference(rank.Limit, start), rank.Step); }
            if (start != 1 || rank.Step <= 1)
            { throw new InvalidDataException("UnprovedResourceProgress"); }
            return ResourceExpression.Geometric(rank.Limit, rank.Step, false, cancellation);
        }
        if (rank.Limit.Constant is not { } lower || lower > maximum || rank.Initial.Maximum is not { } initial || initial > maximum)
        { throw new InvalidDataException("UnsupportedResourceRankDomain"); }
        if (rank.Kind == ResourceLoopKind.DecreasingSubtract)
        {
            if (!rank.CheckedStep && rank.Step > maximum)
            { throw new InvalidDataException("ResourceCounterMayOverflow"); }
            return ResourceExpression.DivideCeiling(ResourceExpression.Difference(rank.Initial, lower), rank.Step);
        }
        if (lower != 0 || rank.Step <= 1)
        { throw new InvalidDataException("UnprovedResourceProgress"); }
        return ResourceExpression.Geometric(rank.Initial, rank.Step, true, cancellation);
    }
}
