namespace SharpProof.Ir;

// Extracts a finite owned trace from typed IR, never from producer-supplied sizes.
// The first admitted family is a single normal-completion chain of primitive arrays.
internal static class ResourceMemoryProgram
{
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
                    IrGotoInstruction or IrReturnInstruction or IrWriteInstruction { Region: IrWriteRegion.Local, IsStore: false }))
                { return Unknown("MemoryInstructionUnsupported"); }
                UpdateConstants(instruction, constants);
            }
            instructions.AddRange(block.Instructions);
            if (block.Instructions[block.Instructions.Length - 1] is IrReturnInstruction)
            { return Extract(program.Factory, instructions, layout, cancellation); }
            if (block.Instructions[block.Instructions.Length - 1] is IrGotoInstruction next)
            { current = next.Target; }
            else if (block.Instructions[block.Instructions.Length - 1] is IrBranchInstruction branch && TryConstantCondition(branch.Condition, constants, out var taken))
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
            if (instructions[index] is IrBranchInstruction)
            { continue; }
            var readCount = 0;
            foreach (var term in IrInstructionFacts.ReadTerms(instructions[index]))
            {
                cancellation.ThrowIfCancellationRequested();
                if (++readCount > ResourceMemoryTrace.MaximumEvents)
                { return Unknown("MemoryProgramBudgetExceeded"); }
                if (term is IrVariableTerm variable)
                { lastUse[variable.Variable] = index; }
                else if (term is not (IrIntegerTerm or IrBooleanTerm or IrNullTerm))
                { return Unknown("MemoryScalarEffectUnsupported"); }
            }
        }
        var constants = new Dictionary<IrVarId, IrTerm>();
        var slots = new Dictionary<IrVarId, int>();
        var values = new Dictionary<IrVarId, int>();
        var trace = ImmutableArray.CreateBuilder<ResourceMemoryEvent>();
        var nextObject = 0;
        for (var index = 0; index < instructions.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var instruction = instructions[index];
            UpdateConstants(instruction, constants);
            switch (instruction)
            {
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
                        if (!TryReference(assignment.Value, values, out var identity))
                        { return Unknown("UnownedMemoryReference"); }
                        if (!slots.TryGetValue(assignment.Target, out slot))
                        { slot = slots.Count; slots.Add(assignment.Target, slot); }
                        values[assignment.Target] = identity;
                        trace.Add(new(ResourceMemoryEventKind.BindRoot, 0, slot, identity));
                    }
                    else if (assignment.Value is not (IrIntegerTerm or IrBooleanTerm or IrVariableTerm))
                    { return Unknown("MemoryScalarEffectUnsupported"); }
                    break;
                case IrWriteInstruction { Region: IrWriteRegion.Local, IsStore: false }:
                case IrBranchInstruction:
                case IrGotoInstruction:
                    break;
                case IrReturnInstruction returned:
                    var output = 0;
                    if (returned.Value != null && factory.GetTypeInfo(returned.Value.Type).Kind is IrTypeKind.Reference or IrTypeKind.Sequence or IrTypeKind.String)
                    {
                        if (!TryReference(returned.Value, values, out output))
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

    private static IrTerm? ResolveConstant(IrTerm? term, Dictionary<IrVarId, IrTerm> constants)
    {
        constants = ArgumentNullGuard.NotNull(constants, nameof(constants));
        return term is IrVariableTerm variable && constants.TryGetValue(variable.Variable, out var value) ? value : term;
    }

    private static void UpdateConstants(IrInstruction instruction, Dictionary<IrVarId, IrTerm> constants)
    {
        instruction = ArgumentNullGuard.NotNull(instruction, nameof(instruction));
        constants = ArgumentNullGuard.NotNull(constants, nameof(constants));
        var value = instruction is IrAssignInstruction assignment ? ResolveConstant(assignment.Value, constants) : null;
        foreach (var variable in IrInstructionFacts.WrittenVariables(instruction))
        { constants.Remove(variable); }
        if (instruction is IrAssignInstruction assigned && value is IrIntegerTerm or IrBooleanTerm or IrNullTerm)
        { constants[assigned.Target] = value; }
    }

    private static bool TryConstantCondition(IrTerm condition, Dictionary<IrVarId, IrTerm> constants, out bool value)
    {
        condition = ArgumentNullGuard.NotNull(condition, nameof(condition));
        constants = ArgumentNullGuard.NotNull(constants, nameof(constants));
        condition = ResolveConstant(condition, constants)!;
        value = false;
        if (condition is IrBooleanTerm literal)
        { value = literal.Value; return true; }
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

    private static bool TryReference(IrTerm term, Dictionary<IrVarId, int> values, out int identity)
    {
        term = ArgumentNullGuard.NotNull(term, nameof(term));
        values = ArgumentNullGuard.NotNull(values, nameof(values));
        identity = 0;
        return term is IrNullTerm || term is IrVariableTerm variable && values.TryGetValue(variable.Variable, out identity);
    }

    private static ResourceMemoryCheck Unknown(string reason)
    {
        reason = ArgumentNullGuard.NotNull(reason, nameof(reason));
        return new(null, reason);
    }
}
