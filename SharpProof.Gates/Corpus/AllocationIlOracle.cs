using System.Reflection;
using System.Reflection.Emit;

namespace SharpProof.Gates.Corpus;

// Conservative IL reachability is independent of the frontend and SMT facts.
// A potential site is a gap, since source preconditions may exclude its edge.
internal static class AllocationIlOracle
{
    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(opcode => opcode.Value);

    // The maintained callers supply methods from successful C# compiler emission.
    // Those bodies have complete operands and instruction-aligned branch/EH targets;
    // this oracle classifies their reachable allocation sites.
    internal static bool HasPotentialAllocation(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var body = method.GetMethodBody();
        var bytes = body?.GetILAsByteArray();
        if (body == null || bytes == null || bytes.Length == 0 || bytes.Length > 65536)
        { return true; }
        var instructions = new Dictionary<int, (OpCode Op, int Next, int[] Targets)>();
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
            var targets = opcode.OperandType switch
            {
                OperandType.ShortInlineBrTarget => new[] { next + unchecked((sbyte)bytes[cursor]) },
                OperandType.InlineBrTarget => [next + BitConverter.ToInt32(bytes, cursor)],
                OperandType.InlineSwitch => Enumerable.Range(0, BitConverter.ToInt32(bytes, cursor))
                    .Select(index => next + BitConverter.ToInt32(bytes, cursor + 4 + 4 * index)).ToArray(),
                _ => []
            };
            instructions.Add(offset, (opcode, next, targets));
            cursor = next;
        }
        var pending = new Stack<int>();
        var seen = new HashSet<int>();
        pending.Push(0);
        while (pending.Count != 0)
        {
            var offset = pending.Pop();
            if (!seen.Add(offset))
            { continue; }
            var instruction = instructions[offset];
            if (instruction.Op == OpCodes.Newobj || instruction.Op == OpCodes.Newarr || instruction.Op == OpCodes.Box ||
                instruction.Op == OpCodes.Throw || instruction.Op == OpCodes.Rethrow ||
                instruction.Op.FlowControl is FlowControl.Call || instruction.Op == OpCodes.Jmp)
            { return true; }
            foreach (var clause in body.ExceptionHandlingClauses)
            {
                if (offset >= clause.TryOffset && offset < clause.TryOffset + clause.TryLength)
                {
                    pending.Push(clause.HandlerOffset);
                    if (clause.Flags == ExceptionHandlingClauseOptions.Filter)
                    { pending.Push(clause.FilterOffset); }
                }
            }
            foreach (var target in instruction.Targets)
            { pending.Push(target); }
            if (instruction.Op.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw) && instruction.Next < bytes.Length)
            { pending.Push(instruction.Next); }
        }
        return false;
    }
}
