# Move SharpProof To An IR-Native Symbolic Analysis Platform

## Summary

SharpProof, formerly PurelySharp, should be a bounded symbolic C# analysis
platform. Purity diagnostics are one consumer of the shared pipeline:

```text
Roslyn/C# -> Symbolic IR -> normalized state -> proof service -> Z3-backed conclusions -> analyzer/API/CLI outputs
```

The public raw-SMT cleanup is mostly complete. The next work is internal:
replace formula-first compatibility seams with IR-first state, proof,
path-fact, hazard, and ownership flows.

## Current State

- Product-facing name is SharpProof. Current package, namespace, diagnostic,
  analyzerconfig, additional-file, and summary-artifact identity remains
  `PurelySharp` for compatibility.
- Architecture inventory reports zero analyzer raw-SMT construction hotspots
  and zero public symbolic `SmtFormula` surfaces.
- Production inventory currently reports 109,481 C# production lines across
  168 files. The largest modules are `PurelySharp.Symbolic` at 50,736 lines,
  `PurelySharp.Analyzer` at 37,050 lines, `Tools` at 14,989 lines, and
  `SearchLib` at 5,792 lines.
- The largest remaining migration hotspot is `SymbolicProgramPointFacts.cs`,
  now at 8,779 lines. It routes legacy translation through
  `CSharpSmtFormulaTranslator` but still owns much of the formula-first
  path-fact pipeline.
- Direct `CSharpConditionToFormula` usage outside approved shims is gone.
  Translator-wrapper usage now exists only in symbolic reachability,
  switch/path-fact compatibility, and path-fact compatibility paths. Public
  source-query condition proof now delegates formula fallback through
  reachability instead of calling translator shims directly. The inventory
  currently reports 0 analyzer-side and 36 symbolic-side
  `CSharpSmtFormulaTranslator` shim usages that should burn
  down as IR lowerings replace formula-first compatibility.
- `SymbolicProofService` exists, but it must keep growing into the only
  internal bridge from IR facts to solver formulas.
- Ownership/resource IR atoms exist, but analyzer rules still own much of the
  real borrow, disposal, escape, and mutation behavior.
- Declarative known-API lowerings are currently a string/regex seed, not the
  dominant model.

## Target Architecture

- `PurelySharp.Symbolic.Ir` owns C#/Roslyn semantic facts: nullness, equality,
  ranges, string/regex predicates, bounds, type tests, exception preconditions,
  freshness, ownership, aliases, borrows, mutation, disposal, and resource
  lifetime.
- `SymbolicState` owns normalized facts, path conditions, symbol versions,
  contradiction status, and stable proof keys.
- `SymbolicProofService` owns IR-to-SMT encoding, query keys, caching,
  budgets, cancellation, timeout/native-load fallback, and unknown reasons.
- `SearchLib` remains solver-only. It must not absorb Roslyn, analyzer, BCL, or
  C# API semantics.
- `PurelySharp.Analyzer` consumes symbolic services. It should not grow new
  path, reachability, hazard, ownership, or raw-SMT proof logic.
- `CSharpConditionToFormula*` remains a migration shim and should shrink as
  declarative IR lowerings replace direct formula translation.

## Implementation Tranches

### 1. Harden the proof spine

- Normalize and deduplicate `SymbolicState` facts and path conditions.
- Add stable proof keys and symbol-version storage to `SymbolicState`.
- Make `SymbolicProofService` normalize states before encoding.
- Keep fallback `SmtAnalysisService` construction only at the documented proof
  boundary or require a compilation-scoped service.
- Add tests for normalization, duplicate proof keys, contradiction handling,
  unsupported facts, fallback behavior, and cache/budget metadata.

### 2. Convert path facts before adding new surface features

- Migrate direct `CSharpConditionToFormula` usage in `SymbolicProgramPointFacts`
  to IR lowerings or temporary `SymbolicSmtFormulaLowerer` compatibility.
  Direct usage is now isolated behind `CSharpSmtFormulaTranslator`; the
  remaining work is deleting formula-first shim calls domain by domain.
- Convert branch assumptions, assignments, nullable/coalesce, length/count,
  string, pattern, switch, loop, and merged-invariant facts into
  `SymbolicFact` or `SymbolicCondition` first.
- Continue broadening `CollectAncestorReachabilityState` until it covers the
  same ancestor facts as the formula collector. Switch sections, foreach
  entries, catch entries, using entries, lock entries, and monotonic loop-body
  invariants are now visible through public invariant queries.
- Prior assignment and normal-completion facts now have a compatibility shadow
  in `SymbolicState` through `CollectPriorAssignmentState`; the remaining work
  is to replace each lowered formula family with native IR construction.
- `for` initial-entry queries now merge ancestor, prior-statement, and
  initializer compatibility shadows into `SymbolicState`, so public node
  queries can expose loop initializer facts as symbolic facts.
