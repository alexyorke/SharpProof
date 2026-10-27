namespace SharpProof.Gates.Corpus;

// Corpus execution and evidence presentation policies have different units.
// Keep their names distinct even when values happen to match another budget.
internal static class CorpusExecutionDefaults
{
    internal const int MaximumWallTimeSeconds = 300;
    internal const int MaximumReportedDiagnostics = 25;
    internal const int MaximumEmissionDiagnostics = 5;
    internal const int MaximumWorkerDiagnosticItems = 8;
    internal const int MaximumDiagnosticIdentityCharacters = 512;
    internal const int MaximumAllocationOracleBodyBytes = 65_536;
    internal const int AllocationWarmupInvocations = 3;
    internal const int AllocationMeasuredInvocations = 32;
}
