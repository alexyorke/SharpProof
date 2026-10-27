#nullable enable

using System.Collections.Immutable;
using System.Text.Json.Serialization;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static class CompilerManifestArtifactVersions
{
    internal const string Schema = "SharpProof.CompilerManifest";
    internal const int Current = 56;
}

internal static class CompilerRelationalSummaryVersions
{
    internal const int Current = 2;
}

internal static class CompilerSpecificationPackCatalogVersions
{
    internal const int Current = 2;
    internal const string Sha256 = "53be86bbf2cd6b4c093caf96e308220809cbaa7cb39e0d475f84d34df5f6a178";
    internal const string PackIds = "dotnet.scalar";
    internal const string PackIdentities = "dotnet.scalar@1";
}

internal static class CompilerSpecificationPackVersions
{
    internal const int Current = 1;
}

internal enum CompilerContractKind
{
    Requires = 0,
    Ensures = 1,
    Assume = 2
}

internal enum CompilerContractEvidence
{
    CompilerBoundInvocation = 0,
    ClosedAttribute = 1,
    Companion = 2
}

internal sealed record CompilerCallablePreparation(
    WorkerCallableManifestEntry Entry,
    WorkerClaimReason FailureReason
)
{
    public WorkerCallableManifestEntry Entry { get; init; } = Entry ?? throw new ArgumentNullException(nameof(Entry));
    internal bool IsSuccess => FailureReason == WorkerClaimReason.None;
    internal WorkerDiagnosticContext? Diagnostic { get; init; }
    internal WorkerCallablePresentation? Presentation { get; init; }
    internal ImmutableArray<CompilerEffectClaimArtifact> EffectClaims { get; init; } = [];
    internal CompilerCompilationSnapshot Compilation { get; init; } = new();
    internal CompilerTotalCallablePreparation? Total { get; init; }
    internal CompilerTotalEntryPreparation? TotalEntry { get; init; }
    internal ResourceProgram? ResourceProgram { get; init; }
    internal string? ResourceUnknownReason { get; init; }
    internal CompilerForkJoinProjection? ForkJoinProjection { get; init; }
}

internal sealed record CompilerTotalEntryPreparation(string CallableId, IrFactory Factory,
    ImmutableArray<CompilerTotalParameter> Parameters, ImmutableArray<CompilerTotalClause> Clauses)
{
    public string CallableId { get; init; } = CallableId ?? throw new ArgumentNullException(nameof(CallableId));
    public IrFactory Factory { get; init; } = Factory ?? throw new ArgumentNullException(nameof(Factory));
    public ImmutableArray<CompilerTotalParameter> Parameters { get; init; } = Parameters.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Parameters)) : Parameters;
    public ImmutableArray<CompilerTotalClause> Clauses { get; init; } = Clauses.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Clauses)) : Clauses;
    // The last parameter is `this`, which is never null.
    internal bool HasReceiver { get; init; }
}

internal sealed record CompilerTotalCallablePreparation(
    string CallableId,
    IrProgram Program,
    ImmutableArray<CompilerTotalParameter> Parameters,
    IrVarId? Result,
    ImmutableArray<CompilerTotalClause> Clauses,
    bool IsBodyAbstraction = false)
{
    public string CallableId { get; init; } = CallableId ?? throw new ArgumentNullException(nameof(CallableId));
    public IrProgram Program { get; init; } = Program ?? throw new ArgumentNullException(nameof(Program));
    public ImmutableArray<CompilerTotalParameter> Parameters { get; init; } = Parameters.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Parameters)) : Parameters;
    public ImmutableArray<CompilerTotalClause> Clauses { get; init; } = Clauses.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Clauses)) : Clauses;
    internal ImmutableArray<CompilerDisjointInputPair> DisjointInputs { get; init; } = [];
    internal ImmutableArray<CompilerTotalExceptionConstraint> ExceptionConstraints { get; init; } = [];
    internal ImmutableArray<CompilerTotalCallPrecondition> CallPreconditions { get; init; } = [];
    internal bool EffectsCompleteAtEntry { get; init; }
    internal ImmutableArray<string> ValidEffectClaimIds { get; init; } = [];
    // The last parameter is `this`, which is never null.
    internal bool HasReceiver { get; init; }
}

