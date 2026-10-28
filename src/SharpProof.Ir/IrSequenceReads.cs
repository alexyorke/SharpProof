namespace SharpProof.Ir;

// Reading an incoming object is observable until every path has replaced
// the particular value with a local allocation or literal.
internal static class IrSequenceReads
{
    internal static ImmutableHashSet<IrInstructionId> Readers(IrProgram program)
    {
        ArgumentNullGuard.NotNull(program, nameof(program));
        var variables = program.Blocks.SelectMany(block => block is null
            ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
            .SelectMany(IrInstructionFacts.WrittenVariables).ToImmutableHashSet();
        var predecessors = ImmutableDictionary.CreateBuilder<IrBlockId, ImmutableArray<IrBlockId>.Builder>();
        var outgoing = ImmutableDictionary.CreateBuilder<IrBlockId, ImmutableHashSet<IrVarId>>();
        foreach (var block in program.Blocks)
        {
            predecessors.Add(block.Id, ImmutableArray.CreateBuilder<IrBlockId>());
            outgoing.Add(block.Id, variables);
        }
        foreach (var block in program.Blocks)
        {
            if (IrInstructionFacts.TryGetSuccessors(block.Terminator) is not { } successors)
            { continue; }
            if (successors.First is { } first)
            { predecessors[first].Add(block.Id); }
            if (successors.Second is { } second)
            { predecessors[second].Add(block.Id); }
        }
        // Start at the top of the must-analysis lattice and only remove
        // facts. The entry's incoming values are never assumed fresh.
        bool changed;
        do
        {
            changed = false;
            foreach (var block in program.Blocks)
            {
                var fresh = Incoming(block.Id);
                foreach (var instruction in block.Instructions)
                { Transfer(instruction, fresh); }
                if (!outgoing[block.Id].SetEquals(fresh))
                {
                    outgoing[block.Id] = fresh.ToImmutable();
                    changed = true;
                }
            }
        } while (changed);

        var readers = ImmutableHashSet.CreateBuilder<IrInstructionId>();
        foreach (var block in program.Blocks)
        {
            var fresh = Incoming(block.Id);
            foreach (var instruction in block.Instructions)
            {
                var pending = ImmutableStack<IrTerm>.Empty;
                var visited = ImmutableHashSet.CreateBuilder<IrId>();
                foreach (var root in IrInstructionFacts.ReadTerms(instruction))
                { pending = pending.Push(root); }
                while (!pending.IsEmpty)
                {
                    var term = pending.Peek();
                    pending = pending.Pop();
                    if (!visited.Add(term.Id))
                    { continue; }
                    if (term is IrSequenceAccessTerm { Sequence: var accessed } && !IsLocal(accessed, fresh) ||
                        term is IrLengthTerm { Value: var measured } && !IsLocal(measured, fresh) ||
                        IrFieldSites.IsFieldRead(program.Factory, term) && !IsLocal(((IrOpaqueTerm)term).Receiver!, fresh))
                    {
                        readers.Add(instruction.Id);
                        break;
                    }
                    pending = IrTraversal.PushChildren(term, pending);
                }
                Transfer(instruction, fresh);
            }
        }
        return readers.ToImmutable();

        ImmutableHashSet<IrVarId>.Builder Incoming(IrBlockId block)
        {
            RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
            var previous = predecessors[block];
            if (block == program.Entry || previous.Count == 0)
            { return ImmutableHashSet.CreateBuilder<IrVarId>(); }
            var fresh = outgoing[previous[0]].ToBuilder();
            for (var index = 1; index < previous.Count; index++)
            { fresh.IntersectWith(outgoing[previous[index]]); }
            return fresh;
        }
    }

    private static void Transfer(IrInstruction instruction, ImmutableHashSet<IrVarId>.Builder fresh)
    {
        instruction = RepositoryParameterGuard.NotNull(instruction, nameof(instruction));
        fresh = RepositoryParameterGuard.NotNull(fresh, nameof(fresh));
        // Evaluate assignment provenance before removing the old target value.
        var local = instruction is IrAllocationInstruction ||
            instruction is IrAssignInstruction assign && IsLocal(assign.Value, fresh);
        foreach (var variable in IrInstructionFacts.WrittenVariables(instruction))
        {
            if (local)
            { fresh.Add(variable); }
            else
            { fresh.Remove(variable); }
        }
    }

    private static bool IsLocal(IrTerm sequence, ImmutableHashSet<IrVarId>.Builder fresh)
    {
        fresh = RepositoryParameterGuard.NotNull(fresh, nameof(fresh));
        sequence = RepositoryParameterGuard.NotNull(sequence, nameof(sequence));
        return sequence switch
        {
            IrVariableTerm variable => fresh.Contains(variable.Variable),
            IrStringTerm or IrEmptyArrayTerm => true,
            IrBinaryTerm { Operator: IrBinaryOperator.StringConcat } concat => IsLocal(concat.Left, fresh) && IsLocal(concat.Right, fresh),
            IrConditionalTerm conditional => IsLocal(conditional.WhenTrue, fresh) && IsLocal(conditional.WhenFalse, fresh),
            IrCastTerm cast => IsLocal(cast.Operand, fresh),
            _ => false
        };
    }
}
