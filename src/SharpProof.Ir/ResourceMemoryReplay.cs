namespace SharpProof.Ir;

// Observed execution values, not a producer-computed allocation envelope.
internal sealed class ResourceMemoryReplay
{
    private readonly IrProgram _program;
    private readonly List<(IrInstruction Instruction, IrValue? Value)> _observed = [];
    private string? _failure;

    internal ResourceMemoryReplay(IrProgram program)
    { _program = ArgumentNullGuard.NotNull(program, nameof(program)); }

    internal void Observe(IrInstruction instruction, IrValue? value, bool approximation)
    {
        instruction = ArgumentNullGuard.NotNull(instruction, nameof(instruction));
        if (_failure != null)
        { return; }
        if (approximation)
        { _failure = "MemoryReplayApproximation"; return; }
        if (_observed.Count >= ResourceMemoryTrace.MaximumEvents / 4)
        { _failure = "MemoryReplayBudgetExceeded"; return; }
        _observed.Add((instruction, value));
    }

    internal ResourceMemoryCheck Complete(IrProgramExecutionResult execution, ResourceRuntimeLayoutContext layout,
        CancellationToken cancellation)
    {
        execution = ArgumentNullGuard.NotNull(execution, nameof(execution));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        if (_program.Factory.Semantics != IrExecutionSemantics.Total)
        { return Unknown("MemoryReplayRequiresTotalSemantics"); }
        if (_failure != null)
        { return Unknown(_failure); }
        if (execution.Status != IrProgramExecutionStatus.Returned || execution.ConsumedApproximation ||
            _observed.Count == 0 || _observed[_observed.Count - 1].Instruction is not IrReturnInstruction ||
            !ReferenceEquals(execution.Instruction, _observed[_observed.Count - 1].Instruction) ||
            !ReferenceEquals(execution.ReturnValue, _observed[_observed.Count - 1].Value))
        { return Unknown("MemoryReplayIncomplete"); }
        if (_program.Blocks.Length > ResourceMemoryTrace.MaximumEvents / 4)
        { return Unknown("MemoryReplayBudgetExceeded"); }
        var owned = new HashSet<IrInstruction>();
        foreach (var block in _program.Blocks)
        {
            cancellation.ThrowIfCancellationRequested();
            if (block.Instructions.Length > ResourceMemoryTrace.MaximumEvents - owned.Count)
            { return Unknown("MemoryReplayBudgetExceeded"); }
            foreach (var instruction in block.Instructions)
            { owned.Add(instruction); }
        }
        var seen = new HashSet<IrInstruction>();
        var lastUse = new Dictionary<IrVarId, int>();
        for (var index = 0; index < _observed.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var instruction = _observed[index].Instruction;
            if (!owned.Contains(instruction) || !seen.Add(instruction))
            { return Unknown("MemoryReplayInstructionUnowned"); }
            if (instruction is IrBranchInstruction)
            { continue; }
            var reads = 0;
            foreach (var term in IrInstructionFacts.ReadTerms(instruction))
            {
                if (++reads > ResourceMemoryTrace.MaximumEvents)
                { return Unknown("MemoryReplayBudgetExceeded"); }
                if (!ReadVariables(term, lastUse, index, 0))
                { return Unknown("MemoryReplayValueUnsupported"); }
            }
        }
        var factory = _program.Factory;
        var identities = new List<IrValue>();
        var slots = new Dictionary<IrVarId, int>();
        var actualValues = new Dictionary<IrVarId, IrValue>();
        var objects = new HashSet<int>();
        var fieldSlots = new Dictionary<IrMemberId, int>();
        var liveRoots = new HashSet<IrVarId>();
        var events = ImmutableArray.CreateBuilder<ResourceMemoryEvent>();
        var output = 0;
        for (var index = 0; index < _observed.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var (instruction, value) = _observed[index];
            switch (instruction)
            {
                case IrAllocationInstruction objectAllocation when objectAllocation.Length == null:
                    if (objectAllocation.Target is not { } objectTarget || value is not { Kind: IrValueKind.Reference } ||
                        objectAllocation.AllocatedType != value.Type || Find(value, identities) != 0 || !objectAllocation.InitialValues.IsEmpty)
                    { return Unknown("MemoryReplayAllocationUnsupported"); }
                    ResourceAllocationShape objectShape;
                    if (objectAllocation.AllocatedType == factory.ObjectType)
                    { objectShape = ResourceAllocationShape.Object([]); }
                    else if (factory.TryGetObjectStorageType(objectAllocation.AllocatedType, out var fields))
                    { objectShape = ResourceAllocationShape.Object(fields); }
                    else
                    { return Unknown("MemoryReplayObjectStorageUnavailable"); }
                    var objectFootprint = ResourceRuntimeLayout.Bound(objectShape, layout, cancellation);
                    if (objectFootprint.Bounds is not { Lower.Constant: { } objectLower, Upper.Constant: { } objectUpper })
                    { return Unknown(objectFootprint.UnknownReason ?? "MemoryReplayAllocationUnsupported"); }
                    identities.Add(value);
                    actualValues[objectTarget] = value;
                    objects.Add(identities.Count);
                    events.Add(new(ResourceMemoryEventKind.Allocate, identities.Count, Slot(objectTarget, slots), 0, objectLower, objectUpper));
                    liveRoots.Add(objectTarget);
                    break;
                case IrAllocationInstruction allocation:
                    if (allocation.Target is not { } target || value is not { Kind: IrValueKind.Sequence } ||
                        allocation.AllocatedType != value.Type || !allocation.InitialValues.IsEmpty ||
                        factory.GetTypeInfo(value.Type).ElementType is not { } element || Find(value, identities) != 0)
                    { return Unknown("MemoryReplayAllocationUnsupported"); }
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
                    { return Unknown("MemoryReplayAllocationUnsupported"); }
                    var footprint = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(storage,
                        ResourceExpression.Literal(value.Elements.Length)), layout, cancellation);
                    if (footprint.Bounds is not { Lower.Constant: { } lower, Upper.Constant: { } upper })
                    { return Unknown(footprint.UnknownReason ?? "MemoryReplayAllocationUnsupported"); }
                    identities.Add(value);
                    actualValues[target] = value;
                    events.Add(new(ResourceMemoryEventKind.Allocate, identities.Count, Slot(target, slots), 0, lower, upper));
                    liveRoots.Add(target);
                    break;
                case IrAssignInstruction assigned:
                    if (value == null || value.Type != factory.GetVariableInfo(assigned.Target).Type)
                    { return Unknown("MemoryReplayValueUnsupported"); }
                    actualValues[assigned.Target] = value;
                    if (value.Kind is IrValueKind.Sequence or IrValueKind.Reference or IrValueKind.String or IrValueKind.Null)
                    {
                        var identity = value.Kind == IrValueKind.Null ? 0 : Find(value, identities);
                        if (value.Kind != IrValueKind.Null && identity == 0)
                        { return Unknown("MemoryReplayBorrowedReference"); }
                        events.Add(new(ResourceMemoryEventKind.BindRoot, 0, Slot(assigned.Target, slots), identity));
                        liveRoots.Add(assigned.Target);
                    }
                    break;
                case IrWriteInstruction { IsFieldStore: true, IsStore: true, Index: null, Length: null } stored:
                    if (stored.Target == null || stored.Field is not { } field || value == null ||
                        ActualReference(stored.Target, factory, actualValues, 0) is not { } receiverValue)
                    { return Unknown("MemoryReplayFieldStoreUnsupported"); }
                    var receiver = Find(receiverValue, identities);
                    var member = factory.GetMemberInfo(field);
                    if (!objects.Contains(receiver) || member.IsStatic || !member.ParameterTypes.IsEmpty || value.Type != member.ReturnType)
                    { return Unknown("MemoryReplayFieldStoreUnsupported"); }
                    if (value.Kind is IrValueKind.Reference or IrValueKind.Sequence or IrValueKind.String or IrValueKind.Null)
                    {
                        var child = value.Kind == IrValueKind.Null ? 0 : Find(value, identities);
                        if (value.Kind != IrValueKind.Null && child == 0)
                        { return Unknown("MemoryReplayBorrowedReference"); }
                        if (!fieldSlots.TryGetValue(field, out var fieldSlot))
                        { fieldSlot = fieldSlots.Count; fieldSlots.Add(field, fieldSlot); }
                        events.Add(new(ResourceMemoryEventKind.StoreReference, receiver, fieldSlot, child));
                    }
                    break;
                case IrReturnInstruction:
                    if (value != null && value.Kind is IrValueKind.Sequence or IrValueKind.Reference or IrValueKind.String)
                    {
                        output = Find(value, identities);
                        if (output == 0)
                        { return Unknown("MemoryReplayBorrowedReference"); }
                    }
                    return ResourceMemoryTrace.Evaluate(events.ToImmutable(), output, cancellation);
                case IrGotoInstruction or IrBranchInstruction or IrWriteInstruction { Region: IrWriteRegion.Local, IsStore: false }:
                    break;
                default:
                    return Unknown("MemoryReplayInstructionUnsupported");
            }
            foreach (var root in liveRoots.ToArray())
            {
                cancellation.ThrowIfCancellationRequested();
                if (!lastUse.TryGetValue(root, out var last) || last <= index)
                {
                    events.Add(new(ResourceMemoryEventKind.BindRoot, 0, slots[root], 0));
                    liveRoots.Remove(root);
                }
            }
            if (events.Count > ResourceMemoryTrace.MaximumEvents)
            { return Unknown("MemoryReplayBudgetExceeded"); }
        }
        return Unknown("MemoryReplayIncomplete");
    }

    private static IrValue? ActualReference(IrTerm term, IrFactory factory, Dictionary<IrVarId, IrValue> values, int depth)
    {
        term = ArgumentNullGuard.NotNull(term, nameof(term));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        values = ArgumentNullGuard.NotNull(values, nameof(values));
        if (depth > 32)
        { return null; }
        if (term is IrVariableTerm variable)
        { return values.TryGetValue(variable.Variable, out var value) ? value : null; }
        if (term is IrCastTerm cast && (cast.Type == factory.ObjectType || cast.Type == cast.Operand.Type))
        { return ActualReference(cast.Operand, factory, values, depth + 1); }
        return null;
    }

    private static bool ReadVariables(IrTerm term, Dictionary<IrVarId, int> lastUse, int index, int depth)
    {
        term = ArgumentNullGuard.NotNull(term, nameof(term));
        lastUse = ArgumentNullGuard.NotNull(lastUse, nameof(lastUse));
        if (depth > 32)
        { return false; }
        if (term is IrVariableTerm variable)
        { lastUse[variable.Variable] = index; return true; }
        if (term is IrCastTerm cast)
        { return ReadVariables(cast.Operand, lastUse, index, depth + 1); }
        return term is IrIntegerTerm or IrBooleanTerm or IrNullTerm;
    }

    private static int Find(IrValue value, List<IrValue> identities)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        identities = ArgumentNullGuard.NotNull(identities, nameof(identities));
        var identity = value.Kind switch { IrValueKind.Sequence => value.SequenceIdentity, IrValueKind.Reference => value.Reference, _ => null };
        if (identity == null)
        { return 0; }
        for (var index = 0; index < identities.Count; index++)
        {
            var candidate = identities[index];
            var candidateIdentity = candidate.Kind == IrValueKind.Sequence ? candidate.SequenceIdentity : candidate.Reference;
            if (ReferenceEquals(identity, candidateIdentity))
            { return index + 1; }
        }
        return 0;
    }

    private static int Slot(IrVarId variable, Dictionary<IrVarId, int> slots)
    {
        RepositoryParameterGuard.ThrowIfInvalid(variable.IsDefault, nameof(variable));
        slots = ArgumentNullGuard.NotNull(slots, nameof(slots));
        if (!slots.TryGetValue(variable, out var slot))
        { slot = slots.Count; slots.Add(variable, slot); }
        return slot;
    }

    private static ResourceMemoryCheck Unknown(string reason)
    {
        reason = ArgumentNullGuard.NotNull(reason, nameof(reason));
        return new(null, reason);
    }
}
