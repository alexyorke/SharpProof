
namespace SharpProof.Specs;

// Conditional source-work semantics, not a framework allocation summary or
// evidence that the deployment uses this runtime.
internal static class ResourceTaskCatalog
{
    internal const string Model = "task-join-source-at-most-once-v1";
    internal const string ConfiguredModel = "configured-task-join-source-at-most-once-v1";
    internal const string Witness = "bcl.task.run-action.when-all-array";
    internal const string EvidenceSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs";
    internal static string ContentSha256 { get; } = ResourceCatalogIdentity.Sha256Upper(Model + "\n" + ResourceMetadataCatalog.Runtime + "\n" + EvidenceSource +
        "\nTask.Run(Action);default-scheduler;no-cancellation-token;at-most-once\n" +
        "Task.WhenAll(Task[]);fresh-closed-children;work-through-join-completion\n" +
        "isolated-source-callbacks;all-overlap-orders;per-root-invocation\n" +
        "excludes-framework-scheduler-ambient-callback-work;no-framework-allocation-summary\n");
    internal static string ConfiguredContentSha256 { get; } = ResourceCatalogIdentity.Sha256Upper(ConfiguredModel + "\n" + ContentSha256 +
        "\nstandard-Task-builder;single-configured-task-await;continueOnCapturedContext=false\n" +
        "initial-MoveNext-once;completed-or-single-resumption;no-extra-source-statements;no-custom-builder\n" +
        "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncMethodBuilderCore.cs\n" +
        "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilder.cs\n" +
        "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilderT.cs\n" +
        "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/TaskAwaiter.cs\n");

    internal static bool IsReviewedReference(string assembly)
    {
        assembly = ArgumentNullGuard.NotNull(assembly, nameof(assembly));
        return ResourceCatalogIdentity.IsReviewedFrameworkReference(assembly);
    }

    internal static string AssumptionId(string callableId, string assembly, bool configuredAwait = false)
    {
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        assembly = ArgumentNullGuard.NotNull(assembly, nameof(assembly));
        return "resource-metadata-" + (configuredAwait ? ConfiguredModel : Model) + ":" +
            (configuredAwait ? ConfiguredContentSha256 : ContentSha256) + ":" + Witness + ":" + EvidenceSource +
            ":owner:" + ResourceCatalogIdentity.OwnerHash(callableId, assembly);
    }
}
