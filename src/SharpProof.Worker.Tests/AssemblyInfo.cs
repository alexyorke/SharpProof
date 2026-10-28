using NUnit.Framework;

// Fixtures run in parallel; tests within a fixture stay sequential. Fixtures
// that change process-wide state (environment variables, console streams,
// child processes) opt out with [NonParallelizable].
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(4)]
