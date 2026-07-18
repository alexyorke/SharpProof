# SharpProof Refactoring Progress

This is the active source of truth for the comprehensive refactor. Read
`docs/refactoring-baseline.md` for the immutable starting point and
`CANONICAL_OPERATION_TRANSFER_PLAN.md` for historical semantic constraints.

## Invariants

- Preserve diagnostics, proof outcomes, conservative `Unknown` behavior,
  CLI/JSON/SARIF bytes, package contents, and attribute semantics.
- Public .NET API breaks are allowed when they produce a cleaner design.
- Land bounded green commits; delete superseded paths in the same tranche.
- Run .NET commands through `scripts/Invoke-SharpProofDotnet.ps1` or the
  repository test wrapper.

## Completed

- [x] Baseline captured in `docs/refactoring-baseline.md`.
- [x] Architecture tests enforce module ownership, dependency direction, and
  absence of cross-project source compilation.
- [x] `SharpProof.Contracts` and `SharpProof.Tooling.Core` own former `Shared`
  production code. Commit `ff682340`.
- [x] `SharpProof.Testing` owns shared fixtures; tooling tests have one owner;
  `SharpProof.SymbolicCli.Core` owns reusable CLI projections. No external
  `<Compile Include>` remains. Commit `0d9043b1`.
- [x] Analyzer method facts now live in an immutable `MethodAnalysisSnapshot`;
  feature analyzers consume the snapshot while session state exclusively owns
  symbolic query execution and caching.
- [x] `SymbolicQueryService` routes all public query families through one
  validated internal request, including common context validation, analysis
  limits, target requirements, and SMT requirements.
- [x] Public `SymbolicQueryService` is a thin coordinator; source compilation,
  dispatch, execution, and result projection live in the internal
  `SymbolicQueryExecutor`.
- [x] `SharpProofAnalysisSession`, discriminated `SharpProofQuery` records, and
  `SharpProofQueryResult` now form the primary public API. CLI, explain modes,
  samples, documentation, and package consumers use it; the former query
  service is internal and its preview removal is recorded in the API snapshot.
- [x] The preview compatibility cutoff is complete: legacy `Symbolic*` query,
  result, evidence, error, budget, project-context, and raw SMT types are
  internal. Focused immutable `SharpProof*` targets, payloads, evidence,
  errors, budgets, and solver metadata form the exported surface; CLI adapters
  preserve the established external schemas and bytes.
- [x] Source-query and runtime-hazard target dispatch are isolated from
  `SymbolicQueryExecutor`; the executor now owns API coordination while the
  dispatchers own source-kind validation, target routing, and node/syntax-tree
  execution. The superseded executor branches were deleted.
- [x] Condition-proof target, SMT, and source dispatch plus syntax-node proof
  execution live in `SymbolicConditionProofDispatcher`; the executor retains
  only common request validation, limits, and error coordination.
- [x] Program-point result construction is centralized in
  `SymbolicProgramPointProjector` over an immutable query context. Syntax-tree
  aggregation and direct node queries share it, and duplicate node projection
  was deleted.

## Current evidence

- Branch: `codex/nullable-contract-verification`.
- Handwritten production source: 99,712 lines across 437 files.
- Architecture inventory: zero unassigned files and zero dependency violations.
- Release solution build: zero warnings and errors.
- Six lanes: 6,141 passing tests and two documented skips.
- Package consumers pass with native SMT required on Windows x64.

## Remaining tranches

- [ ] Consolidate internal analysis request, context, and immutable snapshot
  shared by analyzer and Symbolic query consumers.
- [x] Redesign and reduce the public Symbolic API; adapt CLI while preserving
  external output.
- [ ] Decompose query/proof/source services and complexity/solver components by
  responsibility, deleting duplicate orchestration.
- [ ] Replace monolithic analyzer diagnostic and code-fix dispatch surfaces with
  typed registries.
- [ ] Decompose EffectSummary host and standardize lightweight tool hosting.
- [ ] Finish test-lane/repository organization and remove dead compatibility
  paths.
- [ ] Run final Release, six-lane, package-consumer, NuGet, VSIX, generated-doc,
  fuzz, EffectSummary, architecture, and public-API gates.

## Next cheapest step

Decompose complexity modeling into loop, call-summary, cost-algebra, and
projection responsibilities while deleting superseded orchestration.
