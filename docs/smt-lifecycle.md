# SMT lifecycle

SMT verification is out of process. The IDE analyzer never creates a Z3
context.

The packaged worker lifecycle is currently supported and exercised only on
Windows x64. Linux x64 and macOS Intel CI exercise analyzer-only package
consumption; Windows arm64 and non-Windows packaged worker lifecycles are not
validated.

Each `SharpProof.Worker` process owns one Z3 context and serves a bounded
project request. Queries use Z3 resource limits rather than wall-clock solver
timeouts, canonical variable names, stable formula ordering, typed models, and
typed unsat cores. Cancellation interrupts the backend and remains
cancellation.

A SAT result becomes `Refuted` only when the extracted assignments replay
through the executable IR and falsify the goal. An UNSAT result becomes
`Proven` only when every core item has admissible justification. Backend
unavailability, malformed backend results, replay failure, resource limits,
and method/project budgets produce typed `Unknown` records. An ordinary
`Unknown` record does not fail the build. Invalid protocol responses, worker
errors, containment failure, and hard launcher termination do fail the build
command.

Only complete `Proven` and replay-validated `Refuted` project results enter the
content-addressed disk cache. Cache keys include protocol, semantics, tool,
target framework, compilation inputs, references, options, and spec versions.

See [Typed abstention reasons](unknown-reasons.md) for exact statuses and
reasons, and [Analysis limits](analysis-limits.md) for configured and fixed
bounds.
