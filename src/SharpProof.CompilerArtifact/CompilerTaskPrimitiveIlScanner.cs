using System.Collections.Immutable;

namespace SharpProof.CompilerArtifact;

[Flags]
internal enum CompilerTaskPrimitiveFaultKind { None = 0, Overflow = 1, DivideByZero = 2, NullReference = 4, RangeCheck = 8, NewArray = 16 }

internal sealed record CompilerTaskPrimitiveIlInstruction(int Offset, int OpCode, int Operand,
    int NextOffset, int TargetOffset, CompilerTaskPrimitiveFaultKind Faults);

internal sealed record CompilerTaskPrimitiveIlScan(ImmutableArray<CompilerTaskPrimitiveIlInstruction>? Instructions, string? UnknownReason);

// Lexical admission of complete compiler-emitted primitive bodies, not an IL
// verifier or runtime helper-selection premise. This table is shared by the
// collector and the native consumer; metadata ownership stays in the collector.
internal static class CompilerTaskPrimitiveIlScanner
{
    internal const int MaximumCodeBytes = 4096;

    internal static CompilerTaskPrimitiveIlScan Scan(ImmutableArray<byte> code, CancellationToken cancellation,
        Func<int, string?>? validateNewArrayType = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (code.IsDefault || code.Length is < 1 or > MaximumCodeBytes)
        { return new(null, "TaskEmittedFaultBodyInstructionUnsupported"); }
        var instructions = ImmutableArray.CreateBuilder<CompilerTaskPrimitiveIlInstruction>();
        var starts = new HashSet<int>();
        var cursor = 0;
        while (cursor < code.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            var offset = cursor;
            var opcode = (int)code[cursor++];
            if (opcode == 0xfe)
            {
                if (cursor == code.Length)
                { return new(null, "TaskEmittedFaultBodyInstructionUnsupported"); }
                opcode = 0xfe00 | code[cursor++];
            }
            if (opcode is 0x28 or 0x29 or 0x6f)
            { return new(null, code.Length - cursor >= 4 ? "TaskEmittedFaultBodyCallUnsupported" : "TaskEmittedFaultBodyInstructionUnsupported"); }
            var width = OperandBytes(opcode);
            if (width < 0 || width > code.Length - cursor)
            { return new(null, "TaskEmittedFaultBodyInstructionUnsupported"); }
            var operand = 0;
            if (width == 1)
            { operand = opcode == 0x1f || opcode is >= 0x2b and <= 0x37 ? unchecked((sbyte)code[cursor]) : code[cursor]; }
            else if (width == 2)
            { operand = code[cursor] | code[cursor + 1] << 8; }
            else if (width == 4)
            { operand = code[cursor] | code[cursor + 1] << 8 | code[cursor + 2] << 16 | code[cursor + 3] << 24; }
            cursor += width;
            // Resolve collector-owned metadata at this instruction, preserving
            // first-failure order without publishing an incomplete scan.
            if (opcode == 0x8d && validateNewArrayType?.Invoke(operand) is { } failure)
            { return new(null, failure); }
            var target = -1;
            if (opcode is >= 0x2b and <= 0x44)
            {
                var destination = (long)cursor + operand;
                if (destination < 0 || destination >= code.Length)
                { return new(null, "TaskEmittedFaultBodyInstructionUnsupported"); }
                target = (int)destination;
            }
            starts.Add(offset);
            instructions.Add(new(offset, opcode, operand, cursor, target, Faults(opcode)));
        }
        if (instructions[instructions.Count - 1].OpCode != 0x2a)
        { return new(null, "TaskEmittedFaultBodyInstructionUnsupported"); }
        foreach (var instruction in instructions)
        {
            cancellation.ThrowIfCancellationRequested();
            if (instruction.TargetOffset >= 0 && !starts.Contains(instruction.TargetOffset))
            { return new(null, "TaskEmittedFaultBodyInstructionUnsupported"); }
        }
        return new(instructions.ToImmutable(), null);
    }

    private static int OperandBytes(int opcode)
    {
        return opcode switch
        {
            0x0e or 0x10 or 0x11 or 0x13 or 0x1f or >= 0x2b and <= 0x37 => 1,
            0xfe09 or 0xfe0b or 0xfe0c or 0xfe0e => 2,
            0x20 or >= 0x38 and <= 0x44 or 0x8d => 4,
            0x00 or >= 0x02 and <= 0x0d or 0x14 or >= 0x15 and <= 0x1e or 0x25 or 0x26 or 0x2a or
            >= 0x58 and <= 0x69 or 0x6d or 0x82 or 0x83 or 0x84 or 0x86 or 0x87 or 0x88 or
            0x8e or 0x94 or 0x9e or >= 0xb3 and <= 0xb8 or 0xd1 or 0xd2 or >= 0xd6 and <= 0xdb or
            >= 0xfe01 and <= 0xfe05 => 0,
            _ => -1
        };
    }

    private static CompilerTaskPrimitiveFaultKind Faults(int opcode)
    {
        return opcode switch
        {
            0x5b or 0x5d => CompilerTaskPrimitiveFaultKind.DivideByZero | CompilerTaskPrimitiveFaultKind.Overflow,
            0x5c or 0x5e => CompilerTaskPrimitiveFaultKind.DivideByZero,
            0x82 or 0x83 or 0x84 or 0x86 or 0x87 or 0x88 or >= 0xb3 and <= 0xb8 or >= 0xd6 and <= 0xdb => CompilerTaskPrimitiveFaultKind.Overflow,
            0x8d => CompilerTaskPrimitiveFaultKind.NewArray,
            0x8e => CompilerTaskPrimitiveFaultKind.NullReference,
            0x94 or 0x9e => CompilerTaskPrimitiveFaultKind.NullReference | CompilerTaskPrimitiveFaultKind.RangeCheck,
            _ => CompilerTaskPrimitiveFaultKind.None
        };
    }
}
