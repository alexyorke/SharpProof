namespace SharpProof.Worker;

// The cumulative allocation kernel supplies the byte bound separately. This
// admission establishes closed primitive-array ownership for its transfer to
// auxiliary/retained upper bounds; each reference use has a local or dominating owned origin.
internal static class NativeResourceMemoryEnvelope
{
    internal const string ModelId = "primitive-array-cumulative-envelope-v2";

    internal static bool Admits(CompilerTotalCallablePreparation owner, CancellationToken cancellation)
    {
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        cancellation.ThrowIfCancellationRequested();
        var program = owner.Program;
        var factory = program.Factory;
        if (owner.IsBodyAbstraction || !owner.EffectsCompleteAtEntry || owner.HasReceiver ||
            factory.Semantics != IrExecutionSemantics.Total || program.Blocks.Length > ResourceMemoryTrace.MaximumEvents / 4)
        { return false; }
        foreach (var parameter in owner.Parameters)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Scalar(factory, factory.GetVariableInfo(parameter.Entry).Type))
            { return false; }
        }
        var remaining = ResourceExpression.MaximumNodes;
        // Dominance proves initialization, not the value's provenance in isolation.
        // Every defining instruction and every reference operand is checked below.
        // With no borrowed reference inputs or external stores, induction over
        // execution therefore keeps every admitted reference null or locally owned.
        var origins = ReferenceDefinitions(program, ref remaining, cancellation);
        foreach (var block in program.Blocks)
        {
            cancellation.ThrowIfCancellationRequested();
            var references = new HashSet<IrVarId>();
            foreach (var origin in origins)
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (var definition in origin.Value)
                {
                    if (definition != block.Id && Dominates(program, definition, block.Id, ref remaining, cancellation))
                    { references.Add(origin.Key); break; }
                }
            }
            foreach (var instruction in block.Instructions)
            {
                cancellation.ThrowIfCancellationRequested();
                if (--remaining < 0)
                { return false; }
                switch (instruction)
                {
                    case IrAllocationInstruction allocation:
                        if (allocation.Length == null || !allocation.InitialValues.IsEmpty ||
                            !PrimitiveArray(factory, allocation.AllocatedType))
                        { return false; }
                        break;
                    case IrAssignInstruction assignment:
                        var targetType = factory.GetVariableInfo(assignment.Target).Type;
                        if (!(Scalar(factory, targetType) || Reference(factory, targetType)) ||
                            Reference(factory, targetType) != Reference(factory, assignment.Value.Type))
                        { return false; }
                        break;
                    case IrWriteInstruction { Region: IrWriteRegion.Local, IsStore: false }:
                    case IrBranchInstruction:
                    case IrGotoInstruction:
                    case IrReturnInstruction:
                    case IrThrowInstruction:
                    case IrExceptionalExitInstruction:
                    case IrAssumeInstruction:
                    case IrAssertInstruction:
                        break;
                    case IrHavocInstruction { HavocKind: IrHavocKind.Variables } havoc:
                        foreach (var variable in havoc.Variables)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (!Scalar(factory, factory.GetVariableInfo(variable).Type))
                            { return false; }
                        }
                        break;
                    default:
                        return false;
                }
                foreach (var term in IrInstructionFacts.ReadTerms(instruction))
                {
                    if (!Closed(term, factory, references, ref remaining, cancellation))
                    { return false; }
                }
                if (instruction is IrAllocationInstruction { Target: { } allocated })
                { references.Add(allocated); }
                else if (instruction is IrAssignInstruction assigned && Reference(factory, assigned.Value.Type))
                { references.Add(assigned.Target); }
            }
        }
        return true;
    }

    private static Dictionary<IrVarId, HashSet<IrBlockId>> ReferenceDefinitions(IrProgram program, ref int remaining, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        var definitions = new Dictionary<IrVarId, HashSet<IrBlockId>>();
        foreach (var block in program.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                cancellation.ThrowIfCancellationRequested();
                if (--remaining < 0)
                { return definitions; }
                IrVarId? target = instruction switch
                {
                    IrAllocationInstruction allocation => allocation.Target,
                    IrAssignInstruction assignment when Reference(program.Factory, program.Factory.GetVariableInfo(assignment.Target).Type) => assignment.Target,
                    _ => null
                };
                if (target is not { } variable)
                { continue; }
                if (!definitions.TryGetValue(variable, out var blocks))
                { definitions.Add(variable, blocks = []); }
                blocks.Add(block.Id);
            }
        }
        return definitions;
    }

    private static bool Dominates(IrProgram program, IrBlockId origin, IrBlockId use, ref int remaining, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        RepositoryParameterGuard.ThrowIfInvalid(origin.IsDefault, nameof(origin));
        RepositoryParameterGuard.ThrowIfInvalid(use.IsDefault, nameof(use));
        // A path from entry to the use that avoids the origin disproves dominance.
        var pending = new Stack<IrBlockId>();
        var visited = new HashSet<IrBlockId>();
        pending.Push(program.Entry);
        while (pending.Count != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (--remaining < 0)
            { return false; }
            var block = pending.Pop();
            if (block == origin || !visited.Add(block))
            { continue; }
            if (block == use)
            { return false; }
            if (IrInstructionFacts.TryGetSuccessors(program.GetBlock(block).Terminator) is not { } successors)
            { return false; }
            if (successors.First is { } first)
            { pending.Push(first); }
            if (successors.Second is { } second)
            { pending.Push(second); }
        }
        return true;
    }

    private static bool Closed(IrTerm root, IrFactory factory, HashSet<IrVarId> references,
        ref int remaining, CancellationToken cancellation)
    {
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        references = ArgumentNullGuard.NotNull(references, nameof(references));
        var pending = new Stack<IrTerm>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (--remaining < 0)
            { return false; }
            var term = pending.Pop();
            switch (term)
            {
                case IrVariableTerm variable when Scalar(factory, variable.Type) || Reference(factory, variable.Type) && references.Contains(variable.Variable):
                case IrIntegerTerm:
                case IrBooleanTerm:
                case IrNullTerm:
                    break;
                case IrUnaryTerm when Scalar(factory, term.Type):
                case IrBinaryTerm when Scalar(factory, term.Type):
                case IrConditionalTerm:
                case IrLengthTerm:
                    break;
                case IrSequenceAccessTerm access when Scalar(factory, access.Type) && PrimitiveArray(factory, access.Sequence.Type):
                    break;
                case IrCastTerm cast when !Reference(factory, cast.Type) || Reference(factory, cast.Operand.Type):
                    break;
                default:
                    return false;
            }
            IrTraversal.PushChildren(term, pending);
        }
        return true;
    }

    private static bool PrimitiveArray(IrFactory factory, IrTypeId type)
    {
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        return factory.GetTypeInfo(type) is { Kind: IrTypeKind.Sequence, ElementType: { } element } &&
            factory.GetTypeInfo(element) is { Kind: IrTypeKind.Boolean } or
            { Kind: IrTypeKind.Integer, Width: 8 or 16 or 32 or 64 };
    }

    private static bool Scalar(IrFactory factory, IrTypeId type)
    { factory = ArgumentNullGuard.NotNull(factory, nameof(factory)); return factory.GetTypeInfo(type).Kind is IrTypeKind.Integer or IrTypeKind.Boolean; }

    private static bool Reference(IrFactory factory, IrTypeId type)
    { factory = ArgumentNullGuard.NotNull(factory, nameof(factory)); return factory.GetTypeInfo(type).Kind is IrTypeKind.Reference or IrTypeKind.Sequence or IrTypeKind.String; }
}
