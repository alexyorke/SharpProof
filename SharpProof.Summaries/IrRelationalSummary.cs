namespace SharpProof.Summaries;

public enum IrSummaryOrigin
{
    Source = 0,
    ImplementationIl = 1,
    SpecificationPack = 2
}

public enum IrSummaryCompleteness
{
    CompleteNormalRelation = 0,
    Incomplete = 1
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

public enum IrSummaryTermination
{
    TerminatesOrThrows = 0,
    Unknown = 1
}

public enum IrSummaryExceptionKind
{
    UnknownRuntime = 0
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
                : evidenceIdentity.Length > 256 ||
                    evidenceIdentity.Any(static character =>
                        char.IsControl(character))))
        {
            throw new ArgumentException(
                "Summary evidence identity is invalid.",
                nameof(evidenceIdentity));
        }

        if (evidenceCallIdentity == null ||
            evidenceCallIdentity.Length > 256 ||
            evidenceCallIdentity.Any(static character =>
                char.IsControl(character)))
        {
            throw new ArgumentException(
                "Summary evidence call identity is invalid.",
                nameof(evidenceCallIdentity));
        }

        Origin = origin;
        EvidenceSha256 = evidenceSha256;
        EvidenceIdentity = evidenceIdentity;
        EvidenceCallIdentity = evidenceCallIdentity ??
            throw new ArgumentNullException(nameof(evidenceCallIdentity));
    }

    public IrSummaryOrigin Origin { get; }

    public string EvidenceSha256 { get; }

    public string EvidenceIdentity { get; }

    /// <summary>
    /// Identifies the summarized member that owns this evidence.  It is
    /// separate from <see cref="EvidenceIdentity"/> because a specification
    /// pack identity identifies the audited pack rather than the member.
    /// </summary>
    public string EvidenceCallIdentity { get; }

    private static bool IsSha256(string? value)
    {
        return value != null && value.Length == 64 &&
            value.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}

public sealed class IrSummarySignature
{
    public IrSummarySignature(
        IrMemberId member,
        IrVarId? receiver,
        IEnumerable<IrVarId> parameters,
        IrVarId result,
        IrSummaryProvenance provenance)
    {
        Member = member;
        Receiver = receiver;
        Parameters = parameters == null
            ? throw new ArgumentNullException(nameof(parameters))
            : parameters.ToImmutableArray();
        Result = result;
        Provenance = provenance ??
            throw new ArgumentNullException(nameof(provenance));
    }

    public IrMemberId Member { get; }

    public IrVarId? Receiver { get; }

    public ImmutableArray<IrVarId> Parameters { get; }

    public IrVarId Result { get; }

    public IrSummaryProvenance Provenance { get; }
}

public sealed class IrExceptionalSummaryExit
{
    internal IrExceptionalSummaryExit(
        IrSummaryExceptionKind kind,
        IrTerm? condition)
    {
        Kind = kind;
        Condition = condition;
    }

    public IrSummaryExceptionKind Kind { get; }

    /// <summary>
    /// Null denotes a conservative exceptional exit whose exact input
    /// partition is not expressible in the current IR.
    /// </summary>
    public IrTerm? Condition { get; }
}

public sealed class IrRelationalSummary
{
    internal IrRelationalSummary(
        IrFactory factory,
        IrSummarySignature signature,
        ImmutableArray<IrVarId> existentialVariables,
        IrTerm normalCompletion,
        IrTerm normalRelation,
        ImmutableArray<IrExceptionalSummaryExit> exceptionalExits,
        ImmutableArray<IrMemberId> dependencies,
        ImmutableArray<IrSummaryProvenance> dependencyProvenance,
        IrSummaryEffect effects,
        IrSummaryTermination termination)
    {
        Factory = factory;
        Signature = signature;
        ExistentialVariables = existentialVariables;
        NormalCompletion = normalCompletion;
        NormalRelation = normalRelation;
        ExceptionalExits = exceptionalExits;
        Dependencies = dependencies;
        DependencyProvenance = dependencyProvenance;
        Effects = effects;
        Termination = termination;
        Completeness = IrSummaryCompleteness.CompleteNormalRelation;
    }

    public IrFactory Factory { get; }

    public IrSummarySignature Signature { get; }

    public ImmutableArray<IrVarId> ExistentialVariables { get; }

    public IrTerm NormalCompletion { get; }

    public IrTerm NormalRelation { get; }

    public ImmutableArray<IrExceptionalSummaryExit> ExceptionalExits { get; }

    public ImmutableArray<IrMemberId> Dependencies { get; }

    public ImmutableArray<IrSummaryProvenance> DependencyProvenance { get; }

    public IrSummaryEffect Effects { get; }

    public IrSummaryTermination Termination { get; }

    public IrSummaryCompleteness Completeness { get; }
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
