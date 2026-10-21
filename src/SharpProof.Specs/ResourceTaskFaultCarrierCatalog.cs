using System.Collections.Immutable;
using SharpProof.Ir;

namespace SharpProof.Specs;

// Fixed .NET 8.0.32 fault bookkeeping only. Exception objects, strings, traces,
// scheduler/global initialization and externally induced storage are separate.
internal static class ResourceTaskFaultCarrierCatalog
{
    internal const string Model = "task-join-fixed-fault-carriers-v2";
    // One queue attempt and one at-most-once callback can each record a fault.
    internal const int MaximumChildFaultRecords = 2;
    internal const int MaximumJoinedDispatches = 16 * MaximumChildFaultRecords;
    internal const string TaskSource = ResourceTaskCatalog.EvidenceSource;
    internal const string HolderSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskExceptionHolder.cs";
    internal const string DispatchSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Runtime/ExceptionServices/ExceptionDispatchInfo.cs";
    internal const string StateSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs";
    internal const string ListSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs";

    // CancellationToken embeds one source reference. The remaining contingent
    // fields comprise six references and two Int32 values.
    internal static ImmutableArray<ResourceStorageKind> ContingentFields { get; } =
        [ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.Reference,
         ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.Word32,
         ResourceStorageKind.Word32, ResourceStorageKind.Reference, ResourceStorageKind.Reference];
    internal static ImmutableArray<ResourceStorageKind> HolderFields { get; } =
        [ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.Byte];
    // Exception reference plus embedded DispatchState: four references and UIntPtr.
    // Referenced payloads are deliberately not part of this fixed-instance model.
    internal static ImmutableArray<ResourceStorageKind> DispatchFields { get; } =
        [ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.Reference,
         ResourceStorageKind.Reference, ResourceStorageKind.NativeInteger, ResourceStorageKind.Reference];
    internal static ImmutableArray<ResourceStorageKind> ListFields { get; } =
        [ResourceStorageKind.Reference, ResourceStorageKind.Word32, ResourceStorageKind.Word32];
}
