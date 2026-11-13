using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SharpProof.Worker.Protocol;

internal enum WorkerSelectedFeature
{
    Unspecified = 0,
    Effects = 1,
    Contracts = 2
}

internal enum WorkerSelectionReason
{
    Unspecified = 0,
    ExplicitAnnotation = 1,
    DiscoveredPostcondition = 2,
    DiscoveredCallPrecondition = 3
}

internal enum WorkerClaimKind
{
    Unspecified = 0,
    Postcondition = 1,
    Effect = 2,
    CallPrecondition = 3
}

internal enum WorkerClaimEvidence
{
    Unspecified = 0,
    DirectClause = 1,
    CompanionClause = 2,
    ReturnAttribute = 3,
    Attribute = 4,
    CallSite = 5
}

internal enum WorkerEffectContractKind
{
    Unspecified = 0,
    EnforcePure = 1,
    ZeroAllocations = 2,
    AllowedCapabilities = 3,
    DoesNotThrow = 4,
    AllowedExceptions = 5,
    EffectContract = 6,
    ResourceUpperBound = 7
}

internal enum WorkerResourceMetric
{
    Unspecified = 0,
    LoopBodyEntries = 1,
    ArrayAllocations = 2,
    AllocatedElements = 3,
    AllocatedBytes = 4,
    PeakAuxiliaryBytes = 5,
    RetainedBytes = 6,
    SourceActivationDepth = 7,
    SourceStackBytes = 8
}

internal static class WorkerResourceModels
{
    public const string CallableScope = "callable-source";
    public const string TaskJoinScope = "joined-isolated-source-callbacks";
    public const string TaskSourceAllocationScope = "joined-isolated-source-allocation-prefixes";
    public const string TaskStorageScope = "joined-isolated-normal-completion";
    public const string TaskRuntimeScope = "joined-isolated-cumulative-runtime-premises";
    public const string TaskRuntimeCostModel = "task-join-declared-runtime-allocation-prefixes-v1";
    public const string TaskStorageModel = "task-join-normal-owned-storage-v1";
    public const string ConfiguredTaskStorageModel = "configured-task-join-normal-owned-storage-v1";
    public const string TaskCompilerCarrierModel = "emitted-task-compiler-carriers-v1";
    public const string TaskJoinModel = "task-join-source-at-most-once-v1";
    public const string ConfiguredTaskJoinModel = "configured-task-join-source-at-most-once-v1";
    public const string CostModel = "loop-entries-and-primitive-array-elements-v3";
    public const int Expression = 1;
    public const int Program = 8;
    public const string StackCostModel = "source-activation-records-v1:word8:bookkeeping2";
    public const string AllocationLayout = "coreclr-8.0.32-x64-source-storage-v1";
    public const string AllocationCostModel = AllocationLayout + ":cumulative-bytes-v1";
    public const string LifetimeCostModel = AllocationLayout + ":finite-owned-strong-reachability-v1:normal-completion";
    public const int MaximumExpressionBytes = 1024 * 1024;
    public const string Asymptotic = "single-input-unbounded-expression-extension-v1";
    public const string NormalDomain = "unbounded-scalar-counter-extension-v1:ideal-primitive-allocation";
    public const string Accounting = "nonnegative-credit-potential-v1:unit-spend";
    public const string Expected = "entry-bernoulli-boolean-v1:conditional-marginal-upper";
    public const int MaximumAsymptoticDegree = 4096;
}

internal sealed record WorkerResourceConstraint
{
    [JsonRequired]
    public WorkerResourceMetric Metric { get; init; }
    [JsonRequired]
    public string CostModel { get; init; } = WorkerResourceModels.CostModel;
    [JsonRequired]
    public int ExpressionModel { get; init; } = WorkerResourceModels.Expression;
    [JsonRequired]
    public byte[] UpperBound { get; init; } = [];
}

internal sealed record WorkerResourceAsymptoticEvidence
{
    [JsonRequired]
    public string Model { get; init; } = WorkerResourceModels.Asymptotic;
    [JsonRequired]
    public string? UpperBound { get; init; }
    [JsonRequired]
    public int? PolynomialDegree { get; init; }
    [JsonRequired]
    public int? LogarithmicDegree { get; init; }
    [JsonRequired]
    public bool? IsZero { get; init; }
    [JsonRequired]
    public int? InputOrdinal { get; init; }
    [JsonRequired]
    public string? InputKind { get; init; }
    [JsonRequired]
    public string? InputName { get; init; }
    [JsonRequired]
    public string? UnknownReason { get; init; }
    [JsonRequired]
    public WorkerResourceLowerAsymptoticEvidence? Lower { get; init; }
}

