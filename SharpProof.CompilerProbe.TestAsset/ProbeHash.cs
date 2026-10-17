namespace SharpProof.CompilerProbe.TestAsset;

internal static class ProbeHash
{
    internal static string Text(string value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return Bytes(Encoding.UTF8.GetBytes(value));
    }

    internal static string File(string path)
    {
        path = RepositoryParameterGuard.NotNull(path, nameof(path));
        using var stream = System.IO.File.OpenRead(path);
        return SharpProof.Ir.HashEncoding.ComputeSha256Hex(stream);
    }

    internal static string Bytes(byte[] value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return SharpProof.Ir.HashEncoding.ComputeSha256Hex(value);
    }
}
