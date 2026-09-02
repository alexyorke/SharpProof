using System.Security.Cryptography;

#if SHARPPROOF_WORKER_PROTOCOL
namespace SharpProof.Worker.Protocol;

internal static class ProtocolHashEncoding
#else
namespace SharpProof.Ir;

internal static class HashEncoding
#endif
{
    internal static string ToLowerHex(IEnumerable<byte> bytes)
    {
        return string.Concat(bytes.Select(static value => value.ToString(
            "x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    internal static string ComputeSha256Hex(byte[] bytes)
    {
        using var hash = SHA256.Create();
        return ToLowerHex(hash.ComputeHash(bytes));
    }
}
