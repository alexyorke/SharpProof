using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SharpProof.Worker.Protocol;

internal static class WorkerProtocolVersions
{
    public const string Current = "49";
    public const string EmptySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
}

internal static class WorkerCacheVersions
{
    public const int Current = 72;
}

internal static class WorkerManifestVersions
{
    public const int Current = 9;
}

internal static class WorkerLauncherDefaults
{
    public const int TerminationGraceMilliseconds = 1000;
    public const int MaximumTerminationGraceMilliseconds = 300000;
    // Result publication must remain bounded after the run token is canceled.
    public const int ResultPublicationTimeoutMilliseconds = 30_000;
}

internal sealed record WorkerVerifyRequest
{
    [JsonRequired]
    public string ProtocolVersion { get; init; } = WorkerProtocolVersions.Current;
    [JsonRequired]
    public WorkerFileReference CompilerManifest { get; init; } = new();
    [JsonRequired]
    public WorkerBudgets Budgets { get; init; } = new();
    [JsonRequired]
    public WorkerCacheOptions Cache { get; init; } = new();
    [JsonRequired]
    public WorkerVerifyPolicy VerifyPolicy { get; init; } = WorkerVerifyPolicy.Advisory;
    [JsonRequired]
    public WorkerAssumptionPolicy AssumptionPolicy { get; init; } = WorkerAssumptionPolicy.Allow;
}

internal sealed record WorkerFileReference
{
    [JsonRequired]
    public string Path { get; init; } = string.Empty;
    [JsonRequired]
    public string Sha256 { get; init; } = string.Empty;
}

internal enum WorkerFeatureSet
{
    Unspecified = 0,
    Effects = 1,
    Contracts = 2,
    All = 3
}

internal enum WorkerVerifyPolicy
{
    Unspecified = 0,
    Advisory = 1,
    WarnOnUnknown = 2,
    RequireProven = 3
}

internal enum WorkerAssumptionPolicy
{
    Unspecified = 0,
    Allow = 1,
    Warn = 2,
    Error = 3
}

internal sealed class WorkerBudgets
{
    public const int MaximumParallelism = 4;
    public const uint DefaultQueryRlimit = SharpProof.Configuration.SolverDefaults.QueryRlimit;
    public const uint DefaultMethodRlimit = 20000000U;
    public const int DefaultMethodWallTimeMilliseconds = 10000;
    public const int DefaultProjectWallTimeMilliseconds = 300000;
    public const int DefaultMaximumExpressionDepth = 64;
    public const int MaximumAllowedExpressionDepth = 256;
    [JsonRequired]
    public uint QueryRlimit { get; set; } = DefaultQueryRlimit;
    [JsonRequired]
    public uint MethodRlimit { get; set; } = DefaultMethodRlimit;
    [JsonRequired]
    public int MethodWallTimeMilliseconds { get; set; } = DefaultMethodWallTimeMilliseconds;
    [JsonRequired]
    public int ProjectWallTimeMilliseconds { get; set; } = DefaultProjectWallTimeMilliseconds;
    [JsonRequired]
    public int MaxParallelism { get; set; } = MaximumParallelism;
    [JsonRequired]
    public int MaximumExpressionDepth { get; set; } = DefaultMaximumExpressionDepth;
}

internal sealed record WorkerCacheOptions
{
    public const bool DefaultEnabled = true;
    public const long DefaultMaximumBytes = 536870912L;
    [JsonRequired]
    public bool Enabled { get; init; } = DefaultEnabled;
    [JsonRequired]
    public string? Directory { get; init; }
    [JsonRequired]
    public long MaximumBytes { get; init; } = DefaultMaximumBytes;
}
