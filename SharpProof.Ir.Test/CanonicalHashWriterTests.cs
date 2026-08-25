using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class CanonicalHashWriterTests
{
    private const string GoldenHash =
        "f11c5f9ada1e3d32677b90b80baee7ffe826e1abb68161e4c3474fd57a103c17";

    [Test]
    public void TypedAndBatchWritesPreserveTheCanonicalByteFormat()
    {
        using var typed = new CanonicalHashWriter();
        typed.Add("domain")
            .Add(true)
            .Add(42)
            .Add(uint.MaxValue)
            .Add(long.MinValue)
            .Add(new byte[] { 0, 1, 255 });
        using var batch = new CanonicalHashWriter();
        batch.Add("domain", true, 42, uint.MaxValue, long.MinValue,
            new byte[] { 0, 1, 255 });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(typed.Finish(), Is.EqualTo(GoldenHash));
            Assert.That(batch.Finish(), Is.EqualTo(GoldenHash));
        }
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
    public void EnumFramesIncludeFullAssemblyIdentity()
    {
        var firstType = DefineDynamicEnum(new Version(1, 0, 0, 0));
        var secondType = DefineDynamicEnum(new Version(2, 0, 0, 0));

        using var firstWriter = new CanonicalHashWriter();
        using var secondWriter = new CanonicalHashWriter();
        var firstHash = firstWriter.Add(Enum.ToObject(firstType, 1)).Finish();
        var secondHash = secondWriter.Add(Enum.ToObject(secondType, 1)).Finish();

        Assert.That(firstHash, Is.Not.EqualTo(secondHash));
    }

    [Test]
    public void IllFormedStringsAreRejectedBeforeUtf8Replacement()
    {
        using var writer = new CanonicalHashWriter();

        Assert.That(
            (Action)(() => writer.Add("\uD800")),
            Throws.TypeOf<ArgumentException>());
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
        using var writer = new CanonicalHashWriter();
        using var stream = new GrowingStream([0, 1, 2, 3]);

        Assert.That(
            (Action)(() => writer.Add(stream)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void FailedStreamWritePoisonsTheWriter()
    {
        using var writer = new CanonicalHashWriter();
        using var stream = new ThrowingStream([0, 1, 2, 3]);

        Assert.Throws<IOException>((Action)(() => writer.Add(stream)));
        Assert.Throws<InvalidOperationException>(
            (Action)(() => writer.Add("after failure")));
        Assert.Throws<InvalidOperationException>(
            (Action)(() => writer.Finish()));
    }

    [Test]
    public void ZeroLengthStreamGrowthFailsClosed()
    {
        using var writer = new CanonicalHashWriter();
        using var stream = new GrowingStream([]);

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

    private static Type DefineDynamicEnum(Version version)
    {
        var assemblyName = new AssemblyName("SharpProof.EnumCollision")
        {
            Version = version
        };
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            assemblyName, AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Main");
        var builder = module.DefineEnum(
            "SharpProof.Test.Mode", TypeAttributes.Public, typeof(int));
        builder.DefineLiteral("On", 1);
        return builder.CreateTypeInfo()!.AsType();
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

    private sealed class ThrowingStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        private bool _returnedPrefix;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_returnedPrefix)
            {
                throw new IOException("synthetic stream failure");
            }

            _returnedPrefix = true;
            return base.Read(buffer, offset, Math.Min(2, count));
        }
    }
}