internal sealed record WorkerResourceLowerAsymptoticEvidence
{
    [JsonRequired]
    public string Model { get; init; } = WorkerResourceModels.NormalDomain;
    [JsonRequired]
    public string? InferredBound { get; init; }
    [JsonRequired]
    public string? LowerBound { get; init; }
    [JsonRequired]
    public string? TightBound { get; init; }
    [JsonRequired]
    public string? TightUnknownReason { get; init; }
    [JsonRequired]
    public WorkerResourceAsymptoticEvidence? Growth { get; init; }
    [JsonRequired]
    public string? UnknownReason { get; init; }
}

internal sealed record WorkerResourceBoundEvidence
{
    [JsonRequired]
    public string ExecutionScope { get; init; } = WorkerResourceModels.CallableScope;
    [JsonRequired]
    public WorkerResourceMetric Metric { get; init; }
    [JsonRequired]
    public string CostModel { get; init; } = WorkerResourceModels.CostModel;
    [JsonRequired]
    public int ExpressionModel { get; init; } = WorkerResourceModels.Expression;
    [JsonRequired]
    public int ProgramModel { get; init; } = WorkerResourceModels.Program;
    [JsonRequired]
    public string DeclaredBoundSha256 { get; init; } = string.Empty;
    [JsonRequired]
    public string? InferredUpperBound { get; init; }
    [JsonRequired]
    public string? UnknownReason { get; init; }
    [JsonRequired]
    public string? ObservedLowerBound { get; init; }
    [JsonRequired]
    public string? EvaluatedUpperBound { get; init; }
    [JsonRequired]
    public WorkerResourceMetadataAssumption[] MetadataAssumptions { get; init; } = [];
    [JsonRequired]
    public WorkerResourceTaskRuntimePremiseEvidence? RuntimePremises { get; init; }
    [JsonRequired]
    public WorkerResourceAsymptoticEvidence? Asymptotic { get; init; }
    [JsonRequired]
    public WorkerResourceAccountingEvidence[] Accounting { get; init; } = [];
    [JsonRequired]
    public WorkerResourceExpectedEvidence? Expected { get; init; }
}

internal sealed record WorkerResourceExpectedEvidence
{
    [JsonRequired]
    public string Model { get; init; } = WorkerResourceModels.Expected;
    [JsonRequired]
    public int? InputOrdinal { get; init; }
    [JsonRequired]
    public string? InputName { get; init; }
    [JsonRequired]
    public int? TrueWeight { get; init; }
    [JsonRequired]
    public int? FalseWeight { get; init; }
    [JsonRequired]
    public string? FalseUpperBound { get; init; }
    [JsonRequired]
    public string? TrueUpperBound { get; init; }
    [JsonRequired]
    public string? UpperBound { get; init; }
    [JsonRequired]
    public string? UnknownReason { get; init; }
}

internal sealed record WorkerResourceAccountingEvidence
{
    [JsonRequired]
    public string Model { get; init; } = WorkerResourceModels.Accounting;
    [JsonRequired]
    public string InitialCredit { get; init; } = string.Empty;
    [JsonRequired]
    public string DepositPerIteration { get; init; } = string.Empty;
    [JsonRequired]
    public int CounterWidth { get; init; }
    [JsonRequired]
    public bool CheckedDeposit { get; init; }
    [JsonRequired]
    public string MaximumSpend { get; init; } = string.Empty;
}

internal sealed record WorkerResourceMetadataAssumption
{
    [JsonRequired]
    public string AssumptionId { get; init; } = string.Empty;
    [JsonRequired]
    public string WitnessIdentifier { get; init; } = string.Empty;
    [JsonRequired]
    public string CatalogSha256 { get; init; } = string.Empty;
    [JsonRequired]
    public string ReferenceAssembly { get; init; } = string.Empty;
    [JsonRequired]
    public string ReferenceSha256 { get; init; } = string.Empty;
    [JsonRequired]
    public string RuntimeModel { get; init; } = string.Empty;
    [JsonRequired]
    public string EvidenceSource { get; init; } = string.Empty;
}
internal sealed record WorkerSourceLocation
{
    [JsonRequired]
    public string Path { get; init; } = string.Empty;
    [JsonRequired]
    public int Start { get; init; }
    [JsonRequired]
    public int Length { get; init; }
    [JsonRequired]
    public int Line { get; init; }
    [JsonRequired]
    public int Column { get; init; }
}

