# Potential Bugs (Second Pass)

A second independent review of the SharpProof codebase. Each entry lists the exact
file and line numbers, the defect, its impact, and a concrete fix. No production code
was modified while producing this report.

---

## Bug 1 — Inverted comparison operators in integer-bound derivation (unsound)

- **File:** `SharpProof/Symbolic/Ir/SymbolicOperationTransferKernel.cs`
- **Lines:** 164–165
- **Category:** Incorrect boolean logic / inverted conditions — soundness defect
- **Code:**
  ```csharp
  // RelationProvesIntegerBound, (termOnLeft: false, ...) arm
  (false, SymbolicRelationOperator.LessThan)          => strictlyPositive ? constant <= 0  : constant <= -1,
  (false, SymbolicRelationOperator.LessThanOrEqual)   => strictlyPositive ? constant < 0   : constant <= 0,
  ```
- **Why it's a bug:**

  `RelationProvesIntegerBound` is reached from `FactProvesIntegerBound`
  (lines 152–157). When `termOnLeft == false`, the stored atom is
  `constant OP term` (the variable is on the right), so:
  - `constant < term` is mathematically identical to `term > constant`
    (the `(true, GreaterThan)` case).
  - `constant <= term` is mathematically identical to `term >= constant`
    (the `(true, GreaterThanOrEqual)` case).

  The correct return values therefore have to **mirror** the `termOnLeft == true`
  arms at lines 161–162:

  | Relation (term on right) | Strictly positive (`term > 0`) | Non-negative (`term >= 0`) |
  |---|---|---|
  | `constant < term`  | `constant >= 0`  | `constant >= -1` |
  | `constant <= term` | `constant > 0`   | `constant >= 0`  |

  The committed code instead returns `constant <= 0`, `constant <= -1`,
  `constant < 0`, `constant <= 0` — every one inverted.

  Concrete impact (unsound): `AddDerivedIntegerBounds` (lines 101–137) is invoked for
  every integer assignment whose `DeriveIntegerBounds` flag is set. Whenever a path
  condition places the constant on the left of the comparison, e.g.:

  ```csharp
  if (-5 < x) { y = x; }   // path condition: -5 < x  ≡  x > -5
  ```

  the buggy `(false, LessThan)` arm evaluates `constant <= 0` → `-5 <= 0` → `true`
  for `strictlyPositive`, so the analyzer fabricates the fact `y > 0`. Since `x`
  could be `-4` (which satisfies `-5 < x`), `y` could be `-4`, making `y > 0`
  false. The false fact then propagates into downstream proof queries
  (`SymbolicProofService.ClassifyReachability`, `ClassifyImplication`,
  `ClassifyBranchFeasibility`, `ClassifyConditionTruth`, `ClassifyHazardTrigger`),
  so the analyzer can **falsely prove** that array accesses, loop bounds, or
  other conditions are safe when they are not.

  Conversely, for genuinely positive constants such as `5 < x` (which really does
  prove `x > 0`), the buggy code evaluates `5 <= 0` → `false`, **missing** the
  valid derivation and yielding false `Unknown` results.

- **How to fix:**
  ```csharp
  (false, SymbolicRelationOperator.LessThan) =>
      strictlyPositive ? constant >= 0  : constant >= -1,
  (false, SymbolicRelationOperator.LessThanOrEqual) =>
      strictlyPositive ? constant > 0   : constant >= 0,
  ```
  Add regression cases in the symbolic test suite that exercise `if (C < x) { ... }`
  and `if (C <= x) { ... }` for both positive and negative `C`, checking that
  downstream `y = x` inherits the correct positivity / non-negativity fact and
  that no false `ProvenTrue` verdict is produced.

---

## Bug 2 — `IsExact` / `UnknownReason` / `Provenance` silently dropped during state invalidation and merge

- **Files / lines:**
  - `SharpProof/Symbolic/Ir/SymbolicIrReferenceScanner.cs:19–25` (`Remove`)
  - `SharpProof/Symbolic/Ir/SymbolicStateMerger.cs:40–44` (`MergePathStatesAcrossAll`)
  - `SharpProof/Symbolic/Ir/SymbolicStateMerger.cs:59–64` (`RewriteToVersions`)
  - `SharpProof/Symbolic/Ir/CompilerProgramPointAnalysis.cs:36–39` (`Collect` always wraps in `Exact`)
- **Category:** Incorrect optional/state handling — soundness guard bypass
- **Code:**

  `SymbolicIrReferenceScanner.Remove` (lines 19–25):
  ```csharp
  private static SymbolicState Remove(
      SymbolicState state,
      Func<SymbolicFact, bool> factMatches,
      Func<SymbolicCondition, bool> conditionMatches) => new SymbolicState(
      state.Facts.Where(fact => !factMatches(fact)),
      state.PathConditions.Where(condition => !conditionMatches(condition)),
      state.SymbolVersions).Normalize();
  ```

  `SymbolicStateMerger.RewriteToVersions` (lines 59–64):
  ```csharp
  private static SymbolicState RewriteToVersions(SymbolicState state, ImmutableDictionary<string, int> versions) =>
      new(
          state.Facts.Select(fact => SymbolicIrVersionRewriter.RewriteToCurrentVersions(fact, versions)),
          state.PathConditions.Select(condition => SymbolicIrVersionRewriter.RewriteToCurrentVersions(condition, versions)),
          versions,
          state.IsContradictory);
  ```

  `SymbolicStateMerger.MergePathStatesAcrossAll` (lines 40–44):
  ```csharp
  return new SymbolicState(
      facts,
      MergePathConditionsAcrossAll(normalized),
      versions,
      normalized.All(static state => state.IsContradictory));
  ```

  `CompilerProgramPointAnalysis.Collect` (lines 36–39):
  ```csharp
  return captured == null
      ? Unsupported(site, result.Truncated ? "iteration-limit" : "target-block")
      : SymbolicLoweringResult<SymbolicState>.Exact(
          captured.Normalize(), new("compiler-cfg-program-point", site.Span, "exact"));
  ```

