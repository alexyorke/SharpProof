# SharpProof Semantics

This document defines the soundness boundary for SharpProof preview analysis. If an
implementation detail, optimization, diagnostic, or document conflicts with this
file, this file wins.

## Outcomes and evidence

SharpProof has three semantic outcomes:

- `Proven` means that the goal follows from lowered program facts, resolved API
  specifications, verified contracts, and any explicitly declared user
  assumptions.
- `Refuted` means that a validated counterexample or effect trace violates the
  goal.
- `Unknown` means that SharpProof cannot establish either result within its
  supported language, models, or resource limits.

Unsupported syntax, missing or ambiguous specifications, approximate facts,
budget exhaustion, solver timeout, and unsupported encoding produce
claim-level `Unknown`. They never produce `Proven` or `Refuted`. Backend
unavailability, infrastructure failure, malformed backend output, containment
failure, and failed counterexample replay also prevent a semantic result, but
protocol version 5 marks the whole run `Failed`; these conditions are fatal
under every build policy. Unsupported unannotated analyzer callables remain
silent. Explicitly selected unsupported callables produce SP0047.

Approximate facts cannot be promoted to assumptions. A proof is valid only when
its evidence core contains lowerings, resolved specifications, verified
contracts, or explicit user assumptions. A counterexample is valid only after
replay against the executable program model. Failed replay is an encoder defect,
not a program defect.

Caller cancellation remains cancellation. It is propagated by the analyzer and
becomes run status `Canceled`, not a semantic claim outcome. A project boundary
becomes run status `TimedOut`. Cancellation, timeouts, failures, budget
exhaustion, and all `Unknown` outcomes are not reusable proof-cache entries.

## Accountable selection and worker runs

Worker protocol version 5 separates `WorkerRunStatus` from
`WorkerClaimOutcome`. The compiler-symbol-based manifest is sealed before
verification. It contains every selected callable and every discovered
postcondition with a stable semantic claim ID, evidence kind, dense ordinal,
and mapped source location. A valid response has exact manifest/result
equality: no claim may be missing, duplicated, invented, or assigned to the
wrong callable.

Selection is relative to the compiler artifact's `WorkerFeatureSet`, which is
populated from `SharpProofFeatures`. `Contracts` includes contract annotations,
assumptions, and postcondition claims while excluding effect-only annotations.
`Effects` includes effect-selected callables while excluding postcondition
claims and contract assumptions. `All` is their union. Strict accountability
applies to everything selected by that feature set; disabled features are not
silently counted as analyzed. Because the current worker does not produce
effect-proof claims, an effect-selected callable has explicit incomplete
coverage rather than an empty success.

Every selected callable has explicit `Complete` or `Incomplete` coverage.
Every manifest claim has exactly one `Proven`, `Refuted`, or `Unknown` result.
The worker must never fabricate a clause-zero claim to describe a callable
failure. User assumptions and trusted boundaries have stable evidence IDs and
remain visible whether or not they enter an individual proof core.

A `Complete` run means the worker finished and produced a structurally valid
accounting response; it does not mean every claim was proven. `TimedOut`,
`Canceled`, and `Failed` are run states, not claim outcomes. The launcher's
verification and assumption policies decide how a valid complete response
affects the build, but cannot turn a failed run or refutation into success.

## Abstract-domain concretization

For every abstract value `a`, `gamma(a)` is the set of concrete values or
execution traces represented by `a`. Domain order is semantic inclusion:
`a <= b` only when `gamma(a)` is a subset of `gamma(b)`. `Bottom` represents
the empty set, `Top` represents every value in the domain, `Join` contains the
union of both operands, and `Widen` must contain both its previous value and its
next value. `Havoc(Bottom)` remains `Bottom`; every other havoc is `Top`.

The interval/congruence value `[lower, upper] mod m = r` represents every signed
64-bit integer within the optional bounds whose normalized remainder modulo
`m` is `r`. Modulus zero represents the exact singleton `r`; modulus one
imposes no congruence restriction.

The nullness domain represents `{}`, `{null}`, `{non-null references}`, and
their union. A sequence-cardinality value represents sequences whose
non-negative length belongs to its interval and whose emptiness agrees with
`Empty`, `NonEmpty`, or `Top`.

An effect summary represents all concrete traces whose reads, writes,
allocations, capabilities, escaping exception types, and termination behavior
are contained component-wise by the summary. An incomplete or uncertain
summary remains an over-approximation, but it is not eligible to establish an
absence-of-effect proof. Thus larger abstract values lose precision; they never
authorize a stronger result.

## Contracts and trust

Postconditions use partial correctness: they apply to normal returns. Divergence
does not itself violate a postcondition, observable purity, or `DoesNotThrow`.
A postcondition is established only when its bound C# expression is both
defined and true on every normal return; a possible exception while evaluating
the postcondition produces `Unknown`. Verification assumptions include the
lowered body's normal-completion condition, so throwing executions are not
mistaken for normal-return counterexamples.

`Contract.Assume` is explicit user evidence and must remain visible as
`Justification.UserAssumed`. A diagnostic suppression changes reporting only; it
cannot sharpen a summary or proof. A trust declaration can authorize only an
explicitly declared contract or effect summary. Trust without such a declaration
leaves the result `Unknown`. A complete external API specification or trusted
effect summary describes the whole observable call boundary, including any type
initialization caused by that call.

Compiler-elided `Contract.Requires`, `Contract.Ensures`, and `Contract.Assume`
calls do not evaluate their arguments. A direct runtime invocation of
`Contract.Result<T>()` or `Contract.Old<T>(...)` is invalid and may allocate and
throw `InvalidOperationException`; their effect specs describe that direct-call
behavior.

