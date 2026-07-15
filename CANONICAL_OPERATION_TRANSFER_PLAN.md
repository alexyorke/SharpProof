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
- [x] Capture public API snapshots, representative CLI/JSON/SARIF bytes, package
  contents, seeded fuzz output, and EffectSummary golden output.
- [x] Add a differential harness that compares normalized `SymbolicState`
  instances, support status, unknown/truncation reasons, and provenance.
- [x] Inventory every caller and semantic responsibility in the 14,417-line
  overlap surface; assign each old entry point to a migration slice or document
  why it remains a source-only adapter.
- [x] Define deletion gates for each legacy file or method before introducing
  its replacement.

### Legacy Caller And Deletion Map

Paths below are relative to the repository root. A file marked `adapter` may
remain only as source/CFG traversal or evidence projection; it must not retain
transfer policy after its slice is complete.

| Legacy owner | Direct production callers | Responsibility | Migration slice and deletion gate |
| --- | --- | --- | --- |
| `SymbolicProgramPointFacts` | `SymbolicInvariantService`, `SymbolicReachabilityService`, and the Symbolic statement/branch/loop helpers | Walk containing blocks, order prior statements, construct entry state, expose mutation queries | Phase 1 front-end. Keep only a source-position adapter after every routed operation emits canonical events and differential state parity passes. |
| `SymbolicStatementStateTransfer` | `SymbolicProgramPointFacts`, `SymbolicBranchCompletionStateTransfer` | Route statements; try/catch, using, declaration, completion, and block entry transfer | Phases 2-3. Delete semantic branches after their event lowerers pass state/provenance/truncation parity; retain no independent state mutation. |
| `SymbolicExpressionStateTransfer` | `SymbolicProgramPointFacts`, `SymbolicStatementStateTransfer` | Route assignments, coalesce assignment, increment/decrement, and expression completion | Phase 2. Delete the file after all expression forms use assignment/mutation events and the assignment differential suite passes. |
| `SymbolicAssignmentStateTransfer` | Symbolic expression, loop, program-point, and statement transfer | Declaration/assignment value facts, tuples, elements, nullability, aliases, current-instance members, and throw guards | Phase 2. Delete after simple, compound, tuple, element, nullable, and guarded assignments have canonical parity and no caller remains. |
| `SymbolicTrackedAssignmentStateTransfer` | Analyzer `PuritySymbolicStateFacts` | Shared scalar/reference/collection assignment facts | Phase 2 seed. Fold into the assignment event handler; delete when Symbolic and Analyzer both consume the handler. |
| `SymbolicStateInvalidator` | Symbolic assignment, expression, loop, program-point, and statement transfer | Discover nested mutations and remove invalidated symbol/reference facts | Phase 2. Delete after mutation descriptors own invalidation and alias/version tests pass. |
| `SymbolicNormalCompletionStateTransfer` | Symbolic assignment, branch, expression, and statement transfer | Facts guaranteed by successful expression completion, including index/length and member non-null facts | Phases 2-4. Delete transfer logic after normal-completion and hazard events are canonical; any residual syntax lookup is an adapter. |
| `SymbolicBranchCompletionStateTransfer` | Symbolic assignment, control-flow, loop, and statement transfer | If/switch branch assumptions, branch-local transfer, merge guards, and member postconditions | Phase 3. Delete state mutation after branch/switch events and canonical merge pass differential tests; retain only source branch enumeration if required. |
| `SymbolicLoopStateTransfer` | Symbolic assignment, branch, control-flow, normal-completion, program-point, and statement transfer | Loop entry/body/exit facts, bounded invariants, dependency/mutation discovery | Phase 3. Delete transfer and fixed-point policy after CFG loop events pass loop/reachability parity; source dependency extraction may remain as an adapter. |
| `SymbolicControlFlowCompletionStateTransfer` | Symbolic branch, loop, and statement transfer | Loop completion, break/continue/return/throw reachability, lock/finally completion | Phase 3. Delete after completion events own reachability and exceptional/normal exits. |
| `SymbolicStateMerger` | Analyzer `PurityAnalysisStateMerger`, Symbolic statement transfer | Canonical path-condition choice and bounded merging | Phase 3 kernel owner. Preserve and expand this implementation; delete all competing fact/version/ownership merge policy from callers. |
| `SymbolicRuntimeHazardCandidateFactory` | `SymbolicRuntimeHazardQueryService` | Enumerate syntax candidates and dispatch hazard families | Phase 4. Delete after canonical operation events emit typed exception preconditions for every family. |
| `SymbolicRuntimeHazardSyntaxCandidateFactory` | `SymbolicRuntimeHazardCandidateFactory` | Reconstruct hazard kinds, categories, exception types, and triggers from syntax | Phase 4. Delete semantic reconstruction; retain only source span/evidence extraction for Roslyn gaps. |
| `SymbolicRuntimeHazardIrTriggerFactory`, `SymbolicRuntimeHazardTriggerFactory`, `SymbolicRuntimeHazardKnownGuardFactory` | Candidate factories and each other | Lower conditions, construct checked/range/null/cast/cardinality triggers, attach known guards | Phase 4. Delete family by family after descriptors match ordering, type/category, trigger, proof status, and unsupported outcomes. |
| `SymbolicRuntimeHazardQueryService` | `SymbolicQueryService`, Analyzer `ExceptionFlowQuery.RuntimeHazards`, `SharpProofDiagnosticSuppressor` | Select scope, obtain path state, prove preconditions, classify and project hazards | Phase 4 adapter. Keep query/proof/evidence projection only; remove candidate and trigger semantics. |
| `PurityAssignmentStateTransfer` partials | `PurityAnalysisEngine.Cfg` | Apply writes, aliases, delegate targets, ownership, disposal, borrowing, and caller-visible mutation | Phases 2 and 5. Delete transfer policy after the CFG adapter feeds canonical events and purity state/diagnostic parity passes. |
| `PuritySymbolicStateFacts` | Purity branch/merge/assignment/resource components and assignment, delegate, invocation, ownership, and return rules | Construct/query aliases, borrows, ownership, releases, and assigned symbolic values | Phases 2 and 5. Move mutation constructors to the kernel; retain read-only diagnostic queries only until evidence projection is migrated. |
| `PurityResourceStateFacts` partials | Purity CFG/recursive analysis, assignment transfer, and assignment/field/property/invocation/return rules | Ownership, freshness, escape, dispose/release transitions, and lifetime diagnostics | Phases 2 and 5. Delete transition policy after lifetime events match; keep only analyzer-specific evidence predicates. |
| `PurityAnalysisStateMerger` | `PurityAnalysisEngine.CfgTransfer`, engine initialization, `PuritySymbolicStateFacts` | Merge path states, versions, delegate targets, captures, ownership, and releases | Phases 3 and 5. Replace symbolic merge with the kernel; keep a thin analyzer metadata merge only after state parity and loop convergence pass. |
| `PurityAnalysisEngine.CfgBranchAssumptions` and `.CfgTransfer` | Purity CFG worklist | Apply branch assumptions, propagate successors, merge revisits, and handle completion | Phases 3 and 5. Keep CFG scheduling as an adapter; delete duplicated condition/state semantics when canonical transitions are consumed. |
| `ExceptionPathStateService` partials | `ExceptionFlowQuery.Catches`, `ExceptionFlowQuery.SiteCollection`, `ExceptionSiteClassifier.NullFacts` | Collect exception-site state, mutation-aware dominance, reachability, and throwing-finally shadowing | Phases 3 and 5. Keep exception-site/source queries only; delete transfer/completion interpretation after canonical state and completion edges match. |
| `ExceptionFlowQuery.RuntimeHazards` | `ExceptionFlowQuery.SiteCollection` | Re-query unknown hazards and translate them into exception-flow candidates | Phases 4 and 5. Consume canonical descriptors directly and delete reconstruction after exception ordering/evidence parity passes. |
| Exception-flow catch/callee/property/resource site collectors | `ExceptionFlowQuery.SiteCollection`, `ExceptionFlowAnalyzer` | Language/runtime source discovery and analyzer-specific diagnostic policy | Adapter by design. Preserve source discovery and policy, but route all state, completion, ownership, and hazard semantics through canonical results. |

