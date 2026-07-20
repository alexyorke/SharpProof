internal static class EffectSummaryHash {
    internal static string FileSha256(string path) {
        using var stream = File.OpenRead(path);
        return LowerHex(SHA256.HashData(stream));
    }

    internal static string Sha256(byte[] bytes) =>
        LowerHex(SHA256.HashData(bytes));

    internal static string Sha256(string text) =>
        Sha256(Encoding.UTF8.GetBytes(text));

    private static string LowerHex(byte[] bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();
}
