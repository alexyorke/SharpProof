namespace SharpProof.Tools.Fuzz;
internal sealed record AnalyzerRunResult(ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<string> Exceptions);
