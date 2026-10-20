using System.Collections.Immutable;
using SharpProof.Ir;

namespace SharpProof.Specs;

// One notification construction with a preexisting message and stable dispatch list.
// Input exceptions/dispatches, resource initialization, lazy property views and
// event subscribers belong to separate allocation components.
internal static class ResourceTaskFaultNotificationCatalog
{
    internal const string Model = "task-fault-notification-storage-v1";
    internal const string ExceptionSource = ResourceTaskFaultCarrierCatalog.StateSource;
    internal const string ConstructorSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Exception.cs";
    internal const string AggregateSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/AggregateException.cs";
    internal const string EventSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskScheduler.cs";

    internal static ImmutableArray<ResourceStorageKind> ExceptionFields { get; } =
        [ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.Reference,
         ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.Reference,
         ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.Reference,
         ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.NativeInteger,
         ResourceStorageKind.NativeInteger, ResourceStorageKind.Word32, ResourceStorageKind.Word32];
    // Both the array and the initially null lazy ReadOnlyCollection view have slots.
    internal static ImmutableArray<ResourceStorageKind> AggregateFields { get; } =
        [.. ExceptionFields, ResourceStorageKind.Reference, ResourceStorageKind.Reference];
    internal static ImmutableArray<ResourceStorageKind> EventFields { get; } =
        [ResourceStorageKind.Reference, ResourceStorageKind.Byte];
}
