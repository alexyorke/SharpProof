using System.Security.Cryptography;
namespace SharpProof.Ir;
internal sealed class CanonicalHashWriter : IDisposable
{
    private readonly IncrementalHash _hash =
        IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _finished;

    internal CanonicalHashWriter Add(string? value)
    {
        return value == null
            ? AddFrame(ValueKind.Null, [])
            : AddFrame(ValueKind.String, Encoding.UTF8.GetBytes(value));
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
        var length = checked((int)(stream.Length - stream.Position));
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

        return this;
    }

    private CanonicalHashWriter Add(Enum value)
    {
        return AddEnum(value.GetType(), value.ToString(), nameof(value));
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
        if (_finished)
        {
            throw new ObjectDisposedException(nameof(CanonicalHashWriter));
        }

        _hash.AppendData([
            (byte)kind, (byte)length, (byte)(length >> 8),
            (byte)(length >> 16), (byte)(length >> 24)
        ]);
    }

    internal CanonicalHashWriter Add(object? value)
    {
        return value switch
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
                nameof(value))
        };
    }

    internal CanonicalHashWriter Add<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return AddEnum(typeof(TEnum), value.ToString(), nameof(value));
    }

    internal CanonicalHashWriter Add<TEnum>(TEnum? value)
        where TEnum : struct, Enum
    {
        return value.HasValue
            ? Add(value.Value)
            : Add((string?)null);
    }

    private CanonicalHashWriter AddEnum(
        Type type,
        string name,
        string argumentName)
    {
        if (name.Length == 0 || name[0] == '-' || char.IsDigit(name[0]))
        {
            throw new ArgumentOutOfRangeException(
                argumentName,
                "Canonical enum values must have a declared name.");
        }

        return AddFrame(
            ValueKind.Enum,
            Encoding.UTF8.GetBytes(
                (type.Assembly.GetName().Name ?? string.Empty) +
                "\n" +
                (type.FullName ?? type.Name) +
                "\n" +
                name));
    }

    internal string Finish()
    {
        if (_finished)
        {
            throw new ObjectDisposedException(nameof(CanonicalHashWriter));
        }

        _finished = true;
        return HashEncoding.ToLowerHex(_hash.GetHashAndReset());
    }
    public void Dispose()
    {
        _finished = true;
        _hash.Dispose();
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
