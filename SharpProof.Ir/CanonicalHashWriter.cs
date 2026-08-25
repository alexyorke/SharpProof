using System.Security.Cryptography;
namespace SharpProof.Ir;
internal sealed class CanonicalHashWriter : IDisposable
{
    private readonly IncrementalHash _hash =
        IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _finished;
    private bool _poisoned;

    internal CanonicalHashWriter Add(string? value)
    {
        if (value == null)
        {
            return AddFrame(ValueKind.Null, []);
        }
        if (!Utf16WellFormedness.IsWellFormed(value))
        {
            throw new ArgumentException(
                "Canonical hash strings require well-formed UTF-16.",
                nameof(value));
        }
        return AddFrame(ValueKind.String, Encoding.UTF8.GetBytes(value));
    }

    internal CanonicalHashWriter Add(bool value)
    {
        return AddFrame(ValueKind.Boolean, BitConverter.GetBytes(value));
    }

    internal CanonicalHashWriter Add(int value)
    {
        return AddNumber(ValueKind.Int32, value);
    }

    internal CanonicalHashWriter Add(uint value)
    {
        return AddNumber(ValueKind.UInt32, value);
    }

    internal CanonicalHashWriter Add(long value)
    {
        return AddNumber(ValueKind.Int64, value);
    }

    internal CanonicalHashWriter Add(byte[] bytes)
    {
        return AddFrame(
            ValueKind.Bytes,
            ArgumentNullGuard.NotNull(bytes, nameof(bytes)));
    }

    internal CanonicalHashWriter Add(Stream stream)
    {
        stream = ArgumentNullGuard.NotNull(stream, nameof(stream));
        EnsureWritable();
        var completed = false;
        try
        {
            var remaining = stream.Length - stream.Position;
            if (remaining is < 0 or > int.MaxValue)
            {
                throw new InvalidDataException(
                    "The canonical hash stream length is outside the supported range.");
            }

            var length = (int)remaining;
            AddFrameHeader(ValueKind.Bytes, length);
            var buffer = new byte[Math.Min(length, 81920)];
            var bytesRead = 0;
            int read;
            while ((read = stream.Read(
                       buffer, 0, Math.Min(buffer.Length, length - bytesRead))) != 0)
            {
                bytesRead += read;
                _hash.AppendData(buffer, 0, read);
            }

            if (bytesRead != length || stream.ReadByte() != -1)
            {
                throw new InvalidDataException(
                    "The canonical hash stream does not match its declared length.");
            }
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                _poisoned = true;
            }
        }

        return this;
    }

    private CanonicalHashWriter Add(Enum value)
    {
        var name = value.ToString();
        if (name.Length == 0 || name[0] == '-' || char.IsDigit(name[0]))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Canonical enum values must have a declared name.");
        }

        var type = value.GetType();
        return AddFrame(
            ValueKind.Enum,
            Encoding.UTF8.GetBytes(
                (type.Assembly.GetName().Name ?? string.Empty) +
                "\n" +
                (type.FullName ?? type.Name) +
                "\n" +
                name));
    }

    private CanonicalHashWriter AddNumber<T>(ValueKind kind, T value)
        where T : struct, IFormattable
    {
        return AddFrame(
            kind,
            Encoding.UTF8.GetBytes(
                value.ToString(null, CultureInfo.InvariantCulture)));
    }

    private CanonicalHashWriter AddFrame(ValueKind kind, byte[] bytes)
    {
        AddFrameHeader(kind, bytes.Length);
        _hash.AppendData(bytes);
        return this;
    }

    private void AddFrameHeader(ValueKind kind, int length)
    {
        EnsureWritable();

        _hash.AppendData([
            (byte)kind, (byte)length, (byte)(length >> 8),
            (byte)(length >> 16), (byte)(length >> 24)
        ]);
    }

    internal CanonicalHashWriter Add(params object?[] values)
    {
        foreach (var value in values)
        {
            _ = value switch
            {
                null => Add((string?)null),
                string text => Add(text),
                bool boolean => Add(boolean),
                int integer => Add(integer),
                uint unsignedInteger => Add(unsignedInteger),
                long integer => Add(integer),
                byte[] bytes => Add(bytes),
                Enum enumeration => Add(enumeration),
                _ => throw new ArgumentException(
                    "Canonical hash values must use a supported exact type.",
                    nameof(values))
            };
        }
        return this;
    }

    internal string Finish()
    {
        EnsureWritable();

        _finished = true;
        return string.Concat(_hash.GetHashAndReset().Select(static value =>
            value.ToString("x2", CultureInfo.InvariantCulture)));
    }
    public void Dispose()
    {
        _finished = true;
        _hash.Dispose();
    }

    private void EnsureWritable()
    {
        if (_finished)
        {
            throw new ObjectDisposedException(nameof(CanonicalHashWriter));
        }
        if (_poisoned)
        {
            throw new InvalidOperationException(
                "The canonical hash writer cannot continue after a failed stream write.");
        }
    }

    private enum ValueKind : byte
    {
        Null,
        String,
        Boolean,
        Int32,
        UInt32,
        Int64,
        Bytes,
        Enum
    }
}
