namespace SharpProof.Ir;

// Reading the length or an element of a string or array, or a field of an
// object, is a read of the state it belongs to, unless the body created that
// sequence or object itself. The IR
// models such reads exactly, so they are found from the terms rather than
// from approximation sites.
internal static class IrSequenceReads
{
    internal static ImmutableHashSet<IrInstructionId> Readers(IrProgram program)
    {
        ArgumentNullGuard.NotNull(program, nameof(program));
        var instructions = program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).ToImmutableArray();
        var writes = ImmutableDictionary.CreateBuilder<IrVarId, ImmutableArray<IrInstruction>.Builder>();
        foreach (var instruction in instructions)
        {
            foreach (var variable in IrInstructionFacts.WrittenVariables(instruction))
            {
                if (!writes.TryGetValue(variable, out var writers))
                {
                    writers = ImmutableArray.CreateBuilder<IrInstruction>();
                    writes[variable] = writers;
                }
                writers.Add(instruction);
            }
        }
        // A fresh variable only ever holds a sequence this body allocated or
        // a literal.
        var fresh = writes.Where(pair => pair.Value.All(writer =>
            writer is null ? throw new System.ArgumentNullException(nameof(writer)) : (writer is IrAllocationInstruction or IrAssignInstruction))).Select(pair => pair.Key).ToImmutableHashSet().ToBuilder();
        while (true)
        {
            var previous = fresh.ToImmutable();
            foreach (var variable in previous)
            {
                if (variable.IsDefault)
                { RepositoryParameterGuard.ThrowIfInvalid(variable.IsDefault, nameof(variable)); }
                if (writes[variable].Any(writer =>
                    writer is null ? throw new ArgumentNullException(nameof(writer)) :
                    writer is IrAssignInstruction assign && !IsLocal(assign.Value, fresh)))
                {
                    fresh.Remove(variable);
                }
            }
            if (fresh.Count == previous.Count)
            { break; }
        }
        var readers = ImmutableHashSet.CreateBuilder<IrInstructionId>();
        foreach (var instruction in instructions)
        {
            var pending = ImmutableStack<IrTerm>.Empty;
            foreach (var root in IrInstructionFacts.ReadTerms(instruction))
            { pending = pending.Push(root); }
            while (!pending.IsEmpty)
            {
                var term = pending.Peek();
                pending = pending.Pop();
                if (term is IrSequenceAccessTerm { Sequence: var accessed } && !IsLocal(accessed, fresh) ||
                    term is IrLengthTerm { Value: var measured } && !IsLocal(measured, fresh) ||
                    IrFieldSites.IsFieldRead(program.Factory, term) && !IsLocal(((IrOpaqueTerm)term).Receiver!, fresh))
                {
                    readers.Add(instruction.Id);
                    break;
                }
                pending = IrTraversal.PushChildren(term, pending);
            }
        }
        return readers.ToImmutable();
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
