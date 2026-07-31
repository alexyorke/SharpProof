# Typed abstention reasons

SharpProof represents semantic uncertainty with closed enums. Display text can
explain a result, but semantic branching, proof evidence, serialization, and
cache identity use the typed values below.

`Unknown` is not failure converted into proof. It means SharpProof did not
establish `Proven` or replay-validated `Refuted` within the admitted model and
budgets. Unsupported unannotated analyzer callables abstain silently;
unsupported explicitly selected callables report SP0047, and worker
verification returns an explicit typed record.

## Frontend expression and program lowering

`SharpProof.Frontend.FrontendAbstention` has these exact values:

| Value | Meaning |
|---|---|
| `None` | The classification is exact; this is not an abstention |
| `UnsupportedOperationKind` | A known Roslyn operation kind is outside the frontend subset |
| `UnsupportedType` | The IR has no exact admitted type mapping |
| `ErrorOperation` | Roslyn produced an error operation |
| `InvalidOperation` | Roslyn produced an invalid/none operation |
| `UserDefinedOperator` | Operator semantics depend on user code |
| `LiftedOperator` | Nullable lifted operator semantics are not modeled exactly |
| `UncheckedOverflowSemantics` | The requested unchecked behavior cannot be preserved exactly |
| `ConversionMayChangeValue` | A conversion is not proven value-preserving in the admitted IR |
| `UnsupportedMemberAccess` | The member observation has no exact lowering |
| `UnsupportedInvocationShape` | Receiver, arguments, reduction, defaults, or call shape is unsupported |
| `UnsupportedControlFlow` | Program control flow is outside the lowerer subset |
| `UnsupportedStatement` | A statement has no exact program lowering |
| `UnsupportedMutation` | A mutation has no exact state model |
| `UnknownOperationKind` | A future numeric Roslyn operation kind is not in the closed table |

An exact expression result carries `None`. A closed abstention must carry one
of the other values. Program lowering also records the exact `OperationId` that
caused each abstention.

## Analyzer language gate

`SharpProof.Analyzer.LanguageSubsetAbstentionReason` is internal and has these
exact values:

- `None`
- `UnsupportedCallable`
- `MissingOperationRoot`
- `UnsupportedOperationKind`
- `UnsupportedType`
- `UnsupportedOperationShape`

The effect gate runs before effect-summary analysis. Contract call-site
analysis still runs independently, so an unrelated unsupported effect
operation cannot hide a concrete SP0027 precondition refutation. Unsupported
unannotated analyzer callables emit no incomplete-analysis diagnostic. Corpus
instrumentation records their internal semantic status so silence is not
counted as proof.

Effect not-proven messages identify the incomplete facet with one of these
stable reason prefixes:

- `AllocationUnknown` - only allocation evidence was insufficient;
- `CapabilitySetUnknown` - only capability evidence was insufficient; and
- `ExceptionSetUnknown` - only escaping-exception evidence was insufficient.

Uncertainty in one facet does not block a result for an independent facet.
Purity depends on observable read/write regions and capabilities;
zero-allocation depends on allocation; capability contracts depend on the
capability set; and exception contracts depend on the escaping-exception set.

## Approximation provenance

Facts that over-approximate execution use
`SharpProof.Verify.ApproximationReason`:

- `UnsupportedOperation`
- `UnresolvedApi`
- `AbstractJoin`
- `Widening`
- `Budget`
- `ExternalBoundary`

An `ApproximatedJustification` is deliberately not a `ProofJustification`.
Approximate facts therefore cannot be promoted into assumptions or appear as
evidence authorizing `Proven`.

## SMT backend failures

`SharpProof.Verify.BackendFailureReason` has these exact values:

- `None`
- `UnsupportedEncoding`
- `ResourceLimit`
- `Timeout`
- `Unavailable`
- `MalformedResult`
- `InfrastructureFailure`

`None` accompanies satisfiable or unsatisfiable backend results. Every other
value accompanies backend `Unknown` and is mapped through the proof kernel.

## Proof-kernel abstention

`SharpProof.Verify.AbstentionReason` has these exact values:

