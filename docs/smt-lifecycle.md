# SMT lifecycle

SMT verification is out of process. The IDE analyzer never creates a Z3
context.

The packaged worker lifecycle is currently supported and exercised only on
Windows x64. Package-consumer CI restores the exact same three-package
artifacts and exercises portable analyzer consumption on Windows x64, Linux
x64, macOS x64, and macOS ARM64. Every unsupported matrix host also asserts
that requested verification is rejected explicitly. Windows ARM64 and
non-Windows worker execution remain unsupported and are not exercised.

Each `SharpProof.Worker` process serves one bounded project request and owns
one isolated Z3 context per configured solver lane. Queries use Z3 resource
limits rather than wall-clock solver timeouts, canonical variable names,
stable formula ordering, typed models, and typed unsat cores. Cancellation
interrupts the active backend and remains cancellation. A lane interrupted by
a method timeout is disposed and recreated before it can accept another
query; a lane without a backend factory is retired instead.

A SAT result becomes `Refuted` only after two replay layers. First, the proof
kernel requires the extracted assignments to close exactly over every
requested Boolean/integer model variable, re-evaluates all lowered assumptions
as true, and re-evaluates the lowered goal as false. Second, the worker seeds
and independently executes the compiler-produced whole-body program along the
model-selected concrete CFG path, reconstructs the post-state, and requires the
original `Ensures` condition to evaluate to false. Contract-only ordinary
`void` methods are exact zero-step replays. Constructor postconditions abstain
as `UnsupportedBody` until base-constructor and field-initializer semantics are
lowered. Only canonical user-model variables are exposed in the result;
lowered temporaries remain internal.

An executed spec-modeled call cannot be independently replayed, so the
candidate is reported as claim `Unknown` with
`CounterexampleNotReplayable`; an operation on an unselected CFG path does not
block replay. Other unsupported or inconsistent replay state remains the
fatal `CounterexampleReplayFailed` discrepancy. An UNSAT result becomes
`Proven` only when every core item has admissible justification. Unsupported
encoding, resource limits, and method boundaries produce typed claim-level
`Unknown` results. An undefined postcondition is also a typed `Unknown`
result. Backend unavailability, malformed backend results, failure to replay
an otherwise replayable counterexample, containment failure, and
infrastructure failure make the protocol version 9 run `Failed` and fail the
build under every policy.
Project timeout and caller cancellation use the separate `TimedOut` and
`Canceled` run statuses.

`SharpProofVerifyPolicy` controls whether otherwise valid incomplete selected
analysis is informational, warning, or error SP0047 output.
`SharpProofAssumptionPolicy` similarly controls SP0048 for declared user or
trusted evidence. Neither policy changes solver semantics or converts a failed
run into success.

Only exact-manifest, complete `Proven` and replay-validated `Refuted` project
results enter the content-addressed disk cache. Cache keys include protocol,
semantics, tool identity and canonical packaged worker runtime-closure digest,
target framework, the exact
closed compiler artifact and lowered IR, budgets, spec versions, and a
canonical digest of the complete trusted spec content. Cache schema version 10
revalidates the stored semantic payload against the complete current manifest.
Strict `require-proven` runs do not consume or write this local semantic cache.

See [Typed abstention reasons](unknown-reasons.md) for exact statuses and
reasons, and [Analysis limits](analysis-limits.md) for configured and fixed
bounds.
