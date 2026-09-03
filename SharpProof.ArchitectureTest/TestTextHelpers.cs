namespace SharpProof.ArchitectureTest;

internal static class TestTextHelpers
{
    internal static int CountOrdinal(string value, string needle)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(
                   needle,
                   offset,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }

        return count;
    }
}
