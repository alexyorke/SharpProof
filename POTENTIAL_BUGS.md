# Potential Bugs

Analysis date: 2026-07-07

---

## 1. SMT reason-code strings still leak to CLI in two places

**Files:** `Tools/SharpProof.SymbolicCli/Program.cs:519`, `Tools/SharpProof.SymbolicCli/Program.cs:931`

`GetDisplayStatusReason()` and the new `GetDisplayReason()` on `SymbolicConditionProofResult` were fixed, but the CLI still prints raw `Reason` strings for:

- `diagnostic.Reason` at line 519 (from `SymbolicConservativeUnknownDiagnostic`)
- `reason.Reason` at line 931 (from `SymbolicConditionProofReasonSummary`)

`SymbolicConservativeUnknownDiagnostic.Reason` defaults to `"not_common_to_all_candidate_program_points"` (safe), but callers *can* pass arbitrary reason codes. `SymbolicConditionProofReasonSummary.Reason` holds whatever raw reason was aggregated — these leak through to the user.

---

## 2. `"smt_syntactic_no_match"` lacks a display translation

**File:** `SharpProof.Symbolic/Smt/SmtSyntacticClassifier.cs:37`

`GetDisplayStatusReason()` in `SymbolicRuntimeHazardQueryService.cs` does not handle `"smt_syntactic_no_match"`. If this code ever flows through the hazard or proof-reason display path, the raw string reaches the user.

---

## 3. Static shared query cache is unbounded with no eviction

**File:** `SharpProof.Symbolic/Smt/SmtAnalysisService.cs:17-18`

```csharp
private static readonly ConcurrentDictionary<string, PurityProofResult> s_sharedQueryCache = new(...);
private static readonly ConcurrentQueue<string> s_sharedQueryCacheOrder = new();
```

- `s_sharedQueryCache` is never trimmed. If `UseSharedResultCache` is enabled, entries accumulate indefinitely.
- `s_sharedQueryCacheOrder` is written to (`Enqueue`) but never read back for eviction. Dead queue.
- The cache is limited to 4096 entries (`SharedQueryCacheEntryLimit`), but `TryAddSharedResult` checks the dictionary *after* enqueue and does not enforce the limit — it only checks `count > limit` and skips adding. The queue grows unboundedly.

---

## 4. Race on static cache across `SmtAnalysisService` instances

**File:** `SharpProof.Symbolic/Smt/SmtAnalysisService.cs:77-148`

`Classify()` accesses `s_sharedQueryCache` and `s_sharedQueryCacheOrder` without synchronisation across instances. The `_solverLock` is per-instance. When `UseSharedResultCache` is `true`, concurrent instances can race on `TryGetSharedResult` / `AddSharedResult`.

---

## 5. `SmtAnalysisService` never recovers from transient Z3 failures

**File:** `SharpProof.Symbolic/Smt/SmtAnalysisService.cs:172-176`

```csharp
catch (Exception ex) when (IsZ3OrEncodingFailure(ex))
{
    _solverUnavailable = true;
    DisposeProofSearch();
    return Unknown("smt_unavailable");
}
```

Once `_solverUnavailable` is set to `true`, the service permanently returns `Unknown("smt_unavailable")` — even if the underlying issue (e.g. a transient native load failure or resource contention) resolves. The `_disposed` check is also permanently fatal.

---

## 6. Nondeterministic IR encoding causes flaky test expectations

**File:** `SharpProof.Symbolic/SymbolicPublicModels.cs:242-248`

`FormatFactText` can produce either `"value <= 0"` or `"!(value > 0)"` for the same logical condition depending on how the IR lowered the SymbolicFact polarity. Several MaybeFacts assertions were made robust with `Has.Member` + `Is.InRange(2,3)`, but tests at `SymbolicSourceQueryLineTests.cs:1297-1298` still use `Does.Contain("!(copy > 0)")` without a count fallback — could flake if the encoder produces a different negation form.

---

## 7. Known memory-risk path in effect summary

**File:** `Tools/SharpProof.EffectSummary/Program.cs` (`AssemblyEffectSummarizer.VisitThrownExceptionEdges`)

Referenced in `AGENTS.md` as a known OOM risk. When processing large assemblies, `VisitThrownExceptionEdges` can exhaust memory. No bounding or streaming mechanism is documented as present; the path holds the full assembly in memory.

---

## 8. `SymbolicSourceQueryService.cs` is 8,072 lines

**File:** `SharpProof.Symbolic/SymbolicSourceQueryService.cs`

Combines query orchestration, ~30 public/sealed result DTOs, formatting, filtering, projection, and CLI-target output. A single-file class this large is prone to:
- Accidental shared mutable state between methods
- Hard-to-find merge conflicts
- Missed edge cases in switch/pattern-match chains

---

## 9. `SymbolicRuntimeHazardQueryService` is split into partial files

**Files:**
- `SharpProof.Symbolic/SymbolicRuntimeHazardQueryService.cs` (987 lines)
- `SharpProof.Symbolic/SymbolicRuntimeHazardCandidateFactory.cs`
- `SharpProof.Symbolic/SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs` (1,049 lines)

The `SymbolicRuntimeHazardQueryService` class spans three files as `partial`. Two of those files are named `*Factory*` but are technically part of the same class. `IsFallbackDerivedTriggerPrecondition` and `GetDisplayStatusReason` live in the main file, while trigger construction lives in the factory files. This spread makes it easy to accidentally duplicate logic or miss a code path.

---

## 10. `SmtFormulaVersionRewriter` — unchecked for correctness

**File:** `SharpProof.Symbolic/Smt/SmtFormulaVersionRewriter.cs`

This rewriter transforms formula version metadata. If the version rewrite logic misses a formula subclass, stale or incorrectly-versioned formulas could reach the solver, producing wrong proof results. No tests were observed that specifically validate version rewriting.

---

## 11. Several test files are compiled out by default

**File:** `SharpProof.Test/SharpProof.Test.csproj`

```xml
<Compile Remove="EffectSummaryToolTests.cs" />
<Compile Remove="AnalyzerPackagingTests.cs" />
<Compile Remove="ImpactedTestSelectionJsonSession.cs" />
...
```

These files are excluded from `SharpProof.Test` and only compiled as linked files in `SharpProof.ToolingTest`. If someone runs `dotnet test` on `SharpProof.Test.csproj` directly, those tests silently disappear. No CI check verifies they are still runnable.

---

## 12. No `[SetUp]` or `[TearDown]` in either test project

Neither `SharpProof.Test` nor `SharpProof.ToolingTest` has any `[SetUp]`, `[TearDown]`, `[OneTimeSetUp]`, `[OneTimeTearDown]`, or `[ModuleInitializer]` methods. This means:
- `SmtAnalysisService` instances are created fresh per test (expensive but safe)
- No cleanup or isolation guarantees — if a test modifies a static, the corruption leaks to subsequent tests

---

## 13. Formula-backup path silently demotes to `Unsupported`

**Files:** Trigger construction methods in `SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs`

When `TryCreateNumericZeroCondition` fails IR lowering, the formula-fallback path (e.g. `TryTranslateZeroCondition`) produces a trigger with `.formula-fallback` provenance. The trigger is then classified as `Unsupported` by `ClassifyTriggerCore`. This means a real divide-by-zero hazard goes unreported (silent false negative) rather than being flagged as unknown.

---

## 14. `AnalyzerTestHost.TrustedPlatformReferences` is lazily loaded but never cleared

