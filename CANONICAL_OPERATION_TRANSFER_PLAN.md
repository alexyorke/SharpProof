# Canonical Operation Transfer Rewrite

This file is the source of truth for the production-code reduction goal. At the
start of every continuation, read this file before choosing work. Take the first
unchecked item whose prerequisites are complete. At the end of every green
tranche, update its checkbox, the LOC ledger, validation evidence, commit, and
the checkpoint below.

## Goal

Replace parallel Symbolic and Analyzer interpretations of C# operations with
one canonical operation-transfer kernel, then delete the superseded paths.
Preserve supported behavior, conservative `Unknown` outcomes, CLI forms,
serialized schemas, diagnostics, evidence, and package contents. The public
.NET API may break where doing so enables a smaller canonical implementation.

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
- [x] Migrate loop entry/back-edge/exit transitions and bounded fixed points.
- [x] Migrate try/catch/finally, exceptional completion, and reachability.
- [x] Make one merge implementation own fact choices, versions, ownership, and
  conservative truncation.
- [x] Delete superseded branch, loop, completion, and merge paths.
- [x] Gate: normalized-state, flow, exception, and reachability lanes are green;
  record net production LOC removed.

## Phase 4 - Runtime Hazards Lowered Once

- [x] Emit divide-by-zero, checked-overflow, and conversion preconditions from
  canonical operation events.
- [x] Emit null, nullable-value, and dynamic-binding preconditions. Disposal
  remains a canonical lifetime transition: arbitrary member use has no sound
  universal `ObjectDisposedException` precondition.
- [x] Emit indexing, range, collection-cardinality, and array preconditions.
- [x] Emit cast, switch-no-match, argument, direct-throw, and framework-model
  preconditions.
- [x] Migrate query, suppressor, and exception-flow consumers to the canonical
  descriptors.
- [x] Delete redundant syntax candidate and trigger-construction paths, leaving
  only source-span/evidence adapters.
- [x] Gate: hazard ordering, type/category, proof status, witnesses, and unknown
  outcomes match; record net production LOC removed.

## Phase 5 - Analyzer Consumers Become Adapters

- [x] Migrate purity analysis to consume canonical transitions.
- [x] Migrate nullable and runtime-type analysis to consume canonical state.
- [x] Migrate exception flow to consume canonical hazards and completion edges.
- [x] Centralize Analyzer evidence projection without merging diagnostic policy.
- [x] Remove remaining Analyzer-owned transfer and hazard interpretation.
- [x] Gate: Analyzer lane and diagnostic/evidence fixtures are green; record net
  production LOC removed.

## Phase 6 - Secondary Large Reductions

- [x] Convert EffectSummary wrapper policy to ordered typed structural rules;
  preserve unresolved-external versus resolved-implementation semantics.
  - [x] Replace generated-purity exact/prefix predicate fan-out with ordered
    typed visibility rules while retaining the one structural enumerator
    predicate.
  - [x] Make one typed call-family table own semantic-wrapper exact/prefix
    allowlists and shared call membership.
  - [x] Match Type and RuntimeType wrapper properties by structural identity,
    merging the duplicate Boolean/value shape without broadening call sites.
- [x] Project CLI full/compact/invariant/explain/SARIF formats directly from the
  canonical query graph while preserving serialized bytes.
  - [x] Delete the object-backed invariant result adapter; compact and invariant
    JSON plus their CI gates now read `SymbolicQueryResult` directly.
  - [x] Replace the per-scope full JSON class hierarchy with one canonical
    projection and lock point/line/span/file bytes with SHA-256 fixtures.
  - [x] Route explain invariant projection through its canonical point-scoped
    query result; retain SARIF as a projection of the bounded explain graph.
- [x] Run a bounded primary-constructor conversion over internal data carriers,
  preserving class reference equality where it existed.
  - [x] Convert six private readonly ProofCore carriers without changing their
    struct identity, property shapes, or construction sites.
  - [x] Convert 18 internal/private Symbolic carriers while excluding public
    contract types and mutable budgets, pools, builders, sessions, and services.
- [x] Remove remaining stale or resolved `POTENTIAL_DUPS.md` findings.
- [x] Run two `colgrep --force-cpu` semantic-search batches; stop secondary work
  when each finds fewer than 50 safely removable production lines.

## Phase 7 - Authorized Structural Engine Replacement

The user authorized a major rearchitectural rewrite on 2026-07-15. The Phase 6
low-yield stop remains binding for incidental clone extraction, but it does not
block this bounded replacement of reachable legacy semantic engines. Behavior,
conservative `Unknown`, serialized contracts, and test scenarios remain binding;
the unused preview .NET API may break when it obstructs the canonical design.

- [x] Inventory the reachable structural Symbolic and Analyzer transfer family,
  classify source/evidence adapters separately from semantic owners, and record
  gross and reachable LOC before implementation.
  - All 8,583 handwritten lines in the deletion-map family remain reachable:
    5,863 in the structural Symbolic transfer family, 2,276 in purity transfer,
    state-query, merge, and CFG adapters, and 444 in exception-path/hazard
    adapters. The first replacement pool is the 7,315-line structural plus purity
    transfer/merge/CFG surface. The remaining 1,268 lines are read-only queries,
    source discovery, or diagnostic/evidence projection and are not deletion
    credit unless their consumers migrate too.
- [x] Introduce one CFG/`IOperation` program-point state collector whose block
  transfer, branch assumptions, merge, completion, and fixed-point behavior is
  expressed by canonical operation descriptors and transition results.
  - [x] Route straight-line local/parameter declarations and simple assignments
    through the CFG collector and canonical assignment transition; return typed
    `Unsupported` for branch, cycle, target, or operation shapes that have not
    migrated, preserving the structural collector as the fallback.
  - [x] Add canonical successor assumptions and guarded state merging for
    acyclic branches. Branch-condition mutation falls back to common-state
    merging; branch-local target queries remain on the structural collector
    until capture/version snapshots migrate.
  - [x] Preserve the surviving guarded edge when the alternate acyclic branch
    completes. Lexically branch-local targets still fall back.
  - [x] Route all-path return/throw completion through terminal CFG paths,
    guarded canonical merging, and `SymbolicCompletionOperation`; require an
    unreachable Roslyn target block before producing the contradictory state.
  - [ ] Add bounded loop revisits and finally continuations. Execution roots
    containing loops are rejected up front until loop-carried invalidation and
    fixed-point convergence migrate together.
    - [x] Route direct increment/decrement and compound assignment CFG operations
      through typed computed-update lowering and canonical transitions; reject
      overloads, missing prior values, and unsupported arithmetic.
    - [x] Lower while, do, and for entry/exit conditions, loop-body invariants,
      and back-edge invalidation targets into one typed loop transfer plan.
      Foreach remains typed `Unsupported` until finite-domain lowering migrates.
    - [x] Route mutation-independent while-loop back edges through canonical
      invalidation and a bounded normalized-state worklist. Loop-local targets,
      abrupt exits, condition mutations, do/for exits, and foreach remain typed
      fallbacks until their distinct completion/invariant parity migrates.
    - [x] Route mutation-independent do-loop revisits and guaranteed-body exit
      invalidation through the same worklist. Counted-for lower bounds remain a
      separate fallback rather than weakening normalized-state parity.
    - [x] Route counted-for revisits after reapplying typed monotonic invariants
      on back and exit edges. Nullable reassignment and guarded reference
      projection remain fallback shapes until their state parity migrates.
    - [x] Key the worklist by block plus typed finally continuation, execute
      ordered finally regions before their saved destination, and keep
      finally-local targets on the structural fallback until capture parity.
- [ ] Move pattern binding, finite-domain, loop-bound, framework-postcondition,
  and source-provenance discovery behind typed lowering results; discovery may
  retain Roslyn syntax, but it may not mutate `SymbolicState` directly.
  - [x] Replace the 425-line program-point recursive/list/designation pattern
    interpreter with one canonical pattern condition and branch-assumption
    transition. Recursive designation projection congruence now belongs to
    `SymbolicPatternLowerer`, shared by every consumer.
  - [x] Lower finite `foreach` entry facts into a typed domain plan and apply
    them only through canonical loop-edge transitions. Custom analysis limits
    conservatively retain the structural collector until CFG truncation events
    have exact parity. Finite-element, collection-expression, and prior-assignment
    discovery now belong to the typed lowerer; the legacy program-point helpers
    and operation-lowering dependency are deleted.
  - [x] Lower for/while/do monotonic initializer bounds into a typed invariant
    plan. Bound discovery, mutation-preservation checks, and prior-initializer
    validation now return conditions; only the loop-edge kernel mutates state.
  - [x] Lower `[NotNull]`, inferred parameter-not-null, known argument guards,
    and `[MemberNotNull]` normal-completion semantics into one typed framework
    postcondition plan. Statement and expression consumers apply its ordered
    condition groups through the canonical assumption transition.
- [ ] Migrate source queries, invariant/reachability analysis, exception paths,
  and Analyzer purity CFG consumers in behavior-locked vertical slices.
- [ ] Delete `SymbolicProgramPointFacts`, the statement/expression/assignment,
  branch/loop/completion transfer family, and Analyzer assignment/state wrappers
  once no semantic caller reaches them.
- [ ] If the transfer deletion does not meet the LOC gate, collapse remaining
  unused preview query wrappers into the canonical result graph; keep CLI and
  JSON/SARIF projections byte-compatible.
