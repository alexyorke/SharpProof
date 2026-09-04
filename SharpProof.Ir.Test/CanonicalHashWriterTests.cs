using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class CanonicalHashWriterTests
{
    private const string GoldenHash =
        "f11c5f9ada1e3d32677b90b80baee7ffe826e1abb68161e4c3474fd57a103c17";

    [Test]
    public void TypedWritesPreserveTheCanonicalByteFormat()
    {
        using var typed = new CanonicalHashWriter();
        typed.Add("domain")
            .Add(true)
            .Add(42)
            .Add(uint.MaxValue)
            .Add(long.MinValue)
            .Add(new byte[] { 0, 1, 255 });

        Assert.That(typed.Finish(), Is.EqualTo(GoldenHash));
    }

    [Test]
    public void TypeAndNullFramesPreventCanonicalValueCollisions()
    {
        static string Hash(object? value)
        {
            using var writer = new CanonicalHashWriter();
            return writer.Add(value).Finish();
        }

        Assert.That(
            new[] {
                Hash(null),
                Hash(string.Empty),
                Hash("1"),
                Hash(1),
                Hash(1U),
                Hash(1L),
                Hash(true),
                Hash(TestEnum.One),
                Hash(new byte[] { 1 })
            }.Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(9));
    }

    [Test]
    public void UnsupportedBatchValueFailsClosed()
    {
        using var writer = new CanonicalHashWriter();
        using var enumWriter = new CanonicalHashWriter();

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentException>(
                (Action)(() => writer.Add(DateTime.UnixEpoch)));
            Assert.Throws<ArgumentOutOfRangeException>(
                (Action)(() => enumWriter.Add((TestEnum)2)));
        }
    }

    [Test]
    public void StreamFramesPreserveByteArrayIdentity()
    {
        byte[] bytes = [0, 1, 2, 3];
        using var arrayWriter = new CanonicalHashWriter();
        using var streamWriter = new CanonicalHashWriter();
        using var stream = new MemoryStream(bytes, writable: false);
        var expected = arrayWriter.Add(bytes).Finish();

        Assert.That(
            streamWriter.Add(stream).Finish(),
            Is.EqualTo(expected));
    }

    [Test]
    public void StreamGrowthBeyondTheDeclaredLengthFailsClosed()
    {
        AssertStreamGrowthFails([0, 1, 2, 3]);
    }

    [Test]
    public void ZeroLengthStreamGrowthFailsClosed()
    {
        AssertStreamGrowthFails([]);
    }

    private static void AssertStreamGrowthFails(byte[] initial)
    {
        using var writer = new CanonicalHashWriter();
        using var stream = new GrowingStream(initial);

        Assert.That(
            (Action)(() => writer.Add(stream)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void FinishedWriterRejectsFurtherUse()
    {
        using var writer = new CanonicalHashWriter();
        writer.Add("value");
        _ = writer.Finish();

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ObjectDisposedException>(
                (Action)(() => _ = writer.Add("late")));
            Assert.Throws<ObjectDisposedException>(
                (Action)(() => _ = writer.Finish()));
        }
    }

    private enum TestEnum
    {
        One = 1
    }

    private sealed class GrowingStream : MemoryStream
    {
        private bool _grown;

        internal GrowingStream(byte[] initial)
        {
            Write(initial);
            Position = 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_grown)
            {
                _grown = true;
                SetLength(Length + 1);
                Position = Length - 1;
                WriteByte(4);
                Position = 0;
            }

            return base.Read(buffer, offset, count);
        }
    }
}