// Detached graph validation only. External source ownership is established
// separately by the shadow envelope; the owner label is not authentication.
internal sealed class CompilerDecodedShadowBody
{
    private CompilerDecodedShadowBody(CompilerTotalCallablePreparation body)
    {
        body = RepositoryParameterGuard.NotNull(body, nameof(body));
        Body = body;
    }

    internal CompilerTotalCallablePreparation Body { get; }

    internal static CompilerDecodedShadowBody Decode(string ownerId,
        CompilerTotalCallableArtifact artifact, CancellationToken cancellationToken, ImmutableArray<CompilerReferenceSnapshot>? metadataReferences = null)
    {
        ownerId = RepositoryParameterGuard.NotNull(ownerId, nameof(ownerId));
        artifact = RepositoryParameterGuard.NotNull(artifact, nameof(artifact));
        return new(CompilerTotalCallableArtifactCodec.DecodeShadowBodyCore(ownerId, artifact, cancellationToken, metadataReferences));
    }
}

internal sealed record CompilerTotalExceptionConstraint(string ClaimId, ImmutableArray<IrExceptionKind> AllowedKinds)
{
    public string ClaimId { get; init; } = ClaimId ?? throw new ArgumentNullException(nameof(ClaimId));
    public ImmutableArray<IrExceptionKind> AllowedKinds { get; init; } = AllowedKinds.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(AllowedKinds)) : AllowedKinds;
}

internal sealed record CompilerTotalCallPrecondition(IrInstructionId Instruction, string CalleeIdentity,
    int ClauseOrdinal, OperationId ClauseSite, IrTerm Value, IrTerm Safe,
    CompilerMetadataClauseOrigin? MetadataClause = null)
{
    public IrInstructionId Instruction { get; init; } = Instruction.IsDefault ? throw new ArgumentOutOfRangeException(nameof(Instruction)) : Instruction;
    public string CalleeIdentity { get; init; } = CalleeIdentity ?? throw new ArgumentNullException(nameof(CalleeIdentity));
    public OperationId ClauseSite { get; init; } = ClauseSite.IsDefault ? throw new ArgumentOutOfRangeException(nameof(ClauseSite)) : ClauseSite;
    public IrTerm Value { get; init; } = Value ?? throw new ArgumentNullException(nameof(Value));
    public IrTerm Safe { get; init; } = Safe ?? throw new ArgumentNullException(nameof(Safe));
    internal ImmutableArray<CompilerShadowCallHop> Ancestry { get; init; } = [];
}

internal sealed record CompilerShadowCallHop(string CallerIdentity, string CalleeIdentity, IrSourceSpan Site)
{
    public string CallerIdentity { get; init; } = CallerIdentity ?? throw new ArgumentNullException(nameof(CallerIdentity));
    public string CalleeIdentity { get; init; } = CalleeIdentity ?? throw new ArgumentNullException(nameof(CalleeIdentity));
    public IrSourceSpan Site { get; init; } = Site ?? throw new ArgumentNullException(nameof(Site));
}

internal readonly record struct CompilerTotalParameter(IrVarId Entry, IrVarId Current, IrVarId Old)
{
    public IrVarId Entry { get; init; } = Entry.IsDefault ? throw new ArgumentOutOfRangeException(nameof(Entry)) : Entry;
    public IrVarId Current { get; init; } = Current.IsDefault ? throw new ArgumentOutOfRangeException(nameof(Current)) : Current;
    public IrVarId Old { get; init; } = Old.IsDefault ? throw new ArgumentOutOfRangeException(nameof(Old)) : Old;
}