- [ ] Gate: normalized states, proof outcomes, hazards, diagnostics/evidence,
  unknown/truncation reasons, CLI bytes, and affected test lanes match before
  every legacy-path deletion.

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
| Unified guarded loop break paths | `68417c99` | 107,709 | +33 |
| Canonical completed loop exits | `b1d397db` | 107,706 | +30 |
| Canonical loop body entry | `a3976e31` | 107,645 | -31 |
| Unified loop initializer inputs | `0a5cfed5` | 107,607 | -69 |
| Bounded CFG fixed-point owner | `157e70f6` | 107,581 | -95 |
| Finally path scaffolding deletion | `8d29fc19` | 107,552 | -124 |
| Canonical no-fallthrough completion | `982c23ad` | 107,564 | -112 |
| Canonical try completion merge | `cbe2ecce` | 107,558 | -118 |
| Canonical symbolic fact intersection | `8ad69193` | 107,553 | -123 |
| Canonical ownership joins | `9f6628d5` | 107,509 | -167 |
| Canonical path-state versions | `addf9379` | 107,476 | -200 |
| Canonical guarded branch joins | `64e44777` | 107,460 | -216 |
| Canonical residual flow events | `034ba294` | 107,470 | -206 |
| Canonical divide/remainder hazards | `56ebdb15` | 107,497 | -179 |
| Canonical checked-overflow hazards | `eda4b912` | 107,152 | -524 |
| Canonical null-family hazards | `d15a43af` | 107,158 | -518 |
| Canonical relational bounds hazards | `c91a10ed` | 107,096 | -580 |
| Canonical indexing hazards | `1562e72f` | 107,017 | -659 |
| Canonical slicing bounds hazards | `2b86726f` | 107,029 | -647 |
| Canonical runtime-type hazards | `39e7368a` | 106,918 | -758 |
| Canonical throw and switch hazards | `5fb606aa` | 106,923 | -753 |
| Canonical framework hazards and trigger deletion | `8742afc6` | 106,658 | -1,018 |
| Canonical exception-flow throw consumer | `fe51dd22` | 106,607 | -1,069 |
| Canonical hazard descriptor consumers | `a1b56f96` | 106,583 | -1,093 |
| Residual hazard trigger deletion and Phase 4 gate | `07d5b309` | 106,546 | -1,130 |
| Canonical owned-array purity state | `8d74d8fd` | 106,525 | -1,151 |
| Canonical purity null state | `787bd5f4` | 106,468 | -1,208 |
| Canonical owned flow-capture state | `d08c7179` | 106,442 | -1,234 |
| Canonical purity consumer gate | `50309315` | 106,415 | -1,261 |
| Superseded nullable interpreter deletion | `92bc2b50` | 106,343 | -1,333 |
| Shared exact runtime-type resolution | `640c1426` | 106,282 | -1,394 |
| Canonical exact receiver-type state | `79a54498` | 106,316 | -1,360 |
| Superseded exception path facts | `d1ff9c86` | 106,251 | -1,425 |
| Canonical throwing-finally completion | `0e4d3bb5` | 106,173 | -1,503 |
| Central contract evidence projection | `26cbd9c6` | 106,166 | -1,510 |
| Canonical exception-flow hazard projection | `ecb384c5` | 106,104 | -1,572 |
| Canonical flow-capture invalidation | `ccf50299` | 106,094 | -1,582 |
| Residual Analyzer transfer deletion and Phase 5 gate | `93d0289b` | 106,111 | -1,565 |
| Declarative generated-purity policy | `66115c5c` | 106,003 | -1,673 |
| Typed semantic-wrapper call policy | `015ebf6e` | 105,996 | -1,680 |
| Structural Type wrapper policy and EffectSummary gate | `d93a65d8` | 105,995 | -1,681 |
| Direct invariant and compact CLI projection | `2368e6f9` | 105,956 | -1,720 |
| Canonical full JSON CLI projection | `761924e0` | 105,946 | -1,730 |
| Canonical explain/SARIF projection and CLI gate | `7d4b7b8b` | 105,946 | -1,730 |
| ProofCore primary-constructor carriers | `0c2c7b2b` | 105,910 | -1,766 |
| Symbolic primary-constructor carriers and gate | `18dac9b5` | 105,767 | -1,909 |
| Resolved contract-diagnostic duplicate finding | `62d26b2d` | 105,767 | -1,909 |
| Intentional exception-diagnostic policy finding | `68f26259` | 105,767 | -1,909 |
| Intentional switch-visibility shape finding | `23ce27b2` | 105,767 | -1,909 |
| Shared exception-catalog source registration | `a0069b95` | 105,766 | -1,910 |
| Unified proof path-state encoding | `e5ca3459` | 105,755 | -1,921 |
| Central proof-status projection | `a93f4062` | 105,746 | -1,930 |
| Resolved source-query materialization finding | `2ec1efdd` | 105,746 | -1,930 |
| Shared tooling temporary-source lifecycle | `71ec1af7` | 105,746 | -1,930 |
| Shared compact CLI test contracts | `52f1b63c` | 105,746 | -1,930 |
| Shared SARIF input materialization | `26b5b233` | 105,745 | -1,931 |
| Shared production-source inventory | `2a7cce39` | 105,745 | -1,931 |
| Unified raw-SMT line scanning | `c7ec8e42` | 105,745 | -1,931 |
| Intentional compact-domain projection boundary | `49c99dd2` | 105,745 | -1,931 |
| Unified artifact build entry points | `45585697` | 105,745 | -1,931 |
| Unified method-body operation resolution | `d54111b2` | 105,720 | -1,956 |
| Unshipped legacy package script deletion | `c58f50e7` | 105,720 | -1,956 |
| Unused exception type display format deletion | `3c3cd2d4` | 105,716 | -1,960 |
| Intentional compilation host boundaries | `4a8cf2fe` | 105,716 | -1,960 |
| Intentional analyzer distribution closures | `9455928a` | 105,716 | -1,960 |
| Resolved ProofCore fixed-point finding | `462c13e9` | 105,716 | -1,960 |
| Shared Semantic Oracle source programs | `ba8a19eb` | 105,716 | -1,960 |
| Resolved ownership-fact boilerplate finding | `38dcab8d` | 105,716 | -1,960 |
| Resolved Dispose-matching finding | `502aa541` | 105,716 | -1,960 |
| Resolved SMT canonicalization findings | `916d8a17` | 105,716 | -1,960 |
| Resolved SMT division finding | `e51227f6` | 105,716 | -1,960 |
| Intentional conditional-value readers | `eb8c6ae2` | 105,716 | -1,960 |
| Resolved string-length fact finding | `705b4439` | 105,716 | -1,960 |
| Intentional SMT domain fact merges | `3be4f5e8` | 105,716 | -1,960 |
| Intentional shared catalog namespace | `d49e15ca` | 105,716 | -1,960 |
| Central compact-result schema metadata | `23992c39` | 105,708 | -1,968 |
| Central compact SMT diagnostics projection | `2562ff6f` | 105,707 | -1,969 |
| Resolved invariant scope-adapter finding | `cee2af96` | 105,707 | -1,969 |
| Semantic-search residual adapter deletion | `6773fdc0` | 105,585 | -2,091 |
| Phase 7 straight-line CFG collector | `1b67e81a` | 105,762 | -1,914 |
| Phase 7 acyclic CFG branch transfer | `c5e685d3` | 105,970 | -1,706 |
| Phase 7 single-survivor completion | `cdc1a701` | 105,987 | -1,689 |
| Phase 7 all-path CFG completion | `4a51a3e0` | 106,015 | -1,661 |
| Phase 7 computed CFG updates | `7f6b45fb` | 106,108 | -1,568 |
| Phase 7 typed loop transfer plan | `341eac5b` | 106,234 | -1,442 |
| Phase 7 bounded while-loop revisits | `21ebedb1` | 106,324 | -1,352 |
| Phase 7 bounded do-loop revisits | `f0a1f91d` | 106,361 | -1,315 |
| Phase 7 bounded counted-for revisits | `f596da0c` | 106,421 | -1,255 |
| Phase 7 typed finally continuations | `98747716` | 106,528 | -1,148 |
| Phase 7 canonical pattern binding | `c4adcade` | 106,237 | -1,439 |

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
| Phase 3 guarded loop break paths | Commit `68417c99` replaces separate direct, nested, and continue-before-break interpreters with one structural enclosing-guard and fallthrough collector while retaining mutation checks and conservative rejection. Focused loop-exit/path tests: 63 passed; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. Production LOC fell to 107,709, or +33 from the rewrite start. |
| Phase 3 canonical completed loop exits | Commit `b1d397db` makes the operation kernel own `SymbolicLoopEdgeOperation`, routes guarded while/for/do exits through it, and replaces six loop-kind exit arms with one conditional-loop dispatcher. Normal-condition exits retain the existing inline-assignment lowering. Focused loop-exit/path/kernel tests: 86 passed. Production LOC fell to 107,706, or +30 from the rewrite start. |
| Phase 3 canonical loop body entry | Commit `a3976e31` replaces the duplicated while/do/for/foreach body-entry switches in the program-point and block walkers with one loop adapter, and routes invariant application through entry/exit loop-edge events. Focused loop/program-point/kernel tests: 167 passed; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. Production LOC fell below the rewrite start to 107,645, or -31. |
| Phase 3 unified loop initializer inputs | Commit `0a5cfed5` makes one typed initializer stream own for-loop assignment/declaration discovery, shares bound extraction across for/while/do, and applies initializer-target invalidation as one canonical mutation event. Focused loop/program-point/kernel tests: 167 passed; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. Production LOC fell to 107,607, or -69 from the rewrite start. |
| Phase 3 bounded CFG fixed point | Commit `157e70f6` encapsulates Analyzer CFG queue membership, entry/exit state maps, revisit merging, finally-continuation propagation, and the bounded iteration budget in one fixed-point owner. Canonical path-state merging remains unchanged. Focused loop/CFG/finally/resource tests: 46 passed; MainSmtAnalyzer: 487 passed. Production LOC fell to 107,581, or -95 from the rewrite start. |
| Phase 3 finally path cleanup | Commit `8d29fc19` removes an unused condition-mutation interpreter and a one-call statement-state wrapper from path-sensitive throwing-finally proof while preserving the canonical reachability query. Focused exception/finally tests: 136 passed. Production LOC fell to 107,552, or -124 from the rewrite start. |
| Phase 3 canonical no-fallthrough completion | Commit `982c23ad` makes `SymbolicCompletionOperation` own contradictory normal-flow termination for branches, loops, impossible null branches, exhausted try alternatives, and throwing finally blocks, deleting the standalone contradictory-state constructor. Focused completion/loop/try tests: 172 passed. The reusable completion scaffolding raises production LOC to 107,564, or -112 from the rewrite start, and must be repaid by completion-path deletion. |
| Phase 3 canonical try completion merge | Commit `cbe2ecce` routes try/catch alternative states through `SymbolicMergeOperation`, moves limited common fact/condition/version selection into the canonical merger, unifies try/catch block completion collection, and deletes the source-owned merge. Focused completion/limit/exception tests: 162 passed; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. Production LOC fell to 107,558, or -118 from the rewrite start. |
| Phase 3 canonical fact intersection | Commit `8ad69193` makes `SymbolicStateMerger` own incoming-state fact traversal while allowing the Analyzer to retain its evidence-aware identity predicate, and deletes the duplicate Analyzer intersection loop. Focused merge/resource/CFG/try tests: 102 passed. Production LOC fell to 107,553, or -123 from the rewrite start. |
| Phase 3 canonical ownership joins | Commit `9f6628d5` moves all-path release, outstanding obligation, alias traversal, and evidence-aware ownership merging into `SymbolicStateMerger`, deletes the Analyzer implementation, and removes the circular dependency between the merger and `PuritySymbolicStateFacts`. Focused ownership/alias/CFG/try tests: 137 passed; MainSmtAnalyzer: 487 passed. Production LOC fell to 107,509, or -167 from the rewrite start. |
| Phase 3 canonical path-state versions | Commit `addf9379` removes Analyzer's parallel `ISymbol` version map and makes `SymbolicState.SymbolVersions` own definition versions, phi joins, IR rewriting, equality, hashing, and convergence. Focused version/CFG/ownership tests: 228 passed; MainSmtAnalyzer: 487 passed. Production LOC fell to 107,476, or -200 from the rewrite start. |
| Phase 3 canonical guarded branch joins | Commit `64e44777` moves common-state intersection, guarded branch choices, merge limits, and truncation recording into `SymbolicStateMerger` and deletes the branch-owned implementation. Focused switch/path/pattern/limit tests: 154 passed; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. Production LOC fell to 107,460, or -216 from the rewrite start. |
| Phase 3 residual flow events and gate | Commit `034ba294` routes switch-section assumptions, switch-exit exclusions, finite-foreach domains, loop length facts, and branch invalidations through canonical branch, loop-edge, and mutation events. Branch, loop, and completion owners now retain source/CFG discovery but no independent condition or invalidation mutation. Release Symbolic build: zero warnings; focused operation/branch/loop/state/limit tests: 218 passed; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. Production LOC is 107,470, or -206 from the rewrite start; the ten-line cost removes the final flow mutation escape hatches and closes Phase 3. |
| Phase 4 canonical divide/remainder hazards | Commit `56ebdb15` lowers binary and compound divide/remainder operations into typed `SymbolicHazardOperation` descriptors, preserves exact and unsupported confidence/provenance, projects candidates from the descriptor, and deletes the dedicated syntax trigger builder. Direct descriptor plus arithmetic hazard/evidence tests: 143 passed; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. Reusable hazard-operation scaffolding temporarily raises production LOC to 107,497, or -179 from the rewrite start, and must be repaid by checked-overflow and conversion deletions. |
| Phase 4 canonical checked-overflow hazards | Commit `eda4b912` lowers checked binary, signed-division, unary, increment/decrement, compound-assignment, and explicit numeric-conversion overflow into typed hazard operations and deletes the parallel syntax candidate, operator/range, trigger, and fallback builders. Focused operation/exception/authoring tests: 148 passed; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. The slice removes 345 net production lines, bringing production LOC to 107,152, or -524 from the rewrite start, and completes the first Phase 4 item. |
| Phase 4 canonical null-family hazards | Commit `d15a43af` lowers null dereference, argument-null, unbox-null, nullable-value, and dynamic-null binding preconditions into typed hazard operations, including the loop-carried nullable special case, and deletes the six dedicated legacy trigger builders. Disposal remains canonical lifetime state because arbitrary use after disposal does not universally throw `ObjectDisposedException`; existing SP0002 disposal evidence tests characterize that boundary. Direct and focused hazard/evidence tests: 259 passed; post-consolidation focused hazard tests: 255 passed; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. Production LOC is 107,158, or -518 from the rewrite start; the six-line bridge cost must be repaid when the remaining candidate adapters are deleted. |
| Phase 4 canonical relational bounds hazards | Commit `c91a10ed` lowers negative array/stackalloc lengths and collection cardinality into typed operations, preserves conservative mixed-dimension unsupported aggregates and their exact subject, and deletes the dedicated relational trigger builders plus the legacy aggregate trigger API. Focused descriptor, hazard, exception, and authoring tests: 259 passed. Production LOC fell by 62 lines to 107,096, or -580 from the rewrite start. |
| Phase 4 canonical indexing hazards | Commit `1562e72f` lowers built-in element access, safe `Math.Abs` modulo indexes, multidimensional array access, and `Array.GetValue` bounds through canonical hazard operations and deletes four legacy index trigger builders. Focused descriptor, semantic-oracle, exception, authoring, and unknown-hazard tests: 259 passed. Production LOC fell by 79 lines to 107,017, or -659 from the rewrite start. |
| Phase 4 canonical slicing bounds hazards | Commit `2b86726f` moves slicing and `Index` construction argument-range semantics into canonical hazard operations. Together with the relational and indexing slices, all indexing, range, collection-cardinality, negative-length, and array-bounds preconditions now have typed operation owners; array-store mismatch remains correctly grouped with the following cast/type-compatibility family. Focused tests: 259 passed; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. The completed bounds batch is net -67 lines from its 107,096 checkpoint; production LOC is 107,029, or -647 from the rewrite start. |
| Phase 4 canonical runtime-type hazards | Commit `39e7368a` moves invalid reference casts, unboxing mismatches, and covariant array-store mismatches into canonical operation lowering while preserving exact runtime-type checks, null behavior, unsupported subjects, bounds guards, and evidence provenance. The three legacy trigger/candidate semantic blocks were deleted. Focused runtime-hazard, semantic-oracle, exception, authoring, and unknown tests: 258 passed. Production LOC fell by 111 lines to 106,918, or -758 from the rewrite start. |
| Phase 4 canonical throw and switch hazards | Commit `5fb606aa` moves direct throw, rethrow, throw-null partitioning, and switch-expression no-match into canonical operation lowering and deletes both legacy trigger owners. The focused gate exposed and fixed an immutable-builder capacity bug in single-hazard throws before commit. Focused runtime-hazard, semantic-oracle, exception, authoring, and unknown tests: 258 passed. The five-line bridge raises production LOC to 106,923, or -753 from the rewrite start, and must be repaid by final framework-model and trigger-factory deletion. |
| Phase 4 canonical framework hazards and trigger deletion | Commit `8742afc6` moves `Math.Abs`, `Math.Clamp`, and known argument-guard preconditions into canonical operation lowering, relocates the remaining array shape adapter, and deletes both legacy trigger-factory files plus the obsolete trigger carrier. Exact search confirms every `RuntimeHazardCandidate` now projects from `SymbolicHazardOperation`. Focused hazard tests: 258 passed; Release test build: zero warnings; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. The slice removes 265 production lines, bringing production LOC to 106,658, or -1,018 from the rewrite start; the complete fourth Phase 4 item removes 371 lines from its 107,029 checkpoint. |
| Phase 4 canonical exception-flow throw consumer | Commit `fe51dd22` replaces exception flow's independent throw/rethrow/null discovery and classification with canonical proven runtime hazards while preserving its position before callee entries and the shared catch/finally/reachability filters. The now-unused throw classifier and rethrow helpers were deleted. Focused exception, semantic-oracle, and unknown-hazard tests: 189 passed. Production LOC fell by 51 lines to 106,607, or -1,069 from the rewrite start. |
| Phase 4 canonical hazard descriptor consumers | Commit `a1b56f96` retains each `SymbolicHazardOperation` through query classification, derives the public compatibility projection from that descriptor, and shares one per-method hazard query between exception summaries and unknown diagnostics. Exception flow reads direct-throw identity from the descriptor; the suppressor already consumes the same canonical query service and required no parallel classifier. Release test build: zero warnings; focused exception, hazard, unknown-diagnostic, authoring, and suppressor tests: 234 passed; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. Production LOC fell to 106,583, or -1,093 from the rewrite start. |
| Phase 4 residual trigger deletion and gate | Commit `07d5b309` deletes the final known-guard wrapper and unused fact-trigger projection, and moves loop-carried nullable descriptor construction behind `SymbolicOperationLowerer`. Exact search finds `SymbolicHazardOperation` construction only in that lowerer; the remaining syntax candidate code discovers source/`IOperation` shapes and spans but delegates trigger semantics to canonical lowering. Release test build: zero warnings; focused descriptor, hazard, unknown, authoring, exception, and suppressor tests: 290 passed; MainSmtOracle: 573 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. Phase 4 closes at 106,546 production LOC, or -1,130 from the rewrite start. |
| Phase 5 canonical owned-array purity state | Commit `8d74d8fd` removes Analyzer's parallel owned-local-array symbol set and derives the policy query from exact canonical ownership facts emitted by the lifetime transition. Reassignment, `ref`/`out`, branch merge, capture, mutation, and return behavior remains versioned by `SymbolicState`. Release test build: zero warnings; focused array, assignment, merge, ref, capture, and return tests: 170 passed; MainSmtAnalyzer: 487 passed. Production LOC fell to 106,525, or -1,151 from the rewrite start. |
| Phase 5 canonical purity null state | Commit `787bd5f4` removes Analyzer's definitely-null-local set, its independent merge/equality/hash policy, and the bridge that re-injected those values before reachability proofs. Purity null and coalesce queries now read versioned canonical reference-null facts directly. Release test build: zero warnings; focused null, branch, coalesce, and purity tests: 117 passed; MainSmtAnalyzer: 487 passed; the internal characterization fixture remains green after the retired adapter parameter was removed. Production LOC fell to 106,468, or -1,208 from the rewrite start. |
| Phase 5 canonical owned flow-capture state | Commit `d08c7179` removes the parallel owned-array flow-capture set and derives capture ownership from canonical freshness/ownership facts over the existing synthetic capture term. Canonical path merging now owns all-path retention. Release test build: zero warnings; focused array, collection, lambda, capture, and purity tests: 143 passed; MainSmtAnalyzer: 487 passed. The adjacent local concrete-type map was audited and retained because it stores exact `INamedTypeSymbol` identity while a canonical type-test atom proves assignability, not exact runtime type. Production LOC fell to 106,442, or -1,234 from the rewrite start. |
| Phase 5 canonical purity consumer gate | Commit `50309315` removes a second disposable-acquisition pass, the now-dead using-declarator classifier, and a resource-specific non-null fact already emitted by canonical assignment lowering. Exact audit finds purity path semantics entering through canonical assignment, lifetime, mutation, branch-assumption, or merge transitions; retained delegate targets, exact Roslyn types, flow-capture results, and capture-source maps are Analyzer metadata rather than competing symbolic transfer. Release test build: zero warnings; focused using, disposal, alias, mutation, and resource tests: 181 passed; MainSmtAnalyzer: 487 passed; MainGeneral: 3,676 passed and the two documented reflection skips. The first Phase 5 item closes at 106,415 production LOC, or -1,261 from the rewrite start. |
| Phase 5 superseded nullable interpreter deletion | Commit `92bc2b50` deletes the unreferenced exception-site null interpreter that recursively reconstructed cast/default/dominating-if state, plus its now-dead exception syntax helpers and reference-like wrapper. Nullable contract verification already enters through the canonical query service; Roslyn nullable flow remains only a conservative fallback when canonical proof is unknown. Release test build: zero warnings; focused nullable, null-forgiving, reachability, and exception-flow tests: 163 passed. Production LOC fell to 106,343, or -1,333 from the rewrite start. |
| Phase 5 shared exact runtime-type resolution | Commit `640c1426` routes exception method/property dispatch through `SymbolicRuntimeTypeFacts`, extends that shared resolver with same-type conditional/coalesce merges, and deletes exception flow's independent statement-scanning exact-local dictionary. The strengthened fixture proves a conditional local followed by a same-type coalesce still reaches the exact property implementation. Release test build: zero warnings; focused exact-dispatch and exception-flow tests: 60 passed plus the strengthened focused reproduction; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed and only the documented baseline SP0010 failure. Production LOC fell to 106,282, or -1,394 from the rewrite start. |
| Phase 5 canonical exact receiver-type state | Commit `79a54498` replaces purity's local and flow-capture concrete-type dictionaries, independent intersections, equality, and hashing with versioned `SymbolicExactRuntimeTypeAtom` facts. Assignment invalidation and canonical state merging now own their lifetime; the post-CFG probe explicitly projects only exact-type metadata instead of retaining a parallel map. The 34-line canonical fact scaffold raises production LOC to 106,316, or -1,360 from the rewrite start, but deletes both competing state stores and enables later Analyzer-state deletion. Release build: zero warnings; exact-dispatch/using/disposal tests: 58 passed; focused CFG regression set: 15 passed; MainSmtAnalyzer: 487 passed. |
| Phase 5 superseded exception path facts | Commit `d1ff9c86` deletes the unreferenced null/zero dominating-if interpreter and its retired `PathFactKind`, then removes the exception-site wrapper whose `relevantRoot` parameter was ignored. Catch filters and path-sensitive finally checks now call the canonical path-state collector directly. Release test build: zero warnings; focused exception, path, catch-filter, and finally tests: 219 passed, with only the documented baseline SP0010 failure. Production LOC fell to 106,251, or -1,425 from the rewrite start. |
| Phase 5 canonical throwing-finally completion | Commit `0e4d3bb5` replaces exception flow's recursive block/if exit interpreter with canonical completed-block transfer followed by the shared reachability proof. This preserves path-sensitive one-branch and all-branch exits while deleting 89 lines of duplicate completion policy. Direct finally tests: 17 passed; focused exception/path/catch/finally batch: 219 passed with only baseline SP0010. Production LOC fell to 106,173, or -1,503 from the rewrite start. Exception flow now consumes canonical hazard descriptors, path state, and completion semantics, closing the second Phase 5 item. |
| Phase 5 central contract evidence projection | Commit `26cbd9c6` makes one typed Requires/Ensures projector own family keys, baseline identity, proof status, structured unknown reason, truncation, and explain metadata while each analyzer retains rule selection, reporting conditions, messages, and evidence-key policy. Inferred-contract and invalid-contract diagnostics now use the same baseline-plus-explain envelope; syntax-tree fallback and trusted-boundary paths remain separate because their identity or explain policy differs. Release Analyzer build: zero warnings; direct envelope and adjacent contract/suggestion tests: 16 passed; MainSmtAnalyzer: 487 passed. Production LOC fell to 106,166, or -1,510 from the rewrite start. |
| Phase 5 canonical exception-flow hazard projection | Commit `ecb384c5` replaces 19 independent proven-hazard family loops with one ordered compatibility projection. Exception type comes directly from the canonical hazard descriptor; the adapter retains only source labels, legacy category normalization, supported-site filtering, and the established direct-throw/callee/family order. Release Analyzer build: zero warnings; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed with only the documented baseline SP0010 failure. Production LOC fell by 62 lines to 106,104, or -1,572 from the rewrite start. |
| Phase 5 canonical flow-capture invalidation | Commit `ccf50299` invalidates a reassigned CFG flow capture through `SymbolicOperationTransferKernel.Invalidate` before applying exact runtime-type and ownership facts, and deletes Analyzer's owned-array-only fact filter and direct `SymbolicState` reconstruction. The new mixed fresh/external conditional-array regression characterizes conservative caller-visible mutation. Release Analyzer build: zero warnings; focused array/capture/dispatch tests: 35 passed; MainSmtAnalyzer: 487 passed. Production LOC fell by 10 lines to 106,094, or -1,582 from the rewrite start. |
| Phase 5 residual Analyzer transfer deletion and gate | Commit `93d0289b` routes switch evaluation alias/selection conditions through canonical branch-assumption transitions and makes invalidation descriptors own definition-version updates. Exact mutation search finds no Analyzer live-transfer call to `AddFact`, `AddPathCondition`, `RemoveFacts`, or `WithSymbolVersion`; remaining state construction is limited to contract entry, Ensures snapshots, empty queries, and container defaults. Analyzer runtime-hazard code queries and compatibility-projects canonical descriptors without reconstructing triggers. Release Analyzer build: zero warnings; focused transfer/switch tests: 53 passed; diagnostic/evidence fixtures: 371 passed; MainSmtAnalyzer: 487 passed; MainSmtFlow: 256 passed with only the documented baseline SP0010 failure. Phase 5 closes at 106,111 production LOC, or -1,565 from the rewrite start. The 17-line increase from the prior checkpoint centralizes correctness-sensitive version ownership and is retained for the later legacy-engine deletions. |
| Phase 6 declarative generated-purity policy | Commit `66115c5c` replaces 24 one-off generated-purity predicate methods with three ordered visibility rules over exact symbols, prefixes, and the retained immutable-hash-set enumerator predicate. The resolved-summary and unresolved-external entry points remain separate and consume the same table without changing their conservative fallback. Literal inventory accounts for all 153 former `System.*` patterns: 152 live in the table and the remaining containing-type prefix is owned by the structural enumerator predicate. Release EffectSummary build: zero warnings; focused runtime/string/type/cross-assembly/unresolved tests: 12 passed; full Tooling lane: 585 passed. Production LOC fell by 108 lines to 106,003, or -1,673 from the rewrite start. |
| Phase 6 typed semantic-wrapper call policy | Commit `015ebf6e` moves 70 distinct semantic-wrapper call patterns into one typed family/match-kind table, with composition retained for string-span, allocation, argument-guard, and scratch-buffer policies. Literal parity finds no former call pattern missing. The unresolved-external/resolved-summary entry points are unchanged. Release EffectSummary build: zero warnings; focused wrapper/boundary tests: 12 passed; full Tooling lane: 585 passed. The correctness centralization is also net-negative, bringing production LOC to 105,996, or -1,680 from the rewrite start. |
| Phase 6 structural Type wrapper policy and gate | Commit `d93a65d8` merges the duplicate Type Boolean/value wrapper classifiers and matches Type/RuntimeType parameterless properties through containing metadata type, method kind, arity, parameters, return type, typed call-site shape, and dynamic-dispatch evidence. The ordered semantic-wrapper table and typed call-family table now own wrapper policy; remaining custom predicates express genuinely distinct field/return/allocation shapes. Focused Type/RuntimeType/MemberInfo/unresolved/cross-assembly tests: 7 passed; full Tooling lane: 585 passed; Release EffectSummary build: zero warnings. The resolved implementation and conservative unresolved-external paths remain separate. Production LOC is 105,995, or -1,681 from the rewrite start. |
| Phase 6 direct invariant and compact CLI projection | Commit `2368e6f9` deletes the object-backed invariant adapter and makes compact/invariant serialization, proof gates, unknown thresholds, and truncation gates consume `SymbolicQueryResult` directly. The serialized result types are unchanged. A stale runtime-hazard characterization was first updated in `de7fda18` to construct the already-canonical descriptor without changing its assertions. Release CLI and Tooling-test builds: zero warnings; direct descriptor characterization: 1 passed; CLI output/gate fixtures: 82 passed. Production LOC fell by 39 lines to 105,956, or -1,720 from the rewrite start. |
| Phase 6 canonical full JSON CLI projection | Commit `761924e0` replaces the line/span/file inheritance hierarchy and its dispatch adapter with one scope-aware view over `SymbolicQueryResult`; point output remains the canonical program-point object. Four normalized-output SHA-256 fixtures characterize exact full JSON bytes before the migration and pass unchanged afterward. Release CLI build: zero warnings; byte fixtures: 4 passed; broader CLI fixtures: 89 passed. Production LOC fell by 10 lines to 105,946, or -1,730 from the rewrite start. |
| Phase 6 canonical explain/SARIF projection and CLI gate | Commit `7d4b7b8b` makes explain compact its canonical point-scoped `SymbolicQueryResult` rather than rebuilding a query wrapper from the selected program point. SARIF remains a serialization-only view of that bounded explain graph and contains no independent invariant interpretation. Exact explain JSON and SARIF SHA-256 fixtures pass 2/2; the focused report suite passes 9/9; the full Tooling lane passes 590/590. Release CLI build remains at zero warnings. Production LOC is unchanged at 105,946, or -1,730 from the rewrite start, and the second Phase 6 item is closed. |
| Phase 6 ProofCore primary-constructor carriers | Commit `0c2c7b2b` converts six private readonly regex/fact-preprocessing structs selected by IDE0290 to C# primary constructors. Their struct value semantics and public property shapes remain unchanged; the touched files are LF/no-BOM. Release ProofCore build: zero warnings; focused ProofCore Z3, SMT service, and string reasoning fixtures: 244 passed. Production LOC fell by 36 lines to 105,910, or -1,766 from the rewrite start. |
| Phase 6 Symbolic primary-constructor carriers and gate | Commit `18dac9b5` converts 18 internal/private immutable carriers selected by IDE0290 while preserving each class/readonly-struct kind, reference/value equality behavior, property shape, validation, and construction sites. The residual IDE0290 inventory contains only public contract models or mutable builders, budgets, pools, analysis sessions, and services, so the bounded carrier scope is exhausted. Release Symbolic build: zero warnings; focused invariant/source-query, capability, indexing/hazard, proof, IR, and SMT-cache fixtures: 493 passed. All touched files are LF/no-BOM. Production LOC fell to 105,767, or -1,909 from the rewrite start. |
| Phase 6 resolved Requires/Ensures duplicate finding | Commit `62d26b2d` removes the stale first `POTENTIAL_DUPS.md` entry. `ContractDiagnosticSupport.CreateProofProperties` already owns the shared Requires/Ensures baseline, proof, structured-unknown, truncation, and explain envelope; rule selection, locations, message arguments, and `Diagnostic.Create` remain intentionally local diagnostic policy. Focused Requires, Ensures, explain, baseline, truncation, and unknown-reason fixtures: 86 passed. Production LOC remains 105,767, or -1,909 from the rewrite start. |
| Phase 6 intentional exception-diagnostic policy finding | Commit `68f26259` removes the stale exception-envelope report entry. All four paths already share `AnalyzerDiagnosticProperties.AddBaselineAndExplain` and `AnalyzerDiagnosticReporter.ReportIfNotSuppressed`; their descriptors, evidence keys, additional locations, proof/unknown metadata, and message arguments are distinct policy, and another wrapper would add parameter plumbing without centralizing new semantics. Focused summary, unknown-hazard, reachability, propagation, handling, contract, explain, baseline, and authoring fixtures: 194 passed. Production LOC remains 105,767, or -1,909 from the rewrite start. |
| Phase 6 intentional switch-visibility shape finding | Commit `23ce27b2` removes the switch proof-pipeline entry. Switch expressions and statements already share `IsSymbolicConditionAlwaysFalseAt`; the residual code performs distinct Roslyn arm/section selection and lowering, while statements alone must preserve reachable constant `goto case/default` targets. A callback abstraction would add code and obscure the soundness exemption. Focused switch, path-fact, reference-reachability, pattern, and exact-dispatch fixtures: 69 passed. Production LOC remains 105,767, or -1,909 from the rewrite start. |
| Phase 6 shared exception-catalog source registration | Commit `a0069b95` makes one helper own exception-type and optional source-path registration for both source and edge JSON ingestion, preserving ordinal sorting, deduplication, and malformed-entry skips. Source facts and edge facts remain separate because their schemas intentionally differ in call-chain, callee, depth, and source-path semantics. Release Analyzer build: zero warnings; focused catalog validation, exception catalog, propagation, recursion, and handling fixtures: 49 passed. Production LOC is 105,766, or -1,910 from the rewrite start. |
| Phase 6 unified proof path-state encoding | Commit `e5ca3459` routes `SymbolicFact` through the canonical `SymbolicFactCondition` representation and the single condition path-state encoding pipeline. The fact path preserves its version-rewrite opt-out, exact fact polarity/confidence encoding, contradictory-state bypass, atom-level divisor scan, and conservative failure; the duplicate wrapper and fact divisor overload are deleted. Release Symbolic build: zero warnings; focused invariant, IR, proof-pipeline, program-point, path-sensitive, reachability, SMT-service, expression-atom, and syntactic-classifier fixtures: 435 passed. Production LOC fell to 105,755, or -1,921 from the rewrite start. |
| Phase 6 central proof-status projection | Commit `a93f4062` moves truth-value, condition-summary, and runtime-hazard mappings into typed overloads on `SymbolicProofProjection`. Each overload retains its domain-specific proven values and conservative `Unknown` default. Seventeen table cases cover every current enum member plus unknown numeric values; focused proof/query tests pass 267 and runtime-hazard/serialized projection tests pass 281. Release Symbolic build: zero warnings. Production LOC fell to 105,746, or -1,930 from the rewrite start. |
| Phase 6 resolved source-query materialization finding | Commit `2ec1efdd` removes the stale line/span query entry. Line, line-point, and span paths already share the full `AnalyzeAndProjectNode` materializer; only their node selectors, nearest-point logic, and scope metadata remain distinct. Wrapping the two short `Select` calls would add code without centralizing semantics. Focused program-point/query/path tests pass 113 and tooling source-query/projection/JSON tests pass 114. Production LOC remains 105,746, or -1,930 from the rewrite start. |
| Phase 6 shared tooling temporary-source lifecycle | Commit `71ec1af7` introduces one disposable `TemporarySourceFile` fixture and migrates all nine capability, complexity, and standalone-profile CLI tests from repeated GUID/write/`try/finally`/delete blocks. Test names, sources, commands, and assertions are unchanged; `using` now owns cleanup even on failure. Focused fixtures pass 9/9 and the Release ToolingTest build has zero warnings. The tranche removes 51 test lines, bringing tracked test LOC to 142,428; production LOC remains 105,746, or -1,930 from the rewrite start. |
| Phase 6 shared compact CLI test contracts | Commit `52f1b63c` centralizes compact JSON kind/schema/evidence assertions and capability/complexity `--all-lines` rejection checks while retaining the original test methods and feature-specific payload assertions. The shared envelope now also locks `schemaVersion`. Focused compact, capability, complexity, and source-query fixtures pass 108/108; Release ToolingTest build: zero warnings. Test LOC fell by 18 to 142,410; production LOC remains 105,746, or -1,930 from the rewrite start. |
| Phase 6 shared SARIF input materialization | Commit `26b5b233` makes the linked `DotnetSarifBuildRunner` own ordered `.sln`/`.csproj` materialization and disposable temporary-file cleanup for Baseline and CorpusReport. The focused reproduction exposed that an up-to-date incremental build can skip compilation and produce no SARIF, so the shared runner now uses `--no-incremental`. A mixed direct-SARIF/project regression locks input order and zero leaked materialized files. Both Release tool builds have zero warnings; focused Baseline, CorpusReport, and process-ownership fixtures pass 28/28. Production LOC is 105,745, or -1,931 from the rewrite start; tracked test LOC is 142,482 after the behavioral regression. |
| Phase 6 shared production-source inventory | Commit `2a7cce39` makes one PowerShell helper own strict repository containment, normalized relative paths, and production C# exclusions for ProductionMetrics, RawSmtHotspots, and CloneInventory. This fixes CloneInventory's missing outside-root guard and replaces eight repeated module scans while retaining each report's narrower semantic filters. Complete JSON output from all three scripts matches the committed predecessors byte-for-byte by SHA-256; direct root/outside/test/build-output probes pass; focused process-ownership fixtures pass 9/9. The audit scripts remove 21 net lines. Production LOC remains 105,745, or -1,931 from the rewrite start; tracked test LOC is 142,489. |
| Phase 6 unified raw-SMT line scanning | Commit `c7ec8e42` replaces seven repeated file/read/line-counter/result loops with one ordinal needle-and-classifier scanner. Category and descriptor-kind decisions remain explicit at their call sites, while traversal, line numbers, property ordering, and trimmed evidence text have one owner. The complete RawSmtHotspots JSON is byte-identical to the committed predecessor by SHA-256; focused process-ownership fixtures pass 9/9. The script removes 85 net lines. Production LOC remains 105,745, or -1,931 from the rewrite start; tracked test LOC remains 142,489. |
| Phase 6 intentional compact-domain projection boundary | Commit `49c99dd2` removes the compact capability/complexity forwarding finding after testing its proposed shared base. The source results already share `SymbolicMethodResult`, but moving compact envelope/location properties into a base serializes derived domain fields first and changes exact compact JSON bytes. Restoring order requires per-property ordering metadata that makes the abstraction net-positive and adds a new public generic type. The candidate was reverted exactly. Release SymbolicCli build: zero warnings; compact projection, capability, and complexity fixtures: 12 passed. Production LOC remains 105,745, or -1,931 from the rewrite start. |
| Phase 6 unified artifact build entry points | Commit `45585697` routes all local dotnet work through `Invoke-SharpProofDotnet.ps1`, deletes duplicated Job Object wrappers, and gives local plus CI packaging one declarative three-project manifest. The attempted standalone-MSBuild gate reproduced an SDK-resolution failure on Build Tools; the wrapper-backed SDK build then produced the VSIX with zero warnings, so obsolete MSBuild discovery was deleted rather than centralized. The real NuGet entry point built and atomically published exactly three packages; focused process/package-policy fixtures pass 11/11. Build scripts remove 41 net lines. Production LOC remains 105,745, or -1,931 from the rewrite start; tracked test LOC is 142,498. |
| Phase 6 shared method-body operation resolution | Commit `d54111b2` makes `CSharpSyntaxFacts` the sole block-body and expression-body syntax taxonomy used by `MethodBodyOperationResolver`, while preserving the declaration fallback for destructors and deliberately excluded conversion operators. A 16-case table characterizes methods, constructors, operators, conversions, accessors, local functions, properties, indexers, and both fallback paths. Release Symbolic warning-as-error build: zero warnings; direct resolver and capability, complexity, operation-block, operator/conversion, and expression-bodied-property fixtures: 80 passed. Production LOC fell by 25 lines to 105,720, or -1,956 from the rewrite start; tracked test LOC is 142,568. |
| Phase 6 unshipped legacy package script deletion | Commit `c58f50e7` deletes 557 lines of dead packages.config install/uninstall scripts. The package project and every repository consumer omit them, while CI and `AnalyzerPackagingTests` explicitly forbid both `tools/*` entries, so sharing them would have introduced a new payload rather than consolidating live behavior. The Release package build succeeds with zero warnings and the direct package-content fixture passes. The full packaging fixture exposed one stale assertion from the earlier package-manifest migration; separate commit `ac9421e4` now validates the manifest plus wrapper loop, and all 52 fixture cases pass. Production LOC remains 105,720, or -1,956 from the rewrite start; tracked test LOC is 142,575. |
| Phase 6 unused exception type display format deletion | Commit `3c3cd2d4` removes the stale format-clone finding after exact reference search proved `ExceptionTypeDisplayFormat` had no consumers. The allocation format remains local because it alone controls two serialized allocation evidence fields; extracting a single-use policy would add indirection. Release Analyzer warning-as-error build: zero warnings; allocation, diagnostic-evidence, exception-propagation, and exception-contract fixtures: 431 passed. Production LOC fell to 105,716, or -1,960 from the rewrite start; tracked test LOC remains 142,575. |
| Phase 6 intentional compilation host boundaries | Commit `4a8cf2fe` removes the compilation-host report item after characterizing the three policies. Symbolic caches by the raw TPA value and falls back to `System.Object`; fuzzing requires TPA, adds the attribute assembly, and de-duplicates paths; analyzer metadata filters existing files and falls back to runtime-directory enumeration. Their only common code is the environment read/split, while a parameterized shared host would add comparable code and merge distinct failure contracts. Symbolic profile/target and metadata identity/source fixtures: 18 passed; standalone profile, fuzz, and source-query fixtures: 118 passed. Production LOC remains 105,716, or -1,960 from the rewrite start; tracked test LOC remains 142,575. |
| Phase 6 intentional analyzer distribution closures | Commit `9455928a` removes follow-up finding 23 after mapping all three observable closures. Source consumers use four project references plus an attribute analyzer path; NuGet ships five SharpProof assemblies, managed/native Z3, a locator, and a lib copy; VSIX uses three project references plus ten Visual Studio runtime support assemblies and transitive project output. A common manifest requires per-consumer metadata for nearly every entry, adds LOC, and relocates rather than unifies transport policy. The current Release package content check and all 52 packaging fixture cases pass. Production LOC remains 105,716, or -1,960 from the rewrite start; tracked test LOC remains 142,575. |
| Phase 6 resolved ProofCore fixed-point finding | Commit `462c13e9` removes stale follow-up finding 24. Commit `1f077511` had already introduced `SmtFactFixedPoint.Collect`; Boolean, reference, integer, and string collectors all use its single bounded iteration count, ordered scan, changed flag, and non-success early exit. Exact call inventory finds four collector entry points and no competing loop. Focused ProofCore Z3, purity proof, SMT service, syntactic classifier, and expression-atom fixtures: 268 passed. Production LOC remains 105,716, or -1,960 from the rewrite start; tracked test LOC remains 142,575. |
| Phase 6 shared Semantic Oracle source programs | Commit `ba8a19eb` replaces the only 12 byte-identical source programs remaining across `SemanticOracleSmtTests` and `SemanticOracleRuntimeHazardAnalyzerSmtTests` with typed constants in `SemanticOracleTestSources`. A Roslyn literal probe found an estimated 152 duplicated source lines before the change and zero cross-fixture literal groups afterward. No test signature, marker, assertion, query entrypoint, or analyzer entrypoint changed; both complete fixtures pass 551/551. The report's claim of broad fixture-level duplication was stale. Production LOC remains 105,716, or -1,960 from the rewrite start; tracked test LOC fell to 142,465. |
| Phase 6 resolved ownership-fact boilerplate finding | Commit `38dcab8d` removes a stale pre-kernel report entry. `AddOwnedLocalArrayFacts`, `AddFreshMutableObjectFacts`, and `AddOwnedDisposableLocalFacts` no longer contain the reported `AddFact` loops or independent path-state rebuilding; all three route acquisition through `PurityOperationTransferAdapter.ApplyLifetime` and the canonical lifetime kernel. Their remaining guards, lifetime kinds, provenance, and evidence keys are distinct analyzer policy, so another forwarding helper would not centralize semantics. Focused lifetime-kernel, array, object, mutation, and disposal fixtures pass 165/165. Production LOC remains 105,716, or -1,960 from the rewrite start; tracked test LOC remains 142,465. |
| Phase 6 resolved Dispose-matching finding | Commit `502aa541` removes a misclassified report item. `IsParameterlessDisposeInvocation` is the sole invocation matcher and is already shared by lifetime transfer and double-dispose diagnostics, including `DisposeAsync`. `IsDisposableResourceType` instead decides whether an object-creation type implements the disposable contracts and has one caller; merging type and invocation classification would conflate distinct Roslyn inputs. The 165-case lifetime/array/object/disposal gate includes explicit sync and async disposal scenarios and remains green. Production LOC remains 105,716, or -1,960 from the rewrite start; tracked test LOC remains 142,465. |
| Phase 6 resolved SMT canonicalization findings | Commit `916d8a17` removes two stale entries already addressed by `77a625d93`. General aliases and Boolean equivalences call one path-compressing `FindCanonical` over the shared `(Parent, Differs)` representation; the Boolean wrapper preserves accumulated negation through `Differs`. Both union paths also call the single ordinal `SelectCanonical` tie-breaker. Focused syntactic-classifier, SMT-service, ProofCore purity-proof, and expression-atom fixtures pass 135/135. Production LOC remains 105,716, or -1,960 from the rewrite start; tracked test LOC remains 142,465. |
| Phase 6 resolved SMT division finding | Commit `e51227f6` removes a stale numeric-classifier entry already addressed by `bf2a878e`. `SmtIntegerArithmetic` owns signed floor and ceiling division with `BigInteger.DivRem`; both `long` overloads delegate directly through `BigInteger`, and the syntactic classifier only calls those shared operations. The focused 135-case syntactic/SMT/proof gate covering affine reasoning remains green. Production LOC remains 105,716, or -1,960 from the rewrite start; tracked test LOC remains 142,465. |
| Phase 6 intentional conditional-value readers | Commit `eb8c6ae2` removes a net-positive abstraction candidate. Four typed readers select a known conditional branch before recursing into integer, reference-null, string, or string-length semantics; a generic delegate helper adds more plumbing than it removes. Boolean conditionals are intentionally different: when the condition is unknown they fork both branches under `MaxConditionalBranchEvaluationDepth` and succeed only on agreement, preserving conservative proof behavior. The complete 135-case syntactic-classifier/SMT/proof gate remains green. Production LOC remains 105,716, or -1,960 from the rewrite start; tracked test LOC remains 142,465. |
| Phase 6 resolved string-length fact finding | Commit `705b4439` removes a stale call-site report. Direct exact-string collection and exact-string alias merging already call the one `AddStringLengthFact` mutator, which normalizes aliases, intersects any existing `SmtIntegerInterval`, records the exact length, and surfaces contradictions. No competing interval-application block remains. The green 135-case syntactic-classifier/SMT/proof gate covers string and alias reasoning. Production LOC remains 105,716, or -1,960 from the rewrite start; tracked test LOC remains 142,465. |
| Phase 6 intentional SMT domain fact merges | Commit `3be4f5e8` removes a net-positive generic-merge candidate. Integer aliases intersect intervals and test `IsContradictory`; strings union exclusions, reconcile exact values, and derive length facts; references detect null-state disagreement; Booleans first transform the alias value by accumulated negation. Their common dictionary lookup/store/remove shell is smaller than a typed callback/result abstraction and contains no shared proof policy. The complete 135-case syntactic-classifier/SMT/proof gate remains green. Production LOC remains 105,716, or -1,960 from the rewrite start; tracked test LOC remains 142,465. |
| Phase 6 intentional shared catalog namespace | Commit `d49e15ca` removes a folder-name recommendation rather than a duplicate. `Constants.cs` and `BclPurityFallbackHeuristics.cs` are linked into both Analyzer and EffectSummary from one physical source, so no code is duplicated. `Constants` is a shipped type in `SharpProof.Analyzer.Engine`; even after the later API-compatibility constraint was relaxed, moving it to a neutral namespace would only add using and migration churn without changing dependency direction or LOC. Constants and BCL-fallback inventory fixtures pass 234 with the two documented reflection skips. Production LOC remains 105,716, or -1,960 from the rewrite start; tracked test LOC remains 142,465. |
| Phase 6 central compact-result schema metadata | Commit `23992c39` applies the user's relaxed .NET API constraint by replacing the unused `ISymbolicCompactResult` interface and six repeated schema triplets with `SymbolicSchemaResultBase`. Explicit `JsonPropertyOrder` values preserve the existing schema-first capability/complexity order and kind-first query/hazard/explain order. Compact envelope/order and explain byte fixtures pass 14/14; the Release Symbolic CLI warning-as-error build has zero warnings. Production LOC fell to 105,708, or -1,968 from the rewrite start; tracked test LOC is 142,472 after adding an invariant that locks each compact property prefix. |
| Phase 6 central compact SMT diagnostics projection | Commit `2562ff6f` makes `SymbolicSmtDiagnosticsProjectionBase` the single owner of eight flattened SMT configuration/counter properties used by analysis and invariant summaries. A primary constructor preserves null validation with net-negative code; explicit order 100-109 keeps each flattened block between its existing surrounding fields. Compact/explain order and SHA-256 fixtures pass 14/14, and the Release CLI warning-as-error build has zero warnings. Production LOC fell to 105,707, or -1,969 from the rewrite start; tracked test LOC remains 142,472. |
| Phase 6 resolved invariant scope-adapter finding | Commit `cee2af96` removes a stale pre-canonical-graph entry. `SymbolicCliInvariantResultAdapter` no longer exists and exact search finds no replacement `TryCreate` scope switch; point, line, span, and file compact/invariant output project directly from `SymbolicQueryResult`. The 14-case compact/explain order and byte gate remains green. Production LOC remains 105,707, or -1,969 from the rewrite start; tracked test LOC remains 142,472. |
| Phase 6 intentional compact span projection | The seven nullable span-only properties intentionally guard a flattened JSON compatibility view at its single adapter boundary. An `IsSpanScope` member would add handwritten LOC, preserve the same serialized-string check, and perform no less work because each property is independently read by the serializer. Carrying an extra typed flag through `SymbolicCompactQueryScope` would add constructor and storage code. The existing compact/explain order and byte gate remains the relevant characterization. Production LOC remains 105,707, or -1,969 from the rewrite start; tracked test LOC remains 142,472. |
| Phase 6 central compact method location projection | Commit `6d402908` introduces `SymbolicCompactMethodResult<T>` as the single owner of the nine file/method/span location passthroughs shared by capability and complexity compact results. Explicit payload ordering preserves the existing schema-kind-location-domain JSON sequence; a focused invariant now locks all nine flattened location columns. Capability, complexity, and compact-domain fixtures pass 12/12, and the Release CLI warning-as-error build has zero warnings. Production LOC fell to 105,706, or -1,970 from the rewrite start; tracked test LOC is 142,478. |
| Phase 6 central condition-proof filtering | Commit `630fce4d` replaces the two type-specific condition-proof filters with one typed selector-based implementation shared by summary and point-result text rendering. Both target-filter text/JSON fixtures pass 2/2. Production LOC fell to 105,698, or -1,978 from the rewrite start; tracked test LOC remains 142,478. |
| Phase 6 central capability-site formatting | Commit `5fc154ae` makes one formatter own unknown/capability prefix selection and symbol/operation detail fallback for both standard and explain text output. Capability and explain fixtures pass 5/5. Production LOC fell to 105,692, or -1,984 from the rewrite start; tracked test LOC remains 142,478. |
| Phase 6 central Fuzz source imports | Commit `ef00c012` replaces fifteen independently emitted import blocks with one ordered LF-based `BuildUsings` source helper while retaining each generated program's exact namespace list. Fuzz and Roslyn-shape fixtures pass 27/27. Production LOC fell to 105,684, or -1,992 from the rewrite start; tracked test LOC remains 142,478. |
| Phase 6 central Fuzz expectation classification | Commit `e695517d` moves conservative/definitely-pure/definitely-impure classification onto `FuzzExpectation`; summary counts and conservative-family selection now consume the same typed policy. The independent test oracle remains separate. Fuzz and Roslyn-shape fixtures pass 27/27. Production LOC fell to 105,680, or -1,996 from the rewrite start; tracked test LOC remains 142,478. |
| Phase 6 resolved SARIF materialization finding | Commit `62d6cf59` removes a stale entry: Baseline and CorpusReport already call the single `DotnetSarifBuildRunner.MaterializeAsync`, whose disposable result owns all temporary paths. Baseline, corpus, and process-ownership fixtures pass 28/28. Production LOC remains 105,680, or -1,996 from the rewrite start; tracked test LOC remains 142,478. |
| Phase 6 central baseline identity equality | Commit `b06476c8` replaces two custom comparer classes with one `BaselineIdentityKey` that owns ordinal ID/symbol and case-insensitive normalized-path equality. Full baseline keys compose it and use generated equality for normalized optional fields. A case-variant path characterization is green; Baseline workflow fixtures pass 7/7. Production LOC fell to 105,637, or -2,039 from the rewrite start; tracked test LOC remains 142,478. |
| Phase 6 central CorpusReport counters | Commit `f37aab29` replaces string and categorized dictionary increment implementations with one constrained generic counter. CorpusReport fixtures pass 12/12. Production LOC fell to 105,631, or -2,045 from the rewrite start; tracked test LOC remains 142,478. |
| Phase 6 intentional SMT semantic dispatch | Commit `30750190` removes a mixed stale/false-positive entry. `SmtFormulaTraversal` already owns child enumeration, mapping, bottom-up rewrite, and rebuilding; alias normalization and syntactic scans consume it. Structural keys must encode node-specific operators/payloads, while Z3 encoding must map each node to distinct solver semantics. A visitor would retain those cases as one method per node and add interface/dispatch plumbing. Traversal, classifier, structural-key, and encoder fixtures pass 183/183. Production LOC remains 105,631, or -2,045 from the rewrite start; tracked test LOC remains 142,478. |
| Phase 6 duplicate-report completion | Every remaining merged-audit item now has a green implementation or an evidence-backed intentional/stale disposition in this ledger. The empty `POTENTIAL_DUPS.md` scaffolding is deleted. Production LOC remains 105,631, or -2,045 from the rewrite start; tracked test LOC remains 142,478. |
| Phase 6 bounded semantic-search stop gate | Two `colgrep --force-cpu` batches were inspected against production code. Batch 1 found 46 safely removable lines in two generic operation wrappers; commit `6773fdc0` deletes them and keeps the same characterization on the direct lowerer-plus-kernel path, with all 40 operation-transfer model fixtures green. Batch 2 found no >=50-line safe deletion: the catalog hits are required data, option parsing is already registry-driven, and the remaining Analyzer/encoder switches carry distinct policy. Production LOC fell to 105,585, or -2,091 from the rewrite start; tracked test LOC is 142,479. |
| Phase 7 straight-line CFG collector | Commit `1b67e81a` adds a production-routed CFG/`IOperation` collector for direct local/parameter declarations and simple assignments. It returns typed `Unsupported` for unmigrated control-flow and operation shapes, so the structural engine remains the conservative fallback. Four normalized-state differential cases plus the explicit branch-fallback case pass; the complete program-point and operation-transfer batch passes 120/120. Release Symbolic warning-as-error build: zero warnings. This first migration scaffold raises production LOC to 105,762, or -1,914 from the rewrite start, and test LOC to 142,534. |
| Phase 7 acyclic CFG branch transfer | Commit `c5e685d3` adds typed true/false successor assumptions, an acyclic worklist, guarded canonical joins, and condition-mutation detection. A path-snapshot regression was reproduced in the broader gate; branch-local targets now fall back until capture/version lowering migrates, while post-join queries use the canonical path. Direct collector fixtures pass 8/8; the path/program-point/transfer batch passes 153/153; full MainSmtOracle passes 573/573; Release Symbolic warning-as-error build has zero warnings. The branch scaffold raises production LOC to 105,970, or -1,706 from the rewrite start, and test LOC to 142,590; it must be repaid with the structural branch-transfer deletion. |
| Phase 7 single-survivor completion | Commit `cdc1a701` retains the guarded state when one acyclic branch completes and only the other reaches a point after the branch. A full-lane probe exposed optimistic first-pass loop states, so every execution root containing a loop now returns typed `Unsupported` before CFG traversal until bounded fixed-point and loop-carried invalidation migrate together. Direct collector fixtures pass 9/9; full MainSmtOracle passes 573/573. Production LOC is 105,987, or -1,689 from the rewrite start; test LOC is 142,612. |
| Phase 7 all-path CFG completion | Commit `4a51a3e0` records non-regular terminal CFG edges, guarded-merges their states, and applies canonical no-fallthrough completion only when Roslyn marks the target block unreachable. The normalized-state differential matches exactly; direct collector fixtures pass 10/10, the path/program-point/transfer batch passes 157/157, full MainSmtOracle passes 573/573, and the Release Symbolic warning-as-error build has zero warnings. Production LOC is 106,015, or -1,661 from the rewrite start; test LOC is 142,635. |
| Phase 7 computed CFG updates | Commit `7f6b45fb` routes direct increment/decrement and compound assignment operations through `SymbolicAssignmentValueUpdater`, typed computed-update descriptors, and the canonical kernel. Two normalized-state differentials cover increment and compound arithmetic; direct collector fixtures pass 12/12, the path/program-point/transfer batch passes 159/159, and full MainSmtOracle passes 573/573. Production LOC is 106,108, or -1,568 from the rewrite start; test LOC is 142,637. |
| Phase 7 typed loop transfer plan | Commit `341eac5b` lowers while, do, and for entry/exit conditions, structural invariants, and local/parameter back-edge invalidation targets into one typed result. Foreach and unsupported mutation targets remain conservative fallbacks. Focused loop/program-point fixtures pass 153/153 and full MainSmtOracle passes 573/573. Production LOC is 106,234, or -1,442 from the rewrite start; test LOC is 142,691. |
| Phase 7 bounded while-loop revisits | Commit `21ebedb1` consumes mutation-independent while-loop plans at backward CFG edges, invalidates every loop-carried target before merging, and terminates revisits on normalized state identity under a graph-size budget. Three broader reproductions exposed unsound abrupt-exit handling and two deliberate do/for invariant differences; those shapes now remain typed fallbacks. Focused loop/program-point/transfer fixtures pass 197/197, full MainSmtOracle passes 573/573, and the Release Symbolic warning-as-error build has zero warnings. Production LOC is 106,324, or -1,352 from the rewrite start; test LOC is 142,736. |
| Phase 7 bounded do-loop revisits | Commit `f0a1f91d` routes mutation-independent do-loop back edges through the bounded worklist and invalidates loop-carried targets on the guaranteed-body exit edge, matching the structural collector's deliberately conservative state. Focused loop/program-point/transfer fixtures pass 197/197 and full MainSmtOracle passes 573/573. Production LOC is 106,361, or -1,315 from the rewrite start; test LOC is 142,756. |
| Phase 7 bounded counted-for revisits | Commit `f596da0c` reapplies typed monotonic initializer invariants after loop-carried invalidation and on exit, then routes counted-for back edges through the bounded worklist. The full Flow lane exposed two earlier acyclic parity leaks; nullable reassignment and guarded reference projection now return typed fallback, restoring the recorded 256-pass/1-baseline-failure result. Focused loop/program-point/transfer fixtures pass 199/199, full MainSmtOracle passes 573/573, and the Release Symbolic warning-as-error build has zero warnings. Production LOC is 106,421, or -1,255 from the rewrite start; test LOC is 142,780. |
| Phase 7 typed finally continuations | Commit `98747716` replaces the block-only queue key with a block-plus-continuation point, executes every saved finally region before its original destination, and distinguishes structured-finally completion from overriding abrupt completion. Finally-local targets remain fallback. Focused loop/program-point/transfer/finally fixtures pass 217/217, full MainSmtOracle passes 573/573, MainSmtFlow is at its recorded 256-pass/1-baseline-failure result, and the Release Symbolic warning-as-error build has zero warnings. Production LOC is 106,528, or -1,148 from the rewrite start; test LOC is 142,802. |
| Phase 7 canonical pattern binding | Commit `c4adcade` deletes the program-point recursive, property, positional, list-element, relational, and designation interpreter and consumes the one typed `SymbolicPatternLowerer` condition through a canonical branch-assumption transition. A full-lane reproduction found recursive outer-designation projection congruence missing from the canonical owner; it now emits binding plus length/string projection equality without changing reassignment invalidation for ordinary declaration patterns. Focused pattern/list/property/program-point fixtures pass 178/178; MainSmtOracle passes 573/573; MainSmtAnalyzer passes 487/487; the Release Symbolic warning-as-error build has zero warnings. Production LOC is 106,237, or -1,439 from the rewrite start; test LOC remains 142,802. |
| Phase 7 typed finite-foreach domains | Commit `0ad4ece9` replaces direct finite-domain state mutation with `SymbolicFiniteDomainLowerer`, a typed plan consumed only through canonical loop-edge transitions. Inline arrays and prior-assigned finite arrays are characterized directly. A broader limits probe exposed that the CFG collector did not reproduce structural try-merge and scoped-completion truncation events, so custom analysis limits now conservatively select the structural collector until typed truncation parity migrates. Focused finite-domain/program-point/limit/transfer fixtures pass 139/139; MainSmtOracle passes 573/573; MainSmtFlow remains at its recorded 256-pass/1-baseline-failure result; the Release Symbolic warning-as-error build has zero warnings. This migration scaffold raises production LOC to 106,296, or -1,380 from the rewrite start; test LOC rises to 142,831. |
| Phase 7 canonical finite-element discovery | Commit `b31b8fef` moves array, collection-expression, bounded-element, prior-assignment, referenced-symbol invalidation, and truncation discovery into `SymbolicFiniteDomainLowerer`. `SymbolicOperationLowerer` now consumes the same typed result for finite array postconditions, and the 231-line legacy program-point discovery block is deleted. Focused finite-domain/foreach/program-point/limit/operation/element-access fixtures pass 37/37; MainSmtOracle passes 573/573; MainSmtFlow remains at its recorded 256-pass/1-baseline-failure result; the Release Symbolic warning-as-error build has zero warnings. Production LOC is 106,307, or -1,369 from the rewrite start; test LOC remains 142,831. |
| Phase 7 typed loop-bound invariants | Commit `46cf1914` replaces loop-bound discovery's temporary `SymbolicState` construction with `SymbolicLoopInvariantPlan`. For/while/do initializer bounds, strict upper bounds, dependency invalidation, and monotonic-update checks now produce typed conditions; both CFG lowering and structural fallback apply them only through canonical loop-edge transitions. Focused loop/lowerer/program-point/operation fixtures pass 186/186; MainSmtOracle passes 573/573; MainSmtFlow remains at its recorded 256-pass/1-baseline-failure result; the Release Symbolic warning-as-error build has zero warnings. This typed scaffold raises production LOC to 106,320, or -1,356 from the rewrite start; test LOC remains 142,831. |
| Phase 7 typed framework postconditions | Commit `pending` introduces `SymbolicFrameworkPostconditionLowerer` as the single owner of parameter-not-null, inferred-not-null, known argument-guard, and member-not-null normal-completion discovery. Statement and expression paths consume ordered typed condition groups through one bulk canonical assumption transition; the legacy direct-state implementations and their shared member helpers are deleted. Focused nullable/program-point/reachability/element/exception fixtures have 293 passing cases plus only the documented SP0010 baseline failure; MainSmtOracle passes 573/573; MainSmtFlow remains at its recorded 256-pass/1-baseline-failure result; the Release Symbolic warning-as-error build has zero warnings. This typed scaffold raises production LOC to 106,424, or -1,252 from the rewrite start; test LOC remains 142,831. |