| Value | Boundary |
|---|---|
| `UnsupportedOperation` | The proof query contains an operation outside the verified subset |
| `ApproximationTouchedGoal` | Establishing the goal would depend on approximate evidence |
| `MissingApiSpecification` | An external member has no exact resolved spec |
| `UnsupportedEncoding` | The active SMT backend cannot encode the query |
| `ResourceLimit` | A deterministic solver or method resource allowance was exhausted |
| `Timeout` | The method wall boundary was reached |
| `BackendUnavailable` | The configured backend or native dependency is unavailable |
| `InfrastructureFailure` | Non-semantic worker/backend infrastructure failed |
| `MalformedBackendResult` | Status, core, or model shape is invalid |
| `CounterexampleReplayFailed` | A SAT model failed exact assignment-closure or lowered-term replay |
| `PostconditionMayBeUndefined` | A candidate input makes the postcondition expression throw instead of yielding a Boolean value |

Only the proof kernel constructs proof outcomes. Backend UNSAT becomes
`Proven` only after evidence-core hygiene. Backend SAT becomes `Refuted` only
after its assignments exactly close the requested model and replay every
lowered assumption as true and the goal as false. Any failed check becomes
`Unknown`. The worker applies the additional independent whole-body replay
before assembling a `Refuted` record.

## Worker verification records

Protocol version 9 binds compiler-manifest evidence and separates run state,
callable coverage, and claim outcome.
Every enum reserves `Unspecified` as its zero value; a valid request or response
must use a permitted nonzero value where the field is required.

The compiler artifact's `WorkerFeatureSet` is exactly:

- `Unspecified` - invalid placeholder;
- `Effects` - select effect annotations and exclude postcondition claims; each
  effective selected effect contract receives a typed effect claim;
- `Contracts` - select contract annotations, assumptions, and postcondition
  claims while excluding effect-only annotations; and
- `All` - select both surfaces.

`WorkerVerifyPolicy` is `Unspecified`, `Advisory`, `WarnOnUnknown`, or
`RequireProven`. `WorkerAssumptionPolicy` is `Unspecified`, `Allow`, `Warn`, or
`Error`. `Unspecified` is invalid for artifact feature selection and for
required request policies. The launcher maps the other policy values to
SP0047/SP0048 severity and build behavior; policy never changes a claim outcome
or makes a failed run successful.

`WorkerRunStatus` is exactly:

| Value | Meaning |
|---|---|
| `Unspecified` | Invalid placeholder; rejected by protocol validation |
| `Complete` | Verification finished and the exact accounting response is structurally complete; claims may still be `Unknown` |
| `TimedOut` | The project boundary expired |
| `Canceled` | Caller cancellation stopped the run |
| `Failed` | Input, compilation, backend, replay, containment, protocol, or infrastructure processing failed |

`WorkerRunFailureReason` is exactly:

| Value | Meaning |
|---|---|
| `Unspecified` | Invalid placeholder |
| `None` | Required for a `Complete`, `TimedOut`, or `Canceled` run |
| `InvalidRequest` | The request failed schema or value validation |
| `InputUnavailable` | The required compiler-manifest artifact could not be read |
| `CompilationFailure` | The compiler-produced artifact contains one or more error diagnostics |
| `CompilerManifestMismatch` | Artifact digest/schema, expression-depth binding, lowered graph, callable/claim ownership, or assumption declarations are invalid or inconsistent |
| `BackendUnavailable` | The configured SMT backend or native payload is unavailable |
| `InfrastructureFailure` | A non-semantic worker component failed |
| `MalformedResult` | A backend, cache, or assembled response failed structural validation |
| `CounterexampleReplayFailed` | A candidate refutation did not replay |
| `ContainmentFailure` | Required process/resource containment could not be established |

`WorkerCallableCoverage` is `Unspecified`, `Complete`, or `Incomplete`.
`WorkerCallableCoverageReason` is exactly:

- `Unspecified`
- `None`
- `UnsupportedCallable`
- `UnsupportedContract`
- `SemanticUnknown`
- `MissingClaimResult`
- `MethodTimeout`
- `ProjectTimeout`
- `Canceled`
- `InfrastructureFailure`

