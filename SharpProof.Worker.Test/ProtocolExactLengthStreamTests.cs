using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

// These tests exercise the protocol's legacy byte-array read entrypoints.
#pragma warning disable CA1835, CA1849

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ProtocolExactLengthStreamTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task ExactReadsCompleteAtDeclaredLength(int mode)
    {
        using var input = new MemoryStream([10, 20]);
        using var bounded = BoundedReadStream.ForExactLength(input, 2, "opening mismatch", "length changed");
        Assert.That(await ReadOne(bounded, mode), Is.EqualTo(10));
        Assert.That(await ReadOne(bounded, mode), Is.EqualTo(20));
        Assert.That(await ReadOne(bounded, mode), Is.EqualTo(-1));
        Assert.That(bounded.ReadByte(), Is.EqualTo(-1));
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    public async Task ExactReadsRejectChangedStreamLength(int mode, bool extend)
    {
        using var input = new MemoryStream();
        input.Write([10, 20]);
        input.Position = 0;
        using var bounded = BoundedReadStream.ForExactLength(input, 2, "opening mismatch", "length changed");
        input.SetLength(extend ? 3 : 1);
        Assert.That(await ReadOne(bounded, mode), Is.EqualTo(10));
        if (extend)
        {
            Assert.That(await ReadOne(bounded, mode), Is.EqualTo(20));
        }
        var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
        {
            await ReadOne(bounded, mode);
        }));
        Assert.That(error!.Message, Is.EqualTo("length changed"));
    }

    [Test]
    public void ExactReadsRejectMismatchedDeclarationAndNegativeLength()
    {
        using var input = new MemoryStream([10, 20]);
        var mismatch = Assert.Throws<InvalidDataException>((Action)(() =>
        {
            using var bounded = BoundedReadStream.ForExactLength(input, 1, "opening mismatch", "length changed");
        }));
        var negative = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
        {
            using var bounded = BoundedReadStream.ForExactLength(input, -1, "opening mismatch", "length changed");
        }));
        Assert.That(mismatch!.Message, Is.EqualTo("opening mismatch"));
        Assert.That(negative!.ParamName, Is.EqualTo("expectedLength"));
        Assert.That(input.Position, Is.Zero);
    }

    [Test]
    public async Task ExactEmptyAndZeroLengthReadsDoNotConsumeInput()
    {
        using var input = new MemoryStream();
        using var bounded = BoundedReadStream.ForExactLength(input, 0, "opening mismatch", "length changed");
        var buffer = new byte[1];
        Assert.That(bounded.Read(buffer, 0, 0), Is.Zero);
        Assert.That(await bounded.ReadAsync(buffer, 0, 0), Is.Zero);
        Assert.That(await ReadOne(bounded, 1), Is.EqualTo(-1));
        Assert.That(bounded.ReadByte(), Is.EqualTo(-1));
    }

    [Test]
    public async Task FileSnapshotReadsAndHashPreservePublishedJsonBytes()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N") + ".json");
        var json = WorkerProtocolJson.SerializeRequest(new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference
            {
                Path = "compiler.manifest.json",
                Sha256 = WorkerProtocolVersions.EmptySha256
            }
        });
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            Assert.That(WorkerProtocolJson.ReadUtf8File(path), Is.EqualTo(json));
            Assert.That(await WorkerProtocolJson.ReadUtf8FileAsync(path), Is.EqualTo(json));
            Assert.That(WorkerProtocolJson.ComputeFileSha256(path),
                Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes))));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<int> ReadOne(BoundedReadStream bounded, int mode)
    {
        System.ArgumentNullException.ThrowIfNull(bounded);
        if (mode == 2)
        {
            return bounded.ReadByte();
        }
        var buffer = new byte[1];
        var count = mode == 1
            ? await bounded.ReadAsync(buffer, 0, 1)
            : bounded.Read(buffer, 0, 1);
        return count == 0 ? -1 : buffer[0];
    }
}
