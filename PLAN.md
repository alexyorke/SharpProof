# Move PurelySharp To An IR-Native Symbolic Analysis Platform

## Summary And Current State

PurelySharp has the right direction now, but the codebase is only halfway there. The target is a bounded symbolic C# platform where purity, runtime hazards, invariant queries, and effect-summary reasoning all use one path: Roslyn/C# -> Symbolic IR -> normalized state -> proof service -> Z3-backed proof -> analyzer/API/CLI output.

Current repo facts from inspection:

- Production code is about 107,675 C# lines across 165 files. `PurelySharp.Symbolic` is the largest module at about 43,091 lines, then `PurelySharp.Analyzer` at about 34,271 lines.
- The biggest pressure points are `SymbolicProgramPointFacts.cs`, `SymbolicSourceQueryService.cs`, `PurityAnalysisEngine.cs`, `MethodInvocationPurityRule.cs`, `CSharpConditionToFormula*`, `SmtSyntacticClassifier.cs`, and effect-summary tooling.
- Four analyzer files still directly construct SMT or call `CSharpConditionToFormula`: `PurityAnalysisEngine.cs`, `PurityAnalysisEngine.StateMerge.cs`, `ExceptionFlowAnalyzer.ExceptionSites.cs`, and `ExceptionFlowAnalyzer.PathFacts.cs`.
- Public symbolic APIs still expose backend concepts such as `SmtFormula`, `HasSmtFormula`, `MergedInvariant`, and raw path-condition formula lists.
- The existing IR is useful but thin: it has terms, atoms, facts, exception preconditions, ownership/freshness/mutation placeholders, and an encoder, but most path facts and public query results still flow through `SmtFormula`.

The new state should be IR-native. `SmtFormula` should become an internal backend encoding detail, not the semantic model used by analyzer code or public symbolic APIs.

## Target Architecture

- `PurelySharp.Symbolic.Ir` becomes the canonical semantic layer for C#/Roslyn facts: terms, atoms, path conditions, assignment versions, nullness, equality, order/ranges, string/regex predicates, length/count, bounds, type tests, exceptional preconditions, ownership, freshness, escape, disposal, and mutation.
- Add an internal `SymbolicProofService` over `SmtAnalysisService`. It should accept IR facts/conditions, normalize and cache them, encode to SMT only at proof time, apply budgets/timeouts/cancellation, and return proof metadata with conservative unknown reasons.
- Keep `SearchLib` solver-only. It should not gain Roslyn, analyzer, BCL, or C# API semantics.
- Make `PurelySharp.Analyzer` a consumer of symbolic services. Analyzer code should not create new C#/Roslyn-specific `SmtFormula` objects, call `CSharpConditionToFormula`, or own independent path/reachability/hazard proof logic.
- Treat `CSharpConditionToFormula*` as a migration shim. New recognizers should be declarative IR lowerings, and old direct formula branches should be deleted after equivalence tests pass.
- Clean-break public .NET symbolic APIs: public users should receive symbolic facts, proof outcomes, unknown reasons, and diagnostics, not raw solver formulas. CLI JSON should stay stable where possible, with additive fields preferred.

## Key API And Type Changes

- Introduce public result DTOs that do not expose `SmtFormula`: `SymbolicFactInfo`, `SymbolicInvariantInfo`, `SymbolicProofInfo`, `SymbolicUnknownReason`, `SymbolicProofBackend`, and `SymbolicBudgetInfo`.
- Replace public `IReadOnlyList<SmtFormula>`, `MergedInvariant`, `PathConditions`, and `HasSmtFormula` surfaces with fact/proof DTOs and stable display text.
- Keep `SymbolicQueryService`, `SymbolicQueryRequest`, `SymbolicConditionProofRequest`, and `SymbolicRuntimeHazardRequest` as the public entrypoints, but change their results to IR/fact-oriented models.
- Make `SymbolicInvariantService`, `SymbolicReachabilityService`, and formula display/projection helpers internal or compatibility-only during migration. Their public formula-returning members should be removed in the clean break.
- Keep `SmtAnalysisService` public only if it is explicitly documented as an advanced backend service. Normal callers should configure SMT through symbolic query options, not pass around formulas.

## Implementation Tranches

### 1. Behavior Locks And Inventory

- Add architecture tests that fail on any new public `SmtFormula` exposure in `PurelySharp.Symbolic` outside `SearchLib`/SMT backend types.
- Extend the existing raw-SMT hotspot inventory to include public formula surfaces, symbolic formula factories, and direct translator usage.
- Add baseline equivalence tests for current invariant query, runtime hazard, exception-flow, purity diagnostic, and CLI JSON scenarios before moving logic.

### 2. IR-Native Proof Service

- Add `SymbolicProofService` with requests for reachability, implication, branch feasibility, hazard trigger feasibility, and invariant projection.
- Make it accept `SymbolicState`, `SymbolicFact`, and `SymbolicCondition`, not `SmtFormula`.
- Move query key creation, normalized fact caching, budget accounting, cancellation checks, fallback reason shaping, and proof metadata into this service.
- Route `SymbolicReachabilityService` through `SymbolicProofService`, keeping old formula-based methods only as temporary internal adapters.

### 3. Public API Clean Break

