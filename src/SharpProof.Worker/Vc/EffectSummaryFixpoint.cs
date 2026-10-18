namespace SharpProof.Worker;

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
    internal SourceEffectSummary Join(SourceEffectSummary other /* SPINT001: all values valid - every bitset and completion combination is valid may-effect evidence */)
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
        CompilerReachableSourceArtifact artifact, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        artifact = ArgumentNullGuard.NotNull(artifact, nameof(artifact));
        var bodyBuilder = ImmutableDictionary.CreateBuilder<string, CompilerSourceBodyArtifact>(StringComparer.Ordinal);
        var bodyOrderBuilder = ImmutableArray.CreateBuilder<string>();
        foreach (var body in artifact.Bodies)
        {
            var presentBody = ArgumentNullGuard.NotNull(body, nameof(body));
            bodyBuilder.AddUnique(presentBody.BodyId, presentBody);
            bodyOrderBuilder.Add(presentBody.BodyId);
        }
        var bodies = bodyBuilder.ToImmutable();
        var bodyOrder = bodyOrderBuilder.ToImmutable();
        var edgeBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.Ordinal);
        var predecessorBuilders = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>.Builder>(StringComparer.Ordinal);
        foreach (var id in bodyOrder)
        {
            edgeBuilder.Add(id, bodies[id].Callees.OrderBy(callee => callee is null ? throw new ArgumentNullException(nameof(callee)) : callee, StringComparer.Ordinal).ToImmutableArray());
            predecessorBuilders.Add(id, ImmutableArray.CreateBuilder<string>());
        }
        var edges = edgeBuilder.ToImmutable();
        foreach (var id in bodyOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var callee in edges[id])
            { predecessorBuilders[callee].Add(id); }
        }
        var predecessors = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.Ordinal);
        foreach (var id in bodyOrder)
        { predecessors.Add(id, predecessorBuilders[id].ToImmutable()); }
        var summaries = ImmutableDictionary.CreateBuilder<string, SourceEffectSummary>(StringComparer.Ordinal);
        foreach (var id in bodyOrder)
        { summaries.Add(id, Local(bodies[id], cancellationToken)); }
        var finished = ImmutableArray.CreateBuilder<string>();
        var visited = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var first in bodyOrder.OrderBy(id => id is null ? throw new ArgumentNullException(nameof(id)) : id, StringComparer.Ordinal))
        {
            var pending = ImmutableStack<(string Id, bool Exit)>.Empty.Push((first, false));
            while (!pending.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pending = pending.Pop(out var frame);
                var (id, exit) = frame;
                if (exit)
                { finished.Add(id); continue; }
                if (!visited.Add(id))
                { continue; }
                pending = pending.Push((id, true));
                for (var index = edges[id].Length - 1; index >= 0; index--)
                { pending = pending.Push((edges[id][index], false)); }
            }
        }
        var components = ImmutableArray.CreateBuilder<ImmutableArray<string>>();
        visited.Clear();
        for (var index = finished.Count - 1; index >= 0; index--)
        {
            var component = ImmutableArray.CreateBuilder<string>();
            var pending = ImmutableStack<string>.Empty.Push(finished[index]);
            while (!pending.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pending = pending.Pop(out var id);
                if (!visited.Add(id))
                { continue; }
                component.Add(id);
                foreach (var caller in predecessors[id])
                { pending = pending.Push(caller); }
            }
            if (component.Count != 0)
            { components.Add(component.ToImmutable().OrderBy(id => id is null ? throw new ArgumentNullException(nameof(id)) : id, StringComparer.Ordinal).ToImmutableArray()); }
        }
        // Reverse Kosaraju's source-first components so outgoing summaries
        // are final before each component's monotone worklist runs.
        for (var index = components.Count - 1; index >= 0; index--)
        {
            var component = components[index];
            var members = component.ToImmutableHashSet(StringComparer.Ordinal);
            var cyclic = component.Length > 1 || edges[component[0]].Contains(component[0], StringComparer.Ordinal);
            if (cyclic)
            {
                foreach (var id in component)
                { summaries[id] = summaries[id] with { MayDiverge = true }; }
            }
            var queued = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            queued.UnionWith(component);
            var pending = ImmutableQueue.CreateRange(component);
            while (!pending.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pending = pending.Dequeue(out var id);
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
                    { pending = pending.Enqueue(caller); }
                }
            }
        }
        return summaries.ToImmutable().ToImmutableSortedDictionary(StringComparer.Ordinal);
    }

    private static SourceEffectSummary Local(CompilerSourceBodyArtifact body, CancellationToken cancellationToken)
    {
        var summary = body.CallsComplete && body.EffectsCompleteAtEntry ? SourceEffectSummary.Empty : SourceEffectSummary.Unknown;
        if (body.Graph == null)
        { return summary.Join(SourceEffectSummary.Unknown); }
        var decoded = PortableIrGraphCodec.Decode(body.Graph, externalVariableIndices: null, cancellationToken: cancellationToken);
        foreach (var instruction in decoded.Instructions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            summary = summary.Join(InstructionDelta(decoded.Factory, instruction));
        }
        // Validated nonnull body graphs always include a program.
        return summary with { MayDiverge = summary.MayDiverge || HasCycle(decoded.Program!, cancellationToken) };
    }

    // An instruction delta is not a callable summary. Default is the exact
    // join identity; Local separately retains unresolved callable completion.
    // The caller supplies an instruction owned by this factory.
    internal static SourceEffectSummary InstructionDelta(IrFactory factory, IrInstruction instruction)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(instruction);
        var neutral = default(SourceEffectSummary);
        return instruction switch
        {
            IrAllocationInstruction => neutral with { MayEffects = SourceMayEffect.Allocation },
            IrWriteInstruction write when IrWriteSites.IsObservable(factory, write) =>
                neutral with { MayEffects = SourceMayEffect.NonlocalWrite },
            IrLockInstruction => neutral with { MayEffects = SourceMayEffect.Synchronization },
            IrThrowInstruction thrown => neutral with
            {
                MayEffects = SourceMayEffect.Allocation,
                ExceptionKinds = 1 << (int)thrown.ExceptionKind
            },
            IrCallInstruction => neutral with { UnknownExceptions = true },
            // Scalar values remain exact, but first use of a runtime cache may
            // allocate. This marker does not make other effect facets unknown.
            IrHavocInstruction { Origin: IrHavocOrigin.Approximation, HavocKind: IrHavocKind.Variables } havoc
                when IrOpaqueCallSite.Effects(factory, havoc.Operation) == IrOpaqueCallEffects.Allocates =>
                neutral with { UnknownEffects = SourceMayEffect.Allocation },
            // A runtime limit may fault. The throw it selects records its kind.
            IrHavocInstruction { Origin: IrHavocOrigin.Approximation, HavocKind: IrHavocKind.Variables } havoc
                when IrOpaqueCallSite.Effects(factory, havoc.Operation) == IrOpaqueCallEffects.Throws => neutral,
            IrHavocInstruction { Origin: IrHavocOrigin.Approximation } => SourceEffectSummary.Unknown,
            IrAssignInstruction or IrBranchInstruction or IrGotoInstruction or IrReturnInstruction or
                IrExceptionalExitInstruction or IrWriteInstruction or IrAssumeInstruction or
                IrHavocInstruction { Origin: IrHavocOrigin.Input, HavocKind: IrHavocKind.Variables } => neutral,
            _ => SourceEffectSummary.Unknown
        };
    }

    private static bool HasCycle(IrProgram program, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(program);
        var colors = ImmutableDictionary.CreateBuilder<IrBlockId, int>();
        var pending = ImmutableStack<(IrBlockId Id, bool Exit)>.Empty.Push((program.Entry, false));
        while (!pending.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pending = pending.Pop(out var frame);
            var (id, exit) = frame;
            if (exit)
            { colors[id] = 2; continue; }
            if (colors.TryGetValue(id, out var color))
            {
                if (color == 1)
                { return true; }
                continue;
            }
            colors[id] = 1;
            pending = pending.Push((id, true));
            foreach (var target in Targets(program.GetBlock(id).Terminator))
            { pending = pending.Push((target, false)); }
        }
        return false;
    }

    private static IEnumerable<IrBlockId> Targets(IrInstruction instruction)
    {
        System.ArgumentNullException.ThrowIfNull(instruction);
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
