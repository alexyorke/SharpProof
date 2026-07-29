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
        return Hex(algorithm.ComputeHash(stream));
    }

    private static string Bytes(byte[] value)
    {
        using var algorithm = SHA256.Create();
        return Hex(algorithm.ComputeHash(value));
    }

    private static string Hex(byte[] value)
    {
        var result = new StringBuilder(value.Length * 2);
        foreach (var item in value)
        {
            result.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }

        return result.ToString();
    }
}
