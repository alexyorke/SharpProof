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
- Production inventory currently reports 113,335 C# production lines across
  181 files. The largest modules are `PurelySharp.Symbolic` at 54,351 lines,
  `PurelySharp.Analyzer` at 37,289 lines, `Tools` at 14,989 lines, and
  `SearchLib` at 5,792 lines.
- The largest remaining migration hotspot is `SymbolicProgramPointFacts.cs`,
  now at 8,864 lines. It routes legacy translation through
  `CSharpSmtFormulaTranslator` but still owns much of the formula-first
  path-fact pipeline.
- Direct `CSharpConditionToFormula` usage outside approved shims is gone.
  Symbolic-layer `CSharpSmtFormulaTranslator` wrapper usage is now gone.
  `PurelySharp.Symbolic/SymbolicReachabilityService.cs` now routes the
  remaining legacy condition, value, branch-fact, and pattern fallback entry
  points directly through the narrower
  `PurelySharp.Symbolic/Smt/CSharpConditionToFormula.LegacyFormulaCompatibility.cs`
  boundary. Public
  source-query condition proof still delegates formula fallback through
  reachability, but the legacy bridge is now smaller and easier to delete
  incrementally. The hotspot inventory now reports 0 analyzer-side and 0
  symbolic-side `CSharpSmtFormulaTranslator` shim usages.
  Pattern translation now also tries lowering the incoming SMT value back to a
  `SymbolicTerm` and routes supported non-binding pattern conditions through
  `SymbolicIrLowerer.TryLowerPatternCondition` before the legacy translator
  fallback. Declaration/binding-heavy pattern shapes still stay on the legacy
  path for now.
  Simple single-variable pattern binding facts now also try an IR-first path
  in reachability before the legacy binder. `var`, declaration, empty
  recursive-designation, and `and`-combined versions of those shapes now emit
  equality, string-content equality, and reference non-null facts through IR;
  recursive property, positional, list, and nullable-specialized bindings
  still fall back to the legacy binder.
  Positive simple `is pattern` branch assumptions now also try an IR-first
  path before the legacy branch collector. For the same simple pattern subset,
  reachability now emits binding facts, matched-expression non-null facts when
  the pattern implies them, and the translated pattern formula before falling
  back to legacy branch assumption collection.
  Branch non-null implications now also try an IR-first path before the legacy
  collector for two bounded families: positive type tests / non-null-implying
  patterns, and null-comparison operand implications for `as`, identity-
  preserving reference casts, and conditional access.
  `NotNullWhen` branch assumptions now also try an IR-first path before the
  legacy collector for direct boolean-returning invocations plus the cheap
  boolean wrappers around them (`!call`, `call == true/false`). Member-target
  `MemberNotNullWhen` propagation still remains on the legacy path.
  Reachability branch-state construction now also recognizes `NotNullWhen`
  and current-instance `MemberNotNullWhen` guard branches directly in IR, so
  public invariant queries no longer depend on formula-only compatibility for
  those guard families.
  Switch-expression state collection now also projects simple one-dimensional
  array list-pattern bindings and `when` guard facts into `SymbolicState`
  before falling back to formula compatibility.
  Comparable-value reachability now routes through the shared typed value
  helper instead of issuing its own direct translator fallback.
  Typed value-kind reachability now also routes through the shared untyped
  value helper instead of keeping a second direct translator path.
  Condition-truth reachability now also reuses the shared
  `TryTranslateConditionFormula` helper instead of issuing its own direct
  translator call.
  Built-in-length reachability now lowers entirely through the shared
  `TryLowerBuiltInLengthTerm` IR helper; the direct built-in-length
  translator shim at that boundary is gone.
- IR known-API inventory now distinguishes 8 condition lowerings from 5
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
- Nullable value-parts reachability is now IR-only at the
  `TryTranslateNullableValueParts` boundary. Assigned nullable value mirroring
  and reachability now use the same IR lowering path for direct nullable
  terms, null/default, coalesce, conditional expressions, conditional access,
  and wrapped underlying values.
