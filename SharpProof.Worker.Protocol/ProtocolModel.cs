using System.Collections.Immutable;

namespace SharpProof.Worker.Protocol;
public static class WorkerProtocolVersions {
    public const string Current = "5";
    public const string EmptySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
}
public static class WorkerCacheVersions { public const int Current = 5; }
public static class WorkerManifestVersions { public const int Current = 2; }
public static class WorkerLauncherDefaults { public const int TerminationGraceMilliseconds = 1_000; }
public sealed class WorkerVerifyRequest {
    public string ProtocolVersion { get; set; } = WorkerProtocolVersions.Current;
    public WorkerFileReference CompilerManifest { get; set; } = new();
    public WorkerBudgets Budgets { get; set; } = new(); public WorkerCacheOptions Cache { get; set; } = new();
    public WorkerVerifyPolicy VerifyPolicy { get; set; } = WorkerVerifyPolicy.Advisory; public WorkerAssumptionPolicy AssumptionPolicy { get; set; } = WorkerAssumptionPolicy.Allow;
}

public sealed class WorkerFileReference { public string Path { get; set; } = string.Empty; public string Sha256 { get; set; } = string.Empty; }
public enum WorkerFeatureSet { Unspecified, Effects, Contracts, All }
public enum WorkerVerifyPolicy { Unspecified, Advisory, WarnOnUnknown, RequireProven }
public enum WorkerAssumptionPolicy { Unspecified, Allow, Warn, Error }
public sealed class WorkerBudgets {
    public const int MaximumParallelism = 4; public const uint DefaultQueryRlimit = 3_000_000;
    public const uint DefaultMethodRlimit = 20_000_000; public const int DefaultMethodWallTimeMilliseconds = 10_000;
    public const int DefaultProjectWallTimeMilliseconds = 300_000; public const int DefaultMaximumExpressionDepth = 64;
    public const long DefaultProcessMemoryLimitBytes = 2L * 1024 * 1024 * 1024;

    public uint QueryRlimit { get; set; } = DefaultQueryRlimit; public uint MethodRlimit { get; set; } = DefaultMethodRlimit;
    public int MethodWallTimeMilliseconds { get; set; } = DefaultMethodWallTimeMilliseconds; public int ProjectWallTimeMilliseconds { get; set; } = DefaultProjectWallTimeMilliseconds;
    public int MaxParallelism { get; set; } = MaximumParallelism; public int MaximumExpressionDepth { get; set; } = DefaultMaximumExpressionDepth;
    public long ProcessMemoryLimitBytes { get; set; } = DefaultProcessMemoryLimitBytes; public int MaxWorkerProcesses { get; set; } = MaximumParallelism;
}
public sealed class WorkerCacheOptions {
    public const long DefaultMaximumBytes = 512L * 1024 * 1024;
    public bool Enabled { get; set; } = true; public string? Directory { get; set; }
    public long MaximumBytes { get; set; } = DefaultMaximumBytes;
}