## Current Checkpoint

- Last updated: 2026-07-15.
- State: Phase 7 is active under explicit authorization for a major,
  behavior-preserving rearchitecture. The earlier phases are gated. The
  `POTENTIAL_DUPS.md` cleanup is complete; its Requires/Ensures and
  exception-flow diagnostic-envelope findings plus the switch-visibility shape
  finding are removed with evidence, and exception-catalog type/source
  registration, proof path-state encoding, and typed proof-status projection are
  centralized. The stale source-query entry is removed; tooling temporary-source
  lifecycle, compact CLI test contracts, and project/solution-to-SARIF
  materialization are centralized. Up-to-date project builds now reliably emit
  the SARIF expected by both tools. Repository containment, relative paths, and
  production-source discovery are now shared across the three audit scripts;
  RawSmtHotspots also has one line-scanning owner. Compact capability/complexity
  wrappers remain an intentional serialized-order compatibility boundary. Local
  artifact builds now share the dotnet wrapper and package-project manifest;
  obsolete standalone-MSBuild discovery is deleted. Method-body operation
  lookup now consumes the shared block/expression syntax taxonomy while
  retaining its two compatibility fallbacks. The unshipped legacy package
  install/uninstall scripts are deleted; CI continues to forbid those payloads.
  The unused exception type-display format is deleted rather than abstracted.
  Symbolic, fuzz, and analyzer compilation hosts remain separate because their
  cache, fallback, filtering, and failure policies differ. Source-consumer,
  NuGet, and VSIX distribution closures remain explicit transport boundaries.
  The only 12 byte-identical Semantic Oracle programs are now shared constants;
  the query and analyzer fixtures retain separate entrypoints and assertions.
  Fresh array, object, and disposable ownership already enters through the
  canonical lifetime adapter; the reported independent fact loops are gone.
  Dispose invocation recognition already has one owner, while disposable-type
  recognition remains a distinct object-creation eligibility check. General and
  Boolean SMT equivalences already share canonical finding and selection. Long
  floor/ceiling division already delegates to the BigInteger implementation.
  Typed conditional-value readers remain explicit because Boolean unknown-branch
  evaluation has a distinct bounded-fork contract. Exact strings and aliases
  already share one string-length interval mutator. Domain fact merges remain
  explicit because each has different transformation and conflict semantics.
  Shared catalog source retains its Analyzer namespace because moving it adds no
  reduction; subsequent .NET API breaks are allowed. Compact schema metadata now
  has one base owner while explicit ordering preserves exact JSON output. The
  flattened SMT diagnostic block likewise has one ordered projection owner. The
  old per-scope invariant adapter is gone; output reads the canonical graph. The
  compact span-only getters remain local because caching or carrying a typed
  span flag adds handwritten code without reducing serialized work. Compact
  capability and complexity results now inherit one typed method-location
  projection while retaining their exact serialized property order. Condition
  proof target filtering now has one generic implementation for summary and
  point-result projections. Standard and explain capability output now share
  one site prefix/detail formatter. Generated Fuzz imports now have one ordered,
  LF-stable source builder. Fuzz expectation buckets now have one domain-model
  owner while their independent test oracle remains intact. Baseline and corpus
  SARIF materialization and temporary cleanup already share one disposable
  implementation. Baseline bucket and full-key identity now share one normalized
  value with case-insensitive path equality. CorpusReport string and categorized
  counts now share one typed increment operation. SMT child traversal and
  rewriting already have one taxonomy; remaining node switches encode genuinely
  operation-specific payload and solver semantics. The exhausted duplicate
  report is deleted. Two bounded semantic-search batches each found fewer than
  50 safely removable production lines; their only accepted residual wrappers
  are deleted, so the plan's secondary-work stop condition is met.