`WorkerClaimOutcome` is `Unspecified`, `Proven`, `Refuted`, or `Unknown`.
Every manifest claim has exactly one non-`Unspecified` outcome.

`WorkerClaimReason` has these exact values:

| Value | Meaning |
|---|---|
| `Unspecified` | Invalid placeholder |
| `None` | A terminal `Proven` or `Refuted` record has no abstention |
| `UnsupportedCallable` | Callable kind, target, companion, or contract binding target is unsupported |
| `UnsupportedContract` | Contract structure or intrinsic use is invalid/unsupported |
| `UnsupportedBody` | The bounded acyclic analyzer or worker cannot model the body, including a selected analyzer body with reachable cyclic flow |
| `UnsupportedExpression` | Contract/body expression, spec application, or proof encoding is unsupported |
| `DeepPostcondition` | The constructed obligation exceeds `MaximumExpressionDepth`; it does not mean general deep verification is implemented |
| `MissingReturnValue` | A result-dependent postcondition has a normal path without a usable return value |
| `ResourceLimit` | A deterministic analyzer block/operation budget or worker per-query/per-method resource allowance is exhausted |
| `MethodTimeout` | The method wall boundary is reached |
| `ProjectTimeout` | The project boundary leaves the record unfinished |
| `Canceled` | Caller cancellation stopped this claim after its manifest was sealed |
| `BackendUnavailable` | Z3/backend loading or availability failed |
| `InfrastructureFailure` | Non-semantic worker infrastructure failed |
| `MalformedBackendResult` | The backend result cannot pass structural/kernel validation |
| `CounterexampleReplayFailed` | Exact term/whole-body postcondition replay or structurally valid effect-event replay disagreed with its candidate; the assembled run fails |
| `PostconditionMayBeUndefined` | Evaluating the postcondition can throw for a candidate input, so its Boolean truth value is not defined on every modeled normal-return state |
| `CounterexampleNotReplayable` | A postcondition candidate depends on an executed modeled call, or a definite effect candidate is outside the admitted allocation-event replay subset |
| `EffectSummaryIncomplete` | The compiler-produced effect summary has an unknown facet or is otherwise incomplete |
| `EffectContractNotEstablished` | A complete may-effect summary does not establish the selected effect contract and no definite replayable violation witness is available |

Effect claim records have an additional closed certainty field:

- `CompleteMayEffectSummary` means the relevant may-effect facet was complete;
- `IncompleteMayEffectSummary` means that facet was incomplete;
- `TrustedCompleteBoundary` means a complete bodyless contract was accepted as
  an explicit trusted boundary;
- `DefiniteViolation` is compiler evidence that a simple unconditional direct
  effect has a source-located structured witness. The worker independently
  replays only the admitted managed object/array allocation form; and
- `Unavailable` means infrastructure or invalid contract evidence prevented a
  semantic effect result.

A may-effect summary is suitable for proving the absence of a disallowed
effect, but the presence of a may-effect is not itself a concrete trace.
Consequently a complete summary that does not establish the contract remains
`Unknown(EffectContractNotEstablished)`. Compiler artifact schema 9 can seal
one unconditional definite managed object/array allocation event for
independent worker replay. The worker validates its order, source-tree
identity/span, selected-constraint and semantic-operation hashes, and sealed
witness, then derives `Allocates` itself. A match can refute
`ZeroAllocations` or an `EffectContract` that excludes `Allocates`.
`EnforcePure` remains observable purity and permits fresh allocation.
The operation hash checks canonical agreement among compiler-produced event
fields; source discovery, analysis, and event lowering remain trusted rather
than being independently reconstructed by the worker.

Definite explicit-throw, receiver-field, empty-lock, exact-`Monitor`,
static-initialization-sensitive allocation, and other unsupported direct
candidates become `Unknown(CounterexampleNotReplayable)`.
Conditional/path-dependent and may-only conflicts without a definite replay
candidate remain `Unknown(EffectContractNotEstablished)`. Invalid replay
structure is malformed compiler evidence and fails as
`CompilerManifestMismatch`; a structurally valid replay that disagrees
semantically becomes the fatal
`Unknown(CounterexampleReplayFailed)`. Effect results remain noncacheable.
Analyzer evidence preserves the more specific
`ManagedAbstractFlow:BlockBudgetExceeded`,
or `ManagedAbstractFlow:OperationBudgetExceeded` detail through JSON, SARIF,
and cache records even when the closed claim reason is projected to
`ResourceLimit`. Cyclic scalar flow disables scalar refinement, but does not
make an effect claim incomplete: the effect engine can still prove the claim
from its conservative scan of every compiler-reachable block.

