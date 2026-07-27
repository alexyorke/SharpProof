using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class CanonicalHashWriterTests {
    private const string GoldenHash =
        "df973f08d53b8866d2da9c28359257b389f39190d7638901d424cc8ee31d2dae";

    [Test]
    public void TypedAndBatchWritesPreserveTheCanonicalByteFormat() {
        using var typed = new CanonicalHashWriter();
        typed.Add("domain")
            .Add(true)
            .Add(42)
            .Add(long.MinValue)
            .Add(new byte[] { 0, 1, 255 });
        using var batch = new CanonicalHashWriter();
        batch.Add("domain", true, 42, long.MinValue,
            new byte[] { 0, 1, 255 });

        using (Assert.EnterMultipleScope()) {
            Assert.That(typed.Finish(), Is.EqualTo(GoldenHash));
            Assert.That(batch.Finish(), Is.EqualTo(GoldenHash));
        }
    }

    [Test]
    public void FinishedWriterRejectsFurtherUse() {
        using var writer = new CanonicalHashWriter();
        writer.Add("value");
        _ = writer.Finish();

        using (Assert.EnterMultipleScope()) {
            Assert.Throws<ObjectDisposedException>(
                (Action)(() => _ = writer.Add("late")));
            Assert.Throws<ObjectDisposedException>(
                (Action)(() => _ = writer.Finish()));
        }
    }
}
