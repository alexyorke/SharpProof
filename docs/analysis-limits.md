# Analysis limits

The checked-in v2 acceptance contract is
`eng/acceptance/v2/contract.json`. Its shipping defaults are:

- at most 4 worker processes and 2 GiB for the worker Job Object;
- Z3 query rlimit 3,000,000 and method rlimit 20,000,000;
- 10 seconds per method and 300 seconds per project as outer fail-closed
  boundaries;
- expression depth 64;
- a 512 MiB content-addressed cache;
- 250 ms cancellation p95 and 1 second forced termination;
- 1,000 deterministic fuzz cases for pull requests and 10,000 nightly.

The analyzer remains default-off and does not create an analysis session in
that mode. Performance gates use 5 warmups, 30 samples, and 200 simulated IDE
edits. Unknown rate is recorded as a metric, never a release gate.

No budget outcome is promoted to `Proven` or `Refuted`. Only terminal hygienic
proofs and replay-validated refutations are cacheable.
