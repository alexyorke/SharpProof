namespace SharpProof.CompilerProbe.TestAsset;

internal static class ProbeHash
{
    internal static string Text(string value)
    {
        if (value is null)
        {
            throw new System.ArgumentNullException(nameof(value));
        }
        return Bytes(Encoding.UTF8.GetBytes(value));
    }

    internal static string File(string path)
    {
        if (path is null)
        {
            throw new System.ArgumentNullException(nameof(path));
        }
        using var stream = System.IO.File.OpenRead(path);
        return SharpProof.Ir.HashEncoding.ComputeSha256Hex(stream);
    }

    internal static string Bytes(byte[] value)
    {
        if (value is null)
        {
            throw new System.ArgumentNullException(nameof(value));
        }
        return SharpProof.Ir.HashEncoding.ComputeSha256Hex(value);
    }
}
