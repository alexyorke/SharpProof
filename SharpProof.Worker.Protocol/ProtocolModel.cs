using System.Collections.Immutable;

namespace SharpProof.Worker.Protocol;

public static class WorkerProtocolVersions {
    public const string Current = "2";
}

public static class WorkerCacheVersions {
    public const int Current = 2;
}

public static class WorkerLauncherDefaults {
    public const int TerminationGraceMilliseconds = 1_000;
}

public sealed class WorkerVerifyRequest {
    public string ProtocolVersion { get; set; } =
        WorkerProtocolVersions.Current;
    public string ProjectDirectory { get; set; } = string.Empty;
    public string AssemblyName { get; set; } = "SharpProof.VerifiedProject";
    public string[] SourceFiles { get; set; } = [];
    public string[] ReferenceAssemblies { get; set; } = [];
    public string[] DefineConstants { get; set; } = [];
    public WorkerCompilationOptions Compilation { get; set; } = new();
    public WorkerBudgets Budgets { get; set; } = new();
    public WorkerCacheOptions Cache { get; set; } = new();
}

public enum WorkerNullableContext {
    Unspecified,
    Disabled,
    Warnings,
    Annotations,
    Enabled
}

public enum WorkerOptimizationLevel {
    Unspecified,
    Debug,
    Release
}

public enum WorkerOutputKind {
    Unspecified,
    ConsoleApplication,
    WindowsApplication,
    DynamicallyLinkedLibrary,
    NetModule,
    WindowsRuntimeMetadata,
    WindowsRuntimeApplication
}

public enum WorkerPlatform {
    Unspecified,
    AnyCpu,
    AnyCpu32BitPreferred,
    X86,
    X64,
    Arm,
    Arm64,
    Itanium
}

public sealed class WorkerCompilationOptions {
    public string TargetFramework { get; set; } = string.Empty;
    public string LanguageVersion { get; set; } = string.Empty;
    public WorkerNullableContext NullableContext { get; set; }
    public WorkerOptimizationLevel Optimization { get; set; }
    public bool? CheckOverflow { get; set; }
    public bool? AllowUnsafe { get; set; }
    public bool? Deterministic { get; set; }
    public WorkerOutputKind OutputKind { get; set; }
    public WorkerPlatform Platform { get; set; }
}

public sealed class WorkerBudgets {
    public const int MaximumParallelism = 4;
    public const uint DefaultQueryRlimit = 3_000_000;
    public const uint DefaultMethodRlimit = 20_000_000;
    public const int DefaultMethodWallTimeMilliseconds = 10_000;
    public const int DefaultProjectWallTimeMilliseconds = 300_000;
    public const int DefaultMaximumExpressionDepth = 64;
    public const long DefaultProcessMemoryLimitBytes = 2L * 1024 * 1024 * 1024;

    public uint QueryRlimit { get; set; } = DefaultQueryRlimit;
    public uint MethodRlimit { get; set; } = DefaultMethodRlimit;
    public int MethodWallTimeMilliseconds { get; set; } =
        DefaultMethodWallTimeMilliseconds;
    public int ProjectWallTimeMilliseconds { get; set; } =
        DefaultProjectWallTimeMilliseconds;
    public int MaxParallelism { get; set; } = MaximumParallelism;
    public int MaximumExpressionDepth { get; set; } =
        DefaultMaximumExpressionDepth;
    public long ProcessMemoryLimitBytes { get; set; } =
        DefaultProcessMemoryLimitBytes;
    public int MaxWorkerProcesses { get; set; } = MaximumParallelism;
}

public sealed class WorkerCacheOptions {
    public const long DefaultMaximumBytes = 512L * 1024 * 1024;

    public bool Enabled { get; set; } = true;
    public string? Directory { get; set; }
    public long MaximumBytes { get; set; } = DefaultMaximumBytes;
}

public enum WorkerVerificationStatus {
    Proven,
    Refuted,
    Unknown
}

public enum WorkerVerificationReason {
    None,
    UnsupportedCallable,
    UnsupportedContract,
    UnsupportedBody,
    UnsupportedExpression,
    DeepEnsures,
    MissingReturnValue,
    ResourceLimit,
    MethodTimeout,
    ProjectTimeout,
    BackendUnavailable,
    InfrastructureFailure,
    MalformedBackendResult,
    CounterexampleReplayFailed
}

public sealed class WorkerVerificationRecord {
    public string CallableId { get; set; } = string.Empty;
    public int ContractOrdinal { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public int SourceStart { get; set; }
    public WorkerVerificationStatus Status { get; set; }
    public WorkerVerificationReason Reason { get; set; }
    public string[] ProofCore { get; set; } = [];
    public WorkerModelValue[] Model { get; set; } = [];
}

public sealed class WorkerModelValue {
    public string Variable { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class WorkerProtocolError {
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class WorkerVerifyResponse {
    public string ProtocolVersion { get; set; } =
        WorkerProtocolVersions.Current;
    public string InputHash { get; set; } = string.Empty;
    public WorkerVerificationRecord[] Records { get; set; } = [];
    public WorkerProtocolError[] Errors { get; set; } = [];
}

public sealed class WorkerProtocolValidationResult {
    internal WorkerProtocolValidationResult(
        IEnumerable<WorkerProtocolError> errors) =>
        Errors = [.. errors];

    public ImmutableArray<WorkerProtocolError> Errors { get; }
    public bool IsValid => Errors.IsDefaultOrEmpty;
}
