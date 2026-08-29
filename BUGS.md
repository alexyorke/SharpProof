# SharpProof bug audit status

This file is the current, evidence-backed status ledger for the repository audit. It keeps unresolved findings, accepted limitations, deferred security/integrity work, rejected leads, and the detailed evidence needed to trace resolved fixes. The compact ledger below provides a quick index without requiring every historical report to be reread.

## Open and accepted findings

The current audit wave is running against exact baseline
ffe74fff1c852d073610cfbebc54c141521a25fb. Twenty read-only agents are
reviewing non-overlapping subsystems. Agents may build or execute disposable
probes, but they do not modify the repository and do not write this ledger.
The main agent is the sole BUGS.md writer.

The following non-security findings were reproduced by their reporting agents
before being added here. No production, test, build, or configuration changes
are included in this audit-only wave.

## Deferred by explicit scope

The following findings concern cybersecurity, raceable trust decisions, or filesystem durability/integrity. They are recorded for a separate security review and were not implemented in this audit, per the user's explicit no-cybersecurity instruction.

## Rejected or reclassified leads

- **1-3:** `ArgumentNullGuard` assignments are intentional null-state narrowing/field initialization patterns, not correctness bugs.
- **4:** `LazyThreadSafetyMode.ExecutionAndPublication` already supplies the required synchronization; no race was reproduced.
- **5:** Documentation breadth is maintenance debt, not an independently reproducible product defect; the documentation audit is tracked separately.
- **6:** `RegisterCompilationEndAction` is a valid Roslyn registration API; the naming claim was based on a mistaken signature assumption.
- **275:** Exact `Contract.Result<T>` nullability matching is intentional contract identity behavior and is covered by binder tests.
- **279:** The original silent profile/configuration disagreement report is superseded. Current configuration parsing detects conflicting aliases and reports the authoritative invalid-configuration diagnostic; no silent shadowing remains.
- **317:** The GUID-bearing compiler-manifest path was replaced with a stable compiler-visible source path and the unchanged-build editorconfig regression is covered by `8127933fc`. Target-level verifier reuse remains intentionally deferred because an inputs/outputs-only skip could bypass repeated refutation and infrastructure checks; a persisted canonical status fingerprint is required before changing that behavior.
- **369:** Explicit-interface implementation admission is fixed by `fa58c7533`; static constructors remain intentionally fail-closed because type-initialization ordering and replay evidence are not modeled.
- **366:** Leading-double-slash path identity divergence was not reproduced by the canonical Linux/.NET path implementation; no publication split was observed.
- **371:** `[SharpProofSuppress]` is documented and tested as analyzer-reporting policy only; collector verification remaining active is intentional fail-closed behavior.
- **412:** The claimed Gates RS0030 build failure was not reproduced; the current Release Gates build is clean and the remaining mutation calls are intentional harness code.
- **417-419:** The reported Linux backslash failures were disproved by canonical PowerShell `Join-Path` normalization and passing path-authority probes.

## Resolved in this branch

Resolved reports are removed after reproduction, implementation, regression testing, and review. This compact table preserves the local evidence anchors.

| Findings | Resolution commit(s) |
| --- | --- |
| 151 | `7e3ef5c8e` (UTF-16 sequence/null-tag SMT encoding and replay tests) |
| 280 | `8d166cad1` (defined divide/remainder cases and retained seed evidence) |
| 284 | `8d166cad1`, `4d2749126` (semantic-cache marker, field/compound alias coverage) |
| 285 | `8d166cad1` (semantic Roslyn outcome-construction architecture scan) |
| 324 | `e5850507a` (audited Roslyn construction and whole-compilation diagnostics boundary) |
| 409 | `6462246f7` (protocol answer catalog and guarded semantic-cache writes) |
| 317 | `8127933fc` (stable compiler-visible manifest source path and incremental regression) |
| 369 | `fa58c7533` (explicit-interface implementation boundary; static constructors remain fail-closed) |
| 364 | `02a645e69` (regular-file validation for private verifier paths) |
| 337 | `cc0f2bc6b` (validated recovery for interrupted release-bundle swaps) |
| 273 | `91636b24f` (directory synchronization after publication deletion) |
| 202 | `0a2c179f9` (runtime companion path validation and generated launcher coverage) |
| 257-262 | `68afb8ca1`, `c3ab72290`, `8bd08c6e0` |
| 263-270 | `0c9e0ec0d`, `0c95dad38`, `0a2c179f9`, `a7b99ca24` |
| 274, 276 | `549c76510` |
| 277 | `68afb8ca1` (bounded summary dependency regression) |
| 278 | `68afb8ca1` |
| 281-283 | `68afb8ca1`, `0a2c179f9`, `a7b99ca24` |
| 286-287 | `a7b99ca24` |
| 288 | `4d2749126` (unknown event receivers retain add/remove accessor effects) |
| 295 | `47b8d6f7b` (captured closure state and allocation effects) |
| 403 | `616f9e619`, `6448cab79`, `f00be7ef3` (authoritative pre-manifest failure rebinding) |
| 410 | `b92cba235`, `f9e77c5b4` (factory method-group delegate inspection) |
| 326-327 | `f336f1213` (meta-analyzer recursive fragments and interface storage) |
| 351 | `8e34fcfca` (nonblank retired-mode alias fallback) |
| 422 | `0ef01b488` (preflight timing evidence remains recordable) |
| 296 | `8c71195e9` (canonicalization arity guards and typed regression) |
| 311 | `9bcb41bd5` (public evidence-authority validation overloads) |
| 350 | `adc74ffaa` (banner-aware generated header detection) |
| 396 | `bf772d063`, `87da87603` (attribute-aware metadata companion prefilter and metadata-table fast path) |
| 411 | `3cf5c3747`, `39dc0ab87` (cataloged semantic strings and static-constructor-safe readonly inference) |
| 416 | `acdf88263` (inventory-driven semantic framework identity scan) |
| 423 | `87da87603` (package tests honor `SHARPPROOF_REPO_ROOT` under isolated coverage output) |
| 271 | `8774a4daa` (verified Z3 loading remains bound to the validated inode) |
| 272 | `2430007b0` (publication reads use no-follow directory handles) |
| 318 | `3be94f0b0` (retained cleanup failures stay inside the task boundary) |
| 406 | `b83fc87b8` (worker request/result inode aliases are rejected) |
| 407 | `ebfaa3e50` (resolver publication gate blocks callbacks until the verified handle is published) |

The deferred security/containment findings addressed in this branch have dedicated threat-model review and focused regression evidence below. Future changes to those areas should preserve that validation boundary.

## Active, deferred, and rejected findings

No unresolved, deferred, partial, policy, rejected, disproved, or not-reproduced bug entries remain. Historical findings are retained only through the compact resolution and reclassification ledgers above.