## Phase 1 - Canonical Operation Model

- [x] Add typed operation descriptors for assignment, mutation, invocation,
  branch assumption, merge, loop edge, completion, lifetime, and hazard events.
- [x] Add an immutable transition result containing normalized state, support
  status, provenance, and conservative unknown/truncation reasons.
- [x] Add one Roslyn `IOperation`/CFG lowering front-end. Retain syntax only for
  source spans, evidence text, and syntax shapes Roslyn does not expose.
  The initial bridge supports local declarations and direct simple assignments;
  unsupported operation shapes remain explicit and later slices extend the same
  front-end instead of adding another interpreter.
- [x] Add adapters from `SymbolicState` and Analyzer purity state into the kernel
  without duplicating transfer policy.
- [x] Add invariants for deterministic event ordering and byte-stable evidence.

## Phase 2 - Assignment, Aliasing, And Lifetime

- [x] Migrate local declarations and simple assignments; shadow-compare old and
  new states, then delete the migrated legacy path.
- [x] Migrate compound assignments, increments/decrements, checked arithmetic,
  and coalesce assignment.
- [x] Migrate tuple/deconstruction assignment and evaluation order.
- [x] Migrate `ref`/`out`, ref-local aliases, invalidation, and version updates.
- [x] Migrate freshness, ownership, borrowing, escape, disposal, and resource
  release transitions.
