using System.Security.Cryptography;
namespace SharpProof.Ir;
internal sealed class CanonicalHashWriter : IDisposable {
    private readonly IncrementalHash _hash =
        IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _finished;
    internal CanonicalHashWriter Add(string? value) => Add(Encoding.UTF8.GetBytes(value ?? string.Empty));
    internal CanonicalHashWriter Add(bool value) => Add(value ? "true" : "false");
    internal CanonicalHashWriter Add(int value) => Add(value.ToString(CultureInfo.InvariantCulture));
    internal CanonicalHashWriter Add(long value) => Add(value.ToString(CultureInfo.InvariantCulture));
    internal CanonicalHashWriter Add(byte[] bytes) {
        if (_finished) throw new ObjectDisposedException(nameof(CanonicalHashWriter));
        var length = bytes.Length;
        _hash.AppendData([
            (byte)length, (byte)(length >> 8),
            (byte)(length >> 16), (byte)(length >> 24)
        ]);
        _hash.AppendData(bytes);
        return this;
    }
    internal CanonicalHashWriter Add(params object?[] values) {
        foreach (var value in values)
            _ = value is byte[] bytes ? Add(bytes) : Add(value is bool boolean
                ? boolean ? "true" : "false"
                : Convert.ToString(value, CultureInfo.InvariantCulture));
        return this;
    }
    internal string Finish() {
        if (_finished) throw new ObjectDisposedException(nameof(CanonicalHashWriter));
        _finished = true;
        return string.Concat(_hash.GetHashAndReset().Select(static value =>
            value.ToString("x2", CultureInfo.InvariantCulture)));
    }
    public void Dispose() {
        _finished = true;
        _hash.Dispose();
    }
}