Proven postconditions additionally carry `WorkerVacuityKind`: `None`,
`ContradictoryPreconditions`, or `NoModeledNormalReturn`. The last two make
partial-correctness vacuity visible rather than silently presenting the result
as an ordinary proof. The field is preserved by canonical JSON and SARIF
projection. Proven claims do not enter the semantic cache.

The worker intentionally coalesces some lower-layer distinctions. For example,
proof `UnsupportedOperation`, `ApproximationTouchedGoal`,
`MissingApiSpecification`, and `UnsupportedEncoding` map to worker
`UnsupportedExpression`. Contract binding failures map to
`UnsupportedContract`, `UnsupportedExpression`, or `UnsupportedCallable`
according to their closed failure kind.

Postcondition whole-body replay executes only the concrete path selected by the
model. Executed modeled calls produce `CounterexampleNotReplayable`; other
unsupported IR operations or inconsistent replay state produce
`CounterexampleReplayFailed`. The same instructions on unselected paths do not
block replay. Contract-only ordinary `void` methods use exact zero-step replay.
Constructor postconditions are `UnsupportedBody` until base-constructor and
field-initializer semantics are lowered. Successful postcondition refutations
expose only canonical user-model variables.

The callable record prevents a zero-claim selected method from disappearing.
The sealed manifest and response must have exact callable/result and
claim/result equality: missing, duplicate, invented, out-of-range, or
mis-owned claims make the response malformed rather than successful.

`WorkerCacheStatus` is exactly:

- `Unspecified`
- `Disabled`
- `Miss`
- `Hit`
- `Written`
- `Rejected`
- `Unavailable`

The response summary includes counts for every claim outcome and reason,
assumptions (including used, user, and trusted counts), cache state,
protocol/manifest/cache/tool/spec versions, the canonical packaged worker
runtime-closure digest and API spec-content SHA-256 identity, effective
budgets, and elapsed time.

## Protocol errors are separate

Malformed requests, invalid compiler artifacts, compilation errors, and
infrastructure failures are serialized in the response `errors` array as typed
string codes such as:

- `request.null`, `request.malformed`, and `protocol.unsupported`;
- `project.compiler_manifest`;
- `compiler_manifest.unavailable` and `compiler_manifest.invalid`;
- `compiler_manifest.options` and `compiler_manifest.lowered_ir`;
- `budgets.rlimit`, `budgets.expression_depth`, and other budget codes;
- `cache.maximum_bytes`;
- `input.unavailable`, `backend.unavailable`, `worker.infrastructure`,
  `containment.unavailable`, and `worker.malformed_result`; and
- `compiler.<diagnostic-id>`.

These errors are not `WorkerClaimReason` values and are not semantic
answers.

## Cancellation, diagnostics, and caching

Caller cancellation remains run status `Canceled`; it is not converted to a
claim reason or cached response. Outer launcher termination is likewise
infrastructure control, not a proof.

An analyzer not-proven diagnostic and a worker `Unknown` record are different
interfaces. Diagnostic silence can also mean disabled reporting or silent
language-gate abstention. See [Diagnostics](diagnostic-examples.md) for the
reporting surface and [Coverage and limits](coverage-and-limits.md) for the
admitted product subset.

Unknown outcomes, protocol errors, cancellation, timeout, malformed results,
backend failures, and failed replay are never semantic cache entries. Only a
`Complete`, exact-manifest, postcondition-only response with complete callable
coverage and all claims replay-validated `Refuted` is cacheable. Cache schema
version 11 revalidates every read against the complete current manifest,
reconstructs supported scalar models, checks entry assumptions and source
ranges, and repeats whole-body replay. Proven claims, effect claims, and
unsupported models are not written or reused. `require-proven` runs bypass
this local semantic cache.
