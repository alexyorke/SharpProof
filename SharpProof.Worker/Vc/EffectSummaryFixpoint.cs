using System.Numerics;

namespace SharpProof.Worker;

// Advisory transfer for Total integer terms. These facts do not enter VCs.
// UInt64 cannot be represented by the signed Int64 interval carrier.
internal static class CoreIrScalarIntervalTransfer
{
    internal static bool TryTypeRange(IrTypeInfo type, out IntervalValue range)
    {
        range = IntervalValue.Bottom;
        if (type.Kind != IrTypeKind.Integer ||
            type.Width is not (8 or 16 or 32 or 64) ||
            type.Width == 64 && !type.Signed)
        {
            return false;
        }
        var magnitude = BigInteger.One << (type.Signed ? type.Width - 1 : type.Width);
        range = IntervalValue.Range(type.Signed ? (long)-magnitude : 0, (long)(magnitude - 1));
        return true;
    }

    internal static IntervalValue Binary(IrBinaryOperator operation, IrTypeInfo type,
        IntervalValue left, IntervalValue right)
    {
        var range = TypeRange(type);
        left = Restrict(left, range);
        right = Restrict(right, range);
        if (left.IsBottom || right.IsBottom)
        {
            return IntervalValue.Bottom;
        }
        if (operation is not (IrBinaryOperator.Add or IrBinaryOperator.Subtract or
            IrBinaryOperator.Multiply or IrBinaryOperator.Divide or IrBinaryOperator.Remainder))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }
        if (left.IsSingleton && right.IsSingleton)
        {
            BigInteger first = left.SingletonValue;
            BigInteger second = right.SingletonValue;
            var value = operation switch
            {
                IrBinaryOperator.Add => first + second,
                IrBinaryOperator.Subtract => first - second,
                IrBinaryOperator.Multiply => first * second,
                IrBinaryOperator.Divide when second.IsZero =>
                    type.Signed && first.Sign < 0 ? BigInteger.One : (BigInteger.One << type.Width) - 1,
                IrBinaryOperator.Divide => first / second,
                IrBinaryOperator.Remainder when second.IsZero => first,
                _ => first % second
            };
            return IntervalValue.Constant(Wrap(value, type));
        }
        BigInteger lowerLeft = left.LowerBound ?? range.LowerBound ?? long.MinValue;
        BigInteger upperLeft = left.UpperBound ?? range.UpperBound ?? long.MaxValue;
        BigInteger lowerRight = right.LowerBound ?? range.LowerBound ?? long.MinValue;
        BigInteger upperRight = right.UpperBound ?? range.UpperBound ?? long.MaxValue;
        if (operation == IrBinaryOperator.Add)
        {
            return Bounded(lowerLeft + lowerRight, upperLeft + upperRight, range);
        }
        if (operation == IrBinaryOperator.Subtract)
        {
            return Bounded(lowerLeft - upperRight, upperLeft - lowerRight, range);
        }
        if (operation == IrBinaryOperator.Multiply)
        {
            var first = lowerLeft * lowerRight;
            var second = lowerLeft * upperRight;
            var third = upperLeft * lowerRight;
            var fourth = upperLeft * upperRight;
            return Bounded(BigInteger.Min(BigInteger.Min(first, second), BigInteger.Min(third, fourth)),
                BigInteger.Max(BigInteger.Max(first, second), BigInteger.Max(third, fourth)), range);
        }
        return range;
    }

    internal static IntervalValue Negate(IrTypeInfo type, IntervalValue operand)
    {
        var range = TypeRange(type);
        operand = Restrict(operand, range);
        if (operand.IsBottom)
        {
            return operand;
        }
        if (operand.IsSingleton)
        {
            return IntervalValue.Constant(Wrap(-(BigInteger)operand.SingletonValue, type));
        }
        return Bounded(-(BigInteger)(operand.UpperBound ?? range.UpperBound ?? long.MaxValue),
            -(BigInteger)(operand.LowerBound ?? range.LowerBound ?? long.MinValue), range);
    }

    internal static IntervalValue Cast(IrTypeInfo source, IrTypeInfo target, IntervalValue operand)
    {
        var sourceRange = TypeRange(source);
        var targetRange = TypeRange(target);
        operand = Restrict(operand, sourceRange);
        if (operand.IsBottom)
        {
            return operand;
        }
        if (operand.IsSingleton)
        {
            return IntervalValue.Constant(Wrap(operand.SingletonValue, target));
        }
        var lower = operand.LowerBound ?? sourceRange.LowerBound ?? long.MinValue;
        var upper = operand.UpperBound ?? sourceRange.UpperBound ?? long.MaxValue;
        return lower >= (targetRange.LowerBound ?? long.MinValue) &&
            upper <= (targetRange.UpperBound ?? long.MaxValue)
            ? operand : targetRange;
    }

    private static IntervalValue TypeRange(IrTypeInfo type)
    {
        return TryTypeRange(type, out var range) ? range :
            throw new ArgumentException("The type has no signed Int64 interval representation.", nameof(type));
    }

    private static IntervalValue Restrict(IntervalValue value, IntervalValue range)
    {
        return IntervalDomain.Instance.AssumeAtMost(
            IntervalDomain.Instance.AssumeAtLeast(value, range.LowerBound ?? long.MinValue),
            range.UpperBound ?? long.MaxValue);
    }

    private static IntervalValue Bounded(BigInteger lower, BigInteger upper, IntervalValue range)
    {
        return lower >= (range.LowerBound ?? long.MinValue) &&
            upper <= (range.UpperBound ?? long.MaxValue)
            ? IntervalValue.Range((long)lower, (long)upper) : range;
    }

    private static long Wrap(BigInteger value, IrTypeInfo type)
    {
        var modulus = BigInteger.One << type.Width;
        value %= modulus;
        if (value.Sign < 0)
        {
            value += modulus;
        }
        if (type.Signed && value >= (modulus >> 1))
        {
            value -= modulus;
        }
        return (long)value;
    }
}

