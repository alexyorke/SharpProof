namespace SharpProof.Ir;

// Reading the length or an element of a string or array, or a field of an
// object, is a read of the state it belongs to, unless the body created that
// sequence or object itself. The IR
// models such reads exactly, so they are found from the terms rather than
// from approximation sites.
internal static class IrSequenceReads
{
    internal static HashSet<IrInstructionId> Readers(IrProgram program)
    {
        var instructions = program.Blocks.SelectMany(block => block.Instructions).ToArray();
        var writes = new Dictionary<IrVarId, List<IrInstruction>>();
        foreach (var instruction in instructions)
        {
            foreach (var variable in IrInstructionFacts.WrittenVariables(instruction))
            {
                if (!writes.TryGetValue(variable, out var writers))
                { writes.Add(variable, writers = []); }
                writers.Add(instruction);
            }
        }
        // A fresh variable only ever holds a sequence this body allocated or
        // a literal.
        var fresh = new HashSet<IrVarId>(writes.Where(pair => pair.Value.All(writer =>
            writer is IrAllocationInstruction or IrAssignInstruction)).Select(pair => pair.Key));
        while (fresh.RemoveWhere(variable => writes[variable].Any(writer =>
            writer is IrAssignInstruction assign && !IsLocal(assign.Value, fresh))) != 0)
        { }
        var readers = new HashSet<IrInstructionId>();
        foreach (var instruction in instructions)
        {
            var pending = new Stack<IrTerm>(IrInstructionFacts.ReadTerms(instruction));
            while (pending.Count != 0)
            {
                var term = pending.Pop();
                if (term is IrSequenceAccessTerm { Sequence: var accessed } && !IsLocal(accessed, fresh) ||
                    term is IrLengthTerm { Value: var measured } && !IsLocal(measured, fresh) ||
                    IrFieldSites.IsFieldRead(program.Factory, term) && !IsLocal(((IrOpaqueTerm)term).Receiver!, fresh))
                {
                    readers.Add(instruction.Id);
                    break;
                }
                IrTraversal.PushChildren(term, pending);
            }
        }
        return readers;
    }

    private static bool IsLocal(IrTerm sequence, HashSet<IrVarId> fresh)
    {
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
