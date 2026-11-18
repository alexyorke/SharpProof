namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalProgramLowerer
{
    private IrProgram InvalidateReferenceInputs(IrProgram program)
    {
        // A copy-backed frame's ref parameter is the frame's own variable: it
        // aliases no field or element, so no write can change it. A root's
        // writable ref or out parameter is the caller's storage.
        var writable = _context.Parameters.Where(binding => _context.IsWritableRootReference(binding.Parameter)).ToImmutableArray();
        var references = _context.Parameters.Where(binding => binding.Parameter.RefKind is RefKind.Ref or RefKind.In &&
                !(_context.WritesBackReferences && binding.Parameter.RefKind == RefKind.Ref) ||
                _context.IsWritableRootReference(binding.Parameter))
            .Select(binding => binding.Current).Distinct()
            .OrderBy(variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (variable.Value)).ToImmutableArray();
        if (references.IsEmpty)
        { return program; }
        var next = program.Blocks.SelectMany(block => block.Instructions)
            .Select(instruction => instruction.Id.Value).DefaultIfEmpty(-1).Max() + 1;
        var blocks = ImmutableArray.CreateBuilder<IrBasicBlock>(program.Blocks.Length);
        foreach (var block in program.Blocks)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var instructions = ImmutableArray.CreateBuilder<IrInstruction>();
            foreach (var instruction in block.Instructions)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                instructions.Add(instruction);
                // A store to a writable root reference, other than the entry
                // binding of its current cell, writes the caller's storage. It
                // may be a field or element, which a bare write forgets, or
                // another by-reference parameter, which is forgotten below.
                if (instruction is IrAssignInstruction assign &&
                    writable.FirstOrDefault(binding => binding.Current == assign.Target) is { } stored &&
                    !(assign.Value is IrVariableTerm { Variable: var source } && source == stored.Entry))
                {
                    instructions.Add(new IrWriteInstruction(new(program.Scope, next++), instruction.Operation, IrWriteRegion.Parameter));
                    next = Forget(program, instructions, instruction.Operation, references.Where(reference => reference != stored.Current), next);
                    continue;
                }
                var mayWrite = instruction is IrWriteInstruction { Region: not IrWriteRegion.Local } ||
                    instruction is IrCallInstruction call &&
                    (IrOpaqueCallSite.Effects(_context.Factory, call.Operation) & IrOpaqueCallEffects.Writes) != 0;
                if (!mayWrite)
                { continue; }
                // The caller may have passed a field or element by reference.
                // Preserve entry snapshots, but a write may change its current
                // cell even when no assignment names the parameter itself.
                next = Forget(program, instructions, instruction.Operation, references, next);
            }
            blocks.Add(new(block.Id, block.Name, instructions.ToImmutable()));
        }
        return new(program.Factory, program.Scope, program.Entry, blocks.ToImmutable());
    }

    private int Forget(IrProgram program, ImmutableArray<IrInstruction>.Builder instructions, OperationId operation,
        IEnumerable<IrVarId> references, int next)
    {
        foreach (var reference in references)
        {
            SpendRegion();
            var temporary = _context.Factory.CreateVariable("reference-alias:" + next.ToString(CultureInfo.InvariantCulture),
                _context.Factory.GetVariableInfo(reference).Type);
            instructions.Add(new IrHavocInstruction(new(program.Scope, next++), operation,
                IrHavocKind.Variables, [temporary], IrHavocOrigin.Approximation));
            SpendRegion();
            instructions.Add(new IrAssignInstruction(new(program.Scope, next++), operation,
                reference, _context.Factory.Variable(temporary)));
        }
        return next;
    }
}
