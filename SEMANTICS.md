# SharpProof semantics

This document defines how to interpret the current verifier's results. The implementation authorities are the typed IR, compiler artifact, worker protocol validators, native VC checks, and replay code. The portable analyzer provides a separate, conservative analysis surface.

## Outcomes and evidence

| Claim outcome | Meaning |
| --- | --- |
| `Proven` | The modeled obligation holds under its recorded assumptions and entry domain |
| `Refuted` | A violating execution or effect site has been validated by concrete replay |
| `Unknown` | The claim could not be established or refuted within the supported model and limits |

An SMT satisfiable result alone is not a refutation. A solver failure, unsupported operation, invalid result, or failed replay cannot be promoted to success. Proofs are about the modeled executions and claim, not arbitrary program behavior.

Worker run status, callable coverage, and claim outcome are distinct. A complete run may contain Unknown claims. Every selected callable and claim must be accounted for by the manifest and response; missing results are protocol failures. Diagnostic silence, suppression, and a successful ordinary C# build are not proof evidence.

## Contracts and trust

Direct `Requires`, `Ensures`, and `Assume` calls form a contiguous prologue. They are compiler-bound to the verified Attributes API. A source lookalike or an unverified assembly contributes no contract evidence.

`Requires` restricts a callee's entry domain and creates obligations at modeled calls. `Ensures` applies on normal return. `Result<T>` names that return value and `Old<T>` names entry state. `Assume` adds explicit user evidence; it is not independently proven.

Direct and companion clauses are alternative effective sources. Valid direct clauses take precedence for the member; a valid companion supplies clauses when there are no valid direct clauses. Invalid or misplaced clauses remain usage errors. Compiler-elided clause invocations, including their argument evaluation, are omitted from executable body semantics.

The `SHARPPROOF_CONTRACTS` symbol is unsupported. SharpProof supplies no runtime enforcement. See [public API](docs/public-api.md) for exact attribute and intrinsic rules.

Closed parameter and return attributes supply validated non-null, positive, or inclusive-range conditions. Invalid targets, unsupported types, and invalid values do not create facts.

Trusted boundaries and resolved API specifications are assumptions with provenance. `SharpProofTrusted` alone does not invent a complete effect summary. External complete effect declarations require the reviewed precondition-free certification accepted by the metadata reader. Reporting suppression changes no evidence. Assumption policy governs declared user assumptions and trusted boundaries; it does not reject every recorded precondition or API specification.

## Entry feasibility and vacuity

The worker checks entry feasibility separately from a body claim. Contradictory preconditions are recorded as vacuous entry evidence. Body facts, arbitrary user assumptions, and API results must not be reported as contradictory preconditions. Proofs that depend on user assumptions remain conditional on that declared evidence.

Postconditions constrain normal returns. A body with no modeled normal return may satisfy a postcondition vacuously; the response records `NoModeledNormalReturn`. This does not establish termination or exception freedom. Effect claims have their own obligations.

Input domains include modeled CLR constraints. An instance receiver is non-null. Closed sealed reference roots of incompatible runtime types cannot denote the same non-null object. Nullable inputs and compatible aliases remain possible.

## Typed execution model

Roslyn operations and CFG are lowered to typed Total IR. Integral operations carry their exact admitted width, signedness, conversion behavior, and checked/unchecked semantics. Normal and exceptional paths remain distinct. Unsupported numeric and expression forms abstain rather than borrowing an unrelated representation.

Passive scalar SSA constraints preserve instruction ownership, predecessor edge guards, and source locations. Branch conditions restrict the corresponding paths. Multiple returns and exceptional exits are joined under their reach conditions.

Heap reasoning tracks modeled entry contents and ordered stores under path reach. A matching latest store determines a later read. Calls or writes outside the precise store model may forget contents, leaving approximated values. An element write does not implicitly change a field. Exact instance-field value reasoning excludes fields declared on explicit-layout types, including readonly fields.

Approximation values can support sound over-approximate proof obligations only within the checks implemented by the worker. They cannot be treated as independently executable concrete counterexamples.

## Calls and loops

Source and admissible metadata calls are resolved through compiler evidence and lowered or modeled within the available boundary. Unknown targets and recursive or opaque call paths carry conservative effects and values. An external declaration is not a source-body proof.