- `SymbolicProofService` exists, and the proof spine now normalizes many
  exact constant/string/bounds/conditional/type-test facts before SMT.
  `SymbolicState` also detects contradictory exact ownership, disposal, and
  resource-lifetime states syntactically. The proof spine must keep growing
  into the only internal bridge from IR facts to solver formulas.
  Reachability state-path encoding now delegates through a shared
  `SymbolicProofService.TryEncodeStatePathConditions` helper instead of
  constructing a null-scoped proof service ad hoc at the reachability call
  site.
  Formula-path-to-`SymbolicState` compatibility now also lives behind
  `SymbolicProofService.CreateStateFromFormulaPath` and
  `TryCreateStateFromFormulaPath`, so reachability no longer owns its own
  lowered-path helper copies.
  `SymbolicProgramPointFacts` formula-path condition addition now also
  delegates through `SymbolicProofService.AddLoweredFormulaPathCondition`
  instead of lowering raw SMT conditions at that call site.
  `SymbolicProgramPointFacts` SMT-term re-encoding for string content, string
  length, member access, built-in length, and array-dimension length now also
  delegates through `SymbolicProofService.TryEncodeDerivedFormulaTerm`
  instead of lowering SMT terms directly at those call sites.
  Array `Length == Count` alias reachability now also delegates derived
  receiver-term re-encoding through `SymbolicProofService.TryEncodeDerivedFormulaTerm`
  instead of lowering the SMT receiver term directly at that boundary.
  Pattern-condition reachability now also delegates formula-term lowering and
  re-encoding through `SymbolicProofService.TryEncodeDerivedFormulaCondition`
  before falling back to the legacy pattern translator, so
  `SymbolicReachabilityService` no longer lowers the incoming SMT value term
  directly at that boundary.
  `as`-expression assigned-value facts now also create the symbolic target
  reference term directly instead of lowering an SMT target formula back into
  a term before building IR facts.
  IR-first formula proof orchestration now also lives behind
  `SymbolicProofService` helpers for condition truth, formula-path
  feasibility, and branch-condition truth, with reachability remaining only as
  the facade layer for callers.
  Path-fact-aware value translation now also uses
  `SymbolicProofService.TryEncodeTermWithPathState` to prove non-zero divisors
  from lowered path state before encoding divide or remainder terms, so the
  old reachability-side `TryTranslateValueWithPathFacts` translator shim is
  gone.
  Plain reachability value and condition translation now also route safe
  divide/modulo IR encoding through `SymbolicProofService` instead of using
  blanket local `ContainsDivisionOrModulo` gates before fallback.
  Proof-state encoding now also routes facts and path conditions through
  proof-service safe encoding, so IR branch facts and condition-truth probes no
  longer rely on separate local divide/modulo guards before they reach the
  shared proof spine.
  Analyzer branch-condition projection and mirrored path-state formula lowering
  in `PurityAnalysisEngine` now also route through `SymbolicProofService`
  helpers, so analyzer path-state updates no longer directly call
  `SymbolicIrFormulaEncoder.TryEncode` for branch conditions or
  `SymbolicSmtFormulaLowerer.TryLowerCondition` for mirrored path conditions.
  Formula-facing ancestor reachability collection in
  `SymbolicProgramPointFacts` now projects `CollectAncestorReachabilityState`
  back through `SymbolicProofService.TryEncodeStatePathConditions` before
  falling back to the legacy formula-only traversal, so another path-condition
  entrypoint now prefers the IR-native state pipeline.
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
- Built-in length reference lowering now also covers count-backed int
  indexers such as `IReadOnlyList<T>` by emitting `SymbolicCountTerm`
  directly from the shared IR helper instead of relying on incidental
  translator-only count support.
- Built-in span/memory locals and parameters now lower as reference-like IR
  terms, so assigned span or memory length snapshots can flow through the
  shared built-in-length helper instead of relying on the legacy translator.
- Collection-expression lengths with exact spread sources now lower to IR
  additive terms, including array spreads and count-backed collection spreads
  such as `IReadOnlyCollection<T>`.
- One-dimensional built-in element and range access guards now lower entirely
  through a shared IR helper that reuses the common `System.Index` and
  `System.Range` shape resolution paths; the direct reachability translator
  fallback for that boundary is gone.
- String `Substring(start).Length` and `Substring(start, length).Length` now
  lower through the IR indexing partial as integer terms. Unsupported
  `System.Range`/`System.Index` symbol flows at the reachability boundary now
  remain conservative instead of using the old built-in-length translator
  shim.
- Direct range-result lengths such as `values[1..^1].Length` and
  `text[1..^1].Length` now also lower through the IR indexing partial before
  formula fallback. Resolvable assigned `System.Range` locals and parameters
  now use the same IR path for range-result lengths. Count-backed collection
  length shapes and unknown range/index reassignments still remain on the
  compatibility path.
