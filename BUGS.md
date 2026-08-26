# SharpProof bug audit status

This file is the current, evidence-backed status ledger for the repository audit. It deliberately keeps only unresolved findings, accepted limitations, deferred security/integrity work, and rejected leads. Resolved findings are listed in the compact ledger at the end so that their fixes remain traceable without retaining superseded reports.

## Open and accepted findings

### 151. SMT precision gap for string operations

**Status:** Accepted fail-closed precision limitation.

The reference interpreter can execute string concatenation and length, while the SMT encoder rejects those operations as unsupported. This produces an abstention rather than an unsound proof. The remaining work is differential-coverage expansion, not a correctness fix. Relevant implementations are `SharpProof.Smt/IrSmtBackend.cs` and `SharpProof.Ir/IrInterpreter.cs`.

### 280. Partial-term differential coverage is intentionally narrow

**Status:** Accepted test-coverage limitation.

The partial-term oracle currently exercises only a small set of observable behaviors; division/remainder dimensions are not represented in the retained campaign. This does not make a production result unsound, but it limits the defects the campaign can detect. Expand the generator and retain seed evidence before treating this as a production fix.

### 284. Cache-soundness identification is name-based

**Status:** Open design limitation; no safe production patch identified.

`SharpProof.Meta.Analyzers/CacheSoundnessRules.cs` recognizes a semantic cache when its type name contains `Cache`. A memoization type with another name is therefore outside SPMETA010's current contract. Flagging every answer write would create broad false positives. The durable fix is an explicit internal cache marker or catalog, with positive and negative tests, rather than a broader heuristic.

### 285. Outcome-constructor architecture enforcement has residual semantic gaps

**Status:** Partially addressed; follow-up remains open.

The architecture inventory now includes `SharpProof.Gates` and rejects target-typed outcome construction in the covered source forms. Target-typed construction in arbitrary expressions and other semantic contexts still requires a Roslyn semantic-model pass to avoid source-only false positives and false negatives. No current production escape was found, and the internal constructors remain restricted.

## Deferred by explicit scope

The following findings concern cybersecurity, raceable trust decisions, or filesystem durability/integrity. They are recorded for a separate security review and were not implemented in this audit, per the user's explicit no-cybersecurity instruction.

### 215. Trusted-attributes payload/hash binding race

**Status:** Deferred security review.

The analyzer hashes the file at a path after Roslyn has loaded the reference, without proving that both reads describe the same bytes.

### 271. Z3 pin/hash versus loaded-library identity

**Status:** Deferred security review.

The container contract validates bytes separately from the library later loaded by the native resolver.

### 272. Publication-path validation versus use

**Status:** Deferred security review.

Path identity is checked by a userspace walk and is not kernel-enforced against a concurrent symlink replacement.

### 273. Publication deletion durability

**Status:** Deferred integrity/durability review.

Reset/invalidation removes publication members without the full filesystem durability protocol required to survive a power loss.

## Rejected or reclassified leads

- **1-3:** `ArgumentNullGuard` assignments are intentional null-state narrowing/field initialization patterns, not correctness bugs.
- **4:** `LazyThreadSafetyMode.ExecutionAndPublication` already supplies the required synchronization; no race was reproduced.
- **5:** Documentation breadth is maintenance debt, not an independently reproducible product defect; the documentation audit is tracked separately.
- **6:** `RegisterCompilationEndAction` is a valid Roslyn registration API; the naming claim was based on a mistaken signature assumption.
- **275:** Exact `Contract.Result<T>` nullability matching is intentional contract identity behavior and is covered by binder tests.
- **279:** Profile/configuration disagreement is an intentional fail-closed policy boundary; changing it would weaken the configured verification contract.

## Resolved in this branch

The detailed reports below were removed after reproduction, implementation, regression testing, and review. The commit is the local evidence anchor.

| Findings | Resolution commit(s) |
| --- | --- |
| 202 | `0a2c179f9` (runtime companion path validation and generated launcher coverage) |
| 257-262 | `68afb8ca1`, `c3ab72290`, `8bd08c6e0` |
| 263-270 | `0c9e0ec0d`, `0a2c179f9`, `a7b99ca24` |
| 274, 276 | `549c76510` |
| 277 | `549c76510` (bounded summary dependency regression) |
| 278 | `a7b99ca24` |
| 281-283 | `68afb8ca1`, `0a2c179f9`, `a7b99ca24` |
| 286-287 | `a7b99ca24` |

The audit does not claim that the deferred security findings are fixed. Any future change to those areas should receive a separate threat-model review and dedicated validation.
