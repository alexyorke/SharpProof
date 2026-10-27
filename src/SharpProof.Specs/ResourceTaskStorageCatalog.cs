using System.Collections.Immutable;
using SharpProof.Ir;

namespace SharpProof.Specs;

// Conditional .NET 8.0.32 normal-completion carrier storage, not deployment
// detection, scheduler storage or an exceptional-prefix allocation summary.
internal static class ResourceTaskStorageCatalog
{
    internal const string Model = "task-join-normal-owned-storage-v1";
    internal const string ConfiguredModel = "configured-task-join-normal-owned-storage-v1";
    internal const string Witness = "bcl.task.run-action.when-all-array.normal-owned-storage";
    internal const string EvidenceSource = ResourceTaskCatalog.EvidenceSource;
    internal const string FutureSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Future.cs";
    internal const string BuilderSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilderT.cs";

    // Task's instance fields in the reviewed source: id, action, state,
    // scheduler, flags, continuation, contingent properties.
    internal static ImmutableArray<ResourceStorageKind> TaskFields { get; } =
        [ResourceStorageKind.Word32, ResourceStorageKind.Reference, ResourceStorageKind.Reference,
         ResourceStorageKind.Reference, ResourceStorageKind.Word32, ResourceStorageKind.Reference, ResourceStorageKind.Reference];
    internal static ImmutableArray<ResourceStorageKind> JoinFields { get; } =
        [.. TaskFields, ResourceStorageKind.Reference, ResourceStorageKind.Word32];

    internal static string ContentSha256 { get; } = ResourceCatalogIdentity.Sha256Upper(Model + "\n" + ResourceMetadataCatalog.Runtime + "\n" + EvidenceSource + "\n" +
        string.Join(",", TaskFields) + "\n" + string.Join(",", JoinFields) +
        "\nnormal-successful-join;isolated-void-primitive-children;all-overlap-orders;per-root-invocation\n" +
        "default-or-suppressed-execution-context;no-debugger-or-tracing-instrumentation\n" +
        "owned-managed-carriers-only;excludes-scheduler-global-initialization-and-caller-induced-storage\n" +
        "one-Task-per-child;one-WhenAll-promise;single-private-continuation-per-child\n");
    internal static string ConfiguredContentSha256 { get; } = ResourceCatalogIdentity.Sha256Upper(ConfiguredModel + "\n" + ContentSha256 + "\n" + FutureSource + "\n" + BuilderSource +
        "\nstandard-builder;single-configured-await-false;Task-of-empty-VoidTaskResult\n" +
        "at-most-one-box;embedded-compiler-state;context-reuses-Task-state-field\n" +
        "at-most-one-MoveNext-Action-and-one-static-ContextCallback;no-weak-debugger-box\n");

    internal static string AssumptionId(string callableId, string assembly, bool configured)
    {
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        assembly = ArgumentNullGuard.NotNull(assembly, nameof(assembly));
        return "resource-metadata-" + (configured ? ConfiguredModel : Model) + ":" +
            (configured ? ConfiguredContentSha256 : ContentSha256) + ":" + Witness + ":" + EvidenceSource +
            ":owner:" + ResourceCatalogIdentity.OwnerHash(callableId, assembly);
    }
}
