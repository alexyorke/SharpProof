# SMT lifecycle

SMT verification is out of process. The IDE analyzer never creates a Z3
context.

The packaged worker lifecycle is supported and exercised only in the canonical
SharpProof Linux amd64 container. Package-consumer CI restores the exact same
three-package artifacts and exercises every declared target framework inside
that container. The analyzer packages remain operating-system-neutral. Native
host execution and ARM64 verifier containers are unsupported.

The launcher validates the container contract and exact runtime closure before
starting one direct child worker. An exact stdin message releases the startup
barrier. Cancellation sends a graceful termination signal and then forces
termination within one monotonic deadline; a Linux parent-death signal prevents
launcher loss from leaving the worker alive. Docker, rather than SharpProof,
owns the hard CPU and memory boundary.

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

An executed API-spec or relational-summary call cannot be independently
replayed, so the candidate is reported as claim `Unknown` with
`CounterexampleNotReplayable`; an operation on an unselected CFG path does not
block replay. Other unsupported or inconsistent replay state remains the
fatal `CounterexampleReplayFailed` discrepancy. An UNSAT result becomes
`Proven` only when every core item has admissible justification. Unsupported
encoding, resource limits, and method boundaries produce typed claim-level
`Unknown` results. An undefined postcondition is also a typed `Unknown`
result. Backend unavailability, malformed backend results, failure to replay
an otherwise replayable counterexample, containment failure, and
infrastructure failure make the protocol version 11 run `Failed` and fail the
build under every policy.
Project timeout and caller cancellation use the separate `TimedOut` and
`Canceled` run statuses.

Effect refutation replay is independent of this SMT lifecycle. Compiler
artifact schema 12 retains schema 10's unconditional definite managed object/array
allocation event. A worker-owned interpreter validates the event identity,
source-tree span, selected constraint, and sealed witness, then derives
`Allocates` without trusting compiler effect bits or executing user code. That
evidence can refute `ZeroAllocations` or an `EffectContract` excluding
`Allocates`; observable `EnforcePure` permits fresh allocation. Unsupported
definite effect candidates become `CounterexampleNotReplayable`, while an
otherwise valid semantic replay disagreement becomes the fatal
`CounterexampleReplayFailed`. Effect results remain outside cache schema 13.
Protocol version 11 carries relational-summary evidence.

`SharpProofVerifyPolicy` controls whether otherwise valid incomplete selected
analysis is informational, warning, or error SP0047 output.
`SharpProofAssumptionPolicy` similarly controls SP0048 for declared user or
trusted evidence. Neither policy changes solver semantics or converts a failed
run into success.

Only exact-manifest, complete, postcondition-only project results whose claims
are all replay-validated `Refuted` enter the content-addressed disk cache.
Cache keys include protocol, semantics, tool identity and canonical packaged
worker runtime-closure digest, target framework, the exact closed compiler
artifact and lowered IR, budgets, spec versions, and a canonical digest of the
complete trusted spec content. Cache schema version 13 revalidates the stored
payload against the complete current manifest, reconstructs each supported
scalar model, checks entry assumptions and source ranges, and repeats
whole-body replay. Proven claims, effect claims, and unsupported models are not
cached. Strict `require-proven` runs do not consume or write this local
semantic cache.

See [Typed abstention reasons](unknown-reasons.md) for exact statuses and
reasons, and [Analysis limits](analysis-limits.md) for configured and fixed
bounds.