internal sealed record CompilerDisjointInputPair([property: JsonRequired] int Left, [property: JsonRequired] int Right);

internal sealed record CompilerTotalClause(CompilerContractKind Kind, IrTerm Value, IrTerm Safe,
    OperationId Operation, string? ClaimId, string? AssumptionId)
{
    public IrTerm Value { get; init; } = Value ?? throw new ArgumentNullException(nameof(Value));
    public IrTerm Safe { get; init; } = Safe ?? throw new ArgumentNullException(nameof(Safe));
    public OperationId Operation { get; init; } = Operation.IsDefault ? throw new ArgumentOutOfRangeException(nameof(Operation)) : Operation;
}

internal sealed record CompilerTotalCallableArtifact
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<CompilerDisjointInputPair>? DisjointInputs { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EffectsCompleteAtEntry { get; init; }
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<string> ValidEffectClaimIds { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsBodyAbstraction { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasReceiver { get; init; }
    public PortableIrGraph Graph { get; init; } = new();
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<CompilerTotalParameterArtifact> Parameters { get; init; } = [];
    public int Result { get; init; } = -1;
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<CompilerTotalClauseArtifact> Clauses { get; init; } = [];
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<CompilerTotalExceptionConstraintArtifact> ExceptionConstraints { get; init; } = [];
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<CompilerTotalCallPreconditionArtifact> CallPreconditions { get; init; } = [];
}

internal sealed record CompilerMetadataClauseOrigin(string AssemblyIdentity, string ImageSha256,
    string ModuleName, string ModuleMvid, int MethodToken, int ParameterOrdinal, int ParameterSequence,
    int ParameterToken, int AttributeToken, int ConstructorToken, string AttributeIdentity,
    string ConstructorIdentity, string Kind, long Minimum, long Maximum,
    ImmutableArray<byte> ConstructorSignature, ImmutableArray<byte> ValueBlob)
{
    public string AssemblyIdentity { get; init; } = AssemblyIdentity ?? throw new ArgumentNullException(nameof(AssemblyIdentity));
    public string ImageSha256 { get; init; } = ImageSha256 ?? throw new ArgumentNullException(nameof(ImageSha256));
    public string ModuleName { get; init; } = ModuleName ?? throw new ArgumentNullException(nameof(ModuleName));
    public string ModuleMvid { get; init; } = ModuleMvid ?? throw new ArgumentNullException(nameof(ModuleMvid));
    public string AttributeIdentity { get; init; } = AttributeIdentity ?? throw new ArgumentNullException(nameof(AttributeIdentity));
    public string ConstructorIdentity { get; init; } = ConstructorIdentity ?? throw new ArgumentNullException(nameof(ConstructorIdentity));
    public string Kind { get; init; } = Kind ?? throw new ArgumentNullException(nameof(Kind));
    public ImmutableArray<byte> ConstructorSignature { get; init; } = ConstructorSignature.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(ConstructorSignature)) : ConstructorSignature;
    public ImmutableArray<byte> ValueBlob { get; init; } = ValueBlob.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(ValueBlob)) : ValueBlob;
}

internal sealed record CompilerTotalCallPreconditionArtifact
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompilerMetadataClauseOrigin? MetadataClause { get; init; }

    public int InstructionIndex { get; init; } = -1;
    public string CalleeIdentity { get; init; } = string.Empty;
    public int ClauseOrdinal { get; init; } = -1;
    public int ClauseSite { get; init; } = -1;
    public int ValueRoot { get; init; } = -1;
    public int SafeRoot { get; init; } = -1;
}

internal sealed record CompilerTotalExceptionConstraintArtifact
{
    public string ClaimId { get; init; } = string.Empty;
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<IrExceptionKind> AllowedKinds { get; init; } = [];
}

internal sealed record CompilerTotalParameterArtifact
{
    public int Entry { get; init; } = -1;
    public int Current { get; init; } = -1;
    public int Old { get; init; } = -1;
}

