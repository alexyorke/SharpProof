using NUnit.Framework;

// Fixture-level parallelism is the measured sweet spot: proof outcomes are
// load-independent since SMT budgets moved to deterministic Z3 rlimit units
// (SearchLib.Smt.SmtResourceBudget), but concurrent Z3 solving scales poorly in
// the native solver, so method-level parallelism (ParallelScope.All) floods the
// workers with Z3 work and makes the suite slower, not faster.
[assembly: Parallelizable(ParallelScope.Fixtures)]