// The callee clause a call-precondition claim checks. The claim's own location
// is the call. A clause in source has a ClauseLocation; a closed attribute read
// from another assembly's metadata has a MetadataClause instead. A clause
// another project of the same build exported has a ClauseLocation in that
// project and names it in ExportedBy.
internal sealed record WorkerCallPreconditionTarget
{
    [JsonRequired]
    public string Callee { get; init; } = string.Empty;
    [JsonRequired]
    public int ClauseOrdinal { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkerSourceLocation? ClauseLocation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkerMetadataClauseLocation? MetadataClause { get; init; }
    // The calling project declared this clause, in a companion, for a callee
    // in another assembly; the callee's own binary does not carry it.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CallerDeclared { get; init; }
    // The simple name of the referenced project whose contract export
    // supplied this clause; its ClauseLocation is in that project's source.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExportedBy { get; init; }

    // Where the clause is declared, for a message.
    internal string DescribeClause(Func<WorkerSourceLocation, string> source)
    {
        source = RepositoryParameterGuard.NotNull(source, nameof(source));
        var declared = ClauseLocation is { } location ? "at " + source(location)
            : MetadataClause is { } metadata ? metadata.Describe() : string.Empty;
        return CallerDeclared ? declared + ", declared by the calling project"
            : ExportedBy is { } exporter ? declared + ", exported by " + exporter : declared;
    }

    // The callee without its assembly identity, which carries a version.
    internal static string StableCallee(string callee)
    {
        callee = RepositoryParameterGuard.NotNull(callee, nameof(callee));
        var separator = callee.LastIndexOf("::", StringComparison.Ordinal);
        return separator < 0 ? callee : callee.Substring(separator + 2);
    }
}

// A closed parameter attribute of a callee in another assembly. Metadata has
// no source span, so the clause is named by its assembly, its member, the
// parameter it guards and the attribute itself.
internal sealed record WorkerMetadataClauseLocation
{
    [JsonRequired]
    public string Assembly { get; init; } = string.Empty;
    [JsonRequired]
    public string Member { get; init; } = string.Empty;
    [JsonRequired]
    public int Parameter { get; init; }
    [JsonRequired]
    public string Attribute { get; init; } = string.Empty;

    // The assembly by simple name: its identity carries a version.
    internal string Describe()
    {
        var comma = Assembly.IndexOf(',');
        var name = comma < 0 ? Assembly : Assembly.Substring(0, comma);
        return FormattableString.Invariant($"[{Attribute}] on parameter {Parameter} in {name}");
    }
}

internal sealed record WorkerCallableManifestEntry
{
    [JsonRequired]
    public string CallableId { get; init; } = string.Empty;
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<WorkerSelectedFeature> SelectedFeatures { get; set; } = [];
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<WorkerSelectionReason> SelectionReasons { get; set; } = [];
    [JsonRequired]
    public WorkerSourceLocation Location { get; init; } = new();
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<string> ClaimIds { get; set; } = [];
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<WorkerAssumptionEvidence> Assumptions { get; set; } = [];
}

internal sealed record WorkerClaimManifestEntry
{
    [JsonRequired]
    public string ClaimId { get; init; } = string.Empty;
    [JsonRequired]
    public string CallableId { get; init; } = string.Empty;
    [JsonRequired]
    public int Ordinal { get; init; }
    [JsonRequired]
    public WorkerClaimKind Kind { get; init; }
    [JsonRequired]
    public WorkerClaimEvidence Evidence { get; init; }
    [JsonRequired]
    public WorkerEffectContractKind EffectContractKind { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkerResourceConstraint? ResourceUpperBound { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkerCallPreconditionTarget? CallPrecondition { get; init; }
    [JsonRequired]
    public WorkerSourceLocation Location { get; init; } = new();
}

internal sealed record WorkerClaimManifest
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = WorkerManifestVersions.Current;
    // Source calls whose callee declares a precondition the compiler can see
    // and that no claim checks. Contract.Requires in another assembly is not
    // in its binary, so calls to it are not counted.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int UncheckedCallSites { get; init; }
    [JsonRequired]
    public string Hash { get; set; } = string.Empty;
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<WorkerCallableManifestEntry> Callables { get; set; } = [];
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<WorkerClaimManifestEntry> Claims { get; set; } = [];
}
