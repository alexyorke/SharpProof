# Typed abstention reasons

Unknown is explicit evidence that a selected claim was not decided. It is not a diagnostic severity, a run status, or proof by silence.

The wire enums and validation rules are in [ProtocolModel.generated.cs](../SharpProof.Worker.Protocol/ProtocolModel.generated.cs) and [ProtocolJson.cs](../SharpProof.Worker.Protocol/ProtocolJson.cs). Consumers must use the actual typed fields instead of parsing display text.

## Worker claim reasons

| Reason | Interpretation |
| --- | --- |
| `UnsupportedCallable` | The selected callable shape is outside execution support |
| `UnsupportedContract` | The contract could not be admitted or bound |
| `UnsupportedBody` | Body/CFG construction is outside the modeled boundary |
| `UnsupportedExpression` | An expression has no admitted interpretation |
| `DeepPostcondition` | The postcondition exceeds the allowed construction depth |
| `MissingReturnValue` | A required normal result value could not be supplied |
| `ResourceLimit` | Bounded solver or construction work was exhausted |
| `MethodTimeout` | The method wall-clock boundary was reached |
| `ProjectTimeout` | The project wall-clock boundary was reached |
| `Canceled` | Work was canceled |
| `BackendUnavailable` | Required solver/backend infrastructure was unavailable |
| `InfrastructureFailure` | A nonsemantic execution/infrastructure failure occurred |
| `MalformedBackendResult` | Solver evidence was malformed or invalid |
| `CounterexampleReplayFailed` | Replay failed at its evidence boundary |
| `PostconditionMayBeUndefined` | Evaluating the postcondition may be undefined/throw |
| `CounterexampleNotReplayable` | The candidate does not establish an executable counterexample |
| `EffectSummaryIncomplete` | Effect evidence is insufficiently complete |
| `EffectContractNotEstablished` | The required effect boundary was not established |
| `SolverIncomplete` | The solver/check could not decide the obligation |

`None` is the successful reason sentinel, not an Unknown explanation. `Unspecified` is an invalid/uninitialized protocol sentinel for emitted claim evidence. Validators enforce the admitted combinations; an enum name alone does not authorize a result tuple.

## Callable coverage and run status

Callable coverage is `Complete` or `Incomplete`. Its reasons include unsupported callable/contract, semantic Unknown, missing claim result, method/project timeout, cancellation, and infrastructure failure.

Run status is `Complete`, `TimedOut`, `Canceled`, or `Failed`. A complete run can still contain stable semantic Unknown results. Missing claim results and inconsistent manifest coverage are not complete success.

Run failure reasons are separate: invalid request, unavailable input, compilation failure, compiler-manifest mismatch, unavailable backend, infrastructure failure, malformed result, replay failure, or containment failure. Read the structured protocol errors as well.

## Effect evidence and vacuity

Effect certainty distinguishes:

- `IncompleteMayEffectSummary`: partial may-effect information.
- `CompleteMayEffectSummary`: complete modeled body evidence.
- `TrustedCompleteBoundary`: reviewed declared external evidence.
- `DefiniteViolation`: replayed violating effect site.
- `Unavailable`: no admissible effect evidence for the result.
- `VacuousEntry`: contradictory entry-domain evidence.

Certainty, outcome, reason, assumptions, witness, and vacuity must agree. For example, a definite violation needs a replayed witness, while a trusted boundary remains an assumption. These are not interchangeable proof grades.

Vacuity is `None`, `ContradictoryPreconditions`, or `NoModeledNormalReturn`. A normal-return postcondition does not prove termination; inspect its vacuity field.

## Frontend and solver abstentions

The frontend keeps its own typed reasons for unsupported operation/type/member/call/control-flow/mutation forms, invalid/error operations, user-defined/lifted operators, overflow/conversion limitations, unknown operation kinds, and expression depth.

See [FrontendSubset.cs](../SharpProof.Frontend/FrontendSubset.cs). An abstention applies to the path and operation producing it; its existence does not mean every use of that language feature is unsupported across all pipelines.

Solver results and IR evaluation also preserve typed failures. Projection to a worker reason must retain incomplete evidence rather than guess a successful interpretation.

## Diagnose an Unknown

1. Identify the selected callable and claim in the manifest.
2. Check run status and protocol errors before interpreting a claim.
3. Read callable coverage, claim reason, assumptions, certainty, and vacuity together.
4. For semantic limits, inspect [coverage and limits](coverage-and-limits.md) and the relevant regression fixtures.
5. For resource limits, inspect [analysis limits](analysis-limits.md) and reproduce with the same artifact and budgets.
6. For replay limits, distinguish approximated values or impossible aliases from a real program violation.

Do not add an unjustified `Assume`, suppress a diagnostic, or reclassify a case merely to obtain green output.

## Cancellation, diagnostics, and caching

Portable diagnostics report their own conservative analysis surface. They are not a replacement for worker records. Suppression changes reporting only.

The cache accepts validated complete eligible responses. Stable semantic Unknown may be cached. Timeout, cancellation, backend failure, and malformed/incomplete runs cannot become durable complete evidence. Corrupt cache data becomes a miss. See [SMT lifecycle](smt-lifecycle.md).
