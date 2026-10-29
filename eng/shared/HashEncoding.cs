using System.Collections.Immutable;
using System.Security.Cryptography;

#if SHARPPROOF_WORKER_PROTOCOL
namespace SharpProof.Worker.Protocol;

internal static class ProtocolHashEncoding
#else
namespace SharpProof.Ir;

internal static class HashEncoding
#endif
{
    private const string LowerHexDigits = "0123456789abcdef";

    // Digests are formatted on hot paths, so this writes two characters per
    // byte into one buffer instead of a string per byte.
    internal static string ToLowerHex(IEnumerable<byte> bytes)
    {
        bytes = RepositoryParameterGuard.NotNull(bytes, nameof(bytes));
        var values = bytes as byte[] ?? bytes.ToArray();
        var characters = new char[values.Length * 2];
        for (var index = 0; index < values.Length; index++)
        {
            characters[2 * index] = LowerHexDigits[values[index] >> 4];
            characters[(2 * index) + 1] = LowerHexDigits[values[index] & 0xF];
        }
        return new string(characters);
    }

    internal static string ComputeSha256Hex(ImmutableArray<byte> bytes)
    {
        RepositoryParameterGuard.ThrowIfMissing(bytes.IsDefault, nameof(bytes));
        using var hash = SHA256.Create();
        return ToLowerHex(hash.ComputeHash(bytes.ToArray()));
    }

    internal static string ComputeSha256Hex(Stream stream)
    {
        stream = RepositoryParameterGuard.NotNull(stream, nameof(stream));
        using var hash = SHA256.Create();
        return ToLowerHex(hash.ComputeHash(stream));
    }

    internal static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(
            static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