- Built-in view-result lengths such as `text.AsSpan(start).Length` and
  `values.Slice(start, length).Length` now lower through the same IR indexing
  partial before formula fallback. Resolvable `AsSpan(range)` and
  `AsMemory(range)` lengths now also lower through IR when the `System.Range`
  and `System.Index` values can be recovered from direct expressions or simple
  local/parameter assignments. Count-backed collection length shapes and
  unknown range/index reassignments still remain on the compatibility path.
- Assigned built-in-length facts now use the shared symbol-length formula path
  for built-in span or memory locals, so assigned range-backed string views
  such as `ReadOnlySpan<char> view = text.AsSpan(range)` can surface length
  proofs through the public query path instead of relying on incidental
  reference-only helpers.
- Array-creation dimension lengths such as `new T[rows, columns].GetLength(1)`
  now lower the requested dimension directly to the corresponding size
  expression in `SymbolicIrLowerer.Indexing`, reducing reliance on
  formula-first translation for new multidimensional array facts while keeping
  the compatibility fallback for tuple/member and other harder shapes.
- Constant-dimension `Array.GetLength(int)` and `Array.GetLongLength(int)`
  calls now flow through the declarative known-API term lowering registry and
  the indexing partial before formula fallback.
- Constant-dimension `Array.GetLowerBound(int)` and `Array.GetUpperBound(int)`
  calls on statically typed C# arrays now lower through the same registry:
  lower bound becomes `0`, and upper bound becomes `GetLength(dimension) - 1`.
- Statically typed C# array `Rank` member access now lowers to an exact
  integer IR constant, so rank checks no longer need generic member fallback.
- Statically typed multidimensional C# array `Length` member access now lowers
  to an IR product of per-dimension lengths. For array creations, those
  per-dimension lengths are the actual size expressions.
- The shared `TryLowerBuiltInLengthTerm` helper now returns the same
  multidimensional array length product, so reachability and runtime-hazard
  callers can consume total array length through IR instead of member-only
  lowering.
- Nullable conditional access such as `matrix?.Length ?? fallback` now reuses
  the multidimensional array total-length IR helper when the receiver is a
  statically typed C# array.
- Built-in reference-backed length term construction is now shared through
  `SymbolicIrLowerer.TryCreateBuiltInLengthReferenceTerm`, and both
  `SymbolicReachabilityService` and `SymbolicProgramPointFacts` now delegate
  their local length-term helpers to that IR entrypoint.
- Reference-backed string-content term construction is now shared through
  `SymbolicIrLowerer.TryCreateStringContentReferenceTerm`, and both
  `SymbolicReachabilityService` and `SymbolicProgramPointFacts` now delegate
  their local string-content helpers to that IR entrypoint.
- String expression non-null proofs now lower through
  `SymbolicIrLowerer.TryLowerStringNonNullCondition`, so reachability no
  longer falls back to the legacy translator for string non-null facts over
  literals, `string.Empty`, concat/interpolation, coalesce, conditional
  selection, or direct string references.
- String value reachability is now IR-only at the `TryTranslateStringValue`
  boundary. Shared IR now lowers implicit-`this` instance members and
  reference-valued conditional access, and conditional-reference string
  content encodes through the IR encoder without using the legacy translator.
- `NotNullIfNotNull` result non-null reachability facts are now IR-only at the
  `TryCreateNotNullIfNotNullResultNonNullFormula` boundary; local/parameter
  assignment facts and semantic proof queries no longer retain a legacy
  translator fallback for that path.
- Array-dimension length reachability facts are now IR-only at the
  `TryTranslateArrayDimensionLengthValue` boundary. The indexing lowerer now
  also handles casted array receivers and casted array-creation receivers, so
  `GetLength`/`GetLongLength` dimension facts no longer retain a legacy
  translator fallback in reachability or assigned-value mirroring.
- `as`-expression assignment facts are now IR-only at the
  `TryCreateAsExpressionAssignedValueFacts` boundary. Reachability no longer
  keeps the legacy translator either as a fallback path or as an equivalence
  gate for those runtime-type and null-propagation facts.
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
  multidimensional array bounds through IR bounds facts without the old
  reachability translator fallback; `System.Index` and `System.Range` shape
  compatibility now flows through shared IR shape resolution.
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
