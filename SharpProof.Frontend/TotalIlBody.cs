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
    ImmutableArray<TotalIlInstruction> instructions, ImmutableArray<TotalIlClosedAttribute> parameterAttributes = default,
    string moduleMvid = "")
{
    internal IMethodSymbol Method { get; } = method;
    internal ImmutableArray<TotalIlClosedAttribute> ParameterAttributes { get; } = parameterAttributes.IsDefault ? [] : parameterAttributes;
    internal string ModuleMvid { get; } = moduleMvid;
    internal string ImageSha256 { get; } = imageSha256;
    internal string Module { get; } = module;
    internal ImmutableArray<SpecialType> Locals { get; } = locals;
    internal bool InitializeLocals { get; } = initializeLocals;
    internal int MaximumStack { get; } = maximumStack;
    internal ImmutableArray<TotalIlInstruction> Instructions { get; } = instructions;
}

internal delegate TotalIlBody? ResolveTotalIlBody(IMethodSymbol method, CancellationToken cancellationToken);

internal sealed class TotalIlClosedAttribute(int methodToken, int parameterOrdinal, int parameterSequence,
    int parameterToken, int attributeToken, int constructorToken, string attributeIdentity,
    string constructorIdentity, string kind, ImmutableArray<byte> constructorSignature, ImmutableArray<byte> valueBlob,
    long minimum, long maximum, int clauseOrdinal = -1)
{
    internal int MethodToken { get; } = methodToken;
    internal int ParameterOrdinal { get; } = parameterOrdinal;
    internal int ParameterSequence { get; } = parameterSequence;
    internal int ParameterToken { get; } = parameterToken;
    internal int AttributeToken { get; } = attributeToken;
    internal int ConstructorToken { get; } = constructorToken;
    internal string AttributeIdentity { get; } = attributeIdentity;
    internal string ConstructorIdentity { get; } = constructorIdentity;
    internal string Kind { get; } = kind;
    internal ImmutableArray<byte> ConstructorSignature { get; } = constructorSignature;
    internal ImmutableArray<byte> ValueBlob { get; } = valueBlob;
    internal long Minimum { get; } = minimum;
    internal long Maximum { get; } = maximum;
    internal int ClauseOrdinal { get; } = clauseOrdinal;

    internal TotalIlClosedAttribute WithClauseOrdinal(int ordinal)
    {
        return new(MethodToken, ParameterOrdinal, ParameterSequence, ParameterToken, AttributeToken,
            ConstructorToken, AttributeIdentity, ConstructorIdentity, Kind, ConstructorSignature,
            ValueBlob, Minimum, Maximum, ordinal);
    }
}

internal sealed class TotalMetadataPrecondition(IrTerm value, IrTerm safe, OperationId clauseSite,
    TotalIlBody body, TotalIlClosedAttribute evidence, string evidenceDigest)
{
    internal IrTerm Value { get; } = value;
    internal IrTerm Safe { get; } = safe;
    internal OperationId ClauseSite { get; } = clauseSite;
    internal TotalIlBody Body { get; } = body;
    internal TotalIlClosedAttribute Evidence { get; } = evidence;
    internal string EvidenceDigest { get; } = evidenceDigest;
}
