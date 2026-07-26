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
budget exhaustion, solver timeout, encoding failure, native failure, and
malformed models produce `Unknown`. They never produce `Proven` or `Refuted`.
Analyzer callables outside the enabled language subset abstain silently.

Approximate facts cannot be promoted to assumptions. A proof is valid only when
its evidence core contains lowerings, resolved specifications, verified
contracts, or explicit user assumptions. A counterexample is valid only after
replay against the executable program model. Failed replay is an encoder defect,
not a program defect.

Caller cancellation remains cancellation. It is propagated by the analyzer and
does not become a semantic outcome. Cancellation, timeouts, native failures,
encoding failures, malformed models, budget exhaustion, and all `Unknown`
outcomes are not reusable proof-cache entries.

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

Analyzer features are opt-in through the compilation-global `sharpproof_mode`
option (or MSBuild `SharpProofMode` property):

- `off` (the default) constructs no analysis session and runs no feature
  pipeline.
- `effects` enables effect-contract analysis.
- `contracts` enables experimental contract analysis.
- `all-experimental` enables both groups.

Feature and proof diagnostics are informational and disabled by default. A host
must opt into both a mode and the desired diagnostic IDs. Configuration and
contract-usage errors remain enabled.

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
