using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Specs.Test;

[TestFixture]
internal sealed class ResourcePrimitiveHelperSelectionTests
{
    [Test]
    public void LinuxOrdinaryJitChecksTheCompleteWorkerAndPortableSelectionUnion()
    {
        var result = ResourcePrimitiveHelperSelection.BoundInt32ArrayEntryFrames("Linux", true, true,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.DirectFramesUpper, Is.Zero);
        Assert.That(result.SelectedEntries, Is.EqualTo(new[]
        {
            ResourcePrimitiveHCallKind.NewArrayWorker,
            ResourcePrimitiveHCallKind.NewArrayValueFastPortable
        }));
        Assert.That(result.RequiredSelectionModel, Is.EqualTo("coreclr8-linux-x64-int32-array-helper-selection-v1"));
        Assert.That(result.RequiredEntryFrameModel, Is.EqualTo("coreclr8-direct-primitive-hcall-trace-frames-v2"));
        Assert.That(result.RequiredContentSha256, Is.EqualTo(ResourcePrimitiveHelperSelection.ContentSha256));
        Assert.That(result.RequiredContentSha256, Does.Match("^[0-9A-F]{64}$"));
        Assert.That(ResourcePrimitiveHelperSelection.SelectionSource,
            Is.EqualTo("https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/jitinterfacegen.cpp#L57"));
        Assert.That(ResourcePrimitiveHelperSelection.SelectionSourceSha256,
            Is.EqualTo("58E24FEE9355951652A7545627AD577A3AE30CEBD096FF58FE16A723E6ED1676"));
    }

    [TestCase("Windows")]
    [TestCase("Darwin")]
    [TestCase("linux")]
    [TestCase("Linux ")]
    [TestCase("")]
    public void UnreviewedPlatformDeclarationsPublishNoSelectionOrModels(string operatingSystem)
    {
        ArgumentNullException.ThrowIfNull(operatingSystem);
        var result = ResourcePrimitiveHelperSelection.BoundInt32ArrayEntryFrames(operatingSystem, true, true,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        AssertUnknown(result, "UnsupportedPrimitiveHelperOperatingSystem");
    }

    [TestCase(false, true, "PrimitiveArrayOrdinaryJitPremiseNotBound")]
    [TestCase(true, false, "PrimitiveArrayStockHelperBindingsPremiseNotBound")]
    [TestCase(false, false, "PrimitiveArrayOrdinaryJitPremiseNotBound")]
    public void EveryMissingExecutionPredicateKeepsTheWholeSelectionUnknown(bool ordinaryJit, bool stockHelperBindings, string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        var result = ResourcePrimitiveHelperSelection.BoundInt32ArrayEntryFrames("Linux", ordinaryJit, stockHelperBindings,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        AssertUnknown(result, reason);
    }

    [TestCase("Mono", "8.0.32", "x64", 8)]
    [TestCase("CoreCLR", "8.0.31", "x64", 8)]
    [TestCase("CoreCLR", "9.0.0", "x64", 8)]
    [TestCase("CoreCLR", "8.0.32", "arm64", 8)]
    [TestCase("CoreCLR", "8.0.32", "x86", 4)]
    [TestCase("CoreCLR", "8.0.32", "x64", 4)]
    public void EveryIncompatibleLayoutDimensionWithholdsAllSelectionEvidence(string runtime, string version, string architecture, int pointerBytes)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(architecture);
        var result = ResourcePrimitiveHelperSelection.BoundInt32ArrayEntryFrames("Linux", true, true,
            new(runtime, version, architecture, pointerBytes), TestContext.CurrentContext.CancellationToken);
        AssertUnknown(result, "IncompatibleResourceRuntimeLayout");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CancellationPropagatesBeforeAnyBoundOrUnknownCanBePublished(bool admitted)
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", admitted ? "x64" : "arm64", 8);
        Action check = () =>
        {
            _ = ResourcePrimitiveHelperSelection.BoundInt32ArrayEntryFrames(admitted ? "Linux" : "Windows",
                admitted, admitted, layout, canceled.Token);
        };
        Assert.Throws<OperationCanceledException>(() => check());
    }

    [Test]
    public void RequiredReferencesAndSelectionArraysKeepTheirConstructorGuards()
    {
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.Throws<ArgumentNullException>(() => ResourcePrimitiveHelperSelection.BoundInt32ArrayEntryFrames(null!, true, true,
            new("CoreCLR", "8.0.32", "x64", 8), cancellation));
        Assert.Throws<ArgumentNullException>(() => ResourcePrimitiveHelperSelection.BoundInt32ArrayEntryFrames("Linux", true, true, null!, cancellation));
        Assert.Throws<ArgumentException>(() => new ResourcePrimitiveHelperSelectionCheck(null, default, "Unavailable"));
    }

    private static void AssertUnknown(ResourcePrimitiveHelperSelectionCheck result, string reason)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(reason);
        Assert.That(result.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.DirectFramesUpper, Is.Null);
        Assert.That(result.SelectedEntries, Is.Empty);
        Assert.That(result.RequiredSelectionModel, Is.Null);
        Assert.That(result.RequiredEntryFrameModel, Is.Null);
        Assert.That(result.RequiredContentSha256, Is.Null);
    }
}
