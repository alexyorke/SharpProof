# SMT lifecycle

SMT verification is out of process. The IDE analyzer never creates a Z3
context.

The packaged worker lifecycle is currently supported and exercised only on
Windows x64. Linux x64, macOS x64, and macOS ARM64 CI exercise analyzer-only package
consumption; Windows arm64 and non-Windows packaged worker lifecycles are not
validated.

Each `SharpProof.Worker` process owns one Z3 context and serves a bounded
project request. Queries use Z3 resource limits rather than wall-clock solver
timeouts, canonical variable names, stable formula ordering, typed models, and
typed unsat cores. Cancellation interrupts the backend and remains
cancellation.

A SAT result becomes `Refuted` only when the extracted assignments replay
through the executable IR and falsify the goal. An UNSAT result becomes
`Proven` only when every core item has admissible justification. Unsupported
encoding, resource limits, and method boundaries produce typed claim-level
`Unknown` results. Backend unavailability, malformed backend results, replay
failure, containment failure, and infrastructure failure make the protocol
version 5 run `Failed` and fail the build under every policy. Project timeout
and caller cancellation use the separate `TimedOut` and `Canceled` run
statuses.

The current replay executes the lowered obligation path. Independent
whole-body replay over the exact compiler CFG remains a 1.0 release gate.
A SAT result whose model depends on a spec-modeled call result is therefore
reported as claim `Unknown` with `CounterexampleReplayFailed`, which makes the
run fail; it is never emitted as `Refuted`.

`SharpProofVerifyPolicy` controls whether otherwise valid incomplete selected
analysis is informational, warning, or error SP0047 output.
`SharpProofAssumptionPolicy` similarly controls SP0048 for declared user or
trusted evidence. Neither policy changes solver semantics or converts a failed
run into success.

Only exact-manifest, complete `Proven` and replay-validated `Refuted` project
results enter the content-addressed disk cache. Cache keys include protocol,
semantics, tool, target framework, the exact closed compiler artifact and
lowered IR, budgets, and spec versions. Cache schema version 5 revalidates the
stored semantic payload against the complete current manifest.

See [Typed abstention reasons](unknown-reasons.md) for exact statuses and
reasons, and [Analysis limits](analysis-limits.md) for configured and fixed
bounds.
