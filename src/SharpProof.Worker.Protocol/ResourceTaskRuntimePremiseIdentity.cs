using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace SharpProof.Worker.Protocol;

internal enum WorkerResourceTaskDeploymentProfile
{
    Unspecified = 0,
    CoreClr8032LinuxX64OrdinaryJitStockClosed = 1
}

internal enum WorkerResourceTaskRuntimeQuantity
{
    Unspecified = 0,
    RawTraceArrayAttemptsPerFaultEpisode = 1,
    RawTracePayloadBytesPerArrayAttempt = 2,
    ResolverArrayAttemptsPerFaultEpisode = 3,
    ResolverSlotsPerArrayAttempt = 4,
    FaultConstructionBytesPerFaultEpisode = 5,
    ResolverReferentBytesPerFaultEpisode = 6,
    SchedulingBytesPerChildStartAttempt = 7,
    JoinBytesPerJoinAttempt = 8,
    AwaitBytesPerConfiguredAwait = 9,
    InitializationBytesPerInvocation = 10,
    TotalFaultEpisodesPerInvocation = 11
}

internal sealed record WorkerResourceTaskRuntimeDeclaration(
    WorkerResourceTaskRuntimeQuantity Quantity, long UpperBound, string Evidence)
{
    public string Evidence { get; init; } = RepositoryParameterGuard.NotNull(Evidence, nameof(Evidence));
}

internal sealed record WorkerResourceTaskRuntimeDeclarations(string RootCallableId,
    WorkerResourceTaskDeploymentProfile Profile, string DeploymentManifestSha256, string Evidence,
    ImmutableArray<WorkerResourceTaskRuntimeDeclaration> Quantities)
{
    public string RootCallableId { get; init; } = RepositoryParameterGuard.NotNull(RootCallableId, nameof(RootCallableId));
    public string DeploymentManifestSha256 { get; init; } = RepositoryParameterGuard.NotNull(DeploymentManifestSha256, nameof(DeploymentManifestSha256));
    public string Evidence { get; init; } = RepositoryParameterGuard.NotNull(Evidence, nameof(Evidence));
    public ImmutableArray<WorkerResourceTaskRuntimeDeclaration> Quantities { get; init; } = Quantities.IsDefault
        ? throw new ArgumentException("Quantities must be initialized.", nameof(Quantities)) : Quantities;
}

// These are caller declarations, not facts inferred from a compiler reference
// pack or observed on the machine executing the worker. Values have no defaults.
internal static class ResourceTaskRuntimePremiseIdentity
{
    internal const string Model = "declared-task-runtime-premises-v1";
    internal const string DeploymentProfileModel = "coreclr-8.0.32-linux-x64-ordinary-jit-stock-closed-v1";
    internal const int MaximumEvidenceLength = 1024;
    internal const int MaximumRootCallableIdLength = 16384;
    private const string ProfileCatalog = Model + "\n" + DeploymentProfileModel + "\n" +
        "CoreCLR;8.0.32;Linux;x64;ordinary-JIT-all-tiers;stock-unmodified-helper-bindings;" +
        "no-ReadyToRun;no-NativeAOT;unchanged-emitted-module;default-scheduler;" +
        "default-or-suppressed-execution-context;no-external-callbacks;no-instrumentation;" +
        "invariant-culture;stock-embedded-resources;closed-nondynamic-noncollectible-loader;" +
        "closed-task-and-holder-lifecycle-including-later-internal-finalization;no-external-mutation;no-external-reregistration;no-external-resurrection;" +
        "internal-stock-task-holder-loader-retries-included-in-declared-budgets\n" +
        "outermost-fault-episode-includes-existing-exception-processing-rethrow-dispatch-capture-restore-record-and-notification;" +
        "all-nested-construction-trace-referent-work-and-attempts-inside-entry;reentry-after-exit-is-new-episode\n" +
        "compiler-source-fixed-allocation-sites-accounted-separately-and-excluded-from-runtime-domains;" +
        "remaining-allocation-classification:initialization,trace-arrays,resolver-referents,fault-construction,remaining-control\n" +
        "counts-include-failed-abandoned-losing-and-retried-allocation-attempts;all-eleven-quantities-required\n" +
        "1:allocation-attempts:per-outermost-fault-episode:all-raw-trace-affinity-restore-dispatch-arrays\n" +
        "2:payload-bytes:per-raw-trace-array-allocation-attempt\n" +
        "3:allocation-attempts:per-outermost-fault-episode:all-resolver-affinity-restore-dispatch-arrays\n" +
        "4:reference-slots:per-resolver-array-allocation-attempt\n" +
        "5:managed-instance-bytes:per-outermost-fault-episode:transitive-fault-construction-messages-nested-replacement-retries\n" +
        "6:managed-instance-bytes:per-outermost-fault-episode:fresh-loader-resolver-referents-and-losing-candidates\n" +
        "7:managed-instance-bytes:per-child-start-attempt:remaining-transitive-scheduler-queue-worker-entire-causal-lifecycle-builder-recording-notification-control\n" +
        "8:managed-instance-bytes:per-join-attempt:remaining-transitive-join-entire-causal-lifecycle-builder-recording-notification-control\n" +
        "9:managed-instance-bytes:per-configured-await:remaining-transitive-await-entire-causal-lifecycle-builder-recording-notification-control\n" +
        "10:managed-instance-bytes:per-root-invocation:all-causally-reached-transitive-cold-initialization-and-retries\n" +
        "11:fault-episodes:per-root-invocation:all-source-helper-runtime-scheduler-initialization-and-existing-exception-processing-entries\n";

