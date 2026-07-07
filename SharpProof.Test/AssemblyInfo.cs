using NUnit.Framework;

// Fixture-level only: many tests assert SMT proof outcomes that run under wall-clock
// budgets (SmtAnalysisOptions.Default); method-level parallelism saturates the machine
// and makes those proofs time out nondeterministically.
[assembly: Parallelizable(ParallelScope.Fixtures)]
