namespace SharpProof.Symbolic;

internal sealed class SymbolicSourceMap
{
    public SymbolicSourceMap(
        string sourceUri,
        int originalStartLine = 1,
        int originalStartColumn = 1)
    {
        if (string.IsNullOrWhiteSpace(sourceUri))
            throw new ArgumentException("Source URI is required.", nameof(sourceUri));

        if (originalStartLine < 1)
            throw new ArgumentOutOfRangeException(
                nameof(originalStartLine),
                originalStartLine,
                "Original start line must be 1 or greater.");

        if (originalStartColumn < 1)
            throw new ArgumentOutOfRangeException(
                nameof(originalStartColumn),
                originalStartColumn,
                "Original start column must be 1 or greater.");

        SourceUri = sourceUri.Trim();
        OriginalStartLine = originalStartLine;
        OriginalStartColumn = originalStartColumn;
    }

    public string SourceUri { get; }

    public int OriginalStartLine { get; }

    public int OriginalStartColumn { get; }
}
