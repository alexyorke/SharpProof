namespace SharpProof.Ir;

// Extracts a finite owned trace from typed IR, never from producer-supplied sizes.
// Admitted paths have known extents and compiler-owned storage/field facts.
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
        var scalarFields = new Dictionary<(int Receiver, IrMemberId Field), IrTerm>();
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
                UpdateReferences(instruction, program.Factory, references, referenceFields, constants, scalarFields, ref nextReference);
                UpdateConstants(instruction, constants, program.Factory, references, referenceFields, scalarFields);
            }
            instructions.AddRange(block.Instructions);
            if (block.Instructions[block.Instructions.Length - 1] is IrReturnInstruction)
            { return Extract(program.Factory, instructions, layout, cancellation); }
            if (block.Instructions[block.Instructions.Length - 1] is IrGotoInstruction next)
            { current = next.Target; }
            else if (block.Instructions[block.Instructions.Length - 1] is IrBranchInstruction branch && TryConstantCondition(branch.Condition, constants, program.Factory, references, referenceFields, scalarFields, out var taken))
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
        var scalarFields = new Dictionary<(int Receiver, IrMemberId Field), IrTerm>();
        var trace = ImmutableArray.CreateBuilder<ResourceMemoryEvent>();
        trace.Add(new(ResourceMemoryEventKind.Allocate, EmptyStringIdentity, EmptyStringIdentity, 0));
        var nextObject = 0;
        var objects = new HashSet<int>();
        var fieldSlots = new Dictionary<IrMemberId, int>();
        var pendingCaptures = new Dictionary<int, IrMemberId>();
        var frames = new HashSet<int>();
        var pendingFrameCaptures = new HashSet<(int Receiver, IrMemberId Field)>();
        var updatableFrameCaptures = new HashSet<(int Receiver, IrMemberId Field)>();
        for (var index = 0; index < instructions.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var instruction = instructions[index];
            UpdateConstants(instruction, constants, factory, values, referenceFields, scalarFields);
            switch (instruction)
            {
                case IrAllocationInstruction textAllocation when textAllocation.AllocatedType == factory.StringType:
                    if (textAllocation.Target is not { } textTarget ||
                        ResolveValue(textAllocation.Length, constants, factory, values, referenceFields, scalarFields) is not IrIntegerTerm textLength ||
                        textLength.Integer.NumericValue < 0 || textLength.Integer.NumericValue > int.MaxValue || textAllocation.InitialValues.Length != 1 ||
                        textAllocation.InitialValues[0] is not IrIntegerTerm)
                    { return Unknown("MemoryStringAllocationUnsupported"); }
                    if (!slots.TryGetValue(textTarget, out var textSlot))
                    { textSlot = slots.Count; slots.Add(textTarget, textSlot); }
                    var textFootprint = ResourceRuntimeLayout.Bound(ResourceAllocationShape.String(ResourceExpression.Literal(textLength.Integer.NumericValue)), layout, cancellation);
                    if (textFootprint.Bounds is not { Lower.Constant: { } textLower, Upper.Constant: { } textUpper })
                    { return Unknown(textFootprint.UnknownReason ?? "MemoryStringAllocationUnsupported"); }
                    if (textLength.Integer.NumericValue == 0)
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
                    IrMemberId? capturedField = null;
                    var frameCaptures = ImmutableArray<IrMemberId>.Empty;
                    if (factory.TryGetClosureFrameType(objectAllocation.AllocatedType, out var frameStorage))
                    {
                        if (!IrClosureFrameSite.Matches(factory, objectAllocation))
                        { return Unknown("MemoryClosureCaptureIncomplete"); }
                        var captures = ImmutableArray.CreateBuilder<IrMemberId>();
                        for (var frameSlot = 0; frameSlot < frameStorage.Length; frameSlot++)
                        {
                            if (!IrClosureFrameSite.TryGetCapture(factory, objectAllocation,
                                index + frameSlot + 1 < instructions.Count ? instructions[index + frameSlot + 1] : null,
                                frameSlot, frameStorage[frameSlot], out var capture))
                            { return Unknown("MemoryClosureCaptureIncomplete"); }
                            captures.Add(capture);
                        }
                        frameCaptures = captures.ToImmutable();
                        objectShape = ResourceAllocationShape.Object(frameStorage);
                    }
                    else if (IrStaticDelegateAllocationSite.Matches(factory, objectAllocation))
                    { objectShape = ResourceAllocationShape.Delegate(); }
                    else if (IrDelegateCaptureSite.Matches(factory, objectAllocation))
                    {
                        if (!IrDelegateCaptureSite.TryGetCapture(factory, objectAllocation,
                            index + 1 < instructions.Count ? instructions[index + 1] : null, out var capture))
                        { return Unknown("MemoryDelegateCaptureIncomplete"); }
                        objectShape = ResourceAllocationShape.Delegate();
                        capturedField = capture;
                    }
                    else if (objectAllocation.AllocatedType == factory.ObjectType)
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
                    if (capturedField is { } requiredCapture)
                    { pendingCaptures.Add(nextObject, requiredCapture); }
                    if (!frameCaptures.IsEmpty)
                    {
                        frames.Add(nextObject);
                        foreach (var capture in frameCaptures)
                        {
                            pendingFrameCaptures.Add((nextObject, capture));
                            if (IrClosureFrameSite.AllowsCaptureUpdates(factory, objectAllocation))
                            { updatableFrameCaptures.Add((nextObject, capture)); }
                        }
                    }
                    trace.Add(new(ResourceMemoryEventKind.Allocate, nextObject, objectSlot, 0, objectLower, objectUpper));
                    break;
                case IrAllocationInstruction allocation:
                    if (allocation.Target is not { } target || ResolveValue(allocation.Length, constants, factory, values, referenceFields, scalarFields) is not IrIntegerTerm length || length.Integer.NumericValue < 0 ||
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
                    if (storage == ResourceStorageKind.Unspecified || length.Integer.NumericValue > int.MaxValue)
                    { return Unknown("MemoryAllocationUnsupported"); }
                    var footprint = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(storage, ResourceExpression.Literal(length.Integer.NumericValue)), layout, cancellation);
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
                        !(constants.TryGetValue(assignment.Target, out var knownCondition) && knownCondition is IrBooleanTerm or IrIntegerTerm))
                    { return Unknown("MemoryScalarEffectUnsupported"); }
                    break;
                case IrWriteInstruction { IsFieldStore: true, IsStore: true, Index: null, Length: null } stored:
                    if (stored.Target == null || stored.Value == null || stored.Field is not { } field ||
                        !TryReference(stored.Target, factory, values, out var receiver, fields: referenceFields) || receiver == 0 || !objects.Contains(receiver))
                    { return Unknown("MemoryFieldStoreUnsupported"); }
                    var member = factory.GetMemberInfo(field);
                    if (member.IsStatic || !member.ParameterTypes.IsEmpty)
                    { return Unknown("MemoryFieldStoreUnsupported"); }
                    var frameCapture = frames.Contains(receiver);
                    if ((frameCapture || IrClosureFrameSite.IsCaptureField(factory, field)) &&
                        !pendingFrameCaptures.Remove((receiver, field)) && !updatableFrameCaptures.Contains((receiver, field)))
                    { return Unknown("MemoryClosureCaptureUnsupported"); }
                    if (factory.GetTypeInfo(member.ReturnType).Kind is IrTypeKind.Reference or IrTypeKind.Sequence or IrTypeKind.String)
                    {
                        if (!TryReference(stored.Value, factory, values, out var child, fields: referenceFields))
                        { return Unknown("MemoryFieldStoreUnsupported"); }
                        if (frameCapture && child == receiver)
                        { return Unknown("MemoryClosureCaptureUnsupported"); }
                        if (IrDelegateCaptureSite.IsTargetField(factory, field))
                        {
                            if (child == 0 || child == receiver || !pendingCaptures.TryGetValue(receiver, out var required) || required != field)
                            { return Unknown("MemoryDelegateCaptureUnsupported"); }
                            pendingCaptures.Remove(receiver);
                        }
                        if (!fieldSlots.TryGetValue(field, out var fieldSlot))
                        { fieldSlot = fieldSlots.Count; fieldSlots.Add(field, fieldSlot); }
                        referenceFields[(receiver, field)] = child;
                        trace.Add(new(ResourceMemoryEventKind.StoreReference, receiver, fieldSlot, child));
                    }
                    else
                    {
                        var scalar = ResolveValue(stored.Value, constants, factory, values, referenceFields, scalarFields);
                        if (scalar is IrIntegerTerm or IrBooleanTerm)
                        { scalarFields[(receiver, field)] = scalar; }
                        else
                        {
                            scalarFields.Remove((receiver, field));
                            if (stored.Value is not IrVariableTerm)
                            { return Unknown("MemoryFieldStoreUnsupported"); }
                        }
                    }
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
                    else if (returned.Value is not (null or IrIntegerTerm or IrBooleanTerm or IrVariableTerm) &&
                        ResolveValue(returned.Value, constants, factory, values, referenceFields, scalarFields) is not (IrIntegerTerm or IrBooleanTerm))
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
        if (term is IrUnaryTerm unary)
        { return ReadVariables(unary.Operand, factory, lastUse, index, depth + 1); }
        if (term is IrBinaryTerm binary)
        { return ReadVariables(binary.Left, factory, lastUse, index, depth + 1) && ReadVariables(binary.Right, factory, lastUse, index, depth + 1); }
        if (term is IrOpaqueTerm { Receiver: { } owner, Arguments.IsEmpty: true } && IrFieldSites.IsFieldRead(factory, term))
        { return ReadVariables(owner, factory, lastUse, index, depth + 1); }
        if (term is IrStringTerm text && factory.GetString(text.Value).Length == 0)
        { return true; }
        return term is IrIntegerTerm or IrBooleanTerm or IrNullTerm;
    }

    private static void UpdateReferences(IrInstruction instruction, IrFactory factory, Dictionary<IrVarId, int> references,
        Dictionary<(int Receiver, IrMemberId Field), int> fields, Dictionary<IrVarId, IrTerm> constants,
        Dictionary<(int Receiver, IrMemberId Field), IrTerm> scalarFields, ref int next)
    {
        instruction = ArgumentNullGuard.NotNull(instruction, nameof(instruction));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        references = ArgumentNullGuard.NotNull(references, nameof(references));
        fields = ArgumentNullGuard.NotNull(fields, nameof(fields));
        constants = ArgumentNullGuard.NotNull(constants, nameof(constants));
        scalarFields = ArgumentNullGuard.NotNull(scalarFields, nameof(scalarFields));
        if (instruction is IrWriteInstruction { IsFieldStore: true, Field: { } field, Target: { } owner, Value: { } child } &&
            TryReference(owner, factory, references, out var receiver, fields: fields))
        {
            var scalar = ResolveValue(child, constants, factory, references, fields, scalarFields);
            if (scalar is IrIntegerTerm or IrBooleanTerm)
            { scalarFields[(receiver, field)] = scalar; }
            else
            { scalarFields.Remove((receiver, field)); }
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
            references[target] = allocation.AllocatedType == factory.StringType && ResolveValue(allocation.Length, constants, factory, references, fields, scalarFields) is IrIntegerTerm extent && extent.Integer.NumericValue == 0
                ? EmptyStringIdentity : ++next;
        }
    }

    private static IrTerm? ResolveConstant(IrTerm? term, Dictionary<IrVarId, IrTerm> constants)
    {
        constants = ArgumentNullGuard.NotNull(constants, nameof(constants));
        return term is IrVariableTerm variable && constants.TryGetValue(variable.Variable, out var value) ? value : term;
    }

    private static IrTerm? ResolveValue(IrTerm? term, Dictionary<IrVarId, IrTerm> constants, IrFactory factory,
        Dictionary<IrVarId, int> references, Dictionary<(int Receiver, IrMemberId Field), int> fields,
        Dictionary<(int Receiver, IrMemberId Field), IrTerm> scalarFields, int depth = 0)
    {
        constants = ArgumentNullGuard.NotNull(constants, nameof(constants));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        references = ArgumentNullGuard.NotNull(references, nameof(references));
        fields = ArgumentNullGuard.NotNull(fields, nameof(fields));
        scalarFields = ArgumentNullGuard.NotNull(scalarFields, nameof(scalarFields));
        term = ResolveConstant(term, constants);
        if (depth > 32)
        { return term; }
        if (term is IrCastTerm cast && factory.GetTypeInfo(cast.Type) is { Kind: IrTypeKind.Integer } target &&
            ResolveValue(cast.Operand, constants, factory, references, fields, scalarFields, depth + 1) is IrIntegerTerm integer)
        {
            var number = integer.Integer.NumericValue;
            var minimum = target.Signed ? -(System.Numerics.BigInteger.One << (target.Width - 1)) : System.Numerics.BigInteger.Zero;
            var maximum = (System.Numerics.BigInteger.One << (target.Width - (target.Signed ? 1 : 0))) - 1;
            if (number >= minimum && number <= maximum)
            { return target.Signed ? factory.Integer(cast.Type, (long)number) : factory.Integer(cast.Type, (ulong)number); }
        }
        if (term is IrOpaqueTerm { Receiver: { } owner, Arguments.IsEmpty: true } read && IrFieldSites.IsFieldRead(factory, read) &&
            TryReference(owner, factory, references, out var receiver, fields: fields) && receiver != 0 &&
            scalarFields.TryGetValue((receiver, read.Member), out var literal))
        { return literal; }
        return term;
    }

    private static void UpdateConstants(IrInstruction instruction, Dictionary<IrVarId, IrTerm> constants,
        IrFactory factory, Dictionary<IrVarId, int> references, Dictionary<(int Receiver, IrMemberId Field), int> fields,
        Dictionary<(int Receiver, IrMemberId Field), IrTerm> scalarFields)
    {
        instruction = ArgumentNullGuard.NotNull(instruction, nameof(instruction));
        constants = ArgumentNullGuard.NotNull(constants, nameof(constants));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        references = ArgumentNullGuard.NotNull(references, nameof(references));
        fields = ArgumentNullGuard.NotNull(fields, nameof(fields));
        scalarFields = ArgumentNullGuard.NotNull(scalarFields, nameof(scalarFields));
        var value = instruction is IrAssignInstruction assignment ? ResolveValue(assignment.Value, constants, factory, references, fields, scalarFields) : null;
        if (instruction is IrAssignInstruction { Value.Type: var type } condition && type == factory.BooleanType &&
            TryConstantCondition(condition.Value, constants, factory, references, fields, scalarFields, out var known))
        { value = factory.Boolean(known); }
        foreach (var variable in IrInstructionFacts.WrittenVariables(instruction))
        { constants.Remove(variable); }
        if (instruction is IrAssignInstruction assigned && value is IrIntegerTerm or IrBooleanTerm or IrNullTerm)
        { constants[assigned.Target] = value; }
    }

    private static bool TryConstantCondition(IrTerm condition, Dictionary<IrVarId, IrTerm> constants,
        IrFactory factory, Dictionary<IrVarId, int> references, Dictionary<(int Receiver, IrMemberId Field), int> fields,
        Dictionary<(int Receiver, IrMemberId Field), IrTerm> scalarFields, out bool value, int depth = 0)
    {
        condition = ArgumentNullGuard.NotNull(condition, nameof(condition));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        references = ArgumentNullGuard.NotNull(references, nameof(references));
        fields = ArgumentNullGuard.NotNull(fields, nameof(fields));
        constants = ArgumentNullGuard.NotNull(constants, nameof(constants));
        scalarFields = ArgumentNullGuard.NotNull(scalarFields, nameof(scalarFields));
        condition = ResolveValue(condition, constants, factory, references, fields, scalarFields)!;
        value = false;
        if (depth > 32)
        { return false; }
        if (condition is IrBooleanTerm literal)
        { value = literal.Value; return true; }
        if (condition is IrUnaryTerm { Operator: IrUnaryOperator.Not } negated &&
            TryConstantCondition(negated.Operand, constants, factory, references, fields, scalarFields, out var operand, depth + 1))
        { value = !operand; return true; }
        if (condition is IrBinaryTerm referenceComparison && referenceComparison.Operator is IrBinaryOperator.Equal or IrBinaryOperator.NotEqual &&
            (referenceComparison.Left.Type != factory.StringType && referenceComparison.Right.Type != factory.StringType ||
             ResolveConstant(referenceComparison.Left, constants) is IrNullTerm || ResolveConstant(referenceComparison.Right, constants) is IrNullTerm) &&
            TryReference(ResolveConstant(referenceComparison.Left, constants)!, factory, references, out var leftReference, fields: fields) &&
            TryReference(ResolveConstant(referenceComparison.Right, constants)!, factory, references, out var rightReference, fields: fields))
        {
            value = referenceComparison.Operator == IrBinaryOperator.Equal ? leftReference == rightReference : leftReference != rightReference;
            return true;
        }
        if (condition is IrBinaryTerm booleanComparison && booleanComparison.Operator is IrBinaryOperator.Equal or IrBinaryOperator.NotEqual &&
            ResolveValue(booleanComparison.Left, constants, factory, references, fields, scalarFields) is IrBooleanTerm leftBoolean &&
            ResolveValue(booleanComparison.Right, constants, factory, references, fields, scalarFields) is IrBooleanTerm rightBoolean)
        {
            value = booleanComparison.Operator == IrBinaryOperator.Equal ? leftBoolean.Value == rightBoolean.Value : leftBoolean.Value != rightBoolean.Value;
            return true;
        }
        if (condition is not IrBinaryTerm comparison ||
            ResolveValue(comparison.Left, constants, factory, references, fields, scalarFields) is not IrIntegerTerm left ||
            ResolveValue(comparison.Right, constants, factory, references, fields, scalarFields) is not IrIntegerTerm right)
        { return false; }
        switch (comparison.Operator)
        {
            case IrBinaryOperator.Equal:
                value = left.Integer.NumericValue == right.Integer.NumericValue;
                return true;
            case IrBinaryOperator.NotEqual:
                value = left.Integer.NumericValue != right.Integer.NumericValue;
                return true;
            case IrBinaryOperator.LessThan:
                value = left.Integer.NumericValue < right.Integer.NumericValue;
                return true;
            case IrBinaryOperator.LessThanOrEqual:
                value = left.Integer.NumericValue <= right.Integer.NumericValue;
                return true;
            case IrBinaryOperator.GreaterThan:
                value = left.Integer.NumericValue > right.Integer.NumericValue;
                return true;
            case IrBinaryOperator.GreaterThanOrEqual:
                value = left.Integer.NumericValue >= right.Integer.NumericValue;
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
