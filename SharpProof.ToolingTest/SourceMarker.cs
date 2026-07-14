namespace SharpProof.Test;

internal static class SourceMarker
{
    public static int FindLine(string source, string marker)
    {
        var position = source.IndexOf(marker, StringComparison.Ordinal);
        if (position < 0) throw new InvalidOperationException("Marker was not found in source.");

        return source[..position].Count(static character => character == '\n') + 1;
    }
}
