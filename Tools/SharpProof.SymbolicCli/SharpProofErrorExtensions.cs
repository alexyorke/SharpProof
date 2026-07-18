using SharpProof.Symbolic;

internal static class SharpProofErrorExtensions
{
    internal static SymbolicError ToSymbolicError(this SharpProofError error)
    {
        if (error == null) throw new ArgumentNullException(nameof(error));
        return new SymbolicError(
            error.Code,
            Enum.TryParse<SymbolicErrorCategory>(error.Category, out var category)
                ? category
                : SymbolicErrorCategory.Internal,
            error.Message,
            error.RecommendedExitCode,
            error.IsRetryable,
            error.Details);
    }
}
