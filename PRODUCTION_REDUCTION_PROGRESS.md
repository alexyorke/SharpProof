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

## Current evidence

- Maintained production: 107,018 lines; net reduction: 608 lines; remaining
  reduction: 19,392. The first seven deletion slices removed 885 production
  C# lines and have repaid the inferred-summary/architecture foundation.
- Release solution build: zero warnings and errors.
- Six lanes: 6,154 passing tests and two documented skips.

## Milestones

- [ ] 3,000 lines removed: inferred-summary foundation and first migrated rules.
- [ ] 7,000 lines removed: manual semantic catalogs substantially migrated.
- [ ] 12,000 lines removed: canonical symbolic traversal owns all transfer paths.
- [ ] 16,000 lines removed: query, analyzer policy, and EffectSummary duplication removed.
- [ ] 20,000 lines removed: CLI/ProofCore cleanup and final dead-code deletion complete.

## Current tranche

Migrate test-only `SymbolicInvariantService` entry points and formatting
projections to executor-owned snapshots or focused projection components;
delete each superseded method with its adapter.
