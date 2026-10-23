using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Specs.Test;

[TestFixture]
internal sealed class PrimitiveHelperSelectionSpecsCoverageTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void SelectionPublishesTheOrderedCompleteUnionOnlyForACompatibleLayout(bool compatible)
    {
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", compatible ? "x64" : "arm64", 8);
        ResourcePrimitiveHCallKind[] entries =
            [ResourcePrimitiveHCallKind.NewArrayWorker, ResourcePrimitiveHCallKind.NewArrayValueFastPortable];
        var direct = entries.Select(entry => ResourceTaskFaultCarrierCatalog.BoundDirectPrimitiveHCallFrames(
            entry, layout, cancellation)).ToArray();
        var selected = ResourcePrimitiveHelperSelection.BoundInt32ArrayEntryFrames("Linux", true, true, layout, cancellation);
        if (compatible)
        {
            Assert.That(direct.All(static check => check is null ? throw new ArgumentNullException(nameof(check)) :
                check.DirectFramesUpper is not null && check.UnknownReason is null), Is.True);
            Assert.That(selected.DirectFramesUpper, Is.EqualTo(direct.Max(static check =>
                check is null ? throw new ArgumentNullException(nameof(check)) : check.DirectFramesUpper)));
            Assert.That(selected.SelectedEntries, Is.EqualTo(entries));
            Assert.That(selected.UnknownReason, Is.Null);
            Assert.That(selected.RequiredSelectionModel, Is.EqualTo(ResourcePrimitiveHelperSelection.Model));
            Assert.That(selected.RequiredEntryFrameModel, Is.EqualTo(ResourceTaskFaultCarrierCatalog.PrimitiveHCallFrameModel));
            Assert.That(selected.RequiredContentSha256, Is.EqualTo(ResourcePrimitiveHelperSelection.ContentSha256));
        }
        else
        {
            Assert.That(direct.All(static check => check is null ? throw new ArgumentNullException(nameof(check)) :
                check.DirectFramesUpper is null && check.UnknownReason == "IncompatibleResourceRuntimeLayout"), Is.True);
            Assert.That(selected.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
            Assert.That(selected.DirectFramesUpper, Is.Null);
            Assert.That(selected.SelectedEntries, Is.Empty);
            Assert.That(selected.RequiredSelectionModel, Is.Null);
            Assert.That(selected.RequiredEntryFrameModel, Is.Null);
            Assert.That(selected.RequiredContentSha256, Is.Null);
        }
    }

    [Test]
    public void CancellationDoesNotTurnTheHelperUnionIntoASelection()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var error = Assert.Throws<OperationCanceledException>((Action)(() =>
            ResourcePrimitiveHelperSelection.BoundInt32ArrayEntryFrames("Linux", true, true,
                new("CoreCLR", "8.0.32", "x64", 8), canceled.Token)));
        Assert.That(error!.CancellationToken, Is.EqualTo(canceled.Token));
    }
}
