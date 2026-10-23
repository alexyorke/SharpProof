using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeTaskFaultNotificationStorageTests
{
    [TestCase(1, 216)]
    [TestCase(2, 224)]
    [TestCase(8, 272)]
    [TestCase(16, 336)]
    [TestCase(17, 344)]
    [TestCase(32, 464)]
    public void ConstructionIncludesInheritedExceptionSlotsArrayAndEventArguments(int faults, int expected)
    {
        var check = NativeTaskFaultNotificationStorageVerifier.Check(faults, new("CoreCLR", "8.0.32", "x64", 8),
            TestContext.CurrentContext.CancellationToken);
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.ConstructionUpper!.Constant, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }

    [TestCase(0)]
    [TestCase(33)]
    public void UnsupportedDispatchCountsHaveNoNumericComponent(int faults)
    {
        var check = NativeTaskFaultNotificationStorageVerifier.Check(faults, new("CoreCLR", "8.0.32", "x64", 8),
            TestContext.CurrentContext.CancellationToken);
        Assert.That(check.ConstructionUpper, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo("TaskFaultNotificationCountUnsupported"));
    }

    [Test]
    public void IncompatibleLayoutsAndCancellationDoNotPublishPartialConstructionBounds()
    {
        var check = NativeTaskFaultNotificationStorageVerifier.Check(2, new("CoreCLR", "8.0.32", "x86", 4),
            TestContext.CurrentContext.CancellationToken);
        Assert.That(check.ConstructionUpper, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action canceled = () =>
        { _ = NativeTaskFaultNotificationStorageVerifier.Check(2, new("CoreCLR", "8.0.32", "x64", 8), cancellation.Token); };
        Assert.Throws<OperationCanceledException>(canceled);
    }
}
