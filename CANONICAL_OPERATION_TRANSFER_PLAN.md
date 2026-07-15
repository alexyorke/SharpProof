# Canonical Operation Transfer Rewrite

This file is the source of truth for the production-code reduction goal. At the
start of every continuation, read this file before choosing work. Take the first
unchecked item whose prerequisites are complete. At the end of every green
tranche, update its checkbox, the LOC ledger, validation evidence, commit, and
the checkpoint below.

## Goal

Replace parallel Symbolic and Analyzer interpretations of C# operations with
one canonical operation-transfer kernel, then delete the superseded paths.
Preserve supported behavior, conservative `Unknown` outcomes, public contracts,
CLI forms, serialized schemas, diagnostics, evidence, and package contents.

Primary target: remove 11,000-16,000 net handwritten production lines from the
starting baseline of 107,676. Do not count generated output, formatting-only
compression, test deletion, or moving logic into manifests as a reduction.

## Non-Negotiable Rules

- Reproduce or characterize behavior before deleting a semantic path.
- Shadow-compare normalized states, facts, hazards, diagnostics, unknown reasons,
  and truncation reasons while both implementations exist.
- Unsupported or failed lowering remains visibly `Unknown`; never infer success.
- Preserve evaluation order, checked arithmetic, alias/version identities,
  branch joins, exception ordering, ownership, disposal, and escape state.
- Run all .NET commands through `scripts/Invoke-SharpProofDotnet.ps1`.
- Commit green vertical slices. Do not mix unrelated subsystems in one commit.
- Do not change an expected result unless a focused reproduction proves the old
  expectation incorrect.
- Leave unrelated untracked audit artifacts untouched.

## Starting Ledger

- Starting commit: `eb484cdc` (`Condense exception flow data carriers`).
- Starting production LOC: 107,676 across 440 files.
- Module LOC: Symbolic 43,856; Analyzer 36,500; EffectSummary 8,904;
  SymbolicCli 6,364; ProofCore 5,662; other production 6,390.
- High-overlap surface: 14,417 lines.
  - Symbolic state transfer: 5,756 lines.
  - Symbolic runtime hazards: 3,801 lines.
  - Analyzer purity state: 2,163 lines.
  - Analyzer exception flow: 2,697 lines.
- Known baseline issue: focused
  `Sp0010_DirectMultidimensionalArrayCreationOutOfRange_ReportsIndexOutOfRangeException`
  fails on `eb484cdc`; the surrounding exception-flow lane passes 207/208.

## Completed Foundations

- [x] Rename and isolate `SharpProof.ProofCore`.
- [x] Centralize formula traversal, fixed-point collection, affine division, and
  canonical equivalence mechanics.
- [x] Introduce typed IR exception-precondition atoms and remove analyzer trigger
  reconstruction already made redundant by them.
- [x] Move queries to `SymbolicQueryContext` and `SymbolicQueryResult` and remove
  the legacy line/span/file result hierarchy.
- [x] Centralize method/property/accessor dispatch resolution.
- [x] Replace Symbolic CLI option-switch parsing with an option registry.
- [x] Centralize shared assignment-value lowering used by Analyzer through
  `SymbolicTrackedAssignmentStateTransfer`.
- [x] Record the new rewrite baseline and quantify the four overlapping engines.

## Phase 0 - Characterization And Deletion Map

- [x] Capture the Release solution build and all six test-lane counts/skips at
  the starting commit, recording any pre-existing failures here.
- [ ] Capture public API snapshots, representative CLI/JSON/SARIF bytes, package
  contents, seeded fuzz output, and EffectSummary golden output.
- [ ] Add a differential harness that compares normalized `SymbolicState`
  instances, support status, unknown/truncation reasons, and provenance.
- [ ] Inventory every caller and semantic responsibility in the 14,417-line
  overlap surface; assign each old entry point to a migration slice or document
  why it remains a source-only adapter.
- [ ] Define deletion gates for each legacy file or method before introducing
  its replacement.

## Phase 1 - Canonical Operation Model

- [ ] Add typed operation descriptors for assignment, mutation, invocation,
  branch assumption, merge, loop edge, completion, lifetime, and hazard events.
- [ ] Add an immutable transition result containing normalized state, support
  status, provenance, and conservative unknown/truncation reasons.
- [ ] Add one Roslyn `IOperation`/CFG lowering front-end. Retain syntax only for
  source spans, evidence text, and syntax shapes Roslyn does not expose.
- [ ] Add adapters from `SymbolicState` and Analyzer purity state into the kernel
  without duplicating transfer policy.
- [ ] Add invariants for deterministic event ordering and byte-stable evidence.

## Phase 2 - Assignment, Aliasing, And Lifetime

- [ ] Migrate local declarations and simple assignments; shadow-compare old and
  new states, then delete the migrated legacy path.
- [ ] Migrate compound assignments, increments/decrements, checked arithmetic,
  and coalesce assignment.
- [ ] Migrate tuple/deconstruction assignment and evaluation order.
- [ ] Migrate `ref`/`out`, ref-local aliases, invalidation, and version updates.
- [ ] Migrate freshness, ownership, borrowing, escape, disposal, and resource
  release transitions.
