using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SharpProof.Ir;

namespace SharpProof.Specs;

internal sealed record ResourcePrimitiveHelperSelectionCheck(int? DirectFramesUpper,
    ImmutableArray<ResourcePrimitiveHCallKind> SelectedEntries, string? UnknownReason)
{
    public ImmutableArray<ResourcePrimitiveHCallKind> SelectedEntries { get; init; } = SelectedEntries.IsDefault
        ? throw new ArgumentException("Selected entries must be initialized.", nameof(SelectedEntries)) : SelectedEntries;
    internal string? RequiredSelectionModel => DirectFramesUpper is null ? null : ResourcePrimitiveHelperSelection.Model;
    internal string? RequiredEntryFrameModel => DirectFramesUpper is null ? null : ResourceTaskFaultCarrierCatalog.PrimitiveHCallFrameModel;
    internal string? RequiredContentSha256 => DirectFramesUpper is null ? null : ResourcePrimitiveHelperSelection.ContentSha256;
}

// Conditional selection for an owned Int32 newarr under the pinned Linux x64
// ordinary JIT and stock helper-table bindings at every compilation tier.
// Declarations are not runtime detection. This bounds the selected entries'
// direct explicit trace frames only, not managed callees, retries or root bytes.
internal static class ResourcePrimitiveHelperSelection
{
    internal const string Model = "coreclr8-linux-x64-int32-array-helper-selection-v1";
    internal const string SelectionSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/jitinterfacegen.cpp#L57";
    internal const string SelectionSourceSha256 = "58E24FEE9355951652A7545627AD577A3AE30CEBD096FF58FE16A723E6ED1676";
    internal const string OpcodeSelectorSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/jitinterface.cpp#L5947";
    internal const string HelperLookupSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/jitinterface.cpp#L10576";
    internal const string SelectorAndLookupSourceSha256 = "69A7F3E84C8EFFF8E03E2A158744CC8BF3F6C6F351B4C5A68C42A04EE74E34D4";
    internal const string HelperTableSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/inc/jithelpers.h#L81";
    internal const string HelperTableSourceSha256 = "48516DA3B718F9051783E3D6F1CFA23C63F9EF13D830338B2018C5F4F1DCFEC4";
    internal const string HelperEntrySource = ResourceTaskFaultCarrierCatalog.PrimitiveHelperSource;
    internal const string HelperEntrySourceSha256 = "3FDD9938D85D5A291191F09E40D5F63D236FDD49819D55BF108DE48408389EDE";

    internal static string ContentSha256 { get; } = Hash(Model + "\n" + ResourceMetadataCatalog.Runtime + "\n" +
        SelectionSource + "\n" + SelectionSourceSha256 + "\n" + OpcodeSelectorSource + "\n" + HelperLookupSource + "\n" +
        SelectorAndLookupSourceSha256 + "\n" + HelperTableSource + "\n" + HelperTableSourceSha256 + "\n" +
        HelperEntrySource + "\n" + HelperEntrySourceSha256 + "\n" + ResourceTaskFaultCarrierCatalog.PrimitiveHCallFrameModel +
        "\nLinux;ordinary-jit;stock-helper-bindings-at-every-compilation-tier\n" +
        "NewArrayWorker,NewArrayValueFastPortable\nentry-local-explicit-trace-frames-only\n");

    internal static ResourcePrimitiveHelperSelectionCheck BoundInt32ArrayEntryFrames(string operatingSystem,
        bool ordinaryJit, bool stockHelperBindings, ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        operatingSystem = ArgumentNullGuard.NotNull(operatingSystem, nameof(operatingSystem));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        if (operatingSystem != "Linux")
        { return new(null, [], "UnsupportedPrimitiveHelperOperatingSystem"); }
        if (!ordinaryJit)
        { return new(null, [], "PrimitiveArrayOrdinaryJitPremiseNotBound"); }
        if (!stockHelperBindings)
        { return new(null, [], "PrimitiveArrayStockHelperBindingsPremiseNotBound"); }

        // jithelpers.h initializes DIRECT and VC to the worker. On Linux x64,
        // InitJITHelpers1 either retains VC's worker binding or assigns the
        // portable entry. The selector's stress/logging branches only choose
        // DIRECT instead of VC, so no setting or warm-state assumption is needed.
        ImmutableArray<ResourcePrimitiveHCallKind> selectedEntries =
            [ResourcePrimitiveHCallKind.NewArrayWorker, ResourcePrimitiveHCallKind.NewArrayValueFastPortable];
        var directFramesUpper = int.MinValue;
        foreach (var entry in selectedEntries)
        {
            var check = ResourceTaskFaultCarrierCatalog.BoundDirectPrimitiveHCallFrames(entry, layout, cancellation);
            if (check.DirectFramesUpper is not { } frames)
            { return new(null, [], check.UnknownReason); }
            directFramesUpper = Math.Max(directFramesUpper, frames);
        }
        return new(directFramesUpper, selectedEntries, null);
    }

    private static string Hash(string value)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty);
    }
}
