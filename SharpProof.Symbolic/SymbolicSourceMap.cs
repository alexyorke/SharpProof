namespace SharpProof.Symbolic;

internal sealed class SymbolicSourceMap(
    string sourceUri,
    int originalStartLine = 1,
    int originalStartColumn = 1)
{
    public string SourceUri { get; } = string.IsNullOrWhiteSpace(sourceUri)
        ? throw new ArgumentException("Source URI is required.", nameof(sourceUri))
        : sourceUri.Trim();

    public int OriginalStartLine { get; } = originalStartLine >= 1
        ? originalStartLine
        : throw new ArgumentOutOfRangeException(
            nameof(originalStartLine), originalStartLine, "Original start line must be 1 or greater.");

    public int OriginalStartColumn { get; } = originalStartColumn >= 1
        ? originalStartColumn
        : throw new ArgumentOutOfRangeException(
            nameof(originalStartColumn), originalStartColumn, "Original start column must be 1 or greater.");
}
