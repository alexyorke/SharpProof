# SMT lifecycle

SMT verification is out of process. The IDE analyzer never creates a Z3
context.

Each `SharpProof.Worker` process owns one Z3 context and serves a bounded
project request. Queries use Z3 resource limits rather than wall-clock solver
timeouts, canonical variable names, stable formula ordering, typed models, and
typed unsat cores. Cancellation interrupts the backend and remains
cancellation.

A SAT result becomes `Refuted` only when the extracted assignments replay
through the executable IR and falsify the goal. An UNSAT result becomes
`Proven` only when every core item has admissible justification. Backend
unavailability, malformed results, replay failure, resource limits, and outer
worker timeouts return `Unknown` or fail the build command closed.

Only complete `Proven` and replay-validated `Refuted` project results enter the
content-addressed disk cache. Cache keys include protocol, semantics, tool,
target framework, compilation inputs, references, options, and spec versions.