- `for` initial-entry reachability now tries the shared `SymbolicState` proof
  path before falling back to legacy formula path conditions.
- Delete each old formula branch only after equivalence tests prove identical
  or more conservative behavior.

### 3. Unify runtime hazards through IR preconditions

- Make runtime-hazard candidates emit `SymbolicExceptionPreconditionAtom`
  first for divide-by-zero, null dereference, nullable value, invalid cast,
  index/range, checked overflow, negative length, direct throw, and dynamic
  null binding.
- Route analyzer `PS0010` and `PS0011` site checks through shared symbolic
  hazard results where equivalent.
- Keep analyzer-only exception summary propagation separate until it is
  covered by equivalence tests.

### 4. Move ownership and resource semantics into IR

- Promote fresh object/array ownership, aliases, escapes, borrows, returned
  ownership, mutation visibility, disposal, and resource lifetime into
  queryable IR facts.
- Use those facts to simplify `PurityAnalysisEngine`, `AssignmentPurityRule`,
  `ReturnStatementPurityRule`, `UsingStatementPurityRule`, and
  `MethodInvocationPurityRule`.
- Defer a full Rust-style borrow checker until local ownership/resource facts
  are stable and queryable.

### 5. Expand declarative lowerings

- Add descriptor groups for nullable, bounds/indexers, collection counts,
  numeric ranges, type tests, disposal/resource APIs, and effect-summary
  preconditions.
- Move C#/BCL recognizers out of `CSharpConditionToFormula*` into IR lowering
  files grouped by domain.
- Keep generated effect summaries as build-time data; lower consumed summary
  preconditions/effects into IR facts before proof.

### 6. Reduce monoliths after behavior is locked

- Split `SymbolicProgramPointFacts`, `SymbolicSourceQueryService`,
  `PurityAnalysisEngine`, `MethodInvocationPurityRule`, and effect-summary
  tooling by responsibility after shared IR/proof behavior is equivalent.
- Treat file growth in those pressure files as architecture debt unless it
  removes duplication elsewhere.

## Acceptance Criteria

- Architecture tests prove no public symbolic `SmtFormula` exposure, no
  analyzer raw-SMT construction hotspots, no Roslyn semantics in `SearchLib`,
  and no direct `CSharpConditionToFormula` calls outside approved shims.
- Inventory scripts report direct translator usage, largest files, and IR
  known-API lowering locations so migration progress is measurable.
- Proof-service tests cover state normalization, duplicate removal, stable
  proof keys, contradiction handling, unsupported IR, fallback behavior,
  timeout, cancellation, and budget exhaustion.
- Equivalence tests guard every deleted hard-coded recognizer or analyzer
  branch.
- Runtime-hazard tests cover analyzer diagnostics and symbolic API results for
  each migrated hazard family.
- Ownership/resource tests cover fresh mutation, escape, alias, returned
  ownership, double dispose, use-after-dispose, borrow conflicts, and `using`.
- Performance checks track proof query count, cache hits, focused test runtime,
  and memory use through repo job-object wrappers.

## Validation

```powershell
git status --short
git diff --check

.\scripts\Get-PurelySharpRawSmtHotspots.ps1 -Json
.\scripts\Get-PurelySharpProductionMetrics.ps1 -Json

.\scripts\Invoke-PurelySharpDotnet.ps1 -MemoryLimitMb 6144 -TimeoutSeconds 420 build .\PurelySharp.Test\PurelySharp.Test.csproj --configuration Debug --no-restore /m:1 "/clp:ErrorsOnly;Summary"

.\scripts\Invoke-PurelySharpDotnet.ps1 -MemoryLimitMb 4096 -TimeoutSeconds 900 test .\PurelySharp.Test\PurelySharp.Test.csproj --configuration Debug --no-build --filter "FullyQualifiedName~ArchitectureReductionTests|FullyQualifiedName~SymbolicIrTests|FullyQualifiedName~SemanticOracleSmtTests|FullyQualifiedName~DiagnosticEvidenceTests" "/clp:ErrorsOnly;Summary"

.\scripts\Invoke-PurelySharpDotnet.ps1 -MemoryLimitMb 6144 -TimeoutSeconds 900 build .\PurelySharp.sln --configuration Debug --no-restore /m:1 "/clp:ErrorsOnly;Summary"

Get-Process dotnet,testhost,vstest.console,MSBuild,VBCSCompiler -ErrorAction SilentlyContinue
```

## Assumptions

- Staged rebrand remains preferred over a hard rename.
- SharpProof is product-facing; `PurelySharp` compatibility identity stays
  until a separate migration project.
- Z3 is used aggressively for eligible proof obligations, but only through
  bounded proof services with caching and conservative fallback.
- Code deletion is allowed only after tests prove shared IR/proof behavior
  preserves behavior or becomes more conservative.