**File:** `SharpProof.Test/AnalyzerTestHost.cs`

```csharp
private static ImmutableArray<MetadataReference>? _trustedPlatformReferences;
```

This static field is populated once via `GetTrustedPlatformReferences()` and held for the lifetime of the test run. If the test runner shares an AppDomain across test projects, stale references could accumulate. The effect depends on test runner isolation mode.

---

## 15. `SymbolicIrFormulaEncoder.TryEncode` returns false for unhandled fact types

**File:** `SharpProof.Symbolic/Ir/SymbolicIrFormulaEncoder.cs`

When encoding fails, `FormatFactText` outputs `"?"` or `"!?"` (depending on polarity). Downstream consumers (CLI output, JSON serialization, diagnostic messages) treat these strings as actual fact text rather than encoding-failure markers. No consumer checks for or warns about `"?"`/`"!?"` sentinel values.

---

## 16. `SymbolicProgramPointFacts.cs` is 13,151 lines — largest single file in the project

**File:** `SharpProof.Symbolic/SymbolicProgramPointFacts.cs`

At 13,151 lines, this is the largest file in the entire codebase. It is an `internal static class` with dozens of methods collecting program-point facts for different syntax constructs (foreach, for, switch, using, lock, etc.). The sheer size makes it easy to:
- Miss a code path when adding a new syntax construct
- Accidentally use mutable shared state (static class, all methods are static)
- Create duplicate logic between similar constructs (e.g., `foreach` vs `for` entry facts)
- Miss updating one of the multiple `MaxMerged*Facts` constants when adding a new bounded collection method

---

## 17. `PurityAnalysisEngine.cs` is 9,053 lines — second largest file

**File:** `SharpProof.Analyzer/Engine/PurityAnalysisEngine.cs`

This is a `partial class` (but only has one file — no other partial parts were found). At 9K+ lines, it combines:
- Entry-point analysis orchestration
- Null-assumption fact generation
- Purity result shaping
- Diagnostic property formatting
- Symbol-to-display-string mapping
- Postcondition checking

Same risks as #16: shared mutable state through fields (`_purityService`, `_smtAnalysis`), hard-to-verify switch chains, and potential for duplication.

---

## 18. `CSharpConditionToFormula` totals ~14,000 lines across partial files

**Files:**
- `SharpProof.Symbolic/Smt/CSharpConditionToFormula.cs` (3,602 lines)
- `SharpProof.Symbolic/Smt/CSharpConditionToFormula.Indexing.cs` (2,961 lines)
- `SharpProof.Symbolic/Smt/CSharpConditionToFormula.Values.cs` (2,999 lines)
- `SharpProof.Symbolic/Smt/CSharpConditionToFormula.Patterns.cs` (2,935 lines)
- `SharpProof.Symbolic/Smt/CSharpConditionToFormula.StringRegex.cs` (1,952 lines)
- `SharpProof.Symbolic/Smt/CSharpConditionToFormula.LegacyFormulaCompatibility.cs`

This `partial class` split across 6+ files translates C# syntax to SMT formulas. The legacy formula compatibility file is particularly risky — it may contain workarounds for older encoding bugs that are no longer relevant, but no tests validate whether the compatibility layer still matches current encoding output.

---

## 19. 16 distinct `.formula-fallback` provenances across the codebase

**Primary file:** `SharpProof.Symbolic/SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs`

Each `.formula-fallback` provenance represents a code path where the analyzer gave up on IR-based lowering and fell back to a translated formula. The formula-fallback triggers are classified as `Unsupported`, meaning **the runtime hazard is silently not reported** for any code pattern that hits one of these fallbacks. This is not a hypothetical — it is the design of the fallback path.

Full list of 16 unique fallback provenances:

| # | Provenance | Hazard Type |
|---|-----------|-------------|
| 1 | `ir.runtime-hazard.divide-by-zero.formula-fallback` | DivideByZero |
| 2 | `ir.runtime-hazard.index.out-of-range.formula-fallback` | IndexOutOfRange |
| 3 | `ir.runtime-hazard.null-dereference.formula-fallback` | NullDereference |
| 4 | `ir.runtime-hazard.unbox-null.formula-fallback` | UnboxNull |
| 5 | `ir.runtime-hazard.argument-null.formula-fallback` | ArgumentNull |
| 6 | `ir.runtime-hazard.nullable-value.without-value.formula-fallback` | NullableValueWithoutValue |
| 7 | `ir.runtime-hazard.invalid-cast.formula-fallback` | InvalidCast |
| 8 | `ir.runtime-hazard.dynamic-null-binding.formula-fallback` | DynamicNullBinding |
| 9 | `ir.runtime-hazard.checked-integral.signed-division-overflow.formula-fallback` | CheckedIntegralOverflow |
| 10 | `ir.runtime-hazard.checked-integral.binary-overflow.formula-fallback` | CheckedIntegralOverflow |
| 11 | `ir.runtime-hazard.checked-integral.unary-minus-overflow.formula-fallback` | CheckedIntegralOverflow |
| 12 | `ir.runtime-hazard.checked-integral.increment-overflow.formula-fallback` | CheckedIntegralOverflow |
| 13 | `ir.runtime-hazard.checked-integral.decrement-overflow.formula-fallback` | CheckedIntegralOverflow |
| 14 | `ir.runtime-hazard.checked-integral.compound-signed-division-overflow.formula-fallback` | CheckedIntegralOverflow |
| 15 | `ir.runtime-hazard.checked-integral.compound-assignment-overflow.formula-fallback` | CheckedIntegralOverflow |
| 16 | `ir.runtime-hazard.checked-conversion.overflow.formula-fallback` | CheckedConversionOverflow |

Plus the dynamic pattern `provenance + ".formula-fallback"` at line 453 of `IrTriggers.cs` (used for switch-expression-no-match fallback).

---

## 20. Thread-static `PurityProofSearch` is deliberately leaked

**File:** `SharpProof.Symbolic/Smt/SmtAnalysisService.cs:207-209`

```csharp
// Note: We deliberately do not dispose the thread-local solver context here
// to allow caching and reuse across SmtAnalysisService instances on the same thread.
```

The `PurityProofSearch` (which wraps `SmtSolver` which wraps the native Z3 context) is stored as `[ThreadStatic]` and is **not disposed** when `SmtAnalysisService.Dispose()` is called. It is only disposed when the solver becomes unavailable (`ClassifyCore` exception path). This means:

- If a test or tool creates many `SmtAnalysisService` instances on the same thread, all share one Z3 context (good).
- But that shared Z3 context is only released when:
  a. The `ClassifyCore` method catches a Z3 exception, OR
  b. The thread exits.
- If Z3 holds native heap memory (~100+ MB typical), it is held until thread exit even after all `SmtAnalysisService` instances are disposed. In long-running processes (IDE, CLI server mode), this is a gradual memory leak.

---

## 21. No `Debug.Assert` anywhere in production code

No `Debug.Assert` or `Debug.Fail` calls exist in `SharpProof.Symbolic`, `SharpProof.Analyzer`, or `SearchLib`. This means:

- Precondition violations (null arguments, invalid state, out-of-range values) are silently ignored in Debug builds.
- Invariants that could catch bugs during development are never checked.
- The `PurityAnalysisEngine` (9K lines) and `SymbolicProgramPointFacts` (13K lines) have no internal consistency assertions.

The project uses `throw new ArgumentNullException` for public method parameter validation but has no debug-only contract checking for internal state.

