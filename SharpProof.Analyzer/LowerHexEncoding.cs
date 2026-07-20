namespace SharpProof.Analyzer;

internal static class LowerHexEncoding
{
    internal static string Encode(byte[] bytes)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));

        var chars = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var value = bytes[i];
            chars[i * 2] = ToHexChar(value >> 4);
            chars[i * 2 + 1] = ToHexChar(value & 0x0F);
        }

        return new string(chars);
    }

    private static char ToHexChar(int value) =>
        (char)(value < 10 ? '0' + value : 'a' + (value - 10));
}