public enum WorkerSelectedFeature { Unspecified, Effects, Contracts }
public enum WorkerSelectionReason { Unspecified, ExplicitAnnotation, DiscoveredPostcondition }
public enum WorkerClaimKind { Unspecified, Postcondition }
public enum WorkerClaimEvidence { Unspecified, DirectClause, CompanionClause, ReturnAttribute }
public sealed class WorkerSourceLocation {
    public string Path { get; set; } = string.Empty; public int Start { get; set; }
    public int Length { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
}
public sealed class WorkerCallableManifestEntry {
    public string CallableId { get; set; } = string.Empty;
    public WorkerSelectedFeature[] SelectedFeatures { get; set; } = []; public WorkerSelectionReason[] SelectionReasons { get; set; } = [];
    public WorkerSourceLocation Location { get; set; } = new(); public string[] ClaimIds { get; set; } = [];
    public WorkerAssumptionEvidence[] Assumptions { get; set; } = [];
}
public sealed class WorkerClaimManifestEntry {
    public string ClaimId { get; set; } = string.Empty; public string CallableId { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public WorkerClaimKind Kind { get; set; }
    public WorkerClaimEvidence Evidence { get; set; }
    public WorkerSourceLocation Location { get; set; } = new();
}
public sealed class WorkerClaimManifest {
    public int SchemaVersion { get; set; } = WorkerManifestVersions.Current; public string Hash { get; set; } = string.Empty;
    public WorkerCallableManifestEntry[] Callables { get; set; } = []; public WorkerClaimManifestEntry[] Claims { get; set; } = [];
}

public enum WorkerRunStatus { Unspecified, Complete, TimedOut, Canceled, Failed }
public enum WorkerRunFailureReason {
    Unspecified, None, InvalidRequest, InputUnavailable, CompilationFailure,
    CompilerManifestMismatch, BackendUnavailable, InfrastructureFailure, MalformedResult,
    CounterexampleReplayFailed, ContainmentFailure
}
public enum WorkerCallableCoverage { Unspecified, Complete, Incomplete }
public enum WorkerCallableCoverageReason {
    Unspecified, None, UnsupportedCallable, UnsupportedContract, SemanticUnknown,
    MissingClaimResult, MethodTimeout, ProjectTimeout, Canceled, InfrastructureFailure
}
public enum WorkerClaimOutcome { Unspecified, Proven, Refuted, Unknown }
public enum WorkerClaimReason {
    Unspecified, None, UnsupportedCallable, UnsupportedContract, UnsupportedBody,
    UnsupportedExpression, DeepPostcondition, MissingReturnValue, ResourceLimit,
    MethodTimeout, ProjectTimeout, Canceled, BackendUnavailable, InfrastructureFailure,
    MalformedBackendResult, CounterexampleReplayFailed
}
public enum WorkerAssumptionKind {
    Unspecified, Precondition, UserAssume, TrustedBoundary, ApiSpecification,
    SourceDomain, NormalCompletion
}
public sealed class WorkerAssumptionEvidence {
    public string Id { get; set; } = string.Empty; public WorkerAssumptionKind Kind { get; set; }
    public bool Used { get; set; }
}
public sealed class WorkerCallableResult {
    public string CallableId { get; set; } = string.Empty; public WorkerCallableCoverage Coverage { get; set; }
    public WorkerCallableCoverageReason Reason { get; set; }
    public WorkerAssumptionEvidence[] Assumptions { get; set; } = [];
}
public sealed class WorkerClaimResult {
    public string ClaimId { get; set; } = string.Empty; public WorkerClaimOutcome Outcome { get; set; }
    public WorkerClaimReason Reason { get; set; }
    public string[] ProofCore { get; set; } = [];
    public WorkerModelValue[] Model { get; set; } = [];
    public WorkerAssumptionEvidence[] Assumptions { get; set; } = [];
}
public sealed class WorkerModelValue {
    public string Variable { get; set; } = string.Empty; public string Kind { get; set; } = string.Empty; public string Value { get; set; } = string.Empty;
}
public sealed class WorkerClaimOutcomeCount {
    public WorkerClaimOutcome Outcome { get; set; }
    public int Count { get; set; }
}
public sealed class WorkerClaimReasonCount {
    public WorkerClaimReason Reason { get; set; }
    public int Count { get; set; }
}
public sealed class WorkerAssumptionSummary {
    public int Total { get; set; }
    public int Used { get; set; }
    public int User { get; set; }
    public int Trusted { get; set; }
}

public enum WorkerCacheStatus { Unspecified, Disabled, Miss, Hit, Written, Rejected, Unavailable }
public sealed class WorkerVersionSummary {
    public string ProtocolVersion { get; set; } = WorkerProtocolVersions.Current; public int ManifestSchemaVersion { get; set; } = WorkerManifestVersions.Current;
    public int CacheSchemaVersion { get; set; } = WorkerCacheVersions.Current;
    public string WorkerVersion { get; set; } = string.Empty; public string ApiSpecVersion { get; set; } = string.Empty;
}
public sealed class WorkerVerificationSummary {
    public int CallableCount { get; set; }
    public int ClaimCount { get; set; }
    public WorkerClaimOutcomeCount[] OutcomeCounts { get; set; } = []; public WorkerClaimReasonCount[] ReasonCounts { get; set; } = [];
    public WorkerAssumptionSummary Assumptions { get; set; } = new();
    public bool CacheHit { get; set; }
    public WorkerCacheStatus CacheStatus { get; set; }
    public WorkerVersionSummary Versions { get; set; } = new(); public WorkerBudgets Budgets { get; set; } = new();
    public long ElapsedMilliseconds { get; set; }
}
public sealed class WorkerProtocolError {
    public string Code { get; set; } = string.Empty; public string Message { get; set; } = string.Empty;
}
public sealed class WorkerVerifyResponse {
    public string ProtocolVersion { get; set; } = WorkerProtocolVersions.Current;
    public string RequestHash { get; set; } = WorkerProtocolVersions.EmptySha256;
    public string InputHash { get; set; } = string.Empty;
    public WorkerClaimManifest Manifest { get; set; } = new();
    public WorkerRunStatus RunStatus { get; set; }
    public WorkerRunFailureReason FailureReason { get; set; }
    public WorkerCallableResult[] CallableResults { get; set; } = []; public WorkerClaimResult[] ClaimResults { get; set; } = [];
    public WorkerVerificationSummary Summary { get; set; } = new();
    public WorkerProtocolError[] Errors { get; set; } = [];
}
public sealed class WorkerProtocolValidationResult {
    internal WorkerProtocolValidationResult(IEnumerable<WorkerProtocolError> errors) => Errors = [.. errors];
    public ImmutableArray<WorkerProtocolError> Errors { get; }
    public bool IsValid => Errors.IsDefaultOrEmpty;
}
