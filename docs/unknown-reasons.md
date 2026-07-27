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

This gate runs before feature analysis. Unsupported analyzer callables emit no
feature diagnostic. Corpus instrumentation records their internal semantic
status so silence is not counted as proof.

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
| `CounterexampleReplayFailed` | A SAT model did not replay to an observed goal failure |

Only the proof kernel constructs proof outcomes. Backend UNSAT becomes
`Proven` only after evidence-core hygiene. Backend SAT becomes `Refuted` only
after executable replay. Any failed check becomes `Unknown`.

## Worker verification records

Protocol version 5 binds compiler-manifest evidence and separates run state,
callable coverage, and claim outcome.
Every enum reserves `Unspecified` as its zero value; a valid request or response
must use a permitted nonzero value where the field is required.

The compiler artifact's `WorkerFeatureSet` is exactly:

- `Unspecified` - invalid placeholder;
- `Effects` - select effect annotations and exclude postcondition claims; the
  current worker reports these callables as incomplete because it has no
  effect-proof claim kind;
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
| `CompilationFailure` | Roslyn could not construct a valid compilation |
| `CompilerManifestMismatch` | Artifact digest/schema, compiler/reference evidence, reconstruction, or discovered claims do not exactly match |
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
| `UnsupportedBody` | The bounded acyclic body executor cannot model the body |
| `UnsupportedExpression` | Contract/body expression, spec application, or proof encoding is unsupported |
| `DeepPostcondition` | The constructed obligation exceeds `MaximumExpressionDepth`; it does not mean general deep verification is implemented |
| `MissingReturnValue` | A result-dependent postcondition has a normal path without a usable return value |
| `ResourceLimit` | Per-query or per-method resource allowance is exhausted |
| `MethodTimeout` | The method wall boundary is reached |
| `ProjectTimeout` | The project boundary leaves the record unfinished |
| `Canceled` | Caller cancellation stopped this claim after its manifest was sealed |
| `BackendUnavailable` | Z3/backend loading or availability failed |
| `InfrastructureFailure` | Non-semantic worker infrastructure failed |
| `MalformedBackendResult` | The backend result cannot pass structural/kernel validation |
| `CounterexampleReplayFailed` | A candidate refutation could not be reproduced by the executable IR |

The worker intentionally coalesces some lower-layer distinctions. For example,
proof `UnsupportedOperation`, `ApproximationTouchedGoal`,
`MissingApiSpecification`, and `UnsupportedEncoding` map to worker
`UnsupportedExpression`. Contract binding failures map to
`UnsupportedContract`, `UnsupportedExpression`, or `UnsupportedCallable`
according to their closed failure kind.

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
user/trusted evidence, cache state, protocol/manifest/cache/tool/spec versions,
effective budgets, and elapsed time.

## Protocol errors are separate

Malformed requests, invalid compiler artifacts, compilation errors, and
infrastructure failures are serialized in the response `errors` array as typed
string codes such as:

- `request.null`, `request.malformed`, and `protocol.unsupported`;
- `project.compiler_manifest`;
- `compiler_manifest.unavailable`, `compiler_manifest.invalid`,
  `compiler_manifest.compilation`, and `compiler_manifest.mismatch`;
- `manifest.failed`;
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
`Complete`, exact-manifest response with complete callable coverage and claims
that are hygienic `Proven` or replay-validated `Refuted` is cacheable. Cache
schema version 5 stores the semantic payload; every read revalidates it against
the complete current manifest.
