using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SharpProof.Worker.Protocol;

internal enum WorkerRunStatus
{
    Unspecified = 0,
    Complete = 1,
    TimedOut = 2,
    Canceled = 3,
    Failed = 4
}

internal enum WorkerRunFailureReason
{
    Unspecified = 0,
    None = 1,
    InvalidRequest = 2,
    InputUnavailable = 3,
    CompilationFailure = 4,
    CompilerManifestMismatch = 5,
    BackendUnavailable = 6,
    InfrastructureFailure = 7,
    MalformedResult = 8,
    CounterexampleReplayFailed = 9,
    ContainmentFailure = 10
}

internal enum WorkerCallableCoverage
{
    Unspecified = 0,
    Complete = 1,
    Incomplete = 2
}

internal enum WorkerCallableCoverageReason
{
    Unspecified = 0,
    None = 1,
    UnsupportedCallable = 2,
    UnsupportedContract = 3,
    SemanticUnknown = 4,
    MissingClaimResult = 5,
    MethodTimeout = 6,
    ProjectTimeout = 7,
    Canceled = 8,
    InfrastructureFailure = 9
}

internal enum WorkerClaimOutcome
{
    Unspecified = 0,
    Proven = 1,
    Refuted = 2,
    Unknown = 3
}

internal enum WorkerEffectEvidenceCertainty
{
    Unspecified = 0,
    IncompleteMayEffectSummary = 1,
    CompleteMayEffectSummary = 2,
    TrustedCompleteBoundary = 3,
    DefiniteViolation = 4,
    Unavailable = 5,
    VacuousEntry = 6
}

[Flags]
internal enum WorkerEffectSet : long
{
    None = 0L,
    ReadsReceiverState = 1L,
    ReadsArgumentState = 2L,
    ReadsCapturedState = 4L,
    ReadsStaticState = 8L,
    ReadsAmbientState = 16L,
    WritesReceiverState = 32L,
    WritesArgumentState = 64L,
    WritesCapturedState = 128L,
    WritesStaticState = 256L,
    WritesAmbientState = 512L,
    Allocates = 1024L,
    Throws = 2048L,
    Synchronizes = 4096L,
    UsesNondeterminism = 8192L,
    UsesNativeCode = 16384L,
    UsesReflection = 32768L,
    AllKnown = 65535L
}

[Flags]
internal enum WorkerEffectCapabilitySet
{
    None = 0,
    IO = 1,
    FileRead = 2,
    FileWrite = 4,
    Network = 8,
    Console = 16,
    Process = 32,
    Environment = 64,
    Registry = 128,
    Clock = 256,
    Randomness = 512,
    Reflection = 1024,
    Synchronization = 2048,
    NativeInterop = 4096,
    AllKnown = 8191
}

internal enum WorkerVacuityKind
{
    None = 0,
    ContradictoryPreconditions = 1,
    NoModeledNormalReturn = 2
}

internal enum WorkerClaimReason
{
    Unspecified = 0,
    None = 1,
    UnsupportedCallable = 2,
    UnsupportedContract = 3,
    UnsupportedBody = 4,
    UnsupportedExpression = 5,
    DeepPostcondition = 6,
    MissingReturnValue = 7,
    ResourceLimit = 8,
    MethodTimeout = 9,
    ProjectTimeout = 10,
    Canceled = 11,
    BackendUnavailable = 12,
    InfrastructureFailure = 13,
    MalformedBackendResult = 14,
    CounterexampleReplayFailed = 15,
    PostconditionMayBeUndefined = 16,
    CounterexampleNotReplayable = 17,
    EffectSummaryIncomplete = 18,
    EffectContractNotEstablished = 19,
    SolverIncomplete = 20
}

internal enum WorkerAssumptionKind
{
    Unspecified = 0,
    Precondition = 1,
    UserAssume = 2,
    TrustedBoundary = 3,
    ApiSpecification = 4,
    SourceDomain = 5,
    NormalCompletion = 6
}

internal sealed record WorkerAssumptionEvidence
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;
    [JsonRequired]
    public WorkerAssumptionKind Kind { get; init; }
    [JsonRequired]
    public bool Used { get; init; }
}

internal sealed record WorkerCallableResult
{
    [JsonRequired]
    public string CallableId { get; init; } = string.Empty;
    [JsonRequired]
    public WorkerCallableCoverage Coverage { get; init; }
    [JsonRequired]
    public WorkerCallableCoverageReason Reason { get; init; }
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<WorkerAssumptionEvidence> Assumptions { get; set; } = [];
}

