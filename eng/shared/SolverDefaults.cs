namespace SharpProof.Configuration;

// Source-linked into the portable solver and worker protocol so neither
// assembly needs a new runtime dependency to share the default resource budget.
internal static class SolverDefaults
{
    internal const uint QueryRlimit = 3_000_000;
}
