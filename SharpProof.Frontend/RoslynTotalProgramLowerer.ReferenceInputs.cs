namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalProgramLowerer
{
    private IrProgram InvalidateReferenceInputs(IrProgram program)
    {
        if (program is null)
        {
            throw new System.ArgumentNullException(nameof(program));
        }
        var references = _context.Parameters.Where(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Parameter.RefKind is RefKind.Ref or RefKind.In))
            .Select(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Current)).OrderBy(variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (variable.Value)).ToImmutableArray();
        if (references.IsEmpty)
        { return program; }
        var next = program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .Select(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Id.Value)).DefaultIfEmpty(-1).Max() + 1;
        var blocks = ImmutableArray.CreateBuilder<IrBasicBlock>(program.Blocks.Length);
        foreach (var block in program.Blocks)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var instructions = ImmutableArray.CreateBuilder<IrInstruction>();
            foreach (var instruction in block.Instructions)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                instructions.Add(instruction);
                var mayWrite = instruction is IrWriteInstruction { Region: not IrWriteRegion.Local } ||
                    instruction is IrCallInstruction call &&
                    (IrOpaqueCallSite.Effects(_context.Factory, call.Operation) & IrOpaqueCallEffects.Writes) != 0;
                if (!mayWrite)
                { continue; }
                // The caller may have passed a field or element by reference.
                // Preserve entry snapshots, but a write may change its current
                // cell even when no assignment names the parameter itself.
                foreach (var reference in references)
                {
                    SpendRegion();
                    var temporary = _context.Factory.CreateVariable("reference-alias:" + next.ToString(CultureInfo.InvariantCulture),
                        _context.Factory.GetVariableInfo(reference).Type);
                    instructions.Add(new IrHavocInstruction(new(program.Scope, next++), instruction.Operation,
                        IrHavocKind.Variables, [temporary], IrHavocOrigin.Approximation));
                    SpendRegion();
                    instructions.Add(new IrAssignInstruction(new(program.Scope, next++), instruction.Operation,
                        reference, _context.Factory.Variable(temporary)));
                }
            }
            blocks.Add(new(block.Id, block.Name, instructions.ToImmutable()));
        }
        return new(program.Factory, program.Scope, program.Entry, blocks.ToImmutable());
    }
}