---

## 22. Inconsistent provenance naming conventions

Two distinct naming conventions are used for provenance strings, with no documented boundary:

- **`"ir.*"`** — Used for IR-backed paths: `"ir.path.inline-assignment"`, `"ir.runtime-hazard.divide-by-zero.trigger"`, etc.
- **`"analyzer.*"`** — Used for analyzer-layer assumptions: `"analyzer.null_assumption"`, `"analyzer.path.null"`, etc.

The `"analyzer.*"` provenances (`PurityAnalysisEngine.cs:4033-4034`) are created by the analyzer layer directly, bypassing the symbolic IR entirely. Downstream consumers that filter or group by provenance prefix (`"ir."` vs `"analyzer."`) may behave differently depending on which path generated the fact. This inconsistency could cause:

- Missing facts in CLI output that filters by `ir.*` prefix
- Double-counting in summary statistics that group by provenance

---

## 23. `SmtSyntacticClassifier` unreachable-code pattern at line 37

**File:** `SharpProof.Symbolic/Smt/SmtSyntacticClassifier.cs:37`

```csharp
return Unknown("smt_syntactic_no_match");
```

This returns `Unknown` with a hardcoded reason string. The `"smt_syntactic_no_match"` string is never handled by any display-translation method (`GetDisplayStatusReason`, `GetDisplayReason`, or any other reason mapper). If this `Unknown` result ever reaches a user-facing display, the raw string leaks. Classes in the same project (`SmtAnalysisService`) use `"smt_*"` prefixed reasons that *are* handled, suggesting `"smt_syntactic_no_match"` was simply missed when the display layer was added.

---

## 24. `SymbolicProofService` creates hidden fallback `SmtAnalysisService` with default options

**File:** `SharpProof.Symbolic/SymbolicProofService.cs:1142`

```csharp
using var fallback = new SmtAnalysisService(SmtAnalysisOptions.Default);
```

When the caller's `SmtAnalysisService` is null or unavailable, this fallback uses `SmtAnalysisOptions.Default` (Bounded mode, 750ms timeout). If the calling code was configured with `Deep` mode or a longer timeout, the fallback silently ignores those settings and uses bounded defaults. This means a proof that requires deep analysis or more time may silently fail on the fallback path.

---

## 25. `CompilationPurityService` disposal relies on the Roslyn compilation end action

**File:** `SharpProof.Analyzer/Engine/CompilationPurityService.cs:22-30`

```csharp
SmtAnalysis = new SmtAnalysisService(smtOptions);
...
public void Dispose() { SmtAnalysis.Dispose(); }
```

This is called from `SharpProofAnalyzer.cs`:

```csharp
compilationEndAction = context =>
{
    purityService.Dispose();
    ...
};
```

If the Roslyn compilation end action is not guaranteed to fire (e.g., if the analyzer host crashes, is cancelled, or the compilation object is collected early), the `SmtAnalysisService` (and its thread-static Z3 context) is never disposed. In the VS IDE, analyzer instances can be long-lived; a nondisposed Z3 context may hold native memory until the VS process exits.

---

## 26. New untracked files on the `codex/refactor-cleanup-bugs` branch

**Files:**
- `SharpProof.Symbolic/SymbolicDispatchFacts.cs` (untracked)
- `POTENTIAL_BUGS.md` (now gitignored)

The presence of `SymbolicDispatchFacts.cs` as an untracked file on a cleanup/refactor branch suggests work-in-progress that has not been committed. If this file is expected to be part of the branch but was accidentally not staged, it represents missing code. If it is an experimental file, it should be in a `.gitignore` pattern or explicitly excluded.

---

## 27. `SharpProof.Symbolic` has 23 files exceeding 500 lines each

This creates a structural risk: with 23 large files in one project, the cognitive load to understand the full codebase is high. Files over 500 lines in `SharpProof.Symbolic`:

| Lines | File |
|-------|------|
| 13,151 | `SymbolicProgramPointFacts.cs` |
| 6,862 | `SymbolicSourceQueryService.cs` |
| 4,729 | `SymbolicReachabilityService.cs` |
| 3,602 | `CSharpConditionToFormula.cs` |
| 3,389 | `SmtSyntacticClassifier.cs` |
| 2,999 | `CSharpConditionToFormula.Values.cs` |
| 2,961 | `CSharpConditionToFormula.Indexing.cs` |
| 2,935 | `CSharpConditionToFormula.Patterns.cs` |
| 2,641 | `SymbolicRuntimeHazardCandidateFactory.cs` |
| 2,335 | `SymbolicComplexityService.cs` |
| 2,101 | `SymbolicIrLowerer.Indexing.cs` |
| 2,071 | `SwitchPathConditionBuilder.cs` |
| 1,952 | `CSharpConditionToFormula.StringRegex.cs` |
| 1,494 | `SymbolicIr.cs` |
| 1,411 | `SymbolicProofService.cs` |
| 1,137 | `SymbolicQueryApi.cs` |
| 1,053 | `SymbolicCapabilityService.cs` |
| 961 | `SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs` |
| 870 | `SymbolicRuntimeHazardQueryService.cs` |
| 706 | `SymbolicIrLowerer.Strings.cs` |
| 658 | `SymbolicInvariantService.cs` |
| 529 | `SmtAnalysisService.cs` |
| 507 | `SymbolicIrLowerer.Patterns.cs` |

Plus `PurityAnalysisEngine.cs` in `SharpProof.Analyzer` at 9,053 lines. Many of these are `partial classes` split across files, but the total code volume in each logical unit is very high, making comprehensive code review difficult.

---

## 28. Hundreds of `null!` (null-forgiving) assignments suppress real null-safety

**Scope:** Widespread across `SharpProof.Symbolic`, `SharpProof.Analyzer`, and `SearchLib` (hundreds of sites).

The `null!` operator (null-forgiving) tells the compiler to suppress nullable warnings, but does not guarantee the value is actually non-null at runtime. Common patterns:

- `out` parameter initialization before `Try*` methods return false
- Fallback values assigned when a `Try*` method fails
- Temporary variables used in `Try*` return patterns

Examples from the codebase:
- `SymbolicIrLowerer.cs:108`: `condition = null!;` — caller returns false immediately after, so this is technically safe, but a future refactor that fails to check the return value would get an NRE.
- `SymbolicIrLowerer.Indexing.cs` (21+ sites): Same pattern throughout indexed lowering methods.
- `PurityAnalysisEngine.cs` (20+ sites): Used in the analysis engine for out parameters.
- `SmtSolver.cs:1119`: `negatedFormula = null!;` — in a switch/default fallback.

The risk is not that any single site is wrong, but that the pattern teaches contributors to use `null!` as a crutch rather than restructuring code to avoid nullable out-parameter patterns.

---

## 29. `== null` used 501 times vs `is null` used 0 times (effectively)

**Scope:** `SharpProof.Symbolic`

Search found 501 uses of `== null` across `SharpProof.Symbolic` and effectively zero uses of `is null`. The `is null` pattern is the modern C# best practice because it cannot be overloaded by a custom `==` operator. While `System.String` and most types used in this codebase do not overload `==`, any future type that does (or any struct compared as nullable) could produce incorrect null-check results.

The two `is null` findings were:
- One used inside a `==` comparison (effectively not a standalone null check)
- One is a type pattern, not a null check

The consistency problem is severe enough that bulk migration effort is warranted.

---

