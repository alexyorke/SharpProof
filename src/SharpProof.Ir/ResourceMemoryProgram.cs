namespace SharpProof.Ir;

// Extracts a finite owned trace from typed IR, never from producer-supplied sizes.
// The first admitted family is a single normal-completion chain of primitive arrays.
internal static class ResourceMemoryProgram
{
    // An immortal zero-byte anchor models shared empty-string storage. It is
    // non-null and cannot collide with the bounded fresh identities/root slots.
    internal const int EmptyStringIdentity = int.MaxValue;
    internal static ResourceMemoryCheck Evaluate(IrProgram program, ResourceRuntimeLayoutContext layout,
        CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        if (program.Factory.Semantics != IrExecutionSemantics.Total)
        { return Unknown("MemoryProgramRequiresTotalSemantics"); }
        if (program.Blocks.Length > ResourceMemoryTrace.MaximumEvents / 4)
        { return Unknown("MemoryProgramBudgetExceeded"); }
        var blocks = program.Blocks.ToDictionary(static block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Id);
        var constants = new Dictionary<IrVarId, IrTerm>();
        var references = new Dictionary<IrVarId, int>();
        var referenceFields = new Dictionary<(int Receiver, IrMemberId Field), int>();
        var nextReference = 0;
        var visited = new HashSet<IrBlockId>();
        var instructions = new List<IrInstruction>();
        var current = program.Entry;
        while (visited.Add(current))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!blocks.TryGetValue(current, out var block))
            { return Unknown("MalformedMemoryControlFlow"); }
            if (block.Instructions.Length > ResourceMemoryTrace.MaximumEvents / 4 - instructions.Count)
            { return Unknown("MemoryProgramBudgetExceeded"); }
            foreach (var instruction in block.Instructions)
            {
                cancellation.ThrowIfCancellationRequested();
                if (instruction is not (IrAssignInstruction or IrAllocationInstruction or IrBranchInstruction or
                    IrGotoInstruction or IrReturnInstruction or IrWriteInstruction { Region: IrWriteRegion.Local, IsStore: false } or
                    IrWriteInstruction { IsFieldStore: true, IsStore: true, Index: null, Length: null }))
                { return Unknown("MemoryInstructionUnsupported"); }
                UpdateReferences(instruction, program.Factory, references, referenceFields, constants, ref nextReference);
                UpdateConstants(instruction, constants, program.Factory, references, referenceFields);
            }
            instructions.AddRange(block.Instructions);
            if (block.Instructions[block.Instructions.Length - 1] is IrReturnInstruction)
            { return Extract(program.Factory, instructions, layout, cancellation); }
            if (block.Instructions[block.Instructions.Length - 1] is IrGotoInstruction next)
            { current = next.Target; }
            else if (block.Instructions[block.Instructions.Length - 1] is IrBranchInstruction branch && TryConstantCondition(branch.Condition, constants, program.Factory, references, referenceFields, out var taken))
            { current = taken ? branch.WhenTrue : branch.WhenFalse; }
            else
            { return Unknown("MemoryControlFlowUnsupported"); }
        }
        return Unknown("MemoryControlFlowUnsupported");
    }

    private static ResourceMemoryCheck Extract(IrFactory factory, List<IrInstruction> instructions,
        ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        instructions = ArgumentNullGuard.NotNull(instructions, nameof(instructions));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        var lastUse = new Dictionary<IrVarId, int>();
        for (var index = 0; index < instructions.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var readCount = 0;
            foreach (var term in IrInstructionFacts.ReadTerms(instructions[index]))
            {
                cancellation.ThrowIfCancellationRequested();
                if (++readCount > ResourceMemoryTrace.MaximumEvents)
                { return Unknown("MemoryProgramBudgetExceeded"); }
                if (!ReadVariables(term, factory, lastUse, index, 0))
                { return Unknown("MemoryScalarEffectUnsupported"); }
            }
        }
        var constants = new Dictionary<IrVarId, IrTerm>();
        var slots = new Dictionary<IrVarId, int>();
        var values = new Dictionary<IrVarId, int>();
        var referenceFields = new Dictionary<(int Receiver, IrMemberId Field), int>();
        var trace = ImmutableArray.CreateBuilder<ResourceMemoryEvent>();
        trace.Add(new(ResourceMemoryEventKind.Allocate, EmptyStringIdentity, EmptyStringIdentity, 0));
        var nextObject = 0;
        var objects = new HashSet<int>();
        var fieldSlots = new Dictionary<IrMemberId, int>();
        for (var index = 0; index < instructions.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var instruction = instructions[index];
            UpdateConstants(instruction, constants, factory, values, referenceFields);
            switch (instruction)
            {
                case IrAllocationInstruction textAllocation when textAllocation.AllocatedType == factory.StringType:
                    if (textAllocation.Target is not { } textTarget ||
                        ResolveConstant(textAllocation.Length, constants) is not IrIntegerTerm textLength ||
                        textLength.Value < 0 || textLength.Value > int.MaxValue || textAllocation.InitialValues.Length != 1 ||
                        textAllocation.InitialValues[0] is not IrIntegerTerm)
                    { return Unknown("MemoryStringAllocationUnsupported"); }
                    if (!slots.TryGetValue(textTarget, out var textSlot))
                    { textSlot = slots.Count; slots.Add(textTarget, textSlot); }
                    var textFootprint = ResourceRuntimeLayout.Bound(ResourceAllocationShape.String(ResourceExpression.Literal(textLength.Value)), layout, cancellation);
                    if (textFootprint.Bounds is not { Lower.Constant: { } textLower, Upper.Constant: { } textUpper })
                    { return Unknown(textFootprint.UnknownReason ?? "MemoryStringAllocationUnsupported"); }
                    if (textLength.Value == 0)
                    {
                        values[textTarget] = EmptyStringIdentity;
                        trace.Add(new(ResourceMemoryEventKind.BindRoot, 0, textSlot, EmptyStringIdentity));
                        break;
                    }
                    values[textTarget] = ++nextObject;
                    trace.Add(new(ResourceMemoryEventKind.Allocate, nextObject, textSlot, 0, textLower, textUpper));
                    break;
                case IrAllocationInstruction objectAllocation when objectAllocation.Length == null:
                    if (objectAllocation.Target is not { } objectTarget || !objectAllocation.InitialValues.IsEmpty)
                    { return Unknown("MemoryAllocationUnsupported"); }
                    ResourceAllocationShape objectShape;
                    if (objectAllocation.AllocatedType == factory.ObjectType)
                    { objectShape = ResourceAllocationShape.Object([]); }
                    else if (factory.TryGetObjectStorageType(objectAllocation.AllocatedType, out var fields))
                    { objectShape = ResourceAllocationShape.Object(fields); }
                    else
                    { return Unknown("MemoryObjectStorageUnavailable"); }
                    var objectFootprint = ResourceRuntimeLayout.Bound(objectShape, layout, cancellation);
                    if (objectFootprint.Bounds is not { Lower.Constant: { } objectLower, Upper.Constant: { } objectUpper })
                    { return Unknown(objectFootprint.UnknownReason ?? "MemoryAllocationUnsupported"); }
                    if (!slots.TryGetValue(objectTarget, out var objectSlot))
                    { objectSlot = slots.Count; slots.Add(objectTarget, objectSlot); }
                    values[objectTarget] = ++nextObject;
                    objects.Add(nextObject);
                    trace.Add(new(ResourceMemoryEventKind.Allocate, nextObject, objectSlot, 0, objectLower, objectUpper));
                    break;
                case IrAllocationInstruction allocation:
                    if (allocation.Target is not { } target || ResolveConstant(allocation.Length, constants) is not IrIntegerTerm length || length.Value < 0 ||
                        !allocation.InitialValues.IsEmpty || factory.GetTypeInfo(allocation.AllocatedType).ElementType is not { } element)
                    { return Unknown("MemoryAllocationUnsupported"); }
                    var type = factory.GetTypeInfo(element);
                    var storage = type.Kind == IrTypeKind.Boolean ? ResourceStorageKind.Byte : type.Kind == IrTypeKind.Integer ? type.Width switch
                    {
                        8 => ResourceStorageKind.Byte,
                        16 => ResourceStorageKind.Word16,
                        32 => ResourceStorageKind.Word32,
                        64 => ResourceStorageKind.Word64,
                        _ => ResourceStorageKind.Unspecified
                    } : ResourceStorageKind.Unspecified;
                    if (storage == ResourceStorageKind.Unspecified || length.Value > int.MaxValue)
                    { return Unknown("MemoryAllocationUnsupported"); }
                    var footprint = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(storage, ResourceExpression.Literal(length.Value)), layout, cancellation);
                    if (footprint.Bounds is not { Lower.Constant: { } lower, Upper.Constant: { } upper })
                    { return Unknown(footprint.UnknownReason ?? "MemoryAllocationUnsupported"); }
                    if (!slots.TryGetValue(target, out var slot))
                    { slot = slots.Count; slots.Add(target, slot); }
                    values[target] = ++nextObject;
                    trace.Add(new(ResourceMemoryEventKind.Allocate, nextObject, slot, 0, lower, upper));
                    break;
                case IrAssignInstruction assignment:
                    if (factory.GetTypeInfo(assignment.Value.Type).Kind is IrTypeKind.Reference or IrTypeKind.Sequence or IrTypeKind.String)
                    {
                        if (!TryReference(assignment.Value, factory, values, out var identity, fields: referenceFields))
                        { return Unknown("UnownedMemoryReference"); }
                        if (!slots.TryGetValue(assignment.Target, out slot))
                        { slot = slots.Count; slots.Add(assignment.Target, slot); }
                        values[assignment.Target] = identity;
                        trace.Add(new(ResourceMemoryEventKind.BindRoot, 0, slot, identity));
                    }
                    else if (assignment.Value is not (IrIntegerTerm or IrBooleanTerm or IrVariableTerm) &&
                        !(assignment.Value.Type == factory.BooleanType && constants.TryGetValue(assignment.Target, out var knownCondition) && knownCondition is IrBooleanTerm))
                    { return Unknown("MemoryScalarEffectUnsupported"); }
                    break;
                case IrWriteInstruction { IsFieldStore: true, IsStore: true, Index: null, Length: null } stored:
                    if (stored.Target == null || stored.Value == null || stored.Field is not { } field ||
                        !TryReference(stored.Target, factory, values, out var receiver, fields: referenceFields) || receiver == 0 || !objects.Contains(receiver))
                    { return Unknown("MemoryFieldStoreUnsupported"); }
                    var member = factory.GetMemberInfo(field);
                    if (member.IsStatic || !member.ParameterTypes.IsEmpty)
                    { return Unknown("MemoryFieldStoreUnsupported"); }
                    if (factory.GetTypeInfo(member.ReturnType).Kind is IrTypeKind.Reference or IrTypeKind.Sequence or IrTypeKind.String)
                    {
                        if (!TryReference(stored.Value, factory, values, out var child, fields: referenceFields))
                        { return Unknown("MemoryFieldStoreUnsupported"); }
                        if (!fieldSlots.TryGetValue(field, out var fieldSlot))
                        { fieldSlot = fieldSlots.Count; fieldSlots.Add(field, fieldSlot); }
                        referenceFields[(receiver, field)] = child;
                        trace.Add(new(ResourceMemoryEventKind.StoreReference, receiver, fieldSlot, child));
                    }
                    else if (stored.Value is not (IrIntegerTerm or IrBooleanTerm or IrVariableTerm))
                    { return Unknown("MemoryFieldStoreUnsupported"); }
                    break;
                case IrWriteInstruction { Region: IrWriteRegion.Local, IsStore: false }:
                case IrBranchInstruction:
                case IrGotoInstruction:
                    break;
                case IrReturnInstruction returned:
                    var output = 0;
                    if (returned.Value != null && factory.GetTypeInfo(returned.Value.Type).Kind is IrTypeKind.Reference or IrTypeKind.Sequence or IrTypeKind.String)
                    {
                        if (!TryReference(returned.Value, factory, values, out output, fields: referenceFields))
                        { return Unknown("UnownedMemoryOutput"); }
                    }
                    else if (returned.Value is not (null or IrIntegerTerm or IrBooleanTerm or IrVariableTerm))
                    { return Unknown("MemoryScalarEffectUnsupported"); }
                    return ResourceMemoryTrace.Evaluate(trace.ToImmutable(), output, cancellation);
                default:
                    return Unknown("MemoryInstructionUnsupported");
            }
            foreach (var variable in values.Keys.ToArray())
            {
                cancellation.ThrowIfCancellationRequested();
                if (!lastUse.TryGetValue(variable, out var last) || last <= index)
                {
                    trace.Add(new(ResourceMemoryEventKind.BindRoot, 0, slots[variable], 0));
                    values.Remove(variable);
                }
            }
            if (trace.Count > ResourceMemoryTrace.MaximumEvents)
            { return Unknown("MemoryProgramBudgetExceeded"); }
        }
        return Unknown("MemoryControlFlowUnsupported");
    }

    private static bool ReadVariables(IrTerm term, IrFactory factory, Dictionary<IrVarId, int> lastUse, int index, int depth)
    {
        term = ArgumentNullGuard.NotNull(term, nameof(term));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        lastUse = ArgumentNullGuard.NotNull(lastUse, nameof(lastUse));
        if (depth > 32)
        { return false; }
        if (term is IrVariableTerm variable)
        { lastUse[variable.Variable] = index; return true; }
        if (term is IrCastTerm cast)
        { return ReadVariables(cast.Operand, factory, lastUse, index, depth + 1); }
        if (term is IrBinaryTerm binary)
        { return ReadVariables(binary.Left, factory, lastUse, index, depth + 1) && ReadVariables(binary.Right, factory, lastUse, index, depth + 1); }
        if (term is IrOpaqueTerm { Receiver: { } owner, Arguments.IsEmpty: true } && IrFieldSites.IsFieldRead(factory, term))
        { return ReadVariables(owner, factory, lastUse, index, depth + 1); }
        if (term is IrStringTerm text && factory.GetString(text.Value).Length == 0)
        { return true; }
        return term is IrIntegerTerm or IrBooleanTerm or IrNullTerm;
    }

    private static void UpdateReferences(IrInstruction instruction, IrFactory factory, Dictionary<IrVarId, int> references,
        Dictionary<(int Receiver, IrMemberId Field), int> fields, Dictionary<IrVarId, IrTerm> constants, ref int next)
    {
        instruction = ArgumentNullGuard.NotNull(instruction, nameof(instruction));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        references = ArgumentNullGuard.NotNull(references, nameof(references));
        fields = ArgumentNullGuard.NotNull(fields, nameof(fields));
        constants = ArgumentNullGuard.NotNull(constants, nameof(constants));
        if (instruction is IrWriteInstruction { IsFieldStore: true, Field: { } field, Target: { } owner, Value: { } child } &&
            TryReference(owner, factory, references, out var receiver, fields: fields))
        {
            if (TryReference(child, factory, references, out var stored, fields: fields))
            { fields[(receiver, field)] = stored; }
            else
            { fields.Remove((receiver, field)); }
        }
        var identity = 0;
        var known = instruction is IrAssignInstruction assignment && TryReference(assignment.Value, factory, references, out identity, fields: fields);
        foreach (var variable in IrInstructionFacts.WrittenVariables(instruction))
        { references.Remove(variable); }
        if (known && instruction is IrAssignInstruction assigned)
        { references[assigned.Target] = identity; }
        if (instruction is IrAllocationInstruction { Target: { } target } allocation)
        {
            references[target] = allocation.AllocatedType == factory.StringType && ResolveConstant(allocation.Length, constants) is IrIntegerTerm { Value: 0 }
                ? EmptyStringIdentity : ++next;
        }
    }

    private static IrTerm? ResolveConstant(IrTerm? term, Dictionary<IrVarId, IrTerm> constants)
    {
        constants = ArgumentNullGuard.NotNull(constants, nameof(constants));
        return term is IrVariableTerm variable && constants.TryGetValue(variable.Variable, out var value) ? value : term;
    }

    private static void UpdateConstants(IrInstruction instruction, Dictionary<IrVarId, IrTerm> constants,
        IrFactory factory, Dictionary<IrVarId, int> references, Dictionary<(int Receiver, IrMemberId Field), int> fields)
    {
        instruction = ArgumentNullGuard.NotNull(instruction, nameof(instruction));
        constants = ArgumentNullGuard.NotNull(constants, nameof(constants));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        references = ArgumentNullGuard.NotNull(references, nameof(references));
        fields = ArgumentNullGuard.NotNull(fields, nameof(fields));
        var value = instruction is IrAssignInstruction assignment ? ResolveConstant(assignment.Value, constants) : null;
        if (instruction is IrAssignInstruction { Value.Type: var type } condition && type == factory.BooleanType &&
            TryConstantCondition(condition.Value, constants, factory, references, fields, out var known))
        { value = factory.Boolean(known); }
        foreach (var variable in IrInstructionFacts.WrittenVariables(instruction))
        { constants.Remove(variable); }
        if (instruction is IrAssignInstruction assigned && value is IrIntegerTerm or IrBooleanTerm or IrNullTerm)
        { constants[assigned.Target] = value; }
    }

    private static bool TryConstantCondition(IrTerm condition, Dictionary<IrVarId, IrTerm> constants,
        IrFactory factory, Dictionary<IrVarId, int> references, Dictionary<(int Receiver, IrMemberId Field), int> fields, out bool value)
    {
        condition = ArgumentNullGuard.NotNull(condition, nameof(condition));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        references = ArgumentNullGuard.NotNull(references, nameof(references));
        fields = ArgumentNullGuard.NotNull(fields, nameof(fields));
        constants = ArgumentNullGuard.NotNull(constants, nameof(constants));
        condition = ResolveConstant(condition, constants)!;
        value = false;
        if (condition is IrBooleanTerm literal)
        { value = literal.Value; return true; }
        if (condition is IrBinaryTerm referenceComparison && referenceComparison.Operator is IrBinaryOperator.Equal or IrBinaryOperator.NotEqual &&
            (referenceComparison.Left.Type != factory.StringType && referenceComparison.Right.Type != factory.StringType ||
             ResolveConstant(referenceComparison.Left, constants) is IrNullTerm || ResolveConstant(referenceComparison.Right, constants) is IrNullTerm) &&
            TryReference(ResolveConstant(referenceComparison.Left, constants)!, factory, references, out var leftReference, fields: fields) &&
            TryReference(ResolveConstant(referenceComparison.Right, constants)!, factory, references, out var rightReference, fields: fields))
        {
            value = referenceComparison.Operator == IrBinaryOperator.Equal ? leftReference == rightReference : leftReference != rightReference;
            return true;
        }
        if (condition is not IrBinaryTerm comparison ||
            ResolveConstant(comparison.Left, constants) is not IrIntegerTerm left ||
            ResolveConstant(comparison.Right, constants) is not IrIntegerTerm right)
        { return false; }
        switch (comparison.Operator)
        {
            case IrBinaryOperator.Equal:
                value = left.Value == right.Value;
                return true;
            case IrBinaryOperator.NotEqual:
                value = left.Value != right.Value;
                return true;
            case IrBinaryOperator.LessThan:
                value = left.Value < right.Value;
                return true;
            case IrBinaryOperator.LessThanOrEqual:
                value = left.Value <= right.Value;
                return true;
            case IrBinaryOperator.GreaterThan:
                value = left.Value > right.Value;
                return true;
            case IrBinaryOperator.GreaterThanOrEqual:
                value = left.Value >= right.Value;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReference(IrTerm term, IrFactory factory, Dictionary<IrVarId, int> values, out int identity, int depth = 0,
        Dictionary<(int Receiver, IrMemberId Field), int>? fields = null)
    {
        term = ArgumentNullGuard.NotNull(term, nameof(term));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        values = ArgumentNullGuard.NotNull(values, nameof(values));
        identity = 0;
        if (depth > 32)
        { return false; }
        if (term is IrCastTerm cast && (cast.Type == factory.ObjectType || cast.Type == cast.Operand.Type))
        { return TryReference(cast.Operand, factory, values, out identity, depth + 1, fields); }
        if (term is IrStringTerm text && factory.GetString(text.Value).Length == 0)
        { identity = EmptyStringIdentity; return true; }
        if (fields != null && term is IrOpaqueTerm { Receiver: { } owner, Arguments.IsEmpty: true } read &&
            IrFieldSites.IsFieldRead(factory, term) &&
            TryReference(owner, factory, values, out var receiver, depth + 1, fields) && receiver != 0)
        { return fields.TryGetValue((receiver, read.Member), out identity); }
        return term is IrNullTerm || term is IrVariableTerm variable && values.TryGetValue(variable.Variable, out identity);
    }

    private static ResourceMemoryCheck Unknown(string reason)
    {
        reason = ArgumentNullGuard.NotNull(reason, nameof(reason));
        return new(null, reason);
    }
}
