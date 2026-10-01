namespace SharpProof.Worker;

internal sealed partial class PassiveLoopCutter
{
    private sealed record ExceptionComponent(ImmutableArray<IrBlockId> Blocks, ImmutableArray<IrVarId> Writes);

    private bool FindExceptionComponents()
    {
        // Kosaraju's second, iterative walk uses the finished forward DFS.
        // Every cyclic component is checked before any encoded graph grows.
        var visited = new HashSet<IrBlockId>();
        var immutable = new HashSet<IrVarId>();
        foreach (var parameter in _candidate.Parameters)
        { Spend(); immutable.Add(parameter.Entry); immutable.Add(parameter.Old); }
        for (var ordinal = _finished.Count - 1; ordinal >= 0; ordinal--)
        {
            Spend();
            var first = _finished[ordinal];
            if (visited.Contains(first))
            { continue; }
            var nodes = new HashSet<IrBlockId>();
            var pending = new Stack<IrBlockId>();
            pending.Push(first);
            while (pending.Count != 0)
            {
                Spend();
                var block = pending.Pop();
                if (!visited.Add(block))
                { continue; }
                nodes.Add(block);
                if (_predecessors.TryGetValue(block, out var predecessors))
                {
                    foreach (var predecessor in predecessors)
                    { Spend(); pending.Push(predecessor); }
                }
            }
            if (nodes.Count == 1 && !Targets(_candidate.Program.GetBlock(first).Terminator).Contains(first))
            { continue; }
            var exceptionFlow = false;
            foreach (var block in nodes)
            {
                foreach (var instruction in _candidate.Program.GetBlock(block).Instructions)
                {
                    Spend();
                    if (instruction is IrAssumeInstruction)
                    { return false; }
                    exceptionFlow |= instruction is IrThrowInstruction;
                }
            }
            var writes = WrittenVariables(nodes);
            foreach (var variable in writes)
            {
                Spend();
                if (immutable.Contains(variable))
                { return false; }
            }
            if (!exceptionFlow)
            { continue; }
            Spend(nodes.Count);
            var component = new ExceptionComponent([.. nodes.OrderBy(block => block.Value)], writes);
            foreach (var block in nodes)
            { Spend(); _exceptionComponents.Add(block, component); }
        }
        return true;
    }

    private ImmutableArray<IrVarId> WrittenVariables(IEnumerable<IrBlockId> nodes)
    {
        var writes = new HashSet<IrVarId>();
        foreach (var node in nodes)
        {
            foreach (var instruction in _candidate.Program.GetBlock(node).Instructions)
            {
                Spend();
                if (instruction is IrAssignInstruction assign)
                { writes.Add(assign.Target); }
                else if (instruction is IrHavocInstruction havoc)
                {
                    foreach (var variable in havoc.Variables)
                    { Spend(); writes.Add(variable); }
                }
            }
        }
        Spend(writes.Count);
        return [.. writes.OrderBy(variable => variable.Value)];
    }
}