## 30. `DefaultMaxConditions` constant has two different values with the same name

**File:** `SharpProof.Symbolic/SymbolicSourceQueryService.cs`

| Location | Line | Value | Context |
|----------|------|-------|---------|
| `SymbolicInvariantTargetPathSummary.DefaultMaxConditions` | 2726 | **8** | Used in `SymbolicInvariantTargetPathSummary.FromTargets()` |
| `SymbolicCompactQueryOptions.DefaultMaxConditions` | 3078 | **50** | Used in `SymbolicCompactQueryOptions` for JSON output |

Both are `private const int DefaultMaxConditions`. The 6x difference between target-path summarization (8) and JSON output truncation (50) is undocumented. A developer changing one could accidentally change the wrong one, or assume they are the same limit.

---

## 31. Small bounded-analysis constants may silently truncate real-world data

Multiple constants define very low limits for symbolic analysis. When exceeded, the analyzer silently stops collecting facts, potentially producing incomplete results without warning:

| Constant | Value | File | Impact |
|----------|-------|------|--------|
| `MaxMergedIfElseFacts` | **16** | `SymbolicProgramPointFacts.cs:18` | If-else chains with >16 unique facts lose information |
| `MaxMergedSwitchFacts` | **32** | `SymbolicProgramPointFacts.cs:19` | Switch with >32 fact-generating cases loses information |
| `MaxFiniteForeachElementFacts` | **8** | `SymbolicProgramPointFacts.cs:22` | Foreach over collection with >8 elements loses per-element facts |
| `MaxTryCompletionBranches` | **8** | `SymbolicProgramPointFacts.cs:21` | Try-catch with >8 catch blocks loses branches |
| `MaxMergeableFactsPerTargetPerState` | **4** | `PurityAnalysisEngine.StateMerge.cs:16` | Very low — state merge may lose most facts |
| `MaxMergedStateGuardFactsPerTargetPerState` | **6** | `PurityAnalysisEngine.StateMerge.cs:18` | Low — may lose guard conditions in state merge |
| `MaxStructuralNullStateDepth` | **4** | `SymbolicProgramPointFacts.cs:24` | Deeply nested null-conditional chains may be truncated |
| `MaxScopedBlockCompletionStatements` | **32** | `SymbolicProgramPointFacts.cs:23` | Long block sequences may lose completion facts |
| `MaxEqualitySubstitutionPasses` | **4** | `SearchLib/SmtSolver.cs:16` | Complex equality chains may not be fully substituted |
| `MaxEqualitySubstitutionReplacementNodes` | **32** | `SearchLib/SmtSolver.cs:17` | Large substitution expressions may be truncated |
| `MaxBoundedRepeat` | **64** | `SearchLib/Z3FormulaEncoder.cs:561` | Regex with >64 repetitions may not be encoded correctly |
| `MaxCharacterClassRangeCount` | **512** | `SearchLib/Z3FormulaEncoder.cs:564` | Very large character classes may hit this in real regex |

None of these limits are user-configurable. No diagnostic is emitted when a limit is hit (silent truncation).

---

## 32. Regex validation timeout of 50ms may be too short

**File:** `SearchLib/SmtSolver.cs:15`

```csharp
private const int ConcreteRegexValidationTimeout = 50;
```

This is used as a `CancellationToken` timeout for regex pattern compilation/validation. Complex regex patterns (nested lookaheads, backreferences, large character classes) can take significantly longer than 50ms to compile in .NET's `Regex` class. A real-world regex pattern from user code could cause a `TimeoutException` or incomplete validation that is silently swallowed.

---

## 33. `SmtResourceBudget.RlimitPerMillisecond` calculation may silently lose precision

**File:** `SearchLib/SmtResourceBudget.cs:28-37`

```csharp
var rlimit = budget.TotalMilliseconds * RlimitPerMillisecond;
if (rlimit >= uint.MaxValue) return uint.MaxValue;
return (uint)rlimit;
```

`budget.TotalMilliseconds` returns `double`. Multiplying by 4000 and casting to `uint` can silently lose precision for:
- Very large timeouts: `double` multiplication may overflow to `Infinity`, which becomes `0` when cast to `uint`
- Fractional millisecond budgets: `(uint)(0.5 * 4000) = (uint)2000.0 = 2000` (OK)
- Very small budgets: `(uint)(0.001 * 4000) = (uint)4.0 = 4` (OK, but lossy)

The `±15%` comment at the declaration acknowledges the imprecision, but the unchecked `double → uint` cast is a potential source of silent zero-budget scenarios.

---

## 34. `ConcurrentQueue` write-only pattern in static cache (dead store)

**File:** `SharpProof.Symbolic/Smt/SmtAnalysisService.cs:18`

```csharp
private static readonly ConcurrentQueue<string> s_sharedQueryCacheOrder = new();
```

This queue tracks insertion order for the shared query cache. However, no code in `SmtAnalysisService.cs` ever dequeues entries. The queue grows unboundedly as queries are cached. The only consumer would be an eviction policy — but no eviction policy exists. This is either dead code (vestigial from a planned feature) or a bug waiting to happen (if eviction is ever implemented without also adding a dequeue call).

---

## 35. `Compile Remove` on test files hides test gaps from CI

**File:** `SharpProof.Test/SharpProof.Test.csproj`

```xml
<Compile Remove="EffectSummaryToolTests.cs" />
<Compile Remove="AnalyzerPackagingTests.cs" />
<Compile Remove="ImpactedTestSelectionJsonSession.cs" />
...
```

These files exist in the `SharpProof.Test` directory but are removed from compilation. They are re-included as linked files in `SharpProof.ToolingTest.csproj`. This means:

- Running `dotnet test SharpProof.Test/SharpProof.Test.csproj` silently skips these tests (no warning, no error).
- If a CI pipeline runs only `SharpProof.Test` (e.g., for a focused PR), these tests are never executed.
- The `ImpactedTestSelectionJsonSession.cs` name suggests a dynamic test-selection system — if this is the only place impact analysis runs, it may be silently disabled in CI.

---

## 36. `SmtResourceBudget` unchecked cast from `double * long` to `uint`

**File:** `SharpProof.Symbolic.SearchLib/SmtResourceBudget.cs`

The budget calculation does unchecked `(uint)(doubleValue * longValue)`. If the product exceeds `uint.MaxValue`, it silently wraps around. The caller (`SolverCanProceed` / `ConsumedResourceCount`) may then observe an unexpectedly small budget and abort a legitimate solver attempt.

## 37. `SmtSolver.ConsumedResourceCount` non-atomic `+=`

**File:** `SharpProof.Symbolic.SearchLib/SmtSolver.cs`

`ConsumedResourceCount += delta` without `Interlocked.Add` or a lock. Though accessed from `[ThreadStatic]` scopes, Z3 callbacks may fire from different threads, producing a torn read. Could cause phantom budget violations or missed timeouts.

## 38. 21 uncovered `default:` switch cases in AnalyzerEngine

**Files:** 14 files across `SharpProof.Analyzer`

Each `default:` in a `switch` over an enum or discriminated union silently handles unknown values with no logging or assertion. When new subtypes are added, the compiler provides no warning about the missed case.

## 39. 30 unbounded `File.Exists` + `File.ReadAllText` calls in Symbolic

**File:** `SharpProof.Symbolic/*.cs` (multiple files)

File I/O scattered across the Symbolic project with no cancellation token, no `async`, and no retry logic. Under heavy CI contention or locked-file scenarios, these calls block the thread and may throw unhandled `IOException`.

