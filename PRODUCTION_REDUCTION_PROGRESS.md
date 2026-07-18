# SharpProof Production Reduction Progress

This is the source of truth for the 20,000-line maintained-production reduction.
Tests are excluded from the metric and must not be deleted.

## Constraints

- Preserve diagnostics, attributes, CLI behavior, serialization, packages, and
  conservative `Unknown` semantics.
- Count tracked handwritten production C#, PowerShell scripts, generators, and
  maintained production specifications.
- Do not obtain reductions through minification, code in strings, hidden
  generated complexity, or moving maintained logic outside the metric.
- Delete superseded implementations in the same green tranche.

## Baseline

- Commit: `e6c324df`.
- Maintained production: 107,626 lines, including the reduction enforcement
  script introduced with this baseline.
- Required reduction: 20,000 lines.
- Completion ceiling: 87,626 lines.
- Baseline tests: 6,147 passing with two documented skips.

## Completed

- [x] Added a tracked maintained-production baseline covering handwritten C#,
  PowerShell build/generator scripts, and explicit production specifications.
- [x] Added a reporting command with milestone and final-target enforcement.
- [x] Added an architecture test that validates the 20,000-line target,
  baseline arithmetic, and non-test production categories.
- [x] Added the shared inferred-summary model with typed purity, effects,
  freshness, visibility, explicit unknown reasons, stable cache keys, and
  semantic differential comparison.
- [x] EffectSummary now projects its existing classification into that shared
  model without changing its serialized contract. Contracts owns the mapping
  so Analyzer can reuse it without depending on the executable tool.
- [x] Removed the manual `System.Type` and `System.RuntimeType` metadata-member
  wrapper catalogs. Generic call-graph inference now owns those classifications;
  six runtime metadata slices characterize the unchanged output.
- [x] Made `SharpProofTarget` the single query-target model across the public
  session API, Symbolic execution, Analyzer, and CLI. Deleted the duplicate
  internal target, enum, validation, and conversion adapter.
- [x] Routed divide, modulo, checked arithmetic, checked update, compound
  assignment, and checked conversion hazards through one operation-level
  candidate adapter. Deleted seven syntax-specific forwarding routes.
- [x] Removed the production `PurityPolicyAuditRegistry`, which duplicated
  analyzer behavior solely for test inspection. The retained audit tests now
  own their expected documentation contract and continue exercising the real
  configuration and precedence paths.
- [x] Made `PurityProofQuery` the only ProofCore hazard-classification request
  path and migrated wrapper-specific tests to it. Removed seven service-specific
  classification entry points, a conversion-only lowering adapter, and four
  unreachable helpers across ProofCore, Symbolic, CLI, and EffectSummary.
- [x] Deleted the 170-line `SymbolicQueryService` pass-through. Analyzer, CLI,
  session, and retained tests now use the same `SymbolicQueryExecutor`; the
  architecture test verifies that the legacy facade type is absent.
- [x] Deleted the 243-line `SymbolicSourceQueryService` facade. Query dispatchers
  now own the program-point, range, and proof components directly; retained
  source-query tests use executor-based helpers in `SharpProof.Testing`.
- [x] Removed the legacy invariant snapshot and implication entry points and
  their duplicate result DTOs. Tests now use `SymbolicProgramPointAnalysis` and
  the canonical condition-proof executor; display and fact merging live with
  their focused projection types.
- [x] Collapsed three runtime-hazard syntax-tree routes into one target-based
  dispatcher path. Removed unreachable source-proof and line-analysis helpers,
  the unused typed hazard wrapper, and a redundant project-session adapter.
- [x] Internalized the concrete public query records behind the existing
  `SharpProofQuery` factories. The session now dispatches simple requests by
  query kind, and the intentional breaking API snapshot no longer exposes eight
  constructor-heavy request implementation types.
- [x] Added one deterministic operation-first runtime-hazard lowering registry
  for arithmetic, index construction, Math.Abs/Clamp, argument guards, and
  switch no-match. Deleted their syntax dispatch branches and forwarding
  adapters; syntax remains only where Roslyn operations do not carry required
  source evidence.
- [x] Moved array/stackalloc lengths, element and Array.GetValue bounds,
  slicing, collection cardinality, and array-store mismatch discovery behind
  the same operation registry. Direct lowerer tests now exercise `IOperation`
  requests instead of compatibility-shaped metadata parameter lists.