- Update `SymbolicQueryResult`, `SymbolicSourceQueryResult`, invariant results, proof summaries, and runtime hazard results to expose symbolic facts/proofs instead of raw formulas.
- Update `Tools/PurelySharp.SymbolicCli` to use the new DTOs while preserving command-line options and existing JSON names where practical.
- Delete or internalize formula-returning public members from `SymbolicInvariantService`, `SymbolicReachabilityService`, `SymbolicFactFactory`, and source query result models.
- Add API-shape tests proving public symbolic APIs do not expose `SmtFormula`.

### 4. Path Facts And State Migration

- Move `SymbolicProgramPointFacts` collection from `List<SmtFormula>` to `SymbolicState` and `ImmutableArray<SymbolicFact>`.
- Move analyzer `PurityAnalysisState.PathConditions` from `ImmutableArray<SmtFormula>` to symbolic state/fact storage.
- Convert `PurityAnalysisEngine.StateMerge.cs` formula rewriting/merge logic into symbolic-state merge logic with symbol-version-aware fact normalization.
- Retire analyzer direct path-condition additions in `PurityAnalysisEngine.cs` after equivalence tests prove branch reachability, null guards, assignment facts, and string/length facts still behave the same or more conservatively.

### 5. Runtime Hazard Unification

- Make `SymbolicRuntimeHazardCandidateFactory` produce IR exception-precondition facts first, not formula triggers.
- Route analyzer exception-site checks in `ExceptionFlowAnalyzer.ExceptionSites.cs` through shared symbolic hazard candidates/results where equivalent.
- Move `ExceptionFlowAnalyzer.PathFacts.cs` path collection to shared symbolic state, preserving analyzer-only exception summary propagation until test-locked.
- Delete legacy direct trigger construction for divide-by-zero, null dereference, nullable value, invalid cast, checked overflow, negative length, index/range, and direct throw only after shared hazard tests match.

### 6. Declarative Lowering Migration

- Add descriptor-based IR lowerings for known APIs and C# constructs: string predicates, regex, concat/interpolation, nullable members, bounds/indexers, span/array/string length, type tests, relational patterns, switch arms, and numeric range facts.
- Move recognizers out of `CSharpConditionToFormula*` into IR lowering files grouped by domain.
- Keep old translator branches as fallback adapters until each domain has equivalence coverage, then delete them.
- Add an architecture test that new C#/Roslyn/API-specific lowering code must live under the IR lowering namespace, not in `SearchLib`, analyzer rules, or direct SMT translators.

### 7. Purity Rule Reduction

- Split `MethodInvocationPurityRule` into invocation effect resolution, receiver dispatch, summaries/catalogs, mutation/ownership, and evidence formatting.
- Move mutation/ownership checks from invocation, assignment, property, field, array, and return rules into IR facts where possible.
- Convert trivial pure/structural operation rules to declarative rule descriptors where they are behaviorally identical.
- Keep generated effect summaries as build-time data, but lower consumed summary preconditions/effects into IR facts before proof.

### 8. Ownership And Resource Direction

- Make fresh arrays/objects, returned fresh values, aliases, escapes, caller-visible mutation, disposal, and resource lifetime facts first-class IR atoms.
- Use these facts to reduce hard-coded "fresh local mutation is okay" and "escaped mutation is impure" branches in analyzer rules.
- Defer a full Rust-style borrow checker until local ownership/freshness facts are stable and queryable through the public symbolic API.

## Test Plan And Acceptance Criteria

- Architecture tests must prove: no new analyzer raw-SMT hotspots, no public symbolic `SmtFormula` exposure, no Roslyn/C# semantics in `SearchLib`, and no new direct `CSharpConditionToFormula` calls outside approved migration shims.
- Equivalence tests must cover every deleted branch: current analyzer diagnostics and symbolic query behavior must match or become more conservative, never more optimistic.
- Runtime hazard tests must cover divide-by-zero, null dereference, nullable value, index/range bounds, checked overflow, invalid casts, direct throw, negative lengths, and dynamic null binding.
- Public API tests must cover file/text/syntax-tree/node inputs, point/line/span/all-lines targets, invariant facts, proof metadata, unknown reasons, and runtime hazards without raw formulas.
- Fallback tests must cover SMT off, timeout, method budget exceeded, expression/path budget exceeded, cancellation, unsupported lowering, approximate regex, and native Z3 load failure.
- Performance checks must compare query counts, cache hits, and focused test runtime before and after the migration; quality must not be reduced to gain speed.
- Validation commands should use the repo wrappers: build `PurelySharp.Test`, run focused architecture/symbolic/SMT/runtime-hazard/diagnostic suites, build `Tools/PurelySharp.SymbolicCli`, then build `PurelySharp.sln` with `/m:1` and memory limits.

## Assumptions

- Public .NET symbolic API compatibility may break, per the selected "Clean Break" direction.
- CLI behavior should remain stable unless a JSON field change is necessary; additive fields are preferred.
- Z3 should be used aggressively for eligible unresolved proof obligations, but never unboundedly.
- Conservative fallback remains mandatory for unsupported lowering, timeout, cancellation, budget exhaustion, encoding failure, and native-load failure.
- Code deletion is allowed only after shared IR/proof-service behavior is covered by equivalence tests.