## 40. `SmtAnalysisServiceTests.cs:559` contains `Thread.Sleep(20)`

**File:** `SharpProof.ToolingTest/SmtAnalysisServiceTests.cs:559`

A hard-coded 20ms sleep introduces flakiness on slower CI runners. If the solver takes longer than 20ms, the test may fail intermittently; if the solver finishes faster, the sleep wastes wall-clock time.

## 41. `Assert.Ignore()` and `Assert.Inconclusive()` used in production test files

**Files:** `SharpProof.ToolingTest/ConstantsTests.Helpers.cs:33`, `SharpProof.ToolingTest/ReadmeExampleFixture.cs:49`

`Assert.Ignore()` silently skips the body of a test method (appears in dead code). `Assert.Pass("Regenerated...")` in `ReadmeExampleFixture.cs` swallows assertions when an environment variable is set — the test always "passes" without verifying anything.

## 42. `global.json` pins SDK to `9.0.315`

**File:** `global.json`

Pinning to a patch-specific SDK (`9.0.315`) means developers without that exact version must rely on `rollForward`. If `rollForward` is not configured, `dotnet build` fails immediately. CI runners with auto-updated SDKs may lack this specific patch.

## 43. `SharpProof.EffectSummary` and `SharpProof.Fuzz.Core` are large files

**Files:**
- `Tools/SharpProof.EffectSummary/Program.cs` — 5,297 lines
- `Tools/SharpProof.Fuzz.Core/Program.cs` — 3,256 lines

Both are single-file `Program.cs` with all logic inlined. This mirrors the same pattern as the 14K-line files in Symbolic — poor navigability, high cognitive load, and difficult code review.

## 44. Temp files in corpus report never cleaned up on failure

**File:** `Tools/SharpProof.Fuzz.Core/Program.cs` (temp path construction)

`Path.Combine(Path.GetTempPath(), "sharpproof-" + Guid.NewGuid()...)` creates temporary directories. If the process exits abnormally (crash, OOM, `TaskCanceledException`), the temp directory is orphaned and accumulates on disk.

## 45. `static readonly HashSet<string>` in `Shared\Constants.cs` is mutable and publicly accessible

**Files:**
- `Shared\SharpProof.Shared/Constants.cs:27,184,188`
- `Shared\SharpProof.Shared/BclPurityFallbackHeuristics.cs:381`

Three `static readonly HashSet<string>` fields can be mutated by any caller via `.Add()`/`.Remove()`/`.Clear()`. From multiple threads, concurrent mutation causes undefined behavior. They should be `ImmutableHashSet<string>` or `IReadOnlySet<string>` backed by a frozen set.

## 46. `SymbolicComplexityService.cs:1550` redundant `== false` on bool method

**File:** `SharpProof.Symbolic/SymbolicComplexityService.cs:1550`

```csharp
if (SomeBoolMethod(...) == false)
```

The method returns `bool` already; `== false` is redundant with `!`. This may indicate a logic inversion intent that should be clarified or simplified.

## 47. `GetFullMetadataName` duplicated across 3 files

