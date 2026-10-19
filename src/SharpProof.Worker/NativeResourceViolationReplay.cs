using System.Numerics;

namespace SharpProof.Worker;

// Search supplies candidates only. Every violation requires an owned, exact
// execution prefix satisfying the entry constraints, not an envelope estimate.
internal static class NativeResourceViolationReplay
{
    private static readonly int[] SampleSizes = [-1, 0, 1, 2, 3, 4, 8, 16, 32, 64, 128];

    internal static async Task<WorkerClaimResult?> FindAsync(CompilerCallablePreparation target,
        CompilerEffectClaimArtifact claim, ResourceExpression limit, WorkerBudgets budgets, CancellationToken cancellation)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        claim = ArgumentNullGuard.NotNull(claim, nameof(claim));
        limit = ArgumentNullGuard.NotNull(limit, nameof(limit));
        budgets = ArgumentNullGuard.NotNull(budgets, nameof(budgets));
        var constraint = claim.Constraint.ResourceUpperBound!;
        if (constraint.Metric is not (WorkerResourceMetric.LoopBodyEntries or WorkerResourceMetric.ArrayAllocations or WorkerResourceMetric.AllocatedElements or WorkerResourceMetric.AllocatedBytes))
        { return null; }
        var layout = constraint.Metric == WorkerResourceMetric.AllocatedBytes ? new NativeResourceLayoutBinding(target).Bind() : null;
        if (constraint.Metric == WorkerResourceMetric.AllocatedBytes && layout is null)
        { return null; }
        var owner = target.Total!;
        var candidate = PassiveCallableArtifactAdapter.Enroll(target)!;
        if (candidate.Requires.Any(clause => IrTermAnalysis.GetDepth(clause.Value) > budgets.MaximumExpressionDepth ||
            IrTermAnalysis.GetDepth(clause.Safe) > budgets.MaximumExpressionDepth) ||
            !PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out _, cancellation))
        { return null; }
        // Cheap independent candidates cover small executions and constant
        // thresholds. The same concrete entry validator guards solver models.
        if (owner.Parameters.Length <= 32)
        {
            foreach (var size in SampleSizes)
            {
                cancellation.ThrowIfCancellationRequested();
                var result = Replay(Sample(owner, size));
                if (result is not null)
                { return result; }
            }
            var threshold = limit.Evaluate(static _ => BigInteger.Zero, cancellation) + 1;
            if (threshold <= 1024 && !SampleSizes.Contains((int)threshold))
            {
                var result = Replay(Sample(owner, (int)threshold));
                if (result is not null)
                { return result; }
            }
        }
        // Native feasibility/allocation queries can supply distinct values for
        // parameters constrained by Requires. Their verdict is not a cost proof.
        using var solver = new PassiveCallableSolver(plan!, budgets.QueryRlimit, budgets.MethodRlimit);
        var entry = await solver.VerifyEntryAsync(cancellation).ConfigureAwait(false);
        if (entry.Outcome is not RefutedOutcome)
        { return null; }
        var initial = Replay(entry.EntryModel);
        if (initial is not null)
        { return initial; }
        var allocation = await solver.VerifyAllocationsAsync(cancellation).ConfigureAwait(false);
        return allocation.Outcome is RefutedOutcome ? Replay(allocation.EntryModel) : null;

        WorkerClaimResult? Replay(ImmutableDictionary<IrVarId, IrValue> inputs)
        {
            ArgumentNullException.ThrowIfNull(inputs);
            var factory = owner.Program.Factory;
            foreach (var parameter in owner.Parameters)
            {
                if (!inputs.TryGetValue(parameter.Entry, out var value) || value.Type != factory.GetVariableInfo(parameter.Entry).Type)
                { return null; }
            }
            if (owner.HasReceiver && inputs[owner.Parameters[owner.Parameters.Length - 1].Entry].Kind == IrValueKind.Null ||
                !CallableReplayValidator.EntryConstraintsHold(factory, candidate.EntryConstraints, inputs, cancellation))
            { return null; }
            foreach (var clause in candidate.Requires)
            {
                if (!CallableReplayValidator.EntryConstraintsHold(factory, [clause.Safe, clause.Value], inputs, cancellation))
                { return null; }
            }
            var evaluatedLimit = limit.Evaluate(input =>
            {
                ArgumentNullException.ThrowIfNull(input);
                var value = inputs[owner.Parameters[input.Ordinal].Entry];
                return input.Kind switch
                {
                    ResourceInputKind.NonNegativeScalar => value.IntegerNumericValue,
                    ResourceInputKind.ArrayLength when value.Kind == IrValueKind.Null => BigInteger.Zero,
                    ResourceInputKind.ArrayLength => value.Elements.Length,
                    ResourceInputKind.StringLength when value.Kind == IrValueKind.Null => BigInteger.Zero,
                    ResourceInputKind.StringLength => value.String.Length,
                    _ => throw new InvalidDataException("Unsupported resource input during replay.")
                };
            }, cancellation);
            var unsupportedByteStorage = false;
            BigInteger observed = 0;
            OperationId? site = null;
            BigInteger violatingPrefix = 0;
            void Observe(OperationId operation, BigInteger amount, bool approximation)
            {
                RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
                if (approximation || unsupportedByteStorage || site is not null)
                { return; }
                observed += amount;
                if (observed > evaluatedLimit)
                {
                    site = operation;
                    violatingPrefix = observed;
                }
            }
            plan!.ReplayResources(inputs, (instruction, elements, approximation) =>
            {
                ArgumentNullException.ThrowIfNull(instruction);
                if (constraint.Metric == WorkerResourceMetric.AllocatedBytes)
                {
                    if (approximation || site is not null)
                    { return; }
                    var bytes = ExactArrayBytes(factory, instruction, elements, layout!, cancellation);
                    if (bytes is null)
                    { unsupportedByteStorage = true; return; }
                    Observe(instruction.Operation, bytes.Value, false);
                    return;
                }
                if (constraint.Metric != WorkerResourceMetric.LoopBodyEntries)
                { Observe(instruction.Operation, constraint.Metric == WorkerResourceMetric.ArrayAllocations ? 1 : elements, approximation); }
            }, (instruction, characters, approximation) =>
            {
                ArgumentNullException.ThrowIfNull(instruction);
                if (constraint.Metric != WorkerResourceMetric.AllocatedBytes || approximation || site is not null)
                { return; }
                var shape = ResourceAllocationShape.String(ResourceExpression.Literal(characters));
                var bound = ResourceRuntimeLayout.Bound(shape, layout!, cancellation);
                if (bound.Bounds is not { IsExact: true, Lower.Constant: { } bytes })
                { unsupportedByteStorage = true; return; }
                Observe(instruction.Operation, bytes, false);
            }, (instruction, approximation) =>
            {
                ArgumentNullException.ThrowIfNull(instruction);
                if (constraint.Metric != WorkerResourceMetric.AllocatedBytes || instruction.Length is not null || approximation || site is not null)
                { return; }
                if (instruction.Target is null || factory.GetTypeInfo(instruction.AllocatedType).Kind != IrTypeKind.Reference)
                { unsupportedByteStorage = true; return; }
                // Certified delegates have a reviewed fixed footprint. Other
                // fresh references contribute only the minimum instance cost.
                Observe(instruction.Operation, factory.IsDelegateReferenceType(instruction.AllocatedType) ? ResourceRuntimeLayout.DelegateBytes : ResourceRuntimeLayout.MinimumObject, false);
            }, (instruction, approximation) =>
            {
                ArgumentNullException.ThrowIfNull(instruction);
                if (constraint.Metric == WorkerResourceMetric.LoopBodyEntries)
                { Observe(instruction.Operation, BigInteger.One, approximation); }
            }, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (site is not { } operation || owner.Program.Factory.GetOperationInfo(operation).SourceSpan is not { Line: > 0 } span)
            { return null; }
            var result = CallableClaimResultAssembler.Create(target, claim.ClaimId, WorkerClaimOutcome.Refuted,
                WorkerClaimReason.None, WorkerEffectEvidenceCertainty.DefiniteViolation);
            var observedText = violatingPrefix.ToString(CultureInfo.InvariantCulture);
            var limitText = evaluatedLimit.ToString(CultureInfo.InvariantCulture);
            result.ResourceBound = new()
            {
                Metric = constraint.Metric,
                CostModel = constraint.CostModel,
                DeclaredBoundSha256 = WorkerProtocolJson.ComputeSha256(constraint.UpperBound),
                ObservedLowerBound = observedText,
                EvaluatedUpperBound = limitText
            };
            result.EffectWitness = new()
            {
                Kind = "resource-bound-exceeded",
                Detail = constraint.Metric + " prefix " + observedText + " exceeds entry bound " + limitText,
                Effects = constraint.Metric == WorkerResourceMetric.LoopBodyEntries ? WorkerEffectSet.None : WorkerEffectSet.Allocates,
                Location = new() { Path = span.Document, Start = span.Start, Length = span.Length, Line = span.Line, Column = span.Column }
            };
            result.Model = [.. owner.Parameters.Select((parameter, ordinal) =>
            {
                RepositoryParameterGuard.RequireInitialized(parameter.Entry.IsDefault || parameter.Current.IsDefault || parameter.Old.IsDefault,
                    nameof(parameter), "Value must be initialized.");
                var formatted = WorkerProjections.FormatTotalValue(inputs[parameter.Entry]);
                return new WorkerModelValue
                {
                    Variable = owner.HasReceiver && ordinal == owner.Parameters.Length - 1 ? "this" : "parameter:" + ordinal.ToString(CultureInfo.InvariantCulture),
                    Kind = formatted.Kind,
                    Value = formatted.Value
                };
            }).OrderBy(static value => value is null ? throw new ArgumentNullException(nameof(value)) : value.Variable, StringComparer.Ordinal)];
            return result;
        }
    }

    private static BigInteger? ExactArrayBytes(IrFactory factory, IrAllocationInstruction instruction, int elements,
        ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        instruction = ArgumentNullGuard.NotNull(instruction, nameof(instruction));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        if (elements < 0 || factory.GetTypeInfo(instruction.AllocatedType).ElementType is not { } element)
        { return null; }
        var type = factory.GetTypeInfo(element);
        var storage = type.Kind == IrTypeKind.Boolean ? ResourceStorageKind.Byte : type.Kind == IrTypeKind.Integer ? type.Width switch
        {
            8 => ResourceStorageKind.Byte,
            16 => ResourceStorageKind.Word16,
            32 => ResourceStorageKind.Word32,
            64 => ResourceStorageKind.Word64,
            _ => ResourceStorageKind.Unspecified
        } : ResourceStorageKind.Unspecified;
        if (storage == ResourceStorageKind.Unspecified)
        { return null; }
        var shape = ResourceAllocationShape.Array(storage, ResourceExpression.Literal(elements));
        var result = ResourceRuntimeLayout.Bound(shape, layout, cancellation);
        return result.Bounds is { IsExact: true, Lower.Constant: { } bytes } ? bytes : null;
    }

    private static ImmutableDictionary<IrVarId, IrValue> Sample(CompilerTotalCallablePreparation owner, int size)
    {
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        var factory = owner.Program.Factory;
        var values = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        foreach (var parameter in owner.Parameters)
        {
            var type = factory.GetVariableInfo(parameter.Entry).Type;
            var info = factory.GetTypeInfo(type);
            values.Add(parameter.Entry, info.Kind switch
            {
                IrTypeKind.Integer when info.Signed && info.Width is 32 or 64 => factory.CreateIntegerValue(type, (long)size),
                IrTypeKind.Sequence when size >= 0 => factory.CreateSequenceValue(type,
                    Enumerable.Repeat(factory.GetTypeInfo(info.ElementType!.Value).CreateDefaultValue(factory), Math.Min(size, 128))),
                _ => info.CreateDefaultValue(factory)
            });
        }
        return values.ToImmutable();
    }
}
