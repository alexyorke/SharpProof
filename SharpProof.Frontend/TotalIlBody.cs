namespace SharpProof.Frontend;

// Build-time, immutable decoded implementation evidence. Only its translated
// original IR is serialized; these instructions never become a worker language.
internal sealed class TotalIlInstruction(int offset, string code, long operand,
    int target = -1, IMethodSymbol? method = null)
{
    internal int Offset { get; } = offset;
    internal string Code { get; } = code;
    internal long Operand { get; } = operand;
    internal int Target { get; } = target;
    internal IMethodSymbol? Method { get; } = method;
}

internal sealed class TotalIlBody(IMethodSymbol method, string imageSha256, string module,
    ImmutableArray<SpecialType> locals, bool initializeLocals, int maximumStack,
    ImmutableArray<TotalIlInstruction> instructions)
{
    internal IMethodSymbol Method { get; } = method;
    internal string ImageSha256 { get; } = imageSha256;
    internal string Module { get; } = module;
    internal ImmutableArray<SpecialType> Locals { get; } = locals;
    internal bool InitializeLocals { get; } = initializeLocals;
    internal int MaximumStack { get; } = maximumStack;
    internal ImmutableArray<TotalIlInstruction> Instructions { get; } = instructions;
}

internal delegate TotalIlBody? ResolveTotalIlBody(IMethodSymbol method, CancellationToken cancellationToken);
