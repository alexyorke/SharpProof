namespace SharpProof.Ir;

// Legacy remains the authority during the dual-pipeline migration.
public enum IrExecutionSemantics
{
    Legacy = 0,
    Total = 1
}

public sealed class IrSourceSpan
{
    // Line and column are one-based display positions; zero means unknown.
    public IrSourceSpan(string document, int start, int length, int line = 0, int column = 0)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            throw new ArgumentException("A source document is required.", nameof(document));
        }
        if (start < 0 || length < 0 || start > int.MaxValue - length)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "The UTF-16 source span is invalid.");
        }
        if (line < 0 || column < 0 || (line == 0) != (column == 0))
        {
            throw new ArgumentOutOfRangeException(nameof(line), "The source position is invalid.");
        }
        (Document, Start, Length, Line, Column) = (document, start, length, line, column);
    }

    public string Document { get; }
    public int Start { get; }
    public int Length { get; }
    public int Line { get; }
    public int Column { get; }
}

internal static class IrCallPreconditionMarker
{
    internal const string Prefix = "$sharpproof.requires:";
    private static readonly System.Text.UTF8Encoding Encoding = new(false, true);

    internal static bool IsReservedName(string name)
    { return name.StartsWith(Prefix, StringComparison.Ordinal); }

    internal static bool TryCreateName(string calleeIdentity, int clauseOrdinal,
        IrSourceSpan? callSite, IrSourceSpan? clauseSite, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(calleeIdentity) || clauseOrdinal < 0 || clauseOrdinal >= 4096 ||
            callSite is not { Length: > 0 } || clauseSite is not { Length: > 0 })
        { return false; }
        try
        {
            if (calleeIdentity.Length > 4096 || callSite.Document.Length > 4096 || clauseSite.Document.Length > 4096 ||
                Encoding.GetByteCount(calleeIdentity) > 4096 || Encoding.GetByteCount(callSite.Document) > 4096 ||
                Encoding.GetByteCount(clauseSite.Document) > 4096)
            { return false; }
            var callee = Encoding.GetBytes(calleeIdentity);
            var callDocument = Encoding.GetBytes(callSite.Document);
            var clauseDocument = Encoding.GetBytes(clauseSite.Document);
            name = Prefix + "v1:" + Convert.ToBase64String(callee) + ":" + Number(clauseOrdinal) + ":" +
                Convert.ToBase64String(callDocument) + ":" + Number(callSite.Start) + ":" + Number(callSite.Length) + ":" +
                Convert.ToBase64String(clauseDocument) + ":" + Number(clauseSite.Start) + ":" + Number(clauseSite.Length);
            return true;
        }
        catch (System.Text.EncoderFallbackException)
        { return false; }
    }


    internal static bool TryCreateMetadataName(string calleeIdentity, int clauseOrdinal,
        IrSourceSpan? callSite, string evidenceSha256, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(calleeIdentity) || calleeIdentity.Length > 4096 ||
            clauseOrdinal < 0 || clauseOrdinal >= 4096 || callSite is not { Length: > 0 } ||
            callSite.Document.Length > 4096 || evidenceSha256 is not { Length: 64 } ||
            evidenceSha256.Any(static value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        { return false; }
        try
        {
            if (Encoding.GetByteCount(calleeIdentity) > 4096 || Encoding.GetByteCount(callSite.Document) > 4096)
            { return false; }
            name = Prefix + "v2:metadata:" + Convert.ToBase64String(Encoding.GetBytes(calleeIdentity)) + ":" +
                Number(clauseOrdinal) + ":" + Convert.ToBase64String(Encoding.GetBytes(callSite.Document)) + ":" +
                Number(callSite.Start) + ":" + Number(callSite.Length) + ":" + evidenceSha256;
            return true;
        }
        catch (System.Text.EncoderFallbackException)
        { return false; }
    }

    private static string Number(int value)
    { return value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
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
    internal ImmutableHashSet<IrVarId> SnapshotVariables { get; set; } = [];

    public Action<IrAssignInstruction, IrValue, bool>? AssignmentObserver { get; set; }
    public Action<IrAllocationInstruction>? AllocationObserver { get; set; }
    public Action<IrWriteInstruction>? WriteObserver { get; set; }
    public Action<IrLockInstruction>? LockObserver { get; set; }
    public Action<IrAllocationInstruction, bool>? AllocationPrefixObserver { get; set; }
    public Action<IrWriteInstruction, bool>? WritePrefixObserver { get; set; }
    public Action<IrLockInstruction, bool>? LockPrefixObserver { get; set; }

    public Func<IrHavocRequest, IrValue?> HavocValueProvider { get; } =
        ArgumentNullGuard.NotNull(havocValueProvider, nameof(havocValueProvider));
}