internal sealed record WorkerClaimResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkerCallablePresentation? Presentation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkerDiagnosticContext? Diagnostic { get; init; }
    [JsonRequired]
    public string ClaimId { get; init; } = string.Empty;
    [JsonRequired]
    public WorkerClaimOutcome Outcome { get; init; }
    [JsonRequired]
    public WorkerClaimReason Reason { get; init; }
    [JsonRequired]
    public WorkerEffectEvidenceCertainty EffectCertainty { get; init; }
    [JsonRequired]
    public WorkerEffectViolationWitness? EffectWitness { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkerResourceBoundEvidence? ResourceBound { get; init; }
    [JsonRequired]
    public WorkerVacuityKind Vacuity { get; init; }
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<string> ProofCore { get; set; } = [];
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<WorkerModelValue> Model { get; set; } = [];
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<WorkerAssumptionEvidence> Assumptions { get; set; } = [];
}

internal sealed record WorkerEffectViolationWitness
{
    [JsonRequired]
    public string Kind { get; init; } = string.Empty;
    [JsonRequired]
    public string Detail { get; init; } = string.Empty;
    [JsonRequired]
    public WorkerEffectSet Effects { get; init; }
    [JsonRequired]
    public WorkerEffectCapabilitySet Capabilities { get; init; }
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<string> ExactExceptionTypeHierarchy { get; set; } = [];
    [JsonRequired]
    public WorkerSourceLocation Location { get; init; } = new();
}

internal sealed record WorkerModelValue
{
    [JsonRequired]
    public string Variable { get; init; } = string.Empty;
    [JsonRequired]
    public string Kind { get; init; } = string.Empty;
    [JsonRequired]
    public string Value { get; init; } = string.Empty;
}

internal enum WorkerCacheStatus
{
    Unspecified = 0,
    Disabled = 1,
    Miss = 2,
    Hit = 3,
    Written = 4,
    Rejected = 5,
    Unavailable = 6
}

internal sealed record WorkerVersionSummary
{
    [JsonRequired]
    public string ProtocolVersion { get; init; } = WorkerProtocolVersions.Current;
    [JsonRequired]
    public int ManifestSchemaVersion { get; init; } = WorkerManifestVersions.Current;
    [JsonRequired]
    public int CacheSchemaVersion { get; init; } = WorkerCacheVersions.Current;
    [JsonRequired]
    public string WorkerVersion { get; init; } = string.Empty;
    [JsonRequired]
    public string ApiSpecVersion { get; init; } = string.Empty;
    [JsonRequired]
    public string WorkerBinarySha256 { get; init; } = WorkerProtocolVersions.EmptySha256;
    [JsonRequired]
    public string ApiSpecContentSha256 { get; init; } = WorkerProtocolVersions.EmptySha256;
}

internal sealed record WorkerVerificationSummary
{
    [JsonRequired]
    public bool CacheHit { get; init; }
    [JsonRequired]
    public WorkerCacheStatus CacheStatus { get; init; }
    [JsonRequired]
    public WorkerVersionSummary Versions { get; init; } = new();
    [JsonRequired]
    public WorkerBudgets Budgets { get; init; } = new();
    [JsonRequired]
    public long ElapsedMilliseconds { get; init; }
    // Measured solver work and time for each callable verified in this run.
    // Absent on cache hits, where no callable was verified.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkerCostReport? Costs { get; init; }
}

internal sealed record WorkerCostReport
{
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<WorkerCallableCost> Callables { get; set; } = [];
}

internal sealed record WorkerCallableCost
{
    [JsonRequired]
    public string CallableId { get; init; } = string.Empty;
    // Z3 resource units: deterministic for a fixed solver build and input.
    [JsonRequired]
    public long ConsumedRlimit { get; init; }
    [JsonRequired]
    public long ElapsedMilliseconds { get; init; }
}

internal sealed record WorkerProtocolError
{
    [JsonRequired]
    public string Code { get; init; } = string.Empty;
    [JsonRequired]
    public string Message { get; init; } = string.Empty;
}

internal sealed record WorkerVerifyResponse
{
    [JsonRequired]
    public string ProtocolVersion { get; init; } = WorkerProtocolVersions.Current;
    [JsonRequired]
    public string RequestHash { get; init; } = WorkerProtocolVersions.EmptySha256;
    [JsonRequired]
    public string InputHash { get; init; } = string.Empty;
    [JsonRequired]
    public WorkerClaimManifest Manifest { get; init; } = new();
    [JsonRequired]
    public WorkerRunStatus RunStatus { get; init; }
    [JsonRequired]
    public WorkerRunFailureReason FailureReason { get; init; }
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<WorkerCallableResult> CallableResults { get; set; } = [];
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<WorkerClaimResult> ClaimResults { get; set; } = [];
    [JsonRequired]
    public WorkerVerificationSummary Summary { get; init; } = new();
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<WorkerProtocolError> Errors { get; set; } = [];
}
