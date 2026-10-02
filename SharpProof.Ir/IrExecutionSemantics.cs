namespace SharpProof.Ir;

// Legacy remains the authority during the dual-pipeline migration.
public enum IrExecutionSemantics
{
    Legacy = 0,
    Total = 1
}

public sealed class IrSourceSpan
{
    public IrSourceSpan(string document, int start, int length)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            throw new ArgumentException("A source document is required.", nameof(document));
        }
        if (start < 0 || length < 0 || start > int.MaxValue - length)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "The UTF-16 source span is invalid.");
        }
        (Document, Start, Length) = (document, start, length);
    }

    public string Document { get; }
    public int Start { get; }
    public int Length { get; }
}

public enum IrHavocOrigin
{
    Input = 0,
    SpecResult = 1,
    Approximation = 2
}

public sealed class IrHavocRequest(
    IrInstructionId instruction, OperationId site, IrVarId variable,
    int occurrence, IrHavocOrigin origin)
{
    public IrInstructionId Instruction { get; } = instruction;
    public OperationId Site { get; } = site;
    public IrVarId Variable { get; } = variable;
    public int Occurrence { get; } = occurrence;
    public IrHavocOrigin Origin { get; } = origin;
}

public sealed class IrProgramReplayOptions(Func<IrHavocRequest, IrValue?> havocValueProvider)
{
    public Action<IrAllocationInstruction>? AllocationObserver { get; set; }

    public Func<IrHavocRequest, IrValue?> HavocValueProvider { get; } =
        ArgumentNullGuard.NotNull(havocValueProvider, nameof(havocValueProvider));
}
