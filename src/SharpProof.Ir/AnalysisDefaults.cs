namespace SharpProof.Ir;

// Convenience overloads without a caller token still need a finite deadline.
// Explicit cancellation-token overloads retain their caller-owned deadlines.
internal static class AnalysisDefaults
{
    internal const int OperationTimeoutMinutes = 30;
}
