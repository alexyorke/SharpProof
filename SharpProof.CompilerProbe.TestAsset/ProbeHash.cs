namespace SharpProof.CompilerProbe.TestAsset;

internal static class ProbeHash
{
    internal static string Text(string value)
    {
        return Bytes(Encoding.UTF8.GetBytes(value));
    }

    internal static string File(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return SharpProof.Ir.HashEncoding.ToLowerHex(
            algorithm.ComputeHash(stream));
    }

    internal static string Bytes(byte[] value)
    {
        return SharpProof.Ir.HashEncoding.ComputeSha256Hex(value);
    }
}