- **Why it's a bug:**

  The `SymbolicState` constructor (`SymbolicIr.cs:136–160`) defaults
  `isExact = true`, `unknownReason = SymbolicUnknownReason.None`, and
  `provenance = []`. Every other state-transformation method
  (`AddFact`, `AddPathCondition`, `WithSymbolVersion`, `Normalize`, `MarkContradictory`)
  explicitly forwards `IsExact`, `UnknownReason`, and `Provenance` to the new state.

  `Remove`, `RewriteToVersions`, and `MergePathStatesAcrossAll` do **not** — they
  use constructor overloads that silently reset those fields to their defaults. An
  inexact state (produced by `SymbolicReachabilityService.UnsupportedState` when the
  CFG collector cannot produce an exact result, or supplied by a public-API caller
  via `SymbolicConditionProofEngine.ProveAtSyntaxNode`'s 7-argument overload, or
  via `SymbolicRuntimeHazardQueryService.QueryRuntimeHazardsCore`'s `initialState`
  parameter) is therefore treated as exact again after the first invalidation or
  merge step.

  `CompilerProgramPointAnalysis.Collect` compounds this by unconditionally wrapping
  every non-null `captured` state in `SymbolicLoweringResult<SymbolicState>.Exact(...)`
  (line 38), regardless of whether the caller-supplied `initialState` was inexact.
  `SymbolicReachabilityService.BuildStructuralPathStateSnapshot` (lines 79–81) then
  trusts that wrapper's `IsExact` and returns the inner state without the
  `UnsupportedState(...)` fallback.

  End-to-end consequence: `SymbolicProofService.ClassifyReachability`,
  `ClassifyImplication`, `ClassifyBranchFeasibility`, `ClassifyConditionTruth`, and
  `ClassifyHazardTrigger` all guard against inexact input with
  `if (!state.IsExact) return SymbolicProofInfo.Unknown(state.UnknownReason);`
  (lines 7, 24, 50, 118–127, 168). Once `Remove` / `RewriteToVersions` /
  `MergePathStatesAcrossAll` resets `IsExact` back to `true`, those guards no longer
  fire and the proof engine can issue a `ProvenTrue` / `ProvenFalse` / `Reachable`
  verdict from a state that was supposed to be reported as `Unknown`.

  During the normal "no caller-supplied `initialState`" path the bug is latent —
  the initial `new SymbolicState()` is exact, so every derived state is exact and
  the lost metadata is not missed. It becomes exploitable whenever an inexact
  `SymbolicState` flows into `CompilerProgramPointAnalysis.Collect` (via
  `SymbolicReachabilityService.BuildStructuralPathStateSnapshot`) and the CFG
  transfer triggers an invalidation or a merge. The public 7-argument
  `ProveAtSyntaxNode` overload and the internal `QueryRuntimeHazardsCore`
  `initialState` parameter both permit exactly that flow, so the defect is a real
  soundness hole rather than a theoretical concern.

- **How to fix:**

  Forward the metadata in all three transformation methods. For `Remove`:
  ```csharp
  private static SymbolicState Remove(
      SymbolicState state,
      Func<SymbolicFact, bool> factMatches,
      Func<SymbolicCondition, bool> conditionMatches) => new SymbolicState(
      state.Facts.Where(fact => !factMatches(fact)),
      state.PathConditions.Where(condition => !conditionMatches(condition)),
      state.SymbolVersions,
      state.IsContradictory,
      state.IsExact,
      state.UnknownReason,
      state.Provenance).Normalize();
  ```
  For `RewriteToVersions`, forward `state.IsExact`, `state.UnknownReason`,
  `state.Provenance` into the 7-argument constructor.
  For `MergePathStatesAcrossAll`, treat the merged state as inexact whenever
  **any** input state is inexact (e.g. `normalized.Any(s => !s.IsExact)`) and
  propagate the most specific `UnknownReason` from the inexact inputs.

  Additionally, `CompilerProgramPointAnalysis.Collect` should not wrap
  unconditionally in `Exact(...)`. When `initialState?.IsExact == false`, it must
  return `SymbolicLoweringResult<SymbolicState>.Unsupported(...)` (or a new
  `Inexact(...)` factory) so `BuildStructuralPathStateSnapshot` routes through
  `UnsupportedState(cfgState)` and the proof service's `Unknown` guard fires.

  Add a regression that supplies an inexact `initialState` to
  `SymbolicConditionProofEngine.ProveAtSyntaxNode` (7-arg overload) for a method
  body containing an assignment that triggers `Remove` (any local mutation), and
  assert that the returned `SymbolicTruthValue` is `Unknown` with
  `UnknownReason != None`.

---
