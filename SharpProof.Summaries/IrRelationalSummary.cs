namespace SharpProof.Summaries;

public enum IrSummaryOrigin
{
    Source = 0,
    ImplementationIl = 1,
    SpecificationPack = 2
}

public enum IrSummaryAbstentionReason
{
    None = 0,
    UnsupportedBody = 1,
    UnsupportedInstruction = 2,
    CyclicControlFlow = 3,
    MissingDependency = 4,
    InvalidSignature = 5,
    ResourceLimit = 6,
    ExpressionDepth = 7
}

public enum IrSummaryEffect
{
    None = 0,
    MayThrow = 1
}

public sealed class IrSummaryProvenance
{
    public IrSummaryProvenance(
        IrSummaryOrigin origin,
        string evidenceSha256,
        string evidenceIdentity = "",
        string evidenceCallIdentity = "")
    {
        if (!Enum.IsDefined(typeof(IrSummaryOrigin), origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        if (!IsSha256(evidenceSha256))
        {
            throw new ArgumentException(
                "Summary evidence must have a lowercase SHA-256 digest.",
                nameof(evidenceSha256));
        }

        if (evidenceIdentity == null ||
            (origin == IrSummaryOrigin.SpecificationPack
                ? string.IsNullOrWhiteSpace(evidenceIdentity)
                : evidenceIdentity.Length != 0))
        {
            throw new ArgumentException(
                "Only specification-pack summaries require a nonblank evidence identity.",
                nameof(evidenceIdentity));
        }

        Origin = origin;
        EvidenceSha256 = evidenceSha256;
        EvidenceIdentity = evidenceIdentity;
        EvidenceCallIdentity = evidenceCallIdentity ?? throw new ArgumentNullException(
            nameof(evidenceCallIdentity));
    }

    public IrSummaryOrigin Origin { get; }

    public string EvidenceSha256 { get; }

    public string EvidenceIdentity { get; }

    public string EvidenceCallIdentity { get; }

    private static bool IsSha256(string? value)
    {
        return value != null && value.Length == 64 &&
            value.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}

public sealed class IrSummarySignature(
    IrMemberId member,
    IrVarId? receiver,
    IEnumerable<IrVarId> parameters,
    IrVarId result,
    IrSummaryProvenance provenance)
{
    public IrMemberId Member { get; } = member;

    public IrVarId? Receiver { get; } = receiver;

    public ImmutableArray<IrVarId> Parameters { get; } = parameters == null
        ? throw new ArgumentNullException(nameof(parameters))
        : parameters.ToImmutableArray();

    public IrVarId Result { get; } = result;

    public IrSummaryProvenance Provenance { get; } = provenance ??
        throw new ArgumentNullException(nameof(provenance));
}

public sealed class IrRelationalSummary
{
    internal IrRelationalSummary(
        IrFactory factory,
        IrSummarySignature signature,
        ImmutableArray<IrVarId> existentialVariables,
        IrTerm normalCompletion,
        IrTerm normalRelation,
        ImmutableArray<IrMemberId> dependencies,
        ImmutableArray<IrSummaryProvenance> dependencyProvenance,
        IrSummaryEffect effects)
    {
        Factory = factory;
        Signature = signature;
        ExistentialVariables = existentialVariables;
        NormalCompletion = normalCompletion;
        NormalRelation = normalRelation;
        Dependencies = dependencies;
        DependencyProvenance = dependencyProvenance;
        Effects = effects;
    }

    public IrFactory Factory { get; }

    public IrSummarySignature Signature { get; }

    public ImmutableArray<IrVarId> ExistentialVariables { get; }

    public IrTerm NormalCompletion { get; }

    public IrTerm NormalRelation { get; }

    public ImmutableArray<IrMemberId> Dependencies { get; }

    public ImmutableArray<IrSummaryProvenance> DependencyProvenance { get; }

    public IrSummaryEffect Effects { get; }

}

public sealed class IrRelationalSummaryBuildResult
{
    internal IrRelationalSummaryBuildResult(
        IrRelationalSummary? summary,
        IrSummaryAbstentionReason reason)
    {
        Summary = summary;
        Reason = reason;
    }

    public IrRelationalSummary? Summary { get; }

    public IrSummaryAbstentionReason Reason { get; }

    public bool IsSuccess => Summary != null &&
        Reason == IrSummaryAbstentionReason.None;
}

public sealed class IrSummaryInstantiation
{
    internal IrSummaryInstantiation(
        IrVarId result,
        IrTerm normalCompletion,
        IrTerm normalRelation,
        ImmutableArray<IrVarId> freshVariables)
    {
        Result = result;
        NormalCompletion = normalCompletion;
        NormalRelation = normalRelation;
        FreshVariables = freshVariables;
    }

    public IrVarId Result { get; }

    public IrTerm NormalCompletion { get; }

    public IrTerm NormalRelation { get; }

    public ImmutableArray<IrVarId> FreshVariables { get; }
}
