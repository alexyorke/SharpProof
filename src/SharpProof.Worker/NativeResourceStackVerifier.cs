using SharpProof.Ir;

namespace SharpProof.Worker;

internal sealed record NativeResourceStackBound(ResourceExpression Depth, ResourceExpression StorageBytes)
{
    public ResourceExpression Depth { get; init; } = ArgumentNullGuard.NotNull(Depth, nameof(Depth));
    public ResourceExpression StorageBytes { get; init; } = ArgumentNullGuard.NotNull(StorageBytes, nameof(StorageBytes));
}

internal sealed record NativeResourceStackCheck(NativeResourceStackBound? Bounds, string? UnknownReason);

// Logical source activation records, not JIT frame sizes or process stack usage.
// This kernel has no publication authority; compiler ownership is checked by callers.
internal static class NativeResourceStackVerifier
{
    internal const string ModelId = WorkerResourceModels.StackCostModel;
    private static readonly NativeResourceStackBound Zero = new(ResourceExpression.Zero, ResourceExpression.Zero);

    internal static NativeResourceStackCheck Derive(ResourceProgram program, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        cancellation.ThrowIfCancellationRequested();
        if (program.Kind != ResourceProgramKind.Activation)
        { return new(null, "ResourceStackRootActivationMissing"); }
        try
        { return new(Visit(program, null, cancellation), null); }
        catch (InvalidDataException exception)
        { return new(null, exception.Message); }
        catch (ResourceExpressionLimitException)
        { return new(null, "ResourceStackProofBudgetExceeded"); }
    }

    private static NativeResourceStackBound Visit(ResourceProgram program, ResourceActivationShape? active, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        cancellation.ThrowIfCancellationRequested();
        switch (program.Kind)
        {
            case ResourceProgramKind.Zero:
            case ResourceProgramKind.AllocateArray:
            case ResourceProgramKind.AllocateManaged:
                return Zero;
            case ResourceProgramKind.UpperEnvelope:
                return Visit(program.Children[0], active, cancellation);
            case ResourceProgramKind.Activation:
                if (program.Activation is not { } frame || program.Children.Length != 1)
                { throw new InvalidDataException("ResourceStackFrameStorageUnavailable"); }
                var nested = Visit(program.Children[0], frame, cancellation);
                return new(ResourceExpression.Add(ResourceExpression.One, nested.Depth),
                    ResourceExpression.Add(FrameBytes(frame), nested.StorageBytes));
            case ResourceProgramKind.Sequence:
            case ResourceProgramKind.Choice:
            case ResourceProgramKind.BooleanDistribution:
            case ResourceProgramKind.CreditLoop:
                if (program.Kind == ResourceProgramKind.CreditLoop)
                { _ = NativeResourceAccountingVerifier.Budget(program, cancellation); }
                var left = Visit(program.Children[0], active, cancellation);
                var right = Visit(program.Children[1], active, cancellation);
                return new(ResourceExpression.Max(left.Depth, right.Depth), ResourceExpression.Max(left.StorageBytes, right.StorageBytes));
            case ResourceProgramKind.Repeat:
                var rank = program.Rank ?? throw new InvalidDataException("MalformedResourceRank");
                if (!Enum.IsDefined(program.RepeatKind) || program.RepeatKind == ResourceRepeatKind.TailRecurrence &&
                    (rank.Kind is not (ResourceLoopKind.DecreasingSubtract or ResourceLoopKind.DecreasingDivide) || rank.Limit.Constant != 0))
                { throw new InvalidDataException("UnsupportedResourceRecurrence"); }
                var count = NativeResourceProgramVerifier.CheckRank(rank, cancellation);
                if (count.Constant == 0)
                { return Zero; }
                var body = Visit(program.Children[0], active, cancellation);
                if (program.RepeatKind == ResourceRepeatKind.Loop)
                { return body; }
                if (active is null)
                { throw new InvalidDataException("ResourceStackRecursiveFrameMissing"); }
                // Prefix calls finish before the next recursive activation. Adding
                // their maximum to the retained recursive chain is conservative.
                return new(ResourceExpression.Add(count, body.Depth),
                    ResourceExpression.Add(ResourceExpression.Multiply(count, FrameBytes(active)), body.StorageBytes));
            default:
                throw new InvalidDataException("ResourceStackCallSemanticsUnsupported");
        }
    }

    private static ResourceExpression FrameBytes(ResourceActivationShape shape)
    {
        shape = ArgumentNullGuard.NotNull(shape, nameof(shape));
        return ResourceExpression.Literal(8 * (2 + shape.Variables.Length + (shape.HasReceiver ? 1 : 0) + (shape.HasResult ? 1 : 0)));
    }
}