Natural loops use cut encodings and invariant candidates. Candidate invariants must pass establishment and preservation checks before use in a proof. Bounded witness search is separate from the proof encoding; finite unrolling cannot prove an arbitrary loop or termination. Unsupported CFG shapes or exhausted construction limits produce Unknown.

Call preconditions and body checkpoints are obligations, not unchecked assumptions. General virtual dispatch, recursion, async execution, and arbitrary metadata bodies are not blanket supported just because a related form can be lowered. See [coverage and limits](docs/coverage-and-limits.md).

## Effects

Native effect claims are decided over the same Total program, through the exception and effect-site verifiers. They include `DoesNotThrow`, `AllowedExceptions`, `ZeroAllocations`, `EnforcePure`, `AllowedCapabilities`, and `EffectContract`.
Exception proofs also require positively admitted entry initialization. The compiler emits a native exception constraint only when the target type and source module initialization have no modeled synchronous fault. Empty initializers, built-in constants, simple static-field writes of admitted values, and the audited parameterless `System.Object` construction can satisfy this check. Bound conversions and initialization dependencies are checked; an initializer that calls unproved code, invokes a property setter or user conversion, or has an unproved dependency yields Unknown. An unused base or enclosing type is not an initialization dependency merely because of inheritance or nesting.

This exception check is separate from entry purity and allocation checks: a no-fault constant static write still changes state. Existing opaque source-callee boundaries remain conservative. A non-null receiver does not add the effects of constructing that receiver to its instance method. Ordinary allocation follows the existing modeled-exception policy; arbitrary initializer programs and precise initialization-exception summaries remain unsupported. Missing native exception constraints cannot supply a proof, and compiler artifact schema 31 rejects the older admission meaning.

Effects distinguish receiver, argument, captured, static, and ambient reads/writes; allocation; exceptions; synchronization; nondeterminism; native code; and reflection. Capability flags are a separate closed set. Each flag is independent: permitting `Throws` does not permit `Allocates`.

Observable purity excludes modeled externally observable effects and relevant ambient reads. A local operation is not automatically externally observable; classification depends on the modeled region and escape behavior. Calls with incomplete information retain conservative may-effects.

A body proof carries `CompleteMayEffectSummary`. A trusted external declaration carries `TrustedCompleteBoundary`, and remains an assumption. A replayed violation carries `DefiniteViolation` with a source site. A contradictory entry carries `VacuousEntry`. These certainties must agree with outcome, reason, witness, and vacuity; protocol validation rejects inconsistent tuples.

## Counterexample replay

Replay validates a candidate against the owned program, entry values, path, and claim. Solver-chosen values for unconstrained results or approximated reads are not arbitrary executable inputs. Undefined postconditions, unsupported replay operations, and missing source-site evidence cannot produce a definite refutation.

Heap witness decoding also checks the closed compiler type evidence. Distinct non-null references from incompatible sealed runtime types cannot be assigned one object token. Impossible aliases downgrade a candidate to Unknown; this witness filter adds no SMT premise. Compatible aliases and null remain allowed. Unused observations can still make a witness conservatively unreplayable, and reference-valued array accesses remain a current admission limit.

Effect witnesses identify the violating allocation, throw, write, or synchronization site. An inlined metadata site without a source position is insufficient for an emitted definite violation.

## Resources, caching, and failure

Native queries and methods have deterministic resource limits as well as wall-clock limits. Cancellation is cooperative within the worker and enforced by the launcher boundary. Completed solver work remains charged even when cancellation arrives later.

A response cache binds compiler artifact bytes, worker and specification identity, protocol, manifest, and budgets. Only validated complete eligible responses are reused. Stable semantic Unknown results may be cached; transient failures are excluded. Corruption or unavailable cache storage becomes a miss, never a changed semantic verdict.

Protocol validation and atomic publication are infrastructure boundaries. A malformed request, unowned output, or backend failure is a run failure or typed abstention, not an empty successful proof set. See [Unknown reasons](docs/unknown-reasons.md), [SMT lifecycle](docs/smt-lifecycle.md), and [preview support](docs/preview-support.md).

## Verification references

- [Architecture](docs/architecture.md): current component ownership.
- [Analysis limits](docs/analysis-limits.md): current defaults.
- [Coverage and limits](docs/coverage-and-limits.md): supported examples and remaining boundaries.
- [Golden fixtures](tests/golden/README.md) and [corpus gates](SharpProof.Gates/README.md): executable regression evidence.