- Last confirmed fact: all 8,583 lines in the legacy deletion-map family are
  reachable. Commits `1b67e81a` and `c5e685d3` production-route straight-line
  assignment and post-join acyclic branch states through canonical CFG events.
  Branch-local target queries, finally regions, unsupported operations, and
  unsupported loop shapes still fall back. Acyclic single-survivor and all-path
  terminal completion are canonical. MainSmtOracle passes 573/573 and the warning-as-error
  Symbolic build is clean. Computed updater operations now use the same CFG
  transfer path. Typed loop transfer plans now own
  while/do/for conditions, invariants, and local/parameter back-edge
  invalidations; queries after mutation-independent while and do loops now use
  bounded canonical CFG revisits; counted-for loops additionally retain their
  typed monotonic initializer invariants. Loop-local targets, abrupt exits,
  condition-dependent while/do mutations, nullable reassignment, guarded
  reference projection and finally-local targets remain conservative fallbacks.
  Finite foreach entry domains now lower to typed conditions and enter state only
  through the canonical loop-edge transition. Finally regions run through typed saved continuations before
  their destinations. Pattern binding has one typed canonical owner; the
  425-line program-point interpreter is deleted. Finite-element discovery and
  bounded prior-assignment validation now have one typed owner, and the 231-line
  legacy discovery block is deleted. Framework normal-completion postconditions
  now have one typed owner and enter state through canonical assumptions. Test
  LOC is 142,831; production LOC is 106,424, or -1,252 from the rewrite start; the 839-line
  scaffold must be repaid when structural transfer paths are deleted.
- Next cheapest step: lower the remaining source-derived normal-completion
  conditions (`DoesNotReturnIf`, throw guards, array bounds, and dereference
  success) into typed results, preserving inline-assignment and pattern-binding
  provenance before deleting their direct-state paths.
- Blockers: none. The known SP0010 focused failure must be tracked as baseline,
  not attributed to the rewrite without new evidence.
