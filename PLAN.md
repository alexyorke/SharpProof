# Rebrand PurelySharp Into An IR-Native Symbolic C# Analysis Platform

## Summary

PurelySharp has outgrown a purity-only identity. The project direction is a
bounded symbolic C# analysis platform where purity diagnostics, runtime-hazard
detection, invariant queries, ownership/resource facts, and effect-summary
reasoning all use one shared path:

```text
Roslyn/C# -> Symbolic IR -> normalized facts/state -> proof service -> Z3-backed conclusions -> analyzer/API/CLI outputs
```

The staged product direction is to keep the current `PurelySharp`
compatibility identity while documenting a broader platform position.
`SharpProof` is only a working codename until formal package, repository, and
trademark clearance is complete.

## Current State

- Compatibility package/API identity is still `PurelySharp`.
- Main production modules are analyzer, symbolic library, solver backend,
  package/VSIX, attributes, code fixes, and tools.
- Largest production pressure points are `SymbolicProgramPointFacts.cs`,
  `PurityAnalysisEngine.cs`, `SymbolicSourceQueryService.cs`,
  `Tools/PurelySharp.EffectSummary/Program.cs`,
  `MethodInvocationPurityRule.cs`, `SmtSyntacticClassifier.cs`, and
  `CSharpConditionToFormula*`.
- Architecture guard inventory currently reports zero analyzer raw-SMT
  hotspots and zero public symbolic `SmtFormula` surfaces.
- Remaining migration debt is concentrated in symbolic direct
  `CSharpConditionToFormula` usage, oversized compatibility services, and
  hard-coded API recognizers that should become declarative IR lowerings.

## Compatibility Policy

- Do not rename projects, namespaces, package IDs, diagnostic IDs, analyzer
  config keys, attributes, additional-file names, or
  `*.PurelySharp.EffectSummary.json` conventions in this tranche.
- Rebrand docs, package descriptions, tags, and repository metadata first.
- Treat a future hard rename as a separate compatibility project with package
  forwarding, analyzerconfig aliases, migration docs, and deprecation policy.
- Public users should see symbolic facts, proof outcomes, unknown reasons,
  hazard preconditions, and budget/cache diagnostics, not raw solver formulas.

## Target Architecture

- `PurelySharp.Symbolic.Ir` owns C#/Roslyn semantic facts: nullness, equality,
  ranges, string/regex facts, bounds, type tests, exception preconditions,
  freshness, ownership, borrows, mutation, disposal, and resource lifetimes.
- `SymbolicProofService` is the proof spine over `SmtAnalysisService`,
  centralizing query keys, normalized fact caching, budgets, cancellation,
  timeout/native-load fallback, unknown reasons, and proof metadata.
- `SearchLib` stays solver-only and must not absorb Roslyn, analyzer, BCL, or
  C# API semantics.
- `PurelySharp.Analyzer` consumes symbolic services instead of owning separate
  path, hazard, ownership, or reachability proof logic.
- `CSharpConditionToFormula*` remains a migration shim and should shrink as
  declarative IR lowerings replace direct formula translation.

## Implementation Tranches

### 1. Staged Rebrand

- Update README, backlog, package descriptions, VSIX description, tags, and
  repository metadata guidance to present PurelySharp as a bounded symbolic C#
  analysis platform.
- Keep `SharpProof` as a codename only until formal availability review.
- Preserve all current compatibility identifiers.

### 2. Measurable Architecture

- Keep raw-SMT hotspot checks green.
- Extend architecture inventory to track largest production files, public API
  leaks, symbolic direct translator usage, and IR known-API lowerings.
- Treat growth in `SymbolicProgramPointFacts`, `SymbolicSourceQueryService`,
  `PurityAnalysisEngine`, and `MethodInvocationPurityRule` as architecture
  debt unless the growth removes duplication elsewhere.

### 3. IR-Native Proof Spine

- Move reachability, implication, branch feasibility, hazard feasibility, and
  invariant proof through `SymbolicProofService`.
- Keep formula compatibility private and temporary.
- Make proof results expose status, backend, unknown reason, source lowering,
  and budget/cache data.

### 4. Collapse Duplicate Analysis Centers

- Route runtime hazards through IR exception-precondition facts.
- Move analyzer path/state merge logic toward `SymbolicState`.
- Move ownership/resource facts out of analyzer rule branches and into shared
  IR facts.
- Keep analyzer diagnostic formatting separate from analysis logic.

### 5. Declarative Lowerings

- Expand `KnownApiLoweringDescriptor` beyond the current string/regex subset.
- Add domains for nullable, bounds/indexers, type tests, numeric ranges,
  collection counts, disposal/resource APIs, and effect-summary preconditions.
- Delete old translator branches only after equivalence tests prove identical
  or more conservative behavior.

### 6. Productized Query Surfaces

- Keep analyzer diagnostics as the default developer experience.
- Keep the symbolic library and CLI as advanced query surfaces for invariants,
  runtime hazards, and proof metadata.
- Add future IDE-facing outputs around facts at a line, why a path is
  reachable, why a hazard is proven, and why purity is unknown.

## Acceptance Criteria

- Architecture tests prove no new analyzer raw-SMT hotspots, no public raw
  solver formula leaks, and no Roslyn semantics in `SearchLib`.
- Inventory scripts report symbolic direct translator usage and IR known-API
  lowering locations so migration progress is measurable.
- Package metadata and docs describe symbolic analysis first, with purity as
  one supported capability.
- Equivalence tests guard every deleted hard-coded recognizer or analyzer
  branch.
- Fallback tests cover SMT off, timeout, cancellation, budget exhaustion,
  unsupported lowering, approximate regex, and native Z3 load failure.
- Performance checks track proof query count, cache hits, focused test runtime,
  and memory use through repo job-object wrappers.

## Assumptions

- Staged rebrand is preferred over a hard rename.
- `SharpProof` is a non-final codename.
- The platform remains bounded and conservative; it should not claim full
  execution prediction.
- Code deletion is allowed only after shared IR/proof behavior is locked by
  tests.