- [x] Delete superseded assignment and Analyzer purity-state implementations.
- [x] Gate: assignment/lifetime differential suite and affected Analyzer lane are
  green; record net production LOC removed.

## Phase 3 - Branches, Loops, Merge, And Completion

- [x] Migrate boolean branch assumptions and conditional/coalesce flow.
- [x] Migrate switch statement/expression branch selection and merging.
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
| Canonical operation model | `97bb94e4` | 107,856 | +180 |
| Simple-assignment shadow path | `4d970e1a` | 108,046 | +370 |
| Symbolic and Analyzer adapters | `d545c69a` | 108,118 | +442 |
| Scalar assignment migration | `01a36afd` | 108,209 | +533 |
| Computed assignment migration | `38ea0509` | 108,350 | +674 |
| Tuple/deconstruction migration | `611b4e17` | 108,393 | +717 |
| Alias and invalidation migration | `fc40894c` | 108,501 | +825 |
| Ownership lifetime migration | `cf2fa67c` | 108,401 | +725 |
| Tracked assignment deletion | `6fc4e26f` | 108,321 | +645 |
| Purity symbolic query boundary | `fa94a63f` | 108,314 | +638 |
| Exclusive lifetime policy migration | `fc30d99e` | 108,308 | +632 |
| `as` assignment interpreter deletion | `c9b46a46` | 108,251 | +575 |
| String and length assignment deletion | `1ac96cbe` | 108,228 | +552 |
| Purity declaration-state consolidation | `3e853878` | 108,192 | +516 |
| Single written-local update pass | `88ad23a6` | 108,184 | +508 |
| Reference assignment postconditions | `7b236a37` | 108,137 | +461 |
| Reference-backed projection deletion | `3504ebd9` | 108,070 | +394 |
| Nullable assignment postconditions | `6d46d6a8` | 108,085 | +409 |
| Canonical assignment snapshots | `b7478495` | 108,097 | +421 |
| Tuple and finite-array projections | `77237b02` | 108,046 | +370 |
| Kernel-derived assignment bounds | `5e144264` | 107,990 | +314 |
| Nullable snapshot operations | `0b1cfa4c` | 107,987 | +311 |
| Explicit-target assignment operations | `a92660db` | 107,986 | +310 |
| Canonical branch assumptions | `d68de6ab` | 107,939 | +263 |
| Shared guarded branch merging | `2f83046d` | 107,831 | +155 |
| Canonical switch assignment choices | `63033d31` | 107,829 | +153 |

## Validation Ledger