- [x] Moved cast, nullable-value, null receiver, dynamic-binding, lock, regex,
  await, with-expression, foreach, and deconstruction hazards into operation
  lowerers. Removed the legacy syntax candidate factory; the renamed 31-line
  source factory owns only multi-descriptor throw/rethrow projection, with one
  explicit Roslyn member-operation gap retained at enumeration.
- [x] Removed the manual string-from-ReadOnlySpan and Path string-slice
  normalization wrapper rules. Generic fixed-point inference reproduces their
  runtime and analyzer classifications; the fresh-copy and span-search rules
  were independently shown to remain necessary and were retained.
- [x] Removed the stack-local char-builder and immutable string-rewrite wrapper
  rules after independent PathCore and analyzer string-suite characterization.
  Generic fixed-point inference now owns both classifications and their call
  families are deleted.
- [x] Removed char-scalar projection and guarded string-scan wrapper rules after
  runtime Char-helper and analyzer string-suite parity. Their shared manual
  identity/display parser was unreachable and is deleted with the rules.
- [x] Characterized the remaining string-hash and `System.Type` identity
  wrapper rules independently. Removing either changes its runtime catalog
  slice from `pure` to `conservative_unknown`, and string hashing also regresses
  analyzer string invariants, so both remain explicit semantic owners rather
  than legacy deletion debt.
- [x] Re-audited the CFG program-point collector, loop transfer, and
  EffectSummary assembly-document reader. Each remains reachable and owns
  distinct proof, loop-invalidation, or exception-summary behavior; none is a
  compatibility adapter that can be removed without deleting a feature.
- [x] Removed the remaining substring semantic-wrapper rule after its dedicated
  runtime slice proved generic fixed-point inference preserves the catalog.
- [x] Replaced 332 net lines of handwritten impacted-test path rules and live
  token scans with the generated inventory's exact dependencies, source-root
  ownership, and reverse project closure. Explicit analyzer modules retain
  validated partial selection; inferred production mappings conservatively
  require the full suite, so missing lexical references cannot under-test a
  semantic change.
- [x] Removed the handwritten BCL impurity member catalog for random-number,
  mutable-string, array-mutation, threading, XML, diagnostics, I/O, and assembly
  loading APIs. Generated purity summaries, configured and namespace policy,
  metadata analysis, and conservative BCL fallback now own these decisions.
  Diagnostics remain present and conservative; as an intentional unreleased
  API break, evidence sources and categories may now describe the inferred path
  instead of the deleted semantic-rule name.
- [x] Retired the three-entry handwritten analyzer test-impact manifest and its
  parallel validation, ownership, dependency-closure, and evidence engine. The
  generated repository inventory is now the sole production/project ownership
  source; inferred mappings conservatively require the full suite. Selector
  tests retain every case against the generated path, and no test was deleted.
- [x] Made the requested test lane the single project-routing owner for filtered
  runs. Deleted five handwritten fixture catalogs and the duplicate filter-name
  parser/repartitioner; impacted selection now infers Main versus Tooling from
  the generated fixture inventory. Fixed six-lane filters, worker caps,
  fail-fast settings, profiling, and concurrent execution remain intact.
- [x] Retired the completed raw-SMT migration hotspot inventory. The canonical
  operation-transfer plan records every migration phase and deletion gate as
  complete, while architecture tests enforce the supported session/query/result
  boundary and prevent raw SMT types from entering the public API. No build,
  CI, documentation, or product path consumed the duplicate source scanner.

## Current evidence

- Maintained production: 104,485 lines (99,501 C#, 4,104 scripts, and 880
  specifications); net reduction: 3,141 lines; remaining reduction: 16,859.
  This tranche removed 324 maintained script lines without deleting tests.
- Release solution build: zero warnings and errors.
- Six lanes: 6,154 passing tests and two documented skips.

## Milestones

- [x] 3,000 lines removed: inferred-summary foundation and first migrated rules.
- [ ] 7,000 lines removed: manual semantic catalogs substantially migrated.
- [ ] 12,000 lines removed: canonical symbolic traversal owns all transfer paths.
- [ ] 16,000 lines removed: query, analyzer policy, and EffectSummary duplication removed.
- [ ] 20,000 lines removed: CLI/ProofCore cleanup and final dead-code deletion complete.

## Current tranche

Replace the next complete duplicated semantic or orchestration owner whose
superseded path repays at least 200 maintained-production lines, preferring
350-line or larger cuts. Prefer inferred or canonical analysis over manual
catalogs when characterization proves it is complete; otherwise preserve the
explicit owner. Require focused parity before deleting the old path in the same
tranche, and do not revisit the independently required string-hash,
type-identity, CFG/loop-transfer, or EffectSummary assembly-reader owners.
