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
- Production inventory currently reports 113,089 C# production lines across
  181 files. The largest modules are `PurelySharp.Symbolic` at 54,105 lines,
  `PurelySharp.Analyzer` at 37,289 lines, `Tools` at 14,989 lines, and
  `SearchLib` at 5,792 lines.
- The largest remaining migration hotspot is `SymbolicProgramPointFacts.cs`,
  now at 8,882 lines. It routes legacy translation through
  `CSharpSmtFormulaTranslator` but still owns much of the formula-first
  path-fact pipeline.
- Direct `CSharpConditionToFormula` usage outside approved shims is gone.
  Translator-wrapper usage now exists only in symbolic reachability,
  switch/path-fact compatibility, and path-fact compatibility paths. Public
  source-query condition proof now delegates formula fallback through
  reachability instead of calling translator shims directly. The inventory
  currently reports 0 analyzer-side and 20 symbolic-side
  `CSharpSmtFormulaTranslator` shim usages that should burn
  down as IR lowerings replace formula-first compatibility.
- IR known-API inventory now distinguishes 8 condition lowerings from 1
  value-term lowering. Term lowerings are intentionally tracked separately
  because value-returning APIs such as `Nullable<T>.GetValueOrDefault` should
  lower to `SymbolicTerm` rather than boolean path conditions.
- Nullable term lowering now lives in `SymbolicIrLowerer.Nullable.cs`, reducing
  `SymbolicIrLowerer.cs` and establishing the pattern for domain-specific IR
  lowering partials. Numeric static-value lowering now lives in
  `SymbolicIrLowerer.Numerics.cs`, and string static-value lowering now lives
  in `SymbolicIrLowerer.Strings.cs`. Object API lowering now lives in
  `SymbolicIrLowerer.Objects.cs`, and pattern/type-test lowering now lives in
  `SymbolicIrLowerer.Patterns.cs`. String equality lowering now also lives in
  the string partial, and tuple equality/member lowering now lives in
  `SymbolicIrLowerer.Tuples.cs`. Nullable `HasValue`/`Value` helper lowering
  now lives in `SymbolicIrLowerer.Nullable.cs`, and element/array-dimension
  lowering now lives in `SymbolicIrLowerer.Indexing.cs`. Conversion/as-cast
  lowering now lives in `SymbolicIrLowerer.Conversions.cs`. Member/length/count
  lowering now lives in `SymbolicIrLowerer.Members.cs`. Type/value-kind
  helpers now live in `SymbolicIrLowerer.Types.cs`, operator mapping/
  comparison helpers now live in `SymbolicIrLowerer.Operators.cs`, and known
  API dispatch now lives in `SymbolicIrLowerer.KnownApis.cs`. Shared condition
  factories now live in `SymbolicIrLowerer.Conditions.cs`. Shared syntax,
  variable-symbol, and integral-constant helpers now live in
  `SymbolicIrLowerer.Utilities.cs`; `SymbolicIrLowerer.cs` is about 262 lines.
- Nullable `HasValue` reachability is now IR-only at the
  `TryCreateNullableHasValueCondition` boundary. The nullable IR lowerer now
  models null/default nullable values, nullable coalesce, conditional
  expressions, conditional access, and nullable coalesce with underlying
  fallback values so those cases no longer require the direct nullable
  `HasValue` translator fallback.
- `SymbolicProofService` exists, and the proof spine now normalizes many
  exact constant/string/bounds/conditional/type-test facts before SMT.
  `SymbolicState` also detects contradictory exact ownership, disposal, and
  resource-lifetime states syntactically. The proof spine must keep growing
  into the only internal bridge from IR facts to solver formulas.
- Multidimensional array element-access and rank-generic `Array.GetValue`
  runtime hazards now emit IR `SymbolicExceptionPreconditionAtom` bounds
  triggers before formula-backed compatibility. The fallback inventory count
  is unchanged because Index/Range and other unsupported element-access or
  invocation shapes still keep the legacy path alive.
- Analyzer PS0010/PS0011 site reporting now also recognizes definite
  `Array.GetValue` index hazards by reusing shared `Array.GetValue` in-range
  formula construction plus analyzer path facts, preserving the
  `definite_array_get_value_index_out_of_range` category/source.
- Shared array element bounds construction now lives in
  `SymbolicIrLowerer.TryCreateArrayElementBoundsCondition`; reachability and
  runtime-hazard code consume that IR condition instead of owning duplicate
  per-dimension `SymbolicBoundsAtom` loops.
- Subsequence/slicing bounds now have an IR condition builder in
  `SymbolicIrLowerer.TryCreateSubsequenceInRangeCondition`. Reachability and
  runtime-hazard slicing checks try that IR path first, then keep the legacy
  formula path for unsupported receiver/start/count shapes.
- Built-in string/array/span length term construction is shared through
  `SymbolicIrLowerer.TryLowerBuiltInLengthTerm`; reachability and
  runtime-hazard element-access checks no longer carry duplicate length-term
  branches.
- Simple integer range proofs now use
  `SymbolicIrLowerer.CreateIntegerInRangeCondition` before formula fallback,
  giving checked-conversion range checks an IR condition path for lowerable
  integral terms.
- Binary add/subtract/multiply range proofs now build IR `SymbolicBinaryTerm`
  conditions before formula fallback, so checked arithmetic overflow guards
  have an IR path for lowerable non-division operands.
- Unary negate and increment/decrement range proofs now also build IR
  arithmetic terms before formula fallback, widening checked arithmetic
  overflow guard coverage beyond binary operations.
- Signed division overflow checks now build IR equality conditions for
  left-min/right-minus-one before falling back to the legacy direct formula
  path. Runtime-hazard signed-division triggers now use the same shared IR
  condition builder instead of owning a duplicate condition shape.
- Runtime-hazard divide-by-zero and reference-null IR helpers now use shared
  `CreateIntegerZeroCondition` and `CreateReferenceNullCondition` factories
  instead of owning duplicate zero/null relation atom construction. This does
  not change the formula fallback inventory count.
- Scalar reachability helper migration remains sensitive. Replacing
  `TryCreateReferenceNullComparison`, numeric-zero, non-negative, or
  negative-length helper outputs with IR-first formulas should wait for
  dedicated equivalence locks around analyzer path-fact consumers.
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
- Continue broadening syntactic contradiction handling; exact conflicting
  ownership, disposal, and resource-lifetime states are now covered.
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
- Catch-filter exception paths now preserve pre-`try` facts for simple boolean
  aliases such as `when (inRange)`, and project the alias initializer into
  branch facts so contradictory guarded index paths can be pruned.
- Built-in element-access range checks now lower one-dimensional and
  multidimensional array bounds through IR bounds facts before legacy formula
  fallback; Index/Range shape compatibility still uses the fallback path.
- Subsequence range checks for `Substring`, `Slice`, `AsSpan`, and `AsMemory`
  now lower supported source/start/count bounds through IR conditions before
  formula fallback.
- Delete each old formula branch only after equivalence tests prove identical
  or more conservative behavior.

### 3. Unify runtime hazards through IR preconditions

- Make runtime-hazard candidates emit `SymbolicExceptionPreconditionAtom`
  first for divide-by-zero, null dereference, nullable value, invalid cast,
  index/range, checked overflow, negative length, direct throw, and dynamic
  null binding.
- Built-in one-dimensional and multidimensional element-access index hazards
  and rank-generic `Array.GetValue` hazards now use IR bounds preconditions
  before formula fallback.
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