| Checkpoint | Evidence |
| --- | --- |
| Rewrite start | Analyzer/Symbolic focused builds previously green; exception-flow focused lane 207/208 with one failure reproduced on the starting commit. |
| Phase 0 build/test baseline | Release solution build: 0 warnings, 0 errors. MainSmtOracle: 573 passed. MainSmtAnalyzer: 487 passed. MainSmtFlow: 256 passed, 1 failed (the pre-existing SP0010 case). MainSmtCore: 257 passed. MainGeneral: 3,634 passed, 2 skipped. Tooling: 585 passed. Total: 5,792 passed, 1 pre-existing failure, 2 explicit MainGeneral skips. |
| Phase 0 contract baseline | Public API SHA-256: shipped `98C260C649C51451C3BD5629DAF01CDA02ECE7ACEFF1AAF4D39CA6FCF7867D25`, unshipped `B5AFAC50F77E3069B2F1350E068810320D56166F2A7C52E5317F7D7581EA1D4E`. Archive manifests: combined NuGet 17 entries / `c08d68be02c78efced7a080ffbc8eadfd305b91540925f7fd40160a6c614f7a8`; Symbolic NuGet 13 entries / `cc85e4f0f085bc8fb088ed7e44344807731c80594a5d4ac8f382f1791a745dd3`; VSIX 31 entries / `7b876bbb1137084a1eb28e0d863ea33cfcee7bd48708b68ca2131323d4255976`. Existing byte/schema/golden fixtures for CLI, JSON, compact/invariant projection, fuzz, and EffectSummary: 140 passed, 0 failed, 0 skipped. |
| Phase 0 differential harness | `SymbolicStateDifferentialHarness` canonicalizes normalized states and truncation ordering while retaining support, unknown reason, provenance, and truncation dimensions. Focused tests: 2 passed, 0 failed, 0 skipped. |
| Phase 0 deletion map | Exact production references for the state-transfer, runtime-hazard, purity-state, and exception-path owners were enumerated. Every owner now has a migration slice, retained-adapter boundary, and behavior gate in the table above. |
| Phase 1 operation model | Commit `97bb94e4` adds typed descriptors for all nine event families and a normalized immutable transition envelope. Focused model tests: 3 passed, 0 failed, 0 skipped. The temporary +180 production LOC is migration scaffolding that must be repaid when legacy transfer paths are deleted. |
| Phase 1 lowering bridge | Commit `4d970e1a` adds the single `IOperation` front-end and kernel dispatch. Local declaration and simple assignment states shadow-match the legacy path, including previous-value invalidation. Focused model tests: 5 passed, 0 failed, 0 skipped. Total temporary migration scaffolding is +370 production LOC. |
| Phase 1 adapters and ordering | Commit `d545c69a` adds thin Symbolic and Analyzer adapters. The kernel rejects non-increasing event sequences conservatively and preserves evidence order. Focused model tests: 7 passed, 0 failed, 0 skipped. Total temporary migration scaffolding is +442 production LOC. |
| Phase 2 scalar assignments | Commit `01a36afd` routes non-self-referential Boolean, integral, and enum declarations/assignments through the canonical kernel in Symbolic and Analyzer. The superseded scalar-equality branches were removed while reference and self-referential policy remains explicit. Focused affected fixtures: 299 passed; full MainSmtAnalyzer lane: 487 passed. Total temporary migration scaffolding is +533 production LOC. |
| Phase 2 computed assignments | Commit `38ea0509` routes compound assignments, checked/unchecked increment and decrement, and unknown coalesce postconditions through typed assignment/mutation events. The old coalesce interpreter was deleted. Focused transfer/program-point/invariant tests: 113 passed; full MainSmtOracle lane: 573 passed. Total temporary migration scaffolding is +674 production LOC. |
| Phase 2 tuple/deconstruction | Commit `611b4e17` gives Symbolic and Analyzer one nested target/pairing plan, removes both independent target walkers, and routes tuple-local equalities as one ordered binding batch. It also restores legacy provenance/evidence fields on canonical scalar bindings. Focused tuple/program-point/transfer/invariant tests: 119 passed; full MainSmtAnalyzer lane: 487 passed. Total temporary migration scaffolding is +717 production LOC. |
| Phase 2 alias and invalidation | Commit `fc40894c` makes mutation events own variable-prefix and member-path invalidation, centralizes idempotent definition-version calculation, and routes Analyzer reference aliases and ref-local shared/mutable borrows through lifetime events. Focused ref/out, alias, mutation, version, and program-point tests: 35 passed; full MainSmtAnalyzer lane: 487 passed. Total temporary migration scaffolding is +825 production LOC. |
| Phase 2 ownership lifetime | Commit `cf2fa67c` routes fresh ownership, disposable acquisition, return, disposal, release, flow-capture ownership, preserved-alias lifetime, and caller-visible mutation facts through canonical events. The superseded 127-line ownership fact factory was deleted. Focused symbolic IR, resource, disposal, using, return, alias, and transfer tests: 221 passed; full MainSmtAnalyzer lane: 487 passed. Total temporary migration scaffolding fell to +725 production LOC. |
| Phase 2 tracked assignment deletion | Commit `6fc4e26f` folds reference, length, collection, string, null-equivalence, and `as` postconditions into canonical assignment lowering and deletes the 168-line legacy tracked-assignment interpreter. A disposal-alias ordering regression was reproduced and fixed by applying alias evidence after target invalidation. Focused assignment/program-point/alias/resource tests: 140 passed; full MainSmtAnalyzer lane: 487 passed. Total temporary migration scaffolding fell to +645 production LOC. |
| Phase 2 purity symbolic query boundary | Commit `fa94a63f` moves the final assignment, alias, and ref-borrow mutations out of `PuritySymbolicStateFacts`, shares one reference-relationship event adapter, and removes a duplicate declaration-alias application. `PuritySymbolicStateFacts` is now read-only. Focused alias/borrow/resource/purity tests: 66 passed; full MainSmtAnalyzer lane: 487 passed. Total temporary migration scaffolding fell to +638 production LOC. |
| Phase 2 exclusive lifetime policy | Commit `fc30d99e` makes the kernel remove superseded ownership/disposal facts before return and dispose transitions; Analyzer resource code no longer filters symbolic facts. Focused transfer/resource/disposal tests: 62 passed; full MainSmtAnalyzer lane: 487 passed. Total temporary migration scaffolding fell to +632 production LOC. |
| Phase 2 `as` assignment deletion | Commit `c9b46a46` routes Symbolic `as` assignments through the canonical runtime-type/null-condition pipeline with byte-stable provenance and deletes the 68-line syntax interpreter. Focused `as` query/analyzer tests: 28 passed; full MainSmtOracle lane: 573 passed. Total temporary migration scaffolding fell to +575 production LOC. |
| Phase 2 string and length assignment deletion | Commit `1ac96cbe` gives canonical assignment lowering a Symbolic evidence profile, routes string null/equality and collection lower-bound facts through it, and deletes the redundant string and assigned-length interpreters. Focused transfer/program-point tests: 122 passed; full MainSmtOracle lane: 573 passed; full MainSmtAnalyzer lane: 487 passed. Total temporary migration scaffolding fell to +552 production LOC. |
| Phase 2 purity declaration-state consolidation | Commit `3e853878` routes variable declarations through the same concrete-type, owned-array, freshness, null, and canonical assignment updates as ordinary assignments while preserving declaration-only delegate, ref-borrow, and using-resource behavior. Full MainSmtAnalyzer lane: 487 passed; focused ownership/using/alias tests: 111 passed. Total temporary migration scaffolding fell to +516 production LOC. |
| Phase 2 single written-local update pass | Commit `88ad23a6` replaces three ordered traversals of the same written-local set with one traversal while retaining alias snapshots before version, assignment, ownership, and null-state updates. Full MainSmtAnalyzer lane: 487 passed; focused ownership/using/alias tests: 111 passed. Total temporary migration scaffolding fell to +508 production LOC. |
| Phase 2 reference assignment postconditions | Commit `7b236a37` moves reference equality, conditional-reference flow, and definite null/non-null facts into canonical assignment lowering and centralizes exact-null classification. The focused reproduction exposed that null-forgiving syntax can lack a direct Roslyn `IOperation`; removing that unnecessary lowerer dependency restored both definite-null diagnostics. Reproductions: 4 passed; full MainSmtOracle: 573 passed; full MainSmtAnalyzer: 487 passed. Total temporary migration scaffolding fell to +461 production LOC. |
| Phase 2 reference-backed projection deletion | Commit `3504ebd9` replaces the legacy length, exact-list-count, string-content, and multidimensional-array assignment builders with one canonical projection builder consumed by locals and current-instance members. Three Span/Memory regressions exposed a drifted reference-like taxonomy; Symbolic type lowering, state facts, and operation lowering now share one definition. Focused projection tests: 171 passed; full MainSmtOracle: 573 passed; full MainSmtAnalyzer: 487 passed. Total temporary migration scaffolding fell to +394 production LOC. |
| Phase 2 nullable assignment postconditions | Commit `6d46d6a8` teaches canonical assignment lowering to represent `Nullable<T>` as ordered HasValue/value postconditions, moves nullable term identity into the nullable lowerer, and deletes the legacy state-mutating builder. The normalized-state differential fixture and three focused nullable proofs pass; full MainSmtOracle: 573 passed; full MainSmtAnalyzer: 487 passed. This correctness-sensitive two-target support temporarily raises scaffolding to +409 production LOC and must be repaid by the next deletions. |
| Phase 2 canonical assignment snapshots | Commit `b7478495` makes explicitly marked canonical bindings propagate direct-source facts through substitution, routes scalar/reference and tuple-element snapshots through that policy, and leaves only the nullable source-shape adapter outside the kernel. Focused state/tuple/path tests: 136 passed; full MainSmtOracle: 573 passed; full MainSmtAnalyzer: 487 passed. The reusable closure policy temporarily raises scaffolding to +421 production LOC and must enable larger legacy deletions. |
| Phase 2 tuple and finite-array projections | Commit `77237b02` moves tuple literal/source and finite-array element postconditions into canonical assignment lowering, reuses the reference-backed projection builder for tuple string/length/dimension facts, and deletes the parallel state-mutating interpreters and tuple identity helpers. Focused transfer/path/program-point tests: 164 passed; full MainSmtOracle: 573 passed; full MainSmtAnalyzer: 487 passed. Production LOC fell to 108,046, or +370 from the rewrite start. |
| Phase 2 kernel-derived assignment bounds | Commit `5e144264` makes canonical bindings derive positive, nonnegative, and remainder bounds from the pre-state, routes self-referential integer updates through the same binding path, moves the not-null-if-not-null implication into canonical lowering, and deletes the parallel post-assignment proof walkers. Focused nullability/range/self-reference tests: 189 passed; full MainSmtOracle: 573 passed; full MainSmtAnalyzer: 487 passed. Production LOC fell to 107,990, or +314 from the rewrite start. |
| Phase 2 nullable snapshot operations | Commit `0b1cfa4c` represents nullable source/target term pairs as assignment-operation propagations and removes the final post-kernel nullable snapshot mutator. Focused nullable/path/transfer tests: 155 passed; full MainSmtOracle: 573 passed; full MainSmtAnalyzer: 487 passed. Production LOC fell to 107,987, or +311 from the rewrite start. |
| Phase 2 explicit-target assignments and gate | Commit `a92660db` routes current-instance member and element writes through explicit-target assignment operations, preserves the caller-owned invalidation boundary, and deletes both legacy state mutators. Focused member/element/path/transfer tests: 183 passed; full MainSmtOracle: 573 passed; full MainSmtAnalyzer: 487 passed. Exact search finds no migrated assignment mutator or tracked-assignment interpreter. The Phase 2 peak of 108,501 fell by 515 production lines to 107,986; canonical scaffolding remains +310 from the rewrite start and must be repaid by later legacy-engine deletions. |
| Phase 3 canonical branch assumptions | Commit `d68de6ab` routes generic Boolean and reference-null assumptions through `SymbolicBranchAssumptionOperation`, replaces branch-state lowering with one condition result, and removes the redundant null-feasibility probes from coalesce and conditional-access analysis. Focused CFG/null/path/operation tests: 105 passed; Release solution build: zero warnings; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. Production LOC fell to 107,939, or +263 from the rewrite start. |
| Phase 3 shared guarded branch merging | Commit `2f83046d` replaces the separate if/switch common-key, guarded-fact, implication, limit, and truncation builders with one typed guarded-branch merger while preserving distinct if/switch limits and provenance. Focused switch/if/pattern/limit tests: 185 passed; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. Production LOC fell to 107,831, or +155 from the rewrite start. |
| Phase 3 canonical switch assignment choices | Commit `63033d31` moves switch-expression assignment choices into canonical assignment lowering, reuses the guarded-choice constructor for statement and expression merging, and deletes the legacy assignment-state switch interpreter. Focused switch/state tests: 153 passed; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. Production LOC fell to 107,829, or +153 from the rewrite start. |

## Current Checkpoint

- Last updated: 2026-07-15.
- State: Phase 2 is gated; Boolean/reference-null assumptions, guarded switch
  statement merging, and switch-expression assignment choices now share the
  canonical branch/event, assignment, and merge mechanics.
- Last confirmed fact: focused switch/state tests pass 153/153,
  MainSmtOracle passes 573/573, MainSmtAnalyzer passes 487/487, and MainSmtFlow
  matches its 256-pass/one-baseline-failure result after switch migration.
- Next cheapest step: inventory loop entry, back-edge, exit, and bounded
  fixed-point transfer owners, then migrate the first complete loop family into
  canonical loop-edge operations and delete its superseded state mutation.
- Blockers: none. The known SP0010 focused failure must be tracked as baseline,
  not attributed to the rewrite without new evidence.
