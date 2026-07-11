# SharpProof Proof And Evidence Schema

SharpProof exposes proof and evidence through several independently versioned
formats. Each format keeps its existing structural version, while the shared
proof/evidence contract is identified by `evidenceSchemaVersion` and
`evidenceSchemaCompatibility` fields.

The current evidence schema version is `1`. Its compatibility policy token is
`additive-v1`. The public .NET constants and compatibility check are available
through `SharpProof.Symbolic.SharpProofEvidenceSchema`.

## Serialized Surfaces

| Surface | Structural version | Evidence fields |
| --- | --- | --- |
| Compact symbolic JSON (`ISymbolicCompactResult`) | `schemaVersion` | `evidenceSchemaVersion`, `evidenceSchemaCompatibility` |
| Composed explain JSON | `schemaVersion` | `evidenceSchemaVersion`, `evidenceSchemaCompatibility` |
| Explain SARIF | SARIF `version` plus `properties.explainSchemaVersion` | run `properties.evidenceSchemaVersion`, `properties.evidenceSchemaCompatibility` |
| Analyzer diagnostic properties | Roslyn diagnostic descriptor/version | `sharpproof.evidence.schema_version`, `sharpproof.evidence.schema_compatibility` |
| Effect summaries | `SchemaVersion` | `EvidenceSchemaVersion`, `EvidenceSchemaCompatibility` |
| Diagnostic baseline documents and entries | `version` | `evidenceSchemaVersion`, `evidenceSchemaCompatibility` |

The structural version describes the containing file or DTO. The evidence
version describes shared meanings such as proof status, unknown reason,
contract text, trigger evidence, operation kind, evidence key, and source
identity. A structural format can evolve additively without changing the
evidence version, and an evidence-breaking change can require a new evidence
version even when a containing format is otherwise unchanged.

## `additive-v1` Compatibility Policy

- Version `0` means a legacy payload that predates explicit evidence
  versioning. Readers accept it during the public preview. Current writers emit
  version `1`, and baseline tooling upgrades accepted legacy input when it
  writes a new file.
- Version `1` readers must ignore unknown JSON fields and diagnostic properties.
  Writers may add optional fields without changing the evidence version.
- Existing required field names, value types, and meanings cannot be removed,
  renamed, or reinterpreted within version `1`.
- Unknown status, reason, backend, or evidence tokens must remain conservative.
  A reader must not turn an unknown token into a proven result or a baseline
  match.
- A breaking change increments `evidenceSchemaVersion` and defines a new policy
  token. Readers reject versions above their supported range instead of
  silently consuming evidence with unknown semantics.
- Versioned effect summaries and baselines must carry the matching compatibility
  token. The analyzer rejects mismatches with `SP0032`; baseline tooling throws
  `NotSupportedException` before generating or matching entries.

Unknown results also expose an additive stable-code taxonomy. The code,
category, source family, raw reason, and retry/configuration flags are described
in [the unknown-reason registry](unknown-reasons.md). Existing family reason
fields remain and are not replaced.

Bounded fact and state processing likewise exposes additive `analysis_limit.*`
event codes and analyzer diagnostic properties. Their typed payload, defaults,
and configuration controls are documented in
[bounded analysis limits](analysis-limits.md).

SMT diagnostics add nested lifecycle configuration and immutable health state.
Transient recovery counters, permanent-unavailability state, and context
generation semantics are described in [SMT lifecycle and health](smt-lifecycle.md).

Consumers should inspect the evidence version before interpreting proof fields.
`SharpProofEvidenceSchema.IsReadCompatible(...)` accepts the legacy
unversioned value and the current version; it rejects negative and future
versions. Compact JSON consumers outside .NET should implement the same check
and treat an absent field as legacy version `0`.

Machine-readable explain output composes several compact and bounded evidence
views without changing their meanings. Its cross-section pointers, truncation
contract, and SARIF projection are documented in
[machine-readable explain reports](explain-reports.md).
