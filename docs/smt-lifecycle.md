# SMT lifecycle

SMT verification is out of process. The IDE analyzer never creates a Z3
context.

The packaged worker lifecycle is supported and exercised only in the canonical
SharpProof Linux amd64 container. Package-consumer CI restores the exact same
three-package artifacts and exercises every declared target framework inside
that container. The analyzer packages remain operating-system-neutral. Native
host execution and ARM64 verifier containers are unsupported.

The MSBuild task validates the container contract and runtime closure, starts
one worker process, and terminates its process tree on cancellation or timeout.
The launcher entry point runs inside that worker. Docker owns CPU and memory
isolation. The project deadline includes artifact preparation.
Each `SharpProof.Worker` process serves one bounded project request and owns
one isolated Z3 context per configured solver lane. Queries use Z3 resource
limits rather than wall-clock solver timeouts, canonical variable names,
stable formula ordering, typed models, and typed unsat cores. Cancellation
interrupts the active backend and remains cancellation. A lane interrupted by
a method timeout is disposed and recreated before it can accept another
query; a lane without a backend factory is retired instead.

A SAT result becomes `Refuted` only after proof-kernel replay. The proof
kernel requires the extracted assignments to close exactly over every
requested model variable, re-evaluates all lowered assumptions
as true, and re-evaluates the lowered goal as false. For callables, it also seeds
and independently executes the compiler-produced whole-body program along the
model-selected concrete CFG path, reconstructs the post-state, and requires the
original `Ensures` condition to evaluate to false. Contract-only ordinary
`void` methods are exact zero-step replays. Constructor postconditions abstain
as `UnsupportedBody` until base-constructor and field-initializer semantics are
lowered. Only canonical user-model variables are exposed in the result;
lowered temporaries remain internal.

The separate Total candidate additionally accepts canonical object and string
types and arrays of Boolean, integer, object or string elements. Its native
model decoder preserves reference identity and length. The original replay
checks still apply; the legacy Boolean/integer model boundary is unchanged.

An executed API-spec or relational-summary call cannot be independently
replayed, so the candidate is reported as claim `Unknown` with
`CounterexampleNotReplayable`; an operation on an unselected CFG path does not
block replay. Other unsupported or inconsistent replay state remains the
fatal `CounterexampleReplayFailed` discrepancy. An UNSAT result becomes
`Proven` only when every core item has admissible justification. Unsupported
encoding, resource limits, and method boundaries produce typed claim-level
`Unknown` results. An undefined postcondition is also a typed `Unknown`
result. Solver-reported theory incompleteness produces
`Unknown(SolverIncomplete)` while leaving the worker run complete. Backend
unavailability, malformed backend results, failure to replay
an otherwise replayable counterexample, containment failure, and
infrastructure failure make the protocol version 13 run `Failed` and fail the
build under every policy.
Project timeout and caller cancellation use the separate `TimedOut` and
`Canceled` run statuses.

Effect refutation replay is independent of this SMT lifecycle. Compiler
artifact schema 21 admits unconditional definite managed object/array
allocation, exact framework explicit-throw, empty-`lock`, and exact-`Monitor`
events. The artifact boundary validates event shape, and a worker interpreter
derives effects, capabilities, and exact exception hierarchy, evaluates the
selected constraint, and matches the witness. Unsupported definite candidates
become CounterexampleNotReplayable; semantic replay disagreement becomes
CounterexampleReplayFailed. Complete effect responses are cacheable.
`SharpProofVerifyPolicy` controls whether otherwise valid incomplete selected
analysis is informational, warning, or error SP0047 output.
`SharpProofAssumptionPolicy` similarly controls SP0048 for declared user or
trusted evidence. Neither policy changes solver semantics or converts a failed
run into success.

Cache schema 15 stores every exact-manifest, valid complete response, including
Proven, replay-validated Refuted, semantic Unknown, effects, and empty claims.
The key includes the full artifact digest, worker/Z3/API-spec identities, and
semantic budgets. Timeouts, cancellation, malformed results, and backend or
infrastructure failures remain noncacheable. Cache reads validate response
binding and payload shape without rerunning proof or concrete replay.
See [Typed abstention reasons](unknown-reasons.md) for exact statuses and
reasons, and [Analysis limits](analysis-limits.md) for configured and fixed
bounds.