**Files:**
- `SharpProof.Symbolic/SymbolicHelper.cs` (appears 2×)
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs`
- `SharpProof.Symbolic.SearchLib/MonotonicIdProvider.cs`

Identical code repeated in three locations. Any bug fix or enhancement to the metadata name logic must be replicated across all three copies.

## 48. Mutable `static readonly` collections — thread safety risk

Same root cause as #45. Two additional `static readonly HashSet<string>` fields in `BclPurityFallbackHeuristics.cs` are publicly accessible and not synchronized. Concurrent reads during `ContainsKey`/`Contains` while another thread calls `.Add()` may produce torn reads or corrupt the internal hash table.

---

**Total: 48 documented potential bugs (initial 35 + 13 from six-agent exploration).**

---

## 49. Expression-bodied methods silently skipped in resource release analysis

**File:** `SharpProof.Analyzer/Engine/PurityAnalysisEngine.cs:2664-2669`

`IsOwnedResourceReleasedOnAllSyntaxPaths` checks `syntaxReference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax { Body: { } body }` — expression-bodied methods (`=>` syntax) and property accessors are silently skipped. The function returns `false`, which causes the caller to skip the owned-resource check, producing a false-positive "missing-dispose" impurity report for methods that use `=>` syntax but properly return/transfer resources.

---

## 50. Delegate `+=` compound assignment incorrectly falls through to `Unresolved`

**File:** `SharpProof.Analyzer/Engine/PurityAnalysisEngine.cs:5397-5402`

```csharp
if (valueTargets != null &&
    currentState.DelegateTargetMap.TryGetValue(targetSymbol, out var currentTargets))
{
    // merge
}
else
{
    nextState = nextState.WithDelegateTarget(targetSymbol, PotentialTargets.Unresolved);
}
```

The `else` branch fires in two cases: (a) `valueTargets` resolved as `null`, or (b) the target symbol is not yet in `DelegateTargetMap`. Case (b) is the bug — when `valueTargets` resolves successfully but this is the first assignment to this delegate variable, the code should store the resolved targets, not mark it `Unresolved`.

---

## 51. Missing path-condition unsatisfiability check in `TryCreateSuccessorState`

**File:** `SharpProof.Analyzer/Engine/PurityAnalysisEngine.cs:3862-3867`

When `addedSymbolicBranchAssumption` is true but `addedBranchAssumptions` is false, the method returns `true` and propagates the state without checking whether the resulting path conditions are unsatisfiable. All other code paths in the same method call `ArePathConditionsUnsatisfiable` before returning. This means a provably-unreachable successor state can leak into the CFG dataflow, causing spurious impurity reports downstream.

---

## 52. `WorklistPuritySolver` marks metadata-only methods as unconditionally pure

**File:** `SharpProof.Analyzer/Engine/Analysis/WorklistPuritySolver.cs:52-54`

```csharp
if (method.DeclaringSyntaxReferences.Length == 0)
{
    results[method] = PurityAnalysisEngine.PurityAnalysisResult.Pure;
    continue;
}
```

Methods without source syntax (e.g., abstract interface declarations, metadata types) are marked pure without checking whether their implementations are impure. If a caller invokes an interface method, and the concrete implementation (known to be impure, reachable from the call graph) has already been analyzed, the solver still marks the slot method as pure. This creates an inconsistency where the same method is pure in one context but its implementations are impure.

---

## 53. `ConditionTruthCacheKey` holds strong reference to `SmtAnalysisService`

**File:** `SharpProof.Analyzer/Engine/ExecutionVisibility.cs:955-975`

The `ConditionTruthCacheKey` struct stores `SmtAnalysisService?` and uses `ReferenceEquals` / `RuntimeHelpers.GetHashCode` for comparison. The cache lives in a `ConcurrentDictionary` inside a `ConditionalWeakTable` value — the `SmtAnalysisService` strong reference prevents it from being GC'd as long as any `SemanticModel` that populated the cache remains alive. In long-lived analyzer hosts (VS IDE), this retains the full Z3 context (~100+ MB native). Additionally, two semantically equivalent `SmtAnalysisService` instances from different passes generate separate cache entries and never share results.

---

## 54. Write-only property signature forces `.get` suffix in `IsKnownPureBCLMember`

**File:** `SharpProof.Analyzer/Engine/ImpurityCatalog.cs:96-98`

```csharp
if (symbol.Kind == SymbolKind.Property)
{
    if (!signature.EndsWith(".get") && !signature.EndsWith(".set"))
    {
        signature += ".get";
```

For write-only (setter-only) properties, the code appends `.get` which generates a signature for a getter that does not exist. The match against `KnownPureMethods` would incorrectly match a getter catalog entry for a write-only property, potentially classifying a mutating setter operation as pure. The code should check `propertySymbol.GetMethod != null` before defaulting to `.get`.

---

## 55. `MergeDelegateTargetMapsFromBlockStates` overwrites earlier entries on conflict

**File:** `SharpProof.Analyzer/Engine/PurityAnalysisEngine.StateMerge.cs:25-40`

`MergeDelegateTargetMapsFromBlockStates` uses a merge-on-first-seen strategy: `map.SetItem(kvp.Key, PotentialTargets.Merge(current, kvp.Value))`. The `Merge` function for `PotentialTargets` returns `Unresolved` if either operand is `Unresolved`. Since block states are processed in arbitrary iteration order from `exitBlockStates.Values`, the first encountered `Unresolved` entry poisons the merged result even if later blocks have concrete targets.

---

## 56. `GetRelatedLocalAliases` fixed-point loop has unbounded growth potential

**File:** `SharpProof.Analyzer/Engine/PurityAnalysisEngine.cs:6604-6651`

The `while (changed)` loop adds bidirectional alias relationships. The `Func` evaluated in the loop can have unbounded growth due to transitive closures through many variables. The HashSet can grow very large with no guard, iterating through all `DescendantNodes` linearly on each pass over a large method body.

---

## 57. `TryGetConstantBranchDecision` unconditionally invokes SMT for every branch

**File:** `SharpProof.Analyzer/Engine/PurityAnalysisEngine.cs:3685-3717`

For every basic block with a branch value, the method first checks compile-time constants, then calls `IsConditionAlwaysTrueUsingSmt` and `IsConditionAlwaysFalseUsingSmt`. Even simple boolean comparisons that could be resolved with symbolic state facts trigger full SMT queries. On large CFGs with many branches, this adds substantial overhead with no caching.

---

## 58. `ImpurityCatalog.UseConfiguredOverrides` uses `AsyncLocal` — latent fragility

**File:** `SharpProof.Analyzer/Engine/ImpurityCatalog.cs:13,30-35`

```csharp
private static readonly AsyncLocal<AnalyzerConfiguration?> _configuredOverrides = new();
```

`AsyncLocal` values flow to async continuations only when `ExecutionContext` is captured. Roslyn analyzers run synchronously so this is fine today, but if `DeterminePurityRecursiveInternal` were ever called from an async context (e.g., `CompilationPurityService` in a background task), and any `ValueTask` or bare `Task` without `ConfigureAwait` is used in the call chain, the context may be lost.

---

## 59. SMT variable name collision for non-constant element indices

**File:** `SharpProof.Symbolic/Ir/SymbolicIrFormulaEncoder.cs:376-381`

`CreateElementAccessIndexText` returns `"?"` for any index formula that is not an `SmtIntegerConstant`. When encoding `a[x]` and `a[y]` where `x` and `y` are distinct symbolic variables, both element accesses produce the SMT variable name `a[?]`. The solver treats these as identical, yielding incorrect proof results for any condition involving indexed references with non-constant indices.

---

## 60. Enum conversion restricted to Int32-backed enums with type classification mismatch

**Files:** `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Conversions.cs:100-108`, `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Types.cs:57-71`

`TryLowerSupportedConversionTerm` only matches enum-to-int casts whose underlying type is `SpecialType.System_Int32`. However, `IsIntegerSmtType` classifies **all** enum types (including those backed by `byte`, `short`, `long`, `ulong`) as `SmtValueKind.Int`. A `long`-backed enum cast to `long` will fail lowering because the pattern match on `EnumUnderlyingType.SpecialType` never fires — the enum's integral value is silently lost.

---

## 61. Jagged array element access round-trips incorrectly through SMT encoding

**File:** `SharpProof.Symbolic/Ir/SymbolicSmtFormulaLowerer.cs:204-233`

`TryParseIndexedVariable` splits on the last `[` in the variable name, interpreting `aName[0][1]` as an element access with receiver `aName[0]` and index `1`. The receiver is reconstructed as `new SymbolicVariableTerm("aName[0]", Reference)`, but `"aName[0]"` is a phantom variable with no semantic counterpart. The resulting `SymbolicElementTerm` does not structurally match the original IR term that was encoded. In formula-fallback paths that round-trip SMT formulas through `SymbolicSmtFormulaLowerer`, this can cause deduplication misses or incorrect condition-key generation.

---

## 62. `IsBuiltInSpanOrMemoryType` duplicated with divergent type comparison

**File:** `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Members.cs:175-187`

The local copy in `Members.cs` compares `ConstructedFrom.ToDisplayString()`, while the authoritative copy in `SymbolicTypeFacts.IsBuiltInSpanOrMemoryType` compares `OriginalDefinition.ToDisplayString()`. If Roslyn's `ToDisplayString()` formatting ever differs between `ConstructedFrom` and `OriginalDefinition`, the two copies will disagree on what constitutes a Span/Memory type, potentially causing `Members.cs` to miss length inference for spans.

---

## 63. `ContainsSymbolWrite` traverses lambda bodies causing false negative in Range/Index shape resolution

**File:** `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Indexing.cs:2110-2133`

`ContainsSymbolWrite` calls `node.DescendantNodes()`, which descends into lambda expression bodies. If a preceding statement contains a lambda that captures a `Range`/`Index` variable and writes to it, the method flags the variable as "written with unknown shape." Since the lambda has not necessarily executed yet at the use site, the shape resolver gives up unnecessarily — it could safely use a previously-resolved assignment shape.

---

## 64. `AssignmentPurityRule` computes delegate-target state that is never used

**File:** `SharpProof.Analyzer/Engine/Rules/AssignmentPurityRule.cs:258-270`

The delegate-assignment tracking block constructs `nextState` via `currentState.WithDelegateTarget(targetSymbol, valueTargets.Value)` and logs its map count, but `nextState` is never returned, stored, or used to influence any subsequent analysis. The state modification is lost. Delegate target tracking silently fails at call sites that depend on `DelegateTargetMap` to resolve delegate invocations.

---

## 65. `DelegateCreationPurityRule` returns Pure without checking target expression purity

**File:** `SharpProof.Analyzer/Engine/Rules/DelegateCreationPurityRule.cs:31-44`

When `IsEscapingDelegateCreation` returns false and `target` is an `IMethodReferenceOperation` with a non-null instance, only the *instance* purity is checked. But if `target` is *not* an `IMethodReferenceOperation` at all (e.g., it's an `IConversionOperation` wrapping a local reference to a previously-created delegate), the method falls through to `return Pure` without ever checking whether the target operation itself is pure.

---

## 66. Magic number `(RefKind)4` cast in FieldReferencePurityRule and PropertyReferencePurityRule

**Files:** `SharpProof.Analyzer/Engine/Rules/FieldReferencePurityRule.cs:176`, `SharpProof.Analyzer/Engine/Rules/PropertyReferencePurityRule.cs:325`

Both files cast to `(RefKind)4` with no named constant. The documented `RefKind` enum only has `None=0, Ref=1, Out=2, In=3`. Value 4 is an internal Roslyn enum value. If the Roslyn assembly renumbers or removes this value in a future version, the cast will silently produce a meaningless result, causing `in`/`ref readonly` parameters to be treated as mutable ref parameters.

---

## 67. `UsingStatementPurityRule` inconsistent Dispose missing-handling for expression vs. local

**File:** `SharpProof.Analyzer/Engine/Rules/UsingStatementPurityRule.cs:150-159,215-219`

When a `using` statement declares a local, a missing `Dispose` or `DisposeAsync` method returns **Impure**. But when the resource is an expression, a missing dispose method simply logs a debug message and returns **Pure**. This is a parity inconsistency that gives different results for semantically identical patterns.

---

## 68. `IsNullPurityRule`'s Binary operator handling may be dead code due to rule ordering

**Files:** `SharpProof.Analyzer/Engine/Rules/IsNullPurityRule.cs:15-16,34-60`, `SharpProof.Analyzer/Engine/Rules/RuleRegistry.cs:42,72`

`IsNullPurityRule` claims `OperationKind.Binary` in its `ApplicableOperationKinds`. `BinaryOperationPurityRule` also claims `OperationKind.Binary`. In `RuleRegistry.cs`, `BinaryOperationPurityRule` is registered before `IsNullPurityRule`. If the engine short-circuits on the first matching rule, `IsNullPurityRule`'s binary null-comparison handling would never execute — they'd already be consumed by `BinaryOperationPurityRule`.

---

## 69. `InterpolatedStringPurityRule` unreachable duplicate dynamic-dispatch check

**File:** `SharpProof.Analyzer/Engine/Rules/InterpolatedStringPurityRule.cs:184-197,269-282`

`CheckImplicitFormattingPurity` contains two identical code blocks checking non-sealed class virtual `ToString`. The second occurrence is unreachable because all paths through the intervening code either return a result or only get past the `IsFrameworkType` check (which returns Pure), making the second identical check dead code.

---

## 70. `LoopPurityRule` skips runtime enumerator member checks for interface/external enumerators

**File:** `SharpProof.Analyzer/Engine/Rules/LoopPurityRule.cs:206-210`

`CheckForEachEnumeratorRuntimeMemberPurity` returns `Pure` immediately when the enumerator type is an interface or has no declaring syntax references. This means `MoveNext`, `Current`, and `Dispose` are never checked for BCL-defined enumerators or interface-typed enumerators. If a third-party library's custom `IEnumerator<T>` implementation has an impure `MoveNext` (e.g., logging, I/O), it would be silently treated as pure.

---

## 71. `RemoveStateFactsReferencingImplicitThisMember` discards `memberName` — over-removes all `this` facts

**File:** `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:2528-2541`

`_ = memberName;` explicitly discards the member name parameter. The method then uses only `ImplicitThisVariableName` (`"this"`) to filter, meaning it removes ALL facts referencing `this` rather than only facts about the specific member. A non-SymbolicState counterpart correctly constructs `"this" + "." + memberName` to target only the specific member. When called after an assignment to `this.Field1`, it removes all facts about `this.Field2`, `this.Field3`, etc.

---

## 72. `TryCreateTopLevelGuardedBreakCondition` asymmetric fallback for single vs. multiple breaks

**File:** `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:7388-7438`

When there is exactly one `break` statement, the method tries three strategies in sequence. When there are 2+ breaks, only `TryCreateDirectGuardedBreakCondition` is used, and any failure causes the entire analysis to return `false`. A loop with two guarded breaks, where either break is inside a nested `if`, will silently lose all loop-exit condition analysis.

---

## 73. Switch exit exclusion negates section conditions with `includePatternBindings: false`

**File:** `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:6774-6785`

In `AddCompletedSwitchExitExclusionFacts`, `TryCreateSwitchStatementSectionCondition` is called with `includePatternBindings: false`. The resulting section condition is then negated. When a switch uses `case Pattern p:`, the section condition built without pattern bindings is a weaker formula than the full condition — the negation of a weaker formula is stronger than it should be. This could cause false-positive "definitely exits" analysis for pattern-based switch sections.

---

## 74. `CollectPriorAssignmentState` double-walks all containing blocks (performance)

**File:** `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:79-96`

`CollectPriorAssignmentState` calls `CollectPriorAssignmentStateNative` (which walks all containing blocks), then immediately calls `CollectPriorAssignmentFacts` (which walks all containing blocks again via its own `EnumerateContainingBlocks` call). No results from the first walk are reused. For deeply nested code, this doubles the AST traversal cost.

---

## 75. No CancellationToken checks in deep recursive formula substitution

**File:** `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:10685-10840`

`TrySubstituteFormula` is a recursive switch on formula types, called from `AddSubstitutedCurrentFacts` which iterates all facts. For each formula it descends into deeply-nested trees. None of these paths check `cancellationToken`. The cancellation token IS passed through the chain but only used for Roslyn semantic model queries — the pure formula-substitution traversal itself is unbounded.

---

## 76. `StatementsDefinitelyExits` default: misses `GotoStatementSyntax` and `LockStatementSyntax`

**File:** `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:8775-8793`

The switch expression handles `ReturnStatementSyntax`, `ThrowStatementSyntax`, `BreakStatementSyntax`, `ContinueStatementSyntax`, etc. The default case returns `false`. A `goto` statement inside a switch or loop is definitely an exit, but `GotoStatementSyntax` falls through to the default `false`. A `lock` statement that contains an unconditionally-throwing expression would also return `false` instead of evaluating its body.

---

## 77. `ClassifyInternalOnlyEffect` reports misleading `ImpurityFeasibility = Unsatisfiable`

**File:** `SearchLib/PurityProofSearch.cs:138-142`

The default (`_ =>`) branch sets `ImpurityFeasibility = Unsatisfiable` on a `ProvablyPure` result. But no impurity check was performed — only path satisfiability was tested. An internal-only effect is always "provably pure" by definition, so the outcome is correct, but the `ImpurityFeasibility` metadata falsely claims the impurity was *proven* unreachable. Downstream consumers that filter or display results based on `ImpurityFeasibility` would display a false claim. Should be `Feasibility.Unknown`.

---

## 78. Division/modulo with unknown divisor bounds aborts entire query before Z3

**File:** `SearchLib/SmtSolver.cs:1986-2001`

The final ternary returns `Unknown` regardless. This means **any** `SmtIntegerBinaryTerm` with `Divide` or `Remainder` and a divisor whose bounds do not provably exclude zero causes `PrepareConcreteFacts` to return `Unknown`, which aborts the entire query *without ever calling Z3*. Z3 could natively reason about divisions, so this pre-processing gate causes false Unknown results for a common class of queries.

---

## 79. `EnumerateConjuncts` / `NegateComparison` duplicated between SmtSyntacticClassifier and SmtSolver

**Files:** `SharpProof.Symbolic/Smt/SmtSyntacticClassifier.cs:109-127,144-274,358-381` and `SearchLib/SmtSolver.cs`, `CSharpConditionToFormula.Values.cs:187-245`

Both `SmtSyntacticClassifier` and `SmtSolver` independently decompose `And` formulas with separate implementations. `NegateComparison` / `ReverseComparison` in `SmtSyntacticClassifier.cs` are exact duplicates of `NegateComparison` / `SwapComparisonOperator` in `CSharpConditionToFormula.Values.cs` — separate names, identical logic. Maintaining duplicate logic across files creates a risk: a fix applied to one copy may not be applied to the other.

---

## 80. `TryEvaluateStringLengthComparison` double-switch risks getting out of sync

**File:** `SearchLib/SmtSolver.cs:2537-2558`

The two-switch pattern doubly encodes the same conditions (first switch sets `value`, second switch decides whether to use it). A future refactor might accidentally trust `value` on a `false` return from the second switch. If someone modifies one switch without the other, the return value and the `value` output would silently disagree.

---

## 81. `SmtAnalysisService.IsWithinFormulaNodeBudget` regex weight asymmetry

**File:** `SharpProof.Symbolic/Smt/SmtAnalysisService.cs:468-470`

The weight (`1 + Math.Max(1, regexMatch.Pattern.Length / 8)`) only applies to `Pop` but NOT to the `Push` traversal logic. The regex pattern is a constant string with no children pushed onto the stack — only `regexFormula.Value` is pushed. So the budget counts the regex node as `1 + patternLength/8` units but only actually traverses into `Value`. The budget calculation is approximate but the asymmetry means the check can be more permissive than intended.

---

## 82. `CSharpConditionToFormula.TryGetTypeOfType` misleading null-forgiving pattern

**File:** `SharpProof.Symbolic/Smt/CSharpConditionToFormula.cs:1199-1201`

```csharp
type = semanticModel.GetTypeInfo(typeOfExpression.Type, cancellationToken).Type!;
return type is { TypeKind: not TypeKind.Error };
```

The `!` operator suppresses the nullable warning on `Type`, immediately followed by a property pattern that implicitly handles null (returns `false`). No NRE exists, but the null-forgiving operator obscures intent. A contributor cleaning up `!` operators may erroneously add a null guard, breaking the pattern.

---

## 83. `SmtPathConditionMerger` O(n*m) key extraction on every merge (performance)

**File:** `SharpProof.Symbolic/Smt/SmtPathConditionMerger.cs:34-53`

`GetFormulaKey` calls `formula.ToString()`, which recursively formats the entire formula tree. The `IntersectWith` in a loop over `pathConditionSets` re-evaluates `GetFormulaKey` for every condition in every set, resulting in O(n*m) formula-to-string conversions per merge. For large path condition sets, this causes significant GC pressure.

---

## 84. `SmtAnalysisService.Classify` uses pre-normalization formulas for budget checks

**File:** `SharpProof.Symbolic/Smt/SmtAnalysisService.cs:96-126`

Budget checks use `query.PathConditions` (raw, pre-normalization) and `query.Hazard.TriggerCondition` (raw). The check runs before `NormalizePathConditions`. A path condition set with many `SmtBooleanConstant(true)` entries could exhaust the budget prematurely even though normalization would have removed them. Can cause premature `Unknown` for queries that would otherwise fit.

---

## 85. `SmtFormulaVersionRewriter` default case silently skips new formula types

**File:** `SharpProof.Symbolic/Smt/SmtFormulaVersionRewriter.cs:143`

The `default: return formula` on line 143 is a safety net — if a new `SmtFormula` subtype is added, version rewriting silently skips it, producing incorrect versioned variables. Unlike the existing bug #10 (general concern), this is the specific code location where the skip happens.

---

## 86. `SymbolicCli` never disposes `SmtAnalysisService`

**File:** `Tools/SharpProof.SymbolicCli/Program.cs:18-19`

The `SmtAnalysisService` is created at entry point top-level but never wrapped in `using` or disposed. It holds a native Z3 solver context (~100+ MB). If the main method exits via exception, the service is never disposed. In a long-running CLI usage (e.g., CI pipeline running many queries), this leaks native heap memory on every invocation.

---

## 87. `VsixHarness` never cleans up temp directory

**File:** `Tools/VsixHarness/Program.cs:44-46`

`Directory.CreateTempSubdirectory("SharpProofVsixHarness")` creates a temp directory, and the extracted `SharpProof.Analyzer.dll` is written there. Neither the directory nor its contents are ever deleted. On repeated runs (e.g., in CI), orphaned temp directories accumulate under `%TEMP%`.

---

## 88. `ConjoinPathConditions` NRE when first formula is null

**File:** `SharpProof.Symbolic/SymbolicInvariantService.cs:286-287`

`ConjoinPathConditions` assigns `var merged = pathConditions[0]` without null checking the element, then constructs `new SmtBinaryFormula(SmtBinaryOperator.And, merged, pathConditions[index])`. While `MergeEncodedStatePathConditions` (the primary caller) filters null entries, `ConjoinPathConditions` itself is internal-static and callable from anywhere. A null element in the list would NRE inside `SmtBinaryFormula`'s constructor.

---

## 89. `PurityClassificationEngine` silently skips unresolved non-interop external calls

**File:** `Tools/SharpProof.EffectSummary/PurityClassificationEngine.cs:421-431`

In `ClassifyMethod`, when `TryResolveCallSummary` fails AND `TryResolveExternalCallClassification` fails, the code falls through to `TryClassifyUnresolvedInteropBoundaryCall`. Regardless of whether interop classification succeeds, a bare `continue;` always skips the rest of the loop iteration. If the external call is NOT an interop boundary call (that helper returns `false`), the call is entirely ignored — no `conservativeCategories.Add("unknown_external_call")` happens. The method may be incorrectly classified as `pure` when it actually calls an unresolvable external method.

---

## 90. `SymbolicProofService` ConditionalWeakTable caches grow unboundedly

**File:** `SharpProof.Symbolic/SymbolicProofService.cs:24-26`

```csharp
private static readonly ConditionalWeakTable<SmtAnalysisService, ProofResultCache> s_serviceCaches = new();
private static readonly ProofResultCache s_fallbackCache = new();
```

`s_serviceCaches` maps each `SmtAnalysisService` instance to a `ProofResultCache`. The `ProofResultCache` is never expunged — same `ConcurrentDictionary` unbounded growth issue as `SmtAnalysisService`'s own shared query cache. `s_fallbackCache` is a second static `ProofResultCache` with the same unbounded growth risk.

---

## 91. `TryGetGlobalOption` silently swallows all non-cancellation exceptions

**File:** `SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs:505-522`

```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
}
```

Any exception from the Roslyn analyzer config provider is silently caught and the option defaults to `false`/empty. This hides genuine configuration bugs — a corrupted or misconfigured `.editorconfig` / `AnalyzerConfigOptionsProvider` would silently produce incorrect analyzer behavior with no diagnostic.

---

## 92. `SymbolicCapability` and `SharpProofCapability` are duplicate enums risking divergence

**Files:** `SharpProof.Symbolic/SymbolicCapabilityModels.cs:8-24` and `SharpProof.Attributes/SharpProofCapability.cs:6-22`

`SharpProof.Symbolic.SymbolicCapability` and `SharpProof.Attributes.SharpProofCapability` are identically-valued but different enum types. A flag added to one may not be added to the other, causing silent capability classification mismatches between the analyzer layer and the symbolic service layer.

---

## 93. `BuiltInEffectSummaryLoader` silent empty catalog if embedded resource is missing

**File:** `SharpProof.Analyzer/BuiltInEffectSummaryLoader.cs:69-73`

If `GetManifestResourceStream` returns null (resource not found), it's silently skipped. If the embedded summary JSON resource is accidentally removed or renamed during a build change, the built-in purity catalog becomes silently empty — all previously-known pure BCL methods become unclassified, producing false SP0002 diagnostics across every user project. No warning or error is emitted.

---

**Total: 93 documented potential bugs (initial 48 + 45 from six-agent round 2).**