- [ ] Delete superseded assignment and Analyzer purity-state implementations.
- [ ] Gate: assignment/lifetime differential suite and affected Analyzer lane are
  green; record net production LOC removed.

## Phase 3 - Branches, Loops, Merge, And Completion

- [ ] Migrate boolean branch assumptions and conditional/coalesce flow.
- [ ] Migrate switch statement/expression branch selection and merging.
- [ ] Migrate loop entry/back-edge/exit transitions and bounded fixed points.
- [ ] Migrate try/catch/finally, exceptional completion, and reachability.
- [ ] Make one merge implementation own fact choices, versions, ownership, and
  conservative truncation.
- [ ] Delete superseded branch, loop, completion, and merge paths.
- [ ] Gate: normalized-state, flow, exception, and reachability lanes are green;
  record net production LOC removed.

## Phase 4 - Runtime Hazards Lowered Once

- [ ] Emit divide-by-zero, checked-overflow, and conversion preconditions from
  canonical operation events.
- [ ] Emit null, nullable-value, dynamic-binding, and disposal preconditions.
- [ ] Emit indexing, range, collection-cardinality, and array preconditions.
- [ ] Emit cast, switch-no-match, argument, direct-throw, and framework-model
  preconditions.
- [ ] Migrate query, suppressor, and exception-flow consumers to the canonical
  descriptors.
- [ ] Delete redundant syntax candidate and trigger-construction paths, leaving
  only source-span/evidence adapters.
- [ ] Gate: hazard ordering, type/category, proof status, witnesses, and unknown
  outcomes match; record net production LOC removed.

## Phase 5 - Analyzer Consumers Become Adapters

- [ ] Migrate purity analysis to consume canonical transitions.
- [ ] Migrate nullable and runtime-type analysis to consume canonical state.
- [ ] Migrate exception flow to consume canonical hazards and completion edges.
- [ ] Centralize Analyzer evidence projection without merging diagnostic policy.
- [ ] Remove remaining Analyzer-owned transfer and hazard interpretation.
- [ ] Gate: Analyzer lane and diagnostic/evidence fixtures are green; record net
  production LOC removed.

## Phase 6 - Secondary Large Reductions

- [ ] Convert EffectSummary wrapper policy to ordered typed structural rules;
  preserve unresolved-external versus resolved-implementation semantics.
- [ ] Project CLI full/compact/invariant/explain/SARIF formats directly from the
  canonical query graph while preserving serialized bytes.
- [ ] Run a bounded primary-constructor conversion over internal data carriers,
  preserving class reference equality where it existed.
- [ ] Remove remaining stale or resolved `POTENTIAL_DUPS.md` findings.
- [ ] Run two `colgrep --force-cpu` semantic-search batches; stop secondary work
  when each finds fewer than 50 safely removable production lines.

## Final Gates

- [ ] Net handwritten production reduction is at least 11,000 lines; continue
  toward 16,000 while parity remains economical.
- [ ] Release and warning-as-error builds succeed with zero warnings.
- [ ] All six test lanes meet or exceed the recorded baseline, apart from
  explicitly documented pre-existing failures/skips.
- [ ] Public API, CLI, JSON/SARIF, diagnostics/evidence, seeded fuzz, NuGet,
  VSIX, native assets, and package-consumer checks pass.
- [ ] No migrated legacy semantic path remains reachable.
- [ ] Two final audits find no remaining high- or medium-severity parallel
  semantic implementation.

## LOC Ledger

| Checkpoint | Commit | Production LOC | Delta From Start |
| --- | --- | ---: | ---: |
| Rewrite start | `eb484cdc` | 107,676 | 0 |

## Validation Ledger

| Checkpoint | Evidence |
| --- | --- |
| Rewrite start | Analyzer/Symbolic focused builds previously green; exception-flow focused lane 207/208 with one failure reproduced on the starting commit. |
| Phase 0 build/test baseline | Release solution build: 0 warnings, 0 errors. MainSmtOracle: 573 passed. MainSmtAnalyzer: 487 passed. MainSmtFlow: 256 passed, 1 failed (the pre-existing SP0010 case). MainSmtCore: 257 passed. MainGeneral: 3,634 passed, 2 skipped. Tooling: 585 passed. Total: 5,792 passed, 1 pre-existing failure, 2 explicit MainGeneral skips. |

## Current Checkpoint

- Last updated: 2026-07-14.
- State: plan committed and the full Release build/test baseline captured; no
  canonical-kernel production code has been added yet.
- Last confirmed fact: the Release solution builds with zero warnings and the
  six lanes pass 5,792 tests, with one pre-existing SP0010 failure and two
  explicit MainGeneral skips.
- Next cheapest step: capture public API, CLI/JSON/SARIF, package, fuzz, and
  EffectSummary contract snapshots, then inventory the simple-assignment slice.
- Blockers: none. The known SP0010 focused failure must be tracked as baseline,
  not attributed to the rewrite without new evidence.
