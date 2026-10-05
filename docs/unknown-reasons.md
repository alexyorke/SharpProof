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
| `ExpressionDepthLimit` | Expression lowering reached its bounded recursion ceiling |

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
| `UnsupportedEncoding` | The active SMT backend cannot encode the query |
| `ResourceLimit` | A deterministic solver or method resource allowance was exhausted |
| `Timeout` | The method wall boundary was reached |
| `BackendUnavailable` | The configured backend or native dependency is unavailable |
| `InfrastructureFailure` | Non-semantic worker/backend infrastructure failed |
| `MalformedBackendResult` | Status, core, or model shape is invalid |
| `CounterexampleReplayFailed` | A SAT model failed exact assignment-closure or lowered-term replay |
| `CounterexampleNotReplayable` | Concrete callable replay reached a registered call without an executable specification |
| `PostconditionMayBeUndefined` | A candidate input makes the postcondition expression throw instead of yielding a Boolean value |
| `InternalConsistencyMayBeUndefined` | A candidate input makes an internal-consistency expression throw instead of yielding a Boolean value; this is distinct from malformed counterexample replay |

Only the proof kernel constructs proof outcomes. Backend UNSAT becomes
`Proven` only after evidence-core hygiene. Backend SAT becomes `Refuted` only
after its assignments exactly close the requested model and replay every
lowered assumption as true and the goal as false. Any failed check becomes
`Unknown`. The kernel also executes the concrete callable path and checks
contract state, source domains, and the original Ensures before refuting.

## Worker verification records

Protocol version 13 binds compiler-manifest evidence and separates run state,
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
| `CounterexampleNotReplayable` | A postcondition candidate depends on an executed modeled call, or a definite effect candidate is outside the admitted unconditional effect-event replay subset |
| `EffectSummaryIncomplete` | The compiler-produced effect summary has an unknown facet or is otherwise incomplete |
| `EffectContractNotEstablished` | A complete may-effect summary does not establish the selected effect contract and no definite replayable violation witness is available |
| `SolverIncomplete` | The SMT solver returned `unknown` because its supported theory is incomplete; the claim remains unproved and unrefuted |

The exact typed outcome and effect-certainty authority follows.

<!-- BEGIN SHARPPROOF TYPED EFFECT RESULTS -->
### `WorkerClaimOutcome`

| Member | Meaning |
|---|---|
| `Unspecified` | Invalid placeholder; rejected for every completed claim result |
| `Proven` | The selected claim was established from authenticated evidence |
| `Refuted` | A concrete counterexample or effect witness passed exact replay |
| `Unknown` | The claim was not proved or refuted and carries one admitted reason |

### `WorkerEffectEvidenceCertainty`

| Member | Meaning |
|---|---|
| `Unspecified` | Required for non-effect claims and invalid for effect claims |
| `IncompleteMayEffectSummary` | The relevant may-effect facet is incomplete |
| `CompleteMayEffectSummary` | The relevant may-effect facet is complete |
| `TrustedCompleteBoundary` | A complete bodyless effect contract is an explicit trusted boundary |
| `DefiniteViolation` | A source-located direct effect has an independently replayable witness |
| `Unavailable` | An `Unknown` effect claim for any schema-admitted unknown reason when no more specific certainty applies |
| `VacuousEntry` | Contradictory entry preconditions prove the effect claim vacuously |

### Allowed effect-result tuples

| Outcome | Reason | Certainty |
|---|---|---|
| `Proven` | `None` | `CompleteMayEffectSummary` |
| `Proven` | `None` | `TrustedCompleteBoundary` |
| `Proven` | `None` | `VacuousEntry` |
| `Refuted` | `None` | `DefiniteViolation` |
| `Unknown` | `UnsupportedContract` | `Unavailable` |
| `Unknown` | `CounterexampleNotReplayable` | `Unavailable` |
| `Unknown` | `EffectSummaryIncomplete` | `IncompleteMayEffectSummary` |
| `Unknown` | `EffectSummaryIncomplete` | `TrustedCompleteBoundary` |
| `Unknown` | `EffectContractNotEstablished` | `CompleteMayEffectSummary` |
| `Unknown` | `EffectContractNotEstablished` | `TrustedCompleteBoundary` |
| `Unknown` | `ResourceLimit` | `IncompleteMayEffectSummary` |
| `Unknown` | `ResourceLimit` | `TrustedCompleteBoundary` |
| `Unknown` | `UnsupportedBody` | `IncompleteMayEffectSummary` |
| `Unknown` | `UnsupportedBody` | `TrustedCompleteBoundary` |
| `Unknown` | `*` | `Unavailable` |
<!-- END SHARPPROOF TYPED EFFECT RESULTS -->

Z3 decides effect claims over the callable's Total program. A proof shows that
no violating site is reachable; a refutation names a violating site reached by
concrete replay of the original program. A reachable site that cannot be
replayed, such as an opaque call whose effects are only possible, leaves the
claim `Unknown(CounterexampleNotReplayable)`, and a body the IR cannot lower
leaves it `Unknown(UnsupportedBody)`. The compiler only declares effect claims;
a trusted complete boundary on a bodyless declaration is published as declared.
Valid complete effect responses are cacheable. Fresh allocation remains
compatible with observable `EnforcePure`.

Proven postconditions additionally carry `WorkerVacuityKind`: `None`,
`ContradictoryPreconditions`, or `NoModeledNormalReturn`. The last two make
partial-correctness vacuity visible rather than silently presenting the result
as an ordinary proof. `NoModeledNormalReturn` also covers an unsatisfiable
`Contract.Assume` combined with the method's other modeled assumptions. The
field is preserved by canonical JSON and SARIF projection. Valid complete
responses, including Proven claims, enter the semantic cache.

The worker intentionally coalesces some lower-layer distinctions. For example,
proof `UnsupportedEncoding` maps to worker `UnsupportedExpression`.
Contract binding failures map to
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

Every valid Complete response bound to the exact current manifest is cacheable,
including Proven, replay-validated Refuted, semantic Unknown, effects, and empty
claims. Cache schema 15 binds the full artifact digest, runtime/spec identities,
and semantic budgets. Protocol errors, cancellation, timeout, malformed results,
backend failures, and failed replay are not cache entries. Reads validate the
stored response binding and payload shape without rerunning proof or replay.
