using NUnit.Framework;
using SharpProof.Worker.Protocol;

// The legacy byte-array overloads are the protocol entrypoints under test.
#pragma warning disable CA1835, CA1849, CA2022

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class BoundedReadStreamTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ArrayReadsPermitExactLimitAndRejectOneMoreByte(bool asynchronous)
    {
        using var inner = new MemoryStream([10, 20, 30]);
        using var bounded = new BoundedReadStream(inner, 2, "payload too large");
        var buffer = new byte[5];

        Assert.That(await ReadAsyncOrSync(bounded, buffer, 1, 4, asynchronous), Is.EqualTo(2));
        Assert.That(buffer, Is.EqualTo(new byte[] { 0, 10, 20, 0, 0 }));
        Assert.That(inner.Position, Is.EqualTo(2));
        Assert.That(await ReadAsyncOrSync(bounded, buffer, 0, 0, asynchronous), Is.Zero);
        Assert.That(inner.Position, Is.EqualTo(2), "zero-length reads must not consume the overflow byte");

        Func<Task> overflowRead = async () =>
        {
            await ReadAsyncOrSync(bounded, buffer, 0, 1, asynchronous);
        };
        var error = Assert.ThrowsAsync<InvalidDataException>(overflowRead);
        Assert.That(error!.Message, Is.EqualTo("payload too large"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ArrayReadsReturnEofAtExactLimit(bool asynchronous)
    {
        using var bounded = new BoundedReadStream(
            new MemoryStream([10, 20]), 2, "payload too large");
        var buffer = new byte[4];

        Assert.That(await ReadAsyncOrSync(bounded, buffer, 0, 4, asynchronous), Is.EqualTo(2));
        Assert.That(await ReadAsyncOrSync(bounded, buffer, 0, 1, asynchronous), Is.Zero);
        Assert.That(bounded.ReadByte(), Is.EqualTo(-1));
    }

    [Test]
    public void ArrayReadsRejectInvalidSlicesBeforeConsumingInput()
    {
        using var inner = new MemoryStream([10, 20]);
        using var bounded = new BoundedReadStream(inner, 1, "payload too large");
        var buffer = new byte[1];

        Assert.Multiple((Action)(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>((Action)(() => bounded.Read(buffer, -1, 1)));
            Assert.Throws<ArgumentOutOfRangeException>((Action)(() => bounded.Read(buffer, 0, -1)));
            Assert.Throws<ArgumentException>((Action)(() => bounded.Read(buffer, 1, 1)));
            Assert.ThrowsAsync<ArgumentOutOfRangeException>((Func<Task>)(async () =>
            {
                await bounded.ReadAsync(buffer, -1, 1);
            }));
            Assert.ThrowsAsync<ArgumentOutOfRangeException>((Func<Task>)(async () =>
            {
                await bounded.ReadAsync(buffer, 0, -1);
            }));
            Assert.ThrowsAsync<ArgumentException>((Func<Task>)(async () =>
            {
                await bounded.ReadAsync(buffer, 1, 1);
            }));
        }));
        Assert.That(inner.Position, Is.Zero);
        Assert.That(bounded.ReadByte(), Is.EqualTo(10));
    }

    private static async Task<int> ReadAsyncOrSync(
        BoundedReadStream stream, byte[] buffer, int offset, int count,
        bool asynchronous)
    {
        System.ArgumentNullException.ThrowIfNull(buffer, nameof(buffer));
        System.ArgumentNullException.ThrowIfNull(stream);
        return asynchronous
            ? await stream.ReadAsync(buffer, offset, count)
            : stream.Read(buffer, offset, count);
    }
}
