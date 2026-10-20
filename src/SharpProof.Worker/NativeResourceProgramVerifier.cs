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
    internal NativeResourceCost? LowerCost { get; init; }
    internal string? LowerDomainUnknownReason { get; init; }
    internal bool LowerDomainVerified => Verified && LowerDomainUnknownReason is null;
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
            var upper = Visit(program, callableId, owner, bindMetadata, cancellation);
            return new(upper, null)
            {
                LowerCost = VisitLower(program, cancellation),
                LowerDomainUnknownReason = NativeResourceNormalDomain.Check(program, owner, cancellation)
            };
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
            case ResourceProgramKind.Activation:
            case ResourceProgramKind.UpperEnvelope:
                return Visit(program.Children[0], callableId, owner, bindMetadata, cancellation);
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
            case ResourceProgramKind.BooleanDistribution:
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
            case ResourceProgramKind.CreditLoop:
                var budget = NativeResourceAccountingVerifier.Budget(program, cancellation);
                var outer = CheckRank(program.Rank!, cancellation);
                var prefix = Visit(program.Children[0], callableId, owner, bindMetadata, cancellation);
                var drain = Visit(program.Children[1], callableId, owner, bindMetadata, cancellation);
                return new(ResourceExpression.Add(
                    ResourceExpression.Multiply(outer, ResourceExpression.Add(ResourceExpression.One, prefix.LoopEntries)),
                    ResourceExpression.Multiply(budget, ResourceExpression.Add(ResourceExpression.One, drain.LoopEntries))),
                    ResourceExpression.Add(ResourceExpression.Multiply(outer, prefix.Arrays), ResourceExpression.Multiply(budget, drain.Arrays)),
                    ResourceExpression.Add(ResourceExpression.Multiply(outer, prefix.Elements), ResourceExpression.Multiply(budget, drain.Elements)));
            default:
                throw new InvalidDataException("MalformedResourceProgram");
        }
    }

    // Lower envelopes are source-size ranges, not verdicts. Validate their
    // ownership above and ordering here, then derive the least guaranteed work.
    // A branch contributes only a bound valid on both arms. Counter envelopes
    // and recursive prefixes therefore cannot inherit a parent's upper cost.
    private static NativeResourceCost VisitLower(ResourceProgram program, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        cancellation.ThrowIfCancellationRequested();
        switch (program.Kind)
        {
            case ResourceProgramKind.Activation:
                return VisitLower(program.Children[0], cancellation);
            case ResourceProgramKind.Zero:
            case ResourceProgramKind.MetadataCall:
            case ResourceProgramKind.AllocateManaged:
                return Zero;
            case ResourceProgramKind.UpperEnvelope:
                _ = VisitLower(program.Children[0], cancellation);
                return Zero;
            case ResourceProgramKind.AllocateArray:
                if (program.LowerLength is not { } lower || program.Length is not { } upper || !ResourceBoundOrder.Proves(lower, upper, cancellation))
                { throw new InvalidDataException("ResourceLowerExtentUnproved"); }
                return new(ResourceExpression.Zero, ResourceExpression.One, lower);
            case ResourceProgramKind.Sequence:
            case ResourceProgramKind.Choice:
            case ResourceProgramKind.BooleanDistribution:
                var left = VisitLower(program.Children[0], cancellation);
                var right = VisitLower(program.Children[1], cancellation);
                if (program.Kind == ResourceProgramKind.Sequence)
                {
                    return new(ResourceExpression.Add(left.LoopEntries, right.LoopEntries),
                        ResourceExpression.Add(left.Arrays, right.Arrays), ResourceExpression.Add(left.Elements, right.Elements));
                }
                return new(CommonLower(left.LoopEntries, right.LoopEntries, cancellation),
                    CommonLower(left.Arrays, right.Arrays, cancellation), CommonLower(left.Elements, right.Elements, cancellation));
            case ResourceProgramKind.Repeat:
                var rank = program.Rank ?? throw new InvalidDataException("MalformedResourceProgram");
                if (!ResourceBoundOrder.Proves(rank.LowerInitial, rank.Initial, cancellation) ||
                    !ResourceBoundOrder.Proves(rank.LowerLimit, rank.Limit, cancellation))
                { throw new InvalidDataException("ResourceLowerRankUnproved"); }
                var body = VisitLower(program.Children[0], cancellation);
                // Recurrence prefixes still use the entry-size upper environment.
                // Until a per-activation lower environment is certified, retain zero.
                if (program.RepeatKind == ResourceRepeatKind.TailRecurrence)
                { return Zero; }
                var count = rank.Kind switch
                {
                    ResourceLoopKind.IncreasingAdd => ResourceExpression.DivideCeiling(ResourceExpression.Difference(rank.LowerLimit, rank.Initial.Constant!.Value), rank.Step),
                    ResourceLoopKind.IncreasingMultiply => ResourceExpression.Geometric(rank.LowerLimit, rank.Step, false, cancellation),
                    ResourceLoopKind.DecreasingSubtract => ResourceExpression.DivideCeiling(ResourceExpression.Difference(rank.LowerInitial, rank.Limit.Constant!.Value), rank.Step),
                    ResourceLoopKind.DecreasingDivide => ResourceExpression.Geometric(rank.LowerInitial, rank.Step, true, cancellation),
                    _ => throw new InvalidDataException("MalformedResourceRank")
                };
                return new(ResourceExpression.Multiply(count, ResourceExpression.Add(ResourceExpression.One, body.LoopEntries)),
                    ResourceExpression.Multiply(count, body.Arrays), ResourceExpression.Multiply(count, body.Elements));
            case ResourceProgramKind.CreditLoop:
                _ = VisitLower(program.Children[1], cancellation);
                return VisitLower(ResourceProgram.Repeat(program.Rank!, ResourceRepeatKind.Loop, program.Children[0]), cancellation);
            default:
                throw new InvalidDataException("MalformedResourceProgram");
        }
    }

    private static ResourceExpression CommonLower(ResourceExpression left, ResourceExpression right, CancellationToken cancellation)
    {
        left = ArgumentNullGuard.NotNull(left, nameof(left));
        right = ArgumentNullGuard.NotNull(right, nameof(right));
        if (ResourceBoundOrder.Proves(left, right, cancellation))
        { return left; }
        return ResourceBoundOrder.Proves(right, left, cancellation) ? right : ResourceExpression.Zero;
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