    internal static string ProfileCatalogSha256 { get; } = Hash(ProfileCatalog);
    internal static ImmutableArray<WorkerResourceTaskRuntimeQuantity> RequiredQuantities { get; } =
    [
        WorkerResourceTaskRuntimeQuantity.RawTraceArrayAttemptsPerFaultEpisode,
        WorkerResourceTaskRuntimeQuantity.RawTracePayloadBytesPerArrayAttempt,
        WorkerResourceTaskRuntimeQuantity.ResolverArrayAttemptsPerFaultEpisode,
        WorkerResourceTaskRuntimeQuantity.ResolverSlotsPerArrayAttempt,
        WorkerResourceTaskRuntimeQuantity.FaultConstructionBytesPerFaultEpisode,
        WorkerResourceTaskRuntimeQuantity.ResolverReferentBytesPerFaultEpisode,
        WorkerResourceTaskRuntimeQuantity.SchedulingBytesPerChildStartAttempt,
        WorkerResourceTaskRuntimeQuantity.JoinBytesPerJoinAttempt,
        WorkerResourceTaskRuntimeQuantity.AwaitBytesPerConfiguredAwait,
        WorkerResourceTaskRuntimeQuantity.InitializationBytesPerInvocation,
        WorkerResourceTaskRuntimeQuantity.TotalFaultEpisodesPerInvocation
    ];

    internal static string Unit(WorkerResourceTaskRuntimeQuantity quantity)
    {
        return quantity switch
        {
            WorkerResourceTaskRuntimeQuantity.RawTraceArrayAttemptsPerFaultEpisode or
                WorkerResourceTaskRuntimeQuantity.ResolverArrayAttemptsPerFaultEpisode => "allocation-attempts",
            WorkerResourceTaskRuntimeQuantity.RawTracePayloadBytesPerArrayAttempt => "payload-bytes",
            WorkerResourceTaskRuntimeQuantity.ResolverSlotsPerArrayAttempt => "reference-slots",
            WorkerResourceTaskRuntimeQuantity.FaultConstructionBytesPerFaultEpisode or
                WorkerResourceTaskRuntimeQuantity.ResolverReferentBytesPerFaultEpisode or
                WorkerResourceTaskRuntimeQuantity.SchedulingBytesPerChildStartAttempt or
                WorkerResourceTaskRuntimeQuantity.JoinBytesPerJoinAttempt or
                WorkerResourceTaskRuntimeQuantity.AwaitBytesPerConfiguredAwait or
                WorkerResourceTaskRuntimeQuantity.InitializationBytesPerInvocation => "managed-instance-bytes",
            WorkerResourceTaskRuntimeQuantity.TotalFaultEpisodesPerInvocation => "fault-episodes",
            _ => throw new ArgumentOutOfRangeException(nameof(quantity))
        };
    }

    internal static string Scope(WorkerResourceTaskRuntimeQuantity quantity)
    {
        return quantity switch
        {
            WorkerResourceTaskRuntimeQuantity.RawTraceArrayAttemptsPerFaultEpisode => "per-outermost-fault-episode:all-raw-trace-affinity-restore-dispatch-arrays",
            WorkerResourceTaskRuntimeQuantity.RawTracePayloadBytesPerArrayAttempt => "per-raw-trace-array-allocation-attempt",
            WorkerResourceTaskRuntimeQuantity.ResolverArrayAttemptsPerFaultEpisode => "per-outermost-fault-episode:all-resolver-affinity-restore-dispatch-arrays",
            WorkerResourceTaskRuntimeQuantity.ResolverSlotsPerArrayAttempt => "per-resolver-array-allocation-attempt",
            WorkerResourceTaskRuntimeQuantity.FaultConstructionBytesPerFaultEpisode => "per-outermost-fault-episode:transitive-fault-construction-messages-nested-replacement-retries",
            WorkerResourceTaskRuntimeQuantity.ResolverReferentBytesPerFaultEpisode => "per-outermost-fault-episode:fresh-loader-resolver-referents-and-losing-candidates",
            WorkerResourceTaskRuntimeQuantity.SchedulingBytesPerChildStartAttempt => "per-child-start-attempt:remaining-transitive-scheduler-queue-worker-entire-causal-lifecycle-builder-recording-notification-control",
            WorkerResourceTaskRuntimeQuantity.JoinBytesPerJoinAttempt => "per-join-attempt:remaining-transitive-join-entire-causal-lifecycle-builder-recording-notification-control",
            WorkerResourceTaskRuntimeQuantity.AwaitBytesPerConfiguredAwait => "per-configured-await:remaining-transitive-await-entire-causal-lifecycle-builder-recording-notification-control",
            WorkerResourceTaskRuntimeQuantity.InitializationBytesPerInvocation => "per-root-invocation:all-causally-reached-transitive-cold-initialization-and-retries",
            WorkerResourceTaskRuntimeQuantity.TotalFaultEpisodesPerInvocation => "per-root-invocation:all-source-helper-runtime-scheduler-initialization-and-existing-exception-processing-entries",
            _ => throw new ArgumentOutOfRangeException(nameof(quantity))
        };
    }