Callee postconditions may be assumed only after verification or explicit trust.

## Effects

Observable purity excludes:

- reads from or writes to ambient state;
- writes to pre-existing state reachable by the caller;
- I/O, synchronization, native code, reflection, and nondeterminism; and
- unresolved effects.

Fresh allocation and writes confined to fresh owned regions are compatible with
observable purity. They are not compatible with `[ZeroAllocations]`.

An operation that may throw and is not discharged by the analysis makes
`[DoesNotThrow]` unknown. This includes implicit exceptions from dereferences,
array and index access, division, casts, checked arithmetic, and similar runtime
operations. An unmodeled external call has unknown effects.

Object, collection, and array initializers are part of the creating expression.
Instance field, property, and event initializers are part of each explicit
instance-constructor summary. Static member initializers are part of an explicit
static-constructor summary. When a source static initializer or static
constructor can run at a method or instance-constructor boundary and its
one-time execution is not modeled there, the summary is `Unknown`.
Metadata static-field access has no callable summary that can cover type
initialization and therefore fails closed.

The analyzer's effect summary is a path-insensitive may analysis. A possible
allocation, disallowed capability, observable access, or disallowed exception
therefore makes the corresponding contract `Unknown`; it is not a validated
effect trace and cannot produce `Refuted`. The definitive SP0013, SP0015, and
SP0030 diagnostics are reserved until concrete effect-trace replay exists.

## Analyzer activation and language boundary

Analyzer behavior is selected through the compilation-global
`sharpproof_profile`/`SharpProofProfile` and
`sharpproof_features`/`SharpProofFeatures` options:

- `advisory` is the default profile. It analyzes selected contracts and keeps
  unsupported unannotated code quiet.
- `strict` requires the verifier, requires proof by default, and rejects
  user/trusted evidence by default. Explicitly disabling verification is a
  configuration error.
- `off` constructs no analysis session, contributes no analyzer/generator
  items through the package, and does not run verification.
- feature value `effects` enables effect contracts, `contracts` enables
  call-site contract analysis, and `all` (the default) enables both. The
  package carries the same selection into the compiler artifact and its manifest.

Feature and proof diagnostics are enabled informational diagnostics by default.
Configuration and contract-usage errors remain enabled at their declared
warning/error severity. `SharpProofMode`/`sharpproof_mode` and
`all-experimental` are deprecated preview compatibility aliases and do not
define the release interface.

A feature diagnostic may be promoted to `Warning` only after at least four
consecutive weekly corpus cycles with no confirmed false positive, no
unexplained canonical snapshot change, and all soundness and performance gates
green. Promotion changes reporting severity only; it cannot enlarge the
supported subset or proof semantics.

The current effect subset accepts non-generic ordinary methods, explicit
constructors, and accessors using locals, primitive expressions, assignments,
direct calls, object and array creation, `if`, `for`, `while`, `do`, constant
`switch`, `try`/`catch`/`finally`, `using`, `lock`, conditional access, and
ordinary interpolation.

It rejects async and iterator bodies, `foreach`, closures, local functions,
delegates, ref parameters or locals, ref returns, ref-like types, open type
parameters, dynamic binding, unsafe and pointer constructs, function pointers,
patterns, deconstruction, queries, `with`, ranges, implicit indexers, custom
interpolated-string handlers, inline arrays, collection expressions and spread,
and primary constructors. A closed constructed generic API call is accepted only
when a specification resolves for that exact call. Every Roslyn `OperationKind`
is classified by a checked-in decision table; an unknown future kind is rejected.

The packaged verifier consumes compiler artifact schema version 3 produced
from the final post-generator compilation. The artifact contains the sealed
feature-selected manifest and, for every selected callable, either a typed
lowering failure or portable whole-body CFG/IR with bound clauses, canonical
variables, body-entry state, parameter mappings, and bound API-spec witness
metadata. It also carries compiler error diagnostics and mapped locations,
handwritten and generated tree hashes and parse evidence, a bounded
proof-relevant compilation-option set, assembly and target identity, and
compiler/reference provenance. It contains no source text.

Before cache lookup or backend creation, the worker validates the artifact
digest and canonical shape, requires the compiler-visible maximum expression
depth to equal the request budget, and requires exact manifest/lowered-callable
equality, including claim ownership and declared assumptions. It hydrates
portable IR without constructing a Roslyn compilation, reparsing source, or
rereading reference files. Compiler versions and MVIDs and reference
paths/hashes/identities/aliases are provenance, not a runtime compatibility
gate.

Artifact collection rejects resolver-dependent `#r`/`#load`, missing-assembly
resolver mode, reference supersession, custom assembly-identity comparers, and
non-file or unreadable references. `AdditionalFiles` are represented by
canonical paths and content hashes without embedding their raw contents.
Analyzer configuration is represented by its observable effects on the final
compilation and effective SharpProof options. Compiler error diagnostics fail
verification as `CompilationFailure`; malformed lowered evidence or an
expression-depth mismatch fails as `CompilerManifestMismatch`.

This closed artifact removes worker-side compiler reconstruction, but it does
not by itself establish independent counterexample replay. Current replay
still evaluates the lowered obligation path rather than an independent
interpreter over the exact whole-body CFG. A SAT result that depends on a
spec-modeled call result becomes `Unknown` with
`CounterexampleReplayFailed`, never `Refuted`. SARIF projection is likewise not
implemented.