internal sealed record CompilerTotalClauseArtifact
{
    public CompilerContractKind Kind { get; init; }
    public int ValueRoot { get; init; } = -1;
    public int SafeRoot { get; init; } = -1;
    public int Operation { get; init; } = -1;
    public string? ClaimId { get; init; }
    public string? AssumptionId { get; init; }
}

internal static class CompilerArtifactLimits
{
    internal const int MaximumInstructions = 4096;
}

internal sealed record CompilerSpecificationPackConfiguration
{
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<string> SpecificationPackIds { get; init; } = [];
    public int SpecificationPackCatalogVersion { get; init; } = CompilerSpecificationPackCatalogVersions.Current;
    public string SpecificationPackCatalogSha256 { get; init; } = CompilerSpecificationPackCatalogVersions.Sha256;
}

internal sealed record CompilerCallableArtifact
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkerCallablePresentation? Presentation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkerDiagnosticContext? Diagnostic { get; init; }
    public string CallableId { get; init; } = string.Empty;
    public WorkerClaimReason FailureReason { get; init; }
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<CompilerEffectClaimArtifact> EffectClaims { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompilerTotalCallableArtifact? Total { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompilerTotalCallableArtifact? TotalEntry { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? ResourceProgram { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResourceUnknownReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? ForkJoinProjection { get; init; }
}

internal sealed record CompilerEffectClaimArtifact
{
    public string ClaimId { get; init; } = string.Empty;
    public WorkerEffectContractKind ContractKind { get; init; }
    public WorkerClaimOutcome Outcome { get; init; }
    public WorkerClaimReason Reason { get; init; }
    public WorkerEffectEvidenceCertainty Certainty { get; init; }
    public CompilerEffectConstraintArtifact Constraint { get; init; } = new();
    public string Evidence { get; init; } = string.Empty;
    public string EvidenceSha256 { get; set; } = string.Empty;
}

internal sealed record CompilerEffectConstraintArtifact
{
    public WorkerEffectSet AllowedEffects { get; init; }
    public WorkerEffectCapabilitySet AllowedCapabilities { get; init; }
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<string> AllowedExceptionTypes { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkerResourceConstraint? ResourceUpperBound { get; init; }
}

internal enum CompilerOutputKind
{
    ConsoleApplication = 0,
    WindowsApplication = 1,
    DynamicallyLinkedLibrary = 2,
    NetModule = 3,
    WindowsRuntimeMetadata = 4,
    WindowsRuntimeApplication = 5
}

internal enum CompilerOptimizationLevel
{
    Debug = 0,
    Release = 1
}

internal enum CompilerPlatform
{
    AnyCpu = 0,
    AnyCpu32BitPreferred = 1,
    Arm = 2,
    Arm64 = 3,
    Itanium = 4,
    X64 = 5,
    X86 = 6
}

internal enum CompilerNullableContext
{
    Disable = 0,
    Warnings = 1,
    Annotations = 2,
    Enable = 3
}

internal enum CompilerMetadataImportOptions
{
    Public = 0,
    Internal = 1,
    All = 2
}

internal enum CompilerAssemblyIdentityComparer
{
    Default = 0,
    Desktop = 1
}

internal enum CompilerResolverPolicy
{
    EvidenceOnly = 0
}

internal sealed record CompilerDiagnosticArtifact
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool IsSource { get; init; }
    public WorkerSourceLocation Location { get; init; } = new();
    [JsonIgnore]
    public int SourceTreeOrdinal { get; init; } = -1;
    [JsonIgnore]
    public string SourceTreePath { get; init; } = string.Empty;
    [JsonIgnore]
    public string SourceTreeSha256 { get; init; } = string.Empty;
    [JsonIgnore]
    public string SourceLineMapSha256 { get; init; } = string.Empty;
}

internal sealed record CompilerManifestArtifact
{
    public string Schema { get; init; } = CompilerManifestArtifactVersions.Schema;
    public int SchemaVersion { get; init; } = CompilerManifestArtifactVersions.Current;
    public string ProtocolVersion { get; init; } = WorkerProtocolVersions.Current;
    public int RelationalSummarySchemaVersion { get; init; } = CompilerRelationalSummaryVersions.Current;
    public int SpecificationPackSchemaVersion { get; init; } = CompilerSpecificationPackVersions.Current;
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<string> SpecificationPackIds { get; init; } = [];
    public int SpecificationPackCatalogVersion { get; init; } = CompilerSpecificationPackCatalogVersions.Current;
    public string SpecificationPackCatalogSha256 { get; init; } = CompilerSpecificationPackCatalogVersions.Sha256;
    public WorkerFeatureSet Features { get; init; }
    public CompilerCompilationSnapshot Compilation { get; init; } = new();
    public WorkerClaimManifest Manifest { get; init; } = new();
    public int MaximumExpressionDepth { get; init; } = WorkerBudgets.DefaultMaximumExpressionDepth;
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<CompilerDiagnosticArtifact> CompilerDiagnostics { get; init; } = [];
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<CompilerCallableArtifact> Callables { get; init; } = [];
    public CompilerReachableSourceArtifact? ReachableSource { get; init; }
}

// Shadow input for interprocedural effect analysis. An absent or incomplete
// body never supplies an effect fact or a normal-completion guarantee.
internal sealed record CompilerReachableSourceArtifact
{
    public bool CollectionComplete { get; init; }
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<CompilerSourceRootArtifact> Roots { get; init; } = [];
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<CompilerSourceBodyArtifact> Bodies { get; init; } = [];
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<CompilerSourceDocumentArtifact> Documents { get; init; } = [];
}

internal sealed record CompilerSourceDocumentArtifact
{
    public int SourceTreeOrdinal { get; init; }
    public string Path { get; init; } = string.Empty;
    public int MaximumBodyEnd { get; init; }
}

internal sealed record CompilerSourceRootArtifact
{
    public string CallableId { get; init; } = string.Empty;
    public string BodyId { get; init; } = string.Empty;
}

internal sealed record CompilerSourceBodyArtifact
{
    public string BodyId { get; init; } = string.Empty;
    public string MethodIdentity { get; init; } = string.Empty;
    public string CallIdentity { get; init; } = string.Empty;
    public bool IsStatic { get; init; }
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<string> ParameterTypes { get; init; } = [];
    public string? ReturnType { get; init; }
    public int SourceTreeOrdinal { get; init; }
    public int Start { get; init; }
    public int Length { get; init; }
    public bool CallsComplete { get; init; }
    public bool EffectsCompleteAtEntry { get; init; }
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<string> Callees { get; init; } = [];
    public PortableIrGraph? Graph { get; init; }
    public bool IsCallSkeleton { get; init; }
    [JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<CompilerSourceCallArtifact> SourceCalls { get; init; } = [];
}

internal sealed record CompilerSourceCallArtifact
{
    public int InstructionIndex { get; init; }
    public string CalleeBodyId { get; init; } = string.Empty;
}

internal readonly struct CompilerEffectConstraintRule(
    WorkerEffectContractKind kind,
    bool effectsMustBeEmpty,
    bool capabilitiesMustBeEmpty,
    bool exceptionsMustBeEmpty)
{
    internal WorkerEffectContractKind Kind { get; } = kind;
    internal bool EffectsMustBeEmpty { get; } = effectsMustBeEmpty;
    internal bool CapabilitiesMustBeEmpty { get; } = capabilitiesMustBeEmpty;
    internal bool ExceptionsMustBeEmpty { get; } = exceptionsMustBeEmpty;
}

internal static class CompilerEffectEvidenceCatalog
{
    internal const string EvidenceDomain = "SharpProof.CompilerEffectClaimEvidence";
    internal const int EvidenceVersion = 11;
    internal static readonly ImmutableArray<WorkerClaimReason> UnknownReasons = [
        WorkerClaimReason.UnsupportedContract,
        WorkerClaimReason.CounterexampleNotReplayable,
        WorkerClaimReason.EffectSummaryIncomplete,
        WorkerClaimReason.EffectContractNotEstablished,
        WorkerClaimReason.ResourceLimit,
        WorkerClaimReason.UnsupportedBody,
    ];
    internal static readonly ImmutableArray<(WorkerClaimOutcome Outcome, WorkerClaimReason Reason,
        WorkerEffectEvidenceCertainty Certainty)> SupportedEffectTuples = [
        (WorkerClaimOutcome.Proven, WorkerClaimReason.None, WorkerEffectEvidenceCertainty.CompleteMayEffectSummary),
        (WorkerClaimOutcome.Proven, WorkerClaimReason.None, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary),
        (WorkerClaimOutcome.Proven, WorkerClaimReason.None, WorkerEffectEvidenceCertainty.VacuousEntry),
        (WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedContract, WorkerEffectEvidenceCertainty.Unavailable),
        (WorkerClaimOutcome.Unknown, WorkerClaimReason.CounterexampleNotReplayable, WorkerEffectEvidenceCertainty.Unavailable),
        (WorkerClaimOutcome.Unknown, WorkerClaimReason.EffectSummaryIncomplete, WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary),
        (WorkerClaimOutcome.Unknown, WorkerClaimReason.EffectSummaryIncomplete, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary),
        (WorkerClaimOutcome.Unknown, WorkerClaimReason.EffectContractNotEstablished, WorkerEffectEvidenceCertainty.CompleteMayEffectSummary),
        (WorkerClaimOutcome.Unknown, WorkerClaimReason.EffectContractNotEstablished, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary),
        (WorkerClaimOutcome.Unknown, WorkerClaimReason.ResourceLimit, WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary),
        (WorkerClaimOutcome.Unknown, WorkerClaimReason.ResourceLimit, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary),
        (WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedBody, WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary),
        (WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedBody, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary),
    ];
    internal static bool HasValidEffectTuple(
        WorkerClaimOutcome outcome, WorkerClaimReason reason,
        WorkerEffectEvidenceCertainty certainty)
    {
        foreach (var tuple in SupportedEffectTuples)
        {
            if (tuple.Outcome == outcome && tuple.Reason == reason &&
                tuple.Certainty == certainty)
            {
                return true;
            }
        }
        return outcome == WorkerClaimOutcome.Unknown &&
            certainty == WorkerEffectEvidenceCertainty.Unavailable &&
            UnknownReasons.Contains(reason);
    }
    internal static readonly ImmutableArray<CompilerEffectConstraintRule> ConstraintRules = [
        new(WorkerEffectContractKind.EnforcePure, true, true, true),
        new(WorkerEffectContractKind.ZeroAllocations, true, true, true),
        new(WorkerEffectContractKind.ResourceUpperBound, true, true, true),
        new(WorkerEffectContractKind.AllowedCapabilities, true, false, true),
        new(WorkerEffectContractKind.DoesNotThrow, true, true, true),
        new(WorkerEffectContractKind.AllowedExceptions, true, true, false),
        new(WorkerEffectContractKind.EffectContract, false, false, false),
    ];
}

internal static class CompilerCallableArtifactReasonCatalog
{
    internal const WorkerClaimReason SuccessReason = WorkerClaimReason.None;
    internal const WorkerClaimReason DiagnosticFailureReason = WorkerClaimReason.UnsupportedCallable;
    internal static readonly ImmutableArray<WorkerClaimReason> FailureReasons = [
        WorkerClaimReason.UnsupportedCallable,
        WorkerClaimReason.UnsupportedContract,
        WorkerClaimReason.UnsupportedBody,
        WorkerClaimReason.UnsupportedExpression,
    ];
    internal static bool IsFailureReason(WorkerClaimReason reason)
    {
        return FailureReasons.Contains(reason);
    }
}
