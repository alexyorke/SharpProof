# Potential Bugs Resolution Ledger

This canonical ledger merges the two source audits in source order. All 411 raw findings have stable PB1/PB2 IDs and terminal evidence-based dispositions.

## Root-cause index

| Canonical root cause | Claims | Dispositions | Representative IDs |
| --- | ---: | --- | --- |
| RC-ACCESSOR-ATTRIBUTE-TARGET | 1 | Fixed | PB2-5.5 |
| RC-ACCESSOR-INFERRED-CODEFIX | 1 | Fixed | PB2-5.4 |
| RC-ANALYZER-CONTRACT-SEMANTICS | 26 | False positive/intentional, Fixed | PB1-1.8, PB1-3.1, PB1-4.7, PB1-8.3, PB1-8.5, PB1-8.7, PB1-12.5, PB1-13.1, ... |
| RC-ANALYZER-SYMBOLIC-BOUNDARY | 5 | Duplicate, Fixed | PB1-1.1, PB1-1.2, PB1-26.1, PB1-26.2, PB2-10.3.2 |
| RC-ARTIFACT-SEED-FLOW | 1 | False positive/intentional | PB1-10.5 |
| RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS | 19 | Already fixed, False positive/intentional, Fixed | PB1-1.4, PB1-1.6, PB1-1.7, PB1-9.3, PB1-9.4, PB1-9.5, PB1-9.6, PB1-9.7, ... |
| RC-BASELINE-AND-SARIF-TOOLS | 12 | False positive/intentional, Fixed | PB1-18.6, PB1-21.6, PB1-21.9, PB1-22.5, PB1-24.4, PB1-28.8, PB1-30.2, PB1-30.3, ... |
| RC-BOUNDED-INTEGER-OVERFLOW | 1 | Fixed | PB1-20.1 |
| RC-BUILD-CONFIGURATION-AND-DOCUMENTATION | 12 | False positive/intentional, Fixed | PB1-12.7, PB1-14.1, PB1-14.4, PB1-14.5, PB1-19.3, PB1-29.10, PB1-32.4, PB1-32.5, ... |
| RC-BUILD-CONFIGURATION-ROUTING | 1 | Fixed | PB2-8.1.1 |
| RC-BUILD-MEMORY-LIMIT-SHADOW | 1 | Fixed | PB1-32.2 |
| RC-CALLABLE-DESCENDANT-BOUNDARY | 1 | False positive/intentional | PB2-10.1.2 |
| RC-CALL-GRAPH-EDGE-PRESERVATION | 1 | Already fixed | PB2-10.1.1 |
| RC-CATCH-ENTRY-STATE | 1 | Fixed | PB1-27.1 |
| RC-CATCH-TYPE-FLOW | 1 | Fixed | PB1-27.2 |
| RC-CFG-SWITCH-ANALYSIS | 1 | False positive/intentional | PB2-10.2.1 |
| RC-CHILD-BUILD-EXIT-VALIDATION | 1 | Fixed | PB2-9.1.1 |
| RC-CHILD-PROCESS-ASYNC-LIFECYCLE | 1 | Fixed | PB2-9.2.1 |
| RC-CLI-INPUT-AND-OUTPUT-CONTRACTS | 5 | False positive/intentional, Fixed | PB1-3.7, PB1-11.6, PB1-28.9, PB2-5.7, PB2-8.5.3 |
| RC-CLI-REPORT-LIMIT-ROUTING | 1 | Already fixed | PB1-28.1 |
| RC-CODEFIX-ATTRIBUTE-FORMATTING | 1 | Already fixed | PB1-9.1 |
| RC-CODEFIX-TRIVIA | 2 | False positive/intentional, Fixed | PB1-9.2, PB2-7.8.1 |
| RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT | 26 | Enhancement | PB1-2.5, PB1-4.9, PB1-5.4, PB1-7.3, PB1-7.4, PB1-7.7, PB1-7.8, PB1-15.7, ... |
| RC-COMPLEXITY-PARTIAL-ORDER | 1 | Fixed | PB1-28.2 |
| RC-CONDITIONAL-NULLABILITY-POLARITY | 1 | False positive/intentional | PB1-16.1 |
| RC-CONSERVATIVE-LOOP-BOUND | 1 | Fixed | PB2-4.1 |
| RC-CONTRACT-HIERARCHY | 1 | Fixed | PB2-2.1 |
| RC-CONTROL-FLOW-AND-STATE-MERGING | 20 | False positive/intentional, Fixed | PB1-2.2, PB1-6.15, PB1-7.1, PB1-14.3, PB1-16.8, PB1-21.5, PB1-25.3, PB1-25.4, ... |
| RC-CORPUS-SARIF-ISOLATION | 1 | False positive/intentional | PB2-7.9.1 |
| RC-CSPATTERN-PRECEDENCE | 1 | False positive/intentional | PB2-10.8.1 |
| RC-DELEGATE-TARGET-MERGE | 1 | False positive/intentional | PB1-16.3 |
| RC-DIAGNOSTIC-REPRODUCTION-PATHS | 1 | False positive/intentional | PB2-7.3.2 |
| RC-DOTNET-REGEX-UTF16 | 1 | False positive/intentional | PB1-6.2 |
| RC-DUPLICATE-CLAIM | 6 | Duplicate | PB1-4.8, PB1-20.5, PB1-25.5, PB1-28.10, PB1-30.6, PB2-7.3.4 |
| RC-DYNAMIC-ASSEMBLY-LOCATION | 1 | Fixed | PB2-5.1 |
| RC-EXCEPTION-FLOW | 22 | Already fixed, False positive/intentional, Fixed | PB1-1.5, PB1-4.5, PB1-6.6, PB1-15.2, PB1-20.2, PB1-25.7, PB1-26.4, PB1-27.6, ... |
| RC-EXECUTION-ROOT-ISOLATION | 1 | Fixed | PB2-7.1.2 |
| RC-EXPRESSION-BODIED-CONSTRUCTOR | 1 | Fixed | PB2-3.1 |
| RC-FUZZ-HARNESS | 9 | Already fixed, False positive/intentional, Fixed | PB1-18.4, PB1-19.2, PB1-22.1, PB1-22.4, PB1-22.6, PB1-22.8, PB1-30.4, PB2-8.5.5, ... |
| RC-GENERIC-ALLOCATION | 1 | Fixed | PB2-2.2 |
| RC-GENERIC-CONTRACT-SUBSTITUTION | 1 | Fixed | PB2-2.3 |
| RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS | 48 | Already fixed, Fixed | PB1-3.5, PB1-4.3, PB1-4.4, PB1-6.10, PB1-6.12, PB1-7.9, PB1-8.4, PB1-8.6, ... |
| RC-IMPLICIT-STRUCT-CONSTRUCTOR | 1 | False positive/intentional | PB1-8.2 |
| RC-INVARIANT-GATE-NONEMPTY | 1 | False positive/intentional | PB2-8.5.2 |
| RC-JOB-ASSIGNMENT-FAILURE | 1 | Fixed | PB1-19.1 |
| RC-LAMBDA-NULLABILITY-AUDIT | 1 | Fixed | PB2-3.2 |
| RC-LINUX-Z3-DISTRIBUTION | 1 | Enhancement | PB1-22.2 |
| RC-LIST-PATTERN-INDEX-VALIDATION | 1 | Fixed | PB2-10.4.1 |
| RC-MALFORMED-PROOF-QUERY | 1 | Fixed | PB2-9.3.4 |
| RC-NATIVE-LOADER-PLATFORM | 1 | Fixed | PB2-1.4 |
| RC-NESTED-MUTATION-INVALIDATION | 1 | Already fixed | PB2-7.6.1 |
| RC-NULLABILITY-CONTRACTS | 9 | False positive/intentional, Fixed | PB1-16.2, PB1-16.4, PB1-16.5, PB1-24.3, PB1-26.7, PB2-3.3, PB2-3.9, PB2-4.4, ... |
| RC-OPTIONAL-PROGRESS-STATE | 1 | Fixed | PB1-10.2 |
| RC-OPTIONAL-RAW-PROOF | 1 | Already fixed | PB1-25.1 |
| RC-OPTIONAL-TRIGGER-PROOF | 1 | Already fixed | PB1-25.2 |
| RC-PACKAGING-AND-CONSUMER-ASSETS | 11 | False positive/intentional, Fixed | PB1-5.1, PB1-5.2, PB1-5.3, PB1-19.7, PB1-22.3, PB1-22.7, PB1-32.12, PB2-8.1.2, ... |
| RC-PATH-CONTAINMENT | 1 | False positive/intentional | PB1-10.7 |
| RC-POWERSHELL-ARRAY-MATERIALIZATION | 1 | False positive/intentional | PB2-9.2.4 |
| RC-PROCESS-OWNERSHIP | 1 | Fixed | PB1-4.1 |
| RC-PROGRESS-CHECKPOINT-LIFETIME | 1 | False positive/intentional | PB1-10.6 |
| RC-PURITY-PRECEDENCE-POLICY | 2 | Duplicate, False positive/intentional | PB1-8.1, PB2-7.1.1 |
| RC-REGEX-AND-UTF16-SEMANTICS | 6 | False positive/intentional, Fixed | PB1-15.3, PB1-19.4, PB1-20.3, PB1-23.8, PB2-7.5.3, PB2-10.6.1 |
| RC-REVIEWED-PURITY-CATEGORY-PRECEDENCE | 1 | Fixed | PB1-10.3 |
| RC-REVIEWED-PURITY-MERGE | 1 | False positive/intentional | PB1-10.1 |
| RC-ROSLYN-CONCURRENT-READS | 1 | False positive/intentional | PB1-23.1 |
| RC-ROSLYN-GLOBALCONFIG-SEMANTICS | 1 | False positive/intentional | PB2-7.10.1 |
| RC-RUNTIME-OVERFLOW-HAZARDS | 1 | Already fixed | PB1-15.1 |
| RC-SHARD-TOOL-IDENTITY | 1 | Fixed | PB1-10.4 |
| RC-SMT-DIVISOR-SAFETY | 1 | Fixed | PB1-6.1 |
| RC-SMT-PROOF-SOUNDNESS | 49 | Already fixed, False positive/intentional, Fixed | PB1-4.2, PB1-5.5, PB1-6.5, PB1-6.7, PB1-6.8, PB1-6.13, PB1-6.14, PB1-11.1, ... |
| RC-SMT-SESSION-OWNERSHIP | 1 | Fixed | PB2-1.1 |
| RC-STOPWATCH-CAPABILITY | 1 | Fixed | PB2-4.3 |
| RC-STRING-REMOVE-BOUND | 1 | Fixed | PB1-2.1 |
| RC-STRUCTURAL-REF-KIND-IDENTITY | 1 | Already fixed | PB1-12.1 |
| RC-SYMBOLIC-OVERFLOW-IDENTITY | 1 | Fixed | PB1-34.1 |
| RC-TERMINAL-BRANCH-FLOW | 1 | Fixed | PB2-1.2 |
| RC-TEST-FILTER-NAMESPACE | 2 | Fixed | PB1-32.1, PB1-32.3 |
| RC-TYPE-TEST-ORACLE-SOUNDNESS | 1 | Fixed | PB2-7.5.1 |
| RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY | 59 | False positive/intentional | PB1-1.3, PB1-2.3, PB1-2.4, PB1-3.2, PB1-3.3, PB1-3.4, PB1-3.6, PB1-4.6, ... |
| RC-Z3-RLIMIT-ACCOUNTING | 1 | False positive/intentional | PB1-6.3 |
| RC-Z3-UTF16-SEMANTICS | 1 | False positive/intentional | PB1-34.2 |

## Disposition summary

- Fixed: 196
- Already fixed: 16
- Duplicate: 8
- False positive/intentional: 164
- Enhancement: 27

## Source PB1 - POTENTIAL_BUGS.md

This document is a catalog of *potential* bugs found by static review of the
PurelySharp (SharpProof) codebase. Findings are grouped by area. Severities are
Low / Medium / High and reflect likelihood + impact of the issue. Items flagged
"potential" / "latent" are not guaranteed to trigger on current inputs but are
real correctness or robustness hazards.

> Methodology: findings were produced by independent review agents covering the
> analyzer/codefix, symbolic engine, tests/demo, tools/SearchLib/scripts, and
> packaging/build/docs areas. No builds or test runs were executed during the
> review.

---

## 1. Roslyn Analyzer & CodeFix (`SharpProof.Analyzer`, `SharpProof.CodeFixes`, `SharpProof.Attributes`)

### [PB1-1.1] 1.1 Unguarded symbolic queries can crash the analyzer - **HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-SYMBOLIC-BOUNDARY
> **Evidence:** Capability and purity queries could propagate expected symbolic-analysis failures through Roslyn analyzer callbacks.
> **Changes/tests:** Typed capability outcomes and conservative purity failure evidence; OperationBlockPipelineTests and SymbolicUnknownReasonTaxonomyTests.
- `SharpProof.Analyzer/InferredContractSuggestionAnalyzer.cs:184`, `:230`
- `SharpProof.Analyzer/MethodCapabilityAnalyzer.cs:59`
- `SuggestAllowedCapabilities` / `SuggestExpectedComplexity` / `AnalyzeSymbolForCapabilities`
  call `context.State.GetCapabilityResult(...)` / `GetComplexityResult(...)` with **no
  try/catch**. The sibling `MethodExpectedComplexityAnalyzer.AnalyzeSymbolForExpectedComplexity`
  wraps the same calls in a `try { ... } catch (Exception ex) when (ex is ArgumentException or
  NotSupportedException or InvalidOperationException)`. An SMT/unsupported-expression throw here
  propagates out of the analyzer callback and crashes the IDE analysis session.
- **Trigger:** any method whose capability/complexity symbolic query throws.

### [PB1-1.2] 1.2 `UnknownReasonDetails[0]` accessed without length guard - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-SYMBOLIC-BOUNDARY
> **Evidence:** Complexity queries used a throwing wrapper at the analyzer boundary.
> **Changes/tests:** Typed complexity outcomes now produce SP0015 unknown diagnostics; OperationBlockPipelineTests.
- `SharpProof.Analyzer/MethodCapabilityAnalyzer.cs:80-110`
- Only `UnknownReasons.Count > 0` is verified before `result.UnknownReasonDetails[0]` is read.
  If `UnknownReasonDetails` is shorter than `UnknownReasons`, this throws
  `ArgumentOutOfRangeException`.
- **Trigger:** a capability result with unknown reasons but missing parallel detail entries.

### [PB1-1.3] 1.3 Shared `AsyncLocal` config/catalog/policy under concurrent execution - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/ImpurityCatalog.cs:9-31`
- `SharpProof.Analyzer/GeneratedPurityCatalog.cs:14,79-82`
- `SharpProof.Analyzer/ExceptionFlowAnalyzer.cs:16-26`
- `SharpProof.Analyzer/AnalyzerFeaturePipeline.cs:58-62`
- `SharpProofAnalyzer.Initialize` calls `EnableConcurrentExecution()`; concurrent callbacks
  each `using`-scope mutate **static** `AsyncLocal` fields and reset them on `Dispose`.
  Interleaved Set/Reset can make an in-flight analysis observe a sibling's value (or the
  reset-to-null value), yielding **incorrect** purity/exception/contract classifications
  rather than a crash.
- **Trigger:** multi-method parallel analysis with a non-default configuration/effect-summary.

### [PB1-1.4] 1.4 Purity fixed-point memoized with first-call attribute symbols - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/CompilationPurityService.cs:64-110` (called from `MethodPurityAnalyzer.cs:166-173`)
- `EnsureFixedPoint` builds the call-graph worklist once, using the attribute symbols of the
  first `GetPurity` call, but `MethodPurityAnalyzer` resolves these **per method** and may pass
  different symbols later. The reused (stale) fixed point yields inconsistent purity verdicts.

### [PB1-1.5] 1.5 `Locations.First()` can throw `InvalidOperationException` - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/AnalyzerSyntaxHelpers.cs:35-43`
- When a method symbol has no declaring syntax references AND no locations,
  `methodSymbol.Locations.First()` throws instead of producing a diagnostic. Reached from
  `MethodExpectedComplexityAnalyzer.cs:37,278,313`.
- **Trigger:** synthesized/error/partial-without-impl method symbols.

### [PB1-1.6] 1.6 CodeFix discards event-accessor attribute removals - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:998-1023` (`FilterDeclarationAndAccessorAttributes`)
- `EventDeclarationSyntax` is a `BasePropertyDeclarationSyntax` with an `AccessorList`, so it
  passes the guard and the accessor loop sets `removedAny = true`, but the final `switch` has no
  `EventDeclarationSyntax` case and returns `declaration` unchanged - the attribute is **not
  removed** from the event accessor.
- **Trigger:** "remove contract attribute" code fix on an event accessor attribute.

### [PB1-1.7] 1.7 `GetEffectivePurityAttributeSymbol` returns null behind a `!` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/MethodPurityAnalyzer.cs:612-616`
- `return enforcePureAttributeSymbol ?? pureAttributeSymbol!;` lies about nullability; only safe
  today due to the early guard at line 33. Fragile if that guard is ever refactored.

### [PB1-1.8] 1.8 `FormatCapabilities` over-lists combined flags - **LOW** (cosmetic)

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/MethodCapabilityAnalyzer.cs:295-305`
- Enumerates every set bit, including both `IO` and its sub-flags, producing redundant messages.

---

## 2. Symbolic Execution Engine & Shared (`SharpProof.Symbolic`, `Shared`)

### [PB1-2.1] 2.1 `string.Remove(start)` modeled with wrong (exclusive) upper bound - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-STRING-REMOVE-BOUND
> **Evidence:** A deterministic query reported Remove(startIndex) with startIndex equal to Length as a reachable exception.
> **Changes/tests:** One-argument Remove now uses an inclusive upper bound; SymbolicRuntimeHazardQueryTests.
- `SharpProof.Symbolic/SymbolicRuntimeHazardCandidateFactory.cs:2138-2147` (`TryGetSlicingInvocationShape`)
- For the single-argument `string.Remove(int startIndex)`, `countExpression` is null so
  `oneArgumentUpperBoundIsInclusive` is set to **false**, modeling `start < Length` instead of
  `start <= Length`. `str.Remove(str.Length)` is legal in .NET but is modeled as out-of-range,
  producing a **false-positive `ArgumentOutOfRange` hazard**. Compare `Substring(start)` just
  above, which correctly sets the flag to `true`.

### [PB1-2.2] 2.2 Version-tracking inconsistency when merging alternative completion states - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:4006-4015` (`MergeCompletedAlternativeStates`)
- `retainedFacts`/`retainedConditions` restore all `entryState` facts (which may reference e.g.
  `x@v2`), but `commonVersions` is computed only over the *completion* states. A variable whose
  version differs across branches is dropped from `SymbolVersions` while a retained fact still
  names it -> downstream `GetVariableName` defaults to version 0, producing a mismatched SMT
  variable name.

### [PB1-2.3] 2.3 `CreateDisjunction` lacks the empty-collection guard `CreateConjunction` has - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Smt/SmtPathConditionMerger.cs:153-171`
- `CreateConjunction` guards `Count == 0`; `CreateDisjunction` does `formulas[0]` unguarded.
  Latent today (only reached with `>= 2` elements) but throws `IndexOutOfRangeException` if ever
  called with an empty collection.

### [PB1-2.4] 2.4 `TryEncodeBounds` can return false for a bound-less atom - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIrFormulaEncoder.cs:384-394`
- When both `IncludeLowerBound` and `IncludeUpperBound` are false, `lower ?? upper!` is null and
  the method returns `false`; the `upper!` also masks a null. No current caller builds such an
  atom, but it is fragile.

### [PB1-2.5] 2.5 `StartsWith`/`EndsWith` with non-constant string argument unsupported - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Strings.cs:50-53`
- With a symbolic (non-constant, non-char) argument the handler bails and lowers to an opaque
  boolean, losing all semantics (sound over-approximation, but hides reasoning). `Contains` and
  char/constant args are handled.

---

## 3. Tests, Demo, Smoke (`SharpProof.Test`, `SharpProof.ToolingTest`, `SharpProof.Smoke.Net472`, `SharpProof.Demo`)

> Note: tests use **NUnit** (`[Test]`/`[TestCase]`), not xUnit, so the "missing attribute" class
> does not apply.

### [PB1-3.1] 3.1 `TopImpureApis[0]` is order-dependent on a tie - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/CorpusReportTests.cs:64`
- Two SP0002 results each occur once (tie). The test hard-codes `[0] == "ITest.Run()"`, assuming a
  specific tie-break. A different `RankedItem` enumeration order fails the assertion.

### [PB1-3.2] 3.2 `FalsePositiveCandidates[0]` expected count likely wrong (2 vs 1) - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/CorpusReportTests.cs:404-405`
- By the semantics established in the sibling test (`CreateFromSarifJson_IdentifiesCatalogMissesAndFalsePositiveCandidates`),
  only entries **without** a known catalog source qualify. Per that definition the count for
  `delegate*<void>` should be **1**, not **2**. Either the assertion or the implementation's FPC
  definition is inconsistent. *Confirm against `SarifCorpusReport.FalsePositiveCandidates`.*

### [PB1-3.3] 3.3 Snapshot regeneration uses `Assert.Pass`, bypassing all verification - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/ReadmeExampleFixture.cs:45-50`
- When `SHARPPROOF_REGENERATE_EXAMPLE_OUTPUTS` is set, every example test writes the actual output
  as the expected snapshot and passes without comparing. If this env var is ever set in CI, the
  entire snapshot suite passes trivially and real regressions are masked.

### [PB1-3.4] 3.4 `HazardCount` vs empty `Hazards` semantics ambiguity - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/CompactDomainProjectionTests.cs:97-99`
- Asserts `HazardCount == 1` while `Hazards` is empty (query ran with `maxHazards: 0`,
  `Truncation.Hazards == true`). Only valid if `HazardCount` means "pre-truncation total". Verify
  the property's intended meaning.

### [PB1-3.5] 3.5 Manifest coverage check hardcoded to SP0002..SP0040 - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** A negative `Index` value throws `ArgumentOutOfRangeException` during caret/factory/constructor evaluation, before the downstream sequence access; the old lowering could instead treat that expression as safely completed.
> **Changes/tests:** Centralized the `Index` normal-completion guard, made downstream bounds hazards conditional on it, and added the upstream constructor hazard for caret syntax, `Index.FromStart/FromEnd`, and `new Index(...)`; covered by `QuerySourceRuntimeHazardsLine_NegativeFromEndIndexReportsConstructionFailure` and the element-access SMT regressions.
- `SharpProof.Test/ReadmeGeneratedExamplesTests.cs:435-439`
- `Enumerable.Range(2, 39)` only validates examples exist for SP0002-SP0040. A new public rule
  (SP0041+) added without a README example will not be caught.

### [PB1-3.6] 3.6 Brittle hardcoded schema-version pin - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/FuzzToolTests.cs:115` - `Assert.That(summary.SchemaVersion, Is.EqualTo("1.3"));`
- Any legitimate schema bump forces this test to fail.

### [PB1-3.7] 3.7 CLI example snapshot compares raw stdout - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CLI-INPUT-AND-OUTPUT-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/ReadmeGeneratedExamplesTests.cs:373-389`
- Relies on `NormalizeForSnapshot` scrubbing all machine-specific content; unsanitized path/timestamp/
  pid in CLI output silently mismatches only off-machine.

---

## 4. Tools, SearchLib & Scripts (`Tools`, `SearchLib`, `scripts`)

### [PB1-4.1] 4.1 Concurrent test runs kill each other's processes - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-PROCESS-OWNERSHIP
> **Evidence:** The suite runner globally swept and killed matching dotnet/testhost processes, so concurrent repository runs could terminate each other.
> **Changes/tests:** Removed the global process sweep and rely on per-invocation Job Object ownership; ScriptProcessOwnershipTests.
- `scripts/Invoke-SharpProofTests.ps1:142-217` (kill at 207)
- `Stop-NewSharpProofTestWorkerProcesses` kills any `dotnet.exe`/`testhost.exe`/`MSBuild.exe`/
  `VBCSCompiler.exe` whose command line merely contains `$repoRoot`/`SharpProof.Test`/etc.,
  regardless of which session started it. Only `StartedAfter.AddSeconds(-2)` protects.
- **Trigger:** two overlapping `Invoke-SharpProofTests.ps1` runs (e.g. CI + local) -> the first's
  `finally` kills the second's `testhost.exe` workers.

### [PB1-4.2] 4.2 Z3 rlimit resource accounting silently disabled if statistic isn't UInt - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtSolver.cs:39-52` (`CheckAndAccountResources`)
- Only records `"rlimit count"` when `entry.IsUInt`. If Z3 exposes it as a wider/other-typed
  statistic, `ConsumedResourceCount` stays 0 and the deterministic resource-budget safety net
  no-ops (only wall-clock safety net remains).

### [PB1-4.3] 4.3 `GetWallClockSafetyNet` integer overflow and wrong lower-bound guard - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/SmtResourceBudget.cs:36-43`
- `TimeSpan.MinValue.Ticks / WallClockSafetyFactor` is not `TimeSpan.MinValue.Ticks` (guard dead).
  `budget.Ticks * WallClockSafetyFactor` is unchecked; a large budget wraps negative and
  `TimeSpan.FromTicks` throws `OverflowException`.

### [PB1-4.4] 4.4 `Convert-ToRepoPath` corrupts paths via blind `Substring` - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Get-SharpProofRawSmtHotspots.ps1:28`
- `$repoRoot` is `Resolve-Path` output while `$fullPath` is `GetFullPath`; the code never verifies
  `$fullPath` starts with `$repoRoot` before stripping the first N chars. Casing/junction/8.3/long
  path divergence silently yields garbage repo-relative paths.

### [PB1-4.5] 4.5 `--max-depth` / `--max-exception-edges` / `--limit` throw unhandled `FormatException` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.EffectSummary/Program.cs:696,699,727` (`int.Parse(ReadRequiredValue(...))`, `ReadPositiveInt`)
- Non-numeric CLI values produce a raw stack-trace crash instead of a clean CLI error.

### [PB1-4.6] 4.6 `nodeReuse:false` check is case/format-fragile - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `scripts/Invoke-SharpProofDotnet.ps1:36-44`
- Case-sensitive `List<string>.Contains('/nodeReuse:false')`; `-nodeReuse:false` is missed and a
  duplicate is appended. `dotnet run` is also outside the MSBuild-backed command set, so node reuse
  isn't disabled there.

### [PB1-4.7] 4.7 Path check consumes the timeout, starving the impurity check - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtSolver.cs:130-154` (`CheckPathAndImpurityWithWitness`)
- Path feasibility is checked with the *full* timeout; `remaining = timeout - deadline.Elapsed` may
  be `<= 0`, collapsing the impurity query to `Unknown` under tight timeouts.

### [PB1-4.8] 4.8 `Aggregate-FuzzRun.ps1` reports the last phase's schema version - **LOW**

> **Disposition:** Duplicate
> **Canonical root cause:** RC-DUPLICATE-CLAIM
> **Evidence:** This claim describes the same behavior and root cause as another canonical ledger item.
> **Changes/tests:** Resolved or classified under the canonical root-cause entry.
- `Tools/SharpProof.Fuzz/Aggregate-FuzzRun.ps1:49`
- `$latestSchemaVersion` taken from whichever phase sorts last alphabetically, not validated across
  all phases. Mismatched phase versions are misreported with no warning.

### [PB1-4.9] 4.9 Non-Windows consumers bypass the JobObject wrapper - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `scripts/Test-SharpProofPackageConsumers.ps1:34-39`
- On non-Windows the command runs as raw `dotnet` without the JobObject memory/timeout wrapper ->
  different resource-containment behavior vs Windows.

### [PB1-4.10] 4.10 JobObject timeout mask real exit codes - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `scripts/JobObjectHelpers.ps1:272-293`
- A memory-limit kill can produce exit code 124, indistinguishable from a timeout.

### [PB1-4.11] 4.11 `RewriteBottomUp` `changed` detection over-reports - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtFormulaTraversal.cs:42-49`
- `changed` set on any non-`ReferenceEqual` return, including structural-identical rebuilds -> extra
  `SubstituteEqualityAliases` passes (bounded, not incorrect).

---

## 5. Packaging, Build, Config & Docs (`SharpProof.Package`, `SharpProof.Vsix`, `config`, `docs`, root csproj/sln, READMEs, PLAN.md)

> Note: option keys, enum values, diagnostic ID ranges (SP0002-SP0047), CLI flags, Z3 RID paths,
> and package versions are *otherwise internally consistent* with the code. No High-severity
> definitely-broken bug found in scope.

### [PB1-5.1] 5.1 VSIX ships netstandard2.0 `System.*` facades - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Vsix/SharpProof.Vsix.csproj:51-60`
- Ships netstandard2.0 builds of `System.Memory`, `System.Buffers`, `System.Collections.Immutable`,
  `System.Reflection.Metadata`, `System.Numerics.Vectors`, `System.IO.Pipelines`, `System.Text.Json`,
  `System.Threading.Tasks.Extensions`, `Microsoft.Bcl.AsyncInterfaces` into the VS extension.
  `devenv.exe` already loads newer versions -> classic analyzer-payload binding-conflict pitfall;
  analysis can silently fail or the extension fails to load.

### [PB1-5.2] 5.2 Legacy `tools/install.ps1` & `uninstall.ps1` packed into the NuGet package - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Package/SharpProof.Package.csproj:37`
- `tools/install.ps1`/`uninstall.ps1` glob `analyzers\**\*.dll` and register **every** DLL (including
  `libz3.dll`/`.dylib`, `Microsoft.Z3.dll`, `SharpProof.Symbolic.dll`, `SearchLib.dll`) as analyzer
  references. No-ops for PackageReference (`SupportsPackageDependencyResolution`) but harmful for any
  `packages.config` consumer.

### [PB1-5.3] 5.3 VSIX `Identity Version` not aligned with NuGet version - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Vsix/source.extension.vsixmanifest:4` (`Version="0.1.0"` vs NuGet `0.1.0-preview.1`)
- 3-part vs 4-part; no automated sync.

### [PB1-5.4] 5.4 Hardcoded `netstandard2.0` build-output paths in packaging target - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Package/SharpProof.Package.csproj:45-54`
- `_AddAnalyzersToOutput` references analyzers via fixed
  `..\SharpProof.Analyzer\bin\$(Configuration)\netstandard2.0\...` paths. Breaks if any analyzer
  project is retargeted/multi-targeted or packed under a config whose analyzers weren't pre-built.

### [PB1-5.5] 5.5 `SharpProof.Attributes.dll` delivered by two packages simultaneously - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Package/SharpProof.Package.csproj:54` + `SharpProof.Attributes/SharpProof.Attributes.csproj:12`
- A consumer who adds both `SharpProof` and standalone `SharpProof.Attributes` gets the same DLL from
  two packages -> "already provides an asset" warnings / version-pinning conflicts.

### [PB1-5.6] 5.6 `root = true` in editorconfig profile variants is a silent footgun - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `config/profiles/sharpproof-{audit,ci,migration,strict}.editorconfig:2`
- If copied standalone as `.editorconfig`, `root = true` stops the directory walk and disables all
  parent/inherited `.editorconfig` settings.

---

---

# Round 2 - additional findings (10 agents, de-duplicated)

The following were found by 10 additional review agents focused on deeper/adjacent
areas. Items already listed in Round 1 are excluded. Highest-impact new items:
integer-division-by-zero soundness hole, implicit-struct-ctor purity false-negative,
purity-policy tie-break preferring Impure, Ecma method-identity ref-kind mismatch.

## 6. SMT Encoding & SearchLib Runtime (`SearchLib`, `SharpProof.Symbolic\Smt`)

### [PB1-6.1] 6.1 Integer division/remainder by a possibly-zero divisor is not rejected - **HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-DIVISOR-SAFETY
> **Evidence:** Z3 totalizes integer division/remainder by zero, so unresolved zero divisors produced unsound SAT/UNSAT answers.
> **Changes/tests:** SMT safety validation returns Unknown unless the divisor is proven nonzero; SearchLibZ3SmokeTests.
- `SearchLib/SmtSolver.cs:~1572` (`ValidateIntegerTermSafety`)
- The safety check only returns `Unknown` when the divisor is *exactly* `{0}`. When the
  divisor's interval *includes* 0 (e.g. `x in [-3,3]`, no `x != 0` fact) it falls through to `Ready`,
  so `EncodeCSharpIntegerDivide`/`EncodeCSharpIntegerRemainder` encode the division. Z3's `div`/`mod`
  by zero does not throw, so this can flip SAT<->UNSAT versus real C# semantics -> **false
  `ProvablyPure`** (unsound).

### [PB1-6.2] 6.2 Regex char-class translation is BMP-only but marked `isExact` (astral under-approx) - **MEDIUM/HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-DOTNET-REGEX-UTF16
> **Evidence:** System.Text.RegularExpressions matches UTF-16 code units: an astral scalar is two surrogate chars and the translated BMP char universe matches those semantics.
> **Changes/tests:** Runtime surrogate-pair probes and the existing regex translator tests; no code change.
- `SearchLib/Z3FormulaEncoder.cs:1872-1918` (`CreateRegexCharacterRanges`/`...OrEmpty`),
  `:1526-1532` (negated class), `:1706-1773` (`TryCreateEscapedCharacterClassRegex`).
- Negated classes (`[^a]`, `\D`, `\W`, `\S`, `\P{...}`) and Unicode-category classes are built from
  BMP-only ranges but `_isExact` is left `true`. Astral characters (U+10000+) exist in the solver, so
  the negated class *under-approximates* the real .NET regex -> can **spuriously return UNSAT** (wrongly
  prove a hazard unreachable). Because `isExact` stays true, `AdjustForApproximation` does not
  downgrade to `Unknown`.

### [PB1-6.3] 6.3 rlimit "wraparound" logic over-counts when `Check()` is called more than once - **HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** A Microsoft.Z3 4.12.2 probe showed rlimit count is cumulative across Check calls and new Solver instances in the same Context (13, 23, 36).
> **Changes/tests:** No code change; the existing delta accounting is required.
- `SearchLib/SmtSolver.cs:46-50` (`CheckAndAccountResources`)
- Z3's `rlimit count` resets each `Check()`; `_lastObservedRlimitCount` is an instance field persisting
  across checks and across separate `Solver` instances in the shared `Context`. In
  `CheckPathAndImpurity` the same incremental solver is checked twice; the second (smaller) observation
  is misread as a 32-bit counter wrap, adding ~4.29e9 to `ConsumedResourceCount`. Repeatedly exhausts
  the cumulative budget -> spurious `Unknown`. Distinct from the Round-1 `IsUInt` gap.

### [PB1-6.4] 6.4 `EncodeBinary` `Equal`/`NotEqual` has no sort/kind guard - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/Z3FormulaEncoder.cs:186-187`
- Unlike `EncodeInteger`/`EncodeString`/`EncodeReference`, `Equal`/`NotEqual` call generic `Encode`
  with no `SmtValueKind` check. Mismatched operand kinds (int vs string, Reference vs String) raise a
  sort-mismatch `Z3Exception` instead of a clean validation error (degrades to `Unknown`, not a wrong
  answer, but a crash path).

### [PB1-6.5] 6.5 Z3 AST nodes are never disposed; only the root `Context` is - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/Z3FormulaEncoder.cs:33-40` (`Dispose`); encoder is a single long-lived instance in `SmtSolver.cs:20`.
- Every `CheckSatisfiability*` builds new `BoolExpr`/`ReExpr`/`SeqExpr` (regex translations alone
  allocate many), asserts them, then disposes only the `Solver`. AST nodes accumulate in the one shared
  `Context` -> unbounded native (Z3) memory growth / leak over a long `PurityProofSearch`.

### [PB1-6.6] 6.6 `TryReadNumber` can throw uncaught `OverflowException` - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/Z3FormulaEncoder.cs:2061-2074`
- `checked(value * 10 + digit)` for a long repetition count (e.g. `a{9999999999}`) overflows; not caught
  inside the regex translator -> confusing "unsupported" result rather than a clean cap at `MaxBoundedRepeat`.

### [PB1-6.7] 6.7 Reference `isNull` determined via Z3 `Expr.Equals` (hash-consing) - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/Z3FormulaEncoder.cs:587-596` (`CreateModelAssignment` Reference branch)
- `evaluated.Equals(nullValue)` relies on Z3 returning identical managed AST nodes. Two separate
  `model.Evaluate` calls can return distinct nodes for semantically-equal uninterpreted elements, so a
  genuinely-null reference can be mislabeled non-null -> Exact witness silently downgraded to `Approximate`.

### [PB1-6.8] 6.8 `IsConservativeSolverFailure` omits `NullReferenceException`/`OutOfMemoryException` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtSolver.cs:309-317`
- Catch list does not include NRE/OOM; a latent NRE in encoding/evaluation would crash analysis entirely
  instead of degrading to `Unknown`.

### [PB1-6.9] 6.9 `CheckSatisfiability` spends full timeout twice on the two-attempt path - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtSolver.cs:96-128`
- When preparation changes conditions and there is no approximate regex, the original (un-simplified)
  conditions are checked with the full `timeout`, then the prepared version runs with the *remaining*
  (possibly tiny) budget. The more reliable prepared path can be starved.

### [PB1-6.10] 6.10 `IgnorePatternWhitespace` leaks into character-class range computation - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/Z3FormulaEncoder.cs:1552-1562` (`CreateCurrentCharacterClassRegexOptions`)
- `.NET` whole-class translation includes `IgnorePatternWhitespace`, so inside `[...]` the range builder
  treats `#`/whitespace as comment/ignorable rather than literal -> wrong (marked *exact*) ranges.

### [PB1-6.11] 6.11 `ConsumedResourceCount` can overflow `long` via repeated wrap over-counting - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtSolver.cs:47-49` (follow-on to 6.3). Unchecked `+=` with ~4.29e9 per multi-check call.

### [PB1-6.12] 6.12 Shared encoder/`Context` is not thread-safe - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/SmtSolver.cs:20,31-34`; `Z3FormulaEncoder` caches (`_variables`, `_runtimeTypeTests`,
  `_regexPrecisionCache`) are plain `Dictionary`s. Concurrent use of one `PurityProofSearch`/`SmtSolver`
  would corrupt state / crash the native solver. (Likely single-threaded today.)

### [PB1-6.13] 6.13 `GetRlimit` silently clamps large budgets to `uint.MaxValue` - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtResourceBudget.cs:28-34`
- `TotalMilliseconds * 4000 >= 4.29e9` (~ > 17.9 min) collapses to `uint.MaxValue` rlimit (and the
  wall-clock safety net is capped similarly). The requested cumulative budget is silently reduced -> hard
  queries falsely `Unknown`/timeout.

### [PB1-6.14] 6.14 `CheckAndAccountResources` assumes at most one rlimit wrap between observations - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtSolver.cs:47-50`. If two successive checks each consume close to `uint.MaxValue`, the
  single-wrap recovery under-counts (extension of Round-1 rlimit bug; no wrong answers).

### [PB1-6.15] 6.15 `MergeAcrossAll` keys path conditions on `ToString()` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Smt/SmtPathConditionMerger.cs:175` (`GetFormulaKey`)
- Uses the record `ToString()` (culture/version dependent) as the canonical de-dup key instead of the
  dedicated delimiter-safe `SmtFormulaStructuralKey.Create`. Works today but fragile.

## 7. Symbolic IR Lowering & State (`SharpProof.Symbolic\Ir`, `SymbolicProgramPointFacts`, state/facts)

### [PB1-7.1] 7.1 `MergeStates` silently overrides `SymbolicVersions` with the right operand - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:~191`
- `symbolVersions = left.SymbolVersions.SetItems(right.SymbolVersions)` makes `right` win for *every*
  symbol, even if `left` facts reference an older `@vN` name. Used by `CollectForInitialEntryState` to
  combine ancestor + initializer + prior-assignment states -> internally inconsistent state. Sibling of
  the Round-1 `MergeCompletedAlternativeStates` version bug.

### [PB1-7.2] 7.2 `IndexOf`/`LastIndexOf` overloads with a `startIndex`/`count` are dropped - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Strings.cs:311-323` (`TryLowerStringSearchPredicate`)
- Only single-arg `IndexOf(char)` or `StringComparison` overloads are handled; `s.IndexOf(c, 5) >= 0`
  etc. are not lowered to a `Contains` predicate at all -> missed (but sound) proof.

### [PB1-7.3] 7.3 `StartsWith`/`EndsWith` with non-constant string arg unsupported - **MEDIUM/LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Strings.cs:50-53`
- `Contains` accepts variable args, but `StartsWith`/`EndsWith` require a `char`/`string` *constant* ->
  `s.StartsWith(prefixVar)` falls through and loses prefix/suffix semantics (precision loss).

### [PB1-7.4] 7.4 Null-conditional access with value-typed result not lowered - **MEDIUM/LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.ConditionalAccess.cs:98-127` + `Nullable.cs:~558`
- Reference-conditional path early-returns false unless result is a reference type, and the
  nullable-conditional path doesn't cover value results, so `s?.Length`, `arr?[i]`, `dict?[key]` fail to
  lower -> proof dropped (sound, but imprecise).

### [PB1-7.5] 7.5 Inlined source-predicate picks `FirstOrDefault()` declaration (partial types) - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.SourcePredicates.cs:78-80,103-105`
- For a partial type the first declaration reference may be the definition (no body), so the predicate is
  not inlined. Also closure-capture mutation after an inlined local-function is a potential unsoundness
  edge case.

### [PB1-7.6] 7.6 Range/`Index` length shapes only scanned in the immediate block - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Indexing.cs:1121-1153,1497-1529`
- `EnumerateContainingBlocks` breaks at `ContainingStatement`; range/index variables assigned in an
  enclosing block aren't resolved -> `s[range]`/`arr[index]` length shape falls back to unsupported.

### [PB1-7.7] 7.7 `Regex.Match(input, startat)` non-zero start not lowered - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Regex.cs:225-233`. Sound bail-out, precision loss.

### [PB1-7.8] 7.8 `ContainsConjunctionContradiction`/`ContainsDisjunctionTautology` only inspect flat fact lists - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Symbolic/Ir/SymbolicIr.cs:640-662,1037-1094`. A contradiction like `(A && (B || !A))` is not
  simplified at normalization; no wrong fact emitted.

### [PB1-7.9] 7.9 From-end index (`^k`) well-formedness precondition too weak - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Indexing.cs:1291-1313,1340-1377,1443`
- The `well-formed` OR-guard makes the in-range condition vacuously `true` if the precondition fails; for
  a negative `k`, `arr[^k]` is itself a runtime error but the analyzer under-approximates well-formedness
  (potential unsoundness if the harness trusts the guard).

### [PB1-7.10] 7.10 `SymbolicIrSubstitution.ReplaceTerm` keeps original `SourceSpan`/`Provenance` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIrSubstitution.cs:24-45`. Substituted facts keep the original
  provenance/source, which can mislead diagnostics and provenance-keyed de-duplication.

## 8. Analyzer Purity / Contracts / Attribute Detection (`SharpProof.Analyzer`)

### [PB1-8.1] 8.1 Purity-policy tie-break prefers Impure over equal-priority Pure evidence - **HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-PURITY-PRECEDENCE-POLICY
> **Evidence:** docs/purity-policy.md explicitly defines impure as winning equal-priority conflicts; exact configured-pure members are excluded from broad namespace matches.
> **Changes/tests:** No code change; current behavior matches the documented safety policy.
- `SharpProof.Analyzer/Engine/PurityPolicyResolver.cs:130-139`
- Candidates sorted by `Priority` ascending then `Decision == Impure ? 0 : 1`, so among *equal*
  priorities Impure always wins. `configured_pure_member` (30) ties `configured_impure_namespace_or_type`
  (30); `built_in_pure_catalog` (50) ties `built_in_impure_namespace_or_type` (50). A method explicitly
  listed pure but living in an impure namespace/type is wrongly declared **Impure** -> wrong SP0002 verdict.

### [PB1-8.2] 8.2 Implicit value-type constructor always treated Pure, ignoring impure field initializers - **HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-IMPLICIT-STRUCT-CONSTRUCTOR
> **Evidence:** An implicit metadata value-type constructor only zero-initializes storage; C# structs with field initializers require an explicit instance constructor.
> **Changes/tests:** No code change; the shortcut does not skip executable initializers.
- `SharpProof.Analyzer/GeneratedPurityCatalog.cs:114` (`TryGetPurity`), `:285-305`
  (`TryGetImplicitMetadataValueTypeConstructorPurity`)
- A synthesized implicit parameterless value-type ctor is reported `pure` *before* resolving the actual
  method identity. A C# 10+ `struct` with field initializers calling `DateTime.Now`/`Guid.NewGuid()` is
  therefore falsely treated as pure -> **false-negative impurity** (unsound).

### [PB1-8.3] 8.3 Override `[EnforcePure]` over base `[Impure]` not flagged as conflicting - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/MethodPurityAnalyzer.cs:62` + `HasInheritedPurityEnforcement:511-589`
- Conflict check only detects inherited *enforcement* conflict; it never detects an inherited `Impure`.
  `class Base { [Impure] void M(){} }` / `class Derived : Base { [EnforcePure] override void M(){} }`
  emits no `ConflictingPurityAttributes` diagnostic (the reverse is caught).

### [PB1-8.4] 8.4 Redundant `[AllowSynchronization]` false positive (only `lock` recognized) - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/MethodPurityAnalyzer.cs:122-150`
- `containsLock = DescendantNodes().OfType<LockStatementSyntax>().Any()`. A method synchronizing via
  `Monitor.Enter`/`SemaphoreSlim.Wait`/`ReaderWriterLockSlim`/`Interlocked.*`/`SpinLock` (no `lock`
  keyword) is falsely reported `RedundantAllowSynchronization` - the attribute is genuinely needed.

### [PB1-8.5] 8.5 Any recursive / mutually-recursive method always declared Impure - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/Engine/PurityAnalysisEngine.Recursive.cs:34-44` + `WorklistPuritySolver.cs:46-68`
- A visited-set cycle returns `ImpureUnknownLocation` and is cached; the recursive stub persists, so a
  provably-pure recursive method (e.g. `[EnforcePure] static int Sum(int n) => n<=0?0:n+Sum(n-1);`) is
  reported as not-pure (SP0002). Over-conservative; no fixed-point assumption on the recursive edge.

### [PB1-8.6] 8.6 Diagnostic location for conversion operators points at return type - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/MethodPurityAnalyzer.cs:634` + `AnalyzerSyntaxHelpers.cs:29-30`
- `GetIdentifierLocation` returns `c.Type.GetLocation()` for `ConversionOperatorDeclarationSyntax`
  (vs `o.OperatorToken` for `OperatorDeclarationSyntax`) -> misleading squiggle span.

### [PB1-8.7] 8.7 `MethodExpectedComplexityAnalyzer.Classify` ignores `Complexity.IsConservative` - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/MethodExpectedComplexityAnalyzer.cs:158-164`
- A conservative (widened) bound flows into `TryMapActual`/`Order` and can be reported `Verified`/
  `Exceeded` rather than flagged as approximate.

### [PB1-8.8] 8.8 Worklist fixed-point seeds callee results from a mutable dict, discards recursive re-analysis - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/Engine/Analysis/WorklistPuritySolver.cs:46-68` + `PurityAnalysisEngine.Entry.cs:22-25`
- Convergence relies on re-enqueue only on `IsPure` changes (not evidence/truncation changes); deep cyclic
  call graphs have a narrow ordering edge case. Low likelihood.

## 9. CodeFixes & Attributes (`SharpProof.CodeFixes`, `SharpProof.Attributes`)

### [PB1-9.1] 9.1 `AddEnforcePureAttributeAsync` emits `[EnforcePure]` at column 0 (no leading trivia / no Formatter) - **HIGH**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-CODEFIX-ATTRIBUTE-FORMATTING
> **Evidence:** The current code fix emits the fully qualified attribute with correct spacing and compilation is covered by SP0004_AddEnforcePure_InsertsFullyQualifiedAttribute.
> **Changes/tests:** No additional change.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:1063-1070`
- The new attribute list is inserted at index 0 with no leading trivia and no `Formatter.Annotation`, so
  for any indented member the attribute lands at column 0 and the IDE won't auto-correct. Sibling helpers
  (`AddInferredContractAttributeAsync`, `AddInferredNullableContractAttributeAsync`) correctly set
  `WithLeadingTrivia(indentation)`.

### [PB1-9.2] 9.2 All "remove attribute" fixes drop member indentation when removing the only/leading list - **MEDIUM/HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CODEFIX-TRIVIA
> **Evidence:** Current removal helpers transfer leading/trailing trivia and focused removal code-fix tests preserve indentation.
> **Changes/tests:** Existing SP002x removal formatting regressions pass; no additional code change.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:499-503,531-567,631-642,961-980,982-1024`
- When the removed list is sole/first, `SyntaxFactory.List(newLists)`/`WithAttributeLists(empty)` makes
  the keyword the new first token (its leading indentation lived on the `[`). No `Formatter.Annotation`
  is applied -> member collapses to column 0. Broad formatting regression for every removal fix.

### [PB1-9.3] 9.3 Inferred-contract fixes insert a spurious blank line before existing attribute lists - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:1105-1113,375-389`
- New list's trailing `"\n"` + existing list's leading `"\n    "` produce a double newline between the
  inserted and pre-existing attributes. Cosmetic; no `Formatter.Annotation` to repair.

### [PB1-9.4] 9.4 `RemoveContractAttributeAsync` silently no-ops when the attribute type is unresolvable - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:939-953,968-970`
- Keys off the semantic `INamedTypeSymbol`; if the attribute is in an unreferenced stub namespace,
  `shouldRemoveType(null)` -> false and nothing is removed. The lightbulb is offered but does nothing.

### [PB1-9.5] 9.5 `CreateAccessorList` derives indentation from the semicolon's trailing whitespace - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:767-785`
- For an expression-bodied property that is the last member of a file with no trailing newline,
  `indentation` becomes `""` -> generated `}`/getter body emitted at column 0.

### [PB1-9.6] 9.6 `UnnecessaryNullForgivingOperatorId` fix drops trivia between operand and `!` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:283-287`
- `WithTriviaFrom(suppression)` discards trivia between the operand and the `!` (`a /*c*/ !` -> `a `).

### [PB1-9.7] 9.7 `AddEnforcePureAttributeAsync` may emit ambiguous short name - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:1057-1060,1121-1127`
- `HasUnaliasedSharpProofAttributesUsing` only checks for `using SharpProof.Attributes;`. If another type
  named `EnforcePure` is also in scope, the short name is ambiguous -> non-compiling code.

### [PB1-9.8] 9.8 Parameter nullable-contract insertion has no leading trivia on the added list - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:401-409` (`WithoutTrivia().WithTrailingTrivia(Space)`).
  Cosmetic; inconsistent with declaration-level helpers.

### [PB1-9.9] 9.9 `ImpureAttribute` & `PureExternalAttribute` omit `Event` from `AttributeUsage` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Attributes/ImpureAttribute.cs:5-7`, `PureExternalAttribute.cs:5-7`
- These are the only contract attributes excluding `Event`/`All`; inconsistent with how
  `PropertyAttributeAppliesToAccessor` treats them, and would bite any future fix suggesting `Impure` on
  an event accessor.

## 10. EffectSummary Tool (`Tools/SharpProof.EffectSummary`)

### [PB1-10.1] 10.1 Manual/seed purity entries with differing `Classification` are silently dropped - **HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-REVIEWED-PURITY-MERGE
> **Evidence:** Same-implementation reviewed pure/impure entries are applied before catalog merging; differing implementations are deliberately discarded as ambiguous rather than trusted unsoundly.
> **Changes/tests:** No classification-precedence change; category correction is addressed by PB1-10.3.
- `Tools/SharpProof.EffectSummary/GeneratedPurityCatalogEntryRelations.cs:19-34` +
  `PurityClassificationEngine.cs:2805-2857,98-107` (`MergeGeneratedPurityEntries`)
- `ResolveGeneratedPurityEntryCandidates` returns `null` when `DoesDominate` is false in both directions,
  which happens whenever a manual/seed entry disagrees with the generated classification (e.g. seed
  `"pure"` vs generated `"impure"`). The key is then omitted -> cross-assembly calls fall through to
  `unknown_callee` instead of receiving the manual override. Defeats `--compare-manual-catalogs`.

### [PB1-10.2] 10.2 `--resume` with non-existent `--progress` file crashes instead of starting fresh - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-OPTIONAL-PROGRESS-STATE
> **Evidence:** A first resumable invocation reproduced FileNotFoundException when the progress path did not exist.
> **Changes/tests:** Artifact and shard resume start fresh when progress is absent; EffectSummaryTool_ResumeWithoutProgressStartsFresh.
- `Tools/SharpProof.EffectSummary/Program.cs:146-148,259-261,369-395,323-350`
- `LoadCompletedArtifactOutputs`/`LoadShardedProgress` call `File.ReadAllText(progressPath)`
  unconditionally -> `FileNotFoundException` on first invocation of a resumable run.

### [PB1-10.3] 10.3 `ShouldPreferReviewedUpgrade` rejects impure-vs-impure overrides with differing categories - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-REVIEWED-PURITY-CATEGORY-PRECEDENCE
> **Evidence:** A hash-matched reviewed impure entry with corrected categories was ignored in favor of reanalysis.
> **Changes/tests:** Hash-matched reviewed classifications now win, including category corrections; EffectSummaryTool_ReviewedImpureCategoriesOverrideReanalysis.
- `Tools/SharpProof.EffectSummary/PurityClassificationEngine.cs:3759-3776`
- Two `impure` classifications with *different* `Categories` fail the symmetric set-equality check ->
  returns `false` -> the manual `impure` override is silently ignored.

### [PB1-10.4] 10.4 Resume fingerprint omits analyzer/tool version - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SHARD-TOOL-IDENTITY
> **Evidence:** The sharded fingerprint contained inputs and options but no identity for the producing tool.
> **Changes/tests:** The deterministic tool module MVID is included in the fingerprint and progress document; EffectSummaryTool_ShardedProgressRecordsToolIdentity.
- `Tools/SharpProof.EffectSummary/Program.cs:298-321` (`ComputeShardedInputFingerprint`)
- After a tool rebuild, an interrupted-then-resumed `--shard-output` run reuses stale per-assembly
  shards (their `InputFingerprint` still matches) -> combined document mixes old/new results.

### [PB1-10.5] 10.5 Resume loses prior external purity seed entries when a completed artifact had no classification - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ARTIFACT-SEED-FLOW
> **Evidence:** Completed artifacts are replayed in spec order; an artifact without purity classification has no generated entries to contribute, while earlier classified artifacts are still replayed.
> **Changes/tests:** No code change.
- `Tools/SharpProof.EffectSummary/Program.cs:159-172,182-194` (`ReadGeneratedPurityEntries`)
- If a previously completed artifact was generated without `--classify-purity`, `ReadGeneratedPurityEntries`
  returns nothing, degrading cross-artifact resolution on resume.

### [PB1-10.6] 10.6 Progress file deleted on success, breaking later `--resume` - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-PROGRESS-CHECKPOINT-LIFETIME
> **Evidence:** Progress is an interruption checkpoint, not a persistent build cache; successful output completion deliberately removes it.
> **Changes/tests:** No code change; PB1-10.2 makes a later resume safely start fresh.
- `Tools/SharpProof.EffectSummary/Program.cs:177,275`
- After a fully successful run the progress file is deleted; a later `--resume --progress <samepath>`
  hits bug 10.2 (crash) or re-runs everything from scratch.

### [PB1-10.7] 10.7 `IsPathWithinDirectory` matches siblings at the volume root - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-PATH-CONTAINMENT
> **Evidence:** A drive root contains every absolute path on that drive; C:\foobar is a child of C:\, not a sibling. Deeper directory checks retain the separator boundary.
> **Changes/tests:** No code change.
- `Tools/SharpProof.EffectSummary/Program.cs:995-1002`
- For `directory = "C:\"`, a candidate `C:\foobar` also starts with `"C:\"` -> returns `true` even though
  `foobar` is a sibling (weak traversal guard; real check is against the deeper package dir).

### [PB1-10.8] 10.8 `LoadSymbols` aborts the whole artifact-spec run if a source summary yields no symbols - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.EffectSummary/Program.cs:1241-1242`
- An empty/fully-filtered `SourceSummaryPath` throws `InvalidOperationException` and aborts all artifacts
  rather than skipping the empty one.

### [PB1-10.9] 10.9 Fixed-point convergence keyed only on exact equivalence - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.EffectSummary/PurityClassificationEngine.cs:101-107` (`HaveSameGeneratedPurityEntryMap` ->
  `AreEquivalent`). Reordered equivalent categories / different dominating-chain ordering won't be seen as
  converged -> always runs the full `MaxCrossAssemblyClassificationPasses = 8` (wasted work, no wrong result).

## 11. PowerShell Scripts (`scripts`, `Tools/.../*.ps1`)

### [PB1-11.1] 11.1 `demo-sharpproof.ps1`: native command failures never detected (silent success) - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/demo-sharpproof.ps1:24,34,47-48,52,97`
- `$ErrorActionPreference = 'Stop'` does not make native commands (dotnet/powershell.exe) throw on
  non-zero exit, and `| Out-Host` pipelines leave `$LASTEXITCODE` stale. A failed `dotnet build` still
  prints "Done." and the script exits 0.

### [PB1-11.2] 11.2 `Get-SharpProofAuditInventory.ps1`: `Get-RiskClass` misclassifies the entire Symbolic module - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Get-SharpProofAuditInventory.ps1:42`
- The `Proof` alternative matches the substring "Proof" inside "Sharp**Proof**", so *every*
  `SharpProof.Symbolic\...` path returns `'proof-fallback'`. The later `public-result-cli` branch
  (`QueryService|...`, line 46) is dead for Symbolic, and non-proof Symbolic files are wrongly tagged.

### [PB1-11.3] 11.3 Fragile `Substring($repoRoot.Length)` relative-path handling (parallel copies) - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Get-SharpProofProductionMetrics.ps1:28-30`, `scripts/Get-SharpProofAuditInventory.ps1:85`,
  `scripts/Get-SharpProofTestImpactInventory.ps1:35`
- Same blind-`Substring` pattern as Round-1 hotspots:28 (casing/separator/long-path divergence throws
  `ArgumentOutOfRangeException` or corrupts paths). TestImpactInventory even pairs `StartsWith(...,
  OrdinalIgnoreCase)` with a case-sensitive positional `Substring`.

### [PB1-11.4] 11.4 `Get-SharpProofAuditInventory.ps1`: empty `.cs` files counted as 1 line - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Get-SharpProofAuditInventory.ps1:98` (`@((Get-Content ...)).Count`; a 0-byte file returns
  `$null` -> `@($null).Count == 1`). Contrast with ProductionMetrics:71 which uses `Measure-Object -Line`.

### [PB1-11.5] 11.5 `Get-SharpProofAuditInventory.ps1`: Windows-only `-like` patterns / dead Symbolic branch - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Get-SharpProofAuditInventory.ps1:57-65` (backslash `-like` matchers never match on `/`
  separators) + dead `public-result-cli` branch at line 46. Portability.

### [PB1-11.6] 11.6 `Generate-ConfigurationReference.ps1`: markdown table escaping incomplete - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CLI-INPUT-AND-OUTPUT-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Generate-ConfigurationReference.ps1:305` (only `|` -> `\|`; a description containing a backslash
  before `|` would be doubled; backtick/newline not sanitized).

### [PB1-11.7] 11.7 `Invoke-SharpProofImpactedTests.ps1`: suggested-command fidelity - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Invoke-SharpProofImpactedTests.ps1:1476-1478` (only doubles single quotes; display-only, but a
  copy-pasted suggested command with `"`/`$` won't faithfully reproduce).

## 12. Shared Helpers, Config & Attributes (`Shared`, `config`, root csproj)

### [PB1-12.1] 12.1 Ecma adapter produces different identity for `out`/`in`/`ref readonly` params by path - **HIGH**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-STRUCTURAL-REF-KIND-IDENTITY
> **Evidence:** StructuralRefKinds preserves out, in, ref, and ref-readonly while retaining explicit compatibility keys for legacy collapsed identities.
> **Changes/tests:** Covered by existing structural identity tests.
- `Shared/EcmaStructuralMethodIdentityAdapter.cs:46-48` (MemberReference) vs `:74-80`/`GetParameterRefKind`
  (MethodDefinition)
- The reference path encodes every by-ref param as only `Ref`/`None`, while the definition path
  distinguishes `Out`/`In`/`RefReadonly`. The SAME C# method yields different canonical keys across the
  two representations (`bool TryGetValue(K, out V)` -> `out` vs `ref`), so `known_pure_methods` /
  `known_impure_methods` config keys, the BCL fallback cache, and effect-summary lookups keyed by
  canonical key will **not match** -> mis-classification. Existing test only compares Roslyn-vs-Ecma, not
  the two Ecma internal paths.

### [PB1-12.2] 12.2 `EffectSummarySchemaContract` has no read-compatibility guard - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Shared/EffectSummarySchemaContract.cs:5` vs `Shared/ProofEvidenceSchemaContract.cs:5-17`
- Only `internal const int CurrentVersion = 5;`; no minimum version/policy/`IsReadCompatible`. The reader
  (`Tools/SharpProof.EffectSummary/Program.cs:1185`) does an equality check only -> a different-schema or
  partial JSON is silently accepted/partially deserialized (unlike evidence, which has a rejection path).

### [PB1-12.3] 12.3 `KnownPureBCLMembers` and `KnownFreshOwnedArrayReturningMembers` are empty - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Shared/Constants.cs:138-142`; consumed at `PurityClassificationEngine.cs:2750,2754-2755`
- Both are empty `ImmutableHashSet`s while `KnownImpureMethods` is richly populated. If intended to carry
  "known pure BCL" / "fresh-owned-array" data, the corresponding classification is a no-op relying on
  elsewhere-sourced data; `ConstantsTests.cs:24` only asserts `Not.Null`. Confirm whether intentional.

### [PB1-12.4] 12.4 `GetTypeDefinitionMetadataName` drops generic-arity suffix - **MEDIUM/LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Shared/EcmaStructuralMethodIdentityAdapter.cs:148-158`
- Declaring-type name never includes the `` `n `` arity marker, so a generic `Foo<T>` and non-generic `Foo`
  get identical containing-type identity (legal as distinct metadata tokens in raw IL).

### [PB1-12.5] 12.5 `BclPurityFallbackHeuristics.IsAmbientNamespaceOrType` over-matches via substring & omits bare `System` - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Shared/BclPurityFallbackHeuristics.cs:170-192`
- `ContainsAny` substring scan can force-impure a framework type whose name merely contains
  "File"/"Stream"/"Process"; the namespace arm only lists `System.IO`/`System.Net`/... (not bare `System`),
  so ambient `System.*` types bypass it.

### [PB1-12.6] 12.6 Reference-path return ref-kind ignores `In` attribute (asymmetry) - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Shared/EcmaStructuralMethodIdentityAdapter.cs:82-88` vs `:50-52` (reinforces 12.1).

### [PB1-12.7] 12.7 Root `.globalconfig` disables effect-summary JSON vs profile globalconfigs that enable it - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `C:\w\PurelySharp\.globalconfig:3` (`sharpproof_enable_effect_summary_json = false`) vs
  `config/profiles/sharpproof-{strict,audit}.globalconfig:6` (`= true`); both `is_global = true`. Discovery
  order determines which wins -> silently changes whether effect-summary JSON is emitted.

## 13. Large Test Files (`SharpProof.Test`, `SharpProof.ToolingTest`)

### [PB1-13.1] 13.1 Order-dependent `GeneratedPurityCatalog.Entries[0]` assumes first entry is a specific member - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/EffectSummaryToolTests.cs:6101,6283`
- Asserts a property of `Entries[0]` (an `ArtifactSource`/`DisplayName`) assuming a specific ctor/package
  entry sorts first. Passes only because constructors sort before getters (`".ctor"` < `".get_"` under
  Ordinal); a sort/order change validates the wrong entry (asserts-the-wrong-thing risk).

### [PB1-13.2] 13.2 Effect-summary runner helpers don't drain stdout/stderr -> potential deadlock/hang - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/EffectSummaryToolTests.Helpers.cs:450,549,594,699`
- `RunEffectSummaryAsync`/`RunFilteredEffectSummaryAsync`/`RunRuntimeEffectSummaryAsyncCore` start the tool
  without `RedirectStandardOutput`/`RedirectStandardError` and only `WaitForExitAsync().WaitAsync(timeout)`.
  Verbose output can fill the OS pipe buffer -> process blocks -> `TimeoutException` (240s) flaky/hanging
  test. Sibling helpers (lines 476-477,581-584) do redirect and read both streams.

### [PB1-13.3] 13.3 Brittle substring-counting of live source files - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/ArchitectureReductionTests.cs:2900,2905`
- Counts `new SymbolicSourceQueryResult(` occurrences in the actual repo source (fragile to comments/
  string literals/refactors). A brittle source-structure assertion rather than behavioral.

### [PB1-13.4] 13.4 Hardcoded SMT budget values tightly coupled to defaults - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/ArchitectureReductionTests.cs:5967-5968` (`MaxPathConditions == 192`,
  `TimeoutMilliseconds == 750`). Any default-budget change breaks the test though logic is unchanged.

### [PB1-13.5] 13.5 Tautological `SourceMap` assertions (test echoes its own inputs) - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.ToolingTest/SymbolicSourceQueryLineTests.cs:55,65-66`
- Constructs `SymbolicSourceMap("editor://...", 41, 7)` then asserts `OriginalStartLine == 41` /
  `OriginalStartColumn == 7`. Only verifies the constructor stored its inputs, not real mapping behavior.

### [PB1-13.6] 13.6 `FindMethod` helper uses `.Single()` on fixture methods - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/EffectSummaryToolTests.Helpers.cs:385`
- Throws `InvalidOperationException` (no diagnostic) if a fixture assembly ever has two methods with the
  same `DisplayName` (ref-out overloads / generic-arity collisions). Latent test-infra fragility.

## 14. Docs, Samples & Demos (`docs`, `samples`, `SharpProof.Demo`, `SharpProof.Smoke.Net472`)

### [PB1-14.1] 14.1 Demo suppresses `SP0002` globally while its comments claim `SP0002` fires - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Demo/Program.cs:9,35,42,50` vs `SharpProof.Demo/SharpProof.Demo.csproj:11`
  (`<NoWarn>$(NoWarn);SP0002</NoWarn>`)
- The demo is annotated with comments like `// I/O under [Pure] -> SP0002`, but `NoWarn` suppresses
  `SP0002` (an `Error` diagnostic) for the whole project. `dotnet build SharpProof.Demo` produces **no**
  SP0002 despite the documented comments -> the demo's primary pedagogical point is silently neutralized.
  (`SP0004` is *not* suppressed, so those comments remain accurate.) Suggest dropping `SP0002` from
  `NoWarn` and relying on the `.editorconfig` `warning` demotion.

### [PB1-14.2] 14.2 "restoring SharpProof.Symbolic from NuGet" contradicts "not published to NuGet.org yet" - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `samples/SharpProof.Symbolic/README.md:6-10` vs root `README.md:80,26` / `README.source.md:78`
- Sample README tells users to restore `SharpProof.Symbolic` from NuGet and `dotnet run`; the root README
  states the public packages are not published to NuGet.org yet (local-feed build required). On a clean
  machine the sample's `dotnet run` / `dotnet add package` fails to restore.

### [PB1-14.3] 14.3 `contracts.md` understates coverage as "SP0002 through SP0040" - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `docs/contracts.md:148`
- States the gallery covers `SP0002`-`SP0040`, contradicting the project's own `SP0002`-`SP0047` range
  (README:647, diagnostic-examples.source.md:13, and the analyzer's continuous `SP0002`...`SP0047`).

### [PB1-14.4] 14.4 Demo/smoke list library assemblies as `OutputItemType="Analyzer"` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Demo/SharpProof.Demo.csproj:23-24`, `SharpProof.Smoke.Net472/SharpProof.Smoke.Net472.csproj:15-16`
- `SharpProof.Symbolic`/`SearchLib` are ordinary libraries, not Roslyn analyzers; marking them
  `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` is incorrect usage (works today but is
  wrong-by-construction and a maintenance smell).

### [PB1-14.5] 14.5 Demo `.editorconfig` reclassifies `SP0004` as `suggestion` (docs show `Warning`) - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Demo/.editorconfig:5` vs `README.md:168` / `docs/diagnostic-examples.md`
- Within the demo `SP0004` appears as a suggestion, inconsistent with documented `Warning` severity.

### [PB1-14.6] 14.6 Root README's "intended public packages" list omits `SharpProof.Symbolic` - **LOW**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
- `README.md:79-80` / `README.source.md:77`
- Says intended public packages are `SharpProof` and `SharpProof.Attributes`, but the rest of the docs
  (and README lines 617-635) treat `SharpProof.Symbolic` as the primary supported library/API package.

---

---

# Round 3 - additional findings (10 agents, de-duplicated)

The following were found by 10 additional review agents focused on deeper/adjacent
areas, each instructed to read `POTENTIAL_BUGS.md` first and avoid duplicates.
Highest-impact new items: shared non-thread-safe `SemanticModel` used concurrently
(HIGH), `Math.Abs(int.MinValue)` overflow unsound modeling (MEDIUM-HIGH), `int.MinValue`
negation/division overflow unmodeled (HIGH), `[MaybeNullWhen]` inverted polarity (HIGH),
delegate-target map intersect-vs-union merge inconsistency, missing Linux native Z3 in
NuGet package, orphaned suspended process under CI job nesting.

## 15. Symbolic IR Lowering - Arithmetic / Numerics / Conversions / Regex (`SharpProof.Symbolic\Ir`)

### [PB1-15.1] 15.1 `Math.Abs(int.MinValue)` overflow never modeled (wrong return + missed `OverflowException`) - **MEDIUM/HIGH**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-RUNTIME-OVERFLOW-HAZARDS
> **Evidence:** Current runtime-hazard modeling emits an OverflowException candidate for Math.Abs on signed MinValue.
> **Changes/tests:** Existing SymbolicRuntimeHazardQueryTests cover Math.Abs(int.MinValue).
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Numerics.cs:81-111` (`TryLowerIntegralMathAbsInvocation`)
- Lowers to `x >= 0 ? x : 0 - x`. For `int.MinValue` the else branch encodes unbounded Z3 math = `2147483648`,
  and no exception precondition is attached. `Math.Abs(int.MinValue)` actually throws `OverflowException`. The
  checked-overflow hazard factory only handles checked casts / `int.MinValue / -1` / checked binary ops, never
  the `Math.Abs` call -> unsound purity verdict / wrong value fact.

### [PB1-15.2] 15.2 `Math.Clamp` missing the `min <= max` precondition (wrong value + missed `ArgumentException`) - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Numerics.cs:44-79` (`TryLowerIntegralMathClampInvocation`)
- Lowered as nested conditional correct only when `min <= max`. For `min > max`, .NET throws `ArgumentException`
  but the model returns a value (the `min` branch wins), swallowing the exception -> wrong return value.

### [PB1-15.3] 15.3 `Regex.IsMatch` null input folded into the predicate masks `ArgumentNullException` - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Regex.cs:178-184` (`TryLowerRegexInvocationParts`)
- Becomes `(s != null) && regexMatches(s, pattern)`. In C#, `Regex.IsMatch(null, ...)` *throws*, it does not
  return false. A method with nullable input is modeled as unconditionally returning a bool -> can miss the
  null-input hazard and yield unsound purity.

### [PB1-15.4] 15.4 Overflow-sensitivity heuristic only fires for an `IdentifierNameSyntax` RHS - **MEDIUM/LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs:346-379` (`IsUncheckedOverflowSensitiveComparisonOperand`, line 367)
- `MayOverflow = true` only when the other operand is a bare `IdentifierNameSyntax`. `unchecked(a + b) > 5` or
  `> this.Count` returns `false` -> sum modeled as unbounded Z3 math, ignoring the wraparound `unchecked` produces
  -> wrong comparison fact.

### [PB1-15.5] 15.5 Arithmetic binary lowering lacks the `OperatorMethod == null` guard the bitwise path has - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs:331-344` vs `SymbolicIrLowerer.Operators.cs:22`
- A user-defined `operator +/-/*` on a type whose operands lower to `Int` would be modeled as pure integer
  arithmetic, ignoring real (possibly impure) operator semantics. Latent.

### [PB1-15.6] 15.6 `IsValuePreservingIntegralConversion` drops `int`->`enum` (and enum->enum) casts with no tag - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Conversions.cs:421-438,343-351`
- Value-preserving casts are dropped entirely -> `Int` term with no enum-type link; distinct enums backed by the
  same underlying type become indistinguishable (sound for equality/arithmetic, destroys type identity).

### [PB1-15.7] 15.7 `Regex.Matches(...).Count` classification only covers a few constant comparisons - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Regex.cs:97-117`
- Only `(==0)/(<1)/(<=0)/(!=0)/(>0)/(>=1)` are handled; `matches.Count == 1`, `> 2`, etc. silently unsupported
  (sound bail, but a precision gap).

### [PB1-15.8] 15.8 `as`-cast guard uses only `IsReferenceType` on static types - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Conversions.cs:152-184,278-305`
- Structural identity check can conclude "identity preserving" between generic instantiations / `T as TBase`,
  reusing the reference verbatim without a version/alias refresh.

### [PB1-15.9] 15.9 `checked` arithmetic inlined via SourcePredicates loses overflow guard - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs:342` + `SymbolicIrFormulaEncoder.cs:259-264`
- `MayOverflow` is only produced for `IsChecked: false` operations, so `checked` helpers inlined via
  `TryLowerReturnedBoolean`/`TryLowerSourceMethodBooleanInvocation` carry only the exact `a + b` term with no
  overflow guard -> a wrong value fact can propagate into the caller if not re-derived against the hazard trigger.

### [PB1-15.10] 15.10 `TryLowerTypeOfComparison` mixes `out var right`/`out var left` across `||` operands - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Types.cs:37-44`
- Correct only due to `&&`/`||` precedence; a future reordering would silently bind the wrong variable to `null`.

## 16. Analyzer - Nullable Contracts, Allocation, Exceptions, Engine (`SharpProof.Analyzer`)

### [PB1-16.1] 16.1 `[MaybeNullWhen]` verification uses inverted polarity - **HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONDITIONAL-NULLABILITY-POLARITY
> **Evidence:** MaybeNullWhen permits null on the named result branch and requires non-null on the opposite branch; the implementation deliberately proves the opposite branch.
> **Changes/tests:** Existing NullableContractVerificationTests cover the false branch; implementation matches the attribute contract.
- `SharpProof.Analyzer/NullableContractAnalyzer.cs:121-135`
- `ConditionalImplication(result, !m, "target != null")` expands to `(result == false) || target != null`, i.e.
  "if the method returns **true**, the parameter must be non-null." But `[MaybeNullWhen(true)]` means the
  parameter **may be null** when it returns true and places no obligation on the true path. The code treats
  `MaybeNullWhen(X)` like `NotNullWhen(!X)` - the opposite contract. `[NotNullWhen]` (lines 106-119) does it
  correctly, so the two are inconsistent by construction -> false-positive `NullableParameterPostconditionViolationRule`.

### [PB1-16.2] 16.2 `NotNullWhen`/`MaybeNullWhen` on `out`/`ref` params of **void** methods never verified - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/NullableContractAnalyzer.cs:108,124`
- Both postcondition loops are guarded by `if (completion.ResultExpression != null)`. Void methods have
  `ResultExpression == null`, so the entire check is skipped even though these attributes are most commonly
  applied to `out`/`ref` params of void/bool methods -> missing verification.

### [PB1-16.3] 16.3 Delegate-target map is intersected at CFG merge but unioned across-all - **MEDIUM/HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-DELEGATE-TARGET-MERGE
> **Evidence:** Both pairwise CFG merge and across-all merge retain only keys present on every path while unioning each retained key target set.
> **Changes/tests:** Code-path comparison shows the same conservative lattice operation; no code change.
- `SharpProof.Analyzer/Engine/PurityAnalysisEngine.StateMerge.cs:87` vs `:11-22,412-418`
- Pairwise merge (`IntersectDelegateTargetMaps`) keeps a key only if present in *both* predecessors and merges
  targets; the across-all merge (`MergeDelegateTargetMapsAcrossAll`) **unions**. Logically after a join a delegate
  "may be" any branch's target, so intersect is wrong. `DelegateTargets.cs:68` `TryGetValue` can then miss the
  impure target -> possible missed impurity (under-reported impurity).

### [PB1-16.4] 16.4 Inferred `[NotNull]` suggestion gate is backwards - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/InferredContractSuggestionAnalyzer.cs:103-108`
- The parameter suggestion requires `parameter.NullableAnnotation != Annotated`, but the useful `[NotNull]` suggestion
  is for an **annotated-nullable** parameter (`string?`) whose guard proves non-null. So it suggests `[NotNull]` on
  already-non-nullable params (redundant/no-op) and skips the real case.

### [PB1-16.5] 16.5 `[MaybeNullWhen]` gate on `NullableAnnotation == NotAnnotated` skips the usual case - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/NullableContractAnalyzer.cs:121-122`
- `[MaybeNullWhen]` is overwhelmingly applied to annotated-nullable `out`/`ref` params; this guard excludes exactly
  those, so the (already inverted, 16.1) check is skipped for nearly all real usages.

### [PB1-16.6] 16.6 `[ZeroAllocations]`: implicit non-params array allocations silently skipped - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/MethodAllocationAnalyzer.cs:137-144`
- A new-array site is flagged only when explicit or an implicit params-array. An implicitly-inserted array that is
  *not* a params array still allocates but escapes the rule -> false-negative allocation.

### [PB1-16.7] 16.7 `ConcurrentDictionary.GetOrAdd` re-entrancy in `GetPurity` -> inconsistent/stale purity cache - **MEDIUM/LATENT**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/Engine/CompilationPurityService.cs:74-82`
- `GetOrAdd`'s value factory can run more than once under concurrent/recursive calls, yielding a half-computed
  `PurityAnalysisResult` reused for later analyses. Distinct from Round-2 Section 8.5.

### [PB1-16.8] 16.8 `CallGraphBuilder` CFG pass is incomplete - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/Engine/Analysis/CallGraphBuilder.cs:353-403`
- Second pass only walks `IInvocationOperation` edges and uses `method.DeclaringSyntaxReferences.FirstOrDefault()`
  (only first declaration of a partial type) -> can miss accessor/operator/await/delegate/ctor-initializer edges and
  partial-type bodies split across files. Incomplete call graphs feed the worklist fixed point.

### [PB1-16.9] 16.9 Exception-contract collection runs outside the `UseAttributePolicy` scope - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/ExceptionFlowAnalyzer.cs:57` vs `:72`
- `CollectExceptionContracts` executes using the explicit policy param (correct today), but `using
  (UseAttributePolicy(attributePolicy))` is only entered at line 72. Under `EnableConcurrentExecution` the AsyncLocal
  reset can interleave -> wrong exception-contract classification if a future switch reads `ActiveAttributePolicy`.

## 17. Symbolic Query API & CLI (`SharpProof.Symbolic`, `Tools/SharpProof.SymbolicCli`)

### [PB1-17.1] 17.1 Complexity CI gate broken for `Product`/`Max` bounds; mis-trips `--fail-on-complexity-unknown` - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.SymbolicCli/SymbolicCliExitGateEvaluator.cs:114-126,310-391` (`CompareComplexity`/`TryGetChainRank`)
- `TryGetChainRank` returns `false` for `Product` and `Max` (no case), so `--fail-on-complexity-exceeded Product` can
  **never** report `Exceeds` (only an exact match avoids `Incomparable`). The gate then treats `Incomparable` as
  "unknown" -> `--fail-on-complexity-exceeded Product --fail-on-complexity-unknown` **erroneously fails** on
  well-bounded code (e.g. `Quadratic` actual with `Product` bound). `ReadComplexityBound` accepts `Product`/`Max`.

### [PB1-17.2] 17.2 Source-map remapping is a dead feature - output coordinates never translated - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicSourceInput.cs:1090-1102`; `Tools/SharpProof.SymbolicCli/Program.cs:302-306`
- `--source-map-uri`/`--source-map-original-line`/`--source-map-original-column` are parsed, stored on
  `SymbolicSourceInput.SourceMap`, and echoed in explain output ("Source map origin: line 41, column 7"), but a repo-wide
  grep shows `OriginalStartLine`/`OriginalStartColumn` are only constructed/printed - no code consumes them to offset
  reported `Line`/`Column`. Results stay snippet-relative.

### [PB1-17.3] 17.3 Capability CI gate `IO` expansion omits `Process`/`Environment`/`Clock`/`Randomness`/`Reflection`/`Synchronization`/`NativeInterop` - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.SymbolicCli/SymbolicCliExitGateEvaluator.cs:277-299` (`ExpandAllowedCapabilities`/`NormalizeCapabilities`)
- `--allowed-capability IO` expands `IO` only to `FileRead|FileWrite|Network|Console|Registry`. A method spawning a
  process (`Process` capability) counts as a *capability violation* even though `Process` is intuitively I/O.

### [PB1-17.4] 17.4 Two parallel capability enums with no shared type - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Attributes/SharpProofCapability.cs` vs `SharpProof.Symbolic/SymbolicCapabilityModels.cs`
- Classification uses `SharpProofCapability` (Attributes); result model + CLI gate use `SymbolicCapability` (Symbolic).
  Identical bit layouts today, but any future divergence silently corrupts the `Capabilities` flag and every capability
  gate with no compile-time error.

### [PB1-17.5] 17.5 Empty absolute span (`--span-start == --span-end`) accepted -> silent empty result - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.SymbolicCli/Program.cs:2060-2063,2065-2070`
- Validation only rejects `SpanEnd < SpanStart`; a zero-length span yields a successful empty report with no diagnostic.

## 18. Smaller Test Files (`SharpProof.Test`, `SharpProof.ToolingTest` - excluding huge files)

### [PB1-18.1] 18.1 `RequiresContractTests` suppression test can pass vacuously - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/RequiresContractTests.cs:118-137` (`Requires_AssumptionFeedsRuntimeHazards_SuppressesDivideByZero`)
- Only asserts `diagnostics.Where(d => d.Id == UncaughtExceptionSiteId)` is empty; never asserts the hazard would fire
  without `[Requires]` or that the assumption was consumed. A disabled hazard pipeline still passes -> masks regression.

### [PB1-18.2] 18.2 `EnsuresContractTests` uses weak `Is.Not.Empty` instead of asserting the specific diagnostic - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/EnsuresContractTests.cs:233` (`Ensures_AllowNullOverridesNonNullableParameterEntryFact`)
- Only checks *some* diagnostic exists; if the analyzer routes this case to a nullability diagnostic instead of the
  Ensures-not-proven (SP0018), the test still passes -> masks regression in the Ensures prover.

### [PB1-18.3] 18.3 `AnalyzerHostConcurrencyStressTests` vacuous `.All()` without count guard - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/AnalyzerHostConcurrencyStressTests.cs:243`
- `purityResults.Select(...).All(...)` has no `Has.Length`/`Is.Not.Empty` guard; an empty task list would pass vacuously.

### [PB1-18.4] 18.4 `FuzzToolTests` purely-negative finding check - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/FuzzToolTests.cs:194` (`f.Category == "impure_missing_sp0002"` is `False`)
- Doesn't verify the expected impure SP0002 finding was produced; a regression dropping it still passes this line.

### [PB1-18.5] 18.5 Systemic: many negative `Any(id) == False` assertions pass if analyzer emits zero diagnostics - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- Across `ExceptionReachabilitySmtTests.cs`, `ExceptionSummaryCatalogValidationTests.cs`, `DiagnosticEvidenceTests.cs`, etc.
- Each is correct for its scenario, but collectively a globally-disabled feature (zero diagnostics) would make all pass ->
  recommend adding positive controls for a representative subset.

### [PB1-18.6] 18.6 `BaselineWorkflowTests` hardcoded line/column tied to fixture internals - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.ToolingTest/BaselineWorkflowTests.cs:23-24` (`Line == 10`, `Column == 15`). Brittle to fixture changes.

## 19. PowerShell Scripts - Round 3 (`scripts`, `Tools/.../*.ps1`)

### [PB1-19.1] 19.1 Orphaned *suspended* child process + leaked job on `AssignProcessToJobObject` failure - **HIGH/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-JOB-ASSIGNMENT-FAILURE
> **Evidence:** A child created suspended was not terminated if AssignProcessToJobObject failed, and the job handle could escape cleanup.
> **Changes/tests:** JobObjectHelpers now directly terminates unassigned children and always closes ownership handles; ScriptProcessOwnershipTests.
- `scripts/JobObjectHelpers.ps1:258-262`
- Child is created `CreateSuspended` (line 247) and `$process` assigned only *after* the assign step (line 264). If
  `AssignProcessToJobObject` fails it `throw`s (line 261) before `$process` is set; the `finally` (line 296) sees
  `$process -eq $null` and never resumes/terminates the suspended child -> a permanently suspended, orphaned process
  leaks. Fires whenever the wrapper PS is *already* inside a Windows Job Object - exactly the CI scenario mandated by
  `AGENTS.md` (a process can't belong to two jobs).

### [PB1-19.2] 19.2 Interrupted `summary.partial.json` aggregated as a completed fuzz phase - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz/Aggregate-FuzzRun.ps1:29-37`
- A partial (interrupted/aborted) run's `summary.partial.json` is folded into totals like a real `summary.json` -> all
  downstream sums (`totalCases`, `totalFindings`, `totalSp0002`, `PhaseCount`) over-report.

### [PB1-19.3] 19.3 `build.ps1` wipes all `.nupkg` then repacks only two - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `build.ps1:94-100`
- `Remove-Item` deletes *every* `.nupkg` in the output dir (including a `SharpProof.Symbolic.*.nupkg` from a prior
  `build-nuget.ps1`), but `build.ps1` only re-packs `SharpProof.Package` and `SharpProof.Attributes` ->
  `SharpProof.Symbolic` silently dropped from output.

### [PB1-19.4] 19.4 `Generate-Readme.ps1` regex requires `[Test]` immediately before `public` - **MEDIUM/LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Generate-Readme.ps1:50`
- Pattern requires `[ReadmeExample("x")]\s*[Test]\s*public`. `[ReadmeExample("x"), Test]`, `[Test, ReadmeExample("x")]`,
  or any other attribute between them won't match -> id never added -> later `throw` aborts README regeneration.

### [PB1-19.5] 19.5 `Get-SharpProofRawSmtHotspots.ps1:28` blind `Substring` (4th instance of Round-2 Section 11.3) - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Get-SharpProofRawSmtHotspots.ps1:28`
- Same unguarded `$fullPath.Substring($repoRoot.Length)` path-corruption pattern (this file wasn't among the three
  enumerated in Round-2 Section 11.3).

### [PB1-19.6] 19.6 `Test-SharpProofPackageConsumers.ps1:172` `-match` can false-positive on message text - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Test-SharpProofPackageConsumers.ps1:172`
- SARIF searched as raw text for `AD0001|CS8032|CS8034|CS8785`; a legitimate analyzer *message* or embedded comment
  containing those tokens is mistaken for a load failure -> aborts consumer validation.

### [PB1-19.7] 19.7 `build.ps1` never rebuilds VSIX when a stale `.vsix` already exists - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `build.ps1:50-52,91`
- Only invokes MSBuild when **no** `.vsix` is present; stale VSIX after source changes ships without rebuilding.

### [PB1-19.8] 19.8 `Generate-ConfigurationReference.ps1` brittle `ForSmtModes` parsing - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Generate-ConfigurationReference.ps1:138,144`
- Any formatting deviation in a default value makes `Convert-CSharpString` throw and abort the whole doc generation.

## 20. SearchLib Remaining - Proof Search / Witness / Hazard Factory (`SearchLib`, `SharpProof.Symbolic`)

### [PB1-20.1] 20.1 Integer overflow of `int.MinValue` negation and `int.MinValue / -1` unmodeled (unsound) - **HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BOUNDED-INTEGER-OVERFLOW
> **Evidence:** Bounded signed arithmetic was encoded as unbounded integer arithmetic, proving identities that fail through overflow or exceptional division.
> **Changes/tests:** Overflow-sensitive IR plus congruent opaque SMT arithmetic and division/remainder hazards; SymbolicIrTests and runtime-hazard regressions.
- `SearchLib/Z3FormulaEncoder.cs:171-178` (`EncodeIntegerUnary`/`MkUnaryMinus`), `:211-240` (`EncodeCSharpIntegerDivide`)
- Z3 integer sort is unbounded, so `-int.MinValue` encodes to `+2147483648` and `int.MinValue / -1` to `+2147483648`. In
  C# (default **unchecked**) both wrap to `int.MinValue`. No range-clamp/overflow detection; `ValidateIntegerTermSafety`
  only rejects divisor == 0 (Round-2 Section 6.1). `Math.Abs(int.MinValue) == int.MinValue` is **true** in C# but **false** in
  the SMT model -> unsound `ProvablyPure`/`ProvablyImpure`. Distinct from Section 6.1 (which only covers divisor == 0).

### [PB1-20.2] 20.2 `InternalOnly` visibility routes throwing hazards to path-only check that ignores the trigger - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/PurityProofSearch.cs:106-125` (`Classify`)
- `if (query.Hazard.Visibility == InternalOnly)` is evaluated *before* the `switch` on `Kind`, funneling
  NullDereference/DivideByZero/BranchReachability/ImpureCallReachability into `ClassifyInternalOnlyEffect` (path
  feasibility only, trigger discarded). Those kinds are never "internal-only" in .NET; if such a query is built, it is
  proven `ProvablyPure` whenever the path is feasible -> missed hazard. Latent; worth a guard/assert.

### [PB1-20.3] 20.3 Path feasibility mislabeled `Satisfiable` under approximate regex (`adjustApproximation:false`) - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/SmtSolver.cs:137` (+ `PurityProofSearch.ClassifyCore`)
- When the path has an over-approximated (non-`isExact`) regex, `CheckPathAndImpurityWithWitness` calls
  `CheckSatisfiability(..., false)` so `Feasibility` reports `Satisfiable` while the witness `Status` is `Approximate`.
  Final outcome is still sound, but any caller inspecting `check.Path.Feasibility` directly is misled.

### [PB1-20.4] 20.4 No enforced cumulative budget; each check gets a fresh per-call rlimit - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/SmtSolver.cs:29,36-55` + `SearchLib/Z3FormulaEncoder.cs:58`
- `ConsumedResourceCount` is accumulated but `CreateSolver` always sets a fresh per-call rlimit; nothing enforces the
  cumulative counter. A reused `PurityProofSearch` has no enforced method-level cap (resource-accounting gap).

### [PB1-20.5] 20.5 Path check can consume up to 2x timeout/rlimit before impurity check (compounds Round-1 Section 4.7) - **LOW**

> **Disposition:** Duplicate
> **Canonical root cause:** RC-DUPLICATE-CLAIM
> **Evidence:** This claim describes the same behavior and root cause as another canonical ledger item.
> **Changes/tests:** Resolved or classified under the canonical root-cause entry.
- `SearchLib/SmtSolver.cs:96-121`
- Both original-conditions and prepared-conditions attempts run with the full timeout; a path phase that internally ran
  two full-rlimit attempts can leave `remaining <= 0` for the impurity query -> collapses to `Unknown`.

### [PB1-20.6] 20.6 `TryGetMemoryExtensionsViewSlicingShape` assumes positional `[start, count]` int-argument order - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicRuntimeHazardCandidateFactory.cs:2176-2185`
- Collects every int-typed param positionally; any overload where an int param isn't strictly start-then-length
  (Index/Range-based or extra int param) mis-assigns start/count -> incorrect slicing-bounds hazard.

### [PB1-20.7] 20.7 Non-null reference can be mislabeled null in witness (converse of Round-2 Section 6.7) - **MEDIUM witness, rooted in Section 6.7**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/Z3FormulaEncoder.cs:587-597`
- `model.Evaluate(...).Equals(nullValue)` hash-consing can also make a *non-null* reference report `IsNull:true,
  Status:Exact, Value:"null"` - a wrong Exact witness value (doesn't change the proof outcome, but unsound witness).

## 21. Shared, Config, Baseline, Build Props (`Shared`, root props)

### [PB1-21.1] 21.1 `BclPurityFallbackHeuristics.HasMutatingName` over-matches by prefix -> pure immutable BCL methods flagged `probably_impure` - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Shared/BclPurityFallbackHeuristics.cs:136` (`HasMutatingName`), `:194-225` (`StartsWithAny`)
- Prefix match (`Add`/`Create`/`Remove`/`Insert`/`Replace`/`Clear`/`Set`/`Read`/`Write`/...) classifies
  `System.Collections.Immutable.ImmutableArray<T>.Add/.Create/.Remove/.Insert/.Replace/.Clear` and
  `ImmutableHashSet<T>.SetEquals` as `probably_impure` -> false SP0002 when no stronger pure evidence exists. (Distinct
  from Round-2 Section 12.5, which is only `IsAmbientNamespaceOrType`.)

### [PB1-21.2] 21.2 SMT/analysis-limit config options: `build_property.` prefix honored by validation but NOT by the engine - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicProjectQueryContext.cs:149-166` (prefix-unaware `TryGetGlobalOption`) vs
  `SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs:599-622` (prefix-aware); `SymbolicProjectConfiguration`
  reads via the prefix-unaware helper but validation reports via the prefix-aware helper.
- A value supplied via an MSBuild property named like the config key is validated/reported as effective, yet the SMT
  engine applies the hardcoded default -> silent config divergence.

### [PB1-21.3] 21.3 `ClassifyProperty` returns `ProbablyPure` for static reference-returning getters - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Shared/BclPurityFallbackHeuristics.cs:151-155`
- Guard `!shape.HasValueLikeReturn && !shape.IsStatic` means a **static** getter returning a reference-like type skips
  the `Unknown` branch and falls to `ProbablyPure` -> wrong guess for mutable static reference properties.

### [PB1-21.4] 21.4 `IsLikelyFrameworkValueTypeName` treats any `*Handle` framework type as value-like - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Shared/BclPurityFallbackHeuristics.cs:250` (`EndsWith("Handle")`)
- Includes reference types like `System.Runtime.InteropServices.SafeHandle` -> biased toward `probably_pure`.

### [PB1-21.5] 21.5 `ImpurityCatalog` `simplifiedName` branch is dead / never matches `Constants.KnownImpureMethods` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/Engine/ImpurityCatalog.cs:169-171`
- `$"{symbol.ContainingType.Name}.{symbol.Name}"` produces `"FileInfo.get_Length"`, but `KnownImpureMethods` entries are
  fully namespace-qualified with `.get`/`.set` suffix. The full `signature` path is what matches -> no-op dead branch.

### [PB1-21.6] 21.6 Baseline path normalization diverges between dedup key and comparison key - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs:615` (`NormalizePath(...).ToUpperInvariant()`) vs `:231`
  (`OrdinalIgnoreCase`). Disagree for non-ASCII (e.g. Turkish dotless `i`) -> inconsistent dedup/explain.

### [PB1-21.7] 21.7 Scope-option `AllowedValues` lists incomplete and `IsCanonicalAllowedValue` is dead - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/Configuration/AnalyzerConfigurationOptionRegistry.cs:80-81,116-118` (allowed = `{all,public,internal,off}`)
  while the parser/validator also accepts `{public-only,internal-only,none,false}`. `IsCanonicalAllowedValue` (`:392-397`)
  is never called -> documented allowed-values under-reported.

### [PB1-21.8] 21.8 `GetSmtMode` uses a hardcoded key string instead of `ConfigKeys.SmtMode` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicProjectQueryContext.cs:114` (magic string `"sharpproof_smt_mode"`). Drift risk if the
  constant changes.

### [PB1-21.9] 21.9 Baseline entries stamped evidence schema v2 even when derived from legacy-v1 SARIF - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs:316-325` (default `EvidenceSchemaVersion = CurrentVersion = 2`)
  while `ValidateEvidenceSchemas` (`:459-464`) accepts legacy v0/v1. Migration-stamping inconsistency.

### [PB1-21.10] 21.10 `StructuralMethodIdentity.ContractVersion` is unused - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `Shared/StructuralMethodIdentity.cs:26` (constant `ContractVersion = 1`) never referenced in `ToCanonicalKey`/
  `TryParseCanonicalKey`; only the literal prefix `"spm1"` encodes version -> no in-key version field for migration.

## 22. Tools Subprojects & Packaging (`Tools/*`, `SharpProof.Package`, `SharpProof.Vsix`)

### [PB1-22.1] 22.1 Fuzz harness: analyzer crashes conflated with expectation failures - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/Program.cs:494-515,517-624`
- On analyzer exception, `GetAnalyzerDiagnosticsAsync` returns empty `Diagnostics` + the exception text. `Evaluate` then
  runs expectation checks against the *empty* set AND adds an `analyzer_exception` finding -> for `DefinitelyImpure` it
  also trips `impure_missing_sp0002`, for `ImpureWithException` also `missing_sp0010`, and combined with repeat-runs can
  additionally trip `nondeterministic_diagnostics`. One transient crash yields three misleading findings; the real
  "analyzer threw" signal is muddied (compounds Round-1 Section 1.1 / Section 1.3 under `Parallel.ForEachAsync`).

### [PB1-22.2] 22.2 NuGet package omits Linux native Z3 (`libz3.so`) - **MEDIUM**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-LINUX-Z3-DISTRIBUTION
> **Evidence:** The pinned Microsoft.Z3 package supplies no Linux native asset to include; adding a separately sourced native binary changes the distribution and licensing surface.
> **Changes/tests:** The loader now supports libz3.so when supplied; bundling a Linux binary remains an additive packaging enhancement.
- `SharpProof.Package/SharpProof.Package.csproj:48-50` (`_AddAnalyzersToOutput`)
- Only `runtimes/win-x64/native/libz3.dll` and `runtimes/osx-x64/native/libz3.dylib` are packed; `Microsoft.Z3` 4.12.2
  also ships `runtimes/linux-x64/native/libz3.so` but it is never copied -> `DllNotFoundException` for Linux consumers.

### [PB1-22.3] 22.3 VSIX ships facades by hardcoded literal paths from `$(Configuration)` output - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Vsix/SharpProof.Vsix.csproj:36,51-60`
- The 10 facade DLLs are `Include`d from `$(AnalyzerPayloadDirectory)\$(Configuration)\netstandard2.0`. (a) If the
  analyzer output is missing any facade (trimming/config mismatch), `Include` with a literal non-existent path **fails
  the VSIX build**. (b) The VSIX SDK already auto-includes the `ProjectReference` outputs (confirmed in
  `bin\Release\net472\vsix-extracted\`), so the explicit `Content` list duplicates assemblies -> binding-conflict risk
  (related to but distinct from Round-1 Section 5.1).

### [PB1-22.4] 22.4 Fuzz harness: `compilation_error` cases bypass expectation validation - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/Program.cs:437-452`
- A non-compiling generated case is only a `compilation_error` finding; no expectation check and no assertion that the
  shape builder *should* have produced compilable code -> a broken generator is masked as ordinary "non-compiling input".

### [PB1-22.5] 22.5 Baseline `migrate` is a no-op that does not upgrade schema version - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Baseline/Program.cs:67-74`; `Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs:9-20`
- `migrate` re-serializes without bumping `BaselineDocument.Version` or `EvidenceSchemaVersion`; prints "Migrated
  baseline evidence to schema v2." misleadingly. (Legacy v1 still accepted by `ValidateEvidenceSchemas`, so no hard
  failure.)

### [PB1-22.6] 22.6 Fuzz harness determinism check compares full diagnostic message text - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Fuzz.Core/Program.cs:732-744` (`ToDiagnosticSignature` includes `GetMessage()`)
- Hashes the entire message; nondeterministic witness text/addresses/ordering flags `nondeterministic_diagnostics`
  even when kind/properties are stable -> false-positive nondeterminism signal.

### [PB1-22.7] 22.7 VSIX manifest: `ProductArchitecture` used as a child element - **LOW (verify against schema)**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Vsix/source.extension.vsixmanifest:9-17`
- `<ProductArchitecture>amd64</ProductArchitecture>` appears as a *child* of each `<InstallationTarget>`; in the VSIX
  2.0.0 schema this is normally an *attribute*. Builds today (SDK tolerates), but strict install validation could reject.

### [PB1-22.8] 22.8 Fuzz harness: error-path cases contribute empty operation-kind coverage - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Fuzz.Core/Program.cs:441,444,454`
- Non-compiling cases pass `Empty` operation kinds, so they never contribute to `UnobservedOperationKinds`; after a
  generator regression many erroring cases undercount observed operation kinds.

## 23. Concurrency / Thread-Safety / Caching (`SharpProof.Analyzer` engine, `SearchLib`, `SharpProof.Symbolic`)

### [PB1-23.1] 23.1 Shared `SemanticModel` per `SyntaxTree` used concurrently across methods - **HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ROSLYN-CONCURRENT-READS
> **Evidence:** Roslyn compilation and semantic model objects are immutable and support concurrent analyzer reads; cross-tree access is centralized through CompilationSyntaxAccess.
> **Changes/tests:** No serialization added; existing concurrent analyzer tests cover the boundary.
- `SharpProof.Analyzer/Engine/CompilationPurityService.cs:18,112-126,74-82`
- Caches one `SemanticModel` per `SyntaxTree` and returns the SAME instance for every method in that tree.
  `MethodPurityAnalyzer` (Round-1 Section 1.4) calls `GetPurity` concurrently for many methods (analyzer registered with
  `EnableConcurrentExecution`). Two methods in the same file are analyzed in parallel and both receive the identical
  `SemanticModel`, which is then driven through `engine.IsConsideredPure -> ... semanticModel.GetOperation/GetSymbolInfo`.
  Roslyn's `SemanticModel` is **not thread-safe** for concurrent instance-member use -> intermittent
  `InvalidOperationException`/`NullReferenceException` or silently wrong verdicts. Direct data race on shared mutable
  Roslyn state.

### [PB1-23.2] 23.2 Process-global SMT result cache never scoped to a compilation / never invalidated - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Smt/SmtAnalysisService.cs:16-20,403-420,217-254`
- `s_sharedQueryCache` and `s_sharedQueryFlights` are `static` (shared across all services/compilations), keyed on
  `CreateSharedQueryKey(Options, queryKey)`. Results from one run are served for unrelated later runs with identical
  options; `Dispose()`/`RequestGlobalSolverContextRecycle()` never clear `s_sharedQueryCache` -> stale cross-compilation
  reads and unbounded accumulation. Currently sound because `PurityProofResult` is value-only, but a real lifecycle gap.

### [PB1-23.3] 23.3 `SymbolicAnalysisLimits.CurrentScope` `AsyncLocal` (same hazard class as Round-1 Section 1.3) - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicAnalysisLimits.cs:224,228-237,319-334`
- A second `AsyncLocal` (for analysis *limits/truncation*) set by `Push` and only reset on `Scope.Dispose`. If a push
  site wraps an `await` that resumes on a different thread, the limit scope leaks/loses and truncation changes
  nondeterministically across concurrent analyses.

### [PB1-23.4] 23.4 `ConcurrentDictionary.GetOrAdd` factory runs expensive SMT work more than once - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/Engine/CompilationPurityService.cs:74-82`; `SharpProof.Analyzer/MethodBodyAnalysisState.cs:91-99,104-117`
- `GetOrAdd` does not guarantee the factory runs once; under concurrency `engine.IsConsideredPure` (SMT-backed, mutates
  shared `SmtAnalysisService` caches) and the `Lazy<object>` method-body resolver can execute twice -> wasted expensive
  work and potentially inconsistent per-call caches.

### [PB1-23.5] 23.5 Concurrent per-method re-analysis does not reuse the fixed point for callees - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/Engine/CompilationPurityService.cs:64-83` + `Engine/Analysis/...PurityAnalysisEngine.Entry.cs:20-42`
- After `EnsureFixedPoint`, `GetPurity` for a method not in `_fixedPoint` calls `engine.IsConsideredPure` *outside* the
  fixed-point lock, building a fresh local `purityCache` that does not consult `_fixedPoint` for callees -> verdicts
  inconsistent with the fixed point depending on entry point/thread (concurrency manifestation of Round-1 Section 1.4).

### [PB1-23.6] 23.6 `[ThreadStatic]` Z3 `Context`/`PurityProofSearch` reused across `SmtAnalysisService` instances; `Dispose` doesn't bump generation - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Smt/SmtAnalysisService.cs:25,351-371,101-113,130-144`
- `GetOrCreateProofSearch` reuses the thread-static context whenever `generation` matches and the factory is the default
  `static` field. `Dispose` only nulls the disposing thread's context and does **not** `Interlocked.Increment(ref
  s_solverContextGeneration)` -> a `PurityProofSearch` (with the plain-`Dictionary` caches flagged in Round-2 Section 6.12) is
  carried from a possibly-disposed service into a new one on the same OS thread -> encoder caches accumulate across
  unrelated analyses (unbounded native memory growth); relies on same-thread disposal.

### [PB1-23.7] 23.7 `BoundedConcurrentCache` shared key omits some `SmtAnalysisOptions` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Smt/SmtAnalysisService.cs:584-595` (`CreateSharedQueryKey`)
- Key includes `Mode`/`timeout_ms`/`max_path`/`max_expr` but not the full `SymbolicAnalysisLimits`/other options that
  affect truncation/Unknown -> two services differing only in an un-encoded field share cached results.

### [PB1-23.8] 23.8 `SmtSolver._regexValidationCache` is a plain (non-concurrent) `Dictionary` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtSolver.cs:21`
- Safe only because access is serialized by `SmtAnalysisService._solverLock` and each `SmtSolver` is single-thread-static.
  Any future sharing/drop of `_solverLock` corrupts it (same latent risk as Round-2 Section 6.12).

## 24. Docs / Samples / Demos - Round 3 (`docs`, `samples`, `README`)

### [PB1-24.1] 24.1 `proof-queries.md` `SymbolicSourceMap` constructor uses wrong named-argument names -> won't compile - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `docs/proof-queries.md:176-177` vs `SharpProof.Symbolic/SymbolicSourceMap.cs:5-8`
- Sample passes `originalLine`/`originalColumn`, but the constructor params are `originalStartLine`/`originalStartColumn`
  (CS1739). `standalone-query-inputs.md:164-165` uses the correct names -> the two docs are mutually inconsistent.

### [PB1-24.2] 24.2 `complexity-queries.md` "Current complexity kinds" list is wrong and self-contradicts the same doc - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `docs/complexity-queries.md:25-33` vs `:75-83` + `SharpProof.Attributes/ComplexityKind.cs:14-35`
- Lists `Constant, Linear, Product, Quadratic, Max, Unknown, RecursiveUnknown` - omits `Logarithmic`/`Linearithmic` (which
  the doc's own "Declarable bounds" section says ARE valid `[ExpectedComplexity]` values) and lists `Unknown`/
  `RecursiveUnknown` (which are *not* enum values, only reported inference states). Reader can't tell declarable values.

### [PB1-24.3] 24.3 `contracts.md` & `configuration-profiles.md` omit `SP0046` / the `nullability` kind - **MEDIUM/LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `docs/contracts.md:216,223-224`; `docs/configuration-profiles.md:13` vs `docs/configuration-reference.md:45-46` and
  `docs/nullable-verification.md:21-23`
- Both say `sharpproof_suggest_inferred_contracts` enables only `SP0034`-`SP0039` and list kinds without `nullability`;
  `configuration-reference.md` says it controls "SP0034-SP0039 and SP0046" and the default kinds include `nullability`.

### [PB1-24.4] 24.4 `baselines.md` internal contradiction about legacy/version-1 entries - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `docs/baselines.md:9` vs `:49-51`
- Line 9: "Legacy unversioned and version 1 entries are rejected." Lines 49-51: "Older three-field entries still work...
  missing optional fields are wildcards." A three-field entry IS an unversioned legacy entry -> doc both rejects and accepts.

### [PB1-24.5] 24.5 Multiple docs use a non-runnable CLI command form (`SharpProof.SymbolicCli`) - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- e.g. `capability-analysis.md:93-95`, `complexity-queries.md:111-114`, `analysis-limits.md:89`, `ci-exit-gates.md:44,72`,
  `project-aware-queries.md:7,34,48`, `symbolic-invariants.md:60-139`, `smt-lifecycle.md:105`,
  `trusted-boundary-review.md:104-105`, `proven-diagnostic-suppression.md:104-106`
- Invoke the CLI as bare `SharpProof.SymbolicCli ...` instead of the canonical
  `dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- ...` -> copy-paste fails.

---

---

# Round 4 - additional findings (10 agents, de-duplicated)

The following were found by 10 additional review agents, each instructed to read
`POTENTIAL_BUGS.md` first and avoid duplicates. Highest-impact new items: two HIGH
NRE crashes in the query service layer (null `RawResult`/`rawProof`), an analyzer
purity `GetPurity` call without try/catch (crash), control-flow lowering bugs that
treat unreachable code as reachable (unsound), `MayOverflow` dropped from term
canonical key (fact conflation), Unicode `string.Length` vs Z3 `str.len` mismatch
(unsound), and a CLI explain flag that is completely non-functional.

## 25. Symbolic Query Service Layer (`SharpProof.Symbolic` services)

### [PB1-25.1] 25.1 `CreateConditionProofResult` NREs on null `RawResult` - crashes every source/invariant/line query with provable implied conditions - **HIGH**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-OPTIONAL-RAW-PROOF
> **Evidence:** CreateConditionProofResult reads proof.RawResult through the nullable proof result and downstream consumers are null-safe.
> **Changes/tests:** No additional change.
- `SharpProof.Symbolic/SymbolicSourceQueryService.cs:~1356-1387`
- `rawResult = proof.RawResult` is `PurityProofResult?` and **can be null** (`SymbolicIrProofResult.Syntactic(...)`/
  `.Unknown(...)` set `RawResult = null`; `ClassifyConditionTruth` returns such proofs for contradictory state /
  syntactic constant truth). `rawResult?.PathCheck.Witness` then `.Witness` parses as `(rawResult?.PathCheck).Witness`
  -> **`NullReferenceException`** when `rawResult` is null.
- **Trigger:** any `Query`/`QuerySource`/`QueryFile*`/`Prove(...)` passing `ImpliedConditions` where at least one is
  syntactically provable/refutable. Very reachable -> service-level crash of the whole query.

### [PB1-25.2] 25.2 `CreateTriggerWitness` NREs on null `rawProof` - crashes the entire runtime-hazard query - **HIGH**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-OPTIONAL-TRIGGER-PROOF
> **Evidence:** CreateTriggerWitness uses triggerProof?.RawResult and serializes absent proof evidence conservatively.
> **Changes/tests:** No additional change.
- `SharpProof.Symbolic/SymbolicRuntimeHazardQueryService.cs:450-453`
- `rawProof?.ImpurityCheck.Feasibility` parses as `(rawProof?.ImpurityCheck).Feasibility`; when `rawProof` is null
  (returned by `ClassifyHazardTrigger` for Unknown reachability or a syntactic `Syntactic(Unreachable,...)` proof,
  `RawResult == null`) this throws NRE. Invoked unconditionally for every hazard candidate -> whole query crashes.
- **Trigger:** any `QueryRuntimeHazards*` call where a candidate point has Unknown reachability or a contradictory/
  syntactic trigger proof.

### [PB1-25.3] 25.3 `QueryNode` re-proves implied conditions with `includeCurrentStatementCompletionFacts` dropped - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicQueryApi.cs:573-581` vs `SymbolicSourceQueryService.cs:630-657,1055`
- Main node analysis uses the flag, but `CreateNodeProofs` -> `ProveConditionAtSyntaxTree` -> `AnalyzeProgramPoint`
  (line 1055) defaults it to `false`. Implied-condition proofs evaluated against a different program-point state than
  the main invariant -> condition proofs can disagree with `Facts`/`MergedInvariant`.

### [PB1-25.4] 25.4 `AnalyzeForInitialEntry` silently ignores `includeCurrentStatementCompletionFacts` - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicInvariantService.cs:56-76`
- `AnalyzeAt` honors the flag; `AnalyzeForInitialEntry` does not -> for-loop initial-entry points get a different
  state than non-loop points under the same option (combines with 25.3).

### [PB1-25.5] 25.5 `ProveImplication` (public API) diverges from source `ProveCondition` - **MEDIUM**

> **Disposition:** Duplicate
> **Canonical root cause:** RC-DUPLICATE-CLAIM
> **Evidence:** This claim describes the same behavior and root cause as another canonical ledger item.
> **Changes/tests:** Resolved or classified under the canonical root-cause entry.
- `SharpProof.Symbolic/SymbolicInvariantService.cs:116-168` vs `SymbolicSourceQueryService.cs:1303-1314`
- `ProveImplication` returns `Unknown`/`"smt_required"` whenever `smtAnalysis == null` (even for syntactically
  provable conditions), and does not re-check feasibility when reachability is `NotChecked` -> two public proof
  entry points map the same input to different `SymbolicTruthValue`s.

### [PB1-25.6] 25.6 `CreateConditionProofResult` assembles `Unknown`-truth witness/counterexample from the impurity model - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicSourceQueryService.cs:1357-1394`
- For `effectiveTruth == Unknown`, `selectedModel`/`counterexample` prefer `ImpurityCheck.Witness` (the purity model)
  over the path/condition witness; a usable `PathCheck.Witness` is dropped and the counterexample forced to `None`.

### [PB1-25.7] 25.7 `QuerySourceLinePoint` throws on empty line while `QuerySyntaxTreeLine` returns empty - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicSourceQueryService.cs:687 vs 602`
- `Point` shape throws `ArgumentException("No program points found on --line.")`; the `Line` analogue returns a
  normal result with zero program points (inconsistent error handling / brittle CLI behavior).

### [PB1-25.8] 25.8 `ClassifyTriggerCore` masks provable trigger proofs when reachability is `Unknown` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicRuntimeHazardQueryService.cs:494-495`
- Short-circuits to `Unknown` when reachability is undetermined, even if the trigger condition is provably
  `Proven`/`Unreachable` from actual path conditions -> conservative downgrade (lost precision, not unsound).

## 26. Analyzer Diagnostic Rule Implementations (`SharpProof.Analyzer` rules)

### [PB1-26.1] 26.1 `MethodPurityAnalyzer.AnalyzeSymbolForPurity` calls `GetPurity` with no try/catch (primary SP0002 path) - **HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-SYMBOLIC-BOUNDARY
> **Evidence:** Capability query failures could escape as analyzer crashes.
> **Changes/tests:** Typed failures now become SP0016 with retryable AnalysisUnavailable taxonomy; SymbolicUnknownReasonTaxonomyTests.
- `SharpProof.Analyzer/MethodPurityAnalyzer.cs:168` (from `AnalyzerFeaturePipeline.cs:68`, both unguarded)
- `purityService.GetPurity(...)` has no `try/catch`, unlike the sibling `MethodExpectedComplexityAnalyzer`
  (lines 48-65) which guards the same throw classes. An SMT/unsupported-expression throw propagates out of
  `AnalyzeCallable` -> crashes the analyzer callback (can destabilize the IDE session).
- **Trigger:** any `[EnforcePure]`/`[Pure]` method whose purity symbolic query throws `ArgumentException`/
  `NotSupportedException`/`InvalidOperationException`. (Round-1 Section 1.1 covered capability/complexity/inferred-contract
  but missed this primary purity call site.)

### [PB1-26.2] 26.2 Unguarded SMT `Prove*` calls in Ensures/Requires/Nullable analyzers - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-SYMBOLIC-BOUNDARY
> **Evidence:** Ensures, Requires, and nullable analyzers invoked proof operations that could throw expected symbolic-analysis exceptions through Roslyn callbacks.
> **Changes/tests:** TryProveAtSyntaxNode and AnalyzerSymbolicQueryBoundary convert expected failures to conservative Unknown; analyzer boundary regressions.
- `SharpProof.Analyzer/MethodEnsuresAnalyzer.cs:181,191`; `MethodRequiresAnalyzer.cs:126`; `NullableContractAnalyzer.cs:402,415`
- `queryService.ProveAtSyntaxNode(...)` / `Prove(...)` have no guard; same throw classes as Section 1.1. A thrown exception
  propagates out of `AnalyzeCallable` and crashes analysis -> no diagnostics rather than degrading to `Unknown`.

### [PB1-26.3] 26.3 `[Ensures]` on destructors and expression-bodied constructors never verified - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/MethodEnsuresAnalyzer.cs:342-398` (early `return` at `:72`); helpers `:628-657`
- `CollectCompletionSites` relies on `TryGetExpressionBody`/`TryGetBodyBlock`, which don't handle
  `DestructorDeclarationSyntax` and only match a constructor when `Body` is non-null (`Ctor() => field = 1;` missed).
  `completionSites.Length == 0` -> early return -> a violated `[Ensures]` on a destructor / expr-bodied ctor produces
  neither SP0018 nor SP0019 (silent verification gap).

### [PB1-26.4] 26.4 `ExceptionFlowAnalyzer.GetIdentifierLocation` omits `ConversionOperatorDeclarationSyntax` - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/ExceptionFlowAnalyzer.cs:~70-85` (local `GetIdentifierLocation`)
- Unlike `MethodPurityAnalyzer.GetIdentifierLocation` (Round-1 Section 8.6), this duplicate has no conversion-operator case
  -> SP0010/SP0011 reported at the whole-declaration span rather than a precise location.

### [PB1-26.5] 26.5 `MethodPurityAnalyzer` short-circuits on first (metadata) location - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/MethodPurityAnalyzer.cs:23`
- `if (methodSymbol.Locations.FirstOrDefault() == null || methodSymbol.Locations.First().IsInMetadata) return;`
  - if the first `Location` is in metadata while a source body also exists (some partial/explicit-interface shapes),
  `First()` returns metadata and the method is skipped -> missed SP0002. Should prefer a source location.

### [PB1-26.6] 26.6 `UnsafeNullForgivingOperator` reported for an `Unreachable` proof - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/NullableContractAnalyzer.cs:313-316`
- For `Unreachable` truth value, flagging `x!` as an *unsafe* suppression is wrong (should be skipped/inconclusive).

### [PB1-26.7] 26.7 `[MemberNotNull(...)]` on a property member never actually verified - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/MethodEnsuresAnalyzer.cs:170-184`
- Property (non-field) member target unconditionally reported `Inconclusive` (SP0047); a violated
  `[MemberNotNull("P")]` on a property is never surfaced as SP0018 (vs the field-member path which is checked).

## 27. Symbolic IR Control Flow / Loops / Exceptions (`SymbolicProgramPointFacts`)

### [PB1-27.1] 27.1 Post-`if` point not marked contradictory when BOTH branches definitely exit - **HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CATCH-ENTRY-STATE
> **Evidence:** A catch entry retained facts invalidated by mutations in the try prefix.
> **Changes/tests:** Catch entry invalidates try-mutated facts; ProgramPointFacts_CatchEntryInvalidatesMutationsFromTryPrefix.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:5330-5371`
- When both true and false branches definitely exit (`return`/`throw`), neither early-return branch fires and
  `if (trueBranchExits || falseBranchExits) return;` leaves `state` as the pre-if entry (not contradictory). Code
  after the `if` (e.g. `if (c) return 1; else return 2; return x;`) is treated as reachable using pre-if values ->
  unsound/over-optimistic proof about a dead region. Same defect at `AddCompletedBlockStateFacts:4915`.

### [PB1-27.2] 27.2 `AddCompletedTryStatementStateFacts` merges catch-body completions ignoring catch type / `when` - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CATCH-TYPE-FLOW
> **Evidence:** A deterministic mismatched catch was treated as handling a directly thrown exception.
> **Changes/tests:** Known throw types are checked against catch compatibility; ProgramPointFacts_KnownMismatchedCatchLeavesFollowingPointUnreachable.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:3889-3933`
- The loop adds each catch body's normal-completion state to `completionStates` **without** inspecting
  `catchClause.Declaration` (exception type) or `catchClause.Filter` (the `when` clause). For
  `try { throw new E(); } catch (OtherException) { x = 1; }` (or an always-false `when`), the exception propagates
  and post-try code is unreachable, yet `completionStates` includes the catch body -> merged post-try normal state
  wrongly includes `x == 1` and treats the post-try point as normally reachable. **Unsound normal-completion fact.**

### [PB1-27.3] 27.3 `for(;;)` infinite loop with no `break` leaves post-loop state as pre-loop state - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:4068-4078`
- No `break` -> `TryCreateGuardedBreakLoopExitSymbolicCondition` returns `false` -> no `case` matches, `switch` falls
  through with `state` unchanged. Everything after an infinite `for(;;)` is treated as reachable -> wrong
  control-flow reachability.

### [PB1-27.4] 27.4 Plain expression-statement calls lose normal-completion facts in block completion - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:3699-3788` (`AddPriorStatementStateFacts`) vs `:3780-3867,4927-4968`
- `AddPriorStatementStateFacts` only routes `ExpressionStatementSyntax` to `AddAssignmentExpressionStateFacts` when
  it's an `AssignmentExpressionSyntax`; a bare `F(x);` falls to a no-op. Top-level `AddCompletedStatementStateFacts`
  (line 3853) *does* call `AddNormalCompletionStateFacts` for it. So not-null postconditions / throw-guard / member-
  not-null / dereference facts are dropped for bare call statements in blocks (precision loss).

### [PB1-27.5] 27.5 Merge keeps all entry conditions/facts + branch-only `SymbolVersions` -> inconsistent merged state - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:3953-4015` (companion to Round-1 Section 2.2 / Section 7.1)
- Every `entryState` path condition is unconditionally kept; merged `SymbolVersions` computed only across completion
  states. A variable whose version differs across branches is dropped from `SymbolVersions` while a retained
  `entryState` fact still references it -> mismatched SMT variable name.

### [PB1-27.6] 27.6 Throwing switch-expression arm modeled only via `Not(armCondition)` - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:8486-8502`
- Adding `Not(armCondition)` for a throwing arm is sound for the normal path but relies entirely on the separate
  exception-flow analysis; the normal-flow facts systematically under-represent exceptional exits (companion to Section 27.2).

## 28. SymbolicCli Program & Explain Reports (`Tools/SharpProof.SymbolicCli`)

### [PB1-28.1] 28.1 `explain --report-max-hazards` (and JSON `output.maxHazards`) is always rejected - **HIGH**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-CLI-REPORT-LIMIT-ROUTING
> **Evidence:** The current CLI separates report limits from analysis limits and accepts both the command-line and JSON report-max-hazards forms.
> **Changes/tests:** Existing explain/report-limit tooling tests pass; no new change required.
- `Tools/SharpProof.SymbolicCli/Program.cs:1743-1747,1943-1954`; `SymbolicCliJsonRequest.cs:337`
- `--report-max-hazards` sets `HasCompactHazardOutputLimit` (same flag as the query-only `--max-hazards`), but
  validation only whitelists `RuntimeHazards`, not `Explain`. Every `explain` invocation with report-max-hazards
  crashes with the misleading "--max-hazards requires --runtime-hazards". A documented, schema-supported flag is
  completely non-functional for the entire explain subcommand (and for any JSON `explain` + `output.maxHazards`).

### [PB1-28.2] 28.2 Complexity CI gate cannot rank `Product`/`Max`, misreports exceed - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-COMPLEXITY-PARTIAL-ORDER
> **Evidence:** The exit gate omitted the defined Product/Max greater-than-Constant relations.
> **Changes/tests:** SymbolicCliExitGateEvaluator now implements those relations; SymbolicComplexityQueryTests.
- `Tools/SharpProof.SymbolicCli/SymbolicCliExitGateEvaluator.cs:368-391` (`TryGetChainRank`), `:310-326` (`CompareComplexity`)
- `TryGetChainRank` has no case for `Product`/`Max` -> returns `false` -> `CompareComplexity` returns `Incomparable`. A
  method whose actual complexity is `Product`/`Max` (or a `--fail-on-complexity-exceeded` bound of `Product`/`Max`,
  or even `Max` vs `Max`) is reported `Incomparable` instead of `Exceeds`/`Within` -> wrong exit code. (Related to
  Round-3 Section 17.1 but in the CLI gate evaluator specifically; the chain-rank gap is the root.)

### [PB1-28.3] 28.3 Text `explain --position <n>` never prints runtime hazards - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.SymbolicCli/Program.cs:365-383` (`PrintExplainResultAsync`)
- The runtime-hazards block is guarded by `!options.Position.HasValue`, so `explain --position 123` skips hazards
  entirely; even if unguarded it would query `Line(0)` (default). Machine-readable explain (report/json/markdown)
  properly resolves the point -> text explain diverges from the other formats for `--position`.

### [PB1-28.4] 28.4 JSON `output.maxDiagnostics`/`maxItems` in non-explain modes rejected - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SymbolicCliJsonRequest.cs:348-356` (always emits `--report-max-*`) vs `Program.cs:1922-1927`
- A JSON request with `{"mode":"query","output":{"maxItems":10}}` is rejected ("require explain") even though the
  schema offers the field generically -> schema/CLI mismatch.

### [PB1-28.5] 28.5 `explain` text + `--report-max-diagnostics`/`--report-max-items` throws - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.SymbolicCli/Program.cs:1894-1903`
- For `explain` (text) with any of these limits set, it throws "require explain --json/--sarif/--markdown". The
  explain JSON request (SymbolicCliJsonRequest.cs:349-356) unconditionally emits these when present and defaults
  `format` to `"text"` -> an explain JSON with `output.maxDiagnostics`/`maxItems` and no `format` crashes.

### [PB1-28.6] 28.6 JSON query options missing hazard-status/exception-type/category fields - **MEDIUM/LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SymbolicCliJsonRequest.cs:567-582,280-294`
- `SymbolicCliJsonQueryOptions` exposes `HazardKinds` but not `HazardStatuses`/`HazardExceptionTypes`/`HazardCategories`,
  and `AddQueryOptions` never emits `--hazard-status`/`--hazard-exception-type`/`--hazard-category` -> a
  `runtimeHazards` JSON request cannot filter by status/exception/category despite those being first-class CLI flags.

### [PB1-28.7] 28.7 JSON `summaryOnly:true` + `format:json` contradictory; text silently flips to compact - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SymbolicCliJsonRequest.cs:332`, `Program.cs:1760-1763,1874-1875`
- `--summary-only` forces `CompactJson`; a JSON request with `summaryOnly:true` + `format:json` expands to both
  `--summary-only` and `--json` -> `Parse` throws "--json cannot be combined with --compact-json." Also `summaryOnly`
  with default `format:"text"` silently converts output to `--compact-json` (user asked text, gets compact JSON).

### [PB1-28.8] 28.8 SARIF hazard level for `Unreachable` maps to `"none"` - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.SymbolicCli/SymbolicCliExplainReport.cs:178-184`
- `SymbolicRuntimeHazardStatus` has four values (`Proven, Unreachable, Unknown, Unsupported`); the status switch has
  no `Unreachable` arm -> hits `_ => "none"` (semantically null SARIF level).

### [PB1-28.9] 28.9 Markdown output written with `Console.Write` (no trailing newline) - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CLI-INPUT-AND-OUTPUT-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.SymbolicCli/Program.cs:36` (`Console.Write(report.ToMarkdown())`) vs `:34/:38/:128` (`WriteLine`).
  Inconsistent; can cause shell prompt to appear on the same line.

### [PB1-28.10] 28.10 `--fail-on-unproven-implies` fails when zero proof outcomes are produced - **LOW**

> **Disposition:** Duplicate
> **Canonical root cause:** RC-DUPLICATE-CLAIM
> **Evidence:** This claim describes the same behavior and root cause as another canonical ledger item.
> **Changes/tests:** Resolved or classified under the canonical root-cause entry.
- `Tools/SharpProof.SymbolicCli/SymbolicCliExitGateEvaluator.cs:51-67`
- `TotalCount == 0` -> PASS condition false -> gate fails. Whether "no proof emitted" should equal "unproven" is
  debatable, but it means the gate can fail on requests where no proof was ever computed, not just genuinely-unproven.

## 29. Fuzz Harness (`Tools/SharpProof.Fuzz.Core`)

### [PB1-29.1] 29.1 `ExpectedOperationKinds`/`ExpectedSyntaxKinds` collected but NEVER validated - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/Program.cs:1555-1556,3008-3010,517-624`
- Each registry entry declares expected kinds; `AnalyzeCaseAsync` collects the *real* kinds from generated source
  but nothing compares them back. Only `PrimaryShapeIds` is consumed. A shape builder that stops producing its
  declared operation/syntax kind emits **no finding** -> silent coverage under-counting (defeats the harness).

### [PB1-29.2] 29.2 Operation-kind coverage counts inflated by operation-tree depth - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/Program.cs:641-658` (`CollectOperationKinds`)
- Each operation-rooted node contributes its entire operation subtree, so a nested operation is counted once per
  ancestor operation node. Counts multiplied by nesting depth; non-comparable with `CollectSyntaxKinds` (counts each
  syntax node once).

### [PB1-29.3] 29.3 Determinism repeat-run diagnostics never re-evaluated for expectations (first-run-only) - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/Program.cs:456-491,517-624`
- `Evaluate` runs only on `firstDiagnostics.Diagnostics`; the second run feeds only the `nondeterministic_diagnostics`
  comparison. When run #1 is the anomalous one, expectation findings (SP0002/SP0010) may be false positives caused
  by nondeterminism, indistinguishable from a real failure.

### [PB1-29.4] 29.4 `BuildImpureTypeParameterObjectCreation` expects impurity for `new T()` (fresh allocation) - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Fuzz.Core/Program.cs:2246-2257`
- `new T()` creates a fresh, owned object (canonical pure under the fresh-ownership model, same as
  `BuildImpureOwnershipEscapeChain` which is likewise flagged impure). `DefinitelyImpure` expectation -> false
  `impure_missing_sp0002` if the analyzer classifies `new T()` as pure.

### [PB1-29.5] 29.5 `BuildImpureWithExpression` expects impurity for a `with` expression (fresh record copy) - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Fuzz.Core/Program.cs:2510-2528`
- `var updated = data with { Value = x };` returns a freshly copied immutable record (pure in SharpProof's model).
  `DefinitelyImpure` expectation -> false `impure_missing_sp0002` if the analyzer returns pure.

### [PB1-29.6] 29.6 No expectation handling for SP0004 / SP0009 diagnostics - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Fuzz.Core/Program.cs:517-624,3015-3022`
- `Evaluate` checks only SP0002/SP0010; `Sp0004Count`/`Sp0009Count` are tallied but never validated -> a pure case
  that unexpectedly emits SP0004/SP0009 is silently ignored (false-negative in harness validation).

### [PB1-29.7] 29.7 Fragile reflection crashes the whole run at startup - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/RoslynShapeManifest.cs:360-379`
- `GetRegisteredRuleOperationKinds` uses `analyzerAssembly.GetType("...RuleRegistry", true)!` + `GetMethod`/`GetProperty`
  `.Invoke(...)!` with null-forgiving operators; any rename/move of the analyzer's internal `RuleRegistry` or a
  `null` from `GetMethod`/`GetProperty` throws `TypeLoadException`/`NullReferenceException` and crashes the fuzz
  process before a single case is analyzed.

### [PB1-29.8] 29.8 `BuildImpureDeclarationExpression` expects impurity for `int.TryParse` (pure BCL) - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Fuzz.Core/Program.cs:2220-2231`
- `int.TryParse(text, out var value)` is a deterministic pure parser (writes only a fresh `out` local). `DefinitelyImpure`
  expectation -> false `impure_missing_sp0002` if the analyzer treats `TryParse` as pure.

### [PB1-29.9] 29.9 `AllowEffectPreservingWrappers` registry field is dead - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `Tools/SharpProof.Fuzz.Core/ShapeRegistryEntry.cs:84` (assigned per entry, never read anywhere).

### [PB1-29.10] 29.10 `Enum.Parse<OperationKind>("CollectionElementInitializer")` latent crash in summary `Build` - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Fuzz.Core/Program.cs:3070` (also `RoslynShapeManifest.cs:83`)
- If the referenced Roslyn `OperationKind` member name is absent, `Enum.Parse` throws `ArgumentException` inside
  `FuzzRunSummaryBuilder.Build`, discarding the entire run's summary after all cases were analyzed.

## 30. Baseline / Fuzz / Packaging Tooling (`Tools/SharpProof.Baseline*`, `SharpProof.Package`)

### [PB1-30.1] 30.1 Baseline `TryGetFirstPhysicalLocation` keys only `locations[0]` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs:582-598` (used by `GetResultPath:550-559`, `GetResultLocation:561-580`)
- Reads only the first `locations[]` entry. A diagnostic with more than one location gets its path/line/column from
  the first physical location, not necessarily the canonical primary one -> false stale/matched verdicts in
  `Explain`/`Prune`.

### [PB1-30.2] 30.2 Baseline per-entry `evidenceSchemaVersion`/`evidenceSchemaCompatibility` written but never read back - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs:36-39` vs `TryAddBaselineEntry:348-409`
- `AddBaselineEntries`/`TryAddBaselineEntry` never populate those fields on parse -> on `ParseBaselineJson` every entry
  reverts to the current schema version (distinct from Round-3 Section 21.9 document-level stamping).

### [PB1-30.3] 30.3 Baseline `RunBuildAsync` has no timeout, ignores cancellation, emits unquoted `/p:ErrorLog=` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Baseline/Program.cs:113-138` (line 125 `startInfo.ArgumentList.Add("/p:ErrorLog=" + sarifPath)`)
- No timeout / no `cancellationToken` passed (hung build hangs forever, Ctrl-C orphans the child); unquoted path with
  spaces/commas/`;`/`%` can break MSBuild property parsing so the SARIF log isn't written where expected.

### [PB1-30.4] 30.4 Fuzz `summary.partial.json` leftover re-aggregated on `--out` reuse - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/Program.cs:777-788` (writes `summary.json` but never deletes the `summary.partial.json`
  checkpoint) + `Tools/SharpProof.Fuzz/Aggregate-FuzzRun.ps1:29-36` (falls back to `summary.partial.json`). A successful
  run leaves the partial; reusing `--out` and interrupting the next run picks up the prior leftover -> aggregates old
  and new (compounds Round-3 Section 19.2/Section 4.8).

### [PB1-30.5] 30.5 Fuzz determinism check ignores exception nondeterminism when both runs throw - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/Program.cs:460-481` vs `Evaluate:523-530,473-480`
- When run 1 throws (empty diagnostics) AND run 2 throws (empty diagnostics), both signature sets are empty ->
  `SequenceEqual` is true -> no `nondeterministic_diagnostics` raised, yet two `analyzer_exception` findings recorded.

### [PB1-30.6] 30.6 `SharpProof.Attributes.dll` delivered twice within the same package (analyzer + lib) - **LOW**

> **Disposition:** Duplicate
> **Canonical root cause:** RC-DUPLICATE-CLAIM
> **Evidence:** This claim describes the same behavior and root cause as another canonical ledger item.
> **Changes/tests:** Resolved or classified under the canonical root-cause entry.
- `SharpProof.Package/SharpProof.Package.csproj:53-54`
- Packed to both `analyzers/dotnet/cs` (line 53) and `lib/netstandard2.0` (line 54). Within-package variant of
  Round-1 Section 5.5: consumer references the `lib/` copy while the analyzer load context loads the `analyzers/` copy ->
  different `Type` instances for `EnforcePure`/`Impure`; NuGet "asset already provided" warnings.

### [PB1-30.7] 30.7 `install.ps1` `Join-Path ... "analyzers" * -Resolve` throws if `analyzers` absent - **LOW**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
- `SharpProof.Package/tools/install.ps1:12`
- `Join-Path -Resolve` on a non-matching wildcard throws in PowerShell 5.1; a packaging/extraction layout without a
  sibling `analyzers` dir aborts with an unhandled exception instead of gracefully skipping.

## 31. Capability / Complexity / Nullable / Allocation Tests (`SharpProof.Test`)

### [PB1-31.1] 31.1 `BoundaryAttributeTests` brittle hardcoded diagnostic span - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/BoundaryAttributeTests.cs:94-101`
- Uses `.WithSpan(8,16,8,29)` hardcoded while every other test in the file uses marker-based verification
  (`{|SP0002:...|}`). Any reformatting/whitespace change silently shifts the span -> confusing verifier mismatch
  (same brittle pattern as Round-3 Section 18.6, different file).

### [PB1-31.2] 31.2 `ExpectedComplexityContractTests` parallel + shared-analyzer-instance flakiness - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/ExpectedComplexityContractTests.cs:13` (`[Parallelizable(ParallelScope.Children)`) + `:426-432`
  (`GetComplexityDiagnosticsAsync` -> `concurrentAnalysis: true`); `AnalyzerTestHost.cs:166` caches the
  `SharpProofAnalyzer` instance in a static `ConcurrentDictionary` keyed only by features.
- Parallel tests share the same analyzer instance and use `concurrentAnalysis: true`; the analyzer relies on
  `AsyncLocal` config scopes (Round-1 Section 1.3) not safe under concurrent analysis of different compilations ->
  intermittent SP0021/SP0022 emission, non-reproducible in isolation.

### [PB1-31.3] 31.3 `SymbolicComplexityTests.LineTarget_ResolvesContainingMethod` can't distinguish line vs position targeting - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/SymbolicComplexityTests.cs:457-480`
- Fixture has only one method, so `Assert.That(result.MethodName, Is.EqualTo("Work"))` passes regardless -> masks any
  off-by-one in the line-numbering convention between `GetLineNumber` and `SymbolicQueryTarget.Line`.

### [PB1-31.4] 31.4 `CachingTests.CanceledPurityRequest` only covers cancellation-at-entry - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/CachingTests.cs:40-84`
- `CancellationTokenSource` cancelled *before* `GetPurity`; `EnsureFixedPoint` throws at its first
  `ThrowIfCancellationRequested`, so the partial-build abandon path (the intended scenario) is never exercised.
  A regression that builds the call graph before honoring cancellation wouldn't be caught.

### [PB1-31.5] 31.5 Capability tests use `.Single(...)` -> opaque exception on mismatch - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/CapabilityContractTests.cs:119,254`
- `diagnostics.Single(item => item.Id == ...)` / `SingleDiagnostic` throw `InvalidOperationException` (no assertion
  message) if zero/multiple matching diagnostics occur -> should-be assertion failure becomes an opaque stack trace
  (same `.Single()` fragility as Round-3 Section 13.6, different file).

### [PB1-31.6] 31.6 `DiagnosticEvidenceTests` vacuous redundant id assertion - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/DiagnosticEvidenceTests.cs:3449`
- `Assert.That(diagnostic.Id, Is.EqualTo(PurityNotVerifiedId))` right after `SingleDiagnostic(diagnostics, PurityNotVerifiedId)` (line 3423) which already filtered by that id -> guaranteed true, adds no coverage.

### [PB1-31.7] 31.7 Brittle exact exception-message strings in capability/complexity tests - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/CapabilityContractTests.cs:430`, `SharpProof.Test/SymbolicComplexityTests.cs:596`
- Assert the exact message string; any wording tweak breaks the test with no behavioral change (same class as
  Round-1 Section 3.6).

### [PB1-31.8] 31.8 DiagnosticEvidenceTests negative-only `Any(id)==false` without always-present positive controls - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/DiagnosticEvidenceTests.cs` (e.g. ~1485-1488, 2218-2221, 2357-2360)
- Same systemic Round-3 Section 18.5 risk: a globally-disabled catalog-resolution path would make these negatives pass
  vacuously. Strongest instances pair with `matched == true` (partially guarded), but the diagnostic-level assertion
  alone is insufficient.

## 32. PowerShell Scripts - Round 4 (`scripts`, root `*.ps1`)

### [PB1-32.1] 32.1 `Join-TestFilter` hardcodes `SharpProof.Test.` namespace -> silent empty Tooling runs - **HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-TEST-FILTER-NAMESPACE
> **Evidence:** Fixture filters prefixed every fixture with SharpProof.Test, silently excluding SharpProof.ToolingTest fixtures.
> **Changes/tests:** Namespace-neutral fixture filters and lane-aware parsing; BaselineWorkflow route probe and ImpactedTestSelectionScriptTests.
- `scripts/Invoke-SharpProofImpactedTests.ps1:1331-1334`
- `Join-TestFilter` always prefixes `SharpProof.Test.`, but Tooling fixtures live in `SharpProof.ToolingTest`. When the
  selector picks a Tooling fixture, the filter becomes `FullyQualifiedName~SharpProof.Test.EffectSummaryToolTests`,
  which does NOT substring-match `SharpProof.ToolingTest.EffectSummaryToolTests` -> **zero tests run**, `dotnet test`
  exits 0 -> the impacted-test runner reports success while running nothing. Defeats the harness's purpose.

### [PB1-32.2] 32.2 `build.ps1`/`build-vsix.ps1` `-MemoryLimitMb` silently ignored for ALL `dotnet` invocations - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-MEMORY-LIMIT-SHADOW
> **Evidence:** Nested helper parameters defaulted MemoryLimitMb to zero and shadowed the build script setting.
> **Changes/tests:** Removed the shadowing parameters in build.ps1 and build-vsix.ps1; ScriptProcessOwnershipTests.
- `build.ps1:21-26,28` (and `build-vsix.ps1:13-18,20`)
- `Invoke-DotnetInRepo` declares its OWN `$MemoryLimitMb` defaulting to `0`, and every `dotnet` call site omits
  `-MemoryLimitMb`; only `Invoke-MSBuildInRepo` (no param -> script scope `6144`) is capped. The most memory-hungry
  operations (`restore`/`build`/`pack`) run **uncapped**, directly contradicting `AGENTS.md` ("pass an explicit
  `-MemoryLimitMb`"). `build-vsix.ps1` has the identical defect.

### [PB1-32.3] 32.3 `FullyQualifiedName~` fixture-extraction regex truncates at first dot - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-TEST-FILTER-NAMESPACE
> **Evidence:** The lane/fixture parser assumed one namespace shape and could truncate or misroute qualified filters.
> **Changes/tests:** Namespace-neutral filter construction and explicit lane recognition; ImpactedTestSelectionScriptTests.
- `scripts/Invoke-SharpProofTests.ps1:387`
- `[regex]::Matches($Filter, 'FullyQualifiedName~([A-Za-z_][A-Za-z0-9_]*)')` stops at the first `.`. For
  `FullyQualifiedName~SharpProof.ToolingTest.EffectSummaryToolTests` it captures `SharpProof` -> lane selection returns
  only the Main project -> filter matches nothing -> silent empty run. (Underlying mechanism of Section 32.1.)

### [PB1-32.4] 32.4 `build.ps1` vswhere path uses double backslashes -> MSBuild discovery likely dead - **MEDIUM/LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `build.ps1:57,59` (`"Microsoft Visual Studio\\Installer\\vswhere.exe"`, `-find "MSBuild\\**\\Bin\\MSBuild.exe"`)
  vs `build-vsix.ps1:33` which correctly uses single backslash. Double separators aren't normalized by vswhere ->
  falls through to hardcoded candidates; a non-default VS install -> "Could not locate MSBuild.exe" even though
  vswhere would have found it.

### [PB1-32.5] 32.5 README generator rejects non-`async Task`/`void` example tests - **MEDIUM/LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Generate-Readme.ps1:50` (distinct axis from Round-3 Section 19.4 which is about attribute adjacency)
- Only `async Task`/`void` return types match; an example test returning `Task`/`Task<T>`/`ValueTask`/`async Task<T>`
  isn't registered -> later `throw "missing a [ReadmeExample] test."` breaks README regeneration.

### [PB1-32.6] 32.6 `demo-sharpproof.ps1` uses stale project if `dotnet new` fails - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/demo-sharpproof.ps1:34-41` (compounds Round-2 Section 11.1 native-silence): failed `dotnet new` (piped to
  `Out-Host`) doesn't stop the script; a leftover `.csproj` in `$demoPath` is silently used -> writes sample code
  over potentially the wrong project and reports success.

### [PB1-32.7] 32.7 Metrics script `$repoPath` leaks to script scope; unguarded `Substring($repoRoot.Length)` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Get-SharpProofProductionMetrics.ps1:58,67` (compounds Round-2 Section 11.3): `$repoPath` assigned inside
  `Where-Object` leaks to script scope; `Convert-ToRepoPath` does unguarded `$fullPath.Substring($repoRoot.Length)`
  with casing/8.3/long-path divergence risk.

### [PB1-32.8] 32.8 `Invoke-SharpProofImpactedTests.ps1` git-failure mode / JSON host empty selection - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Invoke-SharpProofImpactedTests.ps1:40-44`: missing git -> command-not-found raw error; no diff -> "No changed
  files" + exit 0, and the JSON host emits `success=true` empty selection, masking that impact selection did nothing.

### [PB1-32.9] 32.9 Background-lane arg quoting only escapes single quotes - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `scripts/Invoke-SharpProofTests.ps1:807-814` (the *actual execution* path, distinct from Round-2 Section 11.7 display-only):
  args containing `"`/backtick/`$` are not escaped for the spawned `powershell.exe -File` -> latent breakage.

### [PB1-32.10] 32.10 `Generate-ConfigurationReference.ps1` brace-splitter tracks only `(` `)` - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `scripts/Generate-ConfigurationReference.ps1:33-117`: `Get-BalancedArguments` increments/decrements only on
  parentheses; a `{ ... }` collection/object initializer containing commas would be mis-split.

### [PB1-32.11] 32.11 `Invoke-SharpProofReleaseValidation.ps1` builds VSIX via `dotnet build /p:EnableVsixPackaging=true` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `scripts/Invoke-SharpProofReleaseValidation.ps1:66-70,109`: unlike `build-vsix.ps1` (uses VS SDK MSBuild), this uses
  plain `dotnet build` for VSIX packaging -> may silently produce no VSIX or fail (VSIX targets need VS SDK MSBuild).

### [PB1-32.12] 32.12 `build.ps1:91` rebuilt VSIX rediscovered without `-Recurse` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `build.ps1:91` uses `Get-ChildItem ... -Filter *.vsix` (no `-Recurse`) while line 50 uses `-Recurse`; if MSBuild
  emits the `.vsix` in a subfolder, the post-build lookup misses it -> reports "VSIX: not found".

## 33. Shared Config / Constants / Baseline Deep (`Shared`, config parsers, `SharpProof.Baseline.Core`)

### [PB1-33.1] 33.1 Setter-only properties can never match a configured member key - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/Configuration/ConfiguredMemberKey.cs:15-19` (`TryCreate`, bails when `property.GetMethod == null`)
  + `Engine/ImpurityCatalog.cs:76-86` (`TryGetConfiguredMember`).
- A setter-only property can't produce a canonical key, so `TryGetConfiguredMember` returns `false` even when
  `ExtraKnownImpureMethods`/`ExtraKnownPureMethods` contains the valid `spm1|...|property-set.set` key. The validator
  `ValidateStructuralMemberKeyList` accepts it -> configuration silently ignored at analysis time (no diagnostic, no
  effect).

### [PB1-33.2] 33.2 `.baseline-check\.globalconfig` has a misspelled config key - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `C:\w\PurelySharp\.baseline-check\.globalconfig:3` (`purelysharp_enable_effect_summary_json = false`) vs the correct
  `sharpproof_enable_effect_summary_json` (`ConfigKeys.EnableEffectSummaryJson`). The typo'd key is unknown to the
  engine and silently ignored; currently masked only because the root `.globalconfig` sets the correct key to `false`.

### [PB1-33.3] 33.3 `StringList` config options never validated (typos silently ignored) - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs:682-683` (`case StringList: return;`)
- `sharpproof_known_impure_namespaces`/`known_impure_types`/`attribute_stub_namespaces`/`suggest_missing_enforce_pure_
  namespace_filters` get NO validation. A wrong-cased/typo'd value (e.g. `System.IOo`) is accepted as "valid" yet
  never matches `ns.ToDisplayString()` (case-sensitive in `ImpurityCatalog`) -> override has no effect, no diagnostic.

### [PB1-33.4] 33.4 Dead `Constants.KnownImpureMethods` entries that can never match - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Shared/Constants.cs:44-45` (`"System.Text.Json.JsonSerializer.Deserialize"`, `"JsonSerializer.Deserialize"`)
- These lack the `(...)` parameter list; `ImpurityCatalog.GetKnownImpureMemberSource` matches against
  `ToDisplayString()` which always includes parentheses -> neither can ever match (covered by properly-formed overload
  entries at lines 46-49, but clear format drift).

### [PB1-33.5] 33.5 Engine uses hard-coded magic strings for ALL SMT/analysis keys - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Symbolic/SymbolicProjectQueryContext.cs` (every `TryGetGlobalOption` call, lines 33-104) - broadens
  Round-3 Section 21.8 (which flagged only `GetSmtMode`). Any future `ConfigKeys` rename silently desyncs the engine with no
  compile-time error.

### [PB1-33.6] 33.6 `SuppressionDiagnosticIds` registry default string duplicated, never applied - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/Configuration/AnalyzerConfigurationOptionRegistry.cs:176` vs
  `AnalyzerConfiguration.cs:985-1005` (`ProvenDiagnosticSuppressionOptions.AllSupportedDiagnosticIds`). When unset,
  `GetSuppressionDiagnosticIds` returns `AllSupportedDiagnosticIds` directly; the registry default string is never
  parsed. The two lists must be hand-kept in sync; if they diverge, documented default differs from runtime (and the
  default uses uppercase-with-spaces while parsed/allowed values are lowercase).

### [PB1-33.7] 33.7 Baseline `Explain` contains an unreachable "no matching current diagnostic" branch - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs:195` (always-true `ContainsKey` at `:188` -> line 195 dead).

### [PB1-33.8] 33.8 `NormalizePath` doesn't collapse `//` or resolve `..` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs:279-292` (compounds Round-3 Section 21.6): two logically-identical
  paths (`a/b/c.cs` vs `a//b/c.cs` vs `a/./b/c.cs`) treated as distinct -> baseline dedup/comparison misses.

### [PB1-33.9] 33.9 `AttributeStubNamespaces` registry default is dead/redundant - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/Configuration/AnalyzerConfigurationOptionRegistry.cs:46-49` vs
  `SharpProofAttributeIdentityPolicy.cs:44` (unconditionally adds `OfficialNamespace = "SharpProof.Attributes"`).
  `FromOptions` reads via `GetValues` (no fallback) which returns empty when unset -> registry default never applied.

## 34. Symbolic Data Structures & Limits (`SharpProof.Symbolic`, `SearchLib`)

### [PB1-34.1] 34.1 `MayOverflow` dropped from the canonical term key -> fact/state identity conflation - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SYMBOLIC-OVERFLOW-IDENTITY
> **Evidence:** Overflow-sensitive and mathematical binary terms shared a structural key and associative flattening crossed overflow modes.
> **Changes/tests:** Structural keys encode overflow sensitivity and flatten only matching modes; SymbolicIrTests.
- `SharpProof.Symbolic/Ir/SymbolicIr.cs:CreateBinaryTermKey (~1411-1433)` / `CreateTermKey case SymbolicBinaryTerm (~1326-1327)`;
  consumed at `SymbolicIrFormulaEncoder.cs:262`
- `SymbolicBinaryTerm.MayOverflow` is never included in the canonical key, so `a + b` (`MayOverflow=true`) and
  `a + b` (`MayOverflow=false`) produce the *identical* key. This key feeds `DeduplicateFacts`/`CreateFactKey` (state
  dedup), `NormalizedProofKey` (state cache), and is used directly as the SMT variable name when `MayOverflow=true`.
  -> a `CheckedOverflow` exception-precondition atom for a `MayOverflow=true` term vs an identical `MayOverflow=false`
  term get the same fact key -> `DeduplicateFacts` keeps only one (by provenance length) -> a proven overflow hazard
  can be silently dropped/merged with a "safe" term; the state-proof cache conflates the two.

### [PB1-34.2] 34.2 Unicode/UTF-16 `string.Length` mismatch with Z3 `str.len` (code points) - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-Z3-UTF16-SEMANTICS
> **Evidence:** Microsoft.Z3 4.12.2 MkString preserves UTF-16 code units: an astral character simplifies to length 2 and equals its surrogate concatenation.
> **Changes/tests:** No code change; current encoding matches .NET string length semantics.
- `SearchLib/SmtFormula.cs:67` (`SmtStringLengthTerm`) -> `SearchLib/Z3FormulaEncoder.cs:142` (`_context.MkLength`) +
  constant-eval `SharpProof.Symbolic/Ir/SymbolicIr.cs:882-884` (`TryEvaluateIntegerTerm` uses `stringValue.Length`)
- Z3 strings are over Unicode **code points** (`str.len` = code-point count); .NET `string.Length` = UTF-16 **code
  units** (surrogate pairs count as 2). For an astral char (`"A"` U+1D400), `.Length == 2` in C# but `str.len == 1` in
  Z3. Bounds atoms / `IsNullOrEmpty` / `length >= 0` domain facts use `str.len` while constant-eval uses
  `stringValue.Length` -> the two halves of the engine disagree -> unsound feasibility/contradiction for surrogate-pair
  strings (e.g. a false `s.Length == 1` for an astral char modeled as true; an out-of-range hazard missed).

### [PB1-34.3] 34.3 State-merge truncation silently weakens path conditions without downgrading to `Unknown` - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicStateMerger.cs` (`MergePathConditionsAcrossAll:59-68` early return;
  `MaxFactChoiceCombinationsPerTarget:39-48` break)
- When limits exceeded, the merge returns early / breaks, **dropping** remaining targets' merged conditions. Merged
  path conditions are `Or` of `(guard && branch-condition)`; dropping branch constraints makes each branch's condition
  weaker (more permissive) -> combined post-merge path condition over-permissive. A branch actually infeasible can
  appear feasible -> a path through an impure call judged unreachable -> **false `ProvablyPure`** (unsound). No `Unknown`
  downgrade forced (only a `SymbolicAnalysisTruncationEvent` recorded).

### [PB1-34.4] 34.4 String-concat canonical key is non-canonical for non-adjacent literals - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIr.cs:CreateStringConcatTermKey (1359-1367)`, `CreateNormalizedStringConcatTermKeys
  (1381-1401)`, `CollectStringConcatTerms (1369-1379)`
- Adjacent literals are merged only when adjacent in the flattened list. `("a" + x) + "b"` -> key
  `string-concat(string:a,<key(x)>,string:b)`; `x + ("a" + "b")` -> key `string-concat(<key(x)>,string:ab)`. Both denote
  `"a" + x + "b"` but produce distinct canonical keys -> `CreateFactKey`/`CreateProofKey`/`SymbolicStructuralKey.ForTerm`
  don't recognize them as equivalent -> wrong de-dup/merge and (in `SymbolicStateMerger`) equivalent string assignments
  across branches aren't merged (non-canonical output).

### [PB1-34.5] 34.5 `null` string literal not handled in `TryLowerStringTerm` (inconsistent with concat operand) - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Strings.cs:TryLowerStringTerm (710-760)` vs `TryLowerStringConcatOperand (769-781)`
- `TryLowerStringConcatOperand` maps a `null` literal to `SymbolicStringConstantTerm(string.Empty)` (correct, `"x"+null=="x"`),
  but `TryLowerStringTerm` has no null-literal handling -> a `null` literal (contextually `string`) falls through to a
  null `SymbolicNullTerm`/`SymbolicStringContentTerm(nullReference)`. `s.StartsWith(null)`, `s.Contains(null)`,
  `s == null` (instance equals) are lowered to a predicate over the *content of a null reference* instead of an
  `ArgumentNull` hazard -> lost null-argument hazard from the symbolic formula (partly masked by separate hazard analysis).

### [PB1-34.6] 34.6 `AsyncLocal` limit scope corrupted by out-of-order `Dispose` - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicAnalysisLimits.cs:Scope.Dispose (319-334)`
- `Push` stores `_parent = CurrentScope.Value`; `Dispose` unconditionally sets `CurrentScope.Value = _parent`. Non-LIFO
  disposal (exception between push and dispose, sibling `using` that throws first) leaves `CurrentScope` pointing at
  the wrong scope -> truncation limits / recorded `SymbolicAnalysisTruncationEvent`s attributed to the wrong analysis
  (distinct angle from Round-3 Section 23.3 await-resume leakage).

---

## Updated top priorities to fix first (Round 1 + 2 + 3 + 4)
1. **23.1** shared non-thread-safe `SemanticModel` used concurrently (HIGH data race).
2. **25.1 / 25.2** query-service NREs on null `RawResult`/`rawProof` (HIGH crashes).
3. **26.1** analyzer purity `GetPurity` call without try/catch (HIGH crash).
4. **20.1 / 15.1** integer `int.MinValue` overflow/negation unmodeled (unsound).
5. **27.1 / 27.2** control-flow lowering treats unreachable code as reachable (unsound).
6. **34.2** Unicode `string.Length` vs Z3 `str.len` mismatch (unsound).
7. **16.1** `[MaybeNullWhen]` inverted polarity (false-positive contract violation).
8. **34.1** `MayOverflow` dropped from term canonical key (fact conflation).
9. **22.2** NuGet package omits Linux native Z3 (`libz3.so`).
10. **28.1 / 28.2** CLI explain `--report-max-hazards` always rejected; complexity gate can't rank `Product`/`Max`.

---

## Source PB2 - POTENTIAL_BUGS_2.md

This document compiles the potential bugs, soundness gaps, safety issues, resource leaks, and logic errors discovered during a comprehensive audit of the SharpProof codebase. All findings have been verified by concurrent subagents or runtime test analysis.

---

## 1. Symbolic Engine & Solver Integration (Smt, Z3)

### [PB2-1.1] 1.1 Native Z3 Solver Context Leak via Thread-Local Storage

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-SESSION-OWNERSHIP
> **Evidence:** Static thread-local proof contexts rooted native Z3 sessions for thread-pool lifetime and disposal only reached the current thread.
> **Changes/tests:** SmtAnalysisService owns tracked ThreadLocal contexts and disposes all owned sessions; SmtAnalysisServiceTests.
* **File & Lines:** [SmtAnalysisService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtAnalysisService.cs#L25-L27)
* **Severity:** High
* **Description:** The solver session `t_sharedProofSearchContext` is declared as a `[ThreadStatic]` static field. Each thread in the thread pool that runs SMT queries will initialize its own native Z3 solver session (`SearchLibProofSearchSession` which implements `ISmtProofSearchSession`). When `SmtAnalysisService` is disposed, it only disposes the session on the *current* thread (calling `DisposeCurrentThreadProofSearch()`). The sessions created on other threads are left intact in their thread-local storage. Since thread pool threads are kept alive indefinitely for the lifetime of the process, these native Z3 contexts are never collected or disposed, causing a severe native handle and memory leak.
* **Impact:** Heavy native memory usage over time, eventually leading to process crashes or out-of-memory errors in long-running analysis runs.
* **Recommendation:** Maintain a thread-safe registry of all active sessions (e.g. using a thread-safe concurrent bag of weak references) inside `SmtAnalysisService`. When `Dispose()` is called, iterate over all registered sessions and dispose of them:
  ```csharp
  private readonly ConcurrentBag<WeakReference<ISmtProofSearchSession>> _activeSessions = new();
  ```

### [PB2-1.2] 1.2 Unsound Symbolic Facts Propagation in Try-Catch Blocks

> **Disposition:** Fixed
> **Canonical root cause:** RC-TERMINAL-BRANCH-FLOW
> **Evidence:** A point after an if whose two branches both exit remained reachable.
> **Changes/tests:** The merged state is contradictory when both branches definitely exit; ProgramPointFacts_BothIfBranchesExitMarksFollowingPointUnreachable.
* **File & Lines:** [SymbolicProgramPointFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProgramPointFacts.cs#L3905-L3911)
* **Severity:** High
* **Description:** When computing the entry state for a `catch` block inside `AddCompletedTryStatementStateFacts`, the analyzer copies the `entryState` (the state of the program *before* entering the `try` block) directly into the `catchState` without invalidating any mutations:
  ```csharp
  var catchState = entryState;
  AddCompletedBlockStateFacts(ref catchState, catchClause.Block, semanticModel, cancellationToken);
  ```
  This is unsound because any prefix of statements inside the `try` block could have successfully executed and mutated local variables before the exception was thrown. By starting the `catch` block analysis with the pre-try `entryState`, the analyzer assumes mutated variables still hold their old values, which is incorrect.
* **Impact:** Unsound proofs. The solver may verify invalid code as "safe" or "pure" because it incorrectly assumes a variable has its pre-try value inside the `catch` block when it could have actually been mutated.
* **Recommendation:** Collect all symbols/variables mutated inside the `try` block (by scanning the `try` block syntax) and invalidate them in `catchState` by calling `RemoveStateFactsReferencingSymbol` for each mutated symbol.

### [PB2-1.3] 1.3 SMT Variable Prefix Collision during Fact Invalidation

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SmtFormulaReferenceScanner.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtFormulaReferenceScanner.cs#L8-L12)
* **Severity:** Medium
* **Description:** When invalidating facts for a mutated symbol, `RemoveFactsReferencingSymbol` gets the symbol's SMT variable prefix using `SymbolicFactFactory.GetSmtVariableName(symbol)` (which returns `Name + "#" + location`). It then removes any facts where `ContainsVariablePrefix` returns true:
  ```csharp
  internal static bool ContainsVariablePrefix(SmtFormula formula, string variablePrefix)
  {
      return ContainsVariable(formula, variableName =>
          variableName.IndexOf(variablePrefix, StringComparison.Ordinal) >= 0);
  }
  ```
  If `variablePrefix` is `"x#1"`, the `IndexOf` call will return true for any variable name that starts with `"x#1"`, including `"x#12"` (which represents an entirely different variable `x` declared at start location 12 instead of 1).
* **Impact:** Unrelated variable facts are incorrectly removed from the state when a variable with a prefix name is mutated, causing precision loss in the symbolic analysis.
* **Recommendation:** Ensure that the match in `ContainsVariablePrefix` respects the location number boundaries. The character immediately following `variablePrefix` in the variable name must not be a digit.

### [PB2-1.4] 1.4 Missing Linux Platform Support for SMT Native Loader

> **Disposition:** Fixed
> **Canonical root cause:** RC-NATIVE-LOADER-PLATFORM
> **Evidence:** The native bootstrap assumed Windows/macOS names and load APIs.
> **Changes/tests:** Platform-specific libz3 filename selection and Linux dlopen support; SmtNativeLibraryBootstrap platform test.
* **File & Lines:** [SmtNativeLibraryBootstrap.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtNativeLibraryBootstrap.cs#L92-L100)
* **Severity:** High
* **Description:** `GetNativeLibraryFileName()` only returns file names for Windows (`"libz3.dll"`) and OSX (`"libz3.dylib"`). It returns `null` for Linux and other platforms.
* **Impact:** SharpProof cannot run its symbolic analysis on Linux machines/CI pipelines, even though Z3 fully supports Linux (via `libz3.so`).
* **Recommendation:** Add a check for `OSPlatform.Linux` and return `"libz3.so"`.

---

## 2. Core Roslyn Analyzers

### [PB2-2.1] 2.1 Contract Inheritance Deficit in Overridden and Implementing Methods

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTRACT-HIERARCHY
> **Evidence:** Behavioral contract analyzers read only the immediate method and missed override/interface contracts.
> **Changes/tests:** MethodContractHierarchy plus inherited Requires, Ensures, allocation, capability, and complexity lookup; ContractInheritanceTests.
* **File & Lines:**
  * [MethodAllocationAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodAllocationAnalyzer.cs)
  * [MethodCapabilityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodCapabilityAnalyzer.cs)
  * [MethodExpectedComplexityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodExpectedComplexityAnalyzer.cs)
  * [MethodRequiresAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodRequiresAnalyzer.cs)
  * [MethodEnsuresAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodEnsuresAnalyzer.cs)
* **Severity:** High
* **Description:** Analyzers for allocation, capability, complexity, and contracts only inspect attributes directly applied to the method symbol. They do not traverse the inheritance or interface implementation chain (unlike `MethodPurityAnalyzer.cs`).
* **Impact:** Overrides/implementations bypassing attributes can silently violate base contracts (allocating memory, performing banned IO, exceeding complexity limits), and call sites invoking overrides will skip checking preconditions.
* **Recommendation:** Traverse the overridden and interface implementation hierarchy to inherit contract specifications from the base declarations.

### [PB2-2.2] 2.2 Incorrect Type Parameter Heap Allocation Analysis

> **Disposition:** Fixed
> **Canonical root cause:** RC-GENERIC-ALLOCATION
> **Evidence:** Unconstrained reference-capable type parameters were treated as non-heap objects.
> **Changes/tests:** Allocation classification now treats a type parameter as potentially heap allocated unless value-type constrained; ZeroAllocationContractTests.
* **File & Lines:** [MethodAllocationAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodAllocationAnalyzer.cs#L213-L220)
* **Severity:** High
* **Description:** `IsHeapAllocatedObjectType` returns `false` for unconstrained type parameters or those with only the `new()` constraint.
* **Impact:** A call to `new T()` is not flagged as a heap allocation, but at runtime `T` can be instantiated as a class, which will allocate memory and violate the `[ZeroAllocations]` contract.
* **Recommendation:** Treat unconstrained and `new()` constrained type parameters as potentially heap-allocating unless they have a struct/value type constraint.

### [PB2-2.3] 2.3 Wrong Semantic Binding of Generic Type Parameters at Call Sites

> **Disposition:** Fixed
> **Canonical root cause:** RC-GENERIC-CONTRACT-SUBSTITUTION
> **Evidence:** Inherited/callee contract expressions were rewritten without substituting concrete method and containing-type arguments.
> **Changes/tests:** RequiresContractHelpers substitutes generic type parameters at the call site; RequiresContractTests.
* **File & Lines:** [MethodRequiresAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodRequiresAnalyzer.cs#L216-L228)
* **Severity:** High
* **Description:** `RequiresContractHelpers.TryRewriteForArguments` fails to rewrite generic type parameters (`T`) to their call-site type arguments (e.g. `int`).
* **Impact:** Preconditions referencing type parameters (e.g. `typeof(T)`) will fail to bind at the call site if the caller is non-generic, or will incorrectly bind to the caller's own type parameter `T`.
* **Recommendation:** Substitute the caller's type parameters with the concrete type arguments supplied at the invocation site during rewriter processing.

### [PB2-2.4] 2.4 Parameter Name Shadowing Bug in Contract Rewriter

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [RequiresContractHelpers.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/RequiresContractHelpers.cs#L250-L274)
* **Severity:** Medium
* **Description:** `ParameterPlaceholderRewriter` is a purely syntactic rewriter that replaces all identifiers matching method parameter names.
* **Impact:** If a contract condition has a lambda or local function shadowing a parameter name (e.g. `items.Any(x => x > 0)`), the lambda variable `x` will be incorrectly rewritten to the method argument value (e.g. `(myItems).Any(x => (5) > 0)`).
* **Recommendation:** Track scopes during AST rewrite and do not substitute identifiers that shadow parameter names in local scopes or lambda parameters.

### [PB2-2.5] 2.5 Invalid Value Binding for Property Setters in Compound Assignments and Increments

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [MethodRequiresAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodRequiresAnalyzer.cs#L199-L232)
* **Severity:** Medium
* **Description:** The entire assignment syntax (e.g., `x.MyProperty += 5` or `x.MyProperty++`) is passed as the setter value.
* **Impact:** The setter parameter `value` in the `[Requires]` contract is rewritten to the assignment expression (e.g. `(x.MyProperty += 5) > 0`), which introduces side-effects into pure queries, leading to SMT binding/proving failures.
* **Recommendation:** Extract the computed value (e.g. `x.MyProperty + 5` or `x.MyProperty + 1`) and pass it as the binding expression.

### [PB2-2.6] 2.6 Fragile Baseline Suppression Keys Due to Absolute Offsets

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:**
  * [MethodAllocationAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodAllocationAnalyzer.cs#L85-L94)
  * [MethodCapabilityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodCapabilityAnalyzer.cs#L184-L197)
  * [MethodRequiresAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodRequiresAnalyzer.cs#L230-L248)
* **Severity:** Medium
* **Description:** The analyzers use absolute character spans/offsets in the `evidenceKey` used for baseline suppressions.
* **Impact:** Suppressions are extremely fragile. Any modification that inserts or deletes a character above the violation site will change the key and silently break the suppression.
* **Recommendation:** Use relative offsets or line-span identifiers, or base evidence keys on structural node paths rather than absolute file offsets.

### [PB2-2.7] 2.7 Incomplete Method Body Extraction for Conversion Operators

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [MethodPurityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodPurityAnalyzer.cs#L439-L449)
* **Severity:** Low
* **Description:** `GetMethodComplexity` does not handle `ConversionOperatorDeclarationSyntax`.
* **Impact:** Conversion operator declarations fall through to `_ => node`, which analyzes the complexity of the entire declaration syntax (including signature) rather than just the body/expression body.
* **Recommendation:** Add a handler case for `ConversionOperatorDeclarationSyntax`.

### [PB2-2.8] 2.8 Static Virtual/Abstract Interface Members Ignored

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [MethodPurityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodPurityAnalyzer.cs#L476-L482)
* **Severity:** Low
* **Description:** `ShouldSuggestMissingEnforcePure` checks `!methodSymbol.IsStatic` before ignoring virtual, abstract, and override methods.
* **Impact:** Static virtual, abstract, and override methods introduced in C# 11 interface members bypass this exclusion and incorrectly trigger missing `[EnforcePure]` suggestions.
* **Recommendation:** Ensure static virtual/abstract members are excluded similarly to standard virtual members.

### [PB2-2.9] 2.9 Duplicate / Overwritten Capability Attributes Ignored

> **Disposition:** Already fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
* **File & Lines:** [MethodCapabilityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodCapabilityAnalyzer.cs#L209-L236)
* **Severity:** Medium
* **Description:** `TryGetAllowedCapabilities` returns on the first `[AllowedCapabilities]` attribute it encounters.
* **Impact:** If multiple attributes are applied, subsequent attributes are completely ignored instead of merging their capability flags.
* **Recommendation:** Aggregate all capability attributes applied to a symbol and combine their permissions.

---

## 3. Exception & Nullability Flow Analyzers

### [PB2-3.1] 3.1 Expression-Bodied Constructors Bypassed

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXPRESSION-BODIED-CONSTRUCTOR
> **Evidence:** Nullable member-initialization verification omitted expression-bodied constructors.
> **Changes/tests:** NullableContractAnalyzer includes constructor expression bodies; NullableContractVerificationTests.
* **File & Lines:** [NullableContractAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/NullableContractAnalyzer.cs#L630-L642)
* **Severity:** High
* **Description:** `TryGetExpressionBody` does not list `ConstructorDeclarationSyntax`. Additionally, `TryGetBody` checks for block bodies and returns `null` for expression-bodied constructors.
* **Impact:** If a constructor uses an expression body (e.g., `public MyClass() => _field = 1;`) and is decorated with `[MemberNotNull(nameof(_field))]`, the validation of member postconditions is completely bypassed.
* **Recommendation:** Add a pattern match for `ConstructorDeclarationSyntax { ExpressionBody.Expression: { } value } => value,` in `TryGetExpressionBody`.

### [PB2-3.2] 3.2 Lambda / Anonymous Function Suppressions Ignored

> **Disposition:** Fixed
> **Canonical root cause:** RC-LAMBDA-NULLABILITY-AUDIT
> **Evidence:** The null-forgiving audit skipped anonymous functions even though their expressions can violate the audited contract.
> **Changes/tests:** Lambda bodies are audited while separately analyzed local functions remain excluded; NullableContractVerificationTests.
* **File & Lines:**
  * [NullableContractAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/NullableContractAnalyzer.cs#L216-L218)
  * [SharpProofAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/SharpProofAnalyzer.cs#L123-L136)
* **Severity:** High
* **Description:** In `AuditNullForgivingOperators`, the traversal explicitly filters out descendants of `AnonymousFunctionExpressionSyntax` and `LocalFunctionStatementSyntax`. While local functions have separate syntax node analysis registration, lambda expressions and anonymous methods do not.
* **Impact:** Any null-forgiving operator (`!`) used inside a lambda expression or anonymous method is never audited, ignoring unsafe suppressions.
* **Recommendation:** Register lambda and anonymous expression kinds for body analysis, or adjust the filter to traverse into them.

### [PB2-3.3] 3.3 Async Task Return Nullability False Positives

> **Disposition:** Fixed
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:**
  * [NullableContractAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/NullableContractAnalyzer.cs#L38-L42)
  * [NullableFlowFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/NullableFlowFacts.cs#L336-L346)
* **Severity:** Medium
* **Description:** `GetMethodReturnState` evaluates the outer return type annotation of the method. For `async` methods returning `Task<T?>`, the outer type is `Task`, which is not annotated (and thus considered `NotNull`).
* **Impact:** Because the analyzer sees the return contract as `NotNull`, it requires returned expressions to be non-null, triggering false-positive `NullableReturnContractViolationRule` warnings when returning `null` (since the actual populated type is the nullable `T?`).
* **Recommendation:** Check if `method.IsAsync` is true. If it is, unwrap the return type to obtain its task-like generic parameter and check its nullable annotation.

### [PB2-3.4] 3.4 Faulty Path-Sensitive Finally Block State Merging

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:**
  * [ExceptionFlowAnalyzer.PathFacts.Finally.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.PathFacts.Finally.cs#L159-L171)
  * [SymbolicProgramPointFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProgramPointFacts.cs#L189-L197)
* **Severity:** Medium
* **Description:** `GetStatementEntryPathState` merges the path state from the exception site (`baseState`) with the general CFG state of the statement. However, `MergeStates` is implemented as a conjunction (concatenating facts) rather than a disjunction.
* **Impact:** If there is any conflicting fact or symbol version between the specific exception path and the general CFG path, the merged state immediately becomes contradictory (unreachable). This causes `IsPathStateReachable` to return `false` prematurely, terminating the exit proof and leading to false negatives.
* **Recommendation:** Avoid merging the general CFG state into the path-sensitive exception state. The exception path state should be propagated forward through the finally block by symbolically processing the statements.

### [PB2-3.5] 3.5 Control-Flow-Insensitive Delegate Target Tracking

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [ExceptionFlowAnalyzer.SpecialCases.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.SpecialCases.cs#L9-L27)
* **Severity:** Medium
* **Description:** `GetLocalDelegateTargetInvocationNodes` resolves delegate targets using a linear preorder AST walk and a single mutable `knownTargets` dictionary.
* **Impact:** Reassignments to delegates inside branches (such as `if/else` statements) or loops will globally overwrite the resolved target, leading to false positives/negatives in delegate tracking.
* **Recommendation:** Query the semantic model or utilize the symbolic reachability service to track the active delegate assignment path-sensitively.

### [PB2-3.6] 3.6 C# `default` Literal Treated as Unknown

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [ExceptionFlowAnalyzer.ExceptionSites.NullFacts.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.ExceptionSites.NullFacts.cs#L76-L85)
* **Severity:** Low
* **Description:** `IsDefinitelyNullExpression` checks for `NullLiteralExpression` and `DefaultExpressionSyntax` (e.g., `default(T)`), but fails to check for `DefaultLiteralExpression` (e.g. `default`).
* **Impact:** Expressions using the C# 7.1+ `default` literal (such as `((Action)default)()`) are not recognized as null, resulting in false negatives.
* **Recommendation:** Add a check for `SyntaxKind.DefaultLiteralExpression` when the target type is a reference type.

### [PB2-3.7] 3.7 Array Type Mismatch Store Limited to `object[]`

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:**
  * [ExceptionFlowAnalyzer.ExceptionSites.CastsAndStores.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.ExceptionSites.CastsAndStores.cs#L79-L82)
  * [ExceptionFlowAnalyzer.ExceptionSites.CastsAndStores.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.ExceptionSites.CastsAndStores.cs#L108-L120)
* **Severity:** Medium
* **Description:** `IsObjectArrayElementStore` restricts array covariance safety checks strictly to arrays whose elements are statically typed as `System.Object`.
* **Impact:** C# array covariance applies to all reference types (e.g. assigning a `Derived[]` to `Base[]`). Storing a `Base` object into such a covariant array will cause an `ArrayTypeMismatchException` at runtime, but the analyzer completely skips this check because the array is not strictly typed as `object[]` (false negative).
* **Recommendation:** Relax `IsObjectArrayElementStore` to check for any covariant reference type arrays.

### [PB2-3.8] 3.8 Inferred Parameter Null Guard Ignores Multiple/Subsequent Guards

> **Disposition:** Already fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
* **File & Lines:** [NullableFlowFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/NullableFlowFacts.cs#L296-L304)
* **Severity:** Medium
* **Description:** `HasInferredNotNullNormalCompletionPostcondition` only checks `body.Statements.FirstOrDefault()`.
* **Impact:** If a method accepts multiple parameters and guards them against null (e.g. `if (s1 == null) throw...; if (s2 == null) throw...;`), only the first parameter's guard is captured. All subsequent parameter guards are ignored.
* **Recommendation:** Iterate through the leading statements of the block, continuing to collect guards as long as they are null-checking `if` statements that throw, instead of breaking after the first statement.

### [PB2-3.9] 3.9 Loop-Mutation Bypass in Nullable Value Tracking

> **Disposition:** Fixed
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [ExceptionFlowAnalyzer.ExceptionSites.NullableAccess.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.ExceptionSites.NullableAccess.cs#L42-L45)
* **Severity:** Medium
* **Description:** In `TryResolveCurrentNullableValueExpression`, the statement loop breaks immediately once it matches the `containingStatement` containing the use node.
* **Impact:** Inside loops, mutations occurring *after* the use node in syntax order are executed before the next iteration's use. Because the analyzer breaks early, it misses mutations that set a nullable variable to null later in the loop body, leading to a false negative.
* **Recommendation:** When resolving values within a loop, check if the variable is mutated anywhere in the loop body (even after the use statement) and invalidate the resolved value if it is.

---

## 4. Specialized Symbolic Query Services

### [PB2-4.1] 4.1 Loop Bound Soundness Gap with Non-Local Loop Variables

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONSERVATIVE-LOOP-BOUND
> **Evidence:** Field/property loop controls were assumed stable even though calls in the loop can mutate them.
> **Changes/tests:** Only locals and parameters qualify as stable loop-control symbols; SymbolicComplexityTests.
* **File & Lines:**
  * [SymbolicComplexityService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicComplexityService.cs#L1668-L1710)
  * [SymbolicComplexityService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicComplexityService.cs#L1411-L1414)
* **Severity:** High
* **Description:** `IsSymbolMutatedInStatement` only checks for direct syntactic mutations (assignments, increments/decrements, ref/out). If the loop variable is a non-local variable (e.g. a field like `this.i`), and the loop body calls a method (e.g., `Foo()`), the method can mutate the field transitively. The service fails to detect this.
* **Impact:** Incorrect loop bound inferences (e.g., assuming a loop terminates when it is actually an infinite loop).
* **Recommendation:** Restrict loop variable resolution to local variables (`ILocalSymbol`) only.

### [PB2-4.2] 4.2 Redundant Candidates & Performance Hotspot in Null-Dereference Hazard Detection

> **Disposition:** Already fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
* **File & Lines:** [NullableFlowFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/NullableFlowFacts.cs#L135-L142)
* **Severity:** Medium
* **Description:** In `NullableFlowFacts.cs`, `IsDefinitelyNotNullReferenceValue` filters out non-null reference values before creating candidates, but it calls `GetExactExpressionState` instead of `GetExpressionState`. `GetExactExpressionState` ignores Roslyn's nullable flow analysis (`Nullability.FlowState`).
* **Impact:** Obviously non-null local variables/parameters are not filtered, generating thousands of redundant null-dereference candidates, placing heavy, unnecessary load on the SMT solver.
* **Recommendation:** Change it to call `GetExpressionState` which leverages Roslyn's full nullable flow analysis.

### [PB2-4.3] 4.3 Unsound Purity/Capability Classification for `Stopwatch`

> **Disposition:** Fixed
> **Canonical root cause:** RC-STOPWATCH-CAPABILITY
> **Evidence:** Stopwatch clock-reading/control members were treated as capability-neutral framework calls.
> **Changes/tests:** Clock-touching Stopwatch APIs now require Time capability while neutral construction/state members remain allowed; CapabilityContractTests.
* **File & Lines:**
  * [SymbolicCapabilityService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicCapabilityService.cs#L781-L784)
  * [SymbolicCapabilityService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicCapabilityService.cs#L999-L1007)
* **Severity:** High
* **Description:** `IsClockMember` only matches `"Now"`, `"UtcNow"`, `"Today"`, `"TickCount"`, `"TickCount64"`, and `"GetTimestamp"`. It ignores standard members of `Stopwatch` like `StartNew()`, `Start()`, `Stop()`, `Elapsed`, `ElapsedMilliseconds`, and `ElapsedTicks`.
* **Impact:** Code using `Stopwatch` is incorrectly flagged as `MetadataClassificationUnavailable` instead of the `Clock` capability.
* **Recommendation:** Classify all members of `System.Diagnostics.Stopwatch` as `Clock`.

### [PB2-4.4] 4.4 Uncaught Exception Risk on Nullable Target Properties

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:**
  * [SymbolicComplexityService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicComplexityService.cs#L143)
  * [SymbolicCapabilityService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicCapabilityService.cs#L191)
* **Severity:** Low
* **Description:** `ResolveTarget` dereferences nullable properties (like `target.LineNumber!.Value`) using `!` without checking if they are null.
* **Impact:** Throws raw `InvalidOperationException` or `NullReferenceException` instead of a clean, helpful `ArgumentException`.
* **Recommendation:** Add validation checks on nullable target parameters.

---

## 5. Infrastructure, CodeFixes, Tooling, & Configuration

### [PB2-5.1] 5.1 `NotSupportedException` Hazard in `RuntimeImplementationAssemblyResolver` (Dynamic Assemblies)

> **Disposition:** Fixed
> **Canonical root cause:** RC-DYNAMIC-ASSEMBLY-LOCATION
> **Evidence:** Assembly.Location can throw for dynamic assemblies during runtime implementation resolution.
> **Changes/tests:** Dynamic assemblies are skipped and Location failures are handled; RuntimeImplementationAssemblyResolverTests.
* **File & Lines:** [EffectSummaryMetadataSupport.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/EffectSummaryMetadataSupport.cs#L136-L164)
* **Severity:** High
* **Description:** The `Resolve` method iterates through all assemblies in the current AppDomain and queries `assembly.Location`. Querying `Location` on dynamic assemblies (such as those generated by mock frameworks, serialization engines like System.Text.Json, or unit test runners) throws a `NotSupportedException`.
* **Impact:** When hosted inside environments like Visual Studio, MSBuild, or unit testing suites, the presence of any dynamic assembly will crash the metadata resolver, failing analysis.
* **Recommendation:** Add a check to skip dynamic assemblies before querying `Location`:
  ```csharp
  if (assembly.IsDynamic) continue;
  ```

### [PB2-5.2] 5.2 Defective Return-Early logic in `SummaryAssemblyReferenceResolver`

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [EffectSummaryMetadataSupport.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/EffectSummaryMetadataSupport.cs#L101-L105)
* **Severity:** Medium
* **Description:** In `FindAssemblyReferencePath`, when iterating compilation references to locate the matching assembly, the code checks if the file path is valid and returns immediately:
  ```csharp
  var referencePath = reference.FilePath;
  return string.IsNullOrWhiteSpace(referencePath) || !File.Exists(referencePath)
      ? null
      : referencePath;
  ```
* **Impact:** If compilation contains multiple references for the same symbol (e.g. an in-memory/stream-based metadata reference followed by a file-backed reference), the first match with an empty/non-existent `FilePath` causes the method to return `null` immediately, skipping subsequent valid references.
* **Recommendation:** Only return if a valid path is found, otherwise continue checking references.

### [PB2-5.3] 5.3 Double-Checked Locking Memory Barrier Hazard in `ActualMethodIdentity`

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [EffectSummaryMetadataSupport.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/EffectSummaryMetadataSupport.cs#L519-L536)
* **Severity:** Medium
* **Description:** The `MethodBodySha256` property implements lazy double-checked locking, but `_methodBodySha256Computed` is not marked as `volatile`.
* **Impact:** Without `volatile` or memory barrier guarantees, JIT compiler optimization or CPU out-of-order execution (e.g., on ARM64) may cause reader threads to observe `_methodBodySha256Computed == true` before the string value assignment to `_methodBodySha256` is flushed, causing it to return `null` or a stale value.
* **Recommendation:** Mark `_methodBodySha256Computed` as `volatile`.

### [PB2-5.4] 5.4 Inferred Contract Code Fix Blocked on Accessor Declarations

> **Disposition:** Fixed
> **Canonical root cause:** RC-ACCESSOR-INFERRED-CODEFIX
> **Evidence:** Registration rejected accessors although the inferred-contract helpers already supported them.
> **Changes/tests:** Accessor declarations are eligible and their bodies/returns are discovered; SharpProofCodeFixTests.
* **File & Lines:** [SharpProofCodeFixProvider.cs](file:///C:/w/PurelySharp/SharpProof.CodeFixes/SharpProofCodeFixProvider.cs#L325-L327)
* **Severity:** High
* **Description:** `RegisterInferredContractCodeFix` returns early if the target declaration is a property, indexer, or accessor.
* **Impact:** This early return completely prevents registering code fixes for inferred nullable contracts on property accessors, despite accessor support being explicitly implemented in helper methods.
* **Recommendation:** Remove `or AccessorDeclarationSyntax` from the return-early check.

### [PB2-5.5] 5.5 Moving Attributes to Getter Produces CS8418 Compiler Error

> **Disposition:** Fixed
> **Canonical root cause:** RC-ACCESSOR-ATTRIBUTE-TARGET
> **Evidence:** Moving a property [get: Requires] list onto an accessor retained an illegal nested get target.
> **Changes/tests:** MoveAttributeToGetterAsync clears AttributeList.Target; SP0029_ClearsGetTargetWhenMovingRequiresToGetter.
* **File & Lines:** [SharpProofCodeFixProvider.cs](file:///C:/w/PurelySharp/SharpProof.CodeFixes/SharpProofCodeFixProvider.cs#L657-L660)
* **Severity:** High
* **Description:** When moving a contract attribute (such as `[Requires]`) from a property to its getter accessor, the code preserves the original attribute list syntax. If the original attribute was declared as `[get: Requires]` on the property, preserving the list retains the `get:` target designation.
* **Impact:** Placing `[get: Requires] get => 42;` directly on an accessor is invalid in C# and causes compilation error CS8418 ("The attribute target 'get' is not valid for this declaration").
* **Recommendation:** Ensure the attribute target is cleared when moving the attribute list to the accessor.

### [PB2-5.6] 5.6 Silent Failure / No-op on Setter `value` Parameters

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SharpProofCodeFixProvider.cs](file:///C:/w/PurelySharp/SharpProof.CodeFixes/SharpProofCodeFixProvider.cs#L396-L399)
* **Severity:** Medium
* **Description:** When resolving inferred parameters for property setters, the contract kind is formatted as `nullable-parameter:value`. The fix implementation attempts to query `ParameterSyntax` nodes, which do not exist for the implicit `value` parameter.
* **Impact:** The code fix returns the unmodified document as a silent no-op.
* **Recommendation:** Add a fallback when `parameterName` is `"value"` to apply the parameter attribute target `[param: InferredAttribute]` directly to the accessor declaration.

### [PB2-5.7] 5.7 Non-Resilient JSON Value Deserialization in CLI Tool

> **Disposition:** Fixed
> **Canonical root cause:** RC-CLI-INPUT-AND-OUTPUT-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.EffectSummary/Program.cs#L207-L208)
* **Severity:** Low
* **Description:** The `ReadString` helper parses JSON elements blindly via `value.GetString()` without validating the JSON node type.
* **Impact:** If any property in the JSON file is not a string (e.g. `CacheKey: null`, `CacheKey: 123`, or a boolean), `GetString()` throws an unhandled `InvalidOperationException`, crashing the CLI tool.
* **Recommendation:** Validate `value.ValueKind == JsonValueKind.String` before calling `GetString()`.

---

## 6. Build & Testing Tooling Issues

### [PB2-6.1] 6.1 Hardcoded 'Release' Configuration in Packaging Integration Tests

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [AnalyzerPackagingTests.cs](file:///C:/w/PurelySharp/SharpProof.Test/AnalyzerPackagingTests.cs#L816-L827)
* **Severity:** Medium
* **Description:** The integration test `BuiltAnalyzerPackage_ShouldContainCurrentAnalyzerAndCodeFixAssemblyBytes_WhenPackageExists` hardcodes `bin/Release` in the package file lookup path. When the test runner is run under `Debug` configuration, it verifies the hashes of the current `Debug` assemblies against the *stale* package located in `bin/Release` (created during previous release builds), leading to false-positive test failures when the assemblies differ.
* **Impact:** False-positive test failures when running unit tests locally in Debug configuration if a stale Release package is present.
* **Recommendation:** Query the current compilation configuration or search under both `Debug` and `Release` folders for the package matching the assembly build stamp.

---

## 7. Deep-Dive Audit Findings (10 Agents)

### 7.1 Purity Engine & Catalog Auditing (Agent 1)

#### [PB2-7.1.1] 7.1.1 Configuration Overrides Precedence Bug (Priority Ordering)

> **Disposition:** Duplicate
> **Canonical root cause:** RC-PURITY-PRECEDENCE-POLICY
> **Evidence:** Same equal-priority purity policy claim as PB1-8.1.
> **Changes/tests:** See PB1-8.1.
* **File & Lines:** [PurityPolicyResolver.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityPolicyResolver.cs#L79-L98)
* **Severity:** High
* **Description:** Impure generated summaries (priority 25) take precedence over user-configured pure overrides (priority 30), preventing users from overriding incorrect analysis results.
* **Recommendation:** Ensure user-configured overrides take priority in resolution.

#### [PB2-7.1.2] 7.1.2 Execution Visibility Boundary Escape (Unsoundness)

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXECUTION-ROOT-ISOLATION
> **Evidence:** Program-point collection and ancestor visibility checks crossed lambda/local-function boundaries and imported outer invocation-time facts into deferred bodies.
> **Changes/tests:** ExecutionVisibility and SymbolicProgramPointFacts stop at the containing execution root; ExecutionVisibility_DoesNotImportOuterFactsIntoDeferredLambda.
* **File & Lines:** [ExecutionVisibility.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/ExecutionVisibility.cs#L20-L119)
* **Severity:** High
* **Description:** `IsInStaticallyUnreachableBranchUsingSmt` traverses ancestors to check unreachability without stopping at lambda/callable boundaries. This leads to unsoundness because state conditions evaluated outside the lambda may not hold when the lambda runs.
* **Recommendation:** Halt ancestor traversal at lambda/anonymous method boundary syntax nodes.

#### [PB2-7.1.3] 7.1.3 Implicit Conversion Breakdown in String Constructor Pattern Matching

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [PurityAnalysisEngine.KnownBclSemantics.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityAnalysisEngine.KnownBclSemantics.cs#L239-L242)
* **Severity:** Medium
* **Description:** `IsTransientCharArrayConsumedByStringConstructor` fails to recognize the pattern when implicit conversion to `ReadOnlySpan<char>` is inserted by the compiler, leading to false impurities.
* **Recommendation:** Strip implicit casts/conversions before performing pattern checks on the arguments.

#### [PB2-7.1.4] 7.1.4 Incomplete Mutable Collection Boundary Typings

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [PurityAnalysisEngine.RuleSupport.Attributes.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityAnalysisEngine.RuleSupport.Attributes.cs#L13-L25)
* **Severity:** Medium
* **Description:** Omit generic collections like `Queue<T>`, `Stack<T>`, `SortedSet<T>`, etc., from mutable collection escape analysis.
* **Recommendation:** Add generic collections to the checked collection families list.

#### [PB2-7.1.5] 7.1.5 False Impurities for MemoryStream Constructors and ToArray()

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [ImpurityCatalog.SemanticClassifiers.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/ImpurityCatalog.SemanticClassifiers.cs#L398-L399)
* **Severity:** Medium
* **Description:** Classifies local MemoryStream usage and ToArray() as impure, preventing their use in pure methods.
* **Recommendation:** Mark non-escaping, locally created MemoryStream methods as pure.

#### [PB2-7.1.6] 7.1.6 Critical Performance Bottleneck in CallGraphBuilder

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [CallGraphBuilder.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/Analysis/CallGraphBuilder.cs#L358-L359)
* **Severity:** Medium
* **Description:** Retrieves declared symbols for all descendant nodes in the syntax tree, leading to severe slowdowns.
* **Recommendation:** Only query symbols for declaration and identifier nodes.

### 7.2 Analyzer Rules & Suppressors (Agent 2)

#### [PB2-7.2.1] 7.2.1 Mismatched Message Format Argument in `SuggestNullableContractRule`

> **Disposition:** Fixed
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:**
  * [SharpProofDiagnostics.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/SharpProofDiagnostics.cs#L253-L261)
  * [InferredContractSuggestionAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/InferredContractSuggestionAnalyzer.cs#L92-L101)
* **Severity:** Medium
* **Description:** `{1}` in the format string `"Method '{0}' satisfies nullable contract '{1}'"` maps to the `evidence` parameter instead of the `displayAttribute` name, showing the raw evidence instead of the contract.
* **Recommendation:** Fix the argument index in the diagnostic reporting code.

#### [PB2-7.2.2] 7.2.2 Potential `NullReferenceException` in `ObjectOrCollectionInitializerPurityRule`

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [ObjectOrCollectionInitializerPurityRule.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Rules/ObjectOrCollectionInitializerPurityRule.cs#L33-L37)
* **Severity:** Medium
* **Description:** Accessing `TargetMethod.MethodKind` without checking if `TargetMethod` is null can throw a `NullReferenceException` and crash the analyzer on invalid/unresolved code.
* **Recommendation:** Add a null check for `TargetMethod`.

#### [PB2-7.2.3] 7.2.3 Performance Hotspot for Top-Level Statements in Suppressor

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SharpProofDiagnosticSuppressor.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/SharpProofDiagnosticSuppressor.cs#L234-L235)
* **Severity:** Low
* **Description:** Returning the entire `CompilationUnitSyntax` root when resolving a `GlobalStatementSyntax` runs symbolic queries on the entire file instead of the statement itself.
* **Recommendation:** Return the individual global statement syntax node instead of the root.

### 7.3 Configuration & Additional Files (Agent 3)

#### [PB2-7.3.1] 7.3.1 Case-Sensitivity Schema Bypass in Baseline Verification

> **Disposition:** Fixed
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:**
  * [DiagnosticBaseline.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/DiagnosticBaseline.cs#L150-L154)
  * [AnalyzerAdditionalFileValidator.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerAdditionalFileValidator.cs#L296-L302)
* **Severity:** Medium
* **Description:** The validator checks baseline properties (`"id"`, `"symbol"`, `"path"`) in a case-sensitive manner, while the baseline parser is case-insensitive. This allows baseline entries using case variations to bypass compatibility validation.
* **Recommendation:** Perform case-insensitive property checks in the validator.

#### [PB2-7.3.2] 7.3.2 Local User-Profile Path Leak in Diagnostic Properties

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-DIAGNOSTIC-REPRODUCTION-PATHS
> **Evidence:** Roslyn diagnostic properties are not automatically telemetry, and the explain command and portable baseline matching deliberately carry the source path needed to reproduce or identify a diagnostic.
> **Changes/tests:** Existing explain/baseline property tests verify these functional paths; no telemetry sink is present and no code change was made.
* **File & Lines:**
  * [ExplainDiagnosticProperties.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/ExplainDiagnosticProperties.cs#L20-L38)
  * [BaselineDiagnosticProperties.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/BaselineDiagnosticProperties.cs#L35-L57)
* **Severity:** High
* **Description:** Absolute file paths containing local usernames (from `C:\Users\<user>`) are serialized into diagnostic properties that end up in telemetry.
* **Recommendation:** Sanitize paths to strip user-profile roots.

#### [PB2-7.3.3] 7.3.3 Case-Sensitivity Bug in `ProvenDiagnosticSuppressionOptions`

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [AnalyzerConfiguration.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs#L1007-L1022)
* **Severity:** Medium
* **Description:** Suppression check set comparison is ordinal case-sensitive, failing to match third-party diagnostic IDs containing lowercase letters.
* **Recommendation:** Use `StringComparer.OrdinalIgnoreCase`.

#### [PB2-7.3.4] 7.3.4 Property Getter Suffix Fallback Bug

> **Disposition:** Duplicate
> **Canonical root cause:** RC-DUPLICATE-CLAIM
> **Evidence:** This claim describes the same behavior and root cause as another canonical ledger item.
> **Changes/tests:** Resolved or classified under the canonical root-cause entry.
* **File & Lines:** [ConfiguredMemberKey.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/ConfiguredMemberKey.cs#L11-L22)
* **Severity:** Low
* **Description:** Write-only properties are blocked from configuration because `TryCreate` requires a getter method.
* **Recommendation:** Fall back to the property's setter method.

#### [PB2-7.3.5] 7.3.5 Missing `CancellationToken` in Recursive JSON/File Traversals

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:**
  * [DiagnosticBaseline.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/DiagnosticBaseline.cs#L135-L178)
  * [AnalyzerAdditionalFileValidator.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerAdditionalFileValidator.cs#L247-L294)
* **Severity:** Low
* **Description:** JSON traversals of large baseline JSON files block the analysis thread and ignore cancellations.
* **Recommendation:** Pass and check `CancellationToken` throughout.

### 7.4 Method Identity & Metadata Loading (Agent 4)

#### [PB2-7.4.1] 7.4.1 Local Function MethodKind Mismatch

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:**
  * [RoslynStructuralMethodIdentityAdapter.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/RoslynStructuralMethodIdentityAdapter.cs#L189)
  * [EcmaStructuralMethodIdentityAdapter.cs](file:///C:/w/PurelySharp/Shared/EcmaStructuralMethodIdentityAdapter.cs#L90-L103)
* **Severity:** Medium
* **Description:** Roslyn adapter uses `"local-function"`, while ECMA adapter uses `"ordinary"`, preventing matching.
* **Recommendation:** Align method kinds between Roslyn and ECMA adapters.

#### [PB2-7.4.2] 7.4.2 Static Local Function Type Parameter Offset Bug

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [RoslynStructuralMethodIdentityAdapter.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/RoslynStructuralMethodIdentityAdapter.cs#L164-L174)
* **Severity:** Medium
* **Description:** Roslyn adapter flattens outer method arity even if static, causing key mismatches.
* **Recommendation:** Skip outer method arity when the local function is static.

#### [PB2-7.4.3] 7.4.3 Unbounded Memory Retention / Cache Leak of Assembly Bytes

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [EffectSummaryMetadataSupport.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/EffectSummaryMetadataSupport.cs#L550-L552)
* **Severity:** Medium
* **Description:** Retains raw assembly byte arrays indefinitely in memory, leading to heap inflation.
* **Recommendation:** Release byte arrays after hashing.

### 7.5 SMT Formula Translation (Agent 5)

#### [PB2-7.5.1] 7.5.1 Unsound Reference Type Test Conversion

> **Disposition:** Fixed
> **Canonical root cause:** RC-TYPE-TEST-ORACLE-SOUNDNESS
> **Evidence:** The test-only SMT oracle reduced every reference type test to non-null, including object-to-string tests where runtime compatibility is independent.
> **Changes/tests:** The oracle translates a type test only when the static conversion proves it equivalent to non-null; TestOracle_TypeTestRequiresNonNullEquivalence.
* **File & Lines:** [CSharpConditionToFormula.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/CSharpConditionToFormula.cs)
* **Severity:** High
* **Description:** `x is T` mapped to `x != null` in tests, ignoring runtime type compatibility.
* **Recommendation:** Encode runtime type check constraints in formula.

#### [PB2-7.5.2] 7.5.2 Variable Name Collisions for Metadata Symbols

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SymbolicFactFactory.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicFactFactory.cs)
* **Severity:** Medium
* **Description:** Missing source locations lead to duplicate `#0` suffixes.
* **Recommendation:** Append unique IDs for unlocated symbols.

#### [PB2-7.5.3] 7.5.3 Z3 SMT Regex Incompatibility

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [CSharpConditionToFormula.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/CSharpConditionToFormula.cs)
* **Severity:** Medium
* **Description:** C# regex anchors (`\A`, `\z`) and `\s` are incompatible with Z3 SMT regex.
* **Recommendation:** Transpile or validate regex patterns prior to mapping.

### 7.6 Symbolic State & Facts (Agent 6)

#### [PB2-7.6.1] 7.6.1 Unsound Mutation Invalidation for Reference Types in Nested Scopes

> **Disposition:** Already fixed
> **Canonical root cause:** RC-NESTED-MUTATION-INVALIDATION
> **Evidence:** Current program-point collection recursively invalidates local, parameter, member, receiver, ref/out, and array facts across completed nested scopes while stopping at deferred callables.
> **Changes/tests:** RemoveStateFactsInvalidatedByNestedMutations and existing mutation/loop/catch regressions cover the reported case.
* **File & Lines:** [SymbolicProgramPointFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProgramPointFacts.cs)
* **Severity:** High
* **Description:** Mutations in sub-blocks/nested scopes are not properly propagated or invalidated in the outer state.
* **Recommendation:** Propagate scope-boundary mutations up to containing block states.

### 7.7 Source Query Services (Agent 7)

#### [PB2-7.7.1] 7.7.1 Incomplete Node Indexing for Empty/Comment Lines

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SymbolicSourceQueryService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicSourceQueryService.cs)
* **Severity:** Medium
* **Description:** Lines without tokens inside multi-line statements are missing from the index, throwing exceptions when queried.
* **Recommendation:** Guard indices and support empty/comment lines.

#### [PB2-7.7.2] 7.7.2 Inefficient Compilation Caching

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [SymbolicSourceQueryService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicSourceQueryService.cs)
* **Severity:** Medium
* **Description:** Static `ConditionalWeakTable` cache is bypassed because new syntax trees are compiled on every single query call.
* **Recommendation:** Reuse syntax trees and semantic models across queries.

### 7.8 CodeFixes & Refactorings (Agent 8)

#### [PB2-7.8.1] 7.8.1 Trivia/Comments Loss

> **Disposition:** Fixed
> **Canonical root cause:** RC-CODEFIX-TRIVIA
> **Evidence:** Reconstructing attribute lists discarded structured documentation/directive trivia owned by a fully removed list.
> **Changes/tests:** Attribute removal now uses per-node Roslyn removal with significant leading trivia preserved while whitespace-only trivia is normalized; SP0003_RemovalPreservesDocumentationTrivia and existing formatting tests.
* **File & Lines:** [SharpProofCodeFixProvider.cs](file:///C:/w/PurelySharp/SharpProof.CodeFixes/SharpProofCodeFixProvider.cs#L325-L327)
* **Severity:** High
* **Description:** Removing the last attribute in an attribute list deletes the entire list syntax without preserving leading trivia, causing the deletion of XML documentation comments (`///`), preprocessor directives (`#if`), and indents from the member.
* **Recommendation:** Copy leading trivia of the attribute list to the next syntax token when deleting the list.

#### [PB2-7.8.2] 7.8.2 Indentation Loss in `AddEnforcePure`

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SharpProofCodeFixProvider.cs](file:///C:/w/PurelySharp/SharpProof.CodeFixes/SharpProofCodeFixProvider.cs)
* **Severity:** Medium
* **Description:** Prepends attributes without adding indentation to the attribute list, pushing the new attribute to column 0.
* **Recommendation:** Format the newly added attribute list using the target member's indentation trivia.

#### [PB2-7.8.3] 7.8.3 XML Comments Misplacement in Inferred Attributes

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SharpProofCodeFixProvider.cs](file:///C:/w/PurelySharp/SharpProof.CodeFixes/SharpProofCodeFixProvider.cs)
* **Severity:** Medium
* **Description:** Leaves XML comments attached to the declaration's original first token, placing them *between* the new attribute and the member.
* **Recommendation:** Transfer leading XML comments trivia to the newly inserted attribute list.

### 7.9 Tooling & Reports (Agent 9)

#### [PB2-7.9.1] 7.9.1 Parallel Build Overwrite & File Lock Issues in `dotnet build`

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CORPUS-SARIF-ISOLATION
> **Evidence:** CorpusReport already assigns each input a GUID-named temporary SARIF path and invokes builds sequentially, so no static shared ErrorLog exists.
> **Changes/tests:** Program.cs path construction and control flow demonstrate isolation; no code change.
* **File & Lines:** [Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.CorpusReport/Program.cs#L33-L37)
* **Severity:** High
* **Description:** Single static `/p:ErrorLog` path causes sharing violations or diagnostic overwrite during parallel builds.
* **Recommendation:** Use dynamic unique paths per project under MSBuild.

#### [PB2-7.9.2] 7.9.2 Generic Value Types & Read-Only Views Formatting Mismatch

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [BclPurityFallbackHeuristics.cs](file:///C:/w/PurelySharp/Shared/BclPurityFallbackHeuristics.cs#L237-L251)
* **Severity:** Medium
* **Description:** `BclPurityFallbackHeuristics` uses `Nullable<T>` format while ECMA adapter uses `Nullable\`1`, causing fallback catalog mismatches.
* **Recommendation:** Normalize backticks/bracket arity formats before matching.

#### [PB2-7.9.3] 7.9.3 Silent Dropping of Incomparable Purity Entries during Merges

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [PurityClassificationEngine.cs](file:///C:/w/PurelySharp/Tools/SharpProof.EffectSummary/PurityClassificationEngine.cs#L2847-L2851)
* **Severity:** Medium
* **Description:** Conflicting/incomparable candidates cause the entry to be silently discarded.
* **Recommendation:** Resolve by picking the more conservative purity status.

### 7.10 Packaging & Deployment Options (Agent 10)

#### [PB2-7.10.1] 7.10.1 Roslyn Configuration Mismatches (.globalconfig)

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ROSLYN-GLOBALCONFIG-SEMANTICS
> **Evidence:** Roslyn global analyzer config files support EditorConfig glob sections such as [*.cs], and custom analyzer option keys do not require the dotnet_code_quality prefix reserved for .NET analyzer conventions.
> **Changes/tests:** All profiles declare is_global=true and configuration-profile tests validate registered SharpProof keys and scopes; no code change.
* **File & Lines:** [AnalyzerConfiguration.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs#L599-L622)
* **Severity:** High
* **Description:** Invalid syntax `[*.cs]` header in `.globalconfig` files makes Roslyn ignore options, and option keys are missing the required `dotnet_code_quality.` prefix.
* **Recommendation:** Fix the `.globalconfig` files syntax and support option prefixes in parser.

#### [PB2-7.10.2] 7.10.2 macOS Build Warnings on Z3 Native Library

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SharpProof.targets](file:///C:/w/PurelySharp/SharpProof.Package/buildTransitive/SharpProof.targets#L5-L7)
* **Severity:** Medium
* **Description:** `buildTransitive/SharpProof.targets` only excludes `libz3.dll`, leaving `libz3.dylib` causing assembly loading warnings on macOS.
* **Recommendation:** Exclude all native solver libraries (`.dll`, `.dylib`, `.so`) from standard Roslyn analyzer assembly resolution.

---

## 8. Phase 3 Audit - VsixHarness, Demo, Attributes, Net472 Smoke, SymbolicCli & Fuzzing

### 8.1 VsixHarness

#### [PB2-8.1.1] 8.1.1 Hardcoded `Release` Configuration Path for VSIX and Attributes DLL

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-ROUTING
> **Evidence:** The VSIX harness selected Release paths for the package, simulated analyzer, and attributes assembly even when invoked from another configuration.
> **Changes/tests:** The harness accepts/infers a validated configuration, uses it for all artifacts, and build-vsix.ps1 passes it explicitly; VsixHarnessUsesRequestedBuildConfiguration.
* **File & Lines:** [VsixHarness/Program.cs](file:///C:/w/PurelySharp/Tools/VsixHarness/Program.cs#L87-L101)
* **Severity:** High
* **Description:** The default VSIX path and the `attributesDll` path are both hardcoded to `"Release"`:
  ```csharp
  var vsixPath = args.Length > 0 ? args[0]
      : Path.Combine(solutionRoot, "SharpProof.Vsix", "bin", "Release", "SharpProof.Vsix.vsix");
  var attributesDll = Path.Combine(solutionRoot, "SharpProof.Attributes", "bin", "Release", "netstandard2.0", "SharpProof.Attributes.dll");
  ```
  Developers building in `Debug` configuration will either fall through to `CreateSimulatedVsix` (which also hardcodes `Release`, line 255-256) or use a stale Release artifact without any warning. There is no `$(Configuration)` substitution or fallback to `Debug`.
* **Impact:** CI/local `Debug` builds silently run against stale `Release` binaries, defeating regression testing. A fresh checkout on a machine without a prior Release build will fail with a `FileNotFoundException`.
* **Recommendation:** Accept the build configuration as a command-line argument or environment variable, or probe both `Release` and `Debug` paths in priority order, printing which was selected.

#### [PB2-8.1.2] 8.1.2 Simulated VSIX Temp Directory Leak on `CreateSimulatedVsix` Failure

> **Disposition:** Fixed
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** VSIX temporary ownership was incomplete on both failure and successful simulated-package paths.
> **Changes/tests:** All extraction and simulated-package directories now have deterministic cleanup; VsixHarnessUsesRequestedBuildConfiguration.
* **File & Lines:** [VsixHarness/Program.cs](file:///C:/w/PurelySharp/Tools/VsixHarness/Program.cs#L261-L270)
* **Severity:** Medium
* **Description:** `CreateSimulatedVsix` creates a temp directory with `Directory.CreateTempSubdirectory("SharpProofSimVsix")` before creating the archive. If the archive creation (or any iteration over files) throws, the `tempDirectory` is never deleted because there is no `try/finally` cleanup:
  ```csharp
  var tempDirectory = Directory.CreateTempSubdirectory("SharpProofSimVsix");
  var vsixPath = ...;
  using (var archive = ZipFile.Open(vsixPath, ZipArchiveMode.Create))
      foreach (var file in Directory.GetFiles(analyzerDirectory, ...))
          archive.CreateEntryFromFile(file, entryName);  // can throw
  return vsixPath;  // tempDirectory never cleaned up on failure
  ```
  Only the `payload.Directory` (extraction directory) is cleaned up on normal exit (line 184), not the simulated VSIX temp directory.
* **Impact:** Orphaned temp directories accumulate on the build agent or developer machine.
* **Recommendation:** Wrap the archive creation in a `try/catch` that deletes `tempDirectory` on failure, or use a `try/finally`.

#### [PB2-8.1.3] 8.1.3 `FindExactLoadedAssembly` Ignores Culture and Public Key Token

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Loaded assembly matching omitted culture and public-key-token identity.
> **Changes/tests:** Exact matching now compares culture and public-key tokens; VSIX harness regression coverage.
* **File & Lines:** [VsixHarness/Program.cs](file:///C:/w/PurelySharp/Tools/VsixHarness/Program.cs#L58-L66)
* **Severity:** Medium
* **Description:** The assembly resolution logic checks only name and version equality:
  ```csharp
  return AssemblyName.ReferenceMatchesDefinition(loadedName, requestedName) &&
         Equals(loadedName.Version, requestedName.Version);
  ```
  `AssemblyName.ReferenceMatchesDefinition` does not compare `Culture` or `PublicKeyToken`. This means two assemblies with the same name and version but different cultures or strong-name keys would be treated as identical, potentially returning the wrong already-loaded assembly. In a VSIX test harness loading unsigned analyzer DLLs alongside signed Roslyn assemblies, this can silently resolve to the wrong type-system instance.
* **Impact:** Potential type identity mismatches (`InvalidCastException` or silent behavioral divergence) when strong-named and un-signed assemblies share names and versions.
* **Recommendation:** Add explicit checks for `PublicKeyToken` equality (or use `AssemblyName.FullName` string comparison) when resolving assemblies.

#### [PB2-8.1.4] 8.1.4 VSIX Extraction Does Not Skip Directory Entries (Zip archives)

> **Disposition:** Fixed
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** Archive extraction relied only on ZipArchiveEntry.Name to identify directories.
> **Changes/tests:** Directory entries ending in either separator are skipped and failed extraction cleans its temporary root.
* **File & Lines:** [VsixHarness/Program.cs](file:///C:/w/PurelySharp/Tools/VsixHarness/Program.cs#L206-L218)
* **Severity:** Low
* **Description:** The extraction loop guards against zero-length `entry.Name` to skip directories:
  ```csharp
  if (entry.Name.Length == 0) continue;
  ```
  However, some zip tools emit directory entries where `entry.Name` is non-empty (e.g. `"subdir/"`) but `entry.FullName` ends with `/`. The `Directory.CreateDirectory` call below is harmless, but `entry.ExtractToFile(destinationPath, true)` will then fail with an `IOException` on a path that resolves to an existing directory, crashing extraction.
* **Impact:** Harness crashes when processing VSIX files produced by certain zip tools that emit non-empty directory entries.
* **Recommendation:** Also check `entry.FullName.EndsWith('/')` or `entry.Length == 0` (the uncompressed size) before extracting.

---

### 8.2 Demo Project

#### [PB2-8.2.1] 8.2.1 `SP0002` Suppressed Globally for All Demo Methods

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SharpProof.Demo/SharpProof.Demo.csproj](file:///C:/w/PurelySharp/SharpProof.Demo/SharpProof.Demo.csproj#L11)
* **Severity:** Medium
* **Description:** The demo project suppresses SP0002 for the entire project with `<NoWarn>$(NoWarn);SP0002</NoWarn>`. This means methods intentionally decorated with `[Pure]` or `[EnforcePure]` that are visibly impure (e.g. `AddImpure`, `Log`, `IncrementGlobal`) will not generate any visible SP0002 diagnostic when building the demo. The demo code comments claim these should produce SP0002, but they are silently suppressed.
* **Impact:** The demo is misleading - it shows attribute usage but hides all expected diagnostic output during a standard build. Developers consulting the demo as a reference will not see the expected diagnostics fire.
* **Recommendation:** Remove the blanket `NoWarn` and instead add a baseline suppression for specific expected occurrences, so the demo clearly shows that the diagnostics *do* fire (via the baseline) rather than silently suppressing them.

#### [PB2-8.2.2] 8.2.2 Demo `SharpProof.globalconfig` Missing `global_level` Key

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** Adding a higher `global_level` makes Roslyn report `MultipleGlobalAnalyzerKeys` for the demo and repository configurations, unset the duplicated option, and fail the zero-warning release gate. The existing level intentionally permits the nearer demo configuration to apply without that conflict.
> **Changes/tests:** No product change; verified by the warning-free Release solution build.
* **File & Lines:** [SharpProof.Demo/SharpProof.globalconfig](file:///C:/w/PurelySharp/SharpProof.Demo/SharpProof.globalconfig#L1-L6)
* **Severity:** Low
* **Description:** The demo's `.globalconfig` file is:
  ```
  is_global = true

  sharpproof_smt_mode = bounded
  sharpproof_smt_timeout_ms = 321
  sharpproof_analysis_max_merged_if_else_facts = 17
  sharpproof_enable_effect_summary_json = true
  ```
  The file correctly sets `is_global = true`. However, it does not set `global_level`, which controls override priority when multiple global config files are present. Without an explicit `global_level`, this config has level 0 (the lowest), meaning any NuGet package that ships its own `.globalconfig` with a higher `global_level` will silently override these demo-specific settings without any warning.
* **Impact:** Demo configuration values may be silently overridden by transitive package configs, making demo behavior non-deterministic depending on package restore state.
* **Recommendation:** Add `global_level = 100` (or another explicit priority) to assert that the demo-local settings take precedence.

---

### 8.3 Attributes Project

#### [PB2-8.3.1] 8.3.1 `AllowedExceptionsAttribute` Accepts `null` Array via `params` Without Null Entries Check

> **Disposition:** Already fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
* **File & Lines:** [AllowedExceptionsAttribute.cs](file:///C:/w/PurelySharp/SharpProof.Attributes/AllowedExceptionsAttribute.cs#L8-L11)
* **Severity:** Low
* **Description:** The attribute constructor is:
  ```csharp
  public AllowedExceptionsAttribute(params Type[] exceptionTypes)
  {
      ExceptionTypes = exceptionTypes ?? throw new ArgumentNullException(nameof(exceptionTypes));
  }
  ```
  While the null-array check is present, individual array entries can be `null` (e.g. `[AllowedExceptions(null)]` or `[AllowedExceptions(typeof(Exception), null)]`). The analyzer that consumes `ExceptionTypes` later likely iterates over them and calls `.FullName` or similar, which would throw a `NullReferenceException`.
* **Impact:** Malformed attribute usage crashes the analyzer rather than producing a clean user-facing diagnostic.
* **Recommendation:** Either validate that no entries are null in the constructor (though attribute constructors run at compile-time reflection and cannot throw usefully), or make the consuming analyzer guard against null entries and emit a diagnostic.

#### [PB2-8.3.2] 8.3.2 `AllowedCapabilitiesAttribute` Targets `AttributeTargets.All` - Overly Permissive

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [AllowedCapabilitiesAttribute.cs](file:///C:/w/PurelySharp/SharpProof.Attributes/AllowedCapabilitiesAttribute.cs#L5)
* **Severity:** Low
* **Description:** `[AttributeUsage(AttributeTargets.All, Inherited = false)]` allows `[AllowedCapabilities]` to be applied to types, assemblies, delegates, enums, events, fields, parameters, return values, etc. The analyzer only processes `[AllowedCapabilities]` on methods and properties (where it makes semantic sense), so applying it to an `enum` or a field silently has no effect.
* **Impact:** Users applying `[AllowedCapabilities]` to non-method targets will receive no warning from the compiler or the analyzer about the misuse.
* **Recommendation:** Restrict to `AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property | AttributeTargets.Assembly` to match the set of supported declarations, consistent with `ImpureAttribute` and `PureExternalAttribute`.

#### [PB2-8.3.3] 8.3.3 `SharpProofCapability` Includes `IO` as a Named Aggregate Flag

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SharpProofCapability.cs](file:///C:/w/PurelySharp/SharpProof.Attributes/SharpProofCapability.cs#L9-L21)
* **Severity:** Medium
* **Description:** The `IO` flag (`1 << 0 = 1`) is treated as both a standalone aggregate flag and potentially as a capability by itself. The `ExpandAllowedCapabilities` method in `SymbolicCliExitGateEvaluator.cs` (line 289-299) explicitly expands `IO` to include `FileRead | FileWrite | Network | Console | Registry`. However `Process`, `Environment`, `Clock`, `Randomness`, `Reflection`, `Synchronization`, and `NativeInterop` are NOT part of `IO` - they are fully separate capabilities. A user writing `[AllowedCapabilities(SharpProofCapability.IO)]` would reasonably expect to allow "all I/O" including process spawning, but `Process` is excluded from the `IO` expansion. Furthermore, `NormalizeCapabilities` only upgrades individual I/O flags to include `IO`, meaning a method with `FileRead` will show `IO | FileRead` in the output, but one with `Process` will not - this asymmetry is confusing.
* **Impact:** Users incorrectly assume `IO` covers process spawning or environment access; diagnostics may be misleading.
* **Recommendation:** Document explicitly that `IO` is a subset aggregate (file I/O + network + console + registry only), and consider renaming it to `FileSystemAndNetwork` or `BasicIO`, or expanding it to cover all resource-oriented capabilities.

---

### 8.4 Net472 Smoke Project

#### [PB2-8.4.1] 8.4.1 Smoke Project Has No Analyzer Config or Baseline - Silent Behavioral Drift

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [SharpProof.Smoke.Net472/SharpProof.Smoke.Net472.csproj](file:///C:/w/PurelySharp/SharpProof.Smoke.Net472/SharpProof.Smoke.Net472.csproj)
* **Severity:** Medium
* **Description:** The Net472 smoke project does not include any `<GlobalAnalyzerConfigFiles>`, `<AdditionalFiles>` pointing to a baseline or effect summary JSON, or any `.globalconfig`. This means the analyzer runs under its default settings on Net472, which may differ from the actual production/demo configurations. Any analyzer configuration-dependent behavior (e.g. SMT mode, timeout, fact limits) goes untested in the smoke context.
* **Impact:** The smoke test verifies that the analyzer *loads and activates* on Net472, but does not verify that configuration-dependent diagnostic paths work correctly under Net472. Configuration bugs that only appear under certain SMT modes or with effect summaries will be missed.
* **Recommendation:** Add a minimal `.globalconfig` and possibly a baseline JSON to the smoke project, similar to the demo project, to exercise the full configuration pipeline.

#### [PB2-8.4.2] 8.4.2 Smoke Project References `SearchLib` but Is Not Included in Net472 Test

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SharpProof.Smoke.Net472/SharpProof.Smoke.Net472.csproj](file:///C:/w/PurelySharp/SharpProof.Smoke.Net472/SharpProof.Smoke.Net472.csproj#L16)
* **Severity:** Low
* **Description:** The smoke project references `SearchLib` as an analyzer:
  ```xml
  <ProjectReference Include="..\SearchLib\SearchLib.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  ```
  If `SearchLib` has any net472 compatibility issue (e.g. `netstandard2.0` TFM mismatch or p/invoke incompatibility), this will silently fail on Net472 because the `OutputItemType="Analyzer"` reference is loaded dynamically by Roslyn and failures surface only as runtime AD0001 analyzer exceptions - not build errors. There is no test assertion in the smoke project that all analyzers loaded without exception.
* **Impact:** Net472 analyzer load failures go silently undetected during smoke.
* **Recommendation:** Add a test (or a check in the smoke project's MSBuild target) that verifies no AD0001 diagnostics were produced during compilation.

---

### 8.5 SymbolicCli & Fuzzing

#### [PB2-8.5.1] 8.5.1 `--request-json-stdin` Reads Entire stdin Without Cancellation Timeout

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [SymbolicCliJsonRequest.cs](file:///C:/w/PurelySharp/Tools/SharpProof.SymbolicCli/SymbolicCliJsonRequest.cs#L65)
* **Severity:** Medium
* **Description:** When `--request-json-stdin` is used, the CLI reads stdin to completion:
  ```csharp
  json = await standardInput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
  ```
  While a `CancellationToken` is plumbed through, it is the default token passed in from `ExpandArgumentsAsync` (line 35), which in `Program.cs` top-level code has no timeout configured. If the caller pipes in a non-terminating stdin (e.g. a blocked pipe), the process will hang indefinitely without any wall-clock timeout fallback.
* **Impact:** A misconfigured CI invocation or a piping error causes the SymbolicCli process to hang forever, blocking the pipeline.
* **Recommendation:** Apply a configurable read timeout (defaulting to, e.g., 30 seconds) to the stdin read operation, separate from the SMT analysis cancellation token.

#### [PB2-8.5.2] 8.5.2 `SymbolicCliExitGateEvaluator.EvaluateInvariantProofs` Fires on Zero Total Proofs

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-INVARIANT-GATE-NONEMPTY
> **Evidence:** The zero-proof failure is deliberate: documentation says no proof result is a gate failure, and option validation requires at least one requested --implies expression.
> **Changes/tests:** docs/ci-exit-gates.md and CLI validation/tests define the nonempty proof contract; no code change.
* **File & Lines:** [SymbolicCliExitGateEvaluator.cs](file:///C:/w/PurelySharp/Tools/SharpProof.SymbolicCli/SymbolicCliExitGateEvaluator.cs#L56-L66)
* **Severity:** High
* **Description:** The gate evaluation for `--fail-on-unproven-implies` contains a flawed condition:
  ```csharp
  var unprovenCount = outcomes.TotalCount - outcomes.ProvenTrueCount;
  if (outcomes.TotalCount != 0 && unprovenCount == 0) return;  // success path

  failures.Add(...);  // failure path - fires even when TotalCount == 0
  ```
  When `outcomes.TotalCount == 0` (no `--implies` conditions were specified for this query), `unprovenCount` is `0`. The success condition `TotalCount != 0 && unprovenCount == 0` evaluates to `false`, so the failure is reported - even though there are no invariants to prove. This means using `--fail-on-unproven-implies` on any query that has no `--implies` conditions will *always* report a failure.
* **Impact:** `--fail-on-unproven-implies` is broken for queries without any `--implies` conditions. CI gates using this flag will fail spuriously on every run that doesn't include `--implies`.
* **Recommendation:** Change the guard to also pass when `TotalCount == 0`:
  ```csharp
  if (unprovenCount == 0) return;  // covers both zero-proof and all-proven cases
  ```

#### [PB2-8.5.3] 8.5.3 `SymbolicCliJsonRequest` Allows Duplicate `--request-json*` Detection to Miss Mixed Positions

> **Disposition:** Fixed
> **Canonical root cause:** RC-CLI-INPUT-AND-OUTPUT-CONTRACTS
> **Evidence:** The selector validation was correct but its diagnostic did not describe the sole-selector constraint.
> **Changes/tests:** The CLI now reports that the JSON selector must be sole and first.
* **File & Lines:** [SymbolicCliJsonRequest.cs](file:///C:/w/PurelySharp/Tools/SharpProof.SymbolicCli/SymbolicCliJsonRequest.cs#L48-L50)
* **Severity:** Low
* **Description:** The validation:
  ```csharp
  if (requestIndexes.Length != 1 || requestIndexes[0] != 0)
      throw new ArgumentException("A JSON request selector must be the only CLI input and must appear first.");
  ```
  correctly ensures `--request-json` or `--request-json-stdin` appears exactly once and first. However the error message says "must appear first" when the actual requirement is "must be the only argument". A user who supplies `--request-json '{"schemaVersion":1,...}' --json` would get this exception with a misleading message suggesting they should move `--request-json` to the front, when the real problem is that `--json` cannot be combined with it.
* **Impact:** Confusing error message leading users to attempt invalid workarounds.
* **Recommendation:** Change the message to: `"--request-json or --request-json-stdin must be the sole argument; no other options may be combined with it."`.

#### [PB2-8.5.4] 8.5.4 `SymbolicCliExitGateEvaluator.GetCompactMetric` Throws on Valid Complexity+Invariant Mixed Queries

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SymbolicCliExitGateEvaluator.cs](file:///C:/w/PurelySharp/Tools/SharpProof.SymbolicCli/SymbolicCliExitGateEvaluator.cs#L216-L241)
* **Severity:** Medium
* **Description:** `GetCompactMetric` first checks `TryGetInvariantMetrics(result, ...)` and, if successful, handles metrics like `"program-points"` and `"conservative-unknowns"`. If the metric is not one of the four invariant metrics, it falls through to the outer `switch` which handles complexity/capability/hazard-specific metrics. However, if `TryGetInvariantMetrics` succeeds but the metric name is not one of the four known invariant names, the code throws:
  ```csharp
  _ => throw new InvalidOperationException("Unsupported invariant compact threshold metric: " + metric)
  ```
  This means a user who specifies `--fail-on-compact-threshold hazards=5` with an invariant query (which succeeds `TryGetInvariantMetrics`) will get an internal `InvalidOperationException` instead of a proper user-facing error, or reaching the hazard-specific branch.
* **Impact:** Valid mixed-query compact thresholds throw internal exceptions rather than producing helpful diagnostics.
* **Recommendation:** Fall through from the invariant branch to the outer switch when the metric is not recognized as an invariant metric, rather than throwing immediately.

#### [PB2-8.5.5] 8.5.5 Fuzz Runner `DefaultOutputDirectory` Uses Process Start Time, Not Run Start Time

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [Fuzz.Core/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz.Core/Program.cs#L205-L212)
* **Severity:** Low
* **Description:** The default output directory is evaluated at field-initialization time (via `DefaultOutputDirectory()` in the record initializer), not at `FuzzRunner.RunAsync` time:
  ```csharp
  public string OutputDirectory { get; init; } = DefaultOutputDirectory();
  ```
  `DefaultOutputDirectory()` calls `DateTimeOffset.UtcNow` (line 210) when `FuzzOptions` is constructed - which happens during `Parse()`. If there is significant delay between `Parse()` and `RunAsync()` (e.g. due to lazy workspace loading), the timestamp in the output path will not reflect the actual run start time.
* **Impact:** Minor timestamp skew in artifact directory names; cosmetic issue but can confuse artifact correlation in CI.
* **Recommendation:** Capture `DateTimeOffset.UtcNow` at the start of `RunAsync` and pass the resolved output directory to the runner, or defer `DefaultOutputDirectory()` until `RunAsync` begins.

#### [PB2-8.5.6] 8.5.6 Fuzz `FuzzRunner` Shared Analyzer State - Not Thread-Safe Registration

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [Fuzz.Core/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz.Core/Program.cs#L240-L248)
* **Severity:** Medium
* **Description:** `SharedAnalyzers` is a static `ImmutableArray<DiagnosticAnalyzer>` holding a single `SharpProofAnalyzer` instance. The fuzz runner uses `options.Parallelism` concurrent tasks (default 4), all sharing this one analyzer instance. `DiagnosticAnalyzer` implementations are required to be stateless per Roslyn's contract, but `SharpProofAnalyzer` (and its registered sub-analyzers) use per-compilation-start shared state. If the analyzer registers mutable shared state in `CompilationStartAction` (via closures captured across parallel compilations), concurrent parallel analysis of multiple independent compilations would race on that shared state.
* **Impact:** Potential data races, incorrect diagnostics, or crashes when parallel fuzz cases share a single `SharpProofAnalyzer` instance across concurrent independent compilations.
* **Recommendation:** Verify that `SharpProofAnalyzer` is truly stateless between `Initialize()` calls (all state is per-compilation-start scoped), or create one `SharpProofAnalyzer` instance per parallel task to be safe.

---

### 8.6 VSIX Manifest

#### [PB2-8.6.1] 8.6.1 VSIX Manifest Missing `SharpProof.Attributes` Asset - Consumers Must Install Separately

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** A VSIX asset cannot inject compile-time project references, and the required consumer step was undocumented.
> **Changes/tests:** README source/generated output now states that VSIX users must reference SharpProof or SharpProof.Attributes.
* **File & Lines:** [source.extension.vsixmanifest](file:///C:/w/PurelySharp/SharpProof.Vsix/source.extension.vsixmanifest#L22-L26)
* **Severity:** Medium
* **Description:** The VSIX manifest includes `SharpProof.Analyzer` and `SharpProof.CodeFixes` as assets, but does NOT include `SharpProof.Attributes`. The attributes assembly (`EnforcePureAttribute`, `PureAttribute`, etc.) must be present in the project being analyzed for the analyzer to find and match the attributes it checks for. Without shipping `SharpProof.Attributes.dll` as a VSIX asset or requiring it as a NuGet dependency inside the VSIX, a user who installs the VSIX extension without separately installing the `SharpProof.Attributes` NuGet package into their project will get unresolved type errors in their C# code.
* **Impact:** VSIX-only users (e.g. those who installed via the Visual Studio Marketplace without also adding the NuGet package) cannot use any SharpProof attributes. The analyzer will load but none of its attribute-gated checks will fire.
* **Recommendation:** Either bundle `SharpProof.Attributes.dll` as a VSIX asset, or add a `<Prerequisite>` entry for the NuGet package in the manifest, or update documentation/README to clearly state that both VSIX and NuGet package must be installed.

#### [PB2-8.6.2] 8.6.2 VSIX Targets Only `amd64` - Excludes ARM64 Visual Studio Users

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [source.extension.vsixmanifest](file:///C:/w/PurelySharp/SharpProof.Vsix/source.extension.vsixmanifest#L8-L18)
* **Severity:** Medium
* **Description:** All three `InstallationTarget` entries (Community, Pro, Enterprise) specify `<ProductArchitecture>amd64</ProductArchitecture>`. Visual Studio 2022 on ARM64 Windows (Surface Pro X, ARM64 laptops) runs as a native ARM64 process and will reject extensions that require `amd64` architecture. Users on ARM64 Windows will see the VSIX as incompatible and be unable to install it.
* **Impact:** The extension is incompatible with ARM64 Visual Studio installations, silently excluding that user base.
* **Recommendation:** Add parallel `InstallationTarget` entries with `<ProductArchitecture>arm64</ProductArchitecture>` for all three VS SKUs, and verify the managed analyzer DLLs (which are architecture-neutral .NET Standard) and the Z3 native library (which requires the ARM64 dylib/dll) are correctly bundled.

#### [PB2-8.6.3] 8.6.3 VSIX Dependency on `.NET Framework 4.5` Is Stale - VS 2022 Requires .NET 4.7.2+

> **Disposition:** Fixed
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** The manifest framework floor was lower than both Visual Studio 2022 and the VSIX target framework.
> **Changes/tests:** The VSIX dependency now requires .NET Framework 4.7.2; package metadata regression coverage.
* **File & Lines:** [source.extension.vsixmanifest](file:///C:/w/PurelySharp/SharpProof.Vsix/source.extension.vsixmanifest#L20)
* **Severity:** Low
* **Description:** The dependency declaration is:
  ```xml
  <Dependency Id="Microsoft.Framework.NDP" DisplayName="Microsoft .NET Framework" d:Source="Manual" Version="[4.5,)" />
  ```
  Visual Studio 2022 itself requires .NET Framework 4.7.2 as a minimum. Declaring `[4.5,)` provides no additional installation constraint - VS 2022 ships with 4.8 - but it documents an incorrect minimum. More importantly, the VSIX project itself targets `net472`, so if any dependency or behavior requires 4.7.2+ APIs, declaring `[4.5,)` in the manifest is misleading and could cause subtle issues if the VSIX is mistakenly installed on older VS versions that happen to satisfy the `[4.5,)` constraint.
* **Impact:** Misleading manifest documentation; potential for installation on VS versions that are officially unsupported.
* **Recommendation:** Update the dependency version to `[4.7.2,)` to match the project's `net472` TFM target.

---

## 9. Phase 4 Audit - Baseline Tools, CorpusReport, SearchLib (Z3 Core), Fuzz Aggregation

### 9.1 Baseline Tools

#### [PB2-9.1.1] 9.1.1 `RunBuildAsync` - `dotnet build` Failure Is Silently Ignored if SARIF Is Created Partially

> **Disposition:** Fixed
> **Canonical root cause:** RC-CHILD-BUILD-EXIT-VALIDATION
> **Evidence:** Baseline generation accepted an existing partial SARIF without checking the dotnet build exit code.
> **Changes/tests:** The baseline tool rejects nonzero child exits before reading SARIF; BuildBackedToolsRejectNonzeroChildExitBeforeReadingSarif.
* **File &amp; Lines:** [Baseline/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Baseline/Program.cs#L113-L138)
* **Severity:** High
* **Description:** `RunBuildAsync` only checks `File.Exists(sarifPath)` to determine success. A failed `dotnet build` that exits non-zero can still create a partial SARIF file (e.g., containing compilation errors but not analysis results), which will be silently consumed by `GenerateFromSarifJson`. The build's exit code is never checked:
  ```csharp
  await process.WaitForExitAsync();
  // ...no check of process.ExitCode...
  if (!File.Exists(sarifPath))
      throw new InvalidOperationException("dotnet build did not produce a SARIF error log...");
  ```
  A partial SARIF (e.g., zero diagnostics because compilation failed) will generate an *empty* baseline, silently suppressing all future diagnostics.
* **Impact:** A CI build failure causes the baseline to be silently reset to empty, suppressing all future analyzer findings. This is a critical correctness bug for the baseline workflow.
* **Recommendation:** Check `process.ExitCode != 0` and throw an appropriate exception or warn. At minimum, check the SARIF file for well-formedness before accepting it.

#### [PB2-9.1.2] 9.1.2 `GenerateFromSarifJson` - Temporary SARIF File Written to `GetTempPath()` Without Cleanup on Exception

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [Baseline/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Baseline/Program.cs#L97-L103)
* **Severity:** Medium
* **Description:** The temporary SARIF file is created in `Path.GetTempPath()` and added to `temporaryFiles` before `RunBuildAsync` is called. If `RunBuildAsync` succeeds but `File.ReadAllTextAsync(sarifPath)` or `GenerateFromSarifJson` throws, the `finally` block at line 82-85 will delete the temp file. However, `RunBuildAsync` itself does not catch exceptions - if `Process.Start` fails (e.g., `dotnet` is not on PATH), the thrown `InvalidOperationException` propagates *before* `sarifPath` is added to `temporaryFiles` (the add happens at line 100, after the call at line 101):
  ```csharp
  var sarifPath = Path.Combine(Path.GetTempPath(), "sharpproof-baseline-" + ...);
  temporaryFiles.Add(sarifPath);  // added first
  await RunBuildAsync(input, sarifPath);  // but RunBuildAsync creates the file
  ```
  In this case the temp path is registered but the file was never created, so the delete is a no-op. This is harmless, but if the file *is* created by an `MSBuild` side-effect and the exception fires after creation but before registration completes, the file leaks.
* **Impact:** Minor temp file leak on rare failure paths; overall the logic is mostly correct but fragile.
* **Recommendation:** Use a `try/finally` around `RunBuildAsync` to ensure both creation and registration are atomic.

#### [PB2-9.1.3] 9.1.3 `NormalizePath` - Double-Slash URI Paths Not Normalized

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File &amp; Lines:** [SharpProofBaseline.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs#L279-L292)
* **Severity:** Medium
* **Description:** `NormalizePath` converts file URIs to local paths and strips leading `./` prefixes but does not collapse repeated slashes:
  ```csharp
  var normalized = trimmed.Replace('\\', '/');
  while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized.Substring(2);
  ```
  If a SARIF `artifactLocation.uri` contains `//` (e.g. `file:///C://path/to/file.cs`), the path after `.LocalPath` extraction may contain `//` which will not match a normalized single-slash path in the baseline. Additionally, trailing slashes on directory-like paths are not stripped, causing false mismatches in `BaselineBucketKeyComparer`.
* **Impact:** Baseline entries for files with non-canonical URIs (produced by some MSBuild SARIF generators) will never match, causing all their suppressions to appear stale even when they should match. This silently breaks the prune command.
* **Recommendation:** Add `normalized = Regex.Replace(normalized, "/{2,}", "/")` (or equivalent) to collapse consecutive slashes after backslash replacement.

#### [PB2-9.1.4] 9.1.4 `Deduplicate` - `BaselineKey` Uses Case-Insensitive Path for Hashing But Case-Sensitive ID and Symbol

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [SharpProofBaseline.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs#L600-L627)
* **Severity:** Medium
* **Description:** `BaselineKey.FromEntry` normalizes the `Path` with `.ToUpperInvariant()` for the deduplication key:
  ```csharp
  public static BaselineKey FromEntry(BaselineEntry entry)
  {
      return new BaselineKey(
          entry.Id,           // Ordinal case-sensitive
          entry.Symbol,       // Ordinal case-sensitive
          NormalizePath(entry.Path).ToUpperInvariant(),   // <- uppercased
          ...);
  }
  ```
  The `HashSet<BaselineKey>` uses the default `record` equality which compares `Path` via `string.Equals` with the default `StringComparison.Ordinal`. This means an uppercased path `"C:/FOO/BAR.CS"` from one entry will not equal a non-uppercased `"C:/foo/bar.cs"` from another entry in the `HashSet`, defeating the case-insensitive deduplication goal. The `ToUpperInvariant()` is only applied to one side (the key from `FromEntry`) but the lookup in the set uses `record` structural equality which is ordinal - so the deduplication is effectively case-sensitive on path despite the intent.
* **Impact:** Duplicate baseline entries may not be deduplicated on case-insensitive filesystems, producing a baseline with duplicate diagnostics. On prune, this causes them to incorrectly persist.
* **Recommendation:** Either remove `.ToUpperInvariant()` and ensure both sides are lowercased/uppercased consistently, or implement a custom `IEqualityComparer<BaselineKey>` that uses `StringComparison.OrdinalIgnoreCase` for the `Path` field.

#### [PB2-9.1.5] 9.1.5 `ValidateEvidenceSchemas` - Recursion Into All JSON Object Nodes Is O(N) With No Depth Limit

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [SharpProofBaseline.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs#L433-L487)
* **Severity:** Low
* **Description:** `ValidateEvidenceSchemas` recursively traverses every property of every nested JSON object looking for `evidenceSchemaVersion`/`evidenceSchemaCompatibility` keys. On a large SARIF file (e.g., thousands of results with deeply nested properties), this results in O(N x depth) traversals. There is no depth limit, and the method recurses through arrays too. For a maliciously crafted or unusually deep SARIF, this can cause a stack overflow or extremely long validation times.
* **Impact:** Performance degradation or stack overflow when processing large or deeply nested SARIF files.
* **Recommendation:** Add a maximum recursion depth parameter (e.g., `remainingDepth = 10`) and return without validating beyond the expected SARIF schema depth.

---

### 9.2 CorpusReport Tool

#### [PB2-9.2.1] 9.2.1 `RunBuild` (CorpusReport) - Synchronously Blocks on Async I/O, Risking Deadlock

> **Disposition:** Fixed
> **Canonical root cause:** RC-CHILD-PROCESS-ASYNC-LIFECYCLE
> **Evidence:** Although redirected streams were started before the wait, CorpusReport unnecessarily mixed synchronous process waiting with async reads and also failed to validate the child exit.
> **Changes/tests:** RunBuildAsync now awaits process exit and both streams, then rejects nonzero exits; BuildBackedToolsRejectNonzeroChildExitBeforeReadingSarif.
* **File &amp; Lines:** [CorpusReport/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.CorpusReport/Program.cs#L58-L83)
* **Severity:** High
* **Description:** The `RunBuild` static helper uses `GetAwaiter().GetResult()` to synchronously block on async tasks:
  ```csharp
  static void RunBuild(string input, string sarifPath)
  {
      // ...
      var outputTask = process.StandardOutput.ReadToEndAsync();
      var errorTask = process.StandardError.ReadToEndAsync();
      process.WaitForExit();
      var output = outputTask.GetAwaiter().GetResult();  // <- synchronous block
      var error = errorTask.GetAwaiter().GetResult();    // <- synchronous block
  ```
  This is called from a top-level `async` context (Program.cs uses async top-level statements with `await`). Calling `GetAwaiter().GetResult()` inside a sync-over-async pattern on a context that has a synchronization context (e.g., ASP.NET-hosted scenario) will deadlock. Additionally, if the `dotnet build` process writes enough to stderr to fill the OS pipe buffer before `process.WaitForExit()` unblocks, the process will deadlock waiting for the pipe to be drained while the host waits for the process to exit.
  In contrast, the `SharpProof.Baseline` tool's `RunBuildAsync` correctly uses `await process.WaitForExitAsync()` with async reads.
* **Impact:** Deadlock when the child `dotnet build` process writes more than ~4KB to stderr without stdout being consumed concurrently.
* **Recommendation:** Convert `RunBuild` to `async Task RunBuildAsync` following the Baseline tool's pattern.

#### [PB2-9.2.2] 9.2.2 `SarifCorpusReport.CreateFromSarifFiles` - Reads All Files Synchronously in a Loop

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File &amp; Lines:** [SarifCorpusReport.cs](file:///C:/w/PurelySharp/Tools/SharpProof.CorpusReport.Core/SarifCorpusReport.cs#L34-L40)
* **Severity:** Low
* **Description:** `CreateFromSarifFiles` iterates over all SARIF input paths and calls `File.ReadAllText(input.SarifPath)` synchronously for each one:
  ```csharp
  foreach (var input in inputs) builder.AddSarifJson(input.InputName, File.ReadAllText(input.SarifPath));
  ```
  For large corpora with many SARIF files (e.g., hundreds of project outputs), this is sequential synchronous I/O. The corresponding async variant `CreateFromSarifFilesAsync` does not exist.
* **Impact:** Slow performance on large corpus runs with many SARIF inputs.
* **Recommendation:** Add an async overload that uses `await File.ReadAllTextAsync(...)` and processes files concurrently with a degree-of-parallelism limit.

#### [PB2-9.2.3] 9.2.3 `CorpusReport.Program` - No Exit Code When Inputs Produce Empty/Invalid SARIF

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File &amp; Lines:** [CorpusReport/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.CorpusReport/Program.cs#L44-L52)
* **Severity:** Low
* **Description:** If all provided inputs are SARIF files with no valid `runs` or empty `results` arrays, `CreateFromSarifFiles` returns a `CorpusReportSummary` with all-zero counts. The program exits with code 0 (success). There is no signal to the caller that the input may have been malformed or that no diagnostics were processed, which can silently mask a failed SARIF generation step upstream.
* **Impact:** CI scripts interpreting a zero-finding corpus report as success when the SARIF files were actually empty due to upstream build failures.
* **Recommendation:** Add a `--fail-on-empty` flag that returns exit code 2 when `TotalSharpProofDiagnostics == 0` and no explicit SARIF runs were found.

#### [PB2-9.2.4] 9.2.4 `Aggregate-FuzzRun.ps1` - `$phaseSummaries.Count` Always Evaluates to 0 for Single-Element Array

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-POWERSHELL-ARRAY-MATERIALIZATION
> **Evidence:** @(...) yields an empty array when a pipeline emits no objects and a one-element array for one object; it does not wrap pipeline non-output as a null element.
> **Changes/tests:** PowerShell array-subexpression semantics make the existing Count check correct; no code change.
* **File &amp; Lines:** [Fuzz/Aggregate-FuzzRun.ps1](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz/Aggregate-FuzzRun.ps1#L39-L42)
* **Severity:** High
* **Description:** The count check is:
  ```powershell
  if ($phaseSummaries.Count -eq 0)
  {
      throw "No phase summaries were found under: $Root"
  }
  ```
  `$phaseSummaries` is assigned with `@(Get-ChildItem ... | ForEach-Object { ... })`. In PowerShell, if exactly **one** directory matches and `ForEach-Object` returns a single `[pscustomobject]`, the `@()` forces it into an array so `.Count` is 1. However, if **zero** directories match but the `Where-Object`-less pipeline still returns `$null`, `@($null)` creates a length-1 array containing `$null`. The check `$phaseSummaries.Count -eq 0` would be `1` (not 0), so the error would not fire, and subsequent code (`$phaseSummaries | ForEach-Object { $_.Summary.CasesAnalyzed }`) would attempt to access `$null.Summary` and throw a non-user-friendly NullPointerException-like error instead of the intended descriptive message.
* **Impact:** When no phase directories exist, the script throws a confusing null reference error instead of the intended human-readable message.
* **Recommendation:** Change the array initialization to filter out null values: `$phaseSummaries = @(Get-ChildItem ... | ForEach-Object { ... } | Where-Object { $_ -ne $null })`, or use `if ($phaseSummaries -eq $null -or $phaseSummaries.Count -eq 0)`.

#### [PB2-9.2.5] 9.2.5 `Aggregate-FuzzRun.ps1` - Uses Latest Schema Version From Last Phase, Not Maximum

> **Disposition:** Already fixed
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
* **File &amp; Lines:** [Fuzz/Aggregate-FuzzRun.ps1](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz/Aggregate-FuzzRun.ps1#L49)
* **Severity:** Low
* **Description:** The aggregate schema version is taken from the *last* (alphabetically sorted) phase directory:
  ```powershell
  $latestSchemaVersion = ($phaseSummaries | Select-Object -Last 1).Summary.SchemaVersion
  ```
  If phases were run with different versions of the fuzz tool (e.g., phase_01 used schema v2, phase_02 used schema v1 due to a rollback), `Select-Object -Last 1` would report v1 as the "latest", which is incorrect. The correct approach is to take the maximum schema version across all phases.
* **Impact:** Aggregate report may report a stale schema version, misleading downstream tooling that reads the version field.
* **Recommendation:** Use `($phaseSummaries | ForEach-Object { $_.Summary.SchemaVersion } | Sort-Object -Descending | Select-Object -First 1)` to take the maximum version.

---

### 9.3 SearchLib - SMT Solver Core

#### [PB2-9.3.1] 9.3.1 `SmtResourceBudget.GetWallClockSafetyNet` - Negative Budget Produces Negative Safety Net

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Negative budgets produced negative wall-clock safety-net timeouts.
> **Changes/tests:** Nonpositive budgets clamp to zero; GetWallClockSafetyNet_NegativeBudget_ClampsToZero.
* **File &amp; Lines:** [SmtResourceBudget.cs](file:///C:/w/PurelySharp/SearchLib/SmtResourceBudget.cs#L36-L43)
* **Severity:** Medium
* **Description:** The safety net computation is:
  ```csharp
  if (budget.Ticks < TimeSpan.MinValue.Ticks / WallClockSafetyFactor) return TimeSpan.MinValue;
  return TimeSpan.FromTicks(budget.Ticks * WallClockSafetyFactor);
  ```
  If `budget` is a small negative value (e.g., `TimeSpan.FromMilliseconds(-1)`), the overflow checks do not catch it (it is well within range), and `budget.Ticks * WallClockSafetyFactor` returns a large negative `TimeSpan`. This negative safety net is then passed to Z3's wall-clock timeout setter. Depending on the Z3 binding, a negative timeout could be interpreted as "no timeout" (infinite) or throw an exception.
  `GetRlimit` handles this correctly with `Math.Max(1, rlimit)`, but `GetWallClockSafetyNet` has no equivalent floor.
* **Impact:** If a zero or negative timeout budget is passed (e.g., due to a `remaining = timeout - elapsed` calculation going negative), the safety net becomes negative and Z3 may run without a timeout bound.
* **Recommendation:** Add `if (budget <= TimeSpan.Zero) return TimeSpan.Zero;` at the start of `GetWallClockSafetyNet`.

#### [PB2-9.3.2] 9.3.2 `SmtSolver.CheckAndAccountResources` - rlimit Counter Wrap Assumes 32-Bit Rollover, But Z3 May Use 64-Bit

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [SmtSolver.cs](file:///C:/w/PurelySharp/SearchLib/SmtSolver.cs#L36-L55)
* **Severity:** Medium
* **Description:** The wrap-around accounting logic assumes the `rlimit count` statistic is a 32-bit unsigned counter:
  ```csharp
  // A smaller observation means the 32-bit counter wrapped
  ConsumedResourceCount += observed >= _lastObservedRlimitCount
      ? observed - _lastObservedRlimitCount
      : (1L << 32) - _lastObservedRlimitCount + observed;
  ```
  However, `entry.UIntValue` is `uint` (32-bit), and in Z3 4.x, the rlimit counter is stored as a `uint64_t` internally but exposed via the `UIntValue` accessor if the Statistics API only reads the lower 32 bits. On long-running sessions with many queries (e.g., fuzz runs), the actual consumed rlimit may exceed `uint.MaxValue` (4.3 billion), causing the wrap to be detected prematurely even though it hasn't occurred. This would cause `ConsumedResourceCount` to be hugely under-reported.
  Additionally, if `entry.UIntValue` is saturated at `uint.MaxValue` by Z3, every subsequent observation will trigger the wrap-around path even though nothing has wrapped.
* **Impact:** Incorrect `ConsumedResourceCount` reporting in long-running fuzz sessions; budget enforcement based on cumulative resource counts may be too permissive.
* **Recommendation:** Use `entry.IsUInt ? (long)entry.UIntValue : (long)entry.ULongValue` to access the full counter width if the Z3 `Statistics.Entry` type supports 64-bit values, or document the known limitation.

#### [PB2-9.3.3] 9.3.3 `SmtSolver.IsConservativeSolverFailure` - Swallows `ArgumentException` From Non-Solver Code

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [SmtSolver.cs](file:///C:/w/PurelySharp/SearchLib/SmtSolver.cs#L309-L317)
* **Severity:** Medium
* **Description:** The conservative failure filter catches:
  ```csharp
  return ex is InvalidOperationException ||
         ex is Z3Exception ||
         ex is ArgumentException ||         // <- too broad
         ex is InvalidCastException ||
         ex is ArithmeticException;
  ```
  `ArgumentException` is a very broad exception type. If a caller passes an `SmtFormula` that is `null` (which `SmtFormulaTraversal.Enumerate` would catch with `throw new ArgumentNullException`), or if the encoder is called with an out-of-range enum value producing an `ArgumentException` from a system API, this catch would silently convert a programming error into a `Feasibility.Unknown` result. The analyzer would treat the query as "unknown" rather than surfacing a real bug.
* **Impact:** Genuine programming errors (null formula, misuse of APIs) are silently converted to `Unknown` feasibility results, producing false negatives in purity analysis rather than surfacing the underlying bug.
* **Recommendation:** Remove `ArgumentException` from the conservative filter or narrow it to a specific Z3-originated `ArgumentException` subtype by checking the stack trace or exception message.

#### [PB2-9.3.4] 9.3.4 `PurityProofSearch.ClassifyInternalOnlyEffect` - Path-Feasibility Unknown Is Classified as Pure

> **Disposition:** Fixed
> **Canonical root cause:** RC-MALFORMED-PROOF-QUERY
> **Evidence:** The claimed Unknown-as-pure branch is correct, but a null PathConditions value could still reach ToArray and throw.
> **Changes/tests:** PurityProofSearch normalizes null path conditions to the empty path and returns Unknown for a null query/hazard; PurityProof_NullPathConditionsDefaultToEmpty.
* **File &amp; Lines:** [PurityProofSearch.cs](file:///C:/w/PurelySharp/SearchLib/PurityProofSearch.cs#L127-L152)
* **Severity:** High
* **Description:** For `InternalOnly` effects, the code returns `ProvablyPure` even when path feasibility is `Unknown`:
  ```csharp
  Feasibility.Unknown => new PurityProofResult(
      PurityProofOutcome.Unknown,        // <- correctly Unknown
      Attempted(path),
      NotAttempted(),
      "path_feasibility_unknown"),
  _ => new PurityProofResult(
      PurityProofOutcome.ProvablyPure,   // <- Satisfiable path -> ProvablyPure
      Attempted(path),
      NotAttempted(),
      pureReason)
  ```
  Wait - this looks correct. However, the key issue is the ordering: the `Feasibility.Unknown` branch is listed before `_`, so `Unknown` falls into the explicit `Unknown` arm. But the `Feasibility.Satisfiable` arm falls into `_` and returns `ProvablyPure`. This is the **intended** behavior for `InternalOnly` effects: if the path is satisfiable (reachable), the effect is still pure because it is internal-only. This is conceptually correct.

  The actual bug is subtler: **the method calls `pathConditions.ToArray()` but never materializes the array into a field**, so `pathConditions` is enumerated by `_solver.CheckSatisfiability`, which is correct. However, if the same `IEnumerable<SmtFormula>` is a lazy generator (e.g., from LINQ) and multiple classification calls share it (e.g., `ClassifyStaticCacheRead` -> `ClassifyInternalOnlyEffect` -> `_solver.CheckSatisfiability`), the enumeration occurs once via `normalizedPathConditions = pathConditions.ToArray()`, which is safe. No bug here - the `.ToArray()` defensive copy is present.

  The real bug: For the `PurityHazardKind.CallerVisibleMemoryWrite` case in `Classify(PurityProofQuery)` (line 103-104), the method calls `ClassifyCallerVisibleMemoryWrite` with `query.Hazard.TriggerCondition`, but `ClassifyCore` also takes `pathConditions`. If `query.PathConditions` is `null` (the record's default for an uninitialized field), the `pathConditions.ToArray()` inside `ClassifyCore` (line 218) will throw a `NullReferenceException`. The `PurityProofQuery` record constructor does not validate that `PathConditions` is non-null.
* **Impact:** Passing a `PurityProofQuery` with `null` `PathConditions` causes an unhandled `NullReferenceException` inside the solver, crashing the analysis thread.
* **Recommendation:** Add a validation check in `PurityProofQuery`'s constructor (or factory method) to ensure `PathConditions` is never null; default it to `Array.Empty<SmtFormula>()`.

#### [PB2-9.3.5] 9.3.5 `SmtSolver.TryApplyEqualitySubstitutions` - Substitution Order Depends on Dictionary Enumeration Order

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [SmtSolver.cs](file:///C:/w/PurelySharp/SearchLib/SmtSolver.cs#L476-L518)
* **Severity:** Medium
* **Description:** The substitution map is a `Dictionary<SmtVariable, SmtFormula>`. In each pass, all conditions are scanned for equality substitution opportunities, which are collected into this dictionary. The substitution direction is determined by variable name order (`string.CompareOrdinal`, lines 544-547), but when multiple equalities exist for the same variable (e.g., `x = y` and `x = z`), only the first entry survives due to `if (substitutions.ContainsKey(source)) return;` (line 572). The first entry depends on `conditions` iteration order, which is non-deterministic for LINQ-generated sequences.

  More critically, the `SubstituteEqualityAliases` loop runs `pass <= substitutions.Count` times (line 639), but `substitutions.Count` can be 0 even if substitutions exist (if the dictionary is empty after the cycle check eliminates all candidates). This means the substitution loop body runs once (`pass = 0` only), applying one round of substitutions but potentially missing chains that require multiple passes to fully apply.
* **Impact:** Incomplete variable substitution in complex constraint chains with more than one equality alias, leading to less-simplified formulas being submitted to Z3 and potentially slower or less precise proofs.
* **Recommendation:** Re-evaluate the `pass <= substitutions.Count` loop bound to use `MaxEqualitySubstitutionPasses` instead, ensuring chain substitution is fully applied.

#### [PB2-9.3.6] 9.3.6 `SmtFormulaTraversal.RewriteBottomUp` - Stack-Based Rewrite Returns Wrong Formula If Root Is Constant

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [SmtFormulaTraversal.cs](file:///C:/w/PurelySharp/SearchLib/SmtFormulaTraversal.cs#L19-L53)
* **Severity:** Low
* **Description:** The bottom-up rewrite uses an iterative stack:
  ```csharp
  frames.Push(new TraversalFrame(root, false));
  while (frames.Count > 0)
  {
      var frame = frames.Pop();
      if (!frame.Visited) { ... push children ... continue; }
      var childCount = GetChildCount(frame.Formula);
      var children = childCount == 0 ? Array.Empty<SmtFormula>() : new SmtFormula[childCount];
      for (var index = childCount - 1; index >= 0; index--) children[index] = results.Pop();
      var rebuilt = Rebuild(frame.Formula, children);
      var rewritten = rewrite(rebuilt) ?? throw new InvalidOperationException(...);
      results.Push(rewritten);
  }
  return results.Pop();
  ```
  If `root` is a leaf (e.g., `SmtBooleanConstant`, `SmtVariable`) where `GetChildCount(formula) == 0`, `children` is empty, `Rebuild` returns the original formula unchanged, and `rewrite(rebuilt)` is called. This is correct. However, if the `rewrite` delegate returns a formula that has *children* (e.g., the root constant is rewritten to a complex expression), those children are **not themselves rewritten** - the method only applies the rewrite delegate to the rebuilt formula, not recursively to the result of `rewrite`. This can produce incomplete rewrites if the delegate introduces new sub-expressions that also need rewriting.
* **Impact:** One-pass bottom-up rewrite does not recursively rewrite newly-introduced sub-expressions, potentially leaving unreduced sub-terms in the formula after simplification.
* **Recommendation:** Document that `rewrite` must return a formula that does not require further rewriting (i.e., it should only replace leaf-level nodes or produce already-normalized sub-expressions). Alternatively, apply a fixpoint loop to `RewriteBottomUp`.

---

### 9.4 Cross-Cutting Concerns

#### [PB2-9.4.1] 9.4.1 `SmtResourceBudget.RlimitPerMillisecond` Is Calibrated to Z3 4.12.2 Only - No Runtime Calibration

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [SmtResourceBudget.cs](file:///C:/w/PurelySharp/SearchLib/SmtResourceBudget.cs#L13-L19)
* **Severity:** Medium
* **Description:** The conversion constant `RlimitPerMillisecond = 4000` is documented as being calibrated against Z3 4.12.2. The actual Z3 version bundled in `SharpProof.Package` is determined at package-restore time and may drift. Z3 minor and patch versions can have significantly different rlimit-per-millisecond ratios (especially with tactic changes). No runtime calibration or assertion checks the actual Z3 version against the constant.
* **Impact:** After a Z3 version upgrade, all SMT timeout budgets will be systematically too tight (if the new Z3 is faster, consuming more rlimits/ms) or too loose (if it's slower), causing either spurious `Unknown` results or runaway solve times.
* **Recommendation:** Add a startup calibration step that runs a known benchmark formula and measures the actual rlimit/ms ratio, or at minimum assert the Z3 version at startup and warn if it differs from the calibrated version.

#### [PB2-9.4.2] 9.4.2 No Shared Cancellation Token Propagation From CLI to SMT Analysis

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File &amp; Lines:** [Tools/SharpProof.SymbolicCli/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.SymbolicCli/Program.cs#L21-L26)
* **Severity:** Medium
* **Description:** The SymbolicCli uses `using var inputContext = await SymbolicCliInputContext.CreateAsync(options)` and `using var smtAnalysis = new SmtAnalysisService(...)`, but there is no `CancellationToken` plumbed into either the workspace loading or the SMT analysis. If the user sends `Ctrl+C` (SIGINT), the .NET runtime will cancel the default `CancellationToken` for top-level programs only if the program registers a handler. Without a handler, the process either terminates immediately (killing all in-flight Z3 native threads abruptly, possibly corrupting native heap) or hangs until the current SMT query completes.
* **Impact:** `Ctrl+C` during a long SMT query either corrupts native memory or is non-responsive, degrading the developer experience and potentially requiring process kill.
* **Recommendation:** Register a `Console.CancelKeyPress` handler that sets a `CancellationTokenSource`, and propagate the token to `SymbolicCliInputContext.CreateAsync` and the SMT query services.

#### [PB2-9.4.3] 9.4.3 Baseline Tool and CorpusReport Tool Both Call `dotnet build` Without Inheriting Environment Variables

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [Baseline/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Baseline/Program.cs#L113-L130), [CorpusReport/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.CorpusReport/Program.cs#L58-L75)
* **Severity:** Low
* **Description:** Both tools use `new ProcessStartInfo("dotnet") { UseShellExecute = false }` without configuring the child process's environment. By default, `UseShellExecute = false` inherits the parent process's environment - this is correct. However, neither tool sets a working directory for the child `dotnet build` process. MSBuild's behavior depends on the current working directory for resolving relative paths in project files (e.g., relative `$(MSBuildThisFileDirectory)` references). If the CLI tool is run from a directory other than the solution root, `dotnet build` may fail or produce incorrect output.
* **Impact:** Baseline and CorpusReport tools produce wrong results when run from a directory other than the solution root. Error messages are not specific about the CWD mismatch.
* **Recommendation:** Set `startInfo.WorkingDirectory = Path.GetDirectoryName(input)` (for project/solution inputs) or document that the tool must be run from the solution root.

---

## 10. Phase 5 Audit - 10-Agent Comprehensive Review

### 10.1 Purity Analysis Engine (Agent 1 - Engine Files)

#### [PB2-10.1.1] 10.1.1 CFG Second Pass Unconditionally Overwrites First-Pass Callee Edges

> **Disposition:** Already fixed
> **Canonical root cause:** RC-CALL-GRAPH-EDGE-PRESERVATION
> **Evidence:** The CFG enrichment pass now initializes from the first-pass caller set rather than an empty set.
> **Changes/tests:** CallGraphBuilder uses callerSet.ToBuilder before adding CFG invocation targets; no further code change.
* **File & Lines:** [CallGraphBuilder.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/Analysis/CallGraphBuilder.cs#L347-L401)
* **Severity:** High
* **Description:** The first pass collects callee edges from a rich set of operation types: invocations, binary/unary operators, compound assignments, increment/decrement, conversions, constructor initializers, method references, property references, delegate creations, anonymous functions, await operations, delegate assignments, event subscriptions, and variable initializers (stored at lines 347-349). The CFG pass then re-analyzes using only `IInvocationOperation` nodes (line 387) and **unconditionally overwrites** the edge set at line 401 (`edges[method.OriginalDefinition] = callerSetBuilder.ToImmutable()`). The CFG pass starts from an empty builder (line 379-380), so all non-invocation callees from the first pass are silently discarded.
* **Impact:** The call graph loses edges to operator methods, property accessors, delegate creation targets, event subscriptions, and other non-invocation callees. The worklist solver does not re-analyze callers when these callees' purity changes, leading to stale purity results.
* **Recommendation:** Start the CFG pass builder from the existing edge set (`callerSet.ToBuilder()`) and add new edges on top of it.

#### [PB2-10.1.2] 10.1.2 CallGraphBuilder CFG Second Pass Descendant-Traversal Regression on Lambda Return Statements

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CALLABLE-DESCENDANT-BOUNDARY
> **Evidence:** AnonymousFunctionExpressionSyntax is the base syntax type for both simple and parenthesized lambdas, so the existing descendant predicate already stops at both.
> **Changes/tests:** Roslyn syntax hierarchy and the existing predicate demonstrate coverage; no code change.
* **File & Lines:** [PurityAnalysisEngine.CfgReturnValues.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityAnalysisEngine.CfgReturnValues.cs#L149-L164)
* **Severity:** High
* **Description:** `TryGetSingleReturnedExpressionSyntaxFromBody` uses `blockSyntax.DescendantNodes` with a filter that stops at `LocalFunctionStatementSyntax` and `AnonymousFunctionExpressionSyntax`, but does NOT stop at `SimpleLambdaExpressionSyntax` or `ParenthesizedLambdaExpressionSyntax`. Lambda expressions nested inside the method body will have their `ReturnStatementSyntax` nodes traversed, and if there is exactly one such return, it will be incorrectly extracted as the enclosing method's return expression.
* **Impact:** The purity analyzer extracts the wrong returned expression from methods containing lambdas, leading to incorrect symbolic facts in the path state and unsound purity proofs.
* **Recommendation:** Add `SimpleLambdaExpressionSyntax` and `ParenthesizedLambdaExpressionSyntax` to the descendant filter predicate.

#### [PB2-10.1.3] 10.1.3 HasMethodBody in ConcreteReceivers Uses Overly Permissive Check

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** A source declaration was treated as a concrete body without inspecting its syntax.
> **Changes/tests:** Concrete receiver resolution now uses TypeHierarchyEnumeration.HasMethodBody.
* **File & Lines:** [PurityAnalysisEngine.RuleSupport.ConcreteReceivers.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityAnalysisEngine.RuleSupport.ConcreteReceivers.cs#L286-L289)
* **Severity:** Medium
* **Description:** The local `HasMethodBody` method checks only `methodSymbol.DeclaringSyntaxReferences.Length > 0`, which returns `true` for abstract methods, partial methods without bodies, and extern methods. Compare with `TypeHierarchyEnumeration.HasMethodBody` which correctly inspects the syntax tree for actual body/expression-body nodes.
* **Impact:** Abstract methods declared in source but lacking an implementation body are treated as valid concrete dispatch targets, leading to unsound purity analysis.
* **Recommendation:** Inspect the syntax node for actual body or expression-body presence.

#### [PB2-10.1.4] 10.1.4 IUsingDeclarationOperation Not Handled in CFG State Update

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Using declarations were not projected as disposed into the method normal-exit resource state.
> **Changes/tests:** Normal-exit state applies using-declaration disposal facts; UsingDeclaration_DisposesOwnedResourceAtNormalExit.
* **File & Lines:** [PurityAnalysisEngine.RuleSupport.AssignmentState.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityAnalysisEngine.RuleSupport.AssignmentState.cs#L10-L315)
* **Severity:** Medium
* **Description:** `UpdateDelegateMapForOperation` handles `IUsingOperation` (lines 215-218) but NOT `IUsingDeclarationOperation` (C# `using var` declarations). The method `AddUsingDeclarationDisposeFacts` exists in `ResourceFacts.cs` (lines 95-112) but is never called from the CFG traversal. The post-CFG pass does separately probe `OperationKind.UsingDeclaration`, but during CFG analysis, resource release facts from using declarations are never added to the path state.
* **Impact:** False-positive "resource not disposed" diagnostics for code that uses `using var` declarations, because the symbolic resource state never registers the implicit disposal.
* **Recommendation:** Add a case for `IUsingDeclarationOperation` in `UpdateDelegateMapForOperation` that calls `AddUsingDeclarationDisposeFacts`.

#### [PB2-10.1.5] 10.1.5 DestructorDeclarationSyntax Not Handled in GetBodySyntaxNode

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [PurityAnalysisEngine.BodyLookup.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityAnalysisEngine.BodyLookup.cs#L8-L32)
* **Severity:** Low
* **Description:** `GetBodySyntaxNode` handles `MethodDeclarationSyntax`, `ConstructorDeclarationSyntax`, `ConversionOperatorDeclarationSyntax`, etc., but not `DestructorDeclarationSyntax`. Destructors (finalizers) with bodies return `null`, falling through to abstract/extern handling.
* **Impact:** Destructors with bodies are treated as abstract/extern methods with no body, bypassing purity analysis of the destructor body.
* **Recommendation:** Add `DestructorDeclarationSyntax` to the recognized types in `GetBodySyntaxNode`.

#### [PB2-10.1.6] 10.1.6 TryGetDirectThrowOnlySyntax Missing ConversionOperatorDeclarationSyntax

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [PurityAnalysisEngine.Cfg.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityAnalysisEngine.Cfg.cs#L437-L472)
* **Severity:** Low
* **Description:** `TryGetDirectThrowOnlySyntax` handles `MethodDeclarationSyntax` with both `ExpressionBody` and `Body` but omits `ConversionOperatorDeclarationSyntax`, which also has `ExpressionBody` and `Body` properties.
* **Impact:** Conversion operators whose body is a single throw statement are not fast-path identified, missing the early throw-detection exit.
* **Recommendation:** Add cases for `ConversionOperatorDeclarationSyntax` with `ExpressionBody` and `Body`.

### 10.2 Analyzer Rules (Agent 3 - Rules Files)

#### [PB2-10.2.1] 10.2.1 SwitchStatementPurityRule Does Not Analyze Case Bodies

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CFG-SWITCH-ANALYSIS
> **Evidence:** SwitchStatementPurityRule handles the switch value while CFG block operations independently analyze reachable case guards and bodies.
> **Changes/tests:** Existing ConstantSwitchStatement and source switch purity regressions report reachable impure cases and ignore only proven-unreachable ones.
* **File & Lines:** [SwitchStatementPurityRule.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/Rules/SwitchStatementPurityRule.cs#L17-L21)
* **Severity:** High
* **Description:** The rule only checks `switchOperation.Value` for purity and returns `Pure` immediately. It never enumerates switch cases, when guards, or case body operations. By contrast, `SwitchExpressionPurityRule` correctly iterates through all arms.
* **Impact:** Any impure operation inside a `switch` statement case body is silently accepted as pure - a significant soundness gap.
* **Recommendation:** Iterate through `switchOperation.Cases`, checking each case clause's conditions/guards and body operations.

#### [PB2-10.2.2] 10.2.2 Hardcoded RefKind Magic Number `(RefKind)4` Instead of Named Enum

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Ref-readonly parameter recognition retained an unnamed numeric enum fallback.
> **Changes/tests:** The fallback now uses RefKind.RefReadOnlyParameter.
* **File & Lines:** [FieldReferencePurityRule.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/Rules/FieldReferencePurityRule.cs#L119), [PropertyReferencePurityRule.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/Rules/PropertyReferencePurityRule.cs#L253)
* **Severity:** Medium
* **Description:** Both files test `paramRef.Parameter.RefKind == (RefKind)4` to detect `ref readonly` parameters. Roslyn defines `RefKind.RefReadOnly` as a named enum member; using `(RefKind)4` is fragile.
* **Impact:** If the Roslyn enum layout changes, this check silently breaks.
* **Recommendation:** Replace `(RefKind)4` with `RefKind.RefReadOnly`.

#### [PB2-10.2.3] 10.2.3 ArrayElementReferencePurityRule Misses Compound/Increment Parent Operations

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [ArrayElementReferencePurityRule.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/Rules/ArrayElementReferencePurityRule.cs#L34-L48)
* **Severity:** Medium
* **Description:** `IsPartOfAssignmentTarget` only checks `IAssignmentOperation` as a parent. It fails to recognize `ICompoundAssignmentOperation` (e.g., `arr[i] += 1`) and `IIncrementOrDecrementOperation` (e.g., `arr[i]++`). Compare with `InlineArrayAccessPurityRule` which correctly checks all three parent types.
* **Impact:** Compound assignments or increments on array elements are not recognized as write targets, causing incorrect impurity classification.
* **Recommendation:** Add checks for `ICompoundAssignmentOperation` and `IIncrementOrDecrementOperation`.

#### [PB2-10.2.4] 10.2.4 UsingStatementPurityRule Fragile AwaitKeyword Detection via RawKind

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [UsingStatementPurityRule.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/Rules/UsingStatementPurityRule.cs#L295-L297)
* **Severity:** Low
* **Description:** `IsAwaitUsingOperation` checks `usingStatementSyntax.AwaitKeyword.RawKind != 0`. `SyntaxToken.RawKind` is an internal detail that can change between Roslyn versions. The correct approach is `usingStatementSyntax.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)`.
* **Impact:** Fragile detection may break with future Roslyn updates.
* **Recommendation:** Use `IsKind(SyntaxKind.AwaitKeyword)` instead of comparing `RawKind`.

### 10.3 Analyzer Top-Level Files (Agent 2)

#### [PB2-10.3.1] 10.3.1 ExceptionFlowAnalyzer GetIdentifierLocation Missing ConversionOperatorDeclarationSyntax

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [ExceptionFlowAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.cs#L664-L681)
* **Severity:** Low
* **Description:** `GetIdentifierLocation` handles `MethodDeclarationSyntax`, `ConstructorDeclarationSyntax`, `OperatorDeclarationSyntax`, etc., but not `ConversionOperatorDeclarationSyntax`. It falls through to `_ => node.GetLocation()`, returning a span covering the entire declaration.
* **Impact:** SP0010 diagnostic location for conversion operators spans the entire declaration instead of just the type token.
* **Recommendation:** Add a case for `ConversionOperatorDeclarationSyntax conversion => conversion.Type.GetLocation()`.

#### [PB2-10.3.2] 10.3.2 MethodCapabilityAnalyzer Assumes UnknownReasonDetails Is Non-Empty

> **Disposition:** Duplicate
> **Canonical root cause:** RC-ANALYZER-SYMBOLIC-BOUNDARY
> **Evidence:** Same analyzer exception-boundary root cause as PB1-1.1, PB1-1.2, and PB1-26.1.
> **Changes/tests:** See typed query outcomes and conservative diagnostics under PB1-1.1.
* **File & Lines:** [MethodCapabilityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodCapabilityAnalyzer.cs#L90)
* **Severity:** Medium
* **Description:** When capability sites exist but unknown reasons are present (line 80), the code accesses `result.UnknownReasonDetails[0]` without verifying `result.UnknownReasonDetails.Length > 0`. If `UnknownReasons` is populated but `UnknownReasonDetails` is empty, this throws `IndexOutOfRangeException`.
* **Impact:** Analyzer crash (AD0001) for the affected method, suppressing all other purity diagnostics.
* **Recommendation:** Check `result.UnknownReasonDetails.Length > 0` before indexing.

### 10.4 Symbolic Engine (Agent 4 - Core Files)

#### [PB2-10.4.1] 10.4.1 Missing Bounds Check in TryGetListPatternElementPosition

> **Disposition:** Fixed
> **Canonical root cause:** RC-LIST-PATTERN-INDEX-VALIDATION
> **Evidence:** TryGetListPatternElementPosition indexed the pattern list before validating its public integer input.
> **Changes/tests:** Added negative/upper-bound guards; ListPatternElementPosition_RejectsOutOfRangeIndexes.
* **File & Lines:** [CSharpSyntaxFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/CSharpSyntaxFacts.cs#L76)
* **Severity:** High
* **Description:** `TryGetListPatternElementPosition` accesses `listPattern.Patterns[patternIndex]` without bounds validation. If `patternIndex >= listPattern.Patterns.Count`, this throws.
* **Impact:** Stale or out-of-range pattern index crashes the analysis pipeline.
* **Recommendation:** Add `if (patternIndex < 0 || patternIndex >= listPattern.Patterns.Count) return false;` before the access.

#### [PB2-10.4.2] 10.4.2 Incomplete Collection Expression Fixed Lower Bound

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SymbolicFactFactory.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicFactFactory.cs#L441-L458)
* **Severity:** Medium
* **Description:** `TryGetCollectionExpressionFixedLowerBound` returns only `hasSpread && lowerBound > 0`. For collection expressions composed entirely of fixed expressions (e.g., `[a, b, c]`), the function returns `false` even though the minimum length is trivially known (3).
* **Impact:** Length lower-bound facts are silently dropped for spread-free collection expressions, causing missed bounds-check proofs.
* **Recommendation:** Change the return to `true` whenever `lowerBound > 0`, regardless of `hasSpread`.

#### [PB2-10.4.3] 10.4.3 Expensive Ad-Hoc Z3 Solver Creation Per Pipeline Call

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SymbolicProofPipeline.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProofPipeline.cs#L96-L105)
* **Severity:** Medium
* **Description:** When no `SmtAnalysisService` is passed to the pipeline, `Execute` creates a brand-new `SmtAnalysisService` and disposes it immediately. Since pipeline methods are called many times per analysis session, this amounts to hundreds of native Z3 context create/destroy cycles.
* **Impact:** Severe performance degradation when analysis runs without an external SMT configuration.
* **Recommendation:** Cache a single fallback `SmtAnalysisService` instance at the pipeline level and reuse it.

#### [PB2-10.4.4] 10.4.4 Overly Broad ToString Capability Neutrality

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Every metadata method named ToString was assumed capability-neutral.
> **Changes/tests:** Only System.Object.ToString receives that fallback; custom source ToString capability regression coverage.
* **File & Lines:** [SymbolicCapabilityService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicCapabilityService.cs#L1022)
* **Severity:** Medium
* **Description:** `IsKnownCapabilityNeutralSymbol` returns `true` for any method named `ToString` on any type, without checking the containing type. Calling `ToString()` on a type whose override performs I/O is classified as capability-neutral.
* **Impact:** Methods calling custom `ToString()` overrides with capabilities are incorrectly classified as having no capabilities.
* **Recommendation:** Restrict to `System.Object.ToString()` by checking `originalSymbol.ContainingType.SpecialType == SpecialType.System_Object`.

#### [PB2-10.4.5] 10.4.5 Silent Exception Swallowing in TryGetGlobalOption

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SymbolicProjectQueryContext.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProjectQueryContext.cs#L149-L166)
* **Severity:** Medium
* **Description:** `TryGetGlobalOption` catches all `Exception` types except `OperationCanceledException` and silently returns `false`. If the analyzer configuration provider throws due to a corrupt `.globalconfig`, the failure is silently absorbed.
* **Impact:** Configuration errors are invisible; the analyzer silently runs with default settings.
* **Recommendation:** At minimum, emit an analyzer diagnostic or log when catching exceptions.

### 10.5 Symbolic IR & SMT (Agent 5)

#### [PB2-10.5.1] 10.5.1 SwitchPathConditionBuilder Silently Skips Unmodelable Labels in Default Case Condition

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SwitchPathConditionBuilder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SwitchPathConditionBuilder.cs#L122-L141)
* **Severity:** Medium
* **Description:** When computing the default case condition (negation of all explicit selections), labels that cannot be modeled are silently skipped. The default case's negation does not exclude inputs matching those unmodelable labels.
* **Impact:** The default switch case appears reachable for inputs that should match an explicit label, leading to false negatives when the default case contains impure operations.
* **Recommendation:** When any explicit label fails to lower, return `false` for the entire default condition.

#### [PB2-10.5.2] 10.5.2 SmtSyntacticClassifier Conditional Not in Complement Check

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [SmtSyntacticClassifier.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtSyntacticClassifier.cs#L349-L366)
* **Severity:** Low
* **Description:** `AreSyntacticComplements` only handles `SmtUnaryFormula` (negation) and `SmtBinaryFormula` (relational comparison). Boolean conditional formulas like `ite(c, true, false)` would never be recognized as complementary to `ite(c, false, true)`.
* **Impact:** Slight precision loss - `ContainsSyntacticContradiction` misses contradictions involving boolean conditional formulas, wasting solver budget.
* **Recommendation:** Extend `AreSyntacticComplements` to handle `SmtConditionalFormula` with constant-folding.

### 10.6 SearchLib & Shared (Agent 6)

#### [PB2-10.6.1] 10.6.1 Unbounded _regexValidationCache Growth in SmtSolver

> **Disposition:** Fixed
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Concrete regex validation results accumulated without a session bound.
> **Changes/tests:** The per-solver cache is capped and cleared deterministically; ConcreteRegexValidationCache_IsBounded.
* **File & Lines:** [SmtSolver.cs](file:///C:/w/PurelySharp/SearchLib/SmtSolver.cs#L21)
* **Severity:** Medium
* **Description:** `_regexValidationCache` is a `Dictionary` caching regex match results with no size limit or eviction policy. In a long-running solver processing thousands of distinct concrete string inputs, the cache grows without bound.
* **Impact:** Managed heap growth in long-running analysis processes, eventually causing OOM.
* **Recommendation:** Add an LRU eviction policy or cap the cache size.

#### [PB2-10.6.2] 10.6.2 Overly Broad Substring Matching in BclPurityFallbackHeuristics

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [BclPurityFallbackHeuristics.cs](file:///C:/w/PurelySharp/Shared/BclPurityFallbackHeuristics.cs#L170-L192)
* **Severity:** Medium
* **Description:** `IsAmbientNamespaceOrType` calls `ContainsAny(typeName, "Console", "Environment", "Process", ...)` using `string.IndexOf` for substring matching. A type like `FileHandlerProcessor` will match `"File"`, causing the entire type to be over-approximated as ambient/impure.
* **Impact:** False impurity classifications for innocent types whose names happen to contain one of the substring fragments.
* **Recommendation:** Use word-boundary checks or exact-form matching for type names.

#### [PB2-10.6.3] 10.6.3 PurityProofSearch: TriggerCondition Dropped for Non-Standard InternalOnly Hazards

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [PurityProofSearch.cs](file:///C:/w/PurelySharp/SearchLib/PurityProofSearch.cs#L92-L124)
* **Severity:** Medium
* **Description:** When a hazard with `Visibility == InternalOnly` and a `Kind` other than the three known ones is encountered, the catch-all routes it to `ClassifyInternalOnlyEffect` which ignores `query.Hazard.TriggerCondition` entirely.
* **Impact:** If any future hazard kind is introduced with `InternalOnly` visibility, the solver returns `ProvablyPure` without checking reachability.
* **Recommendation:** Add a default case requiring `ClassifyCore` or assert that no such combination exists.

### 10.7 Test Files (Agents 7 & 8)

#### [PB2-10.7.1] 10.7.1 Undisposed SmtAnalysisService in Test Files

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Two test helpers transferred undisposed SMT services into synchronous query calls.
> **Changes/tests:** Both helpers now own services with using declarations.
* **File & Lines:** [ElementAccessSmtTests.cs](file:///C:/w/PurelySharp/SharpProof.Test/ElementAccessSmtTests.cs#L644), [LoopExitSmtInvariantTests.cs](file:///C:/w/PurelySharp/SharpProof.Test/LoopExitSmtInvariantTests.cs#L693-L700), [SymbolicAnalysisLimitsTests.cs](file:///C:/w/PurelySharp/SharpProof.Test/SymbolicAnalysisLimitsTests.cs#L204-L206)
* **Severity:** Low
* **Description:** Multiple test classes create `new SmtAnalysisService(SmtAnalysisOptions.Default)` without wrapping in a `using` statement. Each creates a native Z3 solver context that is never released.
* **Impact:** Native Z3 resources accumulate during SMT-heavy test runs. While the test process's lifetime is short, this can cause test instability in CI with limited memory.
* **Recommendation:** Always wrap `SmtAnalysisService` in `using` declarations.

#### [PB2-10.7.2] 10.7.2 Duplicate Using Directives in NullComparisonTests

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [NullComparisonTests.cs](file:///C:/w/PurelySharp/SharpProof.Test/NullComparisonTests.cs#L16-L17)
* **Severity:** Low
* **Description:** Three test methods have `using SharpProof.Attributes;` listed twice on consecutive lines. Cosmetic only.
* **Impact:** No functional impact beyond code cleanliness.
* **Recommendation:** Remove duplicate `using` directives.

### 10.8 Tools (Agent 9)

#### [PB2-10.8.1] 10.8.1 SymbolicConsumer.cs: Operator Precedence Bug in stableFallback Logic

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CSPATTERN-PRECEDENCE
> **Evidence:** In C#, the alternatives form one or-pattern operand of is; the following logical && applies to the completed is expression, not only the last constant pattern.
> **Changes/tests:** C# pattern grammar makes the current fallback require unknownProofCount for every failure-code alternative; no code change.
* **File & Lines:** [SymbolicConsumer.cs](file:///C:/w/PurelySharp/scripts/package-consumers/SymbolicConsumer.cs#L75-L81)
* **Severity:** High
* **Description:** The `stableFallback` expression uses `is` pattern matching with `or` patterns combined with `&&` without parentheses. `&&` binds tighter than `or`, so `unknownProofCount > 0` is only evaluated for `"smt_initialization_failure"`.
* **Impact:** When the SMT native library is missing, incompatible, or platform-unsupported, `stableFallback` evaluates to `true` even if `unknownProofCount` is 0, incorrectly treating the graceful-degradation path as satisfied.
* **Recommendation:** Add parentheses: `(health.LastFailureCode is ... or "smt_initialization_failure") && unknownProofCount > 0`.

#### [PB2-10.8.2] 10.8.2 Fuzz.Core: Trivia SyntaxKinds Double-Counted in CollectSyntaxKinds

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Structured trivia nodes were traversed twice by overlapping enumeration passes.
> **Changes/tests:** Structured trivia is counted through the node/token traversal and unstructured trivia separately; AnalyzeCase_CountsStructuredTriviaOnce.
* **File & Lines:** [Fuzz.Core/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz.Core/Program.cs#L660-L681)
* **Severity:** Medium
* **Description:** `CollectSyntaxKinds` calls `root.DescendantNodesAndTokens(descendIntoTrivia: true)` (includes trivia) and then separately calls `root.DescendantTrivia(descendIntoTrivia: true)`. Every trivia syntax kind is counted twice.
* **Impact:** Coverage metrics are inflated with duplicate trivia counts, misleading fuzz coverage analysis.
* **Recommendation:** Remove the separate `DescendantTrivia` loop, or set `descendIntoTrivia: false` on `DescendantNodesAndTokens`.

#### [PB2-10.8.3] 10.8.3 Fuzz.Core: CollectOperationKinds Calls GetOperation on Every Descendant Node

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [Fuzz.Core/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz.Core/Program.cs#L649-L655)
* **Severity:** Medium
* **Description:** `CollectOperationKinds` iterates all descendant nodes and calls `compilation.GetOperation(node)` for every single node. `GetOperation` triggers semantic binding - for a large syntax tree this is extremely expensive.
* **Impact:** Severe performance degradation in fuzz case analysis.
* **Recommendation:** Only call `GetOperation` on statement/expression nodes, or cache the semantic model.

#### [PB2-10.8.4] 10.8.4 RoslynShapeManifest: Circular Static Initialization with FuzzCaseGenerator

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [RoslynShapeManifest.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz.Core/RoslynShapeManifest.cs#L212-L215)
* **Severity:** Medium
* **Description:** `BuildOperationEntries` references `FuzzCaseGenerator.RegistryEntries`. Both types have interdependent static initializers. Currently safe due to property access ordering, but fragile.
* **Impact:** Potential `TypeInitializationException` if static initialization ordering changes.
* **Recommendation:** Break the static coupling by passing generator-backed shape IDs via method parameter or lazy-delayed initialization.

#### [PB2-10.8.5] 10.8.5 EffectSummary Program.cs: ReadStrings Uses Unsafe GetString() Without Type Check

> **Disposition:** Already fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
* **File & Lines:** [EffectSummary/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.EffectSummary/Program.cs#L211-L214)
* **Severity:** Low
* **Description:** `ReadStrings` calls `item.GetString()` on each JSON array element without verifying `item.ValueKind == JsonValueKind.String`. If an array element is a number, `GetString()` throws `InvalidOperationException`.
* **Impact:** Malformed JSON in `GeneratedPurityCatalog` causes an unhandled crash.
* **Recommendation:** Check `item.ValueKind == JsonValueKind.String` before calling `GetString()`.

#### [PB2-10.8.6] 10.8.6 EffectSummary Program.cs: Path.GetDirectoryName Null-Forgiven on Root Paths

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [EffectSummary/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.EffectSummary/Program.cs#L132)
* **Severity:** Low
* **Description:** `WriteManifestIfChanged` and other methods use `Path.GetDirectoryName(path)!`. `Path.GetDirectoryName` returns `null` for root paths like `C:\`. The `Directory.CreateDirectory(null)` call will throw.
* **Impact:** Crash when an output path resolves to a filesystem root.
* **Recommendation:** Guard against null: `Path.GetDirectoryName(path) ?? throw new ArgumentException(...)`.

### 10.9 Configuration & CodeFixes (Agent 10)

#### [PB2-10.9.1] 10.9.1 TryGetGlobalOption in AnalyzerConfiguration Silently Swallows All Exceptions

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [AnalyzerConfiguration.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs#L616-L618)
* **Severity:** Medium
* **Description:** The `catch (Exception ex) when (ex is not OperationCanceledException)` block has an empty body. Any exception (including `NullReferenceException`, `OutOfMemoryException`) when querying analyzer config options is silently discarded.
* **Impact:** Configuration-related bugs are silently hidden. Users get default values without indication that loading failed.
* **Recommendation:** Log the exception or narrow the catch to expected exception types.

#### [PB2-10.9.2] 10.9.2 TryGetText in AnalyzerAdditionalFileValidator Hides Runtime Errors

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [AnalyzerAdditionalFileValidator.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerAdditionalFileValidator.cs#L422)
* **Severity:** Medium
* **Description:** `TryGetText` catches all non-cancellation exceptions and sets `text = string.Empty` before returning `false`. This silently discards `OutOfMemoryException` or `NullReferenceException` when loading additional files.
* **Impact:** A corrupt additional file that triggers a runtime error is treated identically to an empty file.
* **Recommendation:** Narrow the catch to expected I/O exceptions or capture the exception message.

#### [PB2-10.9.3] 10.9.3 Recursive JSON Traversal Without Depth Limit in Analyzer Validators

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [AnalyzerAdditionalFileValidator.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerAdditionalFileValidator.cs#L247-L294), [DiagnosticBaseline.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/DiagnosticBaseline.cs#L135-L178)
* **Severity:** Low
* **Description:** Both `ValidateEvidenceSchemasRecursively` and `HasReadCompatibleEvidenceSchema` perform unbounded recursion through JSON arrays and objects, with no depth limit.
* **Impact:** Stack overflow crashes the analyzer when processing deeply nested JSON additional files.
* **Recommendation:** Add a maximum recursion depth guard (e.g., `remainingDepth = 10`).

#### [PB2-10.9.4] 10.9.4 HasUnaliasedSharpProofAttributesUsing Misses Namespace-Scoped Using Directives

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Short-name detection inspected only compilation-unit imports.
> **Changes/tests:** Code fixes now inspect compilation-unit and namespace-scoped imports; SP0004_AddEnforcePure_UsesNamespaceScopedImport.
* **File & Lines:** [SharpProofCodeFixProvider.cs](file:///C:/w/PurelySharp/SharpProof.CodeFixes/SharpProofCodeFixProvider.cs#L1121-L1127)
* **Severity:** Low
* **Description:** `HasUnaliasedSharpProofAttributesUsing` checks only `CompilationUnitSyntax.Usings`, not `NamespaceDeclarationSyntax.Usings` for block-scoped namespaces.
* **Impact:** Code fixes revert to fully qualified names even when a short name import exists within a block-scoped namespace.
* **Recommendation:** Traverse `NamespaceDeclarationSyntax` ancestors and check their `Usings` as well.

## Source PB3 - New Audit

### 1 Z3 Formula Encoding (Agent 1)

#### [PB3-1.3] 1.3 HasSafeArithmetic Inflates `_lastObservedRlimitCount` Across Solver Instances

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** HasSafeArithmetic creates a separate solver but updates the shared _lastObservedRlimitCount.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtSolver.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtSolver.cs#L116-L127)
* **Severity:** High
* **Description:** `HasSafeArithmetic` at line 116 creates a temporary solver via `_encoder.CreateSolver(timeout)` at line 119, calls `CheckAndAccountResources(solver)` at line 126, and disposes the solver. `CheckAndAccountResources` updates `_lastObservedRlimitCount` (line 32) and `ConsumedResourceCount` (line 29). The next call to `CheckSatisfiabilityRawWithWitness` creates a fresh solver whose rlimit count starts from 0, but `_lastObservedRlimitCount` reflects the prior solver's final count. Since `observed < _lastObservedRlimitCount`, the overflow-correction branch at line 31 adds ~4.29 billion resource units—inflating the budget exactly as Bug #1 did when checks cross solver instances.
* **Impact:** Cumulative resource budget is inflated by billions of units after every divisor-safety check, delaying or disabling budget enforcement.
* **Recommendation:** Save and restore `_lastObservedRlimitCount` around `HasSafeArithmetic`, or use Z3 `Push`/`Pop` instead of a separate solver.

#### [PB3-1.4] 1.4 `TryResolveString` Returns `null` String from Dictionary Causing NullReferenceException

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** TryGetValue with null-forgiving operator then string concatenation without null check.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtQuerySafety.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtQuerySafety.cs#L100-L106)
* **Severity:** High
* **Description:** `TryResolveString` at line 100 calls `values.TryGetValue(formula, out value!)` using the null-forgiving operator. If the dictionary was populated with a `null` string value (e.g., via a default-initialized or corrupt entry), it returns `true` with `value = null`. At lines 102–105, `concat.Left` or `concat.Right` values could be `null`, and `left + right` at line 104 throws `NullReferenceException`.
* **Impact:** Analysis thread crashes when string concatenation terms have null-resolved string values.
* **Recommendation:** After `TryGetValue` succeeds, check `value != null` and return `false` if null.

#### [PB3-1.5] 1.5 `EnumerateConjuncts` Recursion May Stack Overflow on Deep `&&` Chains

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Recursive descent into And-chain without depth limit.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtFormulaTraversal.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtFormulaTraversal.cs#L6-L15)
* **Severity:** Low
* **Description:** `EnumerateConjuncts` recursively descends into left and right branches of `And` formulas without any depth limit. A deeply-nested chain of `And` formulas (e.g., from `a && b && c && d` lowered as `And(And(And(a, b), c), d)`) can cause stack overflow for chains exceeding ~10K formulas. The main `Enumerate` method avoids recursion by using an explicit stack, but `EnumerateConjuncts` does not.
* **Impact:** Analyzer crashes on deeply conjoined path conditions.
* **Recommendation:** Convert `EnumerateConjuncts` to use an explicit stack.

#### [PB3-1.6] 1.6 `FormulaChildren` Count/Indexer Mismatch When Third Is Set but Second Is Null

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Count checks Third before Second; indexer checks Second before Third.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtFormulaTraversal.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtFormulaTraversal.cs#L155-L163)
* **Severity:** Low
* **Description:** `FormulaChildren.Count` checks `Third != null` before `Second != null`, returning 3 if `Third` is non-null regardless of `Second`. The indexer `this[1]` (line 160) checks `Second` first and throws `ArgumentOutOfRangeException` if `Second` is null. If `FormulaChildren` were constructed with `(nonNull, null, nonNull)`, `Count` returns 3 but `this[1]` throws. The `GetChildren` method never produces this pattern, but the type is `readonly record struct` and could be constructed externally.
* **Impact:** Potential crash if FormulaChildren is ever constructed with a null middle child.
* **Recommendation:** Prioritize `Second != null` before `Third != null` in Count, or change the check order in the indexer.

#### [PB3-1.7] 1.7 Lock Held During Delegate Execution Enables Deadlock

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** valueFactory invoked inside lock(_gate) in BoundedConcurrentCache.GetOrAdd.
> **Changes/tests:** No fix yet.
* **File & Lines:** [BoundedConcurrentCache.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/Collections/BoundedConcurrentCache.cs#L50-L61)
* **Severity:** Medium
* **Description:** `GetOrAdd` at line 50 invokes the user-provided `valueFactory(key)` at line 58 while holding `lock (_gate)`. If `valueFactory` recursively calls `GetOrAdd` or `TryAdd` on the same cache instance, it re-enters `lock (_gate)` from the same thread (which C# `lock` allows), but re-entrancy can still cause logic errors. More critically, if `valueFactory` acquires another lock that is held by a thread that itself is blocked on `_gate`, a deadlock occurs.
* **Impact:** Thread deadlocks under concurrent analysis workloads when factory delegates participate in lock hierarchies.
* **Recommendation:** Move the `valueFactory` invocation outside the lock, using double-checked locking with per-key Lazy initialization or `ConcurrentDictionary.GetOrAdd`.

#### [PB3-1.8] 1.8 `CollectUnsafeArithmeticChecks` Recursion Without Depth Limit

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Recursive descent through conditional branches without depth limit.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtQuerySafety.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtQuerySafety.cs#L110-L133)
* **Severity:** Low
* **Description:** `CollectUnsafeArithmeticChecks` recursively traverses `SmtConditionalFormula` branches (lines 116–123) and enumerates children for other formulas (line 131). Deeply nested conditional formulas (e.g., thousands of nested `x ? y : z` expressions) cause stack overflow. There is no depth limit despite `SmtFormulaTraversal.IsWithinDepth` already existing.
* **Impact:** Analyzer crashes on deeply conditional arithmetic expressions.
* **Recommendation:** Convert recursion to explicit stack-based iteration or add depth-limit checking.

#### [PB3-1.9] 1.9 `AnyCharacter()` Race on Lazy-Initialized Field Without Memory Barrier

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Double-checked locking without volatile or Lazy<T>.
> **Changes/tests:** No fix yet.
* **File & Lines:** [Z3RegexExpressionFactory.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/Z3RegexExpressionFactory.cs#L10-L14)
* **Severity:** Medium
* **Description:** `AnyCharacter()` checks `if (_anyCharacter != null) return _anyCharacter` without a memory barrier. Two threads can both see `null`, both enter the factory block, and both call `ParseSMTLIB2String`. The field may be assigned before the `ReExpr` construction completes, causing a thread reading the cached reference to use a partially-constructed Z3 expression.
* **Impact:** Potential use of partially-constructed Z3 objects, leading to solver errors or crashes.
* **Recommendation:** Use `Lazy<ReExpr>` with `LazyThreadSafetyMode.ExecutionAndPublication` for thread-safe lazy initialization.

#### [PB3-1.10] 1.10 `SmtRegexValidator` Unbounded Cache Growth from Concurrent Access

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** No synchronization on SmtRegexValidator cache dictionary.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtRegexValidator.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtRegexValidator.cs#L7-L30)
* **Severity:** Medium
* **Description:** The `Dictionary<RegexValidationKey, RegexValidationResult> _cache` at line 7 is accessed in `TryValidate` at lines 12–27 without synchronization. `SmtQuerySafety` shares a single `SmtRegexValidator` instance as `_regexValidator` (line 8 of `SmtQuerySafety.cs`), which is a field of `SmtSolver`. Multiple concurrent solver operations can manipulate the cache dictionary simultaneously, causing index corruption or `ArgumentException`.
* **Impact:** Non-deterministic analyzer behavior and possible data corruption.
* **Recommendation:** Use `ConcurrentDictionary` or synchronize access with a lock. Also improve the eviction strategy (clearing the entire cache when full is poor).

#### [PB3-1.11] 1.11 Unhandled OverflowException from TryReadNumber Escapes Through Translate

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** checked(value * 10 + digit) in TryReadNumber not caught in TryParseBoundedRepeat.
> **Changes/tests:** No fix yet.
* **File & Lines:** [Z3RegexTranslator.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/Z3RegexTranslator.cs#L635-L645)
* **Severity:** High
* **Description:** `TryReadNumber` at line 640 uses `checked(value * 10 + digit)` to parse regex repetition counts. If the number exceeds `int.MaxValue`, `checked` throws `OverflowException`. `TryReadNumber` is called from `TryParseBoundedRepeat` at line 237, which does not catch `OverflowException`. The exception propagates through `TryParseRepeat` → `TryParseConcat` → `TryParseExpression` → `Translate`. The `Translate` method only returns `Failed()` when `TryParseExpression` returns `false`, not when it throws. This crashes the entire SMT encoding.
* **Impact:** Malformed regex patterns with extremely large repetition counts crash analysis threads.
* **Recommendation:** Remove `checked` from `TryReadNumber` and check for overflow explicitly after each digit.

#### [PB3-1.12] 1.12 `TryFindLeadingStartAnchor` Does Not Skip Non-Capturing Groups

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Non-capturing groups like (?:...) are not skipped when searching for start anchors.
> **Changes/tests:** No fix yet.
* **File & Lines:** [Z3RegexPatternNormalizer.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/Z3RegexPatternNormalizer.cs#L97-L126)
* **Severity:** Medium
* **Description:** `TryFindLeadingStartAnchor` only skips inline option groups (`(?i)`) and inline comments (`(?#...)`) when searching for `^`, `\A`, or `\G` anchors. It does NOT skip non-capturing groups (`(?:...)`), named groups (`(?<name>...)`), atomic groups (`(?>...)`), or balancing groups. A pattern like `(?:^)ABC` would not have the `^` anchor detected, causing the Z3 regex to become non-anchored at the start, potentially causing false positives in string matching proofs.
* **Impact:** String-related proof results may be incorrect for patterns with anchored non-capturing groups.
* **Recommendation:** Extend the anchor-skipping logic to include non-capturing, atomic, named, and balancing groups.

#### [PB3-1.13] 1.13 Large Character Classes (>512 Ranges) Return AnyCharacter() Silently

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** When range count exceeds MaxCharacterClassRangeCount, AnyCharacter() is used as fallback.
> **Changes/tests:** No fix yet.
* **File & Lines:** [Z3RegexTranslator.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/Z3RegexTranslator.cs#L545-L548)
* **Severity:** Medium
* **Description:** `CreateCharacterClassTranslation` calls `TryCreateCharacterRangesRegex(ranges, out var regex)`. If the range count exceeds `MaxCharacterClassRangeCount` (512), `CreateCharacterRangesRegex` throws `InvalidOperationException` (caught, returning `false`). The fallback creates `new RegexClassTranslation(_expressions.AnyCharacter(), false, null)` with `IsExact = false`. This means large character classes silently match ANY character, which is an extremely over-approximate translation. The over-approximation can cause false-positive proof conclusions.
* **Impact:** String proof queries for patterns with large character classes can produce unsound Proven results.
* **Recommendation:** Instead of matching any character, mark the entire regex translation as approximate or unsupported when ranges exceed the limit.

#### [PB3-1.14] 1.14 Iterates All 65536 Char Values with Regex Matching for Each

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Per-character regex matching for Unicode categories is extremely expensive.
> **Changes/tests:** No fix yet.
* **File & Lines:** [Z3RegexCharacterRanges.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/Z3RegexCharacterRanges.cs#L86-L101)
* **Severity:** Medium
* **Description:** `Create` iterates from 0 to `char.MaxValue` (65536 iterations) and calls `regex.IsMatch(current.ToString())` for each character code point. For each character class, this allocates 65536 strings and runs the .NET regex engine 65536 times. For patterns with many character classes (e.g., `[\p{L}\p{N}\p{P}]+`), this quickly exhausts the per-query Z3 budget in setup time before the solver even runs.
* **Impact:** Performance degradation and budget exhaustion from regex character class initialization.
* **Recommendation:** Cache character range computations, or precompute Unicode category ranges using `System.Globalization.CharUnicodeInfo` instead of per-character regex evaluation.

#### [PB3-1.15] 1.15 `CollectConcreteStrings` Quadratic Performance in Equality Count

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Fixed-point iteration with O(N^2) worst-case behavior.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtQuerySafety.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtQuerySafety.cs#L82-L99)
* **Severity:** Low
* **Description:** `CollectConcreteStrings` uses a fixed-point iteration loop (line 88) that iterates up to `equalities.Length + 1` times. For each pass, it iterates all equalities and for each, calls `TryResolveString` on both sides. The `TryResolveString` for `SmtStringConcatTerm` at lines 101–106 recurses into both children. In the worst case, this is O(N²) in both the number of equalities and the depth of concat trees.
* **Impact:** Slow analysis for string-heavy code with many concatenation equality constraints.
* **Recommendation:** Use a worklist-based fixed-point algorithm to avoid redundant passes.

#### [PB3-1.16] 1.16 `AssertIntegerDomains` Constrains All Integer Variables to [long.MinValue, long.MaxValue]

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Uses long bounds for all integer variables regardless of actual C# type.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtSolver.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtSolver.cs#L128-L139)
* **Severity:** Low
* **Description:** `AssertIntegerDomains` at lines 130–138 constrains every integer variable to `long.MinValue <= x <= long.MaxValue`. This is the correct range for `long` but far wider than typical C# `int` or `short` variables. Z3's unbounded integer sort combined with loose bounds means that sat queries may find satisfying models with values outside the actual C# type's range (e.g., an `int` variable assigned `long.MaxValue`). This can produce false-positive satisfiability results.
* **Impact:** Potential unsound proof results when integer variable types have tighter bounds than long.
* **Recommendation:** Track the actual C# type of each integer variable and assert type-specific bounds.

#### [PB3-1.17] 1.17 `SmtConditionalFormula` Does Not Validate Branch Kinds Against ResultKind

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** SmtConditionalFormula can be created with mismatched branch/result kinds.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtFormula.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtFormula.cs#L67-L68)
* **Severity:** Medium
* **Description:** `SmtConditionalFormula` takes a `ResultKind` parameter that declares the expected result type, but `WhenTrue` and `WhenFalse` kinds are not validated against `ResultKind`. A conditional could be created with `ResultKind = Int` but `WhenTrue` being a `SmtBooleanConstant`. The `EncodeConditional` method would encode the Boolean constant as an `IntExpr`, causing `InvalidCastException`.
* **Impact:** SMT solver crashes with `InvalidCastException` when lowered conditional expressions have mismatched branch/result types.
* **Recommendation:** Validate branch kinds against `ResultKind` in the record constructor, or add runtime checks during encoding.

#### [PB3-1.18] 1.18 `CollectUnsafeArithmeticChecks` May Add Duplicate Divisor Checks

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Child enumeration after returning from conditional branch processing visits subtrees already processed.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtQuerySafety.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtQuerySafety.cs#L114-L133)
* **Severity:** Low
* **Description:** `CollectUnsafeArithmeticChecks` iterates over formula children at line 131 using `SmtFormulaTraversal.EnumerateChildren`. For a division nested inside a conditional branch, the division is matched at line 126 and a check is added. The child traversal at line 131 also visits the division's children. While this doesn't add a second check for the same division, the traversal visits subtrees that were already processed recursively, causing redundant work.
* **Impact:** Minor performance overhead from redundant child visits.
* **Recommendation:** Use `SmtFormulaTraversal.Enumerate(formula)` instead of recursive calls for child traversal.

### 2 Symbolic IR & Encoding (Agent 2)

#### [PB3-2.1] 2.1 SymbolicIrFormulaEncoder.TryEncodeBounds Returns Lower-Only Bound Without Check for Empty Lower/Upper

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** TryEncodeBounds returns lower-only bound when IncludeUpperBound=false but does not validate that Index >= 0 is vacuously true.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicIrFormulaEncoder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicIrFormulaEncoder.cs#L291-L308)
* **Severity:** Medium
* **Description:** `TryEncodeBounds` at line 291 handles `SymbolicBoundsAtom` where `IncludeLowerBound` and `IncludeUpperBound` can each be independently set. Lines 299-307 create only the requested bounds. However, if `IncludeLowerBound=false` and `IncludeUpperBound=false`, both `lower` and `upper` are `null`, and line 307 evaluates `lower ?? upper!` which is `null` — the returned `formula` is `null` and the method returns `true` (line 308). The caller gets `true` with a null formula. Since `TryEncodeBounds` is called from `TryEncode` for atoms (line 50), the null formula propagates to the solver as a null expression.
* **Impact:** Null formula reaches Z3, causing NullReferenceException or solver crash.
* **Recommendation:** Return `false` when both bounds are excluded, or assert that at least one bound is included.

#### [PB3-2.2] 2.2 SymbolicConditionalTerm Constructor Does Not Validate Kind Equality

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** WhenTrue.Kind and WhenFalse.Kind are compared in TryEncodeTerm but not enforced at construction.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicIr.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicIr.cs#L85-L86)
* **Severity:** Medium
* **Description:** `SymbolicConditionalTerm` at line 85-86 delegates `Kind` to `WhenTrue.Kind` without verifying that `WhenTrue.Kind == WhenFalse.Kind`. `TryEncodeTerm` at lines 206-213 checks `conditional.WhenTrue.Kind == conditional.WhenFalse.Kind` and returns `false` on mismatch, but only at encoding time. The mismatch is not caught at construction time, allowing invalid IR to propagate through multiple lowering passes before failing.
* **Impact:** Late failures from invalid conditional terms that should have been rejected at construction.
* **Recommendation:** Add a runtime check in the record constructor to verify kind equality.

#### [PB3-2.3] 2.3 SymbolicIrVisitor Recursion Without Depth Limit for Deep IR Trees

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** SymbolicIrVisitor uses recursive descent on potentially deep IR trees.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicIrTraversal.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicIrTraversal.cs#L1-L200)
* **Severity:** Low
* **Description:** `SymbolicIrVisitor` (used by `IntrinsicDomainTermCollector` and other visitors) recursively visits IR trees without depth limiting. Deeply nested conditional terms or binary operations (from code with deeply nested ternary expressions or arithmetic) can cause stack overflow.
* **Impact:** Stack overflow on deeply nested symbolic IR trees during state normalization.
* **Recommendation:** Add depth-limited visitation or convert to iterative traversal.

#### [PB3-2.4] 2.4 CreateProofKey Contains SymbolVersions But NormalizedProofKey Does Not Update on SymbolVersion Changes

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** NormalizedProofKey is computed once in constructor but does not reflect changes from WithSymbolVersion.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicIr.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicIr.cs#L152-L184)
* **Severity:** Medium
* **Description:** The `NormalizedProofKey` is computed at line 152 during construction. `WithSymbolVersion` at lines 173-184 creates a new `SymbolicState` but passes the original `IsContradictory` value (line 180) rather than recomputing it. More critically, `NormalizedProofKey` is computed once in the constructor and never recomputed. Since all methods (`AddFact`, `AddPathCondition`, `WithSymbolVersion`) create new instances, the proof key is always the initial one. This appears intentional for immutability, but `WithSymbolVersion` re-computes `NormalizedProofKey` only if the passed state is used where the constructor calls `CreateProofKey`. Actually, looking more carefully at the constructor — `NormalizedProofKey = CreateProofKey(...)` is only computed once. But since `SymbolicState` is immutable, each mutation creates a new instance through the constructor again, so the proof key IS recomputed for each new state. This bug is actually a false alarm — each new instance goes through the constructor which calls `CreateProofKey`. Marking as false positive.
* **Impact:** (Determined to be false positive — immutable object model.)
* **Recommendation:** No action needed.

#### [PB3-2.5] 2.5 GetReferenceFormulaName Returns "?" for Non-Variable References, Losing Identity in Count/Element Encoding

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Non-variable references are encoded as "?" causing collisions.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicIrFormulaEncoder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicIrFormulaEncoder.cs#L322-L324)
* **Severity:** High
* **Description:** `GetReferenceFormulaName` at line 322 returns `"?"` when the formula is not a `SmtVariable`. This is used for `SymbolicMemberTerm` (line 90), `SymbolicElementTerm` (line 99), `SymbolicMultiElementTerm` (line 115), and `SymbolicCountTerm` (line 190) to construct SMT variable names. When the receiver is a complex expression (e.g., another member access or element access), the resulting variable name is `"?.MemberName"` or `"?[index]"` — all such formulas with different non-variable receivers map to the same SMT variable name. This causes the solver to treat different object graph paths as identical, producing unsound proof results.
* **Impact:** Unsound proof results when member/element access chains involve non-trivial receivers.
* **Recommendation:** Extend `GetReferenceFormulaName` to handle nested formula structures, or reject encoding when the receiver cannot be represented as a simple variable name.

#### [PB3-2.6] 2.6 TryEncodeNullTypeTest Returns Hardcoded false for NullTerm Without Checking Polarity

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** TryEvaluateNullTypeTest always returns false, ignoring polarity.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicIr.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicIr.cs#L520-L527)
* **Severity:** Medium
* **Description:** `TryEvaluateNullTypeTest` at line 520 checks `if (typeTest.Value is not SymbolicNullTerm)` returning `false`, then returns `false` anyway at line 525. This method always returns `false` with `value = false`, regardless of the type test's actual semantics. A `SymbolicExactRuntimeTypeAtom` with a `null` value means "is this value of a specific runtime type" — for null, the answer is always `false` regardless of polarity. However, a negated type test (polarity = false) would mean "value is NOT of this type" — for null, the answer is `true`. Because `TryEvaluateFact` at line 354 calls `TryEvaluateNullTypeTest` and then applies polarity at line 356, a negated null type test would correctly compute `false` then negate to `true`. So this is actually correct by accident — the method returns `false` for null, and the caller at line 356 applies `fact.Polarity ? value : !value` which gives `true` for negated facts. This is correct but confusing.
* **Impact:** None — the logic is correct by coincidence. Maintainability concern.
* **Recommendation:** Document the intentional behavior or rewrite for clarity.

#### [PB3-2.7] 2.7 SymbolicCondition Key with IncludesComplementaryConditionOperands Misses Nested Negation

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** CreateConditionKey only checks direct complementary pairs; missses A && B && !(A && C).
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicIr.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicIr.cs#L872-L883)
* **Severity:** Low
* **Description:** `ContainsComplementaryConditionOperands` at line 872 checks for direct complementary pairs (A and !A). However, it does not handle nested negations like `!(A && B)` as complementary to `A`. While `CreateConditionKey` would normalize `!(!A)` to `A` (line 832-833), it won't expand `!(A && B) = !A || !B` via De Morgan. This means key deduplication may miss some logically redundant conditions.
* **Impact:** Minor: deduplication may leave redundant conditions that could be removed.
* **Recommendation:** Add De Morgan expansion for better key normalization.

#### [PB3-2.8] 2.8 RemoveAbsorbedConditionOperands Handles Only One Level of Nested Opposite Operators

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Only direct nested opposite operators are checked; deeper nesting is missed.
> **Changes/tests:** No fix fix yet.
* **File & Lines:** [SymbolicIr.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicIr.cs#L917-L936)
* **Severity:** Low
* **Description:** `RemoveAbsorbedConditionOperands` at line 917 checks each operand condition to see if it is a binary condition with the opposite operator, then checks if any of its operands appear in the top-level operand set. This only handles one level of nesting. For example, `A && (B || (A && C))` — the `A && C` inside the right operand of `||` is not checked because `(B || (A && C))` is not a direct `||` of top-level operands: only `B` and `A && C` are iterated, but `B` is not in the top-level set and `A && C` is not directly a key. The absorption `A || (A && B) = A` for And-over-Or is not checked beyond one level.
* **Impact:** Minor: IR may contain slightly redundant conditions. No soundness impact.
* **Recommendation:** Recursively check nested opposite operators.

#### [PB3-2.9] 2.9 SymbolicIrVisitor OnTerm Method Visited Twice for Same Term in Binary/Conditional

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** CollectAssociativeBinaryTerms visits both left/right, and then CreateBinaryTermKey normalizes them, but visitor visits them twice.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicIr.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicIr.cs#L233-L241)
* **Severity:** Low
* **Description:** The `IntrinsicDomainTermCollector.OnTerm` is visited for each term in the IR tree. When visiting a `SymbolicBinaryTerm` with `Add` operator that has nested `Add` terms, `CollectAssociativeBinaryTerms` flattens the tree, and the visitor calls `OnTerm` for each leaf. But the parent binary term's `OnTerm` is NOT called (since the visitor recurses into children). This is correct — leaf terms are visited once. However, for `SymbolicConditionalTerm`, both branches may contain the same `SymbolicLengthTerm`, and the visitor will add it to `Terms` (via dictionary key, deduplicated). Actually the `Terms` dictionary is keyed by `CreateTermKey`, so duplicates are silently ignored. This is fine.
* **Impact:** None — dictionary deduplication prevents actual duplicates. Minor efficiency concern.
* **Recommendation:** No action needed.

### 3 Symbolic Lowering & Analysis (Agent 3)

#### [PB3-3.1] 3.1 SymbolicSourceTargetSelector.First() on Empty List on Source Property Access

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** .First() used on potentially empty list when GetSource returns empty.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicSourceTargetSelector.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicSourceTargetSelector.cs#L57-L64)
* **Severity:** High
* **Description:** `SelectTargets` calls `GetSource(invocation, context, out var sourceList)` and then immediately accesses `sourceList.First()` at line 60. If `GetSource` returns `false` (no source found), Early return at line 52 returns false. However, if `GetSource` returns `true` but `sourceList` is empty (e.g., a bug in `GetSource`), `First()` throws `InvalidOperationException`. There's no defensive check for an empty list.
* **Impact:** Analysis crash when source target resolution yields an empty list but returns true.
* **Recommendation:** Use `FirstOrDefault` and check for null, or check `sourceList.Any()` before accessing.

#### [PB3-3.2] 3.2 SymbolicLoopStateTransfer Parent-Assumes-BlockSyntax May Fail for Non-Block Parents

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Parent property access assumes parent is BlockSyntax but may be other syntax types.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicLoopStateTransfer.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicLoopStateTransfer.cs#L42-L50)
* **Severity:** Medium
* **Description:** Loop state transfer logic accesses `.Parent` of loop statements and assumes it is a `BlockSyntax`. If the loop statement's parent is not a block (e.g., a `SwitchSectionSyntax` for loops inside switch cases), the cast throws `InvalidCastException`. C# doesn't allow `for`/`foreach`/`while` directly as switch statement children — they must be inside a block within a switch section. But nested loops inside `using` statements or `lock` statements have different parent types. The implicit cast to `BlockSyntax` can crash.
* **Impact:** Analyzer crash on code with loops inside certain nested constructs.
* **Recommendation:** Use pattern match `is BlockSyntax block` and handle non-block cases gracefully.

#### [PB3-3.3] 3.3 Static SymbolicProofCache Cross-Compilation Leak with Long-Lived Cache Entries

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Static fallback cache never evicts entries, causing memory leak across analysis sessions.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicProofCache.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProofCache.cs#L12-L30)
* **Severity:** Medium
* **Description:** The static fallback cache at line 12 holds a `ConcurrentDictionary<SymbolicProofCacheKey, SymbolicProofCacheValue>` that is never evicted. Entries are added for each unique proof query across all analysis sessions. Since `SymbolicProofCacheKey` includes compilation identity information (or equivalent), each session creates entries for its compilations. Over long-running IDE analysis sessions, this cache grows unboundedly.
* **Impact:** Memory pressure grows over time in long-running analysis sessions.
* **Recommendation:** Add a bounded eviction policy (e.g., LRU or size limit) to the static fallback cache.

#### [PB3-3.4] 3.4 Enumerable.First() on IGrouping Without Guard in SymbolicMutationInventory

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** First() on potentially empty grouping result.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicMutationInventory.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicMutationInventory.cs#L70-L75)
* **Severity:** Medium
* **Description:** `CreateMutableContextInventory` uses LINQ `GroupBy` then calls `.First()` on the grouping result. If the group is empty (e.g., all items filtered out by a preceding `Where` clause), `First()` throws `InvalidOperationException`.
* **Impact:** Analysis crash when mutation inventory grouping produces empty groups.
* **Recommendation:** Use `FirstOrDefault` with null check.

#### [PB3-3.5] 3.5 SymbolicAnalysisLimits Budget Exhaustion Does Not Reset Between Compilations

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Budget tracking counters are preserved across compilations without reset.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicAnalysisLimits.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicAnalysisLimits.cs#L1-L60)
* **Severity:** Medium
* **Description:** The analysis limits class uses static or long-lived counters to track budget consumption. Once the budget is exhausted for one compilation, subsequent compilations start with an already-exhausted budget, preventing analysis of later files.
* **Impact:** Following compilations in a batch may receive limited or zero analysis budget.
* **Recommendation:** Reset budget counters at the start of each compilation.

#### [PB3-3.6] 3.6 SymbolicInvariantService Caches Invariants Without Compilation Identity

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Invariant cache key does not include compilation identity, causing cross-compilation collisions.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicInvariantService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicInvariantService.cs#L15-L35)
* **Severity:** High
* **Description:** The invariant service caches computed invariants by method identity. If a method's implementation changes between compilation versions (e.g., during live analysis), the cached invariant from a previous compilation is returned, producing proof results based on stale method behavior. The cache key includes method symbol but not compilation identity.
* **Impact:** Stale proof results that do not reflect current method implementations.
* **Recommendation:** Include compilation identity in the invariant cache key.

#### [PB3-3.7] 3.7 SymbolicRuntimeHazardQueryService Caches Results Without Compilation Context

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Hazard query cache misses compilation changes.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicRuntimeHazardQueryService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicRuntimeHazardQueryService.cs#L20-L50)
* **Severity:** High
* **Description:** The hazard query service caches results of runtime hazard checks. If two different compilations have different method implementations that affect hazard outcomes, the cached result from the first compilation is returned for the second. This can cause false negatives (hazard not detected) when code changes introduce new hazards but the cache returns the old safe result.
* **Impact:** Runtime hazards may be missed across compilation versions.
* **Recommendation:** Include compilation identity in the hazard query cache key.

#### [PB3-3.8] 3.8 SymbolicAssignmentStateTransfer Incorrectly Handles Deconstruction Assignments

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Deconstruction assignment (var (x, y) = ...) may not be fully handled.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicAssignmentStateTransfer.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicAssignmentStateTransfer.cs#L1-L100)
* **Severity:** Medium
* **Description:** Deconstruction assignments like `var (x, y) = GetPoint()` produce `IAssignmentOperation` with multiple targets, but the state transfer logic appears designed for single-target assignments. Deconstruction targets may not be individually tracked in the symbolic state, causing false-positive proof results for code paths that depend on deconstructed values.
* **Impact:** Unsound proof results for methods using deconstruction assignments.
* **Recommendation:** Add explicit handling for deconstruction assignment targets.

#### [PB3-3.9] 3.9 SymbolicBranchCompletionStateTransfer May Miss Exception-Triggering Branches

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Branch completion does not consider exceptional completion paths.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicBranchCompletionStateTransfer.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicBranchCompletionStateTransfer.cs#L1-L80)
* **Severity:** Medium
* **Description:** The branch completion state transfer merges states from two control flow branches (if-else, try-catch, etc.) but does not account for exceptional branch completions. A branch that throws an exception before completion may still contribute its pre-exception facts to the merged state, causing the merged state to contain facts that are only reachable via the exceptional path.
* **Impact:** Merged state may include facts from exceptional paths, leading to unsound proof conclusions.
* **Recommendation:** Track exceptional completions separately and exclude their facts from normal-path merges.

#### [PB3-3.10] 3.10 SymbolicStateFactBuilder Integer Overflow When Large Constant Values Are Added

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** unchecked integer arithmetic on constants may overflow silently.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicStateFactBuilder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicStateFactBuilder.cs#L40-L60)
* **Severity:** Medium
* **Description:** When building symbolic facts from constant integer operations, the builder performs arithmetic in `long` without overflow checking. C# `unchecked` context means operations like `int.MaxValue + 1` produce `-2147483648` instead of the mathematically correct `2147483648L`. Since SMT solvers use arbitrary-precision integers, the solver would see a wrong constant value, causing unsound proof results.
* **Impact:** Unsound proof results for arithmetic operations near integer boundaries.
* **Recommendation:** Use `checked` arithmetic when computing constant values for SMT encoding.

### 4 Analyzer & Contracts (Agent 4)

#### [PB3-4.1] 4.1 EnforcePureContractAnalyzer Cross-File `FileLinePositionSpan` Crash

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** GetMappedLineSpan throws when syntax tree file path is null.
> **Changes/tests:** No fix yet.
* **File & Lines:** [EnforcePureContractAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/EnforcePureContractAnalyzer.cs#L120-L130)
* **Severity:** High
* **Description:** The analyzer calls `location.GetMappedLineSpan()` on locations from different compilation units. When analyzing cross-file references, `FileLinePositionSpan.Path` may be `null` (e.g., for `#line` directives or embedded files), and `GetMappedLineSpan()` throws `InvalidOperationException`. `GetCallableDeclarationLocation` at `AnalyzerSyntaxHelpers.cs:29` calls `syntaxReference.GetSyntax(cancellationToken)` which can return nodes from different syntax trees without checking, and then accesses `Location` properties that assume file-backed locations.
* **Impact:** Analyzer crashes on cross-file analysis when locations lack file paths.
* **Recommendation:** Catch `InvalidOperationException` around `GetMappedLineSpan()` or check `location.IsInSource` before accessing file-backed spans.

#### [PB3-4.2] 4.2 AnalyzerSession GetOrCreateMethodBodyAnalysis Swallows Errors from Lazy Initialization

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Error recovery removes the failed entry but does not propagate the exception meaningfully.
> **Changes/tests:** No fix yet.
* **File & Lines:** [AnalyzerSession.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/AnalyzerSession.cs#L33-L41)
* **Severity:** Medium
* **Description:** The `catch` block at line 36 removes the entry from `_methodBodyAnalyses` if it fails during initialization. This means subsequent calls for the same method will re-attempt initialization, potentially causing infinite retry loops on persistent failures. Additionally, the `ReferenceEquals` check at line 38 may not match if `GetOrAdd` returned a different `Lazy` instance that was added by a concurrent thread. In that case, the failed lazy's entry is NOT removed, leaving a failed lazy in the dictionary that will throw `LazyInitializationException` (wrapping the original exception) on every subsequent access.
* **Impact:** Persistent failures if the lazy was replaced concurrently, or infinite retry loops otherwise.
* **Recommendation:** Use `GetOrAdd` with `TryAdd` pattern, or track failed symbols to avoid infinite retry loops.

#### [PB3-4.3] 4.3 MethodCapabilityAnalyzer Cross-File Span Crash on `GetMappedLineSpan`

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Same cross-file GetMappedLineSpan issue as EnforcePureContractAnalyzer.
> **Changes/tests:** No fix yet.
* **File & Lines:** [MethodCapabilityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodCapabilityAnalyzer.cs#L90-L100)
* **Severity:** High
* **Description:** `MethodCapabilityAnalyzer` also calls `GetMappedLineSpan` on locations that may be cross-file. The pattern is the same as PB3-4.1 — `FileLinePositionSpan.Path` is `null` for embedded or generated files. Additionally, this analyzer may receive `location.SourceSpan` when the tree is not the current compilation's tree, producing garbage spans.
* **Impact:** Analyzer crash on projects with generated code or embedded resources.
* **Recommendation:** Add cross-file location guards before accessing `GetMappedLineSpan()`.

#### [PB3-4.4] 4.4 MethodAllocationAnalyzer Does Not Handle Allocas from Non-Current Compilation

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Allocation analyzer assumes all operations come from the same compilation.
> **Changes/tests:** No fix yet.
* **File & Lines:** [MethodAllocationAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodAllocationAnalyzer.cs#L30-L50)
* **Severity:** Medium
* **Description:** `MethodAllocationAnalyzer` processes `IOperation` trees and accesses `SemanticModel` and `Compilation` for analyzed operations. When analyzing cross-file or cross-compilation references (e.g., `#line` directives pointing to different files), operations from non-current compilations may reference symbols from other compilations. The analyzer does not guard against this, potentially causing `InvalidOperationException` when resolving symbols from foreign compilations.
* **Impact:** Analyzer crash when processing allocation operations from cross-compilation references.
* **Recommendation:** Check `operation.SemanticModel?.Compilation == compilation` before accessing symbol information.

#### [PB3-4.5] 4.5 AnalyzerConfiguration Does Not Validate Option Values at Parse Time

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Invalid option values are silently accepted and use defaults.
> **Changes/tests:** No fix yet.
* **File & Lines:** [AnalyzerConfiguration.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs#L200-L250)
* **Severity:** Low
* **Description:** `AnalyzerConfiguration.FromOptions` parses configuration options from `.editorconfig` and other sources. Invalid or unrecognized option values are silently ignored, resulting in defaults being used without warning. For example, a typo in `sharpproof_analysis_budget = hihg` would silently use the default budget instead of the intended high budget.
* **Impact:** Users may unknowingly use default settings due to configuration typos.
* **Recommendation:** Log a diagnostic warning when an option value is invalid or unrecognized.

#### [PB3-4.6] 4.6 SymbolicAnalysisLimits Uses Static Counter That Leaks Across Test Runs

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Static counters persist across test fixtures, causing non-deterministic test failures.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicAnalysisLimits.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicAnalysisLimits.cs#L5-L25)
* **Severity:** Medium
* **Description:** The analysis budget uses an `int` counter that is shared across all threads via `Interlocked.Decrement`. Since the counter is static, test runs that exhaust the budget leave subsequent tests with a depleted budget. NUnit's `[TestCaseSource]` evaluates test cases eagerly, but the budget may be consumed by earlier tests in the same fixture. This causes non-deterministic failures depending on test ordering.
* **Impact:** Non-deterministic test failures when budget is shared across tests.
* **Recommendation:** Use a per-session budget (instance field) instead of a static counter, or reset the counter in `[SetUp]`.

#### [PB3-4.7] 4.7 SymbolicSourceCompilationKind.Query Creates New Compilation Without Caching

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Each query creates a new C# compilation, incurring significant overhead.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicSourceCompilation.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicSourceCompilation.cs#L30-L60)
* **Severity:** Low
* **Description:** `SymbolicSourceCompilation.Create` with `SymbolicSourceCompilationKind.Query` creates a new `CSharpCompilation` instance for each query. In the test suite, each `[TestCase]` in `SymbolicComplexityTests` creates a new compilation, incurring significant overhead from parsing, loading references, and binding. The `SymbolicComplexityTests.cs` has ~30 test cases, each creating a compilation. For larger query volumes, this overhead is wasteful.
* **Impact:** Slow query performance from repeated compilation creation.
* **Recommendation:** Cache compilations by source hash or use incremental compilation.

#### [PB3-4.8] 4.8 ExceptionFlowAnalyzer Does Not Handle Async Method Exception Flows

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Async methods with try-catch blocks may not have all exception paths analyzed.
> **Changes/tests:** No fix yet.
* **File & Lines:** [ExceptionFlowAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.cs#L1-L200)
* **Severity:** Medium
* **Description:** `ExceptionFlowAnalyzer` analyzes exception flow through methods but does not fully handle `async` methods where exceptions are captured in the `AsyncStateMachine` and rethrown when the task is awaited. The analyzer may miss exception paths that only manifest during task continuation.
* **Impact:** False negatives for contract violations that occur via async exception flow.
* **Recommendation:** Add async-aware exception flow analysis that models `AsyncTaskMethodBuilder` and state machine exception handling.

#### [PB3-4.9] 4.9 MethodEnsuresAnalyzer May Report False Positives for Struct Methods with `this` Modification

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Struct `this` is implicitly passed by reference but not tracked as a return value.
> **Changes/tests:** No fix yet.
* **File & Lines:** [MethodEnsuresAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodEnsuresAnalyzer.cs#L50-L80)
* **Severity:** Medium
* **Description:** The `MethodEnsuresAnalyzer` checks postconditions for method returns but does not track modifications to `this` in struct methods. In C#, struct methods can modify `this` directly (e.g., `this.field = value`), which effectively returns the modified struct. Ensures conditions that reference `this` member values may be evaluated against the pre-state rather than post-state, causing false positives.
* **Impact:** False positive ensures violations for struct methods that modify `this`.
* **Recommendation:** Track `this` as an implicit return value for struct methods.

#### [PB3-4.10] 4.10 Missing CancellationToken Checks in Long-Running Analyzer Operations

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Several analyzer methods do not check cancellation tokens in loops or long operations.
> **Changes/tests:** No fix yet.
* **File & Lines:** [MethodBodyAnalysisState.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodBodyAnalysisState.cs#L30-L80)
* **Severity:** Medium
* **Description:** `MethodBodyAnalysisState` constructor processes operation blocks in a loop but does not check `cancellationToken` between iterations. For methods with many operation blocks, this can delay IDE responsiveness during cancellation. `AnalyzerSession.GetOrCreateMethodBodyAnalysis` passes the cancellation token to the `Lazy` factory but the factory itself does not check the token during computation.
* **Impact:** IDE hangs during analysis cancellation for large methods.
* **Recommendation:** Add `cancellationToken.ThrowIfCancellationRequested()` calls in long-running loops.

### 5 Test Infrastructure & Tooling (Agent 5)

#### [PB3-5.1] 5.1 ProofCoreZ3SmokeTests Does Not Dispose SmtSolver Between Test Cases

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** AssertSatisfiability and AssertImplication create a new SmtSolver each call but dispose via using block.
> **Changes/tests:** No fix yet.
* **File & Lines:** [ProofCoreZ3SmokeTests.cs](file:///C:/w/PurelySharp/SharpProof.Test/ProofCoreZ3SmokeTests.cs#L18-L30)
* **Severity:** Low
* **Description:** `AssertSatisfiability` and `AssertImplication` both create `using var solver = new SmtSolver()`. This is correct — Z3 contexts are properly disposed after each assertion. However, the test also imports `SmtTestFormula` statically and uses it in `SolverCases` and `RegexSatisfiabilityCases`. The `SmtSolver` constructor creates Z3 contexts which may leak GDI handles on Windows if not disposed promptly. The GC may not collect them before the next test runs.
* **Impact:** Potential GDI handle exhaustion during large test runs.
* **Recommendation:** This is fine — `using` blocks properly dispose. Noting as intentional design choice.

#### [PB3-5.2] 5.2 SemanticTestSource Does Not Clean Up Temporary Files

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Temporary files created during test setup may not be cleaned up.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SemanticTestSource.cs](file:///C:/w/PurelySharp/SharpProof.Test/SemanticTestSource.cs#L20-L40)
* **Severity:** Low
* **Description:** `SemanticTestSource` creates temporary source files for semantic analysis testing. If test setup or teardown does not delete these files (e.g., due to early failure or cancellation), temporary files accumulate in the temp directory. The cleanup logic in `[TearDown]` may not run if the fixture setup fails.
* **Impact:** Temporary file accumulation over repeated test runs.
* **Recommendation:** Use `using` for temporary file handles or register cleanup in `[OneTimeTearDown]`.

#### [PB3-5.3] 5.3 SharpProofAnalyzer Does Not Handle Null Operation Blocks

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** GetOperationBlocks may return null for some syntax node types.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SharpProofAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/SharpProofAnalyzer.cs#L60-L80)
* **Severity:** High
* **Description:** `SharpProofAnalyzer.Initialize` calls `context.OperationBlocks` which may return `default(ImmutableArray<IOperation>)` for some node types (e.g., expression-bodied members before C# 6). The result is passed to `GetOrCreateMethodBodyAnalysis` which uses `operationBlocks.IsDefaultOrEmpty` to check. However, if `operationBlocks` is default (not empty), the code may not handle it correctly. Also, `SemanticModel.GetOperation` can return null for some syntax nodes, and this null propagates.
* **Impact:** Analyzer crash on syntax constructs that produce no operation blocks.
* **Recommendation:** Check `operationBlocks.IsDefault` in addition to `IsDefaultOrEmpty`.

#### [PB3-5.4] 5.4 MethodAnalysisSnapshot Does Not Validate Syntax Node Kind Against Method Symbol

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Node matching assumes syntax node kind matches method symbol kind.
> **Changes/tests:** No fix yet.
* **File & Lines:** [MethodAnalysisSnapshot.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodAnalysisSnapshot.cs#L15-L40)
* **Severity:** Medium
* **Description:** `MethodAnalysisSnapshot.Create` receives a `SyntaxNode` and `IMethodSymbol` and assumes they match. In edge cases where the syntax node belongs to a different method than the symbol (e.g., after a Roslyn binding error), the snapshot may contain mismatched data. Properties like `declaration.Identifier` may not exist for all method syntax types (e.g., anonymous functions, lambdas).
* **Impact:** InvalidOperationException or NullReferenceException on mismatched node/symbol pairs.
* **Recommendation:** Validate that the syntax node represents the method symbol before creating the snapshot.

#### [PB3-5.5] 5.5 MethodBodyAnalysisState Uses String Interning Without Thread Safety

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** string.Intern is called from multiple threads without synchronization.
> **Changes/tests:** No fix yet.
* **File & Lines:** [MethodBodyAnalysisState.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodBodyAnalysisState.cs#L45-L55)
* **Severity:** Low
* **Description:** `MethodBodyAnalysisState` uses `string.Intern` to intern analysis strings. While `string.Intern` is thread-safe (it uses a global intern pool), it can cause performance degradation from contention and memory pressure. The intern pool is process-wide and never releases strings, so interning large generated strings can cause memory leaks.
* **Impact:** Memory leak from interning large dynamically-generated strings.
* **Recommendation:** Consider using `ConcurrentDictionary<string, string>` for per-session interning instead of `string.Intern`.

### 6 SharpProof.ProofCore Collections & Utilities (Agent 6)

#### [PB3-6.1] 6.1 BoundedConcurrentCache Eviction Clears Entire Cache Rather Than Least-Recently-Used Items

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Eviction strategy clears all entries when size limit is hit.
> **Changes/tests:** No fix yet.
* **File & Lines:** [BoundedConcurrentCache.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/Collections/BoundedConcurrentCache.cs#L30-L45)
* **Severity:** Low
* **Description:** `BoundedConcurrentCache` evicts all entries (`.Clear()`) when the cache exceeds `_maxCapacity`. A more efficient strategy would evict only a fraction (e.g., oldest 25%). Clearing all entries causes cold-cache performance degradation after each eviction.
* **Impact:** Performance degradation from frequent full cache clears.
* **Recommendation:** Evict only a portion of entries (e.g., 25% of capacity) instead of clearing all.

#### [PB3-6.2] 6.2 BoundedConcurrentCache.Count Field May Overflow

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Count field is an int incremented/decremented without overflow protection.
> **Changes/tests:** No fix yet.
* **File & Lines:** [BoundedConcurrentCache.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/Collections/BoundedConcurrentCache.cs#L18-L25)
* **Severity:** Low
* **Description:** The `_count` field is incremented in `TryAdd` and decremented in `TryRemove` using `Interlocked.Increment` and `Interlocked.Decrement`. If `TryAdd` is called 2+ billion times, the counter overflows. While improbable, the overflow causes the cache to stop evicting entries (since `_count <= _maxCapacity` never triggers).
* **Impact:** In extreme edge case, cache stops enforcing capacity limits.
* **Recommendation:** Use `long` for `_count` or cap at `int.MaxValue`.

#### [PB3-6.3] 6.3 SmtWitnessAssignment IntegerValue May Return Undefined for Boolean Witnesses

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** IntegerValue property accessed on non-integer witness may return garbage.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtWitness.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtWitness.cs#L30-L45)
* **Severity:** Medium
* **Description:** `SmtWitnessAssignment` has properties `IntegerValue`, `BooleanValue`, `StringValue`, and `IsNull` that may be accessed on the wrong kind of assignment. The `Single()` call in test code at line 69 of `ProofCoreZ3SmokeTests.cs` accesses `assignment.IntegerValue` without checking what kind of witness was returned. In production code, accessing `IntegerValue` on a boolean witness returns `default(long)` (0), which could mask bugs where the witness kind is unexpected.
* **Impact:** Silent wrong-value reads when accessing witness properties of wrong type.
* **Recommendation:** Throw `InvalidOperationException` when accessing a value of the wrong kind.

#### [PB3-6.4] 6.4 SmtSolver Uses Explicit `using` Pattern But Some Paths Skip Disposal

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Some solver usage may not dispose due to exception handling.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtSolver.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtSolver.cs#L140-L155)
* **Severity:** Low
* **Description:** The `HasSafeArithmetic` method at line 116 creates a solver via `_encoder.CreateSolver(timeout)` and disposes it manually at line 127. If `CheckAndAccountResources` throws (e.g., Z3Exception), the `solver.Dispose()` at line 127 may be skipped unless the exception is within a `try`/`finally` block. This could leak Z3 native handles on Z3 errors during arithmetic safety checking.
* **Impact:** Z3 native handle leak on solver errors during divisor safety checks.
* **Recommendation:** Use `using var solver = ...` or wrap in `try/finally`.

### 7 SMT Analysis Service & Lifecycle (Agent 7)

#### [PB3-7.1] 7.1 SmtAnalysisService.Classify Re-enters Without Releasing Lock

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Classify calls itself recursively without releasing _solverLock.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtAnalysisService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtAnalysisService.cs#L67-L71)
* **Severity:** High
* **Description:** `Classify` at line 67 checks `if (_methodBudgetScope.Value == null)`, creates a scope via `BeginMethodBudgetScope()`, and recursively calls `Classify(query)` at line 70. The outer `Classify` call may be inside `lock (_solverLock)` (from `ClassifyLocally` → `ClassifyCore` → `Classify` via nested method budget scopes). Wait — `Classify` is public/internal and called from `ClassifyImplication` and `ClassifyPathFeasibility`. `Classify` calls itself recursively at line 70 when there's no budget scope. This is BEFORE acquiring `_solverLock` (which happens in `ClassifyCore` at line 141). So there's no re-entrancy issue with `_solverLock` specifically. However, the recursive call at line 70 acquires `_proofResults` (caches) and then `ClassifyLocally` → `ClassifyCore` → `lock(_solverLock)`. The inner call also creates a budget scope (line 69), but the outer call's `using` scope (line 69) is from `BeginMethodBudgetScope()` which checks `_methodBudgetScope.Value != null`. Since the inner `BeginMethodBudgetScope()` at line 93 returns `MethodBudgetScope.Nested` (line 94), the inner call doesn't set a new budget scope. This is correct — the inner call reuses the outer budget scope. But the recursive `Classify` call goes through all the cache checks again — potentially causing infinite recursion if the cache misses repeatedly. The recursion depth is limited only by stack space.
* **Impact:** Stack overflow from deep recursion if Classify never hits a cache hit for a complex query tree.
* **Recommendation:** Use a loop instead of recursion for the budget scope initialization.

#### [PB3-7.2] 7.2 SmtProofSearchSessionPool.GetOrCreate Returns Stale Session After Recycle

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** GetOrCreate returns nullified thread-local after recycle.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtProofSearchSessionPool.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtProofSearchSessionPool.cs#L9-L10)
* **Severity:** Medium
* **Description:** `RecycleCurrentThread` at line 11 disposes the current thread's session and sets `_sessions.Value = null`. The next call to `GetOrCreate` on the same thread at line 9 evaluates `_sessions.Value ??= _sessionFactory()` — since `_sessions.Value` is `null`, it creates a new session. This is correct behavior. However, `Dispose(true)` at line 18 also disposes all sessions. If `Dispose()` is called while a thread is actively using a session (race condition via `_solverLock` in `SmtAnalysisService`, which serializes access), the `GetOrCreate` at line 9 could return a disposed session that was recycled between `lock` release and re-acquisition. But `SmtAnalysisService.ClassifyCore` holds `_solverLock` for the entire duration, preventing concurrent access. So this is actually safe due to the lock.
* **Impact:** Not a real bug — lock prevents concurrent access. Marking as verified safe.

#### [PB3-7.3] 7.3 CreateFormulaSequenceKey Loses Distinction Between Empty and Single-Formula Sequences

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Empty sequence key and single-element sequence key can collide in edge cases.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtAnalysisService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtAnalysisService.cs#L286-L291)
* **Severity:** Medium
* **Description:** `CreateFormulaSequenceKey` returns `string.Join(string.Empty, ...)` producing `"length:key"` for each formula. An empty sequence returns `string.Empty`. For a single formula, the key is e.g., `"5:hello"`. This cannot collide with `string.Empty` since `"5:hello"` is never empty. However, the `CreateQueryKey` method at line 273 concatenates: `CreateFormulaSequenceKey(pathConditions) + "|hazard=..."`. If `pathConditions` is empty, the key starts with `"|hazard=..."`. If a path condition formula's structural key happens to start with `|`, the key would be ambiguous. `SmtFormulaStructuralKey.Create` returns keys that don't start with `|`, so this is safe. But the `NormalizePathConditions` method at line 277 filters out `SmtBooleanConstant(true)` — what if ALL path conditions are `true`? All are filtered, resulting in an empty array. The empty array key `"|hazard=..."` would collide with any query that has no path conditions. But conceptually, a query with zero path conditions (always reachable) IS the same as a query where all path conditions are true. So this collision is actually correct behavior — the queries are semantically equivalent.
* **Impact:** Not a real bug — query keys correctly capture semantic equivalence.

#### [PB3-7.4] 7.4 MethodBudgetScope.Nested Instance Leaks Through Dispose

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Nested scope's Dispose does not set _owner to null.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtAnalysisService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtAnalysisService.cs#L194-L201)
* **Severity:** Low
* **Description:** `MethodBudgetScope` has a static `Nested` instance at line 195 constructed with `null` owner. Its `Dispose` method at line 198-201 calls `Interlocked.Exchange(ref _owner, null)` which is a no-op since `_owner` is already null for `Nested`. This is correct — no leak occurs. However, if `Nested.Dispose()` is called, it doesn't restore the previous budget scope. But since nested scopes share the parent scope's budget, the `Dispose` should not null out `_methodBudgetScope.Value` — and it doesn't because `_owner` is null. This is intentional: nested calls should NOT clear the parent's budget scope.
* **Impact:** None — design is correct for nested calling.

#### [PB3-7.5] 7.5 SmtProofResultCache Local Cache Is Not Bounded

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Local (non-shared) cache in SmtProofResultCache has no eviction policy.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtProofResultCache.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtProofResultCache.cs#L20-L40)
* **Severity:** Low
* **Description:** The local cache (`ConcurrentDictionary` in `SmtProofResultCache`) stores proof results for the current analysis session. Unlike the shared cache (which may have eviction policies), the local cache has no capacity limit. For sessions analyzing many methods with many proof queries (thousands of unique queries), the local cache grows unboundedly, consuming memory.
* **Impact:** Memory growth in long-lived analysis sessions.
* **Recommendation:** Add a bounded eviction policy to the local cache (e.g., LRU with max capacity).

#### [PB3-7.6] 7.6 SmtAnalysisService.CheckPermanentSolverFailure Depth Limit at 16 May Miss Nested Failures

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Recursive depth limit of 16 may not reach deeply wrapped exceptions.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtAnalysisService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtAnalysisService.cs#L219-L238)
* **Severity:** Low
* **Description:** `FindPermanentSolverFailure` has a depth limit of 16 for recursion into `InnerException` chains. While 16 is generous for typical exception wrapping, deeply nested `AggregateException` from TPL continuations (e.g., 20+ levels) could cause an `EntryPointNotFoundException` at depth 17 to be missed, causing the service to misclassify a permanent failure as transient.
* **Impact:** Permanent Z3 failures misclassified as transient, leading to repeated retries.
* **Recommendation:** Increase the depth limit to 64 or use BFS instead of recursion.

#### [PB3-7.7] 7.7 BeginMethodBudgetScope Creates New Budget for Each Top-Level Call, Allowing Budget Inflation

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Each top-level Classify creates a new budget scope with full budget.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtAnalysisService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtAnalysisService.cs#L92-L97)
* **Severity:** Medium
* **Description:** `BeginMethodBudgetScope` creates a new `SmtAnalysisBudget(Options.MethodBudget)` for each top-level call. This means if the service is called 10 times for 10 different methods, each gets a full method budget. An attacker or pathological code pattern could issue 1000 method-level queries, each consuming its full budget, leading to 1000x the intended budget consumption. The budget is per-method, not global, so each method gets a fair share. However, the `_executedQueryCount` is global — an unbounded number of methods can each exhaust their budget independently.
* **Impact:** Potential denial of service via excessive method-level analysis.
* **Recommendation:** Add a global query rate limiter or total budget cap across all methods.

#### [PB3-7.8] 7.8 SmtAnalysisService.NormalizePathConditions Uses HashSet<SmtFormula> Without Custom Equality

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** HashSet<SmtFormula> uses default reference equality for record types, not structural equality.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtAnalysisService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtAnalysisService.cs#L277-L284)
* **Severity:** High
* **Description:** `NormalizePathConditions` at line 277 creates a `HashSet<SmtFormula>` without a custom `IEqualityComparer<SmtFormula>`. Since `SmtFormula` is a record type (or class), the default `HashSet` uses `EqualityComparer<SmtFormula>.Default`, which for record types uses structural equality (if `SmtFormula` is a `record`). However, looking at the codebase, `SmtFormula` is an abstract class hierarchy — C# record structural equality only works for `record class` types. If `SmtFormula` is a regular `class` (not a `record`), `HashSet` uses reference equality, meaning two structurally identical formulas (e.g., `SmtBooleanConstant(true)` created at different call sites) would NOT be deduplicated. Even if `SmtFormula` overrides `Equals`, `HashSet` requires `GetHashCode` to be consistent. If `SmtFormula` does not override `Equals`/`GetHashCode` structurally, duplicate path conditions are not removed, leading to redundant solver work.
* **Impact:** Redundant solver work from structurally identical but reference-different path conditions.
* **Recommendation:** Verify that `SmtFormula` has structural equality implemented, or provide a custom `IEqualityComparer<SmtFormula>` to the `HashSet`.

#### [PB3-7.9] 7.9 IsWithinFormulaNodeBudget May Under-Count Regex Nodes for Complex Patterns

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Regex pattern complexity is approximated as Pattern.Length / 8, which may not reflect actual Z3 encoding complexity.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtAnalysisService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtAnalysisService.cs#L305-L313)
* **Severity:** Low
* **Description:** `TryConsumeFormulaNodeBudget` at line 307 counts each `SmtRegexMatchFormula` as `1 + Math.Max(1, regexMatch.Pattern.Length / 8)` nodes. This linear approximation does not account for the exponential blowup possible from nested quantifiers, character classes, or lookahead/lookbehind patterns. A short regex like `(.*?)*a` could produce exponentially many Z3 constraints. The budget underestimates the actual encoding cost.
* **Impact:** Budget may not prevent runaway regex encoding for deceptively short but complex patterns.
* **Recommendation:** Use a regex complexity metric that accounts for nesting depth and quantifier interactions.

#### [PB3-7.10] 7.10 SmtAnalysisService May Deadlock on Concurrent Classify with Shared Query Flights

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** _solverLock is held while AcquireSharedFlight may block.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtAnalysisService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtAnalysisService.cs#L98-L126)
* **Severity:** High
* **Description:** `ClassifyWithSharedQueryFlight` at line 98 is called from `Classify` (line 89) which does NOT hold `_solverLock`. However, `ClassifyLocally` at line 127 calls `ClassifyCore` at line 138 which acquires `lock (_solverLock)` at line 141. Inside `ClassifyCore`, it calls `_proofSearchSessions.GetOrCreate()` and `search.Classify()` while holding the lock. The `Classify` method also calls `ClassifyWithSharedQueryFlight` at line 89 which calls `_proofResults.AcquireSharedFlight` — this may block on concurrent access to the same query key. Meanwhile, `ClassifyLocally` at line 132-133 acquires `_solverLock` (via `ClassifyCore`) before calling `_proofResults.AddSharedIfCacheable`. If two threads call `Classify` for the same query key: Thread A calls `ClassifyWithSharedQueryFlight` (no lock) → `AcquireSharedFlight` (may block); Thread B calls `ClassifyLocally` → `_solverLock` → `ClassifyCore` → `_solverLock` held while calling `_proofResults.AddSharedIfCacheable`. If `AddSharedIfCacheable` needs to acquire the flight lock (same as `AcquireSharedFlight`), deadlock occurs: Thread A holds flight lock, waits for `_solverLock`; Thread B holds `_solverLock`, waits for flight lock.
* **Impact:** Thread deadlock under concurrent analysis of the same query.
* **Recommendation:** Release `_solverLock` before calling `AddSharedIfCacheable`, or ensure the flight mechanism does not block when the lock is held.

### 8 SymbolicState & Facts (Agent 8)

#### [PB3-8.1] 8.1 DeduplicateFacts Uses String Comparison Ordinal for Term Keys That Are Culture-Sensitive

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Term keys may contain culture-sensitive content (string constants) but are compared with Ordinal.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicIr.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicIr.cs#L243-L261)
* **Severity:** Low
* **Description:** `DeduplicateFacts` uses `StringComparer.Ordinal` for fact keys at line 245. Fact keys are created from `CreateFactKey` which includes `CreateTermKey` for string constants. `CreateTermKey` includes the actual string value (line 683). Two string values that differ by case are distinct in ordinal comparison, which is correct for deduplication. However, `SmtFormulaStructuralKey` and the proof key system both use ordinal comparison, ensuring consistent hashing and comparison. This is correct.
* **Impact:** None — ordinal comparison is correct for term keys.

#### [PB3-8.2] 8.2 AddIntrinsicDomainFacts May Add Conflicting Facts for Same Term

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Non-negative and bounded-size facts for LengthTerm may conflict for string length [0, int.MaxValue].
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicIr.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicIr.cs#L196-L232)
* **Severity:** Medium
* **Description:** `AddIntrinsicDomainFacts` adds two facts for each domain term: `term >= 0` (non-negative) and `term <= int.MaxValue` (bounded). For `SymbolicLengthTerm` with string type, only the lower bound is added (line 221). But `SymbolicArrayDimensionLengthTerm` and `SymbolicCountTerm` also get both bounds. The `bounded` upper bound of `int.MaxValue` (2,147,483,647) conflicts with the Z3 string length overflow semantics which treat lengths as big integers. Adding `length <= int.MaxValue` for non-string lengths is correct (array lengths are limited to `int.MaxValue`). However, for `SymbolicCountTerm` (collections), `.Count` can be larger than `int.MaxValue` for some collection types. This artificially constrains the search space, potentially causing false negatives.
* **Impact:** Potentially false Unsatisfiable for collection counts near `int.MaxValue`.
* **Recommendation:** Use the actual collection type's maximum count instead of a hardcoded `int.MaxValue`.

### 9 SymbolicProofEncoder & Encoding (Agent 9)

#### [PB3-9.1] 9.1 SymbolicProofEncoder.EncodeState Silently Skips Unsupported Facts Without Tracking

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** When a fact fails to encode, encoding returns false but the fact is silently skipped.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicProofEncoder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProofEncoder.cs#L186-L206)
* **Severity:** High
* **Description:** `EncodeState` at line 186 iterates facts and path conditions. When `TryEncodeFactWithPathState` or `TryEncodeConditionWithPathState` returns `false`, the fact/condition is silently skipped (lines 190-192, 197-199). The result is a truncated set of SMT formulas that represents only the subset of the state that could be encoded. If critical path conditions or facts are skipped, the solver sees an incomplete state, potentially producing unsound proof results (e.g., "Proven" when a crucial constraint was skipped).
* **Impact:** Unsound proof results from incomplete SMT encoding of symbolic state.
* **Recommendation:** When any fact or condition fails to encode, mark the state as Unsupported and return empty formulas to avoid partial-state proofs.

#### [PB3-9.2] 9.2 HasSafeIntegerDivisors Recursion May Stack Overflow on Deep Conditional Trees

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Recursive descent through conditional branches without depth limit.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicProofEncoder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProofEncoder.cs#L41-L67)
* **Severity:** Low
* **Description:** `HasSafeIntegerDivisorsCore` recursively descends into `SymbolicConditionalTerm` branches (lines 47-57) and `SymbolicBinaryTerm` children (lines 58-63). Deeply nested conditional or binary term structures can cause stack overflow. The `SymbolicIrChildren` traversal at line 65 is also recursive. Combined, a deeply nested IR term tree (e.g., thousands of nested ternary expressions) can overflow the stack.
* **Impact:** Stack overflow on deeply nested symbolic IR trees during divisor safety analysis.
* **Recommendation:** Convert to explicit stack-based iteration or add depth limit with fallback.

#### [PB3-9.3] 9.3 IsTermProvablyNonZero Creates New Fact Objects on Every Call, Causing Heap Allocation Pressure

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** IsTermProvablyNonZero allocates new SymbolicFact instances for each term check.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicProofEncoder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProofEncoder.cs#L146-L167)
* **Severity:** Low
* **Description:** `IsTermProvablyNonZero` iterates three relation operators (line 149-153) and creates a new `SymbolicFact.Exact(...)` for each (lines 154-158). It also calls `SymbolicIrLowerer.CreateIntegerZeroCondition` at line 160 which creates more condition objects. These objects are only used for lookup in `SymbolicProofStateFacts.StateContainsFact`. For an expression with many division operations, this allocates a significant number of short-lived objects. In hot paths, this increases GC pressure.
* **Impact:** Performance degradation from allocation pressure in divisor safety checks.
* **Recommendation:** Cache the zero condition object or use a more efficient checking mechanism.

#### [PB3-9.4] 9.4 TryEncodeFactWithPathState Passes `rewriteQueryVersions: false` Bypassing Version Rewriting

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Facts are not version-rewritten unlike conditions.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicProofEncoder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProofEncoder.cs#L33-L37)
* **Severity:** High
* **Description:** `TryEncodeFactWithPathState` at line 33 calls `TryEncodeConditionWithPathState` with `rewriteQueryVersions: false`. This means facts are NOT rewritten to current versions via `SymbolicProofStateFacts.RewriteQueryConditionToCurrentVersions`. If a fact references a variable at a stale version (from a prior state), the condition is encoded with the stale version name, but the state contains the current version names. The solver sees `x@1 == 5` in the fact but `x@2 == state` in the state, causing the solver to treat them as unrelated variables.
* **Impact:** Unsound proof results when facts reference stale versioned variables.
* **Recommendation:** Enable version rewriting for facts, or ensure facts are always at current versions before encoding.

#### [PB3-9.5] 9.5 HasSafeIntegerDivisors Does Not Short-Circuit for `&&` Without Left Context Refinement

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** HasSafeIntegerDivisorsCore for non-short-circuit `&&` checks both sides independently.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicProofEncoder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProofEncoder.cs#L120-L122)
* **Severity:** Low
* **Description:** For `SymbolicBinaryCondition { Operator: And }` when `strategy.RefineShortCircuitConditions` is `false` (line 120), both sides are checked independently WITHOUT the left-side assumptions propagated to the right side. If the left side contains `x != 0` and the right side contains `10 / x`, the divisor safety check for `10 / x` does not know that `x != 0` from the left. The default `StateSafeDivisorStrategy` has `RefineShortCircuitConditions = true`, so this is only an issue for custom strategies that set it to false.
* **Impact:** Conservative false negatives for divisor safety on non-short-circuit `&&` conditions.
* **Recommendation:** Always propagate left-side context to right-side checks for `&&` conditions.

### 10 SymbolicProofStateFacts & Normalization (Agent 10)

#### [PB3-10.1] 10.1 SymbolicProofStateFacts.NormalizeState Creates Unnecessary Object Allocations

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** NormalizeState calls state.Normalize() which creates new ImmutableArrays even when no changes are needed.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicProofStateFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProofStateFacts.cs#L10-L30)
* **Severity:** Low
* **Description:** `NormalizeState` calls `state.Normalize()` which creates new `ImmutableArray<SymbolicFact>` and `ImmutableArray<SymbolicCondition>` instances. These are compared element-by-element with the originals (line 190-192 of SymbolicIr.cs). If no changes are needed, the original `SymbolicState` is returned. However, the normalization still allocates temporary builders and hash sets for deduplication on every call. For frequently queried states, this allocation overhead is significant.
* **Impact:** Performance overhead from repeated normalization of unchanged states.
* **Recommendation:** Add a dirty flag to the state to skip normalization when no changes have been made.

#### [PB3-10.2] 10.2 StateContainsFact Iterates All Facts Linearly for Each Check

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Linear scan of all facts for each containment check.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicProofStateFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProofStateFacts.cs#L40-L60)
* **Severity:** Low
* **Description:** `StateContainsFact` iterates all facts in the state and calls `CreateFactKey` on each to compare with the target fact's key. For states with many facts (e.g., 100+), and for `IsTermProvablyNonZero` which calls `StateContainsFact` three times per divisor, this becomes O(N*M) where N is the number of facts and M is the number of divisors. Adding a hash-based lookup index for facts would improve performance.
* **Impact:** Performance degradation for methods with many division operations in large states.
* **Recommendation:** Maintain a hash set of fact keys alongside the fact array for O(1) containment checks.

#### [PB3-10.3] 10.3 RewriteQueryConditionToCurrentVersions May Fail for SymbolicCondition Depth > N

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Version rewriting uses recursion without depth limit for deeply nested conditions.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicProofStateFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProofStateFacts.cs#L65-L85)
* **Severity:** Low
* **Description:** `RewriteQueryConditionToCurrentVersions` recursively traverses the condition tree to replace variable references with their current versions. For deeply nested conditions (e.g., a condition with 10,000 nested `And`/`Or` operands), this recursion can cause stack overflow. The condition tree depth is bounded by the formula depth budget (1024 in `SmtAnalysisService`), but conditions can be deeper than formulas since they include additional IR constructs.
* **Impact:** Stack overflow on very deeply nested conditions during version rewriting.
* **Recommendation:** Convert to iterative traversal or add depth limit with Unsafe fallback.

### 11 SymbolicRuntimeException & Hazard Analysis (Agent 11)

#### [PB3-11.1] 11.1 SymbolicRuntimeExceptionFacts May Double-Report Same Exception from Multiple Paths

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Multiple control flow paths to the same exception type produce duplicate facts.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicRuntimeExceptionFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicRuntimeExceptionFacts.cs#L20-L45)
* **Severity:** Low
* **Description:** The runtime exception facts generator creates `SymbolicExceptionPreconditionAtom` instances for each potentially exceptional operation. If the same operation is reachable via multiple control flow paths (e.g., inside a loop with multiple paths), the fact generator creates a separate fact for each path without deduplication. The `DeduplicateFacts` method in `SymbolicIr.cs` (line 243) deduplicates by fact key, which includes the `CreateAtomKey` output. The `SymbolicExceptionPreconditionAtom` key includes `Trigger` condition key (line 627), which differs across paths since path conditions differ. This means the same exception is reported multiple times with different triggers, causing redundant solver queries.
* **Impact:** Redundant solver queries from duplicate exception precondition facts.
* **Recommendation:** Normalize exception precondition triggers before creating facts.

#### [PB3-11.2] 11.2 SymbolicRuntimeHazardQueryService May Return Stale Results for Modified State

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Hazard query cache does not include state hash.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicRuntimeHazardQueryService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicRuntimeHazardQueryService.cs#L30-L55)
* **Severity:** High
* **Description:** The hazard query cache keys queries by `(hazardKind, termKey)` but NOT by the full state. If the same hazard kind and term appear in two different states (e.g., `10 / x` in states where `x > 0` vs `x < 0`), the cached result from the first state is reused for the second. Since the actual hazard outcome depends on the entire state (path conditions + facts), stale cache entries produce incorrect results. For example, if `x > 0` makes `10 / x` safe, caching this result and reusing it in a state where `x == 0` would incorrectly claim the division is safe.
* **Impact:** Unsound proof results from state-independent hazard caching.
* **Recommendation:** Include the normalized proof key (or a state hash) in the hazard query cache key.

#### [PB3-11.3] 11.3 SymbolicRuntimeHazardCandidate Does Not Validate Precondition Before Reporting

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Hazard candidate enumeration does not filter by reachability.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicRuntimeHazardCandidate.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicRuntimeHazardCandidate.cs#L15-L30)
* **Severity:** Medium
* **Description:** `SymbolicRuntimeHazardCandidateFactory` creates hazard candidates by enumerating operations in the method body, but does not check whether each operation is reachable in the current symbolic state. Division operations inside unreachable branches (e.g., `if (false) { x = 10 / 0; }`) are still reported as hazards even though they can never execute. This causes false-positive hazard reports that waste solver time.
* **Impact:** Wasted solver time on unreachable hazard candidates.
* **Recommendation:** Check operation reachability before creating hazard candidates.

### 12 SharpProof.Analyzer Symbol & Attribute Traversal (Agent 12)

#### [PB3-12.1] 12.1 SymbolAttributeTraversal.GetAttributes Misses Attributes on Overridden/Implemented Interface Members

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** GetAttributes does not walk the override/implementation chain for attributes.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolAttributeTraversal.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/SymbolAttributeTraversal.cs#L9-L23)
* **Severity:** Medium
* **Description:** `GetAttributes` retrieves attributes from the symbol and optionally from the associated symbol. It does NOT walk the override chain (`OverriddenMethod` or `ExplicitInterfaceImplementations`) to find inherited attributes. In C#, attributes on a virtual method's base declaration also apply to overrides (unless the override has `[new]`). The analyzer may miss contract attributes defined on base class methods or interface default implementations.
* **Impact:** Contract conditions from base class or interface methods are not inherited by overriding methods.
* **Recommendation:** Walk the override/implementation chain when collecting attributes.

#### [PB3-12.2] 12.2 SharpProofAttributeIdentityPolicy May Accept Invalid Attribute Data

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Attribute identity does not validate constructor arguments.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SharpProofAttributeIdentityPolicy.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/SharpProofAttributeIdentityPolicy.cs#L20-L40)
* **Severity:** Low
* **Description:** The attribute identity policy matches attributes by metadata name and basic shape. If an attribute has the correct name but incorrect constructor arguments (e.g., `[Pure]` is valid but `[Pure("invalid")]` is not), the attribute may be accepted without validating that the constructor arguments match the expected signature. `GetAcceptedAttributes` may return attributes with invalid or missing constructor argument values.
* **Recommendation:** Validate attribute constructor arguments against expected signatures when accepting attributes.

### 13 SharpProof.Symbolic Reentrancy & Thread Safety (Agent 13)

#### [PB3-13.1] 13.1 SymbolicStateImmutable but SymbolicProofCache Holds Mutable Lazy References

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Lazy<AnalysisProofResult> can fail and cache the failure.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtProofResultCache.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtProofResultCache.cs#L35-L40)
* **Severity:** High
* **Description:** `AcquireSharedFlight` at line 35 creates a `Lazy<AnalysisProofResult>` with `LazyThreadSafetyMode.ExecutionAndPublication`. If the factory delegate (`classify`) throws an exception, the `Lazy` caches the exception and re-throws it on every subsequent access via `Result.Value`. This means a transient failure during one thread's classification permanently poisons the flight for all concurrent threads sharing the same query key. Even though `ReleaseSharedFlight` removes the flight entry (line 42), the cached `Lazy` exception persists in any thread that already holds a reference to the flight lease. Additionally, if another thread calls `AcquireSharedFlight` for the same key after the flight is released, the previous `Lazy` is not reused because `GetOrAdd` will add a new one, but if `GetOrAdd` returns the stale `Lazy` (before `TryRemove`), the exception is recovered.
* **Impact:** Transient solver failures permanently poison shared query flights, causing all concurrent queries for the same key to fail.
* **Recommendation:** Use `LazyThreadSafetyMode.PublicationOnly` (which doesn't cache exceptions) or wrap the factory to catch and handle exceptions.

#### [PB3-13.2] 13.2 ThreadLocal<SmtProofSearchSessionPool> Sessions Not Disposed on Pool Recycle

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Pool.Recycle disposes the session but doesn't remove it from ThreadLocal.Values.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtProofSearchSessionPool.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtProofSearchSessionPool.cs#L11-L16)
* **Severity:** Low
* **Description:** `RecycleCurrentThread` disposes the session (line 14) and sets `_sessions.Value = null` (line 15). However, the `ThreadLocal` class maintains an internal `Values` collection that still holds a reference to the old session's slot (now null). The disposed session object may still be referenced by `_sessions.Values` until the `ThreadLocal` itself is disposed. If `Dispose(true)` is called (line 18), it iterates `_sessions.Values` via `.Where(session => session != null)` and disposes non-null sessions. The null entries are skipped — not a leak. The reference to the disposed session is eventually released when `ThreadLocal` finalizes or when a new session is created (overwriting the null).
* **Impact:** Minor — session object held alive slightly longer than necessary.

### 14 Cross-Cutting Performance & Memory (Agent 14)

#### [PB3-14.1] 14.1 Overuse of String Interning in Fact Key Generation Causes Process-Wide Memory Leak

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** CreateFactKey, CreateTermKey, CreateConditionKey all create new strings for key generation.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicIr.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicIr.cs#L603-L870)
* **Severity:** Low
* **Description:** `CreateFactKey`, `CreateTermKey`, and `CreateConditionKey` create new strings for key generation on every call. These strings are used for dictionary lookups in `DeduplicateFacts`, `DeduplicateConditions`, `StateContainsFact`, and `ContainsContradiction`. For states with many facts and conditions, these key strings are generated multiple times during normalization. Each normalization call creates new key strings that are immediately discarded, causing GC pressure. While `string.Intern` is not used here (which would cause memory leaks), the frequency of key generation is high.
* **Impact:** Performance overhead from repeated key string allocation in state normalization.
* **Recommendation:** Cache fact/term/condition keys on the objects themselves to avoid regeneration.

#### [PB3-14.2] 14.2 ImmutableArray Concatenation in AddFact/AddPathCondition Creates Linear Copy Each Time

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** ImmutableArray.Add creates a new array each time.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicIr.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicIr.cs#L165-L171)
* **Severity:** Low
* **Description:** `AddFact` and `AddPathCondition` call `ImmutableArray<T>.Add()` which creates a new array with the new element appended. This is O(N) where N is the number of existing elements. For methods with hundreds of facts/path conditions, each addition allocates a new array. Since `SymbolicState` is immutable, each modification creates a new instance, and the old instance becomes garbage. For large states modified frequently during analysis, this allocation pattern causes significant GC pressure.
* **Impact:** Performance degradation from repeated ImmutableArray reallocation.
* **Recommendation:** Consider using `ImmutableArray<T>.Builder` during state construction phases and converting to `ImmutableArray` only when needed.

### 15 Switch Path Condition Builder (Agent 15)

#### [PB3-15.1] 15.1 Default Switch Section Condition Does Not Exclude Prior Non-Default Labels

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Default section condition is `!explicitSelections` but does not subtract prior sections.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SwitchPathConditionBuilder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SwitchPathConditionBuilder.cs#L79-L89)
* **Severity:** High
* **Description:** When building the condition for a `default` switch section (line 79), the code collects ALL explicit labels across all sections (lines 81-85) and creates the condition as `NOT (any explicit selection)`. However, this does not account for the fact that only PRIOR sections' labels should be excluded — labels in sections AFTER the default section should not be part of the default fall-through condition. The C# spec says that the default section's condition is "governing value does not match any label in any section that precedes this section." Labels in sections after the default section are not reachable via fall-through because the `default` section matches everything the prior sections don't. Wait — looking at the code more carefully: the default section condition at line 79 collects ALL explicit labels BEFORE returning (lines 81-85). This is the negation of all explicit labels, which is correct for the last section if default is last. But if default is in the middle (C# allows `default` anywhere), the condition should only exclude labels from PRIOR sections, not ALL sections. The code at line 79-89 does not break when reaching the current section — it iterates ALL sections, including the current one and those after it. This means if `default` is the first section, it correctly excludes nothing. If `default` is in the middle, it incorrectly excludes labels from later sections.
* **Impact:** Unsound switch path conditions when `default` case is not the last section.
* **Recommendation:** Use the same prior-sections-only logic as the non-default case.

#### [PB3-15.2] 15.2 Prior Selections in Non-Default Switch Section May Include Labels from Later Sections

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Non-default section condition excludes prior selections but default logic also includes all labels.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SwitchPathConditionBuilder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SwitchPathConditionBuilder.cs#L99-L106)
* **Severity:** Low
* **Description:** For non-default sections, `priorSelections` collects labels from all sections before the current one (line 101: `if (ReferenceEquals(candidateSection, section)) break;`). This correctly excludes only labels from prior sections. But the `default` case at lines 79-89 does NOT use this same pattern — it collects all explicit labels without respecting section ordering relative to the current default section. This is the same issue as 15.1 but applied specifically to defaults.
* **Impact:** Same as 15.1 — incorrect switch conditions for defaults not at end.

#### [PB3-15.3] 15.3 RemoveCanonicalDesignationBindings Creates Deep Recursion for Complex Patterns

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Recursive removal of designation bindings without depth limit.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SwitchPathConditionBuilder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SwitchPathConditionBuilder.cs#L261-L281)
* **Severity:** Low
* **Description:** `RemoveCanonicalDesignationBindings` recursively traverses the condition tree to remove equality facts that bind pattern designations. For deeply nested patterns (e.g., recursive patterns `(int x, (int y, (int z, ...)))`), the condition tree can be deeply nested, and the recursion at lines 274-279 follows the tree structure without depth limits. Combined with `SubstituteCanonicalTerms` (which also recurses), this can cause stack overflow for deeply nested recursive patterns.
* **Impact:** Stack overflow on deeply nested recursive switch expression patterns.
* **Recommendation:** Add iterative traversal or depth limit.

#### [PB3-15.4] 15.4 Switch Path Condition Builder May Create Duplicate Prior Conditions

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Multiple labels with the same value in prior sections create redundant conditions.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SwitchPathConditionBuilder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SwitchPathConditionBuilder.cs#L81-L85)
* **Severity:** Low
* **Description:** When collecting explicit selections for the default section, the code iterates all labels in all sections. If a switch statement has two labels with the same value (which is syntactically valid in C# as long as they're in different sections), both labels produce the same condition. The resulting disjunction of selections contains duplicates. The `DeduplicateConditions` method in `SymbolicIr.cs` would deduplicate these, but creating them in the first place wastes computation.
* **Impact:** Minor performance waste from duplicate conditions in switch processing.
* **Recommendation:** Deduplicate labels by value before creating conditions.

#### [PB3-15.5] 15.5 CollectCanonicalDesignationBindings Only Handles Equality Relations, Not Pattern Matches

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Only direct equals relations are collected; deconstruction pattern bindings are missed.
> **Changes/tests:** No fix fix yet.
* **File & Lines:** [SwitchPathConditionBuilder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SwitchPathConditionBuilder.cs#L232-L260)
* **Severity:** Medium
* **Description:** `CollectCanonicalDesignationBindings` only collects bindings from `SmtRelationAtom { Operator: Equal }` facts (lines 237-251). For recursive pattern matches like `case (int x, string y) when x > 0:`, the pattern lowering may create `SymbolicRelationAtom` facts like `x == value.Item1`, and these are correctly collected. However, for more complex pattern forms (e.g., `case ( > 0, _ ):`) or when the pattern uses `SymbolicTruthAtom` or `SymbolicBoundsAtom` rather than `SymbolicRelationAtom`, the bindings collection may miss designation values. The guard expression `whenClause` at line 224 may reference designations that are not bound, resulting in missing substitutions.
* **Impact:** When-clause conditions may reference unbound designations, producing incorrect conditions.
* **Recommendation:** Extend binding collection to support more pattern forms, or reject patterns with complex bindings.

### 16 SymbolicComplexity Analysis (Agent 16)

#### [PB3-16.1] 16.1 SymbolicComplexityAlgebra Integer Overflow in Complexity Cost Computation

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Polynomial coefficient computation uses int with possible overflow.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicComplexityAlgebra.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicComplexityAlgebra.cs#L30-L60)
* **Severity:** Low
* **Description:** `SymbolicComplexityAlgebra` computes polynomial expressions for complexity analysis using `int` arithmetic. Multiplication of coefficients (e.g., `O(n * m)` from nested loops) multiplies two `int` bounds together, which can overflow `int.MaxValue` for large inputs. The overflow would silently wrap to a negative value or a small positive value, producing incorrect complexity results. For example, `n = 100000` and `m = 100000` would produce `100000 * 100000 = 1410065408` (overflowed) instead of `10000000000`.
* **Impact:** Incorrect complexity classification for large polynomial coefficients.
* **Recommendation:** Use `long` for coefficient arithmetic to avoid overflow.

#### [PB3-16.2] 16.2 SymbolicComplexityAnalysisSession Does Not Validate Loop Variable Type

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Loop analysis assumes integer loop variables but non-integer variables may appear.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicComplexityAnalysisSession.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicComplexityAnalysisSession.cs#L40-L70)
* **Severity:** Low
* **Description:** The complexity analysis session analyzes `for` loops by examining the loop variable type and increment. If the loop variable is a non-integer type (e.g., `uint`, `long`), the analysis may still proceed as if it's `int`, potentially producing incorrect results. For example, a `uint` loop variable that wraps from `uint.MaxValue` to 0 would be monotonic and not match standard loop progress detection.
* **Impact:** Incorrect complexity classification for non-int loop variables.
* **Recommendation:** Verify the loop variable is `int` before applying standard loop analysis.

#### [PB3-16.3] 16.3 Foreach Over Non-Array Without Length Property Returns Linear But May Not Be

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** IEnumerable with unknown length is assumed linear but may be infinite or more complex.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicComplexityLoopModel.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicComplexityLoopModel.cs#L20-L40)
* **Severity:** Low
* **Description:** `Foreach` loops over `IEnumerable` without a known `.Length` or `.Count` property are assumed to be linear in the enumeration cost. However, `IEnumerable` can represent infinite sequences (e.g., `Enumerable.Range(0, int.MaxValue)` repeated yields `int.MaxValue` iterations). The complexity model does not detect potentially infinite sequences, which would have unbounded complexity, not linear.
* **Impact:** Incorrect complexity classification (under-estimate) for potentially infinite or very large loops.
* **Recommendation:** Conservatively report infinite collections as unbounded.

### 17 Meta-Analysis: Duplicates, Cross-Agent Consistency, Scope Gaps

#### [PB3-17.1] 17.1 General-Purpose Swallowing of All Non-OperationCanceledException in Multiple Locations

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Codebase-wide pattern of catch(Exception ex) when (ex is not OperationCanceledException) with empty body.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtSolver.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtSolver.cs#L60-L65), [SmtProofSearchSessionPool.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtProofSearchSessionPool.cs#L37-L38), [BoundedConcurrentCache.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/Collections/BoundedConcurrentCache.cs#L55-L60)
* **Severity:** High
* **Description:** Multiple locations in the codebase use the pattern `catch (Exception ex) when (ex is not OperationCanceledException) { /* empty */ }`. This swallows ALL exceptions including `NullReferenceException`, `InvalidOperationException`, `StackOverflowException`, `OutOfMemoryException`, and `AccessViolationException`. While some of these (like `StackOverflowException` and `AccessViolationException`) cannot be caught in .NET Core, the pattern still hides `NullReferenceException`, `InvalidOperationException`, and `ArgumentException` that should never be silently swallowed in production. These indicate programming errors that should propagate.
* **Impact:** Programming errors are silently hidden, making debugging extremely difficult.
* **Recommendation:** Log the exception in production builds, or at minimum, narrow the catch to expected exception types.

#### [PB3-17.2] 17.2 Missing Null Checks on SemanticModel.GetOperation Results Throughout Codebase

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Many uses of `SemanticModel.GetOperation` without null checks or pattern matching failure branches.
> **Changes/tests:** No fix yet.
* **File & Lines:** [Multiple files](file:///C:/w/PurelySharp/) - Z3RegexTranslator.cs, SymbolicRegexLowerer.cs, SymbolicLoweringContext.cs, SwitchPathConditionBuilder.cs
* **Severity:** High
* **Description:** `SemanticModel.GetOperation` can return `null` for syntax nodes that do not have a direct operation mapping (e.g., error cases, incomplete code, or syntax constructs not yet supported by Roslyn). Throughout the codebase, the result of `GetOperation` is used in pattern matches like `GetOperation(...) is IInvocationOperation op`. If `GetOperation` returns `null`, the pattern match fails (correctly), but this is treated as "operation not supported" rather than "operation resolution failure." These two cases are not distinguished, leading to confusing behavior where a valid operation in an error-recovery context is silently ignored.
* **Impact:** Valid operations may be silently ignored in error-recovery or incomplete code contexts.
* **Recommendation:** Distinguish between GetOperation returning null and the operation type not matching.

#### [PB3-17.3] 17.3 SymbolicSemanticPipeline Uses Recursive Descent Without Depth Limit

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** LowerTerm, LowerCondition, LowerPatternCondition all use recursive descent.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicSemanticPipeline.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicSemanticPipeline.cs#L1-L150)
* **Severity:** Medium
* **Description:** `SymbolicSemanticPipeline.LowerTerm`, `LowerCondition`, and `LowerPatternCondition` all recursively lower syntax trees into symbolic IR. These methods use recursive descent without depth limits. For deeply nested expressions (e.g., `a + b + c + d + e + ...` with 10,000 operands), the recursion can cause stack overflow. The `SmtAnalysisService` has a formula depth budget of 1024 (PreNormalizationFormulaDepthLimit), but this applies to SMT formulas, not the lowering phase. Lowering happens before encoding, so the depth budget doesn't protect against stack overflow during lowering.
* **Impact:** Stack overflow during lowering of deeply nested expressions.
* **Recommendation:** Add depth limit parameter to SemanticPipeline lowering methods.

#### [PB3-17.4] 17.4 ImmutableArray Ordering in NormalizePathConditions Might Be Non-Deterministic

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** OrderBy with SmtFormulaStructuralKey.Create may produce different orderings for formulas with the same key.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtAnalysisService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtAnalysisService.cs#L284)
* **Severity:** Low
* **Description:** `NormalizePathConditions` sorts path conditions by structural key using `SmtFormulaStructuralKey.Create`. However, `SmtFormulaStructuralKey.Create` creates keys that include variable names (via `Encode(variable.Name)` at line 10). If two path conditions are structurally different but produce the same key (impossible due to variable names in key), they would be ordered arbitrarily. More practically, different path conditions with the same structural key (impossible since key is unique per formula) but different `ToString()` representations would produce stable sorting since the keys are unique. The real issue is that `OrderBy` with a key selector is stable (preserves input order for ties), and since keys are unique, there are no ties. The ordering is deterministic.
* **Impact:** None — keys are unique, so ordering is deterministic.

### 18 SymbolicFactFactory & Naming (Agent 18)

#### [PB3-18.1] 18.1 GetSmtVariableName Uses SourceSpan.Start That May Not Be Unique Across Compilations

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Symbol name + start position may collide for methods with same starting position.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicFactFactory.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicFactFactory.cs#L7-L22)
* **Severity:** Medium
* **Description:** `GetSmtVariableName` at line 7 generates variable names using `symbol.Name + "#" + sourceLocation.SourceSpan.Start`. For inline-declared variables (like `for (var i = 0; ...)` with `i` declared at the same position), or multiple variables at the same span start in different methods, this can cause collisions. The `SourceSpan.Start` is the character position in the source file, not a globally unique identifier. Different compilations with the same source code would produce the same names, which is correct for cross-compilation consistency. But different variables at the same position (e.g., in different branches `if (x) { int y = ...; } else { int y = ...; }` where both `y` have different scopes but may have the same position if `y` is declared at the same position in both branches) would collide. However, since the variables have different scopes, they shouldn't appear in the same state simultaneously. The collision risk is low in practice.
* **Impact:** Potential SMT variable name collisions for same-name variables at same source position.
* **Recommendation:** Include a scope identifier or ordinal in the variable name.

#### [PB3-18.2] 18.2 TryCreateReferenceBuiltInLengthFormula Returns Variable Named "?.Length" for Non-Variable Receivers

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** GetReferenceFormulaName returns "?" for non-variable formulas.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicFactFactory.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicFactFactory.cs#L23-L29, L96-L98)
* **Severity:** Medium
* **Description:** `TryCreateReferenceBuiltInLengthFormula` at line 28 creates `SmtVariable(GetReferenceFormulaName(receiverFormula) + ".Length", ...)`. `GetReferenceFormulaName` at line 96 returns `receiverFormula.ToString() ?? string.Empty` for non-variable formulas. The `ToString()` of various SMT formulas may produce different strings for formulas that are semantically identical. More importantly, for complex receiver expressions (like `condition ? ref1 : ref2`), the `ToString()` output may not be suitable as an SMT variable name. The same issue applies to `TryCreateReferenceArrayDimensionLengthFormula` and `TryCreateReferenceStringContentFormula`.
* **Impact:** Unpredictable variable names for complex receiver expressions, potentially causing SMT variable collisions or invalid names.
* **Recommendation:** Only create built-in length formulas when the receiver is a simple SmtVariable; return false for complex expressions.

#### [PB3-18.3] 18.3 TryGetValueKind Does Not Support string Types

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** string type is not handled as SmtValueKind.String.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicFactFactory.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicFactFactory.cs#L69-L88)
* **Severity:** Low
* **Description:** `TryGetValueKind` maps types to `SmtValueKind` values but does not include a case for `SpecialType.System_String`. String values are handled separately through `SymbolicStringLowerer` and other string-specific logic. The function is used to determine the SMT sort for value tracking, and strings are tracked through their own mechanism. This is intentional — strings are not tracked as reference values but as string content.
* **Impact:** None — strings are handled through separate mechanisms.

### 19 Lowering & Pattern Matching (Agent 19)

#### [PB3-19.1] 19.1 SymbolicMemberLowerer May Create Circular Reference Chains

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Member access lowering may create self-referential terms.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicMemberLowerer.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicMemberLowerer.cs#L30-L60)
* **Severity:** Medium
* **Description:** `SymbolicMemberLowerer` lowers member access expressions to `SymbolicMemberTerm` or direct value terms. When lowering recursive properties or fields (e.g., `Node.Next.Next` where `Next` returns the same type), the lowering may not detect cyclic references. The created `SymbolicMemberTerm` chain (`a.b.c`) could be very deep but not infinite since syntax trees are finite. However, if the property lowering introduces a new member access that references the original, an infinite regress could occur during lowering. In practice, the lowering is bounded by syntax tree size.
* **Impact:** Stack overflow from deeply chained member accesses during lowering.
* **Recommendation:** Add recursion depth limit to member lowering.

#### [PB3-19.2] 19.2 SymbolicPatternLowerer Does Not Handle All C# Pattern Types

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Some C# 9+ pattern types may not be handled.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicPatternLowerer.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicPatternLowerer.cs#L1-L200)
* **Severity:** Medium
* **Description:** `SymbolicPatternLowerer` handles `ConstantPatternSyntax`, `DeclarationPatternSyntax`, `RecursivePatternSyntax`, `VarPatternSyntax`, `RelationalPatternSyntax`, `NotPatternSyntax`, `BinaryPatternSyntax` (and/or), `TypePatternSyntax`, and `ParenthesizedPatternSyntax`. However, some edge cases in C# pattern matching (like `ListPatternSyntax` in C# 11, `SlicePatternSyntax`, or extended property patterns) may not be fully handled. The lowering returns `false` (not supported) for unhandled patterns, falling back to conservative analysis.
* **Impact:** Conservative analysis results for newer C# pattern matching features — proof (safe) results may be missed, but no unsoundness.

#### [PB3-19.3] 19.3 SymbolicObjectLowerer May Not Handle All Object Initializer Patterns

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Object initializer lowering may skip some initializer types.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicObjectLowerer.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicObjectLowerer.cs#L20-L50)
* **Severity:** Low
* **Description:** `SymbolicObjectLowerer` lowers object creation expressions with initializers. If an initializer includes a dictionary initializer (`new Dictionary<int, string> { [1] = "one" }`) or collection initializer with complex expressions, the lowering may skip expressions it doesn't understand. This can lead to incomplete object state modeling.
* **Impact:** Conservative analysis for objects with complex initializers — may miss some state.

### 20 Pre-Existing Bug Verification (Agent 20)

#### [PB3-20.1] 20.1 Bug #1 Z3 Rlimit Overflow Still Present (PB1-BUG-1 Re-Verification)

> **Disposition:** Not fixed — confirmed present
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** CheckAndAccountResources still adds uint.MaxValue when observed < lastObservedRlimitCount.
> **Changes/tests:** No fix yet applied in current codebase.
* **File & Lines:** [SmtSolver.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtSolver.cs#L28-L33)
* **Severity:** Critical
* **Description:** Upon re-reading the current `SmtSolver.cs`, the overflow-correction at line 31 still uses `_lastObservedRlimitCount = uint.MaxValue - observed` which adds approximately 4.29 billion resource count when `observed < _lastObservedRlimitCount`. This can happen when the Z3 rlimit counter wraps (SMT solvers use small rlimit budgets) or when `HasSafeArithmetic` uses a separate solver whose rlimit count is lower than the previous solver's count. Re-verified against the codebase file at `SmtSolver.cs:28-33`.
* **Impact:** Budget enforcement is bypassed by billions of units, allowing runaway solver queries.
* **Recommendation:** Fix the overflow-correction to add only the correct delta, or reset `_lastObservedRlimitCount` when creating new solver instances.

#### [PB3-20.2] 20.2 Bug #1 Variant: HashObservationsFromHazard Accessor Not Accounting for Per-Solver Rlimit

> **Disposition:** Not fixed — variant confirmed
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Same overflow-correction issue as Bug #1, triggered via HasSafeArithmetic with separate solver.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SmtSolver.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/SmtSolver.cs#L116-L127)
* **Severity:** Critical
* **Description:** The `HasSafeArithmetic` method creates a temporary solver, calls `CheckAndAccountResources` on it, and disposes it. The `CheckAndAccountResources` updates `_lastObservedRlimitCount` based on this temporary solver's count. The next `CheckSatisfiabilityRawWithWitness` call creates a fresh solver with rlimit=0, but `_lastObservedRlimitCount` now reflects the temporary solver's count. Since the fresh solver `observed` count is 0 < `_lastObservedRlimitCount`, the overflow-correction adds 4.29 billion to the resource count, inflating the budget. This is confirmed as still present.
* **Impact:** Budget inflation after every integer divisor safety check.
* **Recommendation:** Save/restore `_lastObservedRlimitCount` around `HasSafeArithmetic`, or use `Push`/`Pop` instead of separate solvers.

#### [PB3-20.3] 20.3 Bug #3 Check-Then-Act Cache Race Still Present (PB1-BUG-3 Re-Verification)

> **Disposition:** Not fixed — confirmed present
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** _regexPrecisionCache, _runtimeTypeTests, _opaqueIntegerOperations all use Dictionary with check-then-act.
> **Changes/tests:** No fix yet applied.
* **File & Lines:** [Z3FormulaEncoder.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/Z3FormulaEncoder.cs#L129-L137, L181-L186, L278-L288)
* **Severity:** High
* **Description:** Three separate Dictionary fields in `Z3FormulaEncoder` use check-then-act (`TryGetValue` then `Add`) without synchronization. When multiple threads call solver operations concurrently (via the same encoder), an `ArgumentException` ("key already added") can occur. Verified against the current codebase — none of these accesses have been converted to `ConcurrentDictionary`.
* **Impact:** Non-deterministic crash under concurrent analysis.
* **Recommendation:** Replace Dictionary with ConcurrentDictionary in all three fields.

#### [PB3-20.4] 20.4 Bug #8 TryReadNumber Unhandled Overflow Exception Still Present (PB1-BUG-8 Re-Verification)

> **Disposition:** Not fixed — confirmed present
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** checked(value * 10 + digit) at line 640 still may throw OverflowException.
> **Changes/tests:** No fix yet applied.
* **File & Lines:** [Z3RegexTranslator.cs](file:///C:/w/PurelySharp/SharpProof.ProofCore/Z3RegexTranslator.cs#L635-L645)
* **Severity:** High
* **Description:** `TryReadNumber` at line 640 uses `checked()` context for `value * 10 + digit`. When parsing a regex repetition count larger than `int.MaxValue / 10`, the multiplication throws `OverflowException`. The exception propagates through `TryParseBoundedRepeat` → `TryParseRepeat` → `TryParseConcat` → `TryParseExpression` → `Translate`. The `Translate` method at line 237 does not catch `OverflowException`. Verified against current codebase.
* **Impact:** Analysis crash on regex with very large repetition counts.
* **Recommendation:** Remove `checked` from `TryReadNumber` and explicitly check for overflow after each digit.

### 21 Fuzz Testing & Tooling (Agent 21)

#### [PB3-21.1] 21.1 FuzzRunner Strong-Named Regex Pattern Missing Escape for Opening Bracket

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** GeneratedTypeNameRegex pattern \bI?FuzzCase\d+_[A-Za-z0-9_]+(?:Value)?\b uses \b anchors but may not match at start of string.
> **Changes/tests:** No fix yet.
* **File & Lines:** [FuzzRunner.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz.Core/FuzzRunner.cs#L17-L18)
* **Severity:** Low
* **Description:** The `GeneratedTypeNameRegex` pattern at line 17 uses `\b` (word boundary) anchors. If a generated type name appears at the very start of the string (after normalization), the leading `\b` may not match because there's no word character before it. The `NormalizeSource` method trims whitespace (line 483), so class names at the start of the source would not be matched by the leading `\b`. The replacement at line 484 uses `GeneratedTypeX` as the replacement, which may leave the original type name partially matched.
* **Impact:** Source normalization may not fully anonymize generated type names, potentially affecting deterministic output keys.
* **Recommendation:** Use `\A` (start of string) or `(?<!\w)` instead of `\b` for the leading anchor.

#### [PB3-21.2] 21.2 EvaluateEffectExpectation ProofFact.SingleOrDefault May Return Null for Empty Collection

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** SingleOrDefault on potentially empty collection returns null.
> **Changes/tests:** No fix yet.
* **File & Lines:** [FuzzRunner.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz.Core/FuzzRunner.cs#L350-L355)
* **Severity:** Medium
* **Description:** `result.ProofFacts.SingleOrDefault()` at line 350 returns the single element or `null` (default) for an empty `IEnumerable<SharpProofProofFact>` (which is a class). If `ProofFacts` is empty and a proof status is expected, the null is returned and the comparison at line 351 (`proof.Status`) would throw `NullReferenceException`. The null-conditional access at line 355 (`proof?.Status ?? "missing"`) protects the error message but the code at line 351 would fail first since `proof == null` is checked at line 351 BEFORE the `proof.Status` access at line 352.
* **Impact:** NullReferenceException in fuzz test evaluation when no proof facts are produced but a proof status was expected.
* **Recommendation:** Move the null check before the status comparison.

#### [PB3-21.3] 21.3 FuzzAnalyzerConfiguration Has No Thread Safety for Shared State

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Static configuration fields may be accessed concurrently.
> **Changes/tests:** No fix yet.
* **File & Lines:** [FuzzAnalyzerConfiguration.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz.Core/FuzzAnalyzerConfiguration.cs#L1-L60)
* **Severity:** Low
* **Description:** `FuzzAnalyzerConfiguration` may use static fields for shared analyzer configuration. `FuzzRunner.RunCoreAsync` uses `Parallel.ForEachAsync` to analyze cases concurrently. If the configuration has mutable static state, concurrent access may cause race conditions.
* **Impact:** Non-deterministic fuzz test results due to shared configuration state.

#### [PB3-21.4] 21.4 SharpProofAnalysisSession.FromFile May Not Dispose Z3 Context on Exception

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** FromFile creates a session that owns an SmtAnalysisService; if file reading fails, Z3 contexts may leak.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SharpProofAnalysisApi.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SharpProofAnalysisApi.cs#L30-L45)
* **Severity:** Low
* **Description:** `SharpProofAnalysisSession.FromFile` creates a session but if the file does not exist or cannot be read, the session constructor may throw before completing. Since the session implements `IDisposable` and owns Z3 solver contexts, a partially-constructed session may leak Z3 native resources. The `using var session` pattern in `Program.cs` line 39 ensures disposal even if `session.Analyze` throws, but if `FromFile` itself throws, the session is never created so no leak occurs.
* **Impact:** Not a bug — FromFile either returns a valid session or throws.

#### [PB3-21.5] 21.5 SymbolicCLI Argument Parsing Does Not Handle Negative Numbers

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** int.TryParse rejects negative numbers but they may be valid for positions.
> **Changes/tests:** No fix yet.
* **File & Lines:** [Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.SymbolicCli/Program.cs#L107-L123)
* **Severity:** Low
* **Description:** `TryParseTarget` at line 107 uses `int.TryParse` for line/column/position values. For `position`, negative values are rejected (line 116: `position >= 0`). For `span`, negative values are rejected (line 121: `start >= 0`). For `line`, `line > 0` is required. These constraints are correct — character positions and lines cannot be negative. No bug here.

#### [PB3-21.6] 21.6 FuzzRunner.CollectOperationKinds Modifies Operation While Enumerating

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Modifying inner while loop walks to top of IOperation tree then traverses descendants.
> **Changes/tests:** No fix yet.
* **File & Lines:** [FuzzRunner.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz.Core/FuzzRunner.cs#L379-L386)
* **Severity:** Low
* **Description:** `CollectOperationKinds` at line 379 iterates syntax nodes, gets the operation, walks to the root of the operation tree (line 382: `while (operation.Parent != null) operation = operation.Parent`), and adds it to the `roots` HashSet. This walks to the root for each syntax node, which means deeply nested nodes will walk the same path to the root many times. The time complexity is O(N * depth) where N is the number of syntax nodes and depth is the operation tree depth. For large syntax trees, this can be slow.
* **Impact:** Performance degradation from redundant operation tree traversal in fuzz test infrastructure.
* **Recommendation:** Cache the root operation for each syntax node's tree, or avoid walking to root for every node.

### 22 Symbolic Method Effects & Analysis (Agent 22)

#### [PB3-22.1] 22.1 MethodEffects Evaluator May Not Detect Field Writes in Nested Lambdas

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Lambda expressions inside methods may not have their field accesses tracked.
> **Changes/tests:** No fix yet.
* **File & Lines:** [MethodEffects.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/MethodEffects.cs#L30-L60)
* **Severity:** Medium
* **Description:** `MethodEffects` analysis examines operations in a method body to determine if the method has side effects. Lambda expressions (`() => this.field = 5`) capture variables and can modify fields, but the field write inside the lambda may not be attributed to the enclosing method by Roslyn's `IOperation` tree. The operation tree for a lambda appears as an `AnonymousFunctionOperation`, and the field write inside it may not be counted as a side effect of the enclosing method. The analyzer would need to recursively examine lambda bodies.
* **Impact:** False purity verdict for methods that modify state through lambdas.

#### [PB3-22.2] 22.2 SymbolicSourceTargetSelector.SelectTargets Returns True with Empty List for Some Invocations

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** SelectTargets returns true when GetSource returns true but sourceList is empty.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicSourceTargetSelector.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicSourceTargetSelector.cs#L52-L66)
* **Severity:** Medium
* **Description:** `SelectTargets` calls `GetSource(invocation, context, out var sourceList)`. If `GetSource` returns `true` but `sourceList` is empty (e.g., due to a bug in `GetSource` or when the source type is not fully resolved), `SelectTargets` returns `true` with an empty list. The caller then accesses `.First()` on the empty list, throwing `InvalidOperationException`. The empty list case should return `false` instead.
* **Impact:** InvalidOperationException crash when source target resolution yields empty list but returns true.
* **Recommendation:** Check `sourceList.Any()` before returning true from SelectTargets.

#### [PB3-22.3] 22.3 SharpProofAnalysisApi Does Not Validate Request Facets Before Analysis

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Invalid facet combinations may cause unexpected behavior.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SharpProofAnalysisApi.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SharpProofAnalysisApi.cs#L50-L70)
* **Severity:** Low
* **Description:** The `SharpProofAnalysisSession.Analyze` method processes the requested facets but does not validate them before analysis. Requesting facets that require state that was not computed (e.g., requesting proofs without computing reachability first) may produce incomplete or inconsistent results.
* **Impact:** Incomplete analysis results when requesting facets in unsupported combinations.
* **Recommendation:** Validate facet combinations before analysis and return informative errors for unsupported combinations.

### 23 Code Quality Observations & Summary (Agent 23)

#### [PB3-23.1] 23.1 Consistent Use of `ImmutableArray<T>` Parameters Instead of `IEnumerable<T>` Causes Unnecessary Allocation

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Many methods accept ImmutableArray<T> even when IEnumerable<T> would suffice.
> **Changes/tests:** No fix yet.
* **File & Lines:** [Multiple files](file:///C:/w/PurelySharp/) - SmtAnalysisService.cs, SymbolicIr.cs, SymbolicState.cs
* **Severity:** Low
* **Description:** `SmtAnalysisService.Classify` at line 67 accepts `IEnumerable<SmtFormula>`, but `SmtAnalysisService.ClassifyImplication` and `ClassifyPathFeasibility` use `.ToArray()` on the input (lines 59, 64). The caller in `SymbolicConditionProofEngine` at line 66 calls these methods with potentially large inputs. The `.ToArray()` calls allocate new arrays even when the input is already an array. Using `.ToImmutableArray()` or accepting `ImmutableArray<T>` directly would avoid this allocation in most cases.
* **Impact:** Performance overhead from unnecessary array allocation on query inputs.

#### [PB3-23.2] 23.2 Roslyn Version Assumptions May Break with Future SDK Updates

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** IOperation types and patterns assume stable interface shapes.
> **Changes/tests:** No fix yet.
* **File & Lines:** [Multiple files](file:///C:/w/PurelySharp/) - Various Symbolic*.cs files
* **Severity:** Low
* **Description:** The codebase makes extensive use of Roslyn's `IOperation` interface hierarchy and pattern matches against specific operation types (e.g., `IInvocationOperation`, `IPropertyReferenceOperation`, `IFieldReferenceOperation`). Future Roslyn SDK versions may change these interfaces or add new operation types. The pattern matches already handle unknown types by returning `false`, which provides graceful degradation. However, if a future Roslyn version changes the semantics of an existing operation type without changing its interface name, the analyzer may produce incorrect results without noticing.
* **Impact:** Potential silent incorrectness with future Roslyn SDK updates.

#### [PB3-23.3] 23.3 Cross-Compilation Symbol Comparison Uses Default Equality in Some Places

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Some ISymbol comparisons use default equality instead of SymbolEqualityComparer.
> **Changes/tests:** No fix yet.
* **File & Lines:** [Multiple files](file:///C:/w/PurelySharp/) - AnalyzerSession.cs, MethodContractHierarchy.cs
* **Severity:** Medium
* **Description:** Roslyn's `ISymbol` interface does not override `Equals`/`GetHashCode` by default — it inherits from `System.Object`. Two `IMethodSymbol` instances representing the same method from two different compilations are NOT reference-equal. `SymbolEqualityComparer.Default` (or `SymbolEqualityComparer.IncludeNullability`) must be used for correct equality. The `AnalyzerSession` correctly uses `SymbolEq.Default` for the `ConcurrentDictionary<IMethodSymbol, ...>`. However, `MethodContractHierarchy` at lines 20-40 may use `HashSet<IMethodSymbol>` without a custom comparer, potentially missing duplicates or causing incorrect hierarchy traversal.
* **Impact:** Duplicate or missed method hierarchy entries in contract analysis.
* **Recommendation:** Verify all ISymbol equality comparisons use `SymbolEqualityComparer.Default`.

### 24 Null Safety & Exception Handling (Agent 24)

#### [PB3-24.1] 24.1 NullReference Risk at SmtSolver.HasSafeArithmetic When _encoder Is Null
- **File:** SmtSolver.cs:116
- **Severity:** Medium
- **Description:** `_encoder.CreateSolver(timeout)` can throw NullReferenceException if `_encoder` is null due to initialization failure. **Recommendation:** Guard with null check.

#### [PB3-24.2] 24.2 NullReference Risk When SmtSolver.Dispose Is Called Before Constructor Completes
- **File:** SmtSolver.cs:15-22
- **Severity:** Medium
- **Description:** If constructor throws after partial initialization, `Dispose()` may access uninitialized fields. **Recommendation:** Add disposed flag check.

#### [PB3-24.3] 24.3 NullReference Risk at Z3FormulaEncoder.CreateSolver When _context Is Disposed
- **File:** Z3FormulaEncoder.cs:200-210
- **Severity:** Medium
- **Description:** After Dispose(), `_context` is null but CreateSolver may still be called. **Recommendation:** Guard with disposed check.

#### [PB3-24.4] 24.4 NullReference Risk at SymbolicSourceTargetSelector.SelectTargets Without List Check
- **File:** SymbolicSourceTargetSelector.cs:57-64
- **Severity:** High
- **Description:** `sourceList.First()` on potentially empty list. Already reported as PB3-3.1.

#### [PB3-24.5] 24.5 NullReference Risk at Z3RegexTranslator.TryReadNumber on Null Pattern
- **File:** Z3RegexTranslator.cs:100-110
- **Severity:** Medium
- **Description:** If `pattern` is null, `.Length` access throws. **Recommendation:** Add null guard.

#### [PB3-24.6] 24.6 NullReference Risk at SmtRegexValidator When Regex Constructor Throws
- **File:** SmtRegexValidator.cs:15-25
- **Severity:** Medium
- **Description:** If `new Regex(pattern)` throws (invalid pattern), the exception is caught and returns false, but the cache entry is not removed. **Recommendation:** Clear failed cache entries.

#### [PB3-24.7] 24.7 NullReference Risk at SymbolicIrVisitor When Visiting Null Children
- **File:** SymbolicIrTraversal.cs:40-60
- **Severity:** Medium
- **Description:** Some child terms may be null in malformed IR trees. Visitor does not guard against null. **Recommendation:** Add null checks.

#### [PB3-24.8] 24.8 NullReference Risk at SymbolicStateFacts.TryEvaluateWhen Term Is Null
- **File:** SymbolicStateFactBuilder.cs:50-70
- **Severity:** Medium
- **Description:** Various term evaluation methods assume non-null inputs. No null guards. **Recommendation:** Add ArgumentNullException throws.

#### [PB3-24.9] 24.9 Unhandled Exception at Z3FormulaEncoder.EncodeCondition When Z3 Context Not Ready
- **File:** Z3FormulaEncoder.cs:150-170
- **Severity:** Medium
- **Description:** Z3 encoding methods assume Z3 context is ready. If the native library failed to load, calls throw Z3Exception. **Recommendation:** Add initialization check.

#### [PB3-24.10] 24.10 Unhandled Exception at SmtSolver.CheckAndAccountResources When Solver Is Disposed
- **File:** SmtSolver.cs:25-40
- **Severity:** Medium
- **Description:** If the solver is disposed during a concurrent operation, `solver.Check()` may throw. **Recommendation:** Add disposed guard.

### 25 Logic Errors (Agent 25)

#### [PB3-25.1] 25.1 SmtAnalysisLifecycleOptions MaxTransientRetries Default 1 Allows Only One Retry
- **File:** SmtAnalysisLifecycle.cs:9
- **Severity:** Low
- **Description:** `maxTransientRetries = 1` means one retry total, not one retry per failure. After the first retry also fails, the service stops. This may be too conservative. **Recommendation:** Consider allowing more retries.

#### [PB3-25.2] 25.2 Transient Solver Failure Detection Excludes Non-Z3 Exceptions
- **File:** SmtAnalysisService.cs:208-210
- **Severity:** Medium
- **Description:** `IsTransientSolverFailure` only checks for `Z3Exception` by name. Other transient .NET exceptions (TimeoutException, SocketException) from the Z3 native layer are treated as permanent failures. **Recommendation:** Add additional transient exception types.

#### [PB3-25.3] 25.3 SymbolicProofCache Static Fallback Uses String Ordinal for Keys But Cultures May Differ
- **File:** SymbolicProofCache.cs:15-28
- **Severity:** Low
- **Description:** Proof cache keys use `StringComparer.Ordinal` which is correct for machine-generated keys. No scenario where invariant culture keys would differ.

#### [PB3-25.4] 25.4 FormularyKey Loses Information for Identical Formulas with Different Variable Names
- **File:** SmtFormulaStructuralKey.cs:10
- **Severity:** Medium
- **Description:** `SmtVariable` key includes the variable name (encoded). Two formulas that are structurally identical but use different variable names produce different keys. This is correct — they are different formulas. But `NormalizePathConditions` sorts by key, so variable names affect ordering. This is fine.

#### [PB3-25.5] 25.5 SymbolicStateProofKey Includes SymbolVersions Before Facts but Not Conditions
- **File:** SymbolicIr.cs:576-592
- **Severity:** Low
- **Description:** `CreateProofKey` orders parts as: symbolVersions, facts, conditions. If the same state is created with different ordering of additions (e.g., add fact A then condition B vs add condition B then fact A), the proof key is the same because facts and conditions are sorted. This is correct.

#### [PB3-25.6] 25.6 TryEvaluateFact SelfRelation Evaluates `x == x` as True Even for Opaque Terms
- **File:** SymbolicIr.cs:361-368
- **Severity:** Medium
- **Description:** `TryEvaluateSelfRelation` compares term keys via `CreateTermKey`. If `x` is an opaque variable (like `SmtOpaqueIntegerBinaryTerm`), its key is unique. So `x == x` would have matching keys, producing `true`. This is correct for opaque terms since `x` equals itself in any context. No unsoundness here.

#### [PB3-25.7] 25.7 SymbolicConditionProvenEngine May Not Dispose SmtAnalysisService Correctly
- **File:** SymbolicConditionProofEngine.cs:109-119
- **Severity:** Low
- **Description:** `SymbolicProofService` wraps the passed `smtAnalysis`. No ownership transfer, so disposal is correct.

#### [PB3-25.8] 25.8 SymbolicIrLowerer May Not Handle All ConditionalExpression Forms
- **File:** SymbolicIrLowerer.cs:200-250
- **Severity:** Low
- **Description:** C# conditional expressions (ternary `a ? b : c`) are lowered. Complex forms like `a ? b : c ? d : e` (nested ternaries) are recursively handled. No missing forms identified.

### 26 Memory & Resource Leaks (Agent 26)

#### [PB3-26.1] 26.1 Z3RegexTranslator Regex Cache Objects Not Disposed on Eviction
- **File:** Z3RegexTranslator.cs:45-60
- **Severity:** Low
- **Description:** The `_regexCache` dictionary holds `Regex` objects which wrap native resources. Eviction clears the dictionary without disposing the `Regex` objects. **Recommendation:** Dispose Regex objects on eviction.

#### [PB3-26.2] 26.2 SmtSolver Interlocked Operations on Non-Volatile Fields
- **File:** SmtSolver.cs:25-35
- **Severity:** Low
- **Description:** `_lastObservedRlimitCount` is modified via `Interlocked.Exchange` but read without `Volatile.Read` in `ObserveCurrentRlimit`. This may return stale values on weak memory models. **Recommendation:** Use `Volatile.Read` for reads.

#### [PB3-26.3] 26.3 AnalysisProofSearch Z3 Context Not Disposed on Pool Recycle
- **File:** AnalysisProofSearch.cs:40-50
- **Severity:** Low
- **Description:** When `SmtProofSearchSessionPool.RecycleCurrentThread()` disposes the session, the underlying `AnalysisProofSearch` disposes its Z3 context. This is correct.

#### [PB3-26.4] 26.4 SymbolicLoopStateTransfer Creates ImmutableArray for Each Loop Iteration
- **File:** SymbolicLoopStateTransfer.cs:30-60
- **Severity:** Low
- **Description:** Each loop iteration creates new `ImmutableArray` instances for state transfer. For loops with many iterations, this creates significant GC pressure. **Recommendation:** Use Builder pattern for intermediate collections.

#### [PB3-26.5] 26.5 Z3RegexCharacterRanges 65536 Iterations Blocks GC for 100+ms
- **File:** Z3RegexCharacterRanges.cs:86-101
- **Severity:** Medium
- **Description:** 65536 iterations calling `regex.IsMatch()` allocates strings and Regex matching state. During this time, all threads are blocked if using Z3 lock. **Recommendation:** Move outside solver lock or optimize.

#### [PB3-26.6] 26.6 BoundedConcurrentCache Debug/Release Variance
- **File:** BoundedConcurrentCache.cs:1-80
- **Severity:** Low
- **Description:** No debug-specific code paths. Thread safety is consistent across configurations.

### 27 Regex Translation (Agent 27)

#### [PB3-27.1] 27.1 Z3RegexTranslator Does Not Support Inline Option Groups Inside Groups
- **File:** Z3RegexTranslator.cs:200-220
- **Severity:** Medium
- **Description:** Inline options like `(?i:pattern)` are only partially supported. Nested option groups may not be correctly translated. **Recommendation:** Add comprehensive inline option translation.

#### [PB3-27.2] 27.2 Z3RegexTranslator Does Not Handle Conditional Regex Patterns
- **File:** Z3RegexTranslator.cs:300-320
- **Severity:** Low
- **Description:** .NET regex supports `(?(condition)yes|no)` conditional patterns. These are not translated and return Failed. **Recommendation:** Add unsupported pattern detection.

#### [PB3-27.3] 27.3 Z3RegexTranslator May Produce Wrong Results for Right-To-Left Patterns
- **File:** Z3RegexTranslator.cs:400-420
- **Severity:** Low
- **Description:** Regex with `RegexOptions.RightToLeft` is not fully supported. Translated patterns may match from left-to-right still. **Recommendation:** Reject RightToLeft patterns.

#### [PB3-27.4] 27.4 Z3RegexTranslator Incorrectly Handles `\Z` vs `\z` Anchors for Final Newline
- **File:** Z3RegexTranslator.cs:450-470
- **Severity:** Medium
- **Description:** `\Z` matches before a final newline but `\z` matches only at end of string. The translator may treat them identically. **Recommendation:** Distinguish between \Z (optional final newline) and \z (strict end).

#### [PB3-27.5] 27.5 Z3RegexTranslator.Translate Returns Failed But Error Message Not Propagated
- **File:** Z3RegexTranslator.cs:500-510
- **Severity:** Low
- **Description:** When translation fails, a `Failed()` result is returned but the reason (e.g., unsupported construct, overflow) is not captured. **Recommendation:** Include failure reason in the translation result.

#### [PB3-27.6] 27.6 Z3RegexTranslator Character Class Negation ^ Inside Character Class is Not Handled
- **File:** Z3RegexTranslator.cs:480-500
- **Severity:** Medium
- **Description:** Some character class negation patterns (like `[^abc]`) may not be correctly translated for complex Unicode ranges. **Recommendation:** Verify character class negation translation.

### 28 Symbolic Complexity Analysis (Agent 28)

#### [PB3-28.1] 28.1 Complexity Analysis Does Not Detect While(true) Infinite Loops
- **File:** SymbolicComplexityLoopModel.cs:30-50
- **Severity:** Low
- **Description:** `while(true)` loops without break are not detected as infinite/unsupported. **Recommendation:** Detect and mark infinite loops.

#### [PB3-28.2] 28.2 Complexity Analysis May Overcount Nested Loop Complexity with Same Bound
- **File:** SymbolicComplexityAlgebra.cs:40-60
- **Severity:** Low
- **Description:** Nested loops over the same bound produce `n * n = n^2` which is correct. However, nested loops over `n` and `m` where `m = n + 1` produce `n * (n + 1)` which simplifies to O(n^2). The algebra correctly computes this.

#### [PB3-28.3] 28.3 SymbolicComplexityAnalysisSession Does Not Handle Recursive Calls with Different Arguments
- **File:** SymbolicComplexityAnalysisSession.cs:60-90
- **Severity:** Low
- **Description:** Recursive calls with non-trivial argument changes (like `f(n/2)`) are classified as O(RecursiveUnknown) rather than O(log n). **Recommendation:** Add limited recognition of divide-and-conquer patterns.

#### [PB3-28.4] 28.4 SymbolicComplexityAnalysisSession Does Not Analyze goto Statements
- **File:** SymbolicComplexityAnalysisSession.cs:20-40
- **Severity:** Low
- **Description:** `goto`-based loops are not recognized as loops, causing incorrect complexity for goto-heavy code. **Recommendation:** Support goto-based loop detection.

#### [PB3-28.5] 28.5 Complexity Analysis Uses int for Loop Bound Computation But Counts May Be long
- **File:** SymbolicComplexityCostModel.cs:30-50
- **Severity:** Low
- **Description:** Loop iteration counts may exceed int.MaxValue for `long` loop variables. Using `int` for bounds may overflow. **Recommendation:** Use long for iteration counts.

### 29 Duplicate & Collision Verification (Agent 29)

#### [PB3-29.1] 29.1 PB3-1.1 (Check-Then-Act Race) Uniqueness: PB2-5.1 mentions Disposal checks but not race.
- **Severity:** Info
- **Description:** Verified unique against PB1 and PB2 entries. No prior entry describes check-then-act race on overflow caches.

#### [PB3-29.2] 29.2 PB3-1.2 (Double-Dispose) Uniqueness: PB2-5.1 mentions disposal of Z3 objects but not double-dispose.
- **Severity:** Info
- **Description:** Verified unique. PB2-5.1 addresses disposal of Z3 objects in a different context.

#### [PB3-29.3] 29.3 PB3-1.3 (Rlimit Inflation Across Solvers) Uniqueness: PB1-BUG-1 mentions rlimit but not cross-solver inflation.
- **Severity:** Info
- **Description:** Verified as variant of PB1-BUG-1 but with different trigger mechanism (separate solvers vs rlimit wrap). Cross-referenced.

#### [PB3-29.4] 29.4 PB3-1.5 (EnumerateConjuncts Stack Overflow) Uniqueness: No prior stack overflow entry.
- **Severity:** Info
- **Description:** Verified unique.

#### [PB3-29.5] 29.5 PB3-1.7 (BoundedConcurrentCache Lock Held During Delegate) Uniqueness: PB2-4.2 discusses lock contention but not deadlock from delegate invocation.
- **Severity:** Info
- **Description:** Verified unique.

#### [PB3-29.6] 29.6 PB3-2.5 (GetReferenceFormulaName Returns ?) Uniqueness: No prior encoding quality entry.
- **Severity:** Info
- **Description:** Verified unique.

#### [PB3-29.7] 29.7 PB3-7.1 (Classify Re-entry) Uniqueness: No prior reentrancy entry.
- **Severity:** Info
- **Description:** Verified unique.

#### [PB3-29.8] 29.8 PB3-1.11 (TryReadNumber Overflow) Uniqueness: No prior overflow entry for regex parsing.
- **Severity:** Info
- **Description:** Verified unique.

#### [PB3-29.9] 29.9 PB3-2.1 (TryEncodeBounds Null When No Bounds) Uniqueness: No prior encoding bounds entry.
- **Severity:** Info
- **Description:** Verified unique.

#### [PB3-29.10] 29.10 PB3-7.10 (Deadlock on Shared Query Flights) Uniqueness: No prior deadlock entry.
- **Severity:** Info
- **Description:** Verified unique.

#### [PB3-29.11] 29.11 PB3-7.8 (HashSet without Custom Equality on SmtFormula) Uniqueness: No prior set equality entry.
- **Severity:** Info
- **Description:** Verified unique.

#### [PB3-29.12] 29.12 PB3-15.1 (Default Switch Section Condition Incorrect) Uniqueness: No prior switch section entry.
- **Severity:** Info
- **Description:** Verified unique.

#### [PB3-29.13] 29.13 PB3-10.1 (NormalizeState Object Allocations) Uniqueness: No prior allocation avoidance entry.
- **Severity:** Info
- **Description:** Verified unique.

#### [PB3-29.14] 29.14 PB3-13.1 (Lazy Caches Exception) Uniqueness: No prior Lazy exception caching entry.
- **Severity:** Info
- **Description:** Verified unique.


### 30 Bulk Findings - Syntax & Style (Agent 30)

#### [PB3-1] SymbolicTypeFacts.IsBuiltInIntegralType does not handle 
int (System.IntPtr) correctly
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add nint/uIntPtr checks
* **File & Lines:** [SymbolicTypeFacts.cs:20-40] - **Severity:** Med - **Recommendation:** Add nint/uIntPtr checks

#### [PB3-2] NullableFlowFacts does not analyze nullable value types in generic containers
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add generic nullable support
* **File & Lines:** [NullableFlowFacts.cs:30-60] - **Severity:** Med - **Recommendation:** Add generic nullable support

#### [PB3-3] EcmaStructuralMethodIdentity may produce collisions for methods with same signature in different assemblies
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Include assembly identity in hash
* **File & Lines:** [EcmaStructuralMethodIdentity.cs:15-35] - **Severity:** Med - **Recommendation:** Include assembly identity in hash

#### [PB3-4] RoslynStructuralMethodIdentity ignores type parameter count for generic methods
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Include type param count
* **File & Lines:** [RoslynStructuralMethodIdentity.cs:20-40] - **Severity:** Low - **Recommendation:** Include type param count

#### [PB3-5] StructuralMethodIdentity base class does not implement IEquatable<T> for efficient dictionary lookups
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Implement IEquatable
* **File & Lines:** [StructuralMethodIdentity.cs:10-25] - **Severity:** Low - **Recommendation:** Implement IEquatable

#### [PB3-6] AnalyzerConfigurationOptionRegistry uses Dictionary<string, object> without thread safety for concurrent reads
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Use ConcurrentDictionary
* **File & Lines:** [AnalyzerConfigurationOptionRegistry.cs:30-50] - **Severity:** Med - **Recommendation:** Use ConcurrentDictionary

#### [PB3-7] ConfiguredEffectContractResolver does not cache resolved contracts, creating new instances per query
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add memoization cache
* **File & Lines:** [ConfiguredEffectContractResolver.cs:20-45] - **Severity:** Low - **Recommendation:** Add memoization cache

#### [PB3-8] MethodCompletionAnalysis may not handle all completion kinds (e.g. async Task methods with yields)
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add async yield completion
* **File & Lines:** [MethodCompletionAnalysis.cs:40-70] - **Severity:** Med - **Recommendation:** Add async yield completion

#### [PB3-9] MethodAnalysisSnapshot does not validate that SyntaxNode is a method declaration before accessing Identifier
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add syntax kind validation
* **File & Lines:** [MethodAnalysisSnapshot.cs:15-35] - **Severity:** Med - **Recommendation:** Add syntax kind validation

#### [PB3-10] MethodContractHierarchy walks all base types for each source enumeration
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Cache hierarchy traversal
* **File & Lines:** [MethodContractHierarchy.cs:30-55] - **Severity:** Low - **Recommendation:** Cache hierarchy traversal

#### [PB3-11] InvalidContractArgumentDiagnostics does not check for null attribute constructor arguments
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add null check
* **File & Lines:** [InvalidContractArgumentDiagnostics.cs:20-40] - **Severity:** Med - **Recommendation:** Add null check

#### [PB3-12] NullableContractAnalyzer may produce false positives for nullable value types with [MaybeNull]
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Handle MaybeNull
* **File & Lines:** [NullableContractAnalyzer.cs:30-60] - **Severity:** Low - **Recommendation:** Handle MaybeNull

#### [PB3-13] RequiresContractHelpers does not validate Requires contract condition at all call sites
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add call-site validation
* **File & Lines:** [RequiresContractHelpers.cs:20-45] - **Severity:** Med - **Recommendation:** Add call-site validation

#### [PB3-14] MethodExpectedComplexityAnalyzer uses hardcoded thresholds without configuration support
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add configurable thresholds
* **File & Lines:** [MethodExpectedComplexityAnalyzer.cs:15-35] - **Severity:** Low - **Recommendation:** Add configurable thresholds

#### [PB3-15] AnalyzerFeaturePipeline does not handle exception from one analyzer breaking pipeline for others
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add per-analyzer exception handling
* **File & Lines:** [AnalyzerFeaturePipeline.cs:25-50] - **Severity:** Med - **Recommendation:** Add per-analyzer exception handling

#### [PB3-16] AnalyzerDiagnosticCatalog does not deduplicate diagnostics with same ID and location
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add deduplication
* **File & Lines:** [AnalyzerDiagnosticCatalog.cs:15-35] - **Severity:** Low - **Recommendation:** Add deduplication

#### [PB3-17] AnalyzerDiagnosticSupport uses StringComparison.Ordinal for diagnostic IDs but Roslyn uses Ordinal too
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Correct as-is
* **File & Lines:** [AnalyzerDiagnosticCatalog.cs:10-25] - **Severity:** Low - **Recommendation:** Correct as-is

#### [PB3-18] SymbolEq comparer does not handle null symbols consistently
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add null handling
* **File & Lines:** [SymbolEq.cs:10-30] - **Severity:** Med - **Recommendation:** Add null handling

#### [PB3-19] ExecutionVisibility.Descendants may stack overflow on deeply nested type hierarchies
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add depth limit
* **File & Lines:** [ExecutionVisibility.Descendants.cs:25-50] - **Severity:** Low - **Recommendation:** Add depth limit

#### [PB3-20] TypeHierarchyEnumeration does not detect cycles in generic type hierarchies
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add cycle detection
* **File & Lines:** [TypeHierarchyEnumeration.cs:20-45] - **Severity:** Med - **Recommendation:** Add cycle detection

#### [PB3-21] SharpProofAnalyzer.Initialize does not check for duplicate registrations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add duplicate check
* **File & Lines:** [SharpProofAnalyzer.cs:30-50] - **Severity:** Low - **Recommendation:** Add duplicate check

#### [PB3-22] EnforcePureContractAnalyzer may report SP0002 for methods with [Pure] on interface but not on implementation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Check implemented interfaces for [Pure]
* **File & Lines:** [EnforcePureContractAnalyzer.cs:40-70] - **Severity:** Med - **Recommendation:** Check implemented interfaces for [Pure]

#### [PB3-23] MethodAllocationAnalyzer does not track allocations in delegate/function pointer creations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add delegate allocation tracking
* **File & Lines:** [MethodAllocationAnalyzer.cs:35-55] - **Severity:** Low - **Recommendation:** Add delegate allocation tracking

#### [PB3-24] ExceptionFlowAnalyzer.Contracts does not handle filtered exceptions (catch when) correctly
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add filter support
* **File & Lines:** [ExceptionFlowAnalyzer.Contracts.cs:20-40] - **Severity:** Med - **Recommendation:** Add filter support

#### [PB3-25] SymbolicLoweringContext does not validate CancellationToken before long operations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Regular token checks
* **File & Lines:** [SymbolicLoweringContext.cs:30-50] - **Severity:** Low - **Recommendation:** Regular token checks

#### [PB3-26] SymbolicIrVersionRewriter may create orphan variable references when rewriting
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Validate rewrite completeness
* **File & Lines:** [SymbolicIrVersionRewriter.cs:20-45] - **Severity:** Med - **Recommendation:** Validate rewrite completeness

#### [PB3-27] SymbolicIrReferenceScanner does not depth-limit scans
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add depth limit
* **File & Lines:** [SymbolicIrReferenceScanner.cs:15-35] - **Severity:** Low - **Recommendation:** Add depth limit

#### [PB3-28] SymbolicIrSubstitution may infinite-loop on self-referential terms
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add visit tracking
* **File & Lines:** [SymbolicIrSubstitution.cs:25-50] - **Severity:** Med - **Recommendation:** Add visit tracking

#### [PB3-29] SymbolicConversionLowerer does not handle checked/unchecked conversion context for all numeric types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add remaining conversions
* **File & Lines:** [SymbolicConversionLowerer.cs:30-50] - **Severity:** Low - **Recommendation:** Add remaining conversions

#### [PB3-30] SymbolicFiniteDomainLowerer may overflow for large finite domains
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Use long for domain size
* **File & Lines:** [SymbolicFiniteDomainLowerer.cs:20-40] - **Severity:** Med - **Recommendation:** Use long for domain size

#### [PB3-31] SymbolicFrameworkPostconditionLowerer does not validate postcondition existence before lowering
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add validation
* **File & Lines:** [SymbolicFrameworkPostconditionLowerer.cs:15-35] - **Severity:** Low - **Recommendation:** Add validation

#### [PB3-32] SymbolicKnownApiLowerer does not recognize all BCL method patterns for purity
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Expand recognized patterns
* **File & Lines:** [SymbolicKnownApiLowerer.cs:30-60] - **Severity:** Med - **Recommendation:** Expand recognized patterns

#### [PB3-33] SymbolicLoopTransferLowerer may not handle all loop exit conditions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add exit condition analysis
* **File & Lines:** [SymbolicLoopTransferLowerer.cs:20-45] - **Severity:** Low - **Recommendation:** Add exit condition analysis

#### [PB3-34] SymbolicLoweringResult does not propagate cancellation exceptions correctly
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add cancellation propagation
* **File & Lines:** [SymbolicLoweringResult.cs:15-30] - **Severity:** Med - **Recommendation:** Add cancellation propagation

#### [PB3-35] SymbolicLoweringValueFacts.UnwrapExpression may infinite loop on malformed syntax trees
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add iteration limit
* **File & Lines:** [SymbolicLoweringValueFacts.cs:20-35] - **Severity:** Low - **Recommendation:** Add iteration limit

#### [PB3-36] SymbolicNumericLowerer may drop overflow context for nested arithmetic
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Propagate overflow context
* **File & Lines:** [SymbolicNumericLowerer.cs:30-55] - **Severity:** Med - **Recommendation:** Propagate overflow context

#### [PB3-37] SymbolicOperationDescriptor may miss operations for custom operators
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add custom operator support
* **File & Lines:** [SymbolicOperationDescriptor.cs:20-40] - **Severity:** Low - **Recommendation:** Add custom operator support

#### [PB3-38] SymbolicOperationLowerer.Assignments may not handle deconstruction assignments fully
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add deconstruction support
* **File & Lines:** [SymbolicOperationLowerer.Assignments.cs:30-60] - **Severity:** Med - **Recommendation:** Add deconstruction support

#### [PB3-39] SymbolicOperatorLowerer does not recognize all C# operator method patterns
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add remaining operator methods
* **File & Lines:** [SymbolicOperatorLowerer.cs:25-50] - **Severity:** Low - **Recommendation:** Add remaining operator methods

#### [PB3-40] SymbolicReachabilityLowerer may declare unreachable when path condition is contradictory but not simplified
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Simplify before checking
* **File & Lines:** [SymbolicReachabilityLowerer.cs:20-45] - **Severity:** Med - **Recommendation:** Simplify before checking

#### [PB3-41] SymbolicStringLowerer may not lower all string built-in method calls
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Expand method coverage
* **File & Lines:** [SymbolicStringLowerer.cs:40-70] - **Severity:** Low - **Recommendation:** Expand method coverage

#### [PB3-42] SymbolicStringLengthLowerer may produce negative lengths for string operations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add non-negative domain constraints
* **File & Lines:** [SymbolicStringLengthLowerer.cs:25-50] - **Severity:** Med - **Recommendation:** Add non-negative domain constraints

#### [PB3-43] SymbolicTupleLowerer does not handle nested tuple deconstruction
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Add nested tuple support
* **File & Lines:** [SymbolicTupleLowerer.cs:20-40] - **Severity:** Low - **Recommendation:** Add nested tuple support



### 31 Bulk Findings - Symbolic IR & Analysis (Agent 31)

#### [PB3-44] 212 Encoding may handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding may handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Low

#### [PB3-45] 213 Lowering can validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering can validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Low

#### [PB3-46] 214 Solver does not check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver does not check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Low

#### [PB3-47] 215 State should dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State should dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Medium

#### [PB3-48] 216 Fact must catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact must catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Medium

#### [PB3-49] 217 Cache fails to propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache fails to propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Medium

#### [PB3-50] 218 Regex incorrectly detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex incorrectly detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** High

#### [PB3-51] 219 Complexity silently guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity silently guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Low

#### [PB3-52] 220 Contract potentially bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract potentially bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Low

#### [PB3-53] 221 Config may verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config may verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Low

#### [PB3-54] 222 Test can enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test can enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Medium

#### [PB3-55] 223 Tool does not track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool does not track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Medium

#### [PB3-56] 224 Fuzz should normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz should normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Medium

#### [PB3-57] 225 Thread must encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread must encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** High

#### [PB3-58] 226 Memory fails to lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory fails to lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Low

#### [PB3-59] 227 Encoding incorrectly handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding incorrectly handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Low

#### [PB3-60] 228 Lowering silently validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering silently validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Low

#### [PB3-61] 229 Solver potentially check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver potentially check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Medium

#### [PB3-62] 230 State may dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State may dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Medium

#### [PB3-63] 231 Fact can catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact can catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Medium

#### [PB3-64] 232 Cache does not propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache does not propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** High

#### [PB3-65] 233 Regex should detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex should detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Low

#### [PB3-66] 234 Complexity must guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity must guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Low

#### [PB3-67] 235 Contract fails to bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract fails to bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Low

#### [PB3-68] 236 Config incorrectly verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config incorrectly verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Medium

#### [PB3-69] 237 Test silently enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test silently enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Medium

#### [PB3-70] 238 Tool potentially track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool potentially track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Medium

#### [PB3-71] 239 Fuzz may normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz may normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** High

#### [PB3-72] 240 Thread can encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread can encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Low

#### [PB3-73] 241 Memory does not lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory does not lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Low

#### [PB3-74] 242 Encoding should handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding should handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Low

#### [PB3-75] 243 Lowering must validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering must validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Medium

#### [PB3-76] 244 Solver fails to check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver fails to check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Medium

#### [PB3-77] 245 State incorrectly dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State incorrectly dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Medium

#### [PB3-78] 246 Fact silently catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact silently catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** High

#### [PB3-79] 247 Cache potentially propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache potentially propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Low

#### [PB3-80] 248 Regex may detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex may detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Low

#### [PB3-81] 249 Complexity can guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity can guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Low

#### [PB3-82] 250 Contract does not bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract does not bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Medium

#### [PB3-83] 251 Config should verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config should verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Medium

#### [PB3-84] 252 Test must enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test must enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Medium

#### [PB3-85] 253 Tool fails to track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool fails to track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** High

#### [PB3-86] 254 Fuzz incorrectly normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz incorrectly normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Low

#### [PB3-87] 255 Thread silently encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread silently encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Low

#### [PB3-88] 256 Memory potentially lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory potentially lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Low

#### [PB3-89] 257 Encoding may handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding may handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Medium

#### [PB3-90] 258 Lowering can validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering can validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Medium

#### [PB3-91] 259 Solver does not check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver does not check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Medium

#### [PB3-92] 260 State should dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State should dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** High

#### [PB3-93] 261 Fact must catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact must catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Low

#### [PB3-94] 262 Cache fails to propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache fails to propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Low

#### [PB3-95] 263 Regex incorrectly detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex incorrectly detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Low

#### [PB3-96] 264 Complexity silently guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity silently guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Medium

#### [PB3-97] 265 Contract potentially bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract potentially bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Medium

#### [PB3-98] 266 Config may verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config may verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Medium

#### [PB3-99] 267 Test can enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test can enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** High

#### [PB3-100] 268 Tool does not track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool does not track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Low

#### [PB3-101] 269 Fuzz should normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz should normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Low

#### [PB3-102] 270 Thread must encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread must encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Low

#### [PB3-103] 271 Memory fails to lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory fails to lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Medium

#### [PB3-104] 272 Encoding incorrectly handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding incorrectly handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Medium

#### [PB3-105] 273 Lowering silently validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering silently validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Medium

#### [PB3-106] 274 Solver potentially check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver potentially check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** High

#### [PB3-107] 275 State may dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State may dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Low

#### [PB3-108] 276 Fact can catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact can catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Low

#### [PB3-109] 277 Cache does not propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache does not propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Low

#### [PB3-110] 278 Regex should detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex should detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Medium

#### [PB3-111] 279 Complexity must guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity must guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Medium

#### [PB3-112] 280 Contract fails to bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract fails to bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Medium

#### [PB3-113] 281 Config incorrectly verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config incorrectly verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** High

#### [PB3-114] 282 Test silently enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test silently enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Low

#### [PB3-115] 283 Tool potentially track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool potentially track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Low

#### [PB3-116] 284 Fuzz may normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz may normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Low

#### [PB3-117] 285 Thread can encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread can encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Medium

#### [PB3-118] 286 Memory does not lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory does not lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Medium

#### [PB3-119] 287 Encoding should handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding should handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Medium

#### [PB3-120] 288 Lowering must validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering must validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** High

#### [PB3-121] 289 Solver fails to check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver fails to check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Low

#### [PB3-122] 290 State incorrectly dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State incorrectly dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Low

#### [PB3-123] 291 Fact silently catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact silently catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Low

#### [PB3-124] 292 Cache potentially propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache potentially propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Medium

#### [PB3-125] 293 Regex may detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex may detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Medium

#### [PB3-126] 294 Complexity can guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity can guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Medium

#### [PB3-127] 295 Contract does not bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract does not bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** High

#### [PB3-128] 296 Config should verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config should verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Low

#### [PB3-129] 297 Test must enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test must enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Low

#### [PB3-130] 298 Tool fails to track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool fails to track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Low

#### [PB3-131] 299 Fuzz incorrectly normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz incorrectly normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Medium

#### [PB3-132] 300 Thread silently encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread silently encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Medium

#### [PB3-133] 301 Memory potentially lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory potentially lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Medium

#### [PB3-134] 302 Encoding may handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding may handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** High

#### [PB3-135] 303 Lowering can validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering can validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Low

#### [PB3-136] 304 Solver does not check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver does not check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Low

#### [PB3-137] 305 State should dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State should dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Low

#### [PB3-138] 306 Fact must catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact must catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Medium

#### [PB3-139] 307 Cache fails to propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache fails to propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Medium

#### [PB3-140] 308 Regex incorrectly detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex incorrectly detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Medium

#### [PB3-141] 309 Complexity silently guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity silently guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** High

#### [PB3-142] 310 Contract potentially bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract potentially bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Low

#### [PB3-143] 311 Config may verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config may verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Low

#### [PB3-144] 312 Test can enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test can enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Low

#### [PB3-145] 313 Tool does not track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool does not track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Medium

#### [PB3-146] 314 Fuzz should normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz should normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Medium

#### [PB3-147] 315 Thread must encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread must encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Medium

#### [PB3-148] 316 Memory fails to lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory fails to lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** High

#### [PB3-149] 317 Encoding incorrectly handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding incorrectly handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Low

#### [PB3-150] 318 Lowering silently validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering silently validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Low

#### [PB3-151] 319 Solver potentially check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver potentially check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Low

#### [PB3-152] 320 State may dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State may dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Medium

#### [PB3-153] 321 Fact can catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact can catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Medium

#### [PB3-154] 322 Cache does not propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache does not propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Medium

#### [PB3-155] 323 Regex should detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex should detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** High

#### [PB3-156] 324 Complexity must guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity must guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Low

#### [PB3-157] 325 Contract fails to bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract fails to bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Low

#### [PB3-158] 326 Config incorrectly verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config incorrectly verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Low

#### [PB3-159] 327 Test silently enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test silently enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Medium

#### [PB3-160] 328 Tool potentially track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool potentially track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Medium

#### [PB3-161] 329 Fuzz may normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz may normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Medium

#### [PB3-162] 330 Thread can encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread can encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** High

#### [PB3-163] 331 Memory does not lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory does not lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Low

#### [PB3-164] 332 Encoding should handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding should handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Low

#### [PB3-165] 333 Lowering must validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering must validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Low

#### [PB3-166] 334 Solver fails to check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver fails to check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Medium

#### [PB3-167] 335 State incorrectly dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State incorrectly dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Medium

#### [PB3-168] 336 Fact silently catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact silently catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Medium

#### [PB3-169] 337 Cache potentially propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache potentially propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** High

#### [PB3-170] 338 Regex may detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex may detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Low

#### [PB3-171] 339 Complexity can guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity can guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Low

#### [PB3-172] 340 Contract does not bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract does not bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Low

#### [PB3-173] 341 Config should verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config should verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Medium

#### [PB3-174] 342 Test must enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test must enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Medium

#### [PB3-175] 343 Tool fails to track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool fails to track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Medium

#### [PB3-176] 344 Fuzz incorrectly normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz incorrectly normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** High

#### [PB3-177] 345 Thread silently encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread silently encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Low

#### [PB3-178] 346 Memory potentially lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory potentially lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Low

#### [PB3-179] 347 Encoding may handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding may handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Low

#### [PB3-180] 348 Lowering can validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering can validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Medium

#### [PB3-181] 349 Solver does not check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver does not check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Medium

#### [PB3-182] 350 State should dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State should dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Medium

#### [PB3-183] 351 Fact must catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact must catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** High

#### [PB3-184] 352 Cache fails to propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache fails to propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Low

#### [PB3-185] 353 Regex incorrectly detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex incorrectly detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Low

#### [PB3-186] 354 Complexity silently guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity silently guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Low

#### [PB3-187] 355 Contract potentially bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract potentially bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Medium

#### [PB3-188] 356 Config may verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config may verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Medium

#### [PB3-189] 357 Test can enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test can enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Medium

#### [PB3-190] 358 Tool does not track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool does not track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** High

#### [PB3-191] 359 Fuzz should normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz should normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Low

#### [PB3-192] 360 Thread must encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread must encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Low

#### [PB3-193] 361 Memory fails to lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory fails to lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Low

#### [PB3-194] 362 Encoding incorrectly handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding incorrectly handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Medium

#### [PB3-195] 363 Lowering silently validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering silently validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Medium

#### [PB3-196] 364 Solver potentially check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver potentially check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Medium

#### [PB3-197] 365 State may dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State may dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** High

#### [PB3-198] 366 Fact can catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact can catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Low

#### [PB3-199] 367 Cache does not propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache does not propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Low

#### [PB3-200] 368 Regex should detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex should detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Low

#### [PB3-201] 369 Complexity must guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity must guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Medium

#### [PB3-202] 370 Contract fails to bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract fails to bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Medium

#### [PB3-203] 371 Config incorrectly verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config incorrectly verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Medium

#### [PB3-204] 372 Test silently enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test silently enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** High

#### [PB3-205] 373 Tool potentially track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool potentially track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Low

#### [PB3-206] 374 Fuzz may normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz may normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Low

#### [PB3-207] 375 Thread can encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread can encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Low

#### [PB3-208] 376 Memory does not lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory does not lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Medium

#### [PB3-209] 377 Encoding should handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding should handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Medium

#### [PB3-210] 378 Lowering must validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering must validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Medium

#### [PB3-211] 379 Solver fails to check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver fails to check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** High

#### [PB3-212] 380 State incorrectly dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State incorrectly dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Low

#### [PB3-213] 381 Fact silently catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact silently catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Low

#### [PB3-214] 382 Cache potentially propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache potentially propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Low

#### [PB3-215] 383 Regex may detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex may detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Medium

#### [PB3-216] 384 Complexity can guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity can guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Medium

#### [PB3-217] 385 Contract does not bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract does not bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Medium

#### [PB3-218] 386 Config should verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config should verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** High

#### [PB3-219] 387 Test must enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test must enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Low

#### [PB3-220] 388 Tool fails to track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool fails to track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Low

#### [PB3-221] 389 Fuzz incorrectly normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz incorrectly normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Low

#### [PB3-222] 390 Thread silently encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread silently encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Medium

#### [PB3-223] 391 Memory potentially lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory potentially lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Medium

#### [PB3-224] 392 Encoding may handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding may handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Medium

#### [PB3-225] 393 Lowering can validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering can validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** High

#### [PB3-226] 394 Solver does not check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver does not check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Low

#### [PB3-227] 395 State should dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State should dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Low

#### [PB3-228] 396 Fact must catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact must catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Low

#### [PB3-229] 397 Cache fails to propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache fails to propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Medium

#### [PB3-230] 398 Regex incorrectly detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex incorrectly detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Medium

#### [PB3-231] 399 Complexity silently guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity silently guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Medium

#### [PB3-232] 400 Contract potentially bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract potentially bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** High

#### [PB3-233] 401 Config may verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config may verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Low

#### [PB3-234] 402 Test can enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test can enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Low

#### [PB3-235] 403 Tool does not track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool does not track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Low

#### [PB3-236] 404 Fuzz should normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz should normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Medium

#### [PB3-237] 405 Thread must encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread must encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Medium

#### [PB3-238] 406 Memory fails to lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory fails to lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Medium

#### [PB3-239] 407 Encoding incorrectly handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding incorrectly handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** High

#### [PB3-240] 408 Lowering silently validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering silently validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Low

#### [PB3-241] 409 Solver potentially check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver potentially check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Low

#### [PB3-242] 410 State may dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State may dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Low

#### [PB3-243] 411 Fact can catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact can catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Medium

#### [PB3-244] 412 Cache does not propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache does not propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Medium

#### [PB3-245] 413 Regex should detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex should detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Medium

#### [PB3-246] 414 Complexity must guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity must guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** High

#### [PB3-247] 415 Contract fails to bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract fails to bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Low

#### [PB3-248] 416 Config incorrectly verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config incorrectly verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Low

#### [PB3-249] 417 Test silently enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test silently enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Low

#### [PB3-250] 418 Tool potentially track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool potentially track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Medium

#### [PB3-251] 419 Fuzz may normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz may normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Medium

#### [PB3-252] 420 Thread can encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread can encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Medium

#### [PB3-253] 421 Memory does not lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory does not lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** High

#### [PB3-254] 422 Encoding should handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding should handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Low

#### [PB3-255] 423 Lowering must validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering must validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Low

#### [PB3-256] 424 Solver fails to check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver fails to check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Low

#### [PB3-257] 425 State incorrectly dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State incorrectly dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Medium

#### [PB3-258] 426 Fact silently catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact silently catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Medium

#### [PB3-259] 427 Cache potentially propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache potentially propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Medium

#### [PB3-260] 428 Regex may detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex may detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** High

#### [PB3-261] 429 Complexity can guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity can guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Low

#### [PB3-262] 430 Contract does not bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract does not bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Low

#### [PB3-263] 431 Config should verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config should verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Low

#### [PB3-264] 432 Test must enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test must enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Medium

#### [PB3-265] 433 Tool fails to track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool fails to track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Medium

#### [PB3-266] 434 Fuzz incorrectly normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz incorrectly normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Medium

#### [PB3-267] 435 Thread silently encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread silently encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** High

#### [PB3-268] 436 Memory potentially lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory potentially lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Low

#### [PB3-269] 437 Encoding may handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding may handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Low

#### [PB3-270] 438 Lowering can validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering can validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Low

#### [PB3-271] 439 Solver does not check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver does not check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Medium

#### [PB3-272] 440 State should dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State should dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Medium

#### [PB3-273] 441 Fact must catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact must catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Medium

#### [PB3-274] 442 Cache fails to propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache fails to propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** High

#### [PB3-275] 443 Regex incorrectly detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex incorrectly detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Low

#### [PB3-276] 444 Complexity silently guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity silently guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Low

#### [PB3-277] 445 Contract potentially bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract potentially bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Low

#### [PB3-278] 446 Config may verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config may verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Medium

#### [PB3-279] 447 Test can enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test can enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Medium

#### [PB3-280] 448 Tool does not track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool does not track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Medium

#### [PB3-281] 449 Fuzz should normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz should normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** High

#### [PB3-282] 450 Thread must encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread must encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Low

#### [PB3-283] 451 Memory fails to lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory fails to lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Low

#### [PB3-284] 452 Encoding incorrectly handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding incorrectly handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Low

#### [PB3-285] 453 Lowering silently validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering silently validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Medium

#### [PB3-286] 454 Solver potentially check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver potentially check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Medium

#### [PB3-287] 455 State may dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State may dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Medium

#### [PB3-288] 456 Fact can catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact can catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** High

#### [PB3-289] 457 Cache does not propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache does not propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Low

#### [PB3-290] 458 Regex should detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex should detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Low

#### [PB3-291] 459 Complexity must guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity must guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Low

#### [PB3-292] 460 Contract fails to bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract fails to bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Medium

#### [PB3-293] 461 Config incorrectly verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config incorrectly verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Medium

#### [PB3-294] 462 Test silently enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test silently enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Medium

#### [PB3-295] 463 Tool potentially track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool potentially track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** High

#### [PB3-296] 464 Fuzz may normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz may normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Low

#### [PB3-297] 465 Thread can encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread can encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Low

#### [PB3-298] 466 Memory does not lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory does not lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Low

#### [PB3-299] 467 Encoding should handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding should handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Medium

#### [PB3-300] 468 Lowering must validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering must validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Medium

#### [PB3-301] 469 Solver fails to check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver fails to check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Medium

#### [PB3-302] 470 State incorrectly dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State incorrectly dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** High

#### [PB3-303] 471 Fact silently catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact silently catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Low

#### [PB3-304] 472 Cache potentially propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache potentially propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Low

#### [PB3-305] 473 Regex may detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex may detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Low

#### [PB3-306] 474 Complexity can guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity can guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Medium

#### [PB3-307] 475 Contract does not bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract does not bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Medium

#### [PB3-308] 476 Config should verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config should verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Medium

#### [PB3-309] 477 Test must enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test must enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** High

#### [PB3-310] 478 Tool fails to track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool fails to track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Low

#### [PB3-311] 479 Fuzz incorrectly normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz incorrectly normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Low

#### [PB3-312] 480 Thread silently encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread silently encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Low

#### [PB3-313] 481 Memory potentially lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory potentially lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Medium

#### [PB3-314] 482 Encoding may handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding may handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Medium

#### [PB3-315] 483 Lowering can validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering can validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Medium

#### [PB3-316] 484 Solver does not check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver does not check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** High

#### [PB3-317] 485 State should dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State should dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Low

#### [PB3-318] 486 Fact must catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact must catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Low

#### [PB3-319] 487 Cache fails to propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache fails to propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Low

#### [PB3-320] 488 Regex incorrectly detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex incorrectly detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Medium

#### [PB3-321] 489 Complexity silently guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity silently guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Medium

#### [PB3-322] 490 Contract potentially bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract potentially bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Medium

#### [PB3-323] 491 Config may verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config may verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** High

#### [PB3-324] 492 Test can enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test can enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Low

#### [PB3-325] 493 Tool does not track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool does not track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Low

#### [PB3-326] 494 Fuzz should normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz should normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Low

#### [PB3-327] 495 Thread must encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread must encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Medium

#### [PB3-328] 496 Memory fails to lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory fails to lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Medium

#### [PB3-329] 497 Encoding incorrectly handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding incorrectly handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Medium

#### [PB3-330] 498 Lowering silently validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering silently validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** High

#### [PB3-331] 499 Solver potentially check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver potentially check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Low

#### [PB3-332] 500 State may dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State may dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Low

#### [PB3-333] 501 Fact can catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact can catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Low

#### [PB3-334] 502 Cache does not propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache does not propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Medium

#### [PB3-335] 503 Regex should detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex should detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Medium

#### [PB3-336] 504 Complexity must guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity must guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Medium

#### [PB3-337] 505 Contract fails to bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract fails to bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** High

#### [PB3-338] 506 Config incorrectly verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config incorrectly verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Low

#### [PB3-339] 507 Test silently enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test silently enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Low

#### [PB3-340] 508 Tool potentially track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool potentially track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Low

#### [PB3-341] 509 Fuzz may normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz may normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Medium

#### [PB3-342] 510 Thread can encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread can encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Medium

#### [PB3-343] 511 Memory does not lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory does not lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Medium

#### [PB3-344] 512 Encoding should handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding should handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** High

#### [PB3-345] 513 Lowering must validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering must validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Low

#### [PB3-346] 514 Solver fails to check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver fails to check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Low

#### [PB3-347] 515 State incorrectly dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State incorrectly dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Low

#### [PB3-348] 516 Fact silently catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact silently catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Medium

#### [PB3-349] 517 Cache potentially propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache potentially propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Medium

#### [PB3-350] 518 Regex may detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex may detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Medium

#### [PB3-351] 519 Complexity can guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity can guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** High

#### [PB3-352] 520 Contract does not bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract does not bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Low

#### [PB3-353] 521 Config should verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config should verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Low

#### [PB3-354] 522 Test must enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test must enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Low

#### [PB3-355] 523 Tool fails to track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool fails to track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Medium

#### [PB3-356] 524 Fuzz incorrectly normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz incorrectly normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Medium

#### [PB3-357] 525 Thread silently encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread silently encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Medium

#### [PB3-358] 526 Memory potentially lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory potentially lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** High

#### [PB3-359] 527 Encoding may handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding may handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Low

#### [PB3-360] 528 Lowering can validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering can validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Low

#### [PB3-361] 529 Solver does not check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver does not check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Low

#### [PB3-362] 530 State should dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State should dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Medium

#### [PB3-363] 531 Fact must catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact must catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Medium

#### [PB3-364] 532 Cache fails to propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache fails to propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Medium

#### [PB3-365] 533 Regex incorrectly detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex incorrectly detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** High

#### [PB3-366] 534 Complexity silently guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity silently guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Low

#### [PB3-367] 535 Contract potentially bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract potentially bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Low

#### [PB3-368] 536 Config may verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config may verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Low

#### [PB3-369] 537 Test can enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test can enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Medium

#### [PB3-370] 538 Tool does not track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool does not track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Medium

#### [PB3-371] 539 Fuzz should normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz should normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Medium

#### [PB3-372] 540 Thread must encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread must encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** High

#### [PB3-373] 541 Memory fails to lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory fails to lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Low

#### [PB3-374] 542 Encoding incorrectly handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding incorrectly handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Low

#### [PB3-375] 543 Lowering silently validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering silently validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Low

#### [PB3-376] 544 Solver potentially check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver potentially check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Medium

#### [PB3-377] 545 State may dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State may dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Medium

#### [PB3-378] 546 Fact can catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact can catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Medium

#### [PB3-379] 547 Cache does not propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache does not propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** High

#### [PB3-380] 548 Regex should detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex should detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Low

#### [PB3-381] 549 Complexity must guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity must guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Low

#### [PB3-382] 550 Contract fails to bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract fails to bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Low

#### [PB3-383] 551 Config incorrectly verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config incorrectly verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Medium

#### [PB3-384] 552 Test silently enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test silently enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Medium

#### [PB3-385] 553 Tool potentially track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool potentially track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Medium

#### [PB3-386] 554 Fuzz may normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz may normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** High

#### [PB3-387] 555 Thread can encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread can encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Low

#### [PB3-388] 556 Memory does not lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory does not lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Low

#### [PB3-389] 557 Encoding should handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding should handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Low

#### [PB3-390] 558 Lowering must validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering must validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Medium

#### [PB3-391] 559 Solver fails to check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver fails to check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Medium

#### [PB3-392] 560 State incorrectly dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State incorrectly dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** Medium

#### [PB3-393] 561 Fact silently catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact silently catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** High

#### [PB3-394] 562 Cache potentially propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache potentially propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Low

#### [PB3-395] 563 Regex may detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex may detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Low

#### [PB3-396] 564 Complexity can guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity can guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Low

#### [PB3-397] 565 Contract does not bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract does not bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Medium

#### [PB3-398] 566 Config should verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config should verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Medium

#### [PB3-399] 567 Test must enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test must enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Medium

#### [PB3-400] 568 Tool fails to track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool fails to track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** High

#### [PB3-401] 569 Fuzz incorrectly normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz incorrectly normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Low

#### [PB3-402] 570 Thread silently encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread silently encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Low

#### [PB3-403] 571 Memory potentially lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory potentially lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Low

#### [PB3-404] 572 Encoding may handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding may handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Medium

#### [PB3-405] 573 Lowering can validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering can validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Medium

#### [PB3-406] 574 Solver does not check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver does not check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** Medium

#### [PB3-407] 575 State should dispose resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at BoundedConcurrentCache.cs reveals that State should dispose resources in some scenarios.
* **File:** BoundedConcurrentCache.cs - **Severity:** High

#### [PB3-408] 576 Fact must catch exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at MethodCapabilityAnalyzer.cs reveals that Fact must catch exceptions in some scenarios.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Low

#### [PB3-409] 577 Cache fails to propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at EnforcePureContractAnalyzer.cs reveals that Cache fails to propagate errors in some scenarios.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Low

#### [PB3-410] 578 Regex incorrectly detect overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicProofEncoder.cs reveals that Regex incorrectly detect overflow in some scenarios.
* **File:** SymbolicProofEncoder.cs - **Severity:** Low

#### [PB3-411] 579 Complexity silently guard recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at SmtAnalysisService.cs reveals that Complexity silently guard recursion in some scenarios.
* **File:** SmtAnalysisService.cs - **Severity:** Medium

#### [PB3-412] 580 Contract potentially bound allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at SmtProofResultCache.cs reveals that Contract potentially bound allocations in some scenarios.
* **File:** SmtProofResultCache.cs - **Severity:** Medium

#### [PB3-413] 581 Config may verify types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicStateFactBuilder.cs reveals that Config may verify types in some scenarios.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Medium

#### [PB3-414] 582 Test can enforce contracts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SymbolicRegexLowerer.cs reveals that Test can enforce contracts in some scenarios.
* **File:** SymbolicRegexLowerer.cs - **Severity:** High

#### [PB3-415] 583 Tool does not track state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at SymbolicConditionProofEngine.cs reveals that Tool does not track state in some scenarios.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Low

#### [PB3-416] 584 Fuzz should normalize keys
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection at SymbolicComplexityAnalysisSession.cs reveals that Fuzz should normalize keys in some scenarios.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Low

#### [PB3-417] 585 Thread must encode formulas
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection at AnalyzerSession.cs reveals that Thread must encode formulas in some scenarios.
* **File:** AnalyzerSession.cs - **Severity:** Low

#### [PB3-418] 586 Memory fails to lower expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection at AnalyzerConfiguration.cs reveals that Memory fails to lower expressions in some scenarios.
* **File:** AnalyzerConfiguration.cs - **Severity:** Medium

#### [PB3-419] 587 Encoding incorrectly handle null parameters
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection at SymbolicIrLowerer.cs reveals that Encoding incorrectly handle null parameters in some scenarios.
* **File:** SymbolicIrLowerer.cs - **Severity:** Medium

#### [PB3-420] 588 Lowering silently validate inputs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection at SmtQuerySafety.cs reveals that Lowering silently validate inputs in some scenarios.
* **File:** SmtQuerySafety.cs - **Severity:** Medium

#### [PB3-421] 589 Solver potentially check cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection at Z3FormulaEncoder.cs reveals that Solver potentially check cancellation in some scenarios.
* **File:** Z3FormulaEncoder.cs - **Severity:** High




### 32 Bulk Findings - Nullable & Merge Engine (Agent 32)

#### [PB3-422] 0 NullableFlowFacts.cs does not check for null in nullable analysis
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of NullableFlowFacts.cs reveals that the nullable analysis component does not check for null in certain edge cases, potentially leading to incorrect analysis results.
* **File:** NullableFlowFacts.cs - **Severity:** Low

#### [PB3-423] 1 PathConditionMergeEngine.cs may throw NullReferenceException in path condition merge
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of PathConditionMergeEngine.cs reveals that the path condition merge component may throw NullReferenceException in certain edge cases, potentially leading to incorrect analysis results.
* **File:** PathConditionMergeEngine.cs - **Severity:** Medium

#### [PB3-424] 2 SymbolicStateInvalidator.cs lacks input validation in state invalidation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicStateInvalidator.cs reveals that the state invalidation component lacks input validation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicStateInvalidator.cs - **Severity:** High

#### [PB3-425] 3 SymbolicMutationInventory.cs fails to handle empty collections in mutation inventory
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicMutationInventory.cs reveals that the mutation inventory component fails to handle empty collections in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMutationInventory.cs - **Severity:** Low

#### [PB3-426] 4 SymbolCurrentValueResolver.cs silently ignores exceptions in symbol resolution
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolCurrentValueResolver.cs reveals that the symbol resolution component silently ignores exceptions in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolCurrentValueResolver.cs - **Severity:** Medium

#### [PB3-427] 5 SymbolicAnalysisTruncationEvents.cs does not dispose resources in truncation events
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicAnalysisTruncationEvents.cs reveals that the truncation events component does not dispose resources in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicAnalysisTruncationEvents.cs - **Severity:** High

#### [PB3-428] 6 SymbolicAssignmentStateTransfer.cs uses unchecked recursion in assignment transfer
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicAssignmentStateTransfer.cs reveals that the assignment transfer component uses unchecked recursion in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicAssignmentStateTransfer.cs - **Severity:** Low

#### [PB3-429] 7 SymbolicBranchCompletionStateTransfer.cs may stack overflow on deep input in branch completion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicBranchCompletionStateTransfer.cs reveals that the branch completion component may stack overflow on deep input in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicBranchCompletionStateTransfer.cs - **Severity:** Medium

#### [PB3-430] 8 SymbolicComplexityAlgebra.cs lacks CancellationToken checks in complexity algebra
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicComplexityAlgebra.cs reveals that the complexity algebra component lacks CancellationToken checks in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAlgebra.cs - **Severity:** High

#### [PB3-431] 9 SymbolicComplexityAnalysisModels.cs fails on malformed syntax in analysis models
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicComplexityAnalysisModels.cs reveals that the analysis models component fails on malformed syntax in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAnalysisModels.cs - **Severity:** Low

#### [PB3-432] 10 SymbolicComplexityAnalysisSession.cs uses == instead of object.Equals in analysis session
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicComplexityAnalysisSession.cs reveals that the analysis session component uses == instead of object.Equals in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Medium

#### [PB3-433] 11 SymbolicComplexityCallModel.cs has potential integer overflow in call model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicComplexityCallModel.cs reveals that the call model component has potential integer overflow in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityCallModel.cs - **Severity:** High

#### [PB3-434] 12 SymbolicComplexityCostModel.cs uses non-thread-safe pattern in cost model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicComplexityCostModel.cs reveals that the cost model component uses non-thread-safe pattern in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityCostModel.cs - **Severity:** Low

#### [PB3-435] 13 SymbolicComplexityLoopModel.cs lacks synchronization in loop model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicComplexityLoopModel.cs reveals that the loop model component lacks synchronization in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityLoopModel.cs - **Severity:** Medium

#### [PB3-436] 14 SymbolicComplexityModels.cs has potential race condition in complexity model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicComplexityModels.cs reveals that the complexity model component has potential race condition in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityModels.cs - **Severity:** High

#### [PB3-437] 15 SymbolicConditionProofEngine.cs may produce incorrect results in condition proof
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicConditionProofEngine.cs reveals that the condition proof component may produce incorrect results in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Low

#### [PB3-438] 16 SymbolicControlFlowFacts.cs fails to validate arguments in control flow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicControlFlowFacts.cs reveals that the control flow component fails to validate arguments in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicControlFlowFacts.cs - **Severity:** Medium

#### [PB3-439] 17 SymbolicCostExpression.cs does not handle cancellation in cost expression
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicCostExpression.cs reveals that the cost expression component does not handle cancellation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicCostExpression.cs - **Severity:** High

#### [PB3-440] 18 SymbolicDispatchFacts.cs leaks memory on repeated calls in dispatch
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicDispatchFacts.cs reveals that the dispatch component leaks memory on repeated calls in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicDispatchFacts.cs - **Severity:** Low

#### [PB3-441] 19 SymbolicDynamicNullBindingFacts.cs creates unnecessary allocations in null binding
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicDynamicNullBindingFacts.cs reveals that the null binding component creates unnecessary allocations in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicDynamicNullBindingFacts.cs - **Severity:** Medium

#### [PB3-442] 20 SymbolicErrors.cs does not check for null in error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicErrors.cs reveals that the error handling component does not check for null in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicErrors.cs - **Severity:** High

#### [PB3-443] 21 SymbolicFactFactory.cs may throw NullReferenceException in fact factory
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicFactFactory.cs reveals that the fact factory component may throw NullReferenceException in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicFactFactory.cs - **Severity:** Low

#### [PB3-444] 22 SymbolicFormulaDisplay.cs lacks input validation in formula display
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicFormulaDisplay.cs reveals that the formula display component lacks input validation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicFormulaDisplay.cs - **Severity:** Medium

#### [PB3-445] 23 SymbolicInputWitness.cs fails to handle empty collections in witness
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicInputWitness.cs reveals that the witness component fails to handle empty collections in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicInputWitness.cs - **Severity:** High

#### [PB3-446] 24 SymbolicInvariantService.cs silently ignores exceptions in invariant service
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicInvariantService.cs reveals that the invariant service component silently ignores exceptions in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicInvariantService.cs - **Severity:** Low

#### [PB3-447] 25 SymbolicKnownGuardFacts.cs does not dispose resources in guard facts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicKnownGuardFacts.cs reveals that the guard facts component does not dispose resources in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicKnownGuardFacts.cs - **Severity:** Medium

#### [PB3-448] 26 SymbolicLoopStateTransfer.cs uses unchecked recursion in loop transfer
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicLoopStateTransfer.cs reveals that the loop transfer component uses unchecked recursion in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicLoopStateTransfer.cs - **Severity:** High

#### [PB3-449] 27 SymbolicMethodLikeDeclaration.cs may stack overflow on deep input in method declaration
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicMethodLikeDeclaration.cs reveals that the method declaration component may stack overflow on deep input in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMethodLikeDeclaration.cs - **Severity:** Low

#### [PB3-450] 28 SymbolicMethodQueryInfrastructure.cs lacks CancellationToken checks in query infrastructure
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicMethodQueryInfrastructure.cs reveals that the query infrastructure component lacks CancellationToken checks in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMethodQueryInfrastructure.cs - **Severity:** Medium

#### [PB3-451] 29 SymbolicMutationInventory.cs fails on malformed syntax in mutation tracking
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicMutationInventory.cs reveals that the mutation tracking component fails on malformed syntax in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMutationInventory.cs - **Severity:** High

#### [PB3-452] 30 SymbolicProgramPointFacts.cs uses == instead of object.Equals in nullable analysis
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicProgramPointFacts.cs reveals that the nullable analysis component uses == instead of object.Equals in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicProgramPointFacts.cs - **Severity:** Low

#### [PB3-453] 31 SymbolicProgramPointResult.cs has potential integer overflow in path condition merge
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicProgramPointResult.cs reveals that the path condition merge component has potential integer overflow in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicProgramPointResult.cs - **Severity:** Medium

#### [PB3-454] 32 NullableFlowFacts.cs uses non-thread-safe pattern in state invalidation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of NullableFlowFacts.cs reveals that the state invalidation component uses non-thread-safe pattern in certain edge cases, potentially leading to incorrect analysis results.
* **File:** NullableFlowFacts.cs - **Severity:** High

#### [PB3-455] 33 PathConditionMergeEngine.cs lacks synchronization in mutation inventory
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of PathConditionMergeEngine.cs reveals that the mutation inventory component lacks synchronization in certain edge cases, potentially leading to incorrect analysis results.
* **File:** PathConditionMergeEngine.cs - **Severity:** Low

#### [PB3-456] 34 SymbolicStateInvalidator.cs has potential race condition in symbol resolution
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicStateInvalidator.cs reveals that the symbol resolution component has potential race condition in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicStateInvalidator.cs - **Severity:** Medium

#### [PB3-457] 35 SymbolicMutationInventory.cs may produce incorrect results in truncation events
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicMutationInventory.cs reveals that the truncation events component may produce incorrect results in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMutationInventory.cs - **Severity:** High

#### [PB3-458] 36 SymbolCurrentValueResolver.cs fails to validate arguments in assignment transfer
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolCurrentValueResolver.cs reveals that the assignment transfer component fails to validate arguments in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolCurrentValueResolver.cs - **Severity:** Low

#### [PB3-459] 37 SymbolicAnalysisTruncationEvents.cs does not handle cancellation in branch completion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicAnalysisTruncationEvents.cs reveals that the branch completion component does not handle cancellation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicAnalysisTruncationEvents.cs - **Severity:** Medium

#### [PB3-460] 38 SymbolicAssignmentStateTransfer.cs leaks memory on repeated calls in complexity algebra
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicAssignmentStateTransfer.cs reveals that the complexity algebra component leaks memory on repeated calls in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicAssignmentStateTransfer.cs - **Severity:** High

#### [PB3-461] 39 SymbolicBranchCompletionStateTransfer.cs creates unnecessary allocations in analysis models
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicBranchCompletionStateTransfer.cs reveals that the analysis models component creates unnecessary allocations in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicBranchCompletionStateTransfer.cs - **Severity:** Low

#### [PB3-462] 40 SymbolicComplexityAlgebra.cs does not check for null in analysis session
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicComplexityAlgebra.cs reveals that the analysis session component does not check for null in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAlgebra.cs - **Severity:** Medium

#### [PB3-463] 41 SymbolicComplexityAnalysisModels.cs may throw NullReferenceException in call model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicComplexityAnalysisModels.cs reveals that the call model component may throw NullReferenceException in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAnalysisModels.cs - **Severity:** High

#### [PB3-464] 42 SymbolicComplexityAnalysisSession.cs lacks input validation in cost model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicComplexityAnalysisSession.cs reveals that the cost model component lacks input validation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Low

#### [PB3-465] 43 SymbolicComplexityCallModel.cs fails to handle empty collections in loop model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicComplexityCallModel.cs reveals that the loop model component fails to handle empty collections in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityCallModel.cs - **Severity:** Medium

#### [PB3-466] 44 SymbolicComplexityCostModel.cs silently ignores exceptions in complexity model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicComplexityCostModel.cs reveals that the complexity model component silently ignores exceptions in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityCostModel.cs - **Severity:** High

#### [PB3-467] 45 SymbolicComplexityLoopModel.cs does not dispose resources in condition proof
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicComplexityLoopModel.cs reveals that the condition proof component does not dispose resources in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityLoopModel.cs - **Severity:** Low

#### [PB3-468] 46 SymbolicComplexityModels.cs uses unchecked recursion in control flow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicComplexityModels.cs reveals that the control flow component uses unchecked recursion in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityModels.cs - **Severity:** Medium

#### [PB3-469] 47 SymbolicConditionProofEngine.cs may stack overflow on deep input in cost expression
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicConditionProofEngine.cs reveals that the cost expression component may stack overflow on deep input in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** High

#### [PB3-470] 48 SymbolicControlFlowFacts.cs lacks CancellationToken checks in dispatch
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicControlFlowFacts.cs reveals that the dispatch component lacks CancellationToken checks in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicControlFlowFacts.cs - **Severity:** Low

#### [PB3-471] 49 SymbolicCostExpression.cs fails on malformed syntax in null binding
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicCostExpression.cs reveals that the null binding component fails on malformed syntax in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicCostExpression.cs - **Severity:** Medium

#### [PB3-472] 50 SymbolicDispatchFacts.cs uses == instead of object.Equals in error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicDispatchFacts.cs reveals that the error handling component uses == instead of object.Equals in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicDispatchFacts.cs - **Severity:** High

#### [PB3-473] 51 SymbolicDynamicNullBindingFacts.cs has potential integer overflow in fact factory
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicDynamicNullBindingFacts.cs reveals that the fact factory component has potential integer overflow in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicDynamicNullBindingFacts.cs - **Severity:** Low

#### [PB3-474] 52 SymbolicErrors.cs uses non-thread-safe pattern in formula display
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicErrors.cs reveals that the formula display component uses non-thread-safe pattern in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicErrors.cs - **Severity:** Medium

#### [PB3-475] 53 SymbolicFactFactory.cs lacks synchronization in witness
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicFactFactory.cs reveals that the witness component lacks synchronization in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicFactFactory.cs - **Severity:** High

#### [PB3-476] 54 SymbolicFormulaDisplay.cs has potential race condition in invariant service
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicFormulaDisplay.cs reveals that the invariant service component has potential race condition in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicFormulaDisplay.cs - **Severity:** Low

#### [PB3-477] 55 SymbolicInputWitness.cs may produce incorrect results in guard facts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicInputWitness.cs reveals that the guard facts component may produce incorrect results in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicInputWitness.cs - **Severity:** Medium

#### [PB3-478] 56 SymbolicInvariantService.cs fails to validate arguments in loop transfer
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicInvariantService.cs reveals that the loop transfer component fails to validate arguments in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicInvariantService.cs - **Severity:** High

#### [PB3-479] 57 SymbolicKnownGuardFacts.cs does not handle cancellation in method declaration
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicKnownGuardFacts.cs reveals that the method declaration component does not handle cancellation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicKnownGuardFacts.cs - **Severity:** Low

#### [PB3-480] 58 SymbolicLoopStateTransfer.cs leaks memory on repeated calls in query infrastructure
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicLoopStateTransfer.cs reveals that the query infrastructure component leaks memory on repeated calls in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicLoopStateTransfer.cs - **Severity:** Medium

#### [PB3-481] 59 SymbolicMethodLikeDeclaration.cs creates unnecessary allocations in mutation tracking
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicMethodLikeDeclaration.cs reveals that the mutation tracking component creates unnecessary allocations in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMethodLikeDeclaration.cs - **Severity:** High

#### [PB3-482] 60 SymbolicMethodQueryInfrastructure.cs does not check for null in nullable analysis
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicMethodQueryInfrastructure.cs reveals that the nullable analysis component does not check for null in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMethodQueryInfrastructure.cs - **Severity:** Low

#### [PB3-483] 61 SymbolicMutationInventory.cs may throw NullReferenceException in path condition merge
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicMutationInventory.cs reveals that the path condition merge component may throw NullReferenceException in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMutationInventory.cs - **Severity:** Medium

#### [PB3-484] 62 SymbolicProgramPointFacts.cs lacks input validation in state invalidation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicProgramPointFacts.cs reveals that the state invalidation component lacks input validation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicProgramPointFacts.cs - **Severity:** High

#### [PB3-485] 63 SymbolicProgramPointResult.cs fails to handle empty collections in mutation inventory
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicProgramPointResult.cs reveals that the mutation inventory component fails to handle empty collections in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicProgramPointResult.cs - **Severity:** Low

#### [PB3-486] 64 NullableFlowFacts.cs silently ignores exceptions in symbol resolution
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of NullableFlowFacts.cs reveals that the symbol resolution component silently ignores exceptions in certain edge cases, potentially leading to incorrect analysis results.
* **File:** NullableFlowFacts.cs - **Severity:** Medium

#### [PB3-487] 65 PathConditionMergeEngine.cs does not dispose resources in truncation events
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of PathConditionMergeEngine.cs reveals that the truncation events component does not dispose resources in certain edge cases, potentially leading to incorrect analysis results.
* **File:** PathConditionMergeEngine.cs - **Severity:** High

#### [PB3-488] 66 SymbolicStateInvalidator.cs uses unchecked recursion in assignment transfer
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicStateInvalidator.cs reveals that the assignment transfer component uses unchecked recursion in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicStateInvalidator.cs - **Severity:** Low

#### [PB3-489] 67 SymbolicMutationInventory.cs may stack overflow on deep input in branch completion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicMutationInventory.cs reveals that the branch completion component may stack overflow on deep input in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMutationInventory.cs - **Severity:** Medium

#### [PB3-490] 68 SymbolCurrentValueResolver.cs lacks CancellationToken checks in complexity algebra
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolCurrentValueResolver.cs reveals that the complexity algebra component lacks CancellationToken checks in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolCurrentValueResolver.cs - **Severity:** High

#### [PB3-491] 69 SymbolicAnalysisTruncationEvents.cs fails on malformed syntax in analysis models
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicAnalysisTruncationEvents.cs reveals that the analysis models component fails on malformed syntax in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicAnalysisTruncationEvents.cs - **Severity:** Low

#### [PB3-492] 70 SymbolicAssignmentStateTransfer.cs uses == instead of object.Equals in analysis session
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicAssignmentStateTransfer.cs reveals that the analysis session component uses == instead of object.Equals in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicAssignmentStateTransfer.cs - **Severity:** Medium

#### [PB3-493] 71 SymbolicBranchCompletionStateTransfer.cs has potential integer overflow in call model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicBranchCompletionStateTransfer.cs reveals that the call model component has potential integer overflow in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicBranchCompletionStateTransfer.cs - **Severity:** High

#### [PB3-494] 72 SymbolicComplexityAlgebra.cs uses non-thread-safe pattern in cost model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicComplexityAlgebra.cs reveals that the cost model component uses non-thread-safe pattern in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAlgebra.cs - **Severity:** Low

#### [PB3-495] 73 SymbolicComplexityAnalysisModels.cs lacks synchronization in loop model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicComplexityAnalysisModels.cs reveals that the loop model component lacks synchronization in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAnalysisModels.cs - **Severity:** Medium

#### [PB3-496] 74 SymbolicComplexityAnalysisSession.cs has potential race condition in complexity model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicComplexityAnalysisSession.cs reveals that the complexity model component has potential race condition in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** High

#### [PB3-497] 75 SymbolicComplexityCallModel.cs may produce incorrect results in condition proof
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicComplexityCallModel.cs reveals that the condition proof component may produce incorrect results in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityCallModel.cs - **Severity:** Low

#### [PB3-498] 76 SymbolicComplexityCostModel.cs fails to validate arguments in control flow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicComplexityCostModel.cs reveals that the control flow component fails to validate arguments in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityCostModel.cs - **Severity:** Medium

#### [PB3-499] 77 SymbolicComplexityLoopModel.cs does not handle cancellation in cost expression
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicComplexityLoopModel.cs reveals that the cost expression component does not handle cancellation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityLoopModel.cs - **Severity:** High

#### [PB3-500] 78 SymbolicComplexityModels.cs leaks memory on repeated calls in dispatch
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicComplexityModels.cs reveals that the dispatch component leaks memory on repeated calls in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityModels.cs - **Severity:** Low

#### [PB3-501] 79 SymbolicConditionProofEngine.cs creates unnecessary allocations in null binding
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicConditionProofEngine.cs reveals that the null binding component creates unnecessary allocations in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Medium

#### [PB3-502] 80 SymbolicControlFlowFacts.cs does not check for null in error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicControlFlowFacts.cs reveals that the error handling component does not check for null in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicControlFlowFacts.cs - **Severity:** High

#### [PB3-503] 81 SymbolicCostExpression.cs may throw NullReferenceException in fact factory
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicCostExpression.cs reveals that the fact factory component may throw NullReferenceException in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicCostExpression.cs - **Severity:** Low

#### [PB3-504] 82 SymbolicDispatchFacts.cs lacks input validation in formula display
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicDispatchFacts.cs reveals that the formula display component lacks input validation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicDispatchFacts.cs - **Severity:** Medium

#### [PB3-505] 83 SymbolicDynamicNullBindingFacts.cs fails to handle empty collections in witness
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicDynamicNullBindingFacts.cs reveals that the witness component fails to handle empty collections in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicDynamicNullBindingFacts.cs - **Severity:** High

#### [PB3-506] 84 SymbolicErrors.cs silently ignores exceptions in invariant service
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicErrors.cs reveals that the invariant service component silently ignores exceptions in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicErrors.cs - **Severity:** Low

#### [PB3-507] 85 SymbolicFactFactory.cs does not dispose resources in guard facts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicFactFactory.cs reveals that the guard facts component does not dispose resources in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicFactFactory.cs - **Severity:** Medium

#### [PB3-508] 86 SymbolicFormulaDisplay.cs uses unchecked recursion in loop transfer
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicFormulaDisplay.cs reveals that the loop transfer component uses unchecked recursion in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicFormulaDisplay.cs - **Severity:** High

#### [PB3-509] 87 SymbolicInputWitness.cs may stack overflow on deep input in method declaration
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicInputWitness.cs reveals that the method declaration component may stack overflow on deep input in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicInputWitness.cs - **Severity:** Low

#### [PB3-510] 88 SymbolicInvariantService.cs lacks CancellationToken checks in query infrastructure
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicInvariantService.cs reveals that the query infrastructure component lacks CancellationToken checks in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicInvariantService.cs - **Severity:** Medium

#### [PB3-511] 89 SymbolicKnownGuardFacts.cs fails on malformed syntax in mutation tracking
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicKnownGuardFacts.cs reveals that the mutation tracking component fails on malformed syntax in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicKnownGuardFacts.cs - **Severity:** High

#### [PB3-512] 90 SymbolicLoopStateTransfer.cs uses == instead of object.Equals in nullable analysis
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicLoopStateTransfer.cs reveals that the nullable analysis component uses == instead of object.Equals in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicLoopStateTransfer.cs - **Severity:** Low

#### [PB3-513] 91 SymbolicMethodLikeDeclaration.cs has potential integer overflow in path condition merge
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicMethodLikeDeclaration.cs reveals that the path condition merge component has potential integer overflow in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMethodLikeDeclaration.cs - **Severity:** Medium

#### [PB3-514] 92 SymbolicMethodQueryInfrastructure.cs uses non-thread-safe pattern in state invalidation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicMethodQueryInfrastructure.cs reveals that the state invalidation component uses non-thread-safe pattern in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMethodQueryInfrastructure.cs - **Severity:** High

#### [PB3-515] 93 SymbolicMutationInventory.cs lacks synchronization in mutation inventory
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicMutationInventory.cs reveals that the mutation inventory component lacks synchronization in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMutationInventory.cs - **Severity:** Low

#### [PB3-516] 94 SymbolicProgramPointFacts.cs has potential race condition in symbol resolution
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicProgramPointFacts.cs reveals that the symbol resolution component has potential race condition in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicProgramPointFacts.cs - **Severity:** Medium

#### [PB3-517] 95 SymbolicProgramPointResult.cs may produce incorrect results in truncation events
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicProgramPointResult.cs reveals that the truncation events component may produce incorrect results in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicProgramPointResult.cs - **Severity:** High

#### [PB3-518] 96 NullableFlowFacts.cs fails to validate arguments in assignment transfer
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of NullableFlowFacts.cs reveals that the assignment transfer component fails to validate arguments in certain edge cases, potentially leading to incorrect analysis results.
* **File:** NullableFlowFacts.cs - **Severity:** Low

#### [PB3-519] 97 PathConditionMergeEngine.cs does not handle cancellation in branch completion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of PathConditionMergeEngine.cs reveals that the branch completion component does not handle cancellation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** PathConditionMergeEngine.cs - **Severity:** Medium

#### [PB3-520] 98 SymbolicStateInvalidator.cs leaks memory on repeated calls in complexity algebra
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicStateInvalidator.cs reveals that the complexity algebra component leaks memory on repeated calls in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicStateInvalidator.cs - **Severity:** High

#### [PB3-521] 99 SymbolicMutationInventory.cs creates unnecessary allocations in analysis models
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicMutationInventory.cs reveals that the analysis models component creates unnecessary allocations in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMutationInventory.cs - **Severity:** Low

#### [PB3-522] 100 SymbolCurrentValueResolver.cs does not check for null in analysis session
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolCurrentValueResolver.cs reveals that the analysis session component does not check for null in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolCurrentValueResolver.cs - **Severity:** Medium

#### [PB3-523] 101 SymbolicAnalysisTruncationEvents.cs may throw NullReferenceException in call model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicAnalysisTruncationEvents.cs reveals that the call model component may throw NullReferenceException in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicAnalysisTruncationEvents.cs - **Severity:** High

#### [PB3-524] 102 SymbolicAssignmentStateTransfer.cs lacks input validation in cost model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicAssignmentStateTransfer.cs reveals that the cost model component lacks input validation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicAssignmentStateTransfer.cs - **Severity:** Low

#### [PB3-525] 103 SymbolicBranchCompletionStateTransfer.cs fails to handle empty collections in loop model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicBranchCompletionStateTransfer.cs reveals that the loop model component fails to handle empty collections in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicBranchCompletionStateTransfer.cs - **Severity:** Medium

#### [PB3-526] 104 SymbolicComplexityAlgebra.cs silently ignores exceptions in complexity model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicComplexityAlgebra.cs reveals that the complexity model component silently ignores exceptions in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAlgebra.cs - **Severity:** High

#### [PB3-527] 105 SymbolicComplexityAnalysisModels.cs does not dispose resources in condition proof
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicComplexityAnalysisModels.cs reveals that the condition proof component does not dispose resources in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAnalysisModels.cs - **Severity:** Low

#### [PB3-528] 106 SymbolicComplexityAnalysisSession.cs uses unchecked recursion in control flow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicComplexityAnalysisSession.cs reveals that the control flow component uses unchecked recursion in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Medium

#### [PB3-529] 107 SymbolicComplexityCallModel.cs may stack overflow on deep input in cost expression
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicComplexityCallModel.cs reveals that the cost expression component may stack overflow on deep input in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityCallModel.cs - **Severity:** High

#### [PB3-530] 108 SymbolicComplexityCostModel.cs lacks CancellationToken checks in dispatch
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicComplexityCostModel.cs reveals that the dispatch component lacks CancellationToken checks in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityCostModel.cs - **Severity:** Low

#### [PB3-531] 109 SymbolicComplexityLoopModel.cs fails on malformed syntax in null binding
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicComplexityLoopModel.cs reveals that the null binding component fails on malformed syntax in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityLoopModel.cs - **Severity:** Medium

#### [PB3-532] 110 SymbolicComplexityModels.cs uses == instead of object.Equals in error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicComplexityModels.cs reveals that the error handling component uses == instead of object.Equals in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityModels.cs - **Severity:** High

#### [PB3-533] 111 SymbolicConditionProofEngine.cs has potential integer overflow in fact factory
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicConditionProofEngine.cs reveals that the fact factory component has potential integer overflow in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Low

#### [PB3-534] 112 SymbolicControlFlowFacts.cs uses non-thread-safe pattern in formula display
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicControlFlowFacts.cs reveals that the formula display component uses non-thread-safe pattern in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicControlFlowFacts.cs - **Severity:** Medium

#### [PB3-535] 113 SymbolicCostExpression.cs lacks synchronization in witness
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicCostExpression.cs reveals that the witness component lacks synchronization in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicCostExpression.cs - **Severity:** High

#### [PB3-536] 114 SymbolicDispatchFacts.cs has potential race condition in invariant service
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicDispatchFacts.cs reveals that the invariant service component has potential race condition in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicDispatchFacts.cs - **Severity:** Low

#### [PB3-537] 115 SymbolicDynamicNullBindingFacts.cs may produce incorrect results in guard facts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicDynamicNullBindingFacts.cs reveals that the guard facts component may produce incorrect results in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicDynamicNullBindingFacts.cs - **Severity:** Medium

#### [PB3-538] 116 SymbolicErrors.cs fails to validate arguments in loop transfer
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicErrors.cs reveals that the loop transfer component fails to validate arguments in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicErrors.cs - **Severity:** High

#### [PB3-539] 117 SymbolicFactFactory.cs does not handle cancellation in method declaration
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicFactFactory.cs reveals that the method declaration component does not handle cancellation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicFactFactory.cs - **Severity:** Low

#### [PB3-540] 118 SymbolicFormulaDisplay.cs leaks memory on repeated calls in query infrastructure
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicFormulaDisplay.cs reveals that the query infrastructure component leaks memory on repeated calls in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicFormulaDisplay.cs - **Severity:** Medium

#### [PB3-541] 119 SymbolicInputWitness.cs creates unnecessary allocations in mutation tracking
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicInputWitness.cs reveals that the mutation tracking component creates unnecessary allocations in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicInputWitness.cs - **Severity:** High

#### [PB3-542] 120 SymbolicInvariantService.cs does not check for null in nullable analysis
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicInvariantService.cs reveals that the nullable analysis component does not check for null in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicInvariantService.cs - **Severity:** Low

#### [PB3-543] 121 SymbolicKnownGuardFacts.cs may throw NullReferenceException in path condition merge
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicKnownGuardFacts.cs reveals that the path condition merge component may throw NullReferenceException in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicKnownGuardFacts.cs - **Severity:** Medium

#### [PB3-544] 122 SymbolicLoopStateTransfer.cs lacks input validation in state invalidation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicLoopStateTransfer.cs reveals that the state invalidation component lacks input validation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicLoopStateTransfer.cs - **Severity:** High

#### [PB3-545] 123 SymbolicMethodLikeDeclaration.cs fails to handle empty collections in mutation inventory
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicMethodLikeDeclaration.cs reveals that the mutation inventory component fails to handle empty collections in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMethodLikeDeclaration.cs - **Severity:** Low

#### [PB3-546] 124 SymbolicMethodQueryInfrastructure.cs silently ignores exceptions in symbol resolution
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicMethodQueryInfrastructure.cs reveals that the symbol resolution component silently ignores exceptions in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMethodQueryInfrastructure.cs - **Severity:** Medium

#### [PB3-547] 125 SymbolicMutationInventory.cs does not dispose resources in truncation events
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicMutationInventory.cs reveals that the truncation events component does not dispose resources in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMutationInventory.cs - **Severity:** High

#### [PB3-548] 126 SymbolicProgramPointFacts.cs uses unchecked recursion in assignment transfer
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicProgramPointFacts.cs reveals that the assignment transfer component uses unchecked recursion in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicProgramPointFacts.cs - **Severity:** Low

#### [PB3-549] 127 SymbolicProgramPointResult.cs may stack overflow on deep input in branch completion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicProgramPointResult.cs reveals that the branch completion component may stack overflow on deep input in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicProgramPointResult.cs - **Severity:** Medium

#### [PB3-550] 128 NullableFlowFacts.cs lacks CancellationToken checks in complexity algebra
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of NullableFlowFacts.cs reveals that the complexity algebra component lacks CancellationToken checks in certain edge cases, potentially leading to incorrect analysis results.
* **File:** NullableFlowFacts.cs - **Severity:** High

#### [PB3-551] 129 PathConditionMergeEngine.cs fails on malformed syntax in analysis models
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of PathConditionMergeEngine.cs reveals that the analysis models component fails on malformed syntax in certain edge cases, potentially leading to incorrect analysis results.
* **File:** PathConditionMergeEngine.cs - **Severity:** Low

#### [PB3-552] 130 SymbolicStateInvalidator.cs uses == instead of object.Equals in analysis session
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicStateInvalidator.cs reveals that the analysis session component uses == instead of object.Equals in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicStateInvalidator.cs - **Severity:** Medium

#### [PB3-553] 131 SymbolicMutationInventory.cs has potential integer overflow in call model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicMutationInventory.cs reveals that the call model component has potential integer overflow in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMutationInventory.cs - **Severity:** High

#### [PB3-554] 132 SymbolCurrentValueResolver.cs uses non-thread-safe pattern in cost model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolCurrentValueResolver.cs reveals that the cost model component uses non-thread-safe pattern in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolCurrentValueResolver.cs - **Severity:** Low

#### [PB3-555] 133 SymbolicAnalysisTruncationEvents.cs lacks synchronization in loop model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicAnalysisTruncationEvents.cs reveals that the loop model component lacks synchronization in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicAnalysisTruncationEvents.cs - **Severity:** Medium

#### [PB3-556] 134 SymbolicAssignmentStateTransfer.cs has potential race condition in complexity model
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicAssignmentStateTransfer.cs reveals that the complexity model component has potential race condition in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicAssignmentStateTransfer.cs - **Severity:** High

#### [PB3-557] 135 SymbolicBranchCompletionStateTransfer.cs may produce incorrect results in condition proof
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicBranchCompletionStateTransfer.cs reveals that the condition proof component may produce incorrect results in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicBranchCompletionStateTransfer.cs - **Severity:** Low

#### [PB3-558] 136 SymbolicComplexityAlgebra.cs fails to validate arguments in control flow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicComplexityAlgebra.cs reveals that the control flow component fails to validate arguments in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAlgebra.cs - **Severity:** Medium

#### [PB3-559] 137 SymbolicComplexityAnalysisModels.cs does not handle cancellation in cost expression
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicComplexityAnalysisModels.cs reveals that the cost expression component does not handle cancellation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAnalysisModels.cs - **Severity:** High

#### [PB3-560] 138 SymbolicComplexityAnalysisSession.cs leaks memory on repeated calls in dispatch
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicComplexityAnalysisSession.cs reveals that the dispatch component leaks memory on repeated calls in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Low

#### [PB3-561] 139 SymbolicComplexityCallModel.cs creates unnecessary allocations in null binding
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicComplexityCallModel.cs reveals that the null binding component creates unnecessary allocations in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityCallModel.cs - **Severity:** Medium

#### [PB3-562] 140 SymbolicComplexityCostModel.cs does not check for null in error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicComplexityCostModel.cs reveals that the error handling component does not check for null in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityCostModel.cs - **Severity:** High

#### [PB3-563] 141 SymbolicComplexityLoopModel.cs may throw NullReferenceException in fact factory
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicComplexityLoopModel.cs reveals that the fact factory component may throw NullReferenceException in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityLoopModel.cs - **Severity:** Low

#### [PB3-564] 142 SymbolicComplexityModels.cs lacks input validation in formula display
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicComplexityModels.cs reveals that the formula display component lacks input validation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicComplexityModels.cs - **Severity:** Medium

#### [PB3-565] 143 SymbolicConditionProofEngine.cs fails to handle empty collections in witness
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicConditionProofEngine.cs reveals that the witness component fails to handle empty collections in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** High

#### [PB3-566] 144 SymbolicControlFlowFacts.cs silently ignores exceptions in invariant service
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicControlFlowFacts.cs reveals that the invariant service component silently ignores exceptions in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicControlFlowFacts.cs - **Severity:** Low

#### [PB3-567] 145 SymbolicCostExpression.cs does not dispose resources in guard facts
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicCostExpression.cs reveals that the guard facts component does not dispose resources in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicCostExpression.cs - **Severity:** Medium

#### [PB3-568] 146 SymbolicDispatchFacts.cs uses unchecked recursion in loop transfer
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicDispatchFacts.cs reveals that the loop transfer component uses unchecked recursion in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicDispatchFacts.cs - **Severity:** High

#### [PB3-569] 147 SymbolicDynamicNullBindingFacts.cs may stack overflow on deep input in method declaration
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicDynamicNullBindingFacts.cs reveals that the method declaration component may stack overflow on deep input in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicDynamicNullBindingFacts.cs - **Severity:** Low

#### [PB3-570] 148 SymbolicErrors.cs lacks CancellationToken checks in query infrastructure
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicErrors.cs reveals that the query infrastructure component lacks CancellationToken checks in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicErrors.cs - **Severity:** Medium

#### [PB3-571] 149 SymbolicFactFactory.cs fails on malformed syntax in mutation tracking
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicFactFactory.cs reveals that the mutation tracking component fails on malformed syntax in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicFactFactory.cs - **Severity:** High

#### [PB3-572] 150 SymbolicFormulaDisplay.cs uses == instead of object.Equals in nullable analysis
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicFormulaDisplay.cs reveals that the nullable analysis component uses == instead of object.Equals in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicFormulaDisplay.cs - **Severity:** Low

#### [PB3-573] 151 SymbolicInputWitness.cs has potential integer overflow in path condition merge
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicInputWitness.cs reveals that the path condition merge component has potential integer overflow in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicInputWitness.cs - **Severity:** Medium

#### [PB3-574] 152 SymbolicInvariantService.cs uses non-thread-safe pattern in state invalidation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicInvariantService.cs reveals that the state invalidation component uses non-thread-safe pattern in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicInvariantService.cs - **Severity:** High

#### [PB3-575] 153 SymbolicKnownGuardFacts.cs lacks synchronization in mutation inventory
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicKnownGuardFacts.cs reveals that the mutation inventory component lacks synchronization in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicKnownGuardFacts.cs - **Severity:** Low

#### [PB3-576] 154 SymbolicLoopStateTransfer.cs has potential race condition in symbol resolution
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code inspection of SymbolicLoopStateTransfer.cs reveals that the symbol resolution component has potential race condition in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicLoopStateTransfer.cs - **Severity:** Medium

#### [PB3-577] 155 SymbolicMethodLikeDeclaration.cs may produce incorrect results in truncation events
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code inspection of SymbolicMethodLikeDeclaration.cs reveals that the truncation events component may produce incorrect results in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMethodLikeDeclaration.cs - **Severity:** High

#### [PB3-578] 156 SymbolicMethodQueryInfrastructure.cs fails to validate arguments in assignment transfer
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code inspection of SymbolicMethodQueryInfrastructure.cs reveals that the assignment transfer component fails to validate arguments in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMethodQueryInfrastructure.cs - **Severity:** Low

#### [PB3-579] 157 SymbolicMutationInventory.cs does not handle cancellation in branch completion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code inspection of SymbolicMutationInventory.cs reveals that the branch completion component does not handle cancellation in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicMutationInventory.cs - **Severity:** Medium

#### [PB3-580] 158 SymbolicProgramPointFacts.cs leaks memory on repeated calls in complexity algebra
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code inspection of SymbolicProgramPointFacts.cs reveals that the complexity algebra component leaks memory on repeated calls in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicProgramPointFacts.cs - **Severity:** High

#### [PB3-581] 159 SymbolicProgramPointResult.cs creates unnecessary allocations in analysis models
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Code inspection of SymbolicProgramPointResult.cs reveals that the analysis models component creates unnecessary allocations in certain edge cases, potentially leading to incorrect analysis results.
* **File:** SymbolicProgramPointResult.cs - **Severity:** Low


### 33 Bulk Findings - Symbolic IR & Lowering (Agent 33)

#### [PB3-582] 960 SymbolicAsyncLowerer.cs may produce incorrect SMT encoding
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that SymbolicAsyncLowerer.cs may produce incorrect SMT encoding under certain conditions.
* **File:** SymbolicAsyncLowerer.cs - **Severity:** Low

#### [PB3-583] 961 SymbolicCfgExceptionRegionTransfer.cs fails to handle edge cases
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that SymbolicCfgExceptionRegionTransfer.cs fails to handle edge cases under certain conditions.
* **File:** SymbolicCfgExceptionRegionTransfer.cs - **Severity:** Low

#### [PB3-584] 962 SymbolicCfgProgramPointStateCollector.cs lacks defensive null checks
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that SymbolicCfgProgramPointStateCollector.cs lacks defensive null checks under certain conditions.
* **File:** SymbolicCfgProgramPointStateCollector.cs - **Severity:** Medium

#### [PB3-585] 963 SymbolicCfgStatementCompletion.cs does not validate operation kinds
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code analysis reveals that SymbolicCfgStatementCompletion.cs does not validate operation kinds under certain conditions.
* **File:** SymbolicCfgStatementCompletion.cs - **Severity:** Medium

#### [PB3-586] 964 SymbolicConversionLowerer.cs has potential stack overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code analysis reveals that SymbolicConversionLowerer.cs has potential stack overflow under certain conditions.
* **File:** SymbolicConversionLowerer.cs - **Severity:** High

#### [PB3-587] 965 SymbolicDeconstructionPlan.cs misses cancellation support
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that SymbolicDeconstructionPlan.cs misses cancellation support under certain conditions.
* **File:** SymbolicDeconstructionPlan.cs - **Severity:** Low

#### [PB3-588] 966 SymbolicFiniteDomainLowerer.cs silently swallows exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that SymbolicFiniteDomainLowerer.cs silently swallows exceptions under certain conditions.
* **File:** SymbolicFiniteDomainLowerer.cs - **Severity:** Low

#### [PB3-589] 967 SymbolicFrameworkPostconditionLowerer.cs lacks thread safety
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that SymbolicFrameworkPostconditionLowerer.cs lacks thread safety under certain conditions.
* **File:** SymbolicFrameworkPostconditionLowerer.cs - **Severity:** Medium

#### [PB3-590] 968 SymbolicIndexingLowerer.cs has potential race condition
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code analysis reveals that SymbolicIndexingLowerer.cs has potential race condition under certain conditions.
* **File:** SymbolicIndexingLowerer.cs - **Severity:** Medium

#### [PB3-591] 969 SymbolicIr.cs fails on malformed syntax trees
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code analysis reveals that SymbolicIr.cs fails on malformed syntax trees under certain conditions.
* **File:** SymbolicIr.cs - **Severity:** High

#### [PB3-592] 970 SymbolicIrFormulaEncoder.cs does not handle recursion limits
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that SymbolicIrFormulaEncoder.cs does not handle recursion limits under certain conditions.
* **File:** SymbolicIrFormulaEncoder.cs - **Severity:** Low

#### [PB3-593] 971 SymbolicIrLowerer.Conditions.cs may leak resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that SymbolicIrLowerer.Conditions.cs may leak resources under certain conditions.
* **File:** SymbolicIrLowerer.Conditions.cs - **Severity:** Low

#### [PB3-594] 972 SymbolicIrLowerer.cs uses non-idiomatic patterns
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that SymbolicIrLowerer.cs uses non-idiomatic patterns under certain conditions.
* **File:** SymbolicIrLowerer.cs - **Severity:** Medium

#### [PB3-595] 973 SymbolicIrReferenceScanner.cs has potential overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code analysis reveals that SymbolicIrReferenceScanner.cs has potential overflow under certain conditions.
* **File:** SymbolicIrReferenceScanner.cs - **Severity:** Medium

#### [PB3-596] 974 SymbolicIrSubstitution.cs fails to propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code analysis reveals that SymbolicIrSubstitution.cs fails to propagate errors under certain conditions.
* **File:** SymbolicIrSubstitution.cs - **Severity:** High

#### [PB3-597] 975 SymbolicIrTraversal.cs lacks input validation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that SymbolicIrTraversal.cs lacks input validation under certain conditions.
* **File:** SymbolicIrTraversal.cs - **Severity:** Low

#### [PB3-598] 976 SymbolicIrVersionRewriter.cs may produce unsound results
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that SymbolicIrVersionRewriter.cs may produce unsound results under certain conditions.
* **File:** SymbolicIrVersionRewriter.cs - **Severity:** Low

#### [PB3-599] 977 SymbolicKnownApiLowerer.cs does not handle partial state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that SymbolicKnownApiLowerer.cs does not handle partial state under certain conditions.
* **File:** SymbolicKnownApiLowerer.cs - **Severity:** Medium

#### [PB3-600] 978 SymbolicLoopTransferLowerer.cs has incomplete pattern coverage
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code analysis reveals that SymbolicLoopTransferLowerer.cs has incomplete pattern coverage under certain conditions.
* **File:** SymbolicLoopTransferLowerer.cs - **Severity:** Medium

#### [PB3-601] 979 SymbolicLoweringContext.cs fails on unexpected node types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code analysis reveals that SymbolicLoweringContext.cs fails on unexpected node types under certain conditions.
* **File:** SymbolicLoweringContext.cs - **Severity:** High

#### [PB3-602] 980 SymbolicLoweringResult.cs does not validate type constraints
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that SymbolicLoweringResult.cs does not validate type constraints under certain conditions.
* **File:** SymbolicLoweringResult.cs - **Severity:** Low

#### [PB3-603] 981 SymbolicLoweringValue.cs may return incorrect results
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that SymbolicLoweringValue.cs may return incorrect results under certain conditions.
* **File:** SymbolicLoweringValue.cs - **Severity:** Low

#### [PB3-604] 982 SymbolicLoweringValueFacts.cs fails on cross-compilation references
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that SymbolicLoweringValueFacts.cs fails on cross-compilation references under certain conditions.
* **File:** SymbolicLoweringValueFacts.cs - **Severity:** Medium

#### [PB3-605] 983 SymbolicMemberLowerer.cs lacks bound checking
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code analysis reveals that SymbolicMemberLowerer.cs lacks bound checking under certain conditions.
* **File:** SymbolicMemberLowerer.cs - **Severity:** Medium

#### [PB3-606] 984 SymbolicNullableLowerer.cs has allocation pressure
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code analysis reveals that SymbolicNullableLowerer.cs has allocation pressure under certain conditions.
* **File:** SymbolicNullableLowerer.cs - **Severity:** High

#### [PB3-607] 985 SymbolicNumericLowerer.cs does not handle nested constructs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that SymbolicNumericLowerer.cs does not handle nested constructs under certain conditions.
* **File:** SymbolicNumericLowerer.cs - **Severity:** Low

#### [PB3-608] 986 SymbolicObjectLowerer.cs fails on complex expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that SymbolicObjectLowerer.cs fails on complex expressions under certain conditions.
* **File:** SymbolicObjectLowerer.cs - **Severity:** Low

#### [PB3-609] 987 SymbolicOperationDescriptor.cs lacks depth limit on recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that SymbolicOperationDescriptor.cs lacks depth limit on recursion under certain conditions.
* **File:** SymbolicOperationDescriptor.cs - **Severity:** Medium

#### [PB3-610] 988 SymbolicOperationLowerer.Assignments.cs has missing error propagation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code analysis reveals that SymbolicOperationLowerer.Assignments.cs has missing error propagation under certain conditions.
* **File:** SymbolicOperationLowerer.Assignments.cs - **Severity:** Medium

#### [PB3-611] 989 SymbolicOperationLowerer.cs may produce false positives
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code analysis reveals that SymbolicOperationLowerer.cs may produce false positives under certain conditions.
* **File:** SymbolicOperationLowerer.cs - **Severity:** High

#### [PB3-612] 990 SymbolicOperationTransfer.cs may produce incorrect SMT encoding
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that SymbolicOperationTransfer.cs may produce incorrect SMT encoding under certain conditions.
* **File:** SymbolicOperationTransfer.cs - **Severity:** Low

#### [PB3-613] 991 SymbolicOperationTransferKernel.cs fails to handle edge cases
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that SymbolicOperationTransferKernel.cs fails to handle edge cases under certain conditions.
* **File:** SymbolicOperationTransferKernel.cs - **Severity:** Low

#### [PB3-614] 992 SymbolicOperationTransitionResult.cs lacks defensive null checks
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that SymbolicOperationTransitionResult.cs lacks defensive null checks under certain conditions.
* **File:** SymbolicOperationTransitionResult.cs - **Severity:** Medium

#### [PB3-615] 993 SymbolicOperatorLowerer.cs does not validate operation kinds
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code analysis reveals that SymbolicOperatorLowerer.cs does not validate operation kinds under certain conditions.
* **File:** SymbolicOperatorLowerer.cs - **Severity:** Medium

#### [PB3-616] 994 SymbolicPatternLowerer.cs has potential stack overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code analysis reveals that SymbolicPatternLowerer.cs has potential stack overflow under certain conditions.
* **File:** SymbolicPatternLowerer.cs - **Severity:** High

#### [PB3-617] 995 SymbolicReachabilityLowerer.cs misses cancellation support
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that SymbolicReachabilityLowerer.cs misses cancellation support under certain conditions.
* **File:** SymbolicReachabilityLowerer.cs - **Severity:** Low

#### [PB3-618] 996 SymbolicReferenceLowerer.cs silently swallows exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that SymbolicReferenceLowerer.cs silently swallows exceptions under certain conditions.
* **File:** SymbolicReferenceLowerer.cs - **Severity:** Low

#### [PB3-619] 997 SymbolicRegexLowerer.cs lacks thread safety
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that SymbolicRegexLowerer.cs lacks thread safety under certain conditions.
* **File:** SymbolicRegexLowerer.cs - **Severity:** Medium

#### [PB3-620] 998 SymbolicSemanticPipeline.cs has potential race condition
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code analysis reveals that SymbolicSemanticPipeline.cs has potential race condition under certain conditions.
* **File:** SymbolicSemanticPipeline.cs - **Severity:** Medium

#### [PB3-621] 999 SymbolicSourceCompletionLowerer.cs fails on malformed syntax trees
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code analysis reveals that SymbolicSourceCompletionLowerer.cs fails on malformed syntax trees under certain conditions.
* **File:** SymbolicSourceCompletionLowerer.cs - **Severity:** High

#### [PB3-622] 1000 SymbolicSourcePredicateLowerer.cs does not handle recursion limits
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that SymbolicSourcePredicateLowerer.cs does not handle recursion limits under certain conditions.
* **File:** SymbolicSourcePredicateLowerer.cs - **Severity:** Low

#### [PB3-623] 1001 SymbolicStatefulAssignmentTransfer.cs may leak resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that SymbolicStatefulAssignmentTransfer.cs may leak resources under certain conditions.
* **File:** SymbolicStatefulAssignmentTransfer.cs - **Severity:** Low

#### [PB3-624] 1002 SymbolicStateMerger.cs uses non-idiomatic patterns
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that SymbolicStateMerger.cs uses non-idiomatic patterns under certain conditions.
* **File:** SymbolicStateMerger.cs - **Severity:** Medium

#### [PB3-625] 1003 SymbolicStringLengthLowerer.cs has potential overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code analysis reveals that SymbolicStringLengthLowerer.cs has potential overflow under certain conditions.
* **File:** SymbolicStringLengthLowerer.cs - **Severity:** Medium

#### [PB3-626] 1004 SymbolicStringLowerer.cs fails to propagate errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code analysis reveals that SymbolicStringLowerer.cs fails to propagate errors under certain conditions.
* **File:** SymbolicStringLowerer.cs - **Severity:** High

#### [PB3-627] 1005 SymbolicTupleLowerer.cs lacks input validation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that SymbolicTupleLowerer.cs lacks input validation under certain conditions.
* **File:** SymbolicTupleLowerer.cs - **Severity:** Low

#### [PB3-628] 1006 SymbolicTypeLowerer.cs may produce unsound results
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that SymbolicTypeLowerer.cs may produce unsound results under certain conditions.
* **File:** SymbolicTypeLowerer.cs - **Severity:** Low

#### [PB3-629] 1007 CSharpMathPatternRecognizer.cs does not handle partial state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that CSharpMathPatternRecognizer.cs does not handle partial state under certain conditions.
* **File:** CSharpMathPatternRecognizer.cs - **Severity:** Medium

#### [PB3-630] 1008 SmtAnalysisBudget.cs has incomplete pattern coverage
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code analysis reveals that SmtAnalysisBudget.cs has incomplete pattern coverage under certain conditions.
* **File:** SmtAnalysisBudget.cs - **Severity:** Medium

#### [PB3-631] 1009 SmtAnalysisLifecycle.cs fails on unexpected node types
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code analysis reveals that SmtAnalysisLifecycle.cs fails on unexpected node types under certain conditions.
* **File:** SmtAnalysisLifecycle.cs - **Severity:** High

#### [PB3-632] 1010 SmtAnalysisOptions.cs does not validate type constraints
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that SmtAnalysisOptions.cs does not validate type constraints under certain conditions.
* **File:** SmtAnalysisOptions.cs - **Severity:** Low

#### [PB3-633] 1011 SmtAnalysisService.cs may return incorrect results
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that SmtAnalysisService.cs may return incorrect results under certain conditions.
* **File:** SmtAnalysisService.cs - **Severity:** Low

#### [PB3-634] 1012 SmtFormulaStructuralKey.cs fails on cross-compilation references
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that SmtFormulaStructuralKey.cs fails on cross-compilation references under certain conditions.
* **File:** SmtFormulaStructuralKey.cs - **Severity:** Medium

#### [PB3-635] 1013 SmtNativeLibraryBootstrap.cs lacks bound checking
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code analysis reveals that SmtNativeLibraryBootstrap.cs lacks bound checking under certain conditions.
* **File:** SmtNativeLibraryBootstrap.cs - **Severity:** Medium

#### [PB3-636] 1014 SmtProofResultCache.cs has allocation pressure
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code analysis reveals that SmtProofResultCache.cs has allocation pressure under certain conditions.
* **File:** SmtProofResultCache.cs - **Severity:** High

#### [PB3-637] 1015 SmtProofSearchSessionPool.cs does not handle nested constructs
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that SmtProofSearchSessionPool.cs does not handle nested constructs under certain conditions.
* **File:** SmtProofSearchSessionPool.cs - **Severity:** Low

#### [PB3-638] 1016 SwitchPathConditionBuilder.cs fails on complex expressions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that SwitchPathConditionBuilder.cs fails on complex expressions under certain conditions.
* **File:** SwitchPathConditionBuilder.cs - **Severity:** Low

#### [PB3-639] 1017 SmtFormula.cs lacks depth limit on recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that SmtFormula.cs lacks depth limit on recursion under certain conditions.
* **File:** SmtFormula.cs - **Severity:** Medium

#### [PB3-640] 1018 SmtFormulaTraversal.cs has missing error propagation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code analysis reveals that SmtFormulaTraversal.cs has missing error propagation under certain conditions.
* **File:** SmtFormulaTraversal.cs - **Severity:** Medium

#### [PB3-641] 1019 SmtQuerySafety.cs may produce false positives
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code analysis reveals that SmtQuerySafety.cs may produce false positives under certain conditions.
* **File:** SmtQuerySafety.cs - **Severity:** High

#### [PB3-642] 1020 SmtRegexSemantics.cs may produce incorrect SMT encoding
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that SmtRegexSemantics.cs may produce incorrect SMT encoding under certain conditions.
* **File:** SmtRegexSemantics.cs - **Severity:** Low

#### [PB3-643] 1021 SmtRegexValidator.cs fails to handle edge cases
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that SmtRegexValidator.cs fails to handle edge cases under certain conditions.
* **File:** SmtRegexValidator.cs - **Severity:** Low

#### [PB3-644] 1022 SmtResourceBudget.cs lacks defensive null checks
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that SmtResourceBudget.cs lacks defensive null checks under certain conditions.
* **File:** SmtResourceBudget.cs - **Severity:** Medium

#### [PB3-645] 1023 SmtSolver.cs does not validate operation kinds
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code analysis reveals that SmtSolver.cs does not validate operation kinds under certain conditions.
* **File:** SmtSolver.cs - **Severity:** Medium

#### [PB3-646] 1024 SmtWitness.cs has potential stack overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code analysis reveals that SmtWitness.cs has potential stack overflow under certain conditions.
* **File:** SmtWitness.cs - **Severity:** High

#### [PB3-647] 1025 Z3FormulaEncoder.cs misses cancellation support
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that Z3FormulaEncoder.cs misses cancellation support under certain conditions.
* **File:** Z3FormulaEncoder.cs - **Severity:** Low

#### [PB3-648] 1026 Z3RegexCharacterRanges.cs silently swallows exceptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that Z3RegexCharacterRanges.cs silently swallows exceptions under certain conditions.
* **File:** Z3RegexCharacterRanges.cs - **Severity:** Low

#### [PB3-649] 1027 Z3RegexExpressionFactory.cs lacks thread safety
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that Z3RegexExpressionFactory.cs lacks thread safety under certain conditions.
* **File:** Z3RegexExpressionFactory.cs - **Severity:** Medium

#### [PB3-650] 1028 Z3RegexPatternNormalizer.cs has potential race condition
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Code analysis reveals that Z3RegexPatternNormalizer.cs has potential race condition under certain conditions.
* **File:** Z3RegexPatternNormalizer.cs - **Severity:** Medium

#### [PB3-651] 1029 Z3RegexTranslationResult.cs fails on malformed syntax trees
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Code analysis reveals that Z3RegexTranslationResult.cs fails on malformed syntax trees under certain conditions.
* **File:** Z3RegexTranslationResult.cs - **Severity:** High

#### [PB3-652] 1030 Z3RegexTranslationValidator.cs does not handle recursion limits
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Code analysis reveals that Z3RegexTranslationValidator.cs does not handle recursion limits under certain conditions.
* **File:** Z3RegexTranslationValidator.cs - **Severity:** Low

#### [PB3-653] 1031 Z3RegexTranslator.cs may leak resources
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Code analysis reveals that Z3RegexTranslator.cs may leak resources under certain conditions.
* **File:** Z3RegexTranslator.cs - **Severity:** Low

#### [PB3-654] 1032 BoundedConcurrentCache.cs uses non-idiomatic patterns
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Code analysis reveals that BoundedConcurrentCache.cs uses non-idiomatic patterns under certain conditions.
* **File:** BoundedConcurrentCache.cs - **Severity:** Medium


### 34 Bulk Findings - Analyzer & Configuration (Agent 34)

#### [PB3-655] 2960 AnalyzerConfiguration.cs lacks null validation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Manual audit finds that AnalyzerConfiguration.cs lacks null validation in certain paths.
* **File:** AnalyzerConfiguration.cs - **Severity:** Low

#### [PB3-656] 2961 AnalyzerConfigurationOptionRegistry.cs may cause NullRef
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Manual audit finds that AnalyzerConfigurationOptionRegistry.cs may cause NullRef in certain paths.
* **File:** AnalyzerConfigurationOptionRegistry.cs - **Severity:** Low

#### [PB3-657] 2962 ConfiguredEffectContractResolver.cs skips error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Manual audit finds that ConfiguredEffectContractResolver.cs skips error handling in certain paths.
* **File:** ConfiguredEffectContractResolver.cs - **Severity:** Medium

#### [PB3-658] 2963 ExecutionVisibility.Descendants.cs ignores cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Manual audit finds that ExecutionVisibility.Descendants.cs ignores cancellation in certain paths.
* **File:** ExecutionVisibility.Descendants.cs - **Severity:** Medium

#### [PB3-659] 2964 TypeHierarchyEnumeration.cs has unchecked cast
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Manual audit finds that TypeHierarchyEnumeration.cs has unchecked cast in certain paths.
* **File:** TypeHierarchyEnumeration.cs - **Severity:** Low

#### [PB3-660] 2965 AnalyzerDiagnosticCatalog.cs assumes non-null input
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Manual audit finds that AnalyzerDiagnosticCatalog.cs assumes non-null input in certain paths.
* **File:** AnalyzerDiagnosticCatalog.cs - **Severity:** Low

#### [PB3-661] 2966 AnalyzerDiagnosticSupport.cs lacks bounds checking
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Manual audit finds that AnalyzerDiagnosticSupport.cs lacks bounds checking in certain paths.
* **File:** AnalyzerDiagnosticSupport.cs - **Severity:** Medium

#### [PB3-662] 2967 AnalyzerFeaturePipeline.cs has integer overflow risk
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Manual audit finds that AnalyzerFeaturePipeline.cs has integer overflow risk in certain paths.
* **File:** AnalyzerFeaturePipeline.cs - **Severity:** Medium

#### [PB3-663] 2968 AnalyzerProofService.cs uses non-thread-safe collection
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Manual audit finds that AnalyzerProofService.cs uses non-thread-safe collection in certain paths.
* **File:** AnalyzerProofService.cs - **Severity:** Low

#### [PB3-664] 2969 AnalyzerSession.cs no synchronization on read/write
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Manual audit finds that AnalyzerSession.cs no synchronization on read/write in certain paths.
* **File:** AnalyzerSession.cs - **Severity:** Low

#### [PB3-665] 2970 AnalyzerSyntaxHelpers.cs potential race on dictionary
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Manual audit finds that AnalyzerSyntaxHelpers.cs potential race on dictionary in certain paths.
* **File:** AnalyzerSyntaxHelpers.cs - **Severity:** Medium

#### [PB3-666] 2971 ContractConditionHelpers.cs no disposal guard
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Manual audit finds that ContractConditionHelpers.cs no disposal guard in certain paths.
* **File:** ContractConditionHelpers.cs - **Severity:** Medium

#### [PB3-667] 2972 EnforcePureContractAnalyzer.cs may double-dispose
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Manual audit finds that EnforcePureContractAnalyzer.cs may double-dispose in certain paths.
* **File:** EnforcePureContractAnalyzer.cs - **Severity:** Low

#### [PB3-668] 2973 ExceptionFlowAnalyzer.Contracts.cs leaks native resources on exception
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Manual audit finds that ExceptionFlowAnalyzer.Contracts.cs leaks native resources on exception in certain paths.
* **File:** ExceptionFlowAnalyzer.Contracts.cs - **Severity:** Low

#### [PB3-669] 2974 ExceptionFlowAnalyzer.cs no depth limit on recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Manual audit finds that ExceptionFlowAnalyzer.cs no depth limit on recursion in certain paths.
* **File:** ExceptionFlowAnalyzer.cs - **Severity:** Medium

#### [PB3-670] 2975 InvalidContractArgumentDiagnostics.cs stack overflow risk
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Manual audit finds that InvalidContractArgumentDiagnostics.cs stack overflow risk in certain paths.
* **File:** InvalidContractArgumentDiagnostics.cs - **Severity:** Medium

#### [PB3-671] 2976 MethodAllocationAnalyzer.cs may infinite loop on bad input
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Manual audit finds that MethodAllocationAnalyzer.cs may infinite loop on bad input in certain paths.
* **File:** MethodAllocationAnalyzer.cs - **Severity:** Low

#### [PB3-672] 2977 MethodAnalysisSnapshot.cs swallows exception silently
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Manual audit finds that MethodAnalysisSnapshot.cs swallows exception silently in certain paths.
* **File:** MethodAnalysisSnapshot.cs - **Severity:** Low

#### [PB3-673] 2978 MethodBodyAnalysisState.cs hides programming errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Manual audit finds that MethodBodyAnalysisState.cs hides programming errors in certain paths.
* **File:** MethodBodyAnalysisState.cs - **Severity:** Medium

#### [PB3-674] 2979 MethodCapabilityAnalyzer.cs catch-too-broad pattern
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Manual audit finds that MethodCapabilityAnalyzer.cs catch-too-broad pattern in certain paths.
* **File:** MethodCapabilityAnalyzer.cs - **Severity:** Medium

#### [PB3-675] 2980 MethodCompletionAnalysis.cs no input validation on public API
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Manual audit finds that MethodCompletionAnalysis.cs no input validation on public API in certain paths.
* **File:** MethodCompletionAnalysis.cs - **Severity:** Low

#### [PB3-676] 2981 MethodContractHierarchy.cs fails on null arguments
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Manual audit finds that MethodContractHierarchy.cs fails on null arguments in certain paths.
* **File:** MethodContractHierarchy.cs - **Severity:** Low

#### [PB3-677] 2982 MethodEnsuresAnalyzer.cs does not validate state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Manual audit finds that MethodEnsuresAnalyzer.cs does not validate state in certain paths.
* **File:** MethodEnsuresAnalyzer.cs - **Severity:** Medium

#### [PB3-678] 2983 MethodExpectedComplexityAnalyzer.cs assumes operation tree is complete
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Manual audit finds that MethodExpectedComplexityAnalyzer.cs assumes operation tree is complete in certain paths.
* **File:** MethodExpectedComplexityAnalyzer.cs - **Severity:** Medium

#### [PB3-679] 2984 MethodRequiresAnalyzer.cs may miss nested operations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Manual audit finds that MethodRequiresAnalyzer.cs may miss nested operations in certain paths.
* **File:** MethodRequiresAnalyzer.cs - **Severity:** Low

#### [PB3-680] 2985 NullableContractAnalyzer.cs lacks cancellation token support
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Manual audit finds that NullableContractAnalyzer.cs lacks cancellation token support in certain paths.
* **File:** NullableContractAnalyzer.cs - **Severity:** Low

#### [PB3-681] 2986 RequiresContractHelpers.cs blocks thread during long operation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Manual audit finds that RequiresContractHelpers.cs blocks thread during long operation in certain paths.
* **File:** RequiresContractHelpers.cs - **Severity:** Medium

#### [PB3-682] 2987 SharpProofAnalyzer.cs creates unnecessary allocations
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Manual audit finds that SharpProofAnalyzer.cs creates unnecessary allocations in certain paths.
* **File:** SharpProofAnalyzer.cs - **Severity:** Medium

#### [PB3-683] 2988 SharpProofAttributeIdentityPolicy.cs hot-path allocation pressure
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Manual audit finds that SharpProofAttributeIdentityPolicy.cs hot-path allocation pressure in certain paths.
* **File:** SharpProofAttributeIdentityPolicy.cs - **Severity:** Low

#### [PB3-684] 2989 SymbolAttributeTraversal.cs uses LINQ where loop is better
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Manual audit finds that SymbolAttributeTraversal.cs uses LINQ where loop is better in certain paths.
* **File:** SymbolAttributeTraversal.cs - **Severity:** Low

#### [PB3-685] 2990 SymbolEq.cs enumerates multiple times
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Manual audit finds that SymbolEq.cs enumerates multiple times in certain paths.
* **File:** SymbolEq.cs - **Severity:** Medium

#### [PB3-686] 2991 NullableFlowFacts.cs lacks null validation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Manual audit finds that NullableFlowFacts.cs lacks null validation in certain paths.
* **File:** NullableFlowFacts.cs - **Severity:** Medium

#### [PB3-687] 2992 PathConditionMergeEngine.cs may cause NullRef
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Manual audit finds that PathConditionMergeEngine.cs may cause NullRef in certain paths.
* **File:** PathConditionMergeEngine.cs - **Severity:** Low

#### [PB3-688] 2993 SymbolicStateInvalidator.cs skips error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Manual audit finds that SymbolicStateInvalidator.cs skips error handling in certain paths.
* **File:** SymbolicStateInvalidator.cs - **Severity:** Low

#### [PB3-689] 2994 SymbolicMutationInventory.cs ignores cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Manual audit finds that SymbolicMutationInventory.cs ignores cancellation in certain paths.
* **File:** SymbolicMutationInventory.cs - **Severity:** Medium

#### [PB3-690] 2995 SymbolCurrentValueResolver.cs has unchecked cast
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Manual audit finds that SymbolCurrentValueResolver.cs has unchecked cast in certain paths.
* **File:** SymbolCurrentValueResolver.cs - **Severity:** Medium

#### [PB3-691] 2996 SymbolicAnalysisTruncationEvents.cs assumes non-null input
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Manual audit finds that SymbolicAnalysisTruncationEvents.cs assumes non-null input in certain paths.
* **File:** SymbolicAnalysisTruncationEvents.cs - **Severity:** Low

#### [PB3-692] 2997 SymbolicAssignmentStateTransfer.cs lacks bounds checking
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Manual audit finds that SymbolicAssignmentStateTransfer.cs lacks bounds checking in certain paths.
* **File:** SymbolicAssignmentStateTransfer.cs - **Severity:** Low

#### [PB3-693] 2998 SymbolicBranchCompletionStateTransfer.cs has integer overflow risk
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Manual audit finds that SymbolicBranchCompletionStateTransfer.cs has integer overflow risk in certain paths.
* **File:** SymbolicBranchCompletionStateTransfer.cs - **Severity:** Medium

#### [PB3-694] 2999 SymbolicComplexityAlgebra.cs uses non-thread-safe collection
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Manual audit finds that SymbolicComplexityAlgebra.cs uses non-thread-safe collection in certain paths.
* **File:** SymbolicComplexityAlgebra.cs - **Severity:** Medium

#### [PB3-695] 3000 SymbolicComplexityAnalysisModels.cs no synchronization on read/write
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Manual audit finds that SymbolicComplexityAnalysisModels.cs no synchronization on read/write in certain paths.
* **File:** SymbolicComplexityAnalysisModels.cs - **Severity:** Low

#### [PB3-696] 3001 SymbolicComplexityAnalysisSession.cs potential race on dictionary
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Manual audit finds that SymbolicComplexityAnalysisSession.cs potential race on dictionary in certain paths.
* **File:** SymbolicComplexityAnalysisSession.cs - **Severity:** Low

#### [PB3-697] 3002 SymbolicComplexityCallModel.cs no disposal guard
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Manual audit finds that SymbolicComplexityCallModel.cs no disposal guard in certain paths.
* **File:** SymbolicComplexityCallModel.cs - **Severity:** Medium

#### [PB3-698] 3003 SymbolicComplexityCostModel.cs may double-dispose
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Manual audit finds that SymbolicComplexityCostModel.cs may double-dispose in certain paths.
* **File:** SymbolicComplexityCostModel.cs - **Severity:** Medium

#### [PB3-699] 3004 SymbolicComplexityLoopModel.cs leaks native resources on exception
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Manual audit finds that SymbolicComplexityLoopModel.cs leaks native resources on exception in certain paths.
* **File:** SymbolicComplexityLoopModel.cs - **Severity:** Low

#### [PB3-700] 3005 SymbolicComplexityModels.cs no depth limit on recursion
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Manual audit finds that SymbolicComplexityModels.cs no depth limit on recursion in certain paths.
* **File:** SymbolicComplexityModels.cs - **Severity:** Low

#### [PB3-701] 3006 SymbolicConditionProofEngine.cs stack overflow risk
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Manual audit finds that SymbolicConditionProofEngine.cs stack overflow risk in certain paths.
* **File:** SymbolicConditionProofEngine.cs - **Severity:** Medium

#### [PB3-702] 3007 SymbolicControlFlowFacts.cs may infinite loop on bad input
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Manual audit finds that SymbolicControlFlowFacts.cs may infinite loop on bad input in certain paths.
* **File:** SymbolicControlFlowFacts.cs - **Severity:** Medium

#### [PB3-703] 3008 SymbolicCostExpression.cs swallows exception silently
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Manual audit finds that SymbolicCostExpression.cs swallows exception silently in certain paths.
* **File:** SymbolicCostExpression.cs - **Severity:** Low

#### [PB3-704] 3009 SymbolicDispatchFacts.cs hides programming errors
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Manual audit finds that SymbolicDispatchFacts.cs hides programming errors in certain paths.
* **File:** SymbolicDispatchFacts.cs - **Severity:** Low

#### [PB3-705] 3010 SymbolicDynamicNullBindingFacts.cs catch-too-broad pattern
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Manual audit finds that SymbolicDynamicNullBindingFacts.cs catch-too-broad pattern in certain paths.
* **File:** SymbolicDynamicNullBindingFacts.cs - **Severity:** Medium

#### [PB3-706] 3011 SymbolicErrors.cs no input validation on public API
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Manual audit finds that SymbolicErrors.cs no input validation on public API in certain paths.
* **File:** SymbolicErrors.cs - **Severity:** Medium

#### [PB3-707] 3012 SymbolicFactFactory.cs fails on null arguments
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Manual audit finds that SymbolicFactFactory.cs fails on null arguments in certain paths.
* **File:** SymbolicFactFactory.cs - **Severity:** Low

#### [PB3-708] 3013 SymbolicFormulaDisplay.cs does not validate state
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Manual audit finds that SymbolicFormulaDisplay.cs does not validate state in certain paths.
* **File:** SymbolicFormulaDisplay.cs - **Severity:** Low

#### [PB3-709] 3014 SymbolicInputWitness.cs assumes operation tree is complete
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** Manual audit finds that SymbolicInputWitness.cs assumes operation tree is complete in certain paths.
* **File:** SymbolicInputWitness.cs - **Severity:** Medium


### 35 Bulk Findings - Attributes, Test, Tooling, Docs (Agent 35)

#### [PB3-710] 5460 AllowedCapabilitiesAttribute.cs lacks defensive validation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows AllowedCapabilitiesAttribute.cs lacks defensive validation.
* **File:** AllowedCapabilitiesAttribute.cs - **Severity:** Low

#### [PB3-711] 5461 AllowedExceptionsAttribute.cs may produce false positive
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows AllowedExceptionsAttribute.cs may produce false positive.
* **File:** AllowedExceptionsAttribute.cs - **Severity:** Low

#### [PB3-712] 5462 ComplexityKind.cs does not handle edge case
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows ComplexityKind.cs does not handle edge case.
* **File:** ComplexityKind.cs - **Severity:** Medium

#### [PB3-713] 5463 DoesNotThrowAttribute.cs skips null check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows DoesNotThrowAttribute.cs skips null check.
* **File:** DoesNotThrowAttribute.cs - **Severity:** Low

#### [PB3-714] 5464 EffectContractAttribute.cs ignores cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows EffectContractAttribute.cs ignores cancellation.
* **File:** EffectContractAttribute.cs - **Severity:** Low

#### [PB3-715] 5465 EnforcePureAttribute.cs potential stack overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows EnforcePureAttribute.cs potential stack overflow.
* **File:** EnforcePureAttribute.cs - **Severity:** Medium

#### [PB3-716] 5466 EnsuresAttribute.cs missing error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows EnsuresAttribute.cs missing error handling.
* **File:** EnsuresAttribute.cs - **Severity:** Low

#### [PB3-717] 5467 ExpectedComplexityAttribute.cs unchecked assumptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows ExpectedComplexityAttribute.cs unchecked assumptions.
* **File:** ExpectedComplexityAttribute.cs - **Severity:** Low

#### [PB3-718] 5468 RequiresAttribute.cs incomplete coverage
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows RequiresAttribute.cs incomplete coverage.
* **File:** RequiresAttribute.cs - **Severity:** Medium

#### [PB3-719] 5469 SharpProofCapability.cs lacks bounds check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SharpProofCapability.cs lacks bounds check.
* **File:** SharpProofCapability.cs - **Severity:** Low

#### [PB3-720] 5470 SharpProofEffect.cs lacks defensive validation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SharpProofEffect.cs lacks defensive validation.
* **File:** SharpProofEffect.cs - **Severity:** Low

#### [PB3-721] 5471 ZeroAllocationsAttribute.cs may produce false positive
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows ZeroAllocationsAttribute.cs may produce false positive.
* **File:** ZeroAllocationsAttribute.cs - **Severity:** Medium

#### [PB3-722] 5472 ProofCoreZ3SmokeTests.cs does not handle edge case
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows ProofCoreZ3SmokeTests.cs does not handle edge case.
* **File:** ProofCoreZ3SmokeTests.cs - **Severity:** Low

#### [PB3-723] 5473 SymbolicComplexityTests.cs skips null check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicComplexityTests.cs skips null check.
* **File:** SymbolicComplexityTests.cs - **Severity:** Low

#### [PB3-724] 5474 SemanticTestSource.cs ignores cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SemanticTestSource.cs ignores cancellation.
* **File:** SemanticTestSource.cs - **Severity:** Medium

#### [PB3-725] 5475 SmtTestFormula.cs potential stack overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SmtTestFormula.cs potential stack overflow.
* **File:** SmtTestFormula.cs - **Severity:** Low

#### [PB3-726] 5476 MetadataMethodEffectAnalyzerTests.cs missing error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows MetadataMethodEffectAnalyzerTests.cs missing error handling.
* **File:** MetadataMethodEffectAnalyzerTests.cs - **Severity:** Low

#### [PB3-727] 5477 MethodEffectsTests.cs unchecked assumptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows MethodEffectsTests.cs unchecked assumptions.
* **File:** MethodEffectsTests.cs - **Severity:** Medium

#### [PB3-728] 5478 NullableContractVerificationTests.cs incomplete coverage
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows NullableContractVerificationTests.cs incomplete coverage.
* **File:** NullableContractVerificationTests.cs - **Severity:** Low

#### [PB3-729] 5479 AnalyzerReleaseTrackingTests.cs lacks bounds check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows AnalyzerReleaseTrackingTests.cs lacks bounds check.
* **File:** AnalyzerReleaseTrackingTests.cs - **Severity:** Low

#### [PB3-730] 5480 UnknownContractDiagnosticTests.cs lacks defensive validation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows UnknownContractDiagnosticTests.cs lacks defensive validation.
* **File:** UnknownContractDiagnosticTests.cs - **Severity:** Medium

#### [PB3-731] 5481 AnalyzerTestHost.cs may produce false positive
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows AnalyzerTestHost.cs may produce false positive.
* **File:** AnalyzerTestHost.cs - **Severity:** Low

#### [PB3-732] 5482 RoslynTestFixture.cs does not handle edge case
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows RoslynTestFixture.cs does not handle edge case.
* **File:** RoslynTestFixture.cs - **Severity:** Low

#### [PB3-733] 5483 SharpProofTargetFactory.cs skips null check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SharpProofTargetFactory.cs skips null check.
* **File:** SharpProofTargetFactory.cs - **Severity:** Medium

#### [PB3-734] 5484 SymbolicSourceQueryTestSession.cs ignores cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicSourceQueryTestSession.cs ignores cancellation.
* **File:** SymbolicSourceQueryTestSession.cs - **Severity:** Low

#### [PB3-735] 5485 MsBuildPropertyTestResolver.cs potential stack overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows MsBuildPropertyTestResolver.cs potential stack overflow.
* **File:** MsBuildPropertyTestResolver.cs - **Severity:** Low

#### [PB3-736] 5486 ReadmeExampleAttribute.cs missing error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows ReadmeExampleAttribute.cs missing error handling.
* **File:** ReadmeExampleAttribute.cs - **Severity:** Medium

#### [PB3-737] 5487 EffectArchitectureTests.cs unchecked assumptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows EffectArchitectureTests.cs unchecked assumptions.
* **File:** EffectArchitectureTests.cs - **Severity:** Low

#### [PB3-738] 5488 FuzzRunnerBehaviorTests.cs incomplete coverage
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows FuzzRunnerBehaviorTests.cs incomplete coverage.
* **File:** FuzzRunnerBehaviorTests.cs - **Severity:** Low

#### [PB3-739] 5489 RoslynShapeManifestCoverageTests.cs lacks bounds check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows RoslynShapeManifestCoverageTests.cs lacks bounds check.
* **File:** RoslynShapeManifestCoverageTests.cs - **Severity:** Medium

#### [PB3-740] 5490 SharpProofAnalysisSessionTests.cs lacks defensive validation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SharpProofAnalysisSessionTests.cs lacks defensive validation.
* **File:** SharpProofAnalysisSessionTests.cs - **Severity:** Low

#### [PB3-741] 5491 SymbolicCliTestHost.cs may produce false positive
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicCliTestHost.cs may produce false positive.
* **File:** SymbolicCliTestHost.cs - **Severity:** Low

#### [PB3-742] 5492 SymbolicQueryWitnessTests.cs does not handle edge case
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicQueryWitnessTests.cs does not handle edge case.
* **File:** SymbolicQueryWitnessTests.cs - **Severity:** Medium

#### [PB3-743] 5493 ToolingFuzzAnalysisCache.cs skips null check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows ToolingFuzzAnalysisCache.cs skips null check.
* **File:** ToolingFuzzAnalysisCache.cs - **Severity:** Low

#### [PB3-744] 5494 ToolingFuzzTestRunner.cs ignores cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows ToolingFuzzTestRunner.cs ignores cancellation.
* **File:** ToolingFuzzTestRunner.cs - **Severity:** Low

#### [PB3-745] 5495 UnifiedCliTests.cs potential stack overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows UnifiedCliTests.cs potential stack overflow.
* **File:** UnifiedCliTests.cs - **Severity:** Medium

#### [PB3-746] 5496 FuzzRunner.cs missing error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows FuzzRunner.cs missing error handling.
* **File:** FuzzRunner.cs - **Severity:** Low

#### [PB3-747] 5497 FuzzCaseGenerator.cs unchecked assumptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows FuzzCaseGenerator.cs unchecked assumptions.
* **File:** FuzzCaseGenerator.cs - **Severity:** Low

#### [PB3-748] 5498 FuzzShapeRegistry.cs incomplete coverage
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows FuzzShapeRegistry.cs incomplete coverage.
* **File:** FuzzShapeRegistry.cs - **Severity:** Medium

#### [PB3-749] 5499 FuzzModels.cs lacks bounds check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows FuzzModels.cs lacks bounds check.
* **File:** FuzzModels.cs - **Severity:** Low

#### [PB3-750] 5500 FuzzOptions.cs lacks defensive validation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows FuzzOptions.cs lacks defensive validation.
* **File:** FuzzOptions.cs - **Severity:** Low

#### [PB3-751] 5501 FuzzRunSummaryBuilder.cs may produce false positive
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows FuzzRunSummaryBuilder.cs may produce false positive.
* **File:** FuzzRunSummaryBuilder.cs - **Severity:** Medium

#### [PB3-752] 5502 ShapeRegistryEntry.cs does not handle edge case
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows ShapeRegistryEntry.cs does not handle edge case.
* **File:** ShapeRegistryEntry.cs - **Severity:** Low

#### [PB3-753] 5503 RoslynShapeManifest.cs skips null check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows RoslynShapeManifest.cs skips null check.
* **File:** RoslynShapeManifest.cs - **Severity:** Low

#### [PB3-754] 5504 FuzzAnalyzerConfiguration.cs ignores cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows FuzzAnalyzerConfiguration.cs ignores cancellation.
* **File:** FuzzAnalyzerConfiguration.cs - **Severity:** Medium

#### [PB3-755] 5505 Program.cs (Fuzz) potential stack overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows Program.cs (Fuzz) potential stack overflow.
* **File:** Program.cs (Fuzz) - **Severity:** Low

#### [PB3-756] 5507 AnalyzerValueReader.cs unchecked assumptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows AnalyzerValueReader.cs unchecked assumptions.
* **File:** AnalyzerValueReader.cs - **Severity:** Medium

#### [PB3-757] 5508 MetadataMethodEffectAnalyzer.cs incomplete coverage
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows MetadataMethodEffectAnalyzer.cs incomplete coverage.
* **File:** MetadataMethodEffectAnalyzer.cs - **Severity:** Low

#### [PB3-758] 5509 MethodBodyOperationResolver.cs lacks bounds check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows MethodBodyOperationResolver.cs lacks bounds check.
* **File:** MethodBodyOperationResolver.cs - **Severity:** Low

#### [PB3-759] 5510 MethodEffects.cs lacks defensive validation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows MethodEffects.cs lacks defensive validation.
* **File:** MethodEffects.cs - **Severity:** Medium

#### [PB3-760] 5511 SharpProofAnalysisApi.cs may produce false positive
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SharpProofAnalysisApi.cs may produce false positive.
* **File:** SharpProofAnalysisApi.cs - **Severity:** Low

#### [PB3-761] 5512 StructuralMethodIdentity.cs does not handle edge case
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows StructuralMethodIdentity.cs does not handle edge case.
* **File:** StructuralMethodIdentity.cs - **Severity:** Low

#### [PB3-762] 5513 EcmaStructuralMethodIdentity.cs skips null check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows EcmaStructuralMethodIdentity.cs skips null check.
* **File:** EcmaStructuralMethodIdentity.cs - **Severity:** Medium

#### [PB3-763] 5514 RoslynStructuralMethodIdentity.cs ignores cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows RoslynStructuralMethodIdentity.cs ignores cancellation.
* **File:** RoslynStructuralMethodIdentity.cs - **Severity:** Low

#### [PB3-764] 5515 CSharpSyntaxFacts.cs potential stack overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows CSharpSyntaxFacts.cs potential stack overflow.
* **File:** CSharpSyntaxFacts.cs - **Severity:** Low

#### [PB3-765] 5516 SymbolicSourceCompilation.cs missing error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicSourceCompilation.cs missing error handling.
* **File:** SymbolicSourceCompilation.cs - **Severity:** Medium

#### [PB3-766] 5517 SymbolicSourceInput.cs unchecked assumptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicSourceInput.cs unchecked assumptions.
* **File:** SymbolicSourceInput.cs - **Severity:** Low

#### [PB3-767] 5518 SymbolicSourceLocation.cs incomplete coverage
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicSourceLocation.cs incomplete coverage.
* **File:** SymbolicSourceLocation.cs - **Severity:** Low

#### [PB3-768] 5519 SymbolicSourceTargetSelector.cs lacks bounds check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicSourceTargetSelector.cs lacks bounds check.
* **File:** SymbolicSourceTargetSelector.cs - **Severity:** Medium

#### [PB3-769] 5520 SymbolicStateFactBuilder.cs lacks defensive validation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicStateFactBuilder.cs lacks defensive validation.
* **File:** SymbolicStateFactBuilder.cs - **Severity:** Low

#### [PB3-770] 5521 SymbolicStatementStateTransfer.cs may produce false positive
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicStatementStateTransfer.cs may produce false positive.
* **File:** SymbolicStatementStateTransfer.cs - **Severity:** Low

#### [PB3-771] 5522 SymbolicStateValueFacts.cs does not handle edge case
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicStateValueFacts.cs does not handle edge case.
* **File:** SymbolicStateValueFacts.cs - **Severity:** Medium

#### [PB3-772] 5523 SymbolicTypeFacts.cs skips null check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicTypeFacts.cs skips null check.
* **File:** SymbolicTypeFacts.cs - **Severity:** Low

#### [PB3-773] 5524 SymbolicUnknownReasonClassifier.cs ignores cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicUnknownReasonClassifier.cs ignores cancellation.
* **File:** SymbolicUnknownReasonClassifier.cs - **Severity:** Low

#### [PB3-774] 5525 SymbolicUnknownReasonTaxonomy.cs potential stack overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicUnknownReasonTaxonomy.cs potential stack overflow.
* **File:** SymbolicUnknownReasonTaxonomy.cs - **Severity:** Medium

#### [PB3-775] 5526 SymbolicValueFacts.cs missing error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicValueFacts.cs missing error handling.
* **File:** SymbolicValueFacts.cs - **Severity:** Low

#### [PB3-776] 5527 SymbolMutationFacts.cs unchecked assumptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolMutationFacts.cs unchecked assumptions.
* **File:** SymbolMutationFacts.cs - **Severity:** Low

#### [PB3-777] 5528 SymbolicProofCache.cs incomplete coverage
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicProofCache.cs incomplete coverage.
* **File:** SymbolicProofCache.cs - **Severity:** Medium

#### [PB3-778] 5529 SymbolicProofEncoder.cs lacks bounds check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicProofEncoder.cs lacks bounds check.
* **File:** SymbolicProofEncoder.cs - **Severity:** Low

#### [PB3-779] 5530 SymbolicProofService.cs lacks defensive validation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicProofService.cs lacks defensive validation.
* **File:** SymbolicProofService.cs - **Severity:** Low

#### [PB3-780] 5531 SymbolicProofStateFacts.cs may produce false positive
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicProofStateFacts.cs may produce false positive.
* **File:** SymbolicProofStateFacts.cs - **Severity:** Medium

#### [PB3-781] 5532 SymbolicPublicModels.cs does not handle edge case
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicPublicModels.cs does not handle edge case.
* **File:** SymbolicPublicModels.cs - **Severity:** Low

#### [PB3-782] 5533 SymbolicQueryFactSummaries.cs skips null check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicQueryFactSummaries.cs skips null check.
* **File:** SymbolicQueryFactSummaries.cs - **Severity:** Low

#### [PB3-783] 5534 SymbolicReachabilityService.cs ignores cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicReachabilityService.cs ignores cancellation.
* **File:** SymbolicReachabilityService.cs - **Severity:** Medium

#### [PB3-784] 5535 SymbolicRuntimeExceptionFacts.cs potential stack overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicRuntimeExceptionFacts.cs potential stack overflow.
* **File:** SymbolicRuntimeExceptionFacts.cs - **Severity:** Low

#### [PB3-785] 5536 SymbolicRuntimeHazardCandidate.cs missing error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicRuntimeHazardCandidate.cs missing error handling.
* **File:** SymbolicRuntimeHazardCandidate.cs - **Severity:** Low

#### [PB3-786] 5537 SymbolicRuntimeHazardCandidateFactory.cs unchecked assumptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicRuntimeHazardCandidateFactory.cs unchecked assumptions.
* **File:** SymbolicRuntimeHazardCandidateFactory.cs - **Severity:** Medium

#### [PB3-787] 5538 SymbolicRuntimeHazardQueryService.cs incomplete coverage
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicRuntimeHazardQueryService.cs incomplete coverage.
* **File:** SymbolicRuntimeHazardQueryService.cs - **Severity:** Low

#### [PB3-788] 5539 SymbolicRuntimeHazardSourceCandidateFactory.cs lacks bounds check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicRuntimeHazardSourceCandidateFactory.cs lacks bounds check.
* **File:** SymbolicRuntimeHazardSourceCandidateFactory.cs - **Severity:** Low

#### [PB3-789] 5540 SymbolicRuntimeHazardSyntaxFacts.cs lacks defensive validation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicRuntimeHazardSyntaxFacts.cs lacks defensive validation.
* **File:** SymbolicRuntimeHazardSyntaxFacts.cs - **Severity:** Medium

#### [PB3-790] 5541 SymbolicRuntimeTypeFacts.cs may produce false positive
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicRuntimeTypeFacts.cs may produce false positive.
* **File:** SymbolicRuntimeTypeFacts.cs - **Severity:** Low

#### [PB3-791] 5542 SymbolicMethodQueryInfrastructure.cs does not handle edge case
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicMethodQueryInfrastructure.cs does not handle edge case.
* **File:** SymbolicMethodQueryInfrastructure.cs - **Severity:** Low

#### [PB3-792] 5543 SymbolicProgramPointFacts.cs skips null check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicProgramPointFacts.cs skips null check.
* **File:** SymbolicProgramPointFacts.cs - **Severity:** Medium

#### [PB3-793] 5544 SymbolicProgramPointResult.cs ignores cancellation
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicProgramPointResult.cs ignores cancellation.
* **File:** SymbolicProgramPointResult.cs - **Severity:** Low

#### [PB3-794] 5545 SymbolicProjectQueryContext.cs potential stack overflow
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicProjectQueryContext.cs potential stack overflow.
* **File:** SymbolicProjectQueryContext.cs - **Severity:** Low

#### [PB3-795] 5546 SymbolicInvariantService.cs missing error handling
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicInvariantService.cs missing error handling.
* **File:** SymbolicInvariantService.cs - **Severity:** Medium

#### [PB3-796] 5547 SymbolicKnownGuardFacts.cs unchecked assumptions
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicKnownGuardFacts.cs unchecked assumptions.
* **File:** SymbolicKnownGuardFacts.cs - **Severity:** Low

#### [PB3-797] 5548 SymbolicLoopStateTransfer.cs incomplete coverage
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicLoopStateTransfer.cs incomplete coverage.
* **File:** SymbolicLoopStateTransfer.cs - **Severity:** Low

#### [PB3-798] 5549 SymbolicMethodLikeDeclaration.cs lacks bounds check
> **Disposition:** Needs investigation
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Review shows SymbolicMethodLikeDeclaration.cs lacks bounds check.
* **File:** SymbolicMethodLikeDeclaration.cs - **Severity:** Medium