    internal static string? Validate(WorkerResourceTaskRuntimeDeclarations declarations)
    {
        declarations = ArgumentNullGuard.NotNull(declarations, nameof(declarations));
        if (!IsRootCallableId(declarations.RootCallableId) ||
            declarations.Profile != WorkerResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed ||
            !ProtocolHashEncoding.IsSha256(declarations.DeploymentManifestSha256) || !IsEvidence(declarations.Evidence))
        { return "TaskRuntimePremisesInvalid"; }
        if (declarations.Quantities.IsDefault || declarations.Quantities.Length != RequiredQuantities.Length)
        { return "TaskRuntimePremisesIncomplete"; }
        for (var index = 0; index < RequiredQuantities.Length; index++)
        {
            var declaration = declarations.Quantities[index];
            if (declaration is null || declaration.Quantity != RequiredQuantities[index] || declaration.UpperBound < 0 ||
                !IsEvidence(declaration.Evidence))
            { return "TaskRuntimePremisesInvalid"; }
        }
        return null;
    }

    internal static string DeclarationSha256(WorkerResourceTaskRuntimeDeclarations declarations)
    {
        declarations = ArgumentNullGuard.NotNull(declarations, nameof(declarations));
        var canonical = Base(declarations);
        foreach (var declaration in declarations.Quantities)
        { AppendDeclaration(canonical, declaration); }
        return Hash(canonical.ToString());
    }

    internal static string DeploymentAssumptionId(WorkerResourceTaskRuntimeDeclarations declarations)
    {
        declarations = ArgumentNullGuard.NotNull(declarations, nameof(declarations));
        return "resource-runtime-deployment-v1:" + Hash(Base(declarations).ToString());
    }

    internal static string QuantityAssumptionId(WorkerResourceTaskRuntimeDeclarations declarations,
        WorkerResourceTaskRuntimeDeclaration declaration)
    {
        declarations = ArgumentNullGuard.NotNull(declarations, nameof(declarations));
        declaration = ArgumentNullGuard.NotNull(declaration, nameof(declaration));
        var canonical = Base(declarations);
        if (!declarations.Quantities.Contains(declaration))
        { throw new ArgumentException("The quantity is not owned by these declarations.", nameof(declaration)); }
        AppendDeclaration(canonical, declaration);
        return "resource-runtime-quantity-v1:" + Hash(canonical.ToString());
    }

    internal static bool IsEvidence(string? value)
    {
        if (value is null)
        { return false; }
        return !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumEvidenceLength &&
            !value.Any(static character => char.IsControl(character));
    }

    internal static bool IsRootCallableId(string? value)
    {
        if (value is null)
        { return false; }
        return !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumRootCallableIdLength &&
            !value.Any(static character => char.IsControl(character));
    }

    private static StringBuilder Base(WorkerResourceTaskRuntimeDeclarations declarations)
    {
        declarations = ArgumentNullGuard.NotNull(declarations, nameof(declarations));
        if (Validate(declarations) is { } reason)
        { throw new InvalidDataException(reason); }
        var canonical = new StringBuilder();
        Append(canonical, Model);
        Append(canonical, ProfileCatalogSha256);
        Append(canonical, declarations.RootCallableId);
        Append(canonical, ((int)declarations.Profile).ToString(CultureInfo.InvariantCulture));
        Append(canonical, declarations.DeploymentManifestSha256);
        Append(canonical, declarations.Evidence);
        return canonical;
    }

    private static void AppendDeclaration(StringBuilder canonical, WorkerResourceTaskRuntimeDeclaration declaration)
    {
        canonical = ArgumentNullGuard.NotNull(canonical, nameof(canonical));
        declaration = ArgumentNullGuard.NotNull(declaration, nameof(declaration));
        Append(canonical, ((int)declaration.Quantity).ToString(CultureInfo.InvariantCulture));
        Append(canonical, Unit(declaration.Quantity));
        Append(canonical, Scope(declaration.Quantity));
        Append(canonical, declaration.UpperBound.ToString(CultureInfo.InvariantCulture));
        Append(canonical, declaration.Evidence);
    }

    private static void Append(StringBuilder canonical, string value)
    {
        canonical = ArgumentNullGuard.NotNull(canonical, nameof(canonical));
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        canonical.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
    }

    private static string Hash(string value)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        return ProtocolHashEncoding.ComputeSha256Hex([.. Encoding.UTF8.GetBytes(value)]);
    }
}
