using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpProof.Gates.Corpus;

// Conservative IL reachability is independent of the frontend and SMT facts.
// A potential site is a gap, since source preconditions may exclude its edge.
internal static class AllocationIlOracle
{
    private static readonly ImmutableDictionary<short, OpCode> Opcodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field is null ? throw new System.ArgumentNullException(nameof(field)) : (field.FieldType == typeof(OpCode)))
        .Select(field => field is null ? throw new System.ArgumentNullException(nameof(field)) : ((OpCode)field.GetValue(null)!)).ToImmutableDictionary(opcode /* SPINT001: all values valid - all opcode values, including default nop, can be inspected */ => opcode.Value);

    // The maintained callers supply methods from successful C# compiler emission.
    // Those bodies have complete operands and instruction-aligned branch/EH targets;
    // this oracle classifies their reachable allocation sites.
    internal static bool HasPotentialAllocation(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var body = method.GetMethodBody();
        var bytes = body?.GetILAsByteArray();
        if (body == null || bytes == null || bytes.Length == 0 || bytes.Length > CorpusExecutionDefaults.MaximumAllocationOracleBodyBytes)
        { return true; }
        var instructions = ImmutableDictionary<int, (OpCode Op, int Next, ImmutableArray<int> Targets)>.Empty;
        var cursor = 0;
        while (cursor < bytes.Length)
        {
            var offset = cursor;
            short code = bytes[cursor++];
            if (code == 0xfe)
            {
                code = unchecked((short)(0xfe00 | bytes[cursor++]));
            }
            var opcode = Opcodes[code];
            var size = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => cursor + 4 <= bytes.Length && BitConverter.ToInt32(bytes, cursor) >= 0 ? 4 + 4L * BitConverter.ToInt32(bytes, cursor) : -1,
                _ => 4
            };
            var next = cursor + (int)size;
            ImmutableArray<int> targets = opcode.OperandType switch
            {
                OperandType.ShortInlineBrTarget => [next + unchecked((sbyte)bytes[cursor])],
                OperandType.InlineBrTarget => [next + BitConverter.ToInt32(bytes, cursor)],
                OperandType.InlineSwitch => Enumerable.Range(0, BitConverter.ToInt32(bytes, cursor))
                    .Select(index => next + BitConverter.ToInt32(bytes, cursor + 4 + 4 * index)).ToImmutableArray(),
                _ => []
            };
            instructions = instructions.Add(offset, (opcode, next, targets));
            cursor = next;
        }
        var pending = ImmutableStack<int>.Empty;
        var seen = ImmutableHashSet<int>.Empty;
        pending = pending.Push(0);
        while (!pending.IsEmpty)
        {
            var offset = pending.Peek();
            pending = pending.Pop();
            if (seen.Contains(offset))
            { continue; }
            seen = seen.Add(offset);
            var instruction = instructions[offset];
            if (instruction.Op == OpCodes.Newobj || instruction.Op == OpCodes.Newarr || instruction.Op == OpCodes.Box ||
                instruction.Op == OpCodes.Throw || instruction.Op == OpCodes.Rethrow ||
                instruction.Op.FlowControl is FlowControl.Call || instruction.Op == OpCodes.Jmp)
            { return true; }
            foreach (var clause in body.ExceptionHandlingClauses)
            {
                if (offset >= clause.TryOffset && offset < clause.TryOffset + clause.TryLength)
                {
                    pending = pending.Push(clause.HandlerOffset);
                    if (clause.Flags == ExceptionHandlingClauseOptions.Filter)
                    { pending = pending.Push(clause.FilterOffset); }
                }
            }
            foreach (var target in instruction.Targets)
            { pending = pending.Push(target); }
            if (instruction.Op.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw) && instruction.Next < bytes.Length)
            { pending = pending.Push(instruction.Next); }
        }
        return false;
    }
}
