# Stable Unknown-Reason Taxonomy

SharpProof exposes one additive unknown-reason descriptor across proof,
capability, complexity, runtime-hazard, purity, and `[Ensures]` results. Existing
family enums and free-form reason strings remain available for compatibility;
the descriptor gives automation a stable code and a shared category.

## Public Contract

`SymbolicUnknownReasonInfo` contains:

- `Source`: `Proof`, `Capability`, `Complexity`, `RuntimeHazard`, `Purity`, or
  `Ensures`
- `Category`: the cross-family classification
- `Code`: a stable lower-case dotted identifier
- `RawReason`: the existing enum name or reason string
- `IsRetryable`: whether retrying after an external-state or budget change may
  help
- `IsConfigurationRelated`: whether solver enablement or a configured bound is
  directly involved
- `IsUnknown`: false only for the family's `.none` descriptor

The descriptor is exposed through:

- `SymbolicProofInfo.UnknownReasonInfo`
- `SymbolicCapabilitySite.UnknownReasonInfo` and
  `SymbolicCapabilityResult.UnknownReasonDetails`
- `SymbolicComplexityCalleeInfo.UnknownReasonInfo` and
  `SymbolicComplexityResult.UnknownReasonDetails`
- `SymbolicRuntimeHazard.UnknownReasonInfo`
- the public compact capability, complexity, and runtime-hazard projections

Condition/implication results expose the proof descriptor through their
`Proof`. Purity and `[Ensures]` analyzer results carry the same taxonomy in
diagnostic properties.

## Shared Categories

| Category | Meaning |
| --- | --- |
| `UnsupportedSyntax` | The selected source or symbolic shape is outside the supported lowering surface. |
| `UnsupportedOperation` | The syntax was understood, but the operation cannot be modeled safely. |
| `UnsupportedLibraryModel` | Required framework, metadata, or callee behavior has no authoritative model. |
| `DynamicDispatch` | The runtime target cannot be bounded to a supported implementation. |
| `ExternalBoundary` | Analysis reached source or metadata outside the available proof boundary. |
| `RecursiveAnalysis` | A recursive or cyclic analysis boundary prevented a finite result. |
| `SolverDisabled` | SMT analysis was not enabled. |
| `SolverBudget` | Method, path-condition, or expression-node budget was exhausted. |
| `SolverTimeout` | The bounded solver timed out. |
| `NativeSolverFailure` | Z3/native loading or availability failed. |
| `SolverEncodingFailure` | A supported proof request could not be encoded for the solver. |
| `Cancellation` | The caller canceled analysis. |
| `InvalidInput` | A contract or query condition could not be parsed or bound. |
| `AnalysisUnavailable` | The family query itself failed before classification. |
| `Unknown` | No more specific conservative classification is justified. |

`None` is used only when the result is not unknown.

## Stable Codes

Proof codes distinguish the solver failure modes directly:

| Code | Category |
| --- | --- |
| `proof.unsupported_ir_encoding` | `UnsupportedSyntax` |
| `proof.solver_disabled` | `SolverDisabled` |
| `proof.native_solver_failure` | `NativeSolverFailure` |
| `proof.solver_timeout` | `SolverTimeout` |
| `proof.solver_method_budget` | `SolverBudget` |
| `proof.solver_path_condition_budget` | `SolverBudget` |
| `proof.solver_expression_budget` | `SolverBudget` |
| `proof.solver_encoding_failure` | `SolverEncodingFailure` |
| `proof.canceled` | `Cancellation` |
| `proof.unknown` | `Unknown` |

An exhausted transient retry uses raw reason `smt_transient_failure` and maps
to the retryable `proof.native_solver_failure` code. A service-level permanent
native failure uses raw reason `smt_unavailable` and the same stable proof code;
inspect `SmtAnalysisHealth` to distinguish recovered, degraded, and permanent
service state.

Runtime hazards and `[Ensures]` reuse these suffixes with
`runtime_hazard.` or `ensures.` prefixes. They also define family boundaries
such as `runtime_hazard.unsupported_typed_projection`,
`ensures.invalid_condition`, and `ensures.unsupported_condition`.

Capability codes include:

- `capability.unsupported_target`
- `capability.no_containing_method_body`
- `capability.dynamic_dispatch`
- `capability.library_model_unavailable`
- `capability.unsupported_operation`
- `capability.recursive_source_cycle`
- `capability.external_source_boundary`
- `capability.canceled`
- `capability.unknown`

Complexity codes include:

- `complexity.unsupported_target`
- `complexity.no_containing_method_body`
- `complexity.unsupported_loop_shape`
- `complexity.unsupported_while_loop`
- `complexity.unknown_callee`
- `complexity.external_callee`
- `complexity.dynamic_dispatch`
- `complexity.recursive_cycle`
- `complexity.unsupported_operation`
- `complexity.canceled`
- `complexity.analysis_failure`
- `complexity.unknown`

Purity evidence uses `purity.library_model_fallback`,
`purity.unsupported_operation`, `purity.dynamic_dispatch`,
`purity.external_boundary`, `purity.recursive_analysis`, `purity.canceled`, or
`purity.unknown` only when the evidence is conservative. Known impurity evidence
uses `purity.none`; its existing impurity category remains the authoritative
cause.

## Diagnostic Properties

Unknown analyzer diagnostics add the following versioned properties:

| Property | Value |
| --- | --- |
| `sharpproof.unknown.code` | Stable dotted code |
| `sharpproof.unknown.category` | `SymbolicUnknownReasonCategory` name |
| `sharpproof.unknown.source` | `SymbolicUnknownReasonSource` name |
| `sharpproof.unknown.raw_reason` | Existing raw reason |
| `sharpproof.unknown.retryable` | Boolean text |
| `sharpproof.unknown.configuration_related` | Boolean text |

These fields are emitted for capability, complexity, unknown runtime hazards,
purity fallback/unsupported evidence, and unsupported or unknown `[Ensures]`
results. Family-specific properties such as
`sharpproof.runtime_hazard.unknown_reason` and
`sharpproof.ensures.failure_reason` are retained.

## Compatibility Rules

- Codes documented here are stable identifiers. Their display wording may
  improve without changing the code.
- New codes and enum values are additive. Consumers must tolerate unknown
  future values.
- `RawReason` is evidence and debugging context, not a stable branching key.
- A more specific category may replace `Unknown` in a future additive release;
  an existing specific code must not silently change meaning.
- Persisted consumers should also record the proof/evidence schema fields
  described in [the evidence compatibility policy](evidence-schema.md).