[Flags]
internal enum SourceMayEffect
{
    None = 0,
    Allocation = 1,
    NonlocalWrite = 2,
    Synchronization = 4,
    Capabilities = 8,
    All = Allocation | NonlocalWrite | Synchronization | Capabilities
}

// A may-effect summary is not proof evidence. Completion remains unresolved;
// recursion alone does not invent allocation or write effects.
internal readonly record struct SourceEffectSummary(SourceMayEffect MayEffects, SourceMayEffect UnknownEffects,
    int ExceptionKinds, bool UnknownExceptions, bool MayDiverge, bool CompletionUnknown)
{
    internal static SourceEffectSummary Empty => new(SourceMayEffect.None, SourceMayEffect.None, 0, false, false, true);
    internal static SourceEffectSummary Unknown => new(SourceMayEffect.None, SourceMayEffect.All, 0, true, true, true);
    internal SourceEffectSummary Join(SourceEffectSummary other)
    {
        return new(MayEffects | other.MayEffects, UnknownEffects | other.UnknownEffects, ExceptionKinds | other.ExceptionKinds,
            UnknownExceptions || other.UnknownExceptions, MayDiverge || other.MayDiverge,
            CompletionUnknown || other.CompletionUnknown);
    }
}

internal static class EffectSummaryFixpoint
{
    // The caller supplies the table already checked at the artifact boundary.
    // No summaries from this shadow consumer enter native proof enrollment.
    internal static ImmutableSortedDictionary<string, SourceEffectSummary> ComputeValidated(
        CompilerReachableSourceArtifact artifact, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bodies = artifact.Bodies.ToDictionary(body => body.BodyId, StringComparer.Ordinal);
        var edges = bodies.ToDictionary(pair => pair.Key,
            pair => pair.Value.Callees.OrderBy(id => id, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var predecessors = bodies.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var pair in edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var callee in pair.Value)
            { predecessors[callee].Add(pair.Key); }
        }
        var summaries = bodies.ToDictionary(pair => pair.Key,
            pair => Local(pair.Value, cancellationToken), StringComparer.Ordinal);
        var finished = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var first in bodies.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            var pending = new Stack<(string Id, bool Exit)>();
            pending.Push((first, false));
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (id, exit) = pending.Pop();
                if (exit)
                { finished.Add(id); continue; }
                if (!visited.Add(id))
                { continue; }
                pending.Push((id, true));
                for (var index = edges[id].Length - 1; index >= 0; index--)
                { pending.Push((edges[id][index], false)); }
            }
        }
        var components = new List<string[]>();
        visited.Clear();
        for (var index = finished.Count - 1; index >= 0; index--)
        {
            var component = new List<string>();
            var pending = new Stack<string>();
            pending.Push(finished[index]);
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = pending.Pop();
                if (!visited.Add(id))
                { continue; }
                component.Add(id);
                foreach (var caller in predecessors[id])
                { pending.Push(caller); }
            }
            if (component.Count != 0)
            { components.Add(component.OrderBy(id => id, StringComparer.Ordinal).ToArray()); }
        }
        // Reverse Kosaraju's source-first components so outgoing summaries
        // are final before each component's monotone worklist runs.
        for (var index = components.Count - 1; index >= 0; index--)
        {
            var component = components[index];
            var members = new HashSet<string>(component, StringComparer.Ordinal);
            var cyclic = component.Length > 1 || edges[component[0]].Contains(component[0], StringComparer.Ordinal);
            if (cyclic)
            {
                foreach (var id in component)
                { summaries[id] = summaries[id] with { MayDiverge = true }; }
            }
            var queued = new HashSet<string>(component, StringComparer.Ordinal);
            var pending = new Queue<string>(component);
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = pending.Dequeue();
                queued.Remove(id);
                var joined = summaries[id];
                foreach (var callee in edges[id])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    joined = joined.Join(summaries[callee]);
                }
                if (joined == summaries[id])
                { continue; }
                summaries[id] = joined;
                foreach (var caller in predecessors[id])
                {
                    if (members.Contains(caller) && queued.Add(caller))
                    { pending.Enqueue(caller); }
                }
            }
        }
        return summaries.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }

    private static SourceEffectSummary Local(CompilerSourceBodyArtifact body, CancellationToken cancellationToken)
    {
        var summary = body.CallsComplete && body.EffectsCompleteAtEntry ? SourceEffectSummary.Empty : SourceEffectSummary.Unknown;
        if (body.Graph == null)
        { return summary.Join(SourceEffectSummary.Unknown); }
        var decoded = PortableIrGraphCodec.Decode(body.Graph, cancellationToken: cancellationToken);
        foreach (var instruction in decoded.Instructions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            summary = instruction switch
            {
                IrAllocationInstruction => summary with { MayEffects = summary.MayEffects | SourceMayEffect.Allocation },
                IrWriteInstruction write when write.Region != IrWriteRegion.Local =>
                    summary with { MayEffects = summary.MayEffects | SourceMayEffect.NonlocalWrite },
                IrLockInstruction => summary with { MayEffects = summary.MayEffects | SourceMayEffect.Synchronization },
                IrThrowInstruction thrown => summary with
                {
                    MayEffects = summary.MayEffects | SourceMayEffect.Allocation,
                    ExceptionKinds = summary.ExceptionKinds | (1 << (int)thrown.ExceptionKind)
                },
                IrCallInstruction => summary with { UnknownExceptions = true },
                IrHavocInstruction { Origin: IrHavocOrigin.Approximation } => summary.Join(SourceEffectSummary.Unknown),
                IrAssignInstruction or IrBranchInstruction or IrGotoInstruction or IrReturnInstruction or
                    IrExceptionalExitInstruction or IrWriteInstruction or IrAssumeInstruction or
                    IrHavocInstruction { Origin: IrHavocOrigin.Input, HavocKind: IrHavocKind.Variables } => summary,
                _ => summary.Join(SourceEffectSummary.Unknown)
            };
        }
        return summary with { MayDiverge = summary.MayDiverge || HasCycle(decoded.Program!, cancellationToken) };
    }

    private static bool HasCycle(IrProgram program, CancellationToken cancellationToken)
    {
        var colors = new Dictionary<IrBlockId, int>();
        var pending = new Stack<(IrBlockId Id, bool Exit)>();
        pending.Push((program.Entry, false));
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (id, exit) = pending.Pop();
            if (exit)
            { colors[id] = 2; continue; }
            if (colors.TryGetValue(id, out var color))
            {
                if (color == 1)
                { return true; }
                continue;
            }
            colors[id] = 1;
            pending.Push((id, true));
            foreach (var target in Targets(program.GetBlock(id).Terminator))
            { pending.Push((target, false)); }
        }
        return false;
    }

    private static IEnumerable<IrBlockId> Targets(IrInstruction instruction)
    {
        switch (instruction)
        {
            case IrGotoInstruction go:
                yield return go.Target;
                break;
            case IrBranchInstruction branch:
                yield return branch.WhenTrue;
                yield return branch.WhenFalse;
                break;
            case IrThrowInstruction thrown:
                yield return thrown.Target;
                break;
        }
    }
}